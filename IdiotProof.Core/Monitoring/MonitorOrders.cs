using IdiotProof.Core.Trading;
using IdiotProof.Models;

namespace IdiotProof.Monitor;

/// <summary>
/// The Monitor's orders as <see cref="OrderIntent"/>s for the <see cref="TransactionGateway"/>
/// (IP-T94). The broker-facing shapes are exactly <see cref="DirectionalOrders"/>' (marketable
/// limits, <c>sell_to_open</c> / <c>*_to_close</c> intents); the intent adds who it is for, the
/// strategy's own mode, and the risk plan the Risk Guardian checks. Origin is always
/// <see cref="OrderOrigin.Monitor"/>: entries need the voter panel's verdict, exits skip it, and a
/// Live strategy's orders need no confirmation code — activating it in Live was the confirmed act.
/// </summary>
public static class MonitorOrders
{
    /// <summary>The verdict passed for an entry when the owner has no voter panel configured.</summary>
    public const string PanelOffDetail =
        "voter panel off for this owner (voting disabled or no Claude key) — conditions and the Risk Guardian decide";

    /// <summary>
    /// An entry: <see cref="DirectionalOrders.Entry"/>, sized by the guardian at the signal price
    /// (<paramref name="price"/>) against <paramref name="stop"/>.
    /// </summary>
    public static OrderIntent Entry(Guid owner, Guid strategyId, string? brokerMode, string symbol, TradeDirection direction,
        int quantity, decimal price, decimal stop, decimal? takeProfit, bool extendedHours, string? note) => new()
    {
        OwnerUserId = owner,
        Mode = OrderIntent.ParseMode(brokerMode),
        Purpose = OrderPurpose.Entry,
        Origin = OrderOrigin.Monitor,
        StrategyId = strategyId,
        Order = DirectionalOrders.Entry(symbol, direction, quantity, price, extendedHours),
        ReferencePrice = price,
        StopLossPrice = stop,
        TakeProfitPrice = takeProfit,
        Note = note,
    };

    /// <summary>An exit: <see cref="DirectionalOrders.Exit"/> — always a <c>*_to_close</c>, so it can only reduce.</summary>
    public static OrderIntent Exit(Guid owner, Guid strategyId, string? brokerMode, string symbol, TradeDirection direction,
        int quantity, decimal price, bool extendedHours, string? note) => new()
    {
        OwnerUserId = owner,
        Mode = OrderIntent.ParseMode(brokerMode),
        Purpose = OrderPurpose.Exit,
        Origin = OrderOrigin.Monitor,
        StrategyId = strategyId,
        Order = DirectionalOrders.Exit(symbol, direction, quantity, price, extendedHours),
        ReferencePrice = price,
        Note = note,
    };

    /// <summary>One line naming why the gateway did not place it: the first gate that refused, else its message.</summary>
    public static string WhyNot(GatewayOutcome outcome) =>
        outcome.Verdicts.FirstOrDefault(v => !v.Passed) is { } failed
            ? $"{failed.Gate}: {failed.Detail}"
            : outcome.Message;
}
