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
    /// SHA-256 (hex) over the canonical fields. A Live confirm code is bound to this hash: any
    /// change to symbol, side, size, price, mode, owner or purpose produces a different intent
    /// that needs its own confirmation. The note is excluded (it is commentary, not the order).
    /// </summary>
    public string Hash()
    {
        var o = Order;
        var canon = string.Join('|',
            OwnerUserId.ToString("N"), Mode, Purpose, Origin, StrategyId?.ToString("N") ?? "",
            o.Symbol.ToUpperInvariant(), o.AssetClass, o.Side, o.Type,
            o.Quantity.ToString(CultureInfo.InvariantCulture), Dec(o.Notional), Dec(o.LimitPrice), Dec(o.StopPrice),
            Dec(o.TrailPercent), o.TimeInForce.ToUpperInvariant(), o.ExtendedHours ? "1" : "0", o.PositionIntent ?? "",
            Dec(ReferencePrice), Dec(StopLossPrice), Dec(TakeProfitPrice));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canon)));
    }

    private static string Dec(decimal? d) => d?.ToString("0.########", CultureInfo.InvariantCulture) ?? "";

    /// <summary>Parses a strategy's stored mode string; anything unrecognized is Sandbox (IP-LAW-3).</summary>
    public static BrokerMode ParseMode(string? mode) =>
        Enum.TryParse<BrokerMode>(mode?.Trim(), ignoreCase: true, out var m) && Enum.IsDefined(m) ? m : BrokerMode.Sandbox;

    /// <summary>One line for previews and logs: "BUY 10 NVDA LMT 131.20 DAY ext · Live".</summary>
    public string Describe()
    {
        var o = Order;
        var size = o.Notional is { } n ? "$" + n.ToString("0.##", CultureInfo.InvariantCulture) : o.Quantity.ToString(CultureInfo.InvariantCulture);
        var price = o.Type switch
        {
            OrderType.Limit => " LMT " + Dec(o.LimitPrice),
            OrderType.Stop => " STP " + Dec(o.StopPrice),
            OrderType.StopLimit => $" STP {Dec(o.StopPrice)} LMT {Dec(o.LimitPrice)}",
            OrderType.TrailingStop => $" TRAIL {Dec(o.TrailPercent)}%",
            _ => " MKT",
        };
        return $"{o.Side.ToString().ToUpperInvariant()} {size} {o.Symbol.ToUpperInvariant()}{price} {o.TimeInForce.ToUpperInvariant()}"
             + (o.ExtendedHours ? " ext" : "") + $" · {Mode}";
    }
}
