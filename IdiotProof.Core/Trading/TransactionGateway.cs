using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Shared.Risk;

namespace IdiotProof.Core.Trading;

/// <summary>One gate's decision, in the order the gates ran.</summary>
public sealed record GateVerdict(string Gate, bool Passed, string Detail);

public enum GatewayStatus
{
    /// <summary>The broker accepted the order.</summary>
    Placed,
    /// <summary>A gate vetoed it; nothing reached a broker.</summary>
    Blocked,
    /// <summary>Every gate passed but the broker refused it.</summary>
    Rejected,
    /// <summary>Live and not yet confirmed: a code was issued, nothing was placed.</summary>
    ConfirmationRequired,
    /// <summary>A dry run: every gate evaluated, nothing placed, no code issued.</summary>
    Previewed,
}

/// <summary>What happened to one intent, with every gate's verdict for the preview, the audit trail and the user.</summary>
public sealed record GatewayOutcome(
    GatewayStatus Status,
    OrderIntent Intent,
    IReadOnlyList<GateVerdict> Verdicts,
    string RoutedTo,
    string Message)
{
    public OrderResult? Order { get; init; }
    public string? ConfirmationCode { get; init; }
    public DateTime? ConfirmationExpiresUtc { get; init; }
    public decimal? WorstCaseLoss { get; init; }
    public decimal? EstimatedCost { get; init; }

    /// <summary>The block the user sees before saying yes (and that a confirmation stores).</summary>
    public string Preview()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Intent.Describe());
        sb.AppendLine($"  route      {RoutedTo}");
        if (EstimatedCost is { } cost) sb.AppendLine($"  est. cost  {Money(cost)}");
        if (WorstCaseLoss is { } loss) sb.AppendLine($"  worst case {Money(-loss)} if the stop fills");
        if (Intent.StopLossPrice is { } stop) sb.AppendLine($"  stop       {stop.ToString("0.00##", CultureInfo.InvariantCulture)}");
        foreach (var v in Verdicts) sb.AppendLine($"  {(v.Passed ? "✓" : "✗")} {v.Gate,-10} {v.Detail}");
        return sb.ToString().TrimEnd();
    }

    private static string Money(decimal d) =>
        (d < 0 ? "-$" : "$") + Math.Abs(d).ToString("#,0.00", CultureInfo.InvariantCulture);
}

/// <summary>Caller-supplied facts the gateway can't derive itself.</summary>
public sealed record GatewayOptions
{
    /// <summary>"cli", "mcp", "blazor", "monitor" — recorded with the audit row.</summary>
    public string Caller { get; init; } = "cli";

    /// <summary>
    /// The Monitor's LLM voter-panel verdict for an automated entry (IP-LAW-1). The Monitor runs
    /// the panel on the signal and its candles; the gateway refuses an automated entry without an
    /// approving verdict (fail closed). Null for manual orders, which a person decided.
    /// </summary>
    public (bool Approved, string Detail)? LlmVerdict { get; init; }

    /// <summary>Evaluate every gate and stop before placing (and before issuing a code).</summary>
    public bool DryRun { get; init; }

    /// <summary>Set only by <see cref="TransactionGateway.ConfirmAsync"/> after a code was redeemed.</summary>
    internal bool LiveConfirmed { get; init; }
}

/// <summary>
/// The single path every order takes to a broker — Monitor fires, manual trades from the CLI,
/// MCP and web, options tickets and flattens. Gates run in a fixed order and fail closed:
/// validate → route (IP-LAW-3) → synthetic-data guard (IP-LAW-9) → Risk Guardian (IP-LAW-2) →
/// LLM panel for automated entries (IP-LAW-1) → Live confirmation → place → audit.
/// </summary>
public sealed class TransactionGateway(
    IOrderRouting routing,
    IMarketDataFeed feed,
    RiskGuardianService riskGuardians,
    ConfirmationStore confirmations,
    AuditLogRepository audit,
    ILogger<TransactionGateway> logger)
{
    public Task<GatewayOutcome> PreviewAsync(OrderIntent intent, GatewayOptions? options = null, CancellationToken ct = default) =>
        SubmitAsync(intent, (options ?? new GatewayOptions()) with { DryRun = true }, ct);

    public async Task<GatewayOutcome> SubmitAsync(OrderIntent intent, GatewayOptions? options = null, CancellationToken ct = default)
    {
        options ??= new GatewayOptions();
        var verdicts = new List<GateVerdict>();
        var routedTo = "(not routed)";

        GatewayOutcome Stop(GatewayStatus status, string message) =>
            new(status, intent, verdicts, routedTo, message);

        // 1 ── the order makes sense on its own
        var invalid = Validate(intent);
        verdicts.Add(new("order", invalid is null, invalid ?? "well-formed"));
        if (invalid is not null) return await AuditAsync(Stop(GatewayStatus.Blocked, invalid), options, ct);

        // 2 ── route: Sandbox is always the simulator; Paper/Live go to the owner's own account
        //      only with opt-in + keys, else fall back to Sandbox (IP-LAW-3) — but never pretend
        //      a Live order went live.
        IBrokerClient client;
        try
        {
            client = await routing.ResolveAsync(intent.OwnerUserId, intent.Mode, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            verdicts.Add(new("route", false, "No broker could be resolved: " + ex.Message));
            return await AuditAsync(Stop(GatewayStatus.Blocked, "No broker available."), options, ct);
        }
        routedTo = Describe(client);
        var routedLive = client.BrokerType == BrokerType.Alpaca && !client.IsPaper;
        if (intent.Mode == BrokerMode.Live && !routedLive)
        {
            verdicts.Add(new("route", false, $"Live requested but it resolves to {routedTo}: opt in to Alpaca and add live keys (`idiotproof keys`)."));
            return await AuditAsync(Stop(GatewayStatus.Blocked, "Live trading is not set up for this user."), options, ct);
        }
        verdicts.Add(new("route", true, intent.Mode == BrokerMode.Paper && client.BrokerType == BrokerType.Sandbox
            ? "Paper requested; no paper keys/opt-in, so it runs in the Sandbox simulator"
            : routedTo));

        // 3 ── synthetic data never drives a real entry (IP-LAW-9)
        if (feed is MockDataFeed && client.BrokerType != BrokerType.Sandbox && intent.Purpose == OrderPurpose.Entry)
        {
            verdicts.Add(new("data", false, "Market data is the Mock feed; real accounts only take exits while it is."));
            return await AuditAsync(Stop(GatewayStatus.Blocked, "Mock market data can't drive a real entry (IP-LAW-9)."), options, ct);
        }

        // 4 ── Risk Guardian: entries need a stop on the right side and risk inside the limits.
        //      Exits reduce risk and always pass. Manual options tickets are outside the automated
        //      gates (IP-LAW-1); they are governed by the options level and the Live confirmation.
        decimal? worstCase = null;
        if (intent.Purpose == OrderPurpose.Exit)
        {
            verdicts.Add(new("risk", true, "exit — risk-reducing, always allowed"));
        }
        else if (intent.Origin == OrderOrigin.Options)
        {
            verdicts.Add(new("risk", true, "manual options ticket — outside the automated gates (IP-LAW-1)"));
        }
        else
        {
            var guardian = await riskGuardians.GetForUserAsync(intent.OwnerUserId, ct);
            var setup = ToSetup(intent);
            if (setup is null)
            {
                verdicts.Add(new("risk", false, "An entry needs a protective stop (--stop) and a price to size against (--limit, or a quote)."));
                return await AuditAsync(Stop(GatewayStatus.Blocked, "No stop loss — the Risk Guardian refuses entries without one."), options, ct);
            }
            var risk = guardian.ValidateTrade(setup);
            worstCase = risk.WorstCaseLoss;
            if (!risk.IsApproved)
            {
                verdicts.Add(new("risk", false, string.Join("; ", risk.BlockReasons)));
                return await AuditAsync(Stop(GatewayStatus.Blocked, "Risk Guardian veto: " + string.Join("; ", risk.BlockReasons)) with { WorstCaseLoss = worstCase }, options, ct);
            }
            verdicts.Add(new("risk", true, risk.Warnings.Count == 0 ? "within limits" : "within limits; " + string.Join("; ", risk.Warnings)));
        }

        // 5 ── the LLM voter panel decides automated entries only; a person decided a manual one
        if (intent.Origin == OrderOrigin.Monitor && intent.Purpose == OrderPurpose.Entry)
        {
            if (options.LlmVerdict is not { } llm || !llm.Approved)
            {
                verdicts.Add(new("llm", false, options.LlmVerdict?.Detail ?? "no voter-panel verdict supplied (fail closed)"));
                return await AuditAsync(Stop(GatewayStatus.Blocked, "The LLM voter panel did not approve."), options, ct);
            }
            verdicts.Add(new("llm", true, llm.Detail));
        }

        var estimate = EstimatedCost(intent);
        var cleared = Stop(GatewayStatus.Previewed, "Every gate passed.") with { WorstCaseLoss = worstCase, EstimatedCost = estimate };

        if (options.DryRun) return cleared;

        // 6 ── real money from a person waits for their explicit yes (preview → code → confirm)
        if (routedLive && intent.Origin != OrderOrigin.Monitor && !options.LiveConfirmed)
        {
            var pending = await confirmations.IssueAsync(intent.OwnerUserId, ConfirmationStore.KindOrder,
                intent.ToJson(), intent.Hash(), cleared.Preview(), ct);
            return await AuditAsync(cleared with
            {
                Status = GatewayStatus.ConfirmationRequired,
                Message = $"Live order needs confirmation: code {pending.Code} (valid {ConfirmationStore.Lifetime.TotalSeconds:0}s).",
                ConfirmationCode = pending.Code,
                ConfirmationExpiresUtc = pending.ExpiresUtc,
            }, options, ct);
        }

        // 7 ── place
        OrderResult placed;
        try
        {
            placed = await client.PlaceOrderAsync(intent.Order, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Broker threw placing {Order}", intent.Describe());
            placed = new OrderResult { IsSuccess = false, Message = ex.Message };
        }
        var outcome = cleared with
        {
            Status = placed.IsSuccess ? GatewayStatus.Placed : GatewayStatus.Rejected,
            Message = placed.IsSuccess ? $"Placed on {routedTo}: order {placed.BrokerOrderId}." : $"{routedTo} refused the order: {placed.Message}",
            Order = placed,
        };
        return await AuditAsync(outcome, options, ct);
    }

    /// <summary>
    /// Redeems a Live code and runs the exact stored intent through every gate again. When the
    /// caller re-sent the order (the MCP "same arguments plus the code" flow), pass its hash as
    /// <paramref name="expectedHash"/>: a different order is refused as tampered and the code is
    /// not used up.
    /// </summary>
    public async Task<GatewayOutcome?> ConfirmAsync(
        string code, Guid ownerUserId, string caller, string? expectedHash = null, CancellationToken ct = default)
    {
        var redemption = await confirmations.RedeemAsync(code, ownerUserId, caller, p =>
        {
            var stored = p.Kind == ConfirmationStore.KindOrder ? OrderIntent.FromJson(p.PayloadJson).Hash() : "";
            return expectedHash is not null && !string.Equals(expectedHash, stored, StringComparison.OrdinalIgnoreCase)
                ? expectedHash
                : stored;
        }, ct);
        if (!redemption.Succeeded)
            throw new ConfirmationRefusedException(redemption.Message, redemption.Failure!.Value);
        if (redemption.Confirmation!.Kind != ConfirmationStore.KindOrder)
            return null;

        var intent = OrderIntent.FromJson(redemption.Confirmation.PayloadJson);
        return await SubmitAsync(intent, new GatewayOptions { Caller = caller, LiveConfirmed = true }, ct);
    }

    internal static string? Validate(OrderIntent intent)
    {
        var o = intent.Order;
        if (intent.OwnerUserId == Guid.Empty) return "No owner for this order.";
        if (string.IsNullOrWhiteSpace(o.Symbol)) return "No symbol.";
        if (o.Quantity < 0) return "Quantity can't be negative.";
        if (o.Quantity == 0 && o.Notional is not > 0) return "Size the order with a share count or a positive dollar amount.";
        if (o.Quantity > 0 && o.Notional is not null) return "Size by shares or by dollars, not both.";
        if (o.Type is OrderType.Limit or OrderType.StopLimit && o.LimitPrice is not > 0) return "A limit order needs a positive limit price.";
        if (o.Type is OrderType.Stop or OrderType.StopLimit && o.StopPrice is not > 0) return "A stop order needs a positive stop price.";
        if (o.Type == OrderType.TrailingStop && o.TrailPercent is not > 0) return "A trailing stop needs a trail percent.";
        if (o.ExtendedHours && (o.Type != OrderType.Limit || !o.TimeInForce.Equals("DAY", StringComparison.OrdinalIgnoreCase)))
            return "Extended-hours orders must be DAY limit orders (Alpaca's rule).";
        if (o.AssetClass == AssetClass.Option && (o.Notional is not null || o.ExtendedHours || o.Option is null))
            return "Options trade whole contracts, regular hours, with the contract attached.";
        return null;
    }

    /// <summary>The Risk Guardian's view of an entry; null when it has no stop or no price to size against.</summary>
    internal static TradeSetup? ToSetup(OrderIntent intent)
    {
        var o = intent.Order;
        var price = o.LimitPrice ?? intent.ReferencePrice;
        if (intent.StopLossPrice is not { } stop || price is not > 0) return null;
        var qty = o.Quantity > 0 ? o.Quantity : (int)Math.Floor((o.Notional ?? 0) / price.Value);
        var isLong = o.Side == OrderSide.Buy;
        return new TradeSetup
        {
            Symbol = o.Symbol.ToUpperInvariant(),
            Direction = isLong ? TradeDirection.Long : TradeDirection.Short,
            EntryPrice = price.Value,
            EntryType = o.Type,
            StopLoss = stop,
            TakeProfit = intent.TakeProfitPrice ?? 0m,
            Quantity = Math.Max(qty, 1),
            RiskDollars = Math.Abs(price.Value - stop) * Math.Max(qty, 1),
            RewardDollars = intent.TakeProfitPrice is { } tp ? Math.Abs(tp - price.Value) * Math.Max(qty, 1) : 0m,
            Rationale = intent.Note ?? "",
        };
    }

    private static decimal? EstimatedCost(OrderIntent intent)
    {
        var o = intent.Order;
        if (o.Notional is { } n) return n;
        var price = o.LimitPrice ?? intent.ReferencePrice;
        if (price is null) return null;
        var multiplier = o.AssetClass == AssetClass.Option ? (o.Option?.Multiplier ?? 100) : 1;
        return price.Value * o.Quantity * multiplier;
    }

    private static string Describe(IBrokerClient client) => client.BrokerType switch
    {
        BrokerType.Sandbox => "Sandbox (simulated)",
        BrokerType.Alpaca when client.IsPaper => "Alpaca Paper",
        BrokerType.Alpaca => "Alpaca LIVE",
        var other => other.ToString(),
    };

    /// <summary>Every outcome — placed, refused, vetoed, awaiting confirmation — leaves an audit row.</summary>
    private async Task<GatewayOutcome> AuditAsync(GatewayOutcome outcome, GatewayOptions options, CancellationToken ct)
    {
        if (outcome.Status == GatewayStatus.Previewed) return outcome;
        try
        {
            var data = JsonSerializer.Serialize(new
            {
                status = outcome.Status.ToString(),
                caller = options.Caller,
                intent = JsonDocument.Parse(outcome.Intent.ToJson()).RootElement,
                routed_to = outcome.RoutedTo,
                verdicts = outcome.Verdicts,
                broker_order_id = outcome.Order?.BrokerOrderId,
                broker_message = outcome.Order?.Message,
                code_issued = outcome.ConfirmationCode is not null,
            });
            var category = outcome.Status is GatewayStatus.Placed or GatewayStatus.Rejected ? "order" : "risk";
            await audit.LogAsync(category, $"{outcome.Status}: {outcome.Intent.Describe()} — {outcome.Message}", outcome.Intent.OwnerUserId, data, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Audit write failed for {Order} (continuing).", outcome.Intent.Describe());
        }
        return outcome;
    }
}

/// <summary>Picks the broker client for an owner and mode (the gateway's only route to a broker).</summary>
public interface IOrderRouting
{
    Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct);
}

/// <summary>
/// Sandbox is always the simulator (never the global default, which an env knob can point at a
/// real account); Paper/Live go through <see cref="UserBrokerResolver"/>'s opt-in + keys rule.
/// </summary>
public sealed class OrderRouting(BrokerRouter router, UserBrokerResolver resolver) : IOrderRouting
{
    public async Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) =>
        mode == BrokerMode.Sandbox
            ? router.GetBroker(BrokerType.Sandbox)
            : await resolver.ResolveAsync(ownerUserId, mode.ToString(), ct);
}

/// <summary>A confirmation code was refused; the message tells the user why and what to do.</summary>
public sealed class ConfirmationRefusedException(string message, ConfirmationFailure failure) : Exception(message)
{
    public ConfirmationFailure Failure { get; } = failure;
}
