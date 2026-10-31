using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Models;

namespace IdiotProof.Core.Trading;

/// <summary>Where an order is routed (a strategy's <c>BrokerMode</c>). Sandbox is the safe default (IP-LAW-3).</summary>
public enum BrokerMode { Sandbox, Paper, Live }

/// <summary>Who asked for the order.</summary>
public enum OrderOrigin
{
    /// <summary>The Monitor firing a strategy (all three gates apply, IP-LAW-1).</summary>
    Monitor,
    /// <summary>A person, through the CLI, the MCP server or a web page.</summary>
    Manual,
    /// <summary>A manual options ticket.</summary>
    Options,
    /// <summary>A flatten command closing positions.</summary>
    Flatten,
}

/// <summary>Opening risk or reducing it. Exits skip the LLM panel but honor the kill-switch (IP-LAW-1).</summary>
public enum OrderPurpose { Entry, Exit }

/// <summary>
/// What the gateway does with an intent. Place sends <see cref="OrderIntent.Order"/>; Replace
/// changes the working order <see cref="OrderIntent.TargetOrderId"/> to the sizes/prices/TIF in
/// <see cref="OrderIntent.Order"/>; Cancel cancels that order; CancelAll cancels every open order
/// (only those for <see cref="OrderRequest.Symbol"/> when it is not "*"). All four take the same
/// route, audit and Live confirmation.
/// </summary>
public enum OrderAction { Place, Replace, Cancel, CancelAll }

/// <summary>
/// Everything the transaction gateway needs to decide on and place one order: the broker-facing
/// <see cref="OrderRequest"/> plus who it is for, where it routes, why it exists and the risk
/// plan the Risk Guardian checks. Every order — Monitor fires, manual trades, options, flattens —
/// is expressed as one of these before it reaches a broker.
/// </summary>
public sealed record OrderIntent
{
    public required Guid OwnerUserId { get; init; }
    public required BrokerMode Mode { get; init; }
    public required OrderRequest Order { get; init; }
    public required OrderPurpose Purpose { get; init; }
    public required OrderOrigin Origin { get; init; }

    /// <summary>Place (default), Replace, Cancel or CancelAll.</summary>
    public OrderAction Action { get; init; } = OrderAction.Place;

    /// <summary>The broker order id a Replace or Cancel acts on; null for Place and CancelAll.</summary>
    public string? TargetOrderId { get; init; }

    /// <summary>
    /// For a Replace: the working order's quantity before the change. A replace that raises the
    /// size of an entry is new risk, so the gateway runs the Risk Guardian on it; when this is
    /// unknown (null) any new quantity counts as a raise (fail closed).
    /// </summary>
    public decimal? ReplacedQuantity { get; init; }

    /// <summary>The strategy that produced it, when one did.</summary>
    public Guid? StrategyId { get; init; }

    /// <summary>Reference price for sizing and risk (last trade or the limit), when known.</summary>
    public decimal? ReferencePrice { get; init; }

    /// <summary>Planned protective stop for an entry; the Risk Guardian refuses entries without one (IP-LAW-2).</summary>
    public decimal? StopLossPrice { get; init; }

    /// <summary>Planned take-profit, for the reward:risk warning.</summary>
    public decimal? TakeProfitPrice { get; init; }

    /// <summary>Free-text reason shown in previews and the audit trail ("flatten all", "CLI buy").</summary>
    public string? Note { get; init; }

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Stable JSON of the whole intent; what a pending Live confirmation stores and replays.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, CanonicalJson);

    public static OrderIntent FromJson(string json) =>
        JsonSerializer.Deserialize<OrderIntent>(json, CanonicalJson)
        ?? throw new JsonException("Empty order intent.");

    /// <summary>
    /// SHA-256 (hex) over the canonical JSON of the whole intent: the action, its target, every
    /// <see cref="OrderRequest"/> field (class, legs, take-profit/stop-loss children, trail, TIF,
    /// client id, fractional size...), mode, owner, purpose, origin and the risk plan. A Live confirm
    /// code is bound to this hash, so ANY change to the order produces a different intent that needs
    /// its own confirmation; a field added to the model later is covered automatically. Symbols and
    /// TIF are upper-cased and decimals written without trailing zeros so "131.2" and "131.20" agree.
    /// The note is excluded (it is commentary, not the order).
    /// </summary>
    public string Hash()
    {
        var o = Order;
        var normalized = this with
        {
            Note = null,
            Order = new OrderRequest
            {
                Symbol = o.Symbol.Trim().ToUpperInvariant(), Quantity = o.Quantity, FractionalQuantity = o.FractionalQuantity,
                Notional = o.Notional, Side = o.Side, Type = o.Type, LimitPrice = o.LimitPrice, StopPrice = o.StopPrice,
                TrailPercent = o.TrailPercent, TrailPrice = o.TrailPrice, TimeInForce = o.TimeInForce.Trim().ToUpperInvariant(),
                ExtendedHours = o.ExtendedHours, PositionIntent = o.PositionIntent, ClientOrderId = o.ClientOrderId,
                OrderClass = o.OrderClass, TakeProfit = o.TakeProfit, StopLoss = o.StopLoss,
                Legs = o.Legs?.Select(l => l with { Symbol = l.Symbol.Trim().ToUpperInvariant() }).ToList(),
                AssetClass = o.AssetClass, Option = o.Option,
            },
        };
        var canon = JsonSerializer.Serialize(normalized, HashJson);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canon)));
    }

    private static readonly JsonSerializerOptions HashJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(), new CanonicalDecimalConverter() },
    };

    /// <summary>Writes a decimal without trailing zeros (131.20 becomes 131.2) so equal amounts hash equally.</summary>
    private sealed class CanonicalDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("0.############################", CultureInfo.InvariantCulture));
    }

    private static string Dec(decimal? d) => d?.ToString("0.########", CultureInfo.InvariantCulture) ?? "";

    /// <summary>Parses a strategy's stored mode string; anything unrecognized is Sandbox (IP-LAW-3).</summary>
    public static BrokerMode ParseMode(string? mode) =>
        Enum.TryParse<BrokerMode>(mode?.Trim(), ignoreCase: true, out var m) && Enum.IsDefined(m) ? m : BrokerMode.Sandbox;

    /// <summary>One line for previews and logs: "BUY 10 NVDA LMT 131.20 DAY ext · Live", "CANCEL order 61e6… · Paper".</summary>
    public string Describe()
    {
        var o = Order;
        var target = TargetOrderId is { Length: > 0 } id ? "order " + id : "order ?";
        var named = string.IsNullOrWhiteSpace(o.Symbol) || o.Symbol == "*" ? null : o.Symbol.ToUpperInvariant();
        switch (Action)
        {
            case OrderAction.Cancel:
                return $"CANCEL {target}" + (named is null ? "" : $" ({named})") + $" · {Mode}";
            case OrderAction.CancelAll:
                return "CANCEL ALL open orders" + (named is null ? "" : $" for {named}") + $" · {Mode}";
            case OrderAction.Replace:
                var changes = new List<string>();
                if (o.SizeQuantity > 0) changes.Add("qty " + Dec(o.SizeQuantity));
                if (o.LimitPrice is { } lp) changes.Add("limit " + Dec(lp));
                if (o.StopPrice is { } sp) changes.Add("stop " + Dec(sp));
                if (o.TrailPercent is { } tp) changes.Add($"trail {Dec(tp)}%");
                if (o.TrailPrice is { } tpr) changes.Add("trail $" + Dec(tpr));
                if (!string.IsNullOrWhiteSpace(o.TimeInForce)) changes.Add(o.TimeInForce.ToUpperInvariant());
                if (o.ClientOrderId is { } cid) changes.Add("client id " + cid);
                return $"REPLACE {target}" + (named is null ? "" : $" ({named})") + " -> " + string.Join(", ", changes) + $" · {Mode}";
        }

        var size = o.Notional is { } n ? "$" + n.ToString("0.##", CultureInfo.InvariantCulture) : Dec(o.SizeQuantity);
        var price = o.Type switch
        {
            OrderType.Limit => " LMT " + Dec(o.LimitPrice),
            OrderType.Stop => " STP " + Dec(o.StopPrice),
            OrderType.StopLimit => $" STP {Dec(o.StopPrice)} LMT {Dec(o.LimitPrice)}",
            OrderType.TrailingStop => o.TrailPrice is { } tpx ? $" TRAIL ${Dec(tpx)}" : $" TRAIL {Dec(o.TrailPercent)}%",
            _ => " MKT",
        };
        var cls = o.OrderClass == OrderClass.Simple ? "" : " " + o.OrderClass.ToString().ToUpperInvariant();
        var children = (o.TakeProfit is { } t ? $" TP {Dec(t.LimitPrice)}" : "")
                     + (o.StopLoss is { } s ? $" SL {Dec(s.StopPrice)}" + (s.LimitPrice is { } sl ? $"/{Dec(sl)}" : "") : "");
        var legs = o.Legs is { Count: > 0 } ls
            ? " [" + string.Join(" / ", ls.Select(l => $"{l.Side.ToString().ToUpperInvariant()} {l.RatioQuantity}x {l.Symbol.ToUpperInvariant()}")) + "]"
            : "";
        var symbol = named is null ? "" : " " + named;
        return $"{o.Side.ToString().ToUpperInvariant()} {size}{symbol}{price}{cls}{children}{legs} {o.TimeInForce.ToUpperInvariant()}"
             + (o.ExtendedHours ? " ext" : "") + (o.ClientOrderId is { } c ? $" id {c}" : "") + $" · {Mode}";
    }
}
