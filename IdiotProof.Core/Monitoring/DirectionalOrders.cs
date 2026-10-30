using IdiotProof.Models;

namespace IdiotProof.Monitor;

/// <summary>
/// The direction-aware order shapes the Monitor places. A long opens with a buy and closes with a
/// sell; a short opens with a sell (<c>sell_to_open</c>) and closes with a buy (<c>buy_to_close</c>).
/// Every price buffer is marketable in the direction of the fill, and realized P&amp;L is inverted
/// for a short. Pure: the entry path (<c>MonitorWorker.FireAsync</c>), the exit path
/// (<c>EvaluateExitAsync</c>) and the operator <c>flatten</c> command all build their orders here.
/// </summary>
public static class DirectionalOrders
{
    /// <summary>Entry limit buffer: pay up to +0.2% to buy, accept down to -0.2% to sell short.</summary>
    public const decimal EntryBuffer = 0.002m;

    /// <summary>Exit limit buffer: -0.5% to sell out of a long, +0.5% to buy back a short.</summary>
    public const decimal ExitBuffer = 0.005m;

    public static bool IsShort(TradeDirection direction) => direction == TradeDirection.Short;

    public static OrderSide EntrySide(TradeDirection direction) => IsShort(direction) ? OrderSide.Sell : OrderSide.Buy;

    public static OrderSide ExitSide(TradeDirection direction) => IsShort(direction) ? OrderSide.Buy : OrderSide.Sell;

    /// <summary>"BUY"/"SELL" for logs and console fills.</summary>
    public static string SideLabel(OrderSide side) => side == OrderSide.Buy ? "BUY" : "SELL";

    /// <summary>Marketable entry limit, rounded to the cent.</summary>
    public static decimal EntryLimit(TradeDirection direction, decimal price) =>
        Math.Round(price * (IsShort(direction) ? 1m - EntryBuffer : 1m + EntryBuffer), 2);

    /// <summary>Marketable exit limit, rounded to the cent.</summary>
    public static decimal ExitLimit(TradeDirection direction, decimal price) =>
        Math.Round(price * (IsShort(direction) ? 1m + ExitBuffer : 1m - ExitBuffer), 2);

    /// <summary>
    /// Entry order. A short carries <c>sell_to_open</c> so the broker opens a short rather than
    /// selling shares the account may already hold long. Long entries carry no intent (unchanged).
    /// </summary>
    public static OrderRequest Entry(string symbol, TradeDirection direction, int quantity, decimal price, bool extendedHours) => new()
    {
        Symbol         = symbol,
        Quantity       = quantity,
        Side           = EntrySide(direction),
        Type           = OrderType.Limit,
        LimitPrice     = EntryLimit(direction, price),
        TimeInForce    = "DAY",
        ExtendedHours  = extendedHours,
        PositionIntent = IsShort(direction) ? "sell_to_open" : null,
    };

    /// <summary>
    /// Exit order. The intent is always a <c>_to_close</c> value so a stale quantity can only
    /// close, never flip the account into the opposite position.
    /// </summary>
    public static OrderRequest Exit(string symbol, TradeDirection direction, int quantity, decimal price, bool extendedHours) => new()
    {
        Symbol         = symbol,
        Quantity       = quantity,
        Side           = ExitSide(direction),
        Type           = OrderType.Limit,
        LimitPrice     = ExitLimit(direction, price),
        TimeInForce    = "DAY",
        ExtendedHours  = extendedHours,
        PositionIntent = IsShort(direction) ? "buy_to_close" : "sell_to_close",
    };

    /// <summary>
    /// Whole shares the broker position holds in <paramref name="direction"/>: the positive quantity
    /// for a long, the negated negative quantity for a short (Alpaca reports shorts as "-10").
    /// A position on the other side counts as zero held.
    /// </summary>
    public static int HeldShares(Position? position, TradeDirection direction)
    {
        if (position is null) return 0;
        var signed = IsShort(direction) ? -position.Quantity : position.Quantity;
        return signed > 0m ? (int)Math.Floor(signed) : 0;
    }

    /// <summary>Realized P&amp;L of closing <paramref name="quantity"/> shares: (exit − entry) for a long, (entry − exit) for a short.</summary>
    public static decimal RealizedPnl(TradeDirection direction, decimal entryPrice, decimal exitPrice, int quantity) =>
        (IsShort(direction) ? entryPrice - exitPrice : exitPrice - entryPrice) * quantity;
}
