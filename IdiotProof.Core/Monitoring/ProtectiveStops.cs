using System.Collections.Concurrent;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring;

/// <summary>
/// Broker-side protective stops for Monitor-held positions (IP-T360). The strategy's own stop lives
/// in the Monitor's tick loop, so while the Monitor or its database is down a position would have no
/// stop at all. This keeps ONE resting GTC stop at the broker per held strategy, set
/// <see cref="BackstopBuffer"/> beyond the strategy's stop so the Monitor still exits first while it
/// runs. Each is tagged with a client order id that names its strategy, so it can be found again
/// after a restart.
/// Alpaca stops trigger in the regular session only: in premarket/after-hours the Monitor is the
/// only stop, and a gap through the stop fills at the open.
/// </summary>
public static class ProtectiveStops
{
    /// <summary>How far beyond the strategy's own stop the broker stop rests (0.5%).</summary>
    public const decimal BackstopBuffer = 0.005m;

    private const string Prefix = "ips-";

    public static string ClientIdPrefix(Guid strategyId) => Prefix + strategyId.ToString("N");

    public static string NewClientId(Guid strategyId, DateTime utc) => $"{ClientIdPrefix(strategyId)}-{utc:yyyyMMddHHmmssfff}";

    /// <summary>A protective stop the Monitor placed, for any strategy.</summary>
    public static bool IsProtectiveStop(BrokerOrder order) =>
        order.ClientOrderId?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static bool Belongs(BrokerOrder order, Guid strategyId) =>
        order.ClientOrderId?.StartsWith(ClientIdPrefix(strategyId), StringComparison.Ordinal) == true;

    /// <summary>
    /// The broker stop for a position entered at <paramref name="entry"/>: the strategy's hard stop
    /// (price, else percent off entry) moved <see cref="BackstopBuffer"/> further away and rounded
    /// away from the market. Null when the strategy has no hard stop or it sits on the wrong side.
    /// </summary>
    public static decimal? BackstopPrice(StrategyDefinition def, decimal entry)
    {
        var isShort = def.Direction == TradeDirection.Short;
        decimal? stop = def.StopLossPrice is { } sp
            ? (decimal)sp
            : def.StopLossPercent is { } pct && entry > 0m
                ? entry * (isShort ? 1m + (decimal)pct / 100m : 1m - (decimal)pct / 100m)
                : null;
        if (stop is not { } s || s <= 0m) return null;
        if (entry > 0m && (isShort ? s <= entry : s >= entry)) return null;

        var widened = isShort ? s * (1m + BackstopBuffer) : s * (1m - BackstopBuffer);
        // Alpaca: whole cents at $1 and above, up to 4 decimals below.
        var scale = widened >= 1m ? 100m : 10000m;
        var rounded = isShort ? Math.Ceiling(widened * scale) / scale : Math.Floor(widened * scale) / scale;
        return rounded > 0m ? rounded : null;
    }

    /// <summary>The resting stop: sell-to-close a long / buy-to-close a short, GTC, regular session.</summary>
    public static OrderIntent Place(Guid owner, Guid strategyId, string? brokerMode, string symbol, TradeDirection direction,
        int quantity, decimal stopPrice, string? note) => new()
    {
        OwnerUserId = owner,
        Mode = OrderIntent.ParseMode(brokerMode),
        Purpose = OrderPurpose.Exit,
        Origin = OrderOrigin.Monitor,
        StrategyId = strategyId,
        Order = new OrderRequest
        {
            Symbol = symbol.Trim().ToUpperInvariant(),
            Quantity = quantity,
            Side = DirectionalOrders.ExitSide(direction),
            Type = OrderType.Stop,
            StopPrice = stopPrice,
            TimeInForce = "GTC",
            ExtendedHours = false,
            PositionIntent = direction == TradeDirection.Short ? "buy_to_close" : "sell_to_close",
            ClientOrderId = NewClientId(strategyId, DateTime.UtcNow),
        },
        ReferencePrice = stopPrice,
        Note = note,
    };

    /// <summary>Resizes / re-prices the resting stop in place (stays GTC).</summary>
    public static OrderIntent Replace(Guid owner, Guid strategyId, string? brokerMode, string symbol, string orderId,
        int quantity, decimal stopPrice, decimal previousQuantity, string? note) => new()
    {
        OwnerUserId = owner,
        Mode = OrderIntent.ParseMode(brokerMode),
        Purpose = OrderPurpose.Exit,
        Origin = OrderOrigin.Monitor,
        Action = OrderAction.Replace,
        TargetOrderId = orderId,
        ReplacedQuantity = previousQuantity,
        StrategyId = strategyId,
        Order = new OrderRequest { Symbol = symbol.Trim().ToUpperInvariant(), Quantity = quantity, StopPrice = stopPrice, TimeInForce = "GTC" },
        Note = note,
    };

    public static OrderIntent Cancel(Guid owner, Guid? strategyId, BrokerMode mode, OrderOrigin origin, string symbol, string orderId, string? note) => new()
    {
        OwnerUserId = owner,
        Mode = mode,
        Purpose = OrderPurpose.Exit,
        Origin = origin,
        Action = OrderAction.Cancel,
        TargetOrderId = orderId,
        StrategyId = strategyId,
        Order = new OrderRequest { Symbol = symbol.Trim().ToUpperInvariant() },
        Note = note,
    };
}

/// <summary>One Monitor-held position the keeper protects.</summary>
public sealed record ProtectedHolding(
    Guid Owner, Guid StrategyId, string? BrokerMode, string Symbol, string Title, TradeDirection Direction,
    int Quantity, decimal Entry, DateTime EntryFilledUtc, StrategyDefinition Definition);

/// <summary>What withdrawing the stops before a Monitor exit found.</summary>
/// <param name="Clear">False when a stop is still working at the broker: the exit must wait a tick.</param>
/// <param name="FilledStop">A stop that already closed the position: book it, don't sell again.</param>
public sealed record StopWithdrawal(bool Clear, BrokerOrder? FilledStop);

/// <summary>
/// Keeps the broker-side stops (<see cref="ProtectiveStops"/>) in line with the Monitor's positions,
/// every order through the <see cref="TransactionGateway"/>. Brokers without order queries (the
/// Sandbox simulator) are left to the Monitor's own stops.
/// </summary>
public sealed class ProtectiveStopKeeper(TransactionGateway gateway, IOrderRouting routing, ILogger logger,
    Func<CancellationToken, Task<bool>>? leaderCheck = null)
{
    private static readonly TimeSpan EnsureInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CancelSettle = TimeSpan.FromMilliseconds(300);
    private const int CancelPolls = 6;

    private readonly ConcurrentDictionary<Guid, DateTime> lastEnsureUtc = new();

    private GatewayOptions Options => new() { Caller = "monitor", LeaderCheck = leaderCheck };

    /// <summary>
    /// Makes sure <paramref name="h"/> has exactly one stop at the broker, sized to the shares the
    /// broker holds and priced at <see cref="ProtectiveStops.BackstopPrice"/>; at most every 30s per
    /// strategy. Returns a filled stop when the broker already stopped the position out (the caller
    /// books that exit), else null. Never throws.
    /// </summary>
    public async Task<BrokerOrder?> EnsureAsync(ProtectedHolding h, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (lastEnsureUtc.TryGetValue(h.StrategyId, out var last) && now - last < EnsureInterval) return null;
        lastEnsureUtc[h.StrategyId] = now;
        try
        {
            var mode = OrderIntent.ParseMode(h.BrokerMode);
            var broker = await routing.ResolveAsync(h.Owner, mode, ct);
            if (!broker.SupportsOrderQueries) return null;

            var open = (await broker.GetOrdersAsync(h.Symbol, openOnly: true, ct: ct))
                .Where(o => ProtectiveStops.Belongs(o, h.StrategyId)).ToList();
            if (open.Count == 0 && await FindFilledAsync(broker, h, ct) is { } filled) return filled;

            var positions = await broker.GetPositionsAsync(ct);
            var held = DirectionalOrders.HeldShares(
                positions.FirstOrDefault(p => p.Symbol.Equals(h.Symbol, StringComparison.OrdinalIgnoreCase)), h.Direction);
            var quantity = Math.Min(h.Quantity, held);
            if (quantity <= 0)
            {
                // No shares yet (entry still working) or gone some other way: a stop now would only be
                // refused, or rest against a position that no longer exists.
                foreach (var stray in open) await CancelAsync(h, stray, "no shares held", ct);
                return null;
            }

            if (ProtectiveStops.BackstopPrice(h.Definition, h.Entry) is not { } stopPrice)
            {
                logger.LogWarning("[{Title}] {Symbol} has no hard stop to mirror at the broker; only the Monitor protects it.", h.Title, h.Symbol);
                return null;
            }

            foreach (var extra in open.Skip(1)) await CancelAsync(h, extra, "duplicate protective stop", ct);
            var current = open.FirstOrDefault();
            if (current is null)
            {
                var placed = await gateway.SubmitAsync(ProtectiveStops.Place(h.Owner, h.StrategyId, h.BrokerMode, h.Symbol, h.Direction,
                    quantity, stopPrice, $"[{h.Title}] broker-side protective stop"), Options, ct);
                if (placed.Status == GatewayStatus.Placed)
                    logger.LogInformation("[{Title}] protective stop {Qty} {Symbol} @ {Stop} placed at the broker (order {Id}).",
                        h.Title, quantity, h.Symbol, stopPrice, placed.Order?.BrokerOrderId);
                else
                    logger.LogWarning("[{Title}] protective stop for {Symbol} not placed ({Status}): {Why}",
                        h.Title, h.Symbol, placed.Status, MonitorOrders.WhyNot(placed));
                return null;
            }

            if (current.FilledQuantity > 0m) return null; // the stop is executing; leave it alone
            if (current.Quantity != quantity || current.StopPrice != stopPrice)
            {
                var replaced = await gateway.SubmitAsync(ProtectiveStops.Replace(h.Owner, h.StrategyId, h.BrokerMode, h.Symbol, current.Id,
                    quantity, stopPrice, current.Quantity, $"[{h.Title}] protective stop resized"), Options, ct);
                if (replaced.Status != GatewayStatus.Placed)
                    logger.LogWarning("[{Title}] protective stop {Id} for {Symbol} not resized ({Status}): {Why}",
                        h.Title, current.Id, h.Symbol, replaced.Status, MonitorOrders.WhyNot(replaced));
            }
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Title}] protective-stop check for {Symbol} failed; retrying in {Secs:F0}s.", h.Title, h.Symbol, EnsureInterval.TotalSeconds);
            return null;
        }
    }

    /// <summary>
    /// Cancels this strategy's stops ahead of a Monitor exit and waits for the broker to release the
    /// shares. Not clear (wait a tick) when a stop is still working afterwards, so a stop and the
    /// exit can never both close the position. A broker that can't be asked passes as clear: an exit
    /// is never skipped because a status call failed.
    /// </summary>
    public async Task<StopWithdrawal> WithdrawAsync(ProtectedHolding h, CancellationToken ct)
    {
        lastEnsureUtc.TryRemove(h.StrategyId, out _); // re-protect the next holding tick if the exit doesn't go out
        try
        {
            var broker = await routing.ResolveAsync(h.Owner, OrderIntent.ParseMode(h.BrokerMode), ct);
            if (!broker.SupportsOrderQueries) return new(true, null);

            List<BrokerOrder> Mine(IReadOnlyList<BrokerOrder> orders) => orders.Where(o => ProtectiveStops.Belongs(o, h.StrategyId)).ToList();
            var open = Mine(await broker.GetOrdersAsync(h.Symbol, openOnly: true, ct: ct));
            foreach (var o in open) await CancelAsync(h, o, "Monitor exit", ct);
            for (var i = 0; i < CancelPolls && open.Count > 0; i++)
            {
                await Task.Delay(CancelSettle, ct);
                open = Mine(await broker.GetOrdersAsync(h.Symbol, openOnly: true, ct: ct));
            }
            if (open.Count > 0)
            {
                logger.LogWarning("[{Title}] {Symbol} protective stop still working after cancel; exit waits a tick.", h.Title, h.Symbol);
                return new(false, null);
            }
            return new(true, await FindFilledAsync(broker, h, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Title}] could not withdraw the protective stop for {Symbol}; exiting anyway.", h.Title, h.Symbol);
            return new(true, null);
        }
    }

    /// <summary>Forgets a strategy once its position is closed.</summary>
    public void Forget(Guid strategyId) => lastEnsureUtc.TryRemove(strategyId, out _);

    private async Task<BrokerOrder?> FindFilledAsync(IdiotProof.Brokers.IBrokerClient broker, ProtectedHolding h, CancellationToken ct)
    {
        var since = (await broker.GetOrdersAsync(h.Symbol, openOnly: false, afterUtc: h.EntryFilledUtc.AddMinutes(-1), ct: ct))
            .Where(o => ProtectiveStops.Belongs(o, h.StrategyId)).ToList();
        foreach (var partial in since.Where(o => !o.IsFilled && !o.IsOpen && o.FilledQuantity > 0m))
            logger.LogWarning("[{Title}] protective stop {Id} for {Symbol} part-filled {Qty} before it ended ({Status}); check the position.",
                h.Title, partial.Id, h.Symbol, partial.FilledQuantity, partial.Status);
        return since.Where(o => o.IsFilled && o.FilledQuantity > 0m).OrderByDescending(o => o.FilledUtc).FirstOrDefault();
    }

    private async Task CancelAsync(ProtectedHolding h, BrokerOrder order, string why, CancellationToken ct)
    {
        var outcome = await gateway.SubmitAsync(ProtectiveStops.Cancel(h.Owner, h.StrategyId, OrderIntent.ParseMode(h.BrokerMode),
            OrderOrigin.Monitor, h.Symbol, order.Id, $"[{h.Title}] protective stop cancelled ({why})"), Options, ct);
        if (outcome.Status != GatewayStatus.Placed)
            logger.LogWarning("[{Title}] cancelling protective stop {Id} for {Symbol} failed ({Status}): {Why}",
                h.Title, order.Id, h.Symbol, outcome.Status, MonitorOrders.WhyNot(outcome));
    }
}
