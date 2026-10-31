using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Data;
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

        // 3-5 for a replace / cancel: no new order is opened, so the data, risk and LLM gates
        //      have nothing to size; the route, the audit trail and the Live code still apply.
        //      The exception is a replace that RAISES an entry's size: that is new risk, so it
        //      goes through the data guard and the Risk Guardian like a fresh entry, sized on the
        //      whole replacement order.
        if (intent.Action != OrderAction.Place)
        {
            if (RaisesEntrySize(intent))
            {
                var raised = await GateEntryRiskAsync(intent, client, verdicts, ct);
                if (raised.Blocked is { } veto) return await AuditAsync(Stop(GatewayStatus.Blocked, veto) with { WorstCaseLoss = raised.WorstCase }, options, ct);
                return await FinishAsync(intent, options, verdicts, routedTo, routedLive, client, raised.WorstCase, ct);
            }
            verdicts.Add(new("risk", true, intent.Action switch
            {
                OrderAction.Replace => "replace — does not raise the order's size; Live still needs a code",
                OrderAction.Cancel => "cancel — withdraws a working order",
                _ => "cancel all — withdraws working orders",
            }));
            return await FinishAsync(intent, options, verdicts, routedTo, routedLive, client, null, ct);
        }

        // 3 ── synthetic data never drives a real entry (IP-LAW-9)
        // 4 ── Risk Guardian: entries need a stop on the right side and risk inside the limits.
        //      Exits reduce risk and always pass. Manual options tickets are outside the automated
        //      gates (IP-LAW-1); they are governed by the options level and the Live confirmation.
        decimal? worstCase = null;
        if (intent.Purpose == OrderPurpose.Exit)
        {
            verdicts.Add(new("risk", true, "exit — risk-reducing, always allowed"));
        }
        else
        {
            var gated = await GateEntryRiskAsync(intent, client, verdicts, ct);
            worstCase = gated.WorstCase;
            if (gated.Blocked is { } veto) return await AuditAsync(Stop(GatewayStatus.Blocked, veto) with { WorstCaseLoss = worstCase }, options, ct);
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

        return await FinishAsync(intent, options, verdicts, routedTo, routedLive, client, worstCase, ct);
    }

    /// <summary>Every gate passed: preview, or issue the Live code, or execute the action at the broker; then audit.</summary>
    private async Task<GatewayOutcome> FinishAsync(OrderIntent intent, GatewayOptions options, List<GateVerdict> verdicts,
        string routedTo, bool routedLive, IBrokerClient client, decimal? worstCase, CancellationToken ct)
    {
        var estimate = intent.Action == OrderAction.Place ? EstimatedCost(intent) : null;
        var cleared = new GatewayOutcome(GatewayStatus.Previewed, intent, verdicts, routedTo, "Every gate passed.")
        {
            WorstCaseLoss = worstCase,
            EstimatedCost = estimate,
        };

        if (options.DryRun) return cleared;

        // 6 ── real money from a person waits for their explicit yes (preview → code → confirm)
        if (routedLive && intent.Origin != OrderOrigin.Monitor && !options.LiveConfirmed)
        {
            var pending = await confirmations.IssueAsync(intent.OwnerUserId, ConfirmationStore.KindOrder,
                intent.ToJson(), intent.Hash(), cleared.Preview(), ct);
            return await AuditAsync(cleared with
            {
                Status = GatewayStatus.ConfirmationRequired,
                Message = $"Live {ActionNoun(intent.Action)} needs confirmation: code {pending.Code} (valid {ConfirmationStore.Lifetime.TotalSeconds:0}s).",
                ConfirmationCode = pending.Code,
                ConfirmationExpiresUtc = pending.ExpiresUtc,
            }, options, ct);
        }

        // 7 ── place / replace / cancel
        OrderResult placed;
        try
        {
            var symbol = string.IsNullOrWhiteSpace(intent.Order.Symbol) || intent.Order.Symbol == "*" ? null : intent.Order.Symbol.Trim().ToUpperInvariant();
            placed = intent.Action switch
            {
                OrderAction.Replace => await client.ReplaceOrderAsync(intent.TargetOrderId!, intent.Order, ct),
                OrderAction.Cancel => await client.CancelOrderAsync(intent.TargetOrderId!, ct),
                OrderAction.CancelAll => await client.CancelAllOrdersAsync(symbol, ct),
                _ => await client.PlaceOrderAsync(intent.Order, ct),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Broker threw on {Order}", intent.Describe());
            placed = new OrderResult { IsSuccess = false, Message = ex.Message };
        }
        var done = intent.Action switch
        {
            OrderAction.Replace => $"Replaced on {routedTo}: new order {placed.BrokerOrderId} (was {intent.TargetOrderId}).",
            OrderAction.Cancel => $"Cancel accepted on {routedTo}: order {intent.TargetOrderId}.",
            OrderAction.CancelAll => $"Cancel-all accepted on {routedTo}: {placed.Message}",
            _ => $"Placed on {routedTo}: order {placed.BrokerOrderId}.",
        };
        var outcome = cleared with
        {
            Status = placed.IsSuccess ? GatewayStatus.Placed : GatewayStatus.Rejected,
            Message = placed.IsSuccess ? done : $"{routedTo} refused the {ActionNoun(intent.Action)}: {placed.Message}",
            Order = placed,
        };
        return await AuditAsync(outcome, options, ct);
    }

    /// <summary>
    /// The data guard (IP-LAW-9) and the Risk Guardian (IP-LAW-2) for anything that adds risk: a
    /// new entry, or a replace that raises an entry's size. Adds its verdicts; returns the veto
    /// message when one gate refused (null when both passed) and the worst-case loss when sized.
    /// </summary>
    private async Task<(string? Blocked, decimal? WorstCase)> GateEntryRiskAsync(
        OrderIntent intent, IBrokerClient client, List<GateVerdict> verdicts, CancellationToken ct)
    {
        if (IsSyntheticFeed(feed) && client.BrokerType != BrokerType.Sandbox)
        {
            verdicts.Add(new("data", false, "Market data is the Mock feed; real accounts only take exits while it is."));
            return ("Mock market data can't drive a real entry (IP-LAW-9).", null);
        }

        if (intent.Origin == OrderOrigin.Options && intent.Action == OrderAction.Place)
        {
            verdicts.Add(new("risk", true, "manual options ticket — outside the automated gates (IP-LAW-1)"));
            return (null, null);
        }

        var guardian = await riskGuardians.GetForUserAsync(intent.OwnerUserId, ct);
        var setup = ToSetup(intent);
        if (setup is null)
        {
            if (intent.Action == OrderAction.Replace)
            {
                verdicts.Add(new("risk", false, "Raising a working entry's size is new risk: give its protective stop (--stop) and a price to size against."));
                return ("No stop loss — the Risk Guardian refuses to raise an entry's size without one.", null);
            }
            verdicts.Add(new("risk", false, "An entry needs a protective stop (--stop) and a price to size against (--limit, or a quote)."));
            return ("No stop loss — the Risk Guardian refuses entries without one.", null);
        }
        var risk = guardian.ValidateTrade(setup);
        if (!risk.IsApproved)
        {
            verdicts.Add(new("risk", false, string.Join("; ", risk.BlockReasons)));
            return ("Risk Guardian veto: " + string.Join("; ", risk.BlockReasons), risk.WorstCaseLoss);
        }
        var what = intent.Action == OrderAction.Replace
            ? $"replace raises the size {(intent.ReplacedQuantity is { } was ? was.ToString("0.########", CultureInfo.InvariantCulture) : "?")} → {intent.Order.SizeQuantity.ToString("0.########", CultureInfo.InvariantCulture)}; "
            : "";
        verdicts.Add(new("risk", true, what + (risk.Warnings.Count == 0 ? "within limits" : "within limits; " + string.Join("; ", risk.Warnings))));
        return (null, risk.WorstCaseLoss);
    }

    /// <summary>
    /// True when a replace makes an ENTRY bigger: a new quantity above the working order's
    /// (<see cref="OrderIntent.ReplacedQuantity"/>), or any new quantity when that is unknown.
    /// Exits only reduce risk; price/TIF-only replaces leave the size alone.
    /// </summary>
    internal static bool RaisesEntrySize(OrderIntent intent) =>
        intent.Action == OrderAction.Replace
        && intent.Purpose == OrderPurpose.Entry
        && intent.Order.SizeQuantity > 0
        && (intent.ReplacedQuantity is not { } was || intent.Order.SizeQuantity > was);

    /// <summary>The Mock feed, by type or by name (a wrapper reports the feed it serves).</summary>
    internal static bool IsSyntheticFeed(IMarketDataFeed feed) =>
        feed is MockDataFeed || string.Equals(feed.FeedName, "Mock", StringComparison.OrdinalIgnoreCase);

    private static string ActionNoun(OrderAction action) => action switch
    {
        OrderAction.Replace => "replace",
        OrderAction.Cancel => "cancel",
        OrderAction.CancelAll => "cancel-all",
        _ => "order",
    };

    private static readonly HashSet<string> TimesInForce = new(StringComparer.OrdinalIgnoreCase) { "DAY", "GTC", "OPG", "CLS", "IOC", "FOK" };

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

    /// <summary>
    /// Several Live actions behind ONE code (close-all: cancel every open order, then exit every
    /// position). Every intent is dry-run through the gates first; only when all of them pass is a
    /// code issued, bound to <paramref name="what"/> and the exact set of intent hashes. Returns
    /// the dry-run outcomes and the pending code (null when a gate refused one of them).
    /// </summary>
    public async Task<(IReadOnlyList<GatewayOutcome> Outcomes, PendingConfirmation? Pending)> RequestBatchConfirmationAsync(
        IReadOnlyList<OrderIntent> intents, string what, string preview, string caller, CancellationToken ct = default)
    {
        var owner = BatchOwner(intents);
        var dry = new List<GatewayOutcome>();
        foreach (var i in intents) dry.Add(await PreviewAsync(i, new GatewayOptions { Caller = caller }, ct));
        if (dry.Any(o => o.Status != GatewayStatus.Previewed)) return (dry, null);

        var (payload, hash) = BatchPayload(what, intents);
        var pending = await confirmations.IssueAsync(owner, ConfirmationStore.KindOrderBatch, payload, hash, preview, ct);
        try
        {
            await audit.LogAsync("risk", $"ConfirmationRequired: {what} — {intents.Count} actions behind code {pending.Code}", owner,
                JsonSerializer.Serialize(new { status = "ConfirmationRequired", caller, what, intents = intents.Select(i => i.Describe()) }), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Audit write failed for batch {What} (continuing).", what);
        }
        return (dry, pending);
    }

    /// <summary>
    /// Redeems a batch code issued by <see cref="RequestBatchConfirmationAsync"/> and runs each
    /// intent through every gate again, in order. The set presented must be exactly the set the
    /// code was issued for (same <paramref name="what"/>, same intent hashes); anything else is
    /// refused as tampered and the code is not used up. Throws <see cref="ConfirmationRefusedException"/>.
    /// </summary>
    public async Task<IReadOnlyList<GatewayOutcome>> ConfirmBatchAsync(
        string code, IReadOnlyList<OrderIntent> intents, string what, string caller, CancellationToken ct = default)
    {
        var owner = BatchOwner(intents);
        var (payload, hash) = BatchPayload(what, intents);
        var redemption = await confirmations.RedeemAsync(code, owner, caller,
            p => p.Kind == ConfirmationStore.KindOrderBatch && p.PayloadJson == payload ? hash : "mismatch", ct);
        if (!redemption.Succeeded)
            throw new ConfirmationRefusedException(redemption.Message, redemption.Failure!.Value);

        var placed = new List<GatewayOutcome>();
        foreach (var i in intents)
            placed.Add(await SubmitAsync(i, new GatewayOptions { Caller = caller, LiveConfirmed = true }, ct));
        return placed;
    }

    private static Guid BatchOwner(IReadOnlyList<OrderIntent> intents)
    {
        if (intents.Count == 0) throw new ArgumentException("A batch needs at least one action.", nameof(intents));
        var owner = intents[0].OwnerUserId;
        if (intents.Any(i => i.OwnerUserId != owner))
            throw new ArgumentException("Every action in a batch belongs to the same owner.", nameof(intents));
        return owner;
    }

    /// <summary>The stored payload and its hash: the action name plus the sorted intent hashes.</summary>
    internal static (string Payload, string Hash) BatchPayload(string what, IReadOnlyList<OrderIntent> intents)
    {
        var payload = JsonSerializer.Serialize(new { what, intents = intents.Select(i => i.Hash()).Order(StringComparer.Ordinal) });
        return (payload, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
    }

    internal static string? Validate(OrderIntent intent)
    {
        var o = intent.Order;
        if (intent.OwnerUserId == Guid.Empty) return "No owner for this order.";
        if (o.ClientOrderId is { Length: > 128 }) return "A client order id is at most 128 characters.";
        switch (intent.Action)
        {
            case OrderAction.CancelAll:
                return null;
            case OrderAction.Cancel:
                return string.IsNullOrWhiteSpace(intent.TargetOrderId) ? "Name the order to cancel." : null;
            case OrderAction.Replace:
                if (string.IsNullOrWhiteSpace(intent.TargetOrderId)) return "Name the order to replace.";
                if (o.Quantity < 0 || o.FractionalQuantity is <= 0) return "The new quantity must be positive.";
                if (o.LimitPrice is <= 0 || o.StopPrice is <= 0 || o.TrailPercent is <= 0 || o.TrailPrice is <= 0) return "Prices must be positive.";
                if (o.TrailPercent is not null && o.TrailPrice is not null) return "Trail by percent or by dollars, not both.";
                if (!string.IsNullOrWhiteSpace(o.TimeInForce) && !TimesInForce.Contains(o.TimeInForce.Trim())) return "Time in force is one of day, gtc, opg, cls, ioc, fok.";
                if (o.SizeQuantity <= 0 && o.LimitPrice is null && o.StopPrice is null && o.TrailPercent is null && o.TrailPrice is null
                    && string.IsNullOrWhiteSpace(o.TimeInForce) && o.ClientOrderId is null)
                    return "Say what to change: quantity, limit, stop, trail, time in force or client id.";
                return null;
        }

        var multiLeg = o.OrderClass == OrderClass.Mleg;
        if (string.IsNullOrWhiteSpace(o.Symbol) && !multiLeg) return "No symbol.";
        if (o.AssetClass == AssetClass.Crypto) return ValidateCrypto(o);
        if (o.Quantity < 0) return "Quantity can't be negative.";
        if (o.FractionalQuantity is { } fq && (fq <= 0 || o.Quantity != 0)) return "Give a whole quantity or a positive fractional one, not both.";
        if (o.SizeQuantity == 0 && o.Notional is not > 0) return "Size the order with a share count or a positive dollar amount.";
        if (o.SizeQuantity > 0 && o.Notional is not null) return "Size by shares or by dollars, not both.";
        if (!TimesInForce.Contains(o.TimeInForce.Trim())) return "Time in force is one of day, gtc, opg, cls, ioc, fok.";
        var fractional = o.Notional is not null || (o.FractionalQuantity is { } f && f != decimal.Truncate(f));
        if (fractional && !o.TimeInForce.Trim().Equals("DAY", StringComparison.OrdinalIgnoreCase))
            return "Fractional and dollar-sized orders are DAY only (Alpaca's rule).";
        if (fractional && o.Type == OrderType.TrailingStop) return "Fractional and dollar-sized orders can't be trailing stops.";
        // A multi-leg limit is the net price of the whole combo: positive = debit, negative = credit.
        if (o.Type is OrderType.Limit or OrderType.StopLimit && o.OrderClass != OrderClass.Oco
            && (multiLeg ? o.LimitPrice is null or 0m : o.LimitPrice is not > 0))
            return multiLeg ? "A multi-leg limit needs a net price (positive = debit, negative = credit)." : "A limit order needs a positive limit price.";
        if (o.Type is OrderType.Stop or OrderType.StopLimit && o.StopPrice is not > 0) return "A stop order needs a positive stop price.";
        if (o.Type == OrderType.TrailingStop)
        {
            if (o.TrailPercent is not > 0 && o.TrailPrice is not > 0) return "A trailing stop needs a trail percent or a trail amount.";
            if (o.TrailPercent is not null && o.TrailPrice is not null) return "Trail by percent or by dollars, not both.";
        }
        if (o.ExtendedHours && (o.Type != OrderType.Limit || !o.TimeInForce.Equals("DAY", StringComparison.OrdinalIgnoreCase)))
            return "Extended-hours orders must be DAY limit orders (Alpaca's rule).";
        if (o.AssetClass == AssetClass.Option && !multiLeg && (o.Notional is not null || o.FractionalQuantity is not null || o.ExtendedHours || o.Option is null))
            return "Options trade whole contracts, regular hours, with the contract attached.";
        return ValidateClass(o, fractional);
    }

    /// <summary>
    /// Alpaca's crypto rules: a slash pair, sized by coin quantity (fractional) or dollars, GTC or
    /// IOC, market / limit / stop-limit, a plain order (no brackets, no legs), and no
    /// extended-hours flag because crypto trades around the clock.
    /// </summary>
    private static string? ValidateCrypto(OrderRequest o)
    {
        if (!o.Symbol.Contains('/')) return "A crypto order names a pair, e.g. BTC/USD.";
        if (o.Quantity < 0 || o.FractionalQuantity is <= 0) return "The quantity must be positive.";
        if (o.FractionalQuantity is not null && o.Quantity != 0) return "Give a whole quantity or a fractional one, not both.";
        if (o.SizeQuantity == 0 && o.Notional is not > 0) return "Size the order with a coin quantity or a positive dollar amount.";
        if (o.SizeQuantity > 0 && o.Notional is not null) return "Size by quantity or by dollars, not both.";
        if (o.TimeInForce.Trim().ToUpperInvariant() is not ("GTC" or "IOC")) return "Crypto orders are GTC or IOC (Alpaca's rule).";
        if (o.Type is not (OrderType.Market or OrderType.Limit or OrderType.StopLimit)) return "Crypto orders are market, limit or stop-limit.";
        if (o.Type is OrderType.Limit or OrderType.StopLimit && o.LimitPrice is not > 0) return "A limit order needs a positive limit price.";
        if (o.Type == OrderType.StopLimit && o.StopPrice is not > 0) return "A stop order needs a positive stop price.";
        if (o.ExtendedHours) return "Crypto trades around the clock; there is no extended-hours session to ask for.";
        if (o.OrderClass != OrderClass.Simple || o.TakeProfit is not null || o.StopLoss is not null || o.Legs is { Count: > 0 } || o.Option is not null)
            return "Crypto orders are simple orders: no bracket, OCO, OTO, legs or option contract.";
        return null;
    }

    /// <summary>The order-class rules: which children each class needs and what its parent may be.</summary>
    private static string? ValidateClass(OrderRequest o, bool fractional)
    {
        var tp = o.TakeProfit;
        var sl = o.StopLoss;
        if (tp is { LimitPrice: <= 0 }) return "The take-profit needs a positive limit price.";
        if (sl is not null && (sl.StopPrice <= 0 || sl.LimitPrice is <= 0)) return "The stop-loss needs a positive stop (and limit) price.";
        if (o.OrderClass != OrderClass.Mleg && o.Legs is { Count: > 0 }) return "Only a multi-leg order carries legs.";
        if (o.OrderClass is not (OrderClass.Simple or OrderClass.Mleg))
        {
            if (fractional) return "Bracket, OCO and OTO orders take whole shares.";
            if (o.ExtendedHours) return "Bracket, OCO and OTO orders are regular-hours only.";
            if (!(o.TimeInForce.Trim().Equals("DAY", StringComparison.OrdinalIgnoreCase) || o.TimeInForce.Trim().Equals("GTC", StringComparison.OrdinalIgnoreCase)))
                return "Bracket, OCO and OTO orders are DAY or GTC.";
            // The take-profit sits on the profitable side of the stop: above it on a buy entry
            // (a long), below it on a sell entry (a short). OCO exits a held position, so a sell
            // OCO exits a long (take-profit above the stop) and a buy OCO covers a short.
            if (tp is not null && sl is not null)
            {
                var exitsLong = o.OrderClass == OrderClass.Oco ? o.Side == OrderSide.Sell : o.Side == OrderSide.Buy;
                if (exitsLong ? tp.LimitPrice <= sl.StopPrice : tp.LimitPrice >= sl.StopPrice)
                    return exitsLong ? "The take-profit must be above the stop-loss on a long." : "The take-profit must be below the stop-loss on a short.";
            }
        }
        switch (o.OrderClass)
        {
            case OrderClass.Simple:
                return tp is not null || sl is not null ? "A simple order has no take-profit or stop-loss children; use bracket, oco or oto." : null;
            case OrderClass.Bracket:
                if (tp is null || sl is null) return "A bracket needs both a take-profit and a stop-loss.";
                return o.Type is OrderType.Market or OrderType.Limit ? null : "A bracket's entry is a market or limit order.";
            case OrderClass.Oco:
                if (tp is null || sl is null) return "An OCO needs both a take-profit and a stop-loss.";
                return o.Type == OrderType.Limit ? null : "An OCO is a limit order (its take-profit) paired with a stop.";
            case OrderClass.Oto:
                if ((tp is null) == (sl is null)) return "An OTO takes exactly one child: a take-profit or a stop-loss.";
                return o.Type is OrderType.Market or OrderType.Limit ? null : "An OTO's entry is a market or limit order.";
            case OrderClass.Mleg:
                if (o.Legs is not { Count: >= 2 }) return "A multi-leg order needs at least two legs.";
                if (o.Legs.Any(l => string.IsNullOrWhiteSpace(l.Symbol) || l.RatioQuantity <= 0)) return "Every leg needs a symbol and a positive ratio.";
                if (o.Notional is not null || o.FractionalQuantity is not null) return "A multi-leg order is sized in whole units.";
                return tp is not null || sl is not null ? "A multi-leg order has no take-profit or stop-loss children." : null;
            default:
                return null;
        }
    }

    /// <summary>The Risk Guardian's view of an entry; null when it has no stop or no price to size against.</summary>
    internal static TradeSetup? ToSetup(OrderIntent intent)
    {
        var o = intent.Order;
        var price = o.LimitPrice ?? intent.ReferencePrice;
        // A bracket / OTO carries its own stop-loss child: that is the protective stop.
        var plannedStop = intent.StopLossPrice ?? o.StopLoss?.StopPrice;
        var plannedTarget = intent.TakeProfitPrice ?? o.TakeProfit?.LimitPrice;
        if (plannedStop is not { } stop || price is not > 0) return null;
        if (o.AssetClass == AssetClass.Crypto)
        {
            // The guardian sizes in whole units; 0.01 BTC rounded up to 1 would overstate the risk
            // a hundredfold. Price the whole fractional position as ONE unit instead: dollar risk,
            // stop % and reward:risk all come out exact.
            var units = o.SizeQuantity > 0 ? o.SizeQuantity : (o.Notional ?? 0) / price.Value;
            if (units <= 0) return null;
            return new TradeSetup
            {
                Symbol = o.Symbol.ToUpperInvariant(),
                Direction = o.Side == OrderSide.Buy ? TradeDirection.Long : TradeDirection.Short,
                EntryPrice = price.Value * units,
                EntryType = o.Type,
                StopLoss = stop * units,
                TakeProfit = plannedTarget is { } t ? t * units : 0m,
                Quantity = 1,
                RiskDollars = Math.Abs(price.Value - stop) * units,
                RewardDollars = plannedTarget is { } r ? Math.Abs(r - price.Value) * units : 0m,
                Rationale = intent.Note ?? "",
            };
        }
        var qty =o.SizeQuantity > 0 ? (int)Math.Ceiling(o.SizeQuantity) : (int)Math.Floor((o.Notional ?? 0) / price.Value);
        var isLong = o.Side == OrderSide.Buy;
        return new TradeSetup
        {
            Symbol = o.Symbol.ToUpperInvariant(),
            Direction = isLong ? TradeDirection.Long : TradeDirection.Short,
            EntryPrice = price.Value,
            EntryType = o.Type,
            StopLoss = stop,
            TakeProfit = plannedTarget ?? 0m,
            Quantity = Math.Max(qty, 1),
            RiskDollars = Math.Abs(price.Value - stop) * Math.Max(qty, 1),
            RewardDollars = plannedTarget is { } tp ? Math.Abs(tp - price.Value) * Math.Max(qty, 1) : 0m,
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
        return price.Value * o.SizeQuantity * multiplier;
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
