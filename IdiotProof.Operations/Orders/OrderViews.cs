using System.Globalization;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Orders;

/// <summary>
/// One Alpaca order as the Hub shows it, read from the Trading API JSON (<c>/v2/orders</c>). Alpaca
/// sends quantities and prices as strings; every number here is parsed with <see cref="AccountApis.Dec(JsonElement, string)"/>.
/// The children of a bracket / OCO / OTO / multi-leg order are in <see cref="Legs"/> (read with <c>nested=true</c>).
/// </summary>
internal sealed record OrderView
{
    public string Id { get; init; } = "";
    public string? ClientOrderId { get; init; }
    public string Symbol { get; init; } = "";
    public string? AssetClass { get; init; }
    public string Side { get; init; } = "";
    public string Type { get; init; } = "";
    public string? OrderClass { get; init; }
    public string? PositionIntent { get; init; }
    public decimal? Qty { get; init; }
    public decimal? Notional { get; init; }
    public decimal? FilledQty { get; init; }
    public decimal? FilledAvgPrice { get; init; }
    public decimal? LimitPrice { get; init; }
    public decimal? StopPrice { get; init; }
    public decimal? TrailPercent { get; init; }
    public decimal? TrailPrice { get; init; }
    public decimal? HighWaterMark { get; init; }
    public decimal? RatioQty { get; init; }
    public string TimeInForce { get; init; } = "";
    public string Status { get; init; } = "";
    public bool ExtendedHours { get; init; }
    public DateTime? SubmittedUtc { get; init; }
    public DateTime? FilledUtc { get; init; }
    public DateTime? CanceledUtc { get; init; }
    public DateTime? ExpiredUtc { get; init; }
    public DateTime? UpdatedUtc { get; init; }
    public string? ReplacedBy { get; init; }
    public string? Replaces { get; init; }
    public IReadOnlyList<OrderView> Legs { get; init; } = [];

    /// <summary>Statuses where the order can still fill (and so be replaced or cancelled).</summary>
    public bool IsWorking => Status is "new" or "accepted" or "pending_new" or "accepted_for_bidding" or "partially_filled"
        or "held" or "calculated" or "pending_replace" or "pending_cancel" or "stopped" or "suspended" or "done_for_day";

    public static OrderView Parse(JsonElement e) => new()
    {
        Id = AccountApis.Str(e, "id") ?? "",
        ClientOrderId = AccountApis.Str(e, "client_order_id"),
        Symbol = AccountApis.Str(e, "symbol") ?? "",
        AssetClass = AccountApis.Str(e, "asset_class"),
        Side = AccountApis.Str(e, "side") ?? "",
        Type = AccountApis.Str(e, "order_type") ?? AccountApis.Str(e, "type") ?? "",
        OrderClass = AccountApis.Str(e, "order_class"),
        PositionIntent = AccountApis.Str(e, "position_intent"),
        Qty = AccountApis.Dec(e, "qty"),
        Notional = AccountApis.Dec(e, "notional"),
        FilledQty = AccountApis.Dec(e, "filled_qty"),
        FilledAvgPrice = AccountApis.Dec(e, "filled_avg_price"),
        LimitPrice = AccountApis.Dec(e, "limit_price"),
        StopPrice = AccountApis.Dec(e, "stop_price"),
        TrailPercent = AccountApis.Dec(e, "trail_percent"),
        TrailPrice = AccountApis.Dec(e, "trail_price"),
        HighWaterMark = AccountApis.Dec(e, "hwm"),
        RatioQty = AccountApis.Dec(e, "ratio_qty"),
        TimeInForce = AccountApis.Str(e, "time_in_force") ?? "",
        Status = AccountApis.Str(e, "status") ?? "",
        ExtendedHours = AccountApis.Bool(e, "extended_hours"),
        SubmittedUtc = AccountApis.Instant(e, "submitted_at"),
        FilledUtc = AccountApis.Instant(e, "filled_at"),
        CanceledUtc = AccountApis.Instant(e, "canceled_at"),
        ExpiredUtc = AccountApis.Instant(e, "expired_at"),
        UpdatedUtc = AccountApis.Instant(e, "updated_at"),
        ReplacedBy = AccountApis.Str(e, "replaced_by"),
        Replaces = AccountApis.Str(e, "replaces"),
        Legs = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("legs", out var legs) && legs.ValueKind == JsonValueKind.Array
            ? [.. legs.EnumerateArray().Select(Parse)]
            : [],
    };

    /// <summary>"10" shares, "0.25", or "$500" for a notional order; filled shown as "3/10".</summary>
    public string Size => Notional is { } n && Qty is null ? Fmt.Money(n) : Num(Qty);

    public string Filled => FilledQty is { } f && f != 0 ? Num(f) + (Qty is { } q ? "/" + Num(q) : "") : "-";

    /// <summary>"LMT 100.50", "STP 95 LMT 94.5", "TRAIL 2%", "MKT".</summary>
    public string Price => Type switch
    {
        "limit" => "LMT " + Num(LimitPrice),
        "stop" => "STP " + Num(StopPrice),
        "stop_limit" => $"STP {Num(StopPrice)} LMT {Num(LimitPrice)}",
        "trailing_stop" => TrailPrice is { } tp ? "TRAIL $" + Num(tp) : "TRAIL " + Num(TrailPercent) + "%",
        "market" => "MKT",
        var other => other.ToUpperInvariant(),
    };

    /// <summary>The instant that best dates the order: filled, else cancelled/expired, else submitted.</summary>
    public DateTime? When => FilledUtc ?? CanceledUtc ?? ExpiredUtc ?? SubmittedUtc;

    public static string Num(decimal? v) => v is { } d ? d.ToString("0.########", CultureInfo.InvariantCulture) : "-";

    /// <summary>The JSON shape every orders operation returns for one order.</summary>
    public object ToData(string? account = null) => new
    {
        account,
        id = Id, clientOrderId = ClientOrderId, symbol = Symbol, assetClass = AssetClass, side = Side, type = Type,
        orderClass = OrderClass, positionIntent = PositionIntent, qty = Qty, notional = Notional, filledQty = FilledQty,
        filledAvgPrice = FilledAvgPrice, limitPrice = LimitPrice, stopPrice = StopPrice, trailPercent = TrailPercent,
        trailPrice = TrailPrice, hwm = HighWaterMark, ratioQty = RatioQty, timeInForce = TimeInForce, status = Status,
        extendedHours = ExtendedHours, submittedUtc = SubmittedUtc, filledUtc = FilledUtc, canceledUtc = CanceledUtc,
        expiredUtc = ExpiredUtc, replacedBy = ReplacedBy, replaces = Replaces,
        legs = Legs.Select(l => l.ToData()).ToList(),
    };
}

/// <summary>Reads and renders orders for the <c>orders</c> family.</summary>
internal static class OrderViews
{
    /// <summary>
    /// A table of orders, one row each with its legs indented beneath. The Account column appears only
    /// when more than one account is shown. <paramref name="timeHeader"/> names the date column.
    /// </summary>
    public static string Table(IReadOnlyList<(string Account, OrderView Order)> rows, int? width, bool showAccount, string timeHeader = "Submitted (ET)")
    {
        var table = new TextTable { MaxWidth = width };
        if (showAccount) table.Column("Acct");
        table.Column(timeHeader).Column("Symbol").Column("Side").Column("Qty", Align.Right).Column("Filled", Align.Right)
            .Column("Price").Column("Avg fill", Align.Right).Column("TIF").Column("Class").Column("Status").Column("Id");
        foreach (var (account, o) in rows)
        {
            Add(table, showAccount ? account : null, o, timeHeader.StartsWith("Submitted", StringComparison.Ordinal) ? o.SubmittedUtc : o.When, "");
            foreach (var leg in o.Legs)
                Add(table, showAccount ? "" : null, leg, null, "  -> ");
        }
        return table.Render();
    }

    private static void Add(TextTable table, string? account, OrderView o, DateTime? when, string indent)
    {
        var cells = new List<string?>();
        if (account is not null) cells.Add(account);
        cells.AddRange(
        [
            when is { } w ? Fmt.Et(w) : "", indent + o.Symbol, o.Side, o.Size, o.Filled, o.Price,
            o.FilledAvgPrice is { } avg ? Fmt.Money(avg) : "-", o.TimeInForce.ToUpperInvariant(),
            o.OrderClass is null or "" or "simple" ? "" : o.OrderClass, o.Status, o.Id,
        ]);
        table.Row([.. cells]);
    }

    /// <summary>Field/value lines for one order, then its legs as a table.</summary>
    public static string Detail(string account, OrderView o, int? width)
    {
        var fields = new TextTable { MaxWidth = width }.Column("Field").Column("Value");
        void F(string name, string? value) { if (!string.IsNullOrEmpty(value) && value != "-") fields.Row(name, value); }
        F("Account", account);
        F("Id", o.Id);
        F("Client id", o.ClientOrderId);
        F("Symbol", o.Symbol + (o.AssetClass is { } ac && ac != "us_equity" ? $" ({ac})" : ""));
        F("Side", o.Side + (o.PositionIntent is { } pi ? $" ({pi})" : ""));
        F("Type", o.Price);
        F("Class", o.OrderClass is null or "" ? "simple" : o.OrderClass);
        F("Size", o.Size);
        F("Filled", o.Filled + (o.FilledAvgPrice is { } avg ? " @ " + Fmt.Money(avg) : ""));
        F("Time in force", o.TimeInForce.ToUpperInvariant() + (o.ExtendedHours ? " (extended hours)" : ""));
        F("Status", o.Status);
        F("High-water mark", o.HighWaterMark is { } h ? Fmt.Money(h) : null);
        F("Submitted (ET)", o.SubmittedUtc is { } s ? Fmt.Et(s) : null);
        F("Filled (ET)", o.FilledUtc is { } f ? Fmt.Et(f) : null);
        F("Cancelled (ET)", o.CanceledUtc is { } c ? Fmt.Et(c) : null);
        F("Expired (ET)", o.ExpiredUtc is { } x ? Fmt.Et(x) : null);
        F("Replaces", o.Replaces);
        F("Replaced by", o.ReplacedBy);
        var text = fields.Render();
        if (o.Legs.Count == 0) return text;

        var legs = new TextTable { MaxWidth = width }
            .Column("Leg").Column("Symbol").Column("Side").Column("Ratio", Align.Right).Column("Qty", Align.Right).Column("Filled", Align.Right)
            .Column("Price").Column("Avg fill", Align.Right).Column("Status").Column("Id");
        var n = 0;
        foreach (var leg in o.Legs)
            legs.Row((++n).ToString(CultureInfo.InvariantCulture), leg.Symbol, leg.Side + (leg.PositionIntent is { } lpi ? $" ({lpi})" : ""),
                leg.RatioQty is { } r ? OrderView.Num(r) : "", leg.Size, leg.Filled, leg.Price,
                leg.FilledAvgPrice is { } la ? Fmt.Money(la) : "-", leg.Status, leg.Id);
        return text + $"\n\nLegs ({o.Legs.Count})\n" + legs.Render();
    }

    /// <summary>
    /// Finds one order by its Alpaca id (or client id) in the accounts of <paramref name="scope"/>: the
    /// first account that knows it wins. Null with the reasons when none does.
    /// </summary>
    public static async Task<(string? Account, OrderView? Order, string Why)> FindAsync(
        OperationContext ctx, AccountScope scope, string? id, string? clientId)
    {
        var owner = ctx.RequireOwner();
        var factory = ctx.Get<AlpacaApiFactory>();
        var misses = new List<string>();
        foreach (var mode in AccountApis.Modes(scope))
        {
            var name = mode == BrokerMode.Paper ? "Paper" : "Live";
            try
            {
                await using var api = await factory.ForOwnerAsync(owner, mode, ctx.CancellationToken);
                var json = clientId is not null
                    ? await api.GetAsync("/v2/orders:by_client_order_id", new Dictionary<string, string?> { ["client_order_id"] = clientId, ["nested"] = "true" }, ctx.CancellationToken)
                    : await api.GetAsync($"/v2/orders/{Uri.EscapeDataString(id!)}", new Dictionary<string, string?> { ["nested"] = "true" }, ctx.CancellationToken);
                return (name, OrderView.Parse(json), "");
            }
            catch (AlpacaApiException ex) when (ex.Status is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.UnprocessableEntity)
            {
                misses.Add($"{name}: not found");
            }
            catch (AlpacaAccessException ex) when (scope == AccountScope.All)
            {
                misses.Add($"{name}: {ex.Message}");
            }
        }
        return (null, null, string.Join("; ", misses));
    }

    /// <summary>Lists orders across the accounts of <paramref name="scope"/> (one account's failure is its error line when both are read).</summary>
    public static Task<List<AccountRead<List<OrderView>>>> ListAsync(OperationContext ctx, AccountScope scope, IReadOnlyDictionary<string, string?> query) =>
        AccountApis.ForEachAsync(ctx, scope, async api =>
        {
            var json = await api.GetAsync("/v2/orders", query, ctx.CancellationToken);
            return json.ValueKind == JsonValueKind.Array ? json.EnumerateArray().Select(OrderView.Parse).ToList() : [];
        });
}
