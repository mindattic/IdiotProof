using IdiotProof.Scripting;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Single source of truth for how every DSL verb is shown to a human — an icon,
/// a theme color, a short title, and a one-line description. Both
/// <c>StrategyBlueprintViz</c> (the strategy-card icon list) and <c>Learn.razor</c>
/// (the Learning Center's clickable verb chips) read from this one dictionary, so
/// authoring a description once teaches it in both places.
///
/// Keyed by the CANONICAL bare verb name — the text before "(" that
/// <see cref="ICondition.ToScript"/> emits for condition types, or the literal
/// <c>StrategyBuilder</c> method name for scalar <c>StrategyDefinition</c> fields
/// that have no <see cref="ICondition"/> wrapper. This is the exact same name space
/// <c>StrategyScriptGenerator.GetVerbsByPhase()</c> already reflects off
/// <c>StrategyBuilder</c> (IP-LAW-4) — one catalog, not two.
///
/// <see cref="DslGlossaryTests"/> asserts every <see cref="IndicatorType"/>,
/// <see cref="PatternType"/>, <see cref="PriceLevelType"/> member AND every verb
/// <c>GetVerbsByPhase()</c> reflects resolves here — that test is what makes "every
/// strategy is visually represented" a standing guarantee instead of a one-time fix.
/// </summary>
public static class DslGlossary
{
    public sealed record Entry(string Key, string Icon, string Color, string Title, string Description);

    // Palette — reuses existing CSS vars, no new colors:
    //   VWAP family        -> de facto VWAP blue (matches live-chart.js / strategy-preview.js)
    //   bullish/long-favor -> --green      bearish/short-favor -> --red
    //   Filters regime gate-> --blue       risk/stop           -> --red
    //   profit/rolling-high-> --green      time/giveback exits -> --brand
    private const string Vwap = "#38bdf8";
    private const string Green = "var(--green)";
    private const string Red = "var(--red)";
    private const string Blue = "var(--blue)";
    private const string Brand = "var(--brand)";

    private static readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal)
    {
        // ── VWAP family ──────────────────────────────────────────────────────
        ["IsAboveVwap"] = new("IsAboveVwap", "bi-water", Vwap, "Above VWAP",
            "Price is currently above the session's volume-weighted average price — the classic intraday bullish-bias filter."),
        ["IsBelowVwap"] = new("IsBelowVwap", "bi-water", Vwap, "Below VWAP",
            "Price is currently below VWAP — the bearish-bias counterpart, often paired with short entries."),
        ["OnVwapReclaim"] = new("OnVwapReclaim", "bi-arrow-return-right", Vwap, "VWAP Reclaim",
            "Trigger: the prior bar closed at-or-below VWAP and the current bar closed back above it — a momentum-shift entry."),
        ["OnVwapLoss"] = new("OnVwapLoss", "bi-arrow-return-left", Vwap, "VWAP Loss",
            "Trigger: the prior bar closed at-or-above VWAP and the current bar closed back below it — the bearish mirror of a reclaim."),

        // ── EMA family ───────────────────────────────────────────────────────
        ["IsAboveEma"] = new("IsAboveEma", "bi-graph-up", Green, "Above EMA",
            "Price is above the given period's exponential moving average — a short-term trend filter."),
        ["IsBelowEma"] = new("IsBelowEma", "bi-graph-down", Red, "Below EMA",
            "Price is below the given period's EMA — the bearish counterpart, often used for shorts."),
        ["IsBetweenEma"] = new("IsBetweenEma", "bi-layers-half", Blue, "Between EMAs",
            "Price sits between the fast and slow EMA — the classic pullback zone in a 9/30 continuation setup."),
        ["RequireEmaStack"] = new("RequireEmaStack", "bi-layers", Blue, "EMA Stack",
            "Filters-phase gate: the fast EMA sits above the slow EMA (an uptrend stack) — always-on, not a one-time trigger."),
        ["OnReclaim"] = new("OnReclaim", "bi-arrow-return-right", Green, "EMA Reclaim",
            "Trigger: the prior bar closed at-or-below the EMA and the current bar closed back above it — the pullback-continuation entry."),

        // ── Regime / trend filters (Filters phase) ─────────────────────────
        ["IsDiPositive"] = new("IsDiPositive", "bi-compass", Green, "+DI Dominant",
            "The positive directional indicator (+DI) is dominant — ADX's bullish-direction confirmation."),
        ["IsDiNegative"] = new("IsDiNegative", "bi-compass", Red, "-DI Dominant",
            "The negative directional indicator (-DI) is dominant — ADX's bearish-direction confirmation."),
        ["IsAdxAbove"] = new("IsAdxAbove", "bi-signpost-2", Blue, "ADX Above",
            "Filters-phase gate: the Average Directional Index is above the threshold — a trending (vs. choppy) market filter."),

        // ── RSI family ───────────────────────────────────────────────────────
        ["IsRsiOversold"] = new("IsRsiOversold", "bi-speedometer", Green, "RSI Oversold",
            "The Relative Strength Index is at-or-below the threshold (default 30) — a mean-reversion long signal."),
        ["IsRsiOverbought"] = new("IsRsiOverbought", "bi-speedometer2", Red, "RSI Overbought",
            "RSI is at-or-above the threshold (default 70) — a mean-reversion short signal, or a caution flag before a long."),
        ["IsRsiBullishDivergence"] = new("IsRsiBullishDivergence", "bi-arrow-up-right", Green, "RSI Bullish Divergence",
            "Price makes a lower low while RSI makes a higher low — hidden buying pressure, an early reversal tell."),
        ["IsRsiBearishDivergence"] = new("IsRsiBearishDivergence", "bi-arrow-down-right", Red, "RSI Bearish Divergence",
            "Price makes a higher high while RSI makes a lower high — hidden selling pressure, an early rollover tell."),

        // ── Swing structure ──────────────────────────────────────────────────
        ["IsHigherLow"] = new("IsHigherLow", "bi-graph-up-arrow", Green, "Higher Low",
            "The newest swing low sits above the prior swing low — \"the bottom is likely in,\" a double-bottom buy tell."),
        ["IsLowerHigh"] = new("IsLowerHigh", "bi-graph-down-arrow", Red, "Lower High",
            "The newest swing high sits below the prior swing high — a weakening rally, the failed-high short tell."),
        ["IsAtSupport"] = new("IsAtSupport", "bi-shield-check", Green, "At Support",
            "Price is within tolerance of the recent swing low — a bounce-off-support entry zone."),
        ["IsAtResistance"] = new("IsAtResistance", "bi-shield-exclamation", Red, "At Resistance",
            "Price is within tolerance of the recent swing high — a fade-off-resistance entry zone."),

        // ── MACD ─────────────────────────────────────────────────────────────
        ["IsMacdBullish"] = new("IsMacdBullish", "bi-activity", Green, "MACD Bullish",
            "The MACD line is above its signal line — a momentum confirmation for longs."),
        ["IsMacdBearish"] = new("IsMacdBearish", "bi-activity", Red, "MACD Bearish",
            "The MACD line is below its signal line — a momentum confirmation for shorts."),

        // ── Gap / volume ─────────────────────────────────────────────────────
        ["IsGapUp"] = new("IsGapUp", "bi-arrow-up-right-circle", Green, "Gap Up",
            "Today's price gapped up at least this percent over the previous close — the classic gapper entry filter."),
        ["IsGapDown"] = new("IsGapDown", "bi-arrow-down-right-circle", Red, "Gap Down",
            "Today's price gapped down at least this percent under the previous close — the short-side gapper filter."),
        ["IsGapBetween"] = new("IsGapBetween", "bi-arrows-collapse", Brand, "Gap Between",
            "The gap over the previous close must fall inside a [min%, max%] band — big enough to matter, not already gone. Fails closed without a previous close."),
        ["IsVolumeAbove"] = new("IsVolumeAbove", "bi-bar-chart-fill", Blue, "Volume Above",
            "The current bar's volume is at least this multiple of the rolling average — a conviction filter for a trigger."),

        // ── Price level / band ───────────────────────────────────────────────
        ["HoldsAbove"] = new("HoldsAbove", "bi-shield-check", Green, "Holds Above",
            "Price must be currently above this level AND must never have dropped meaningfully below it — a held-support confirmation."),
        ["HoldsBelow"] = new("HoldsBelow", "bi-shield-exclamation", Red, "Holds Below",
            "Price must be currently below this level AND must never have risen meaningfully above it — held-resistance confirmation for shorts."),
        ["IsNear"] = new("IsNear", "bi-crosshair", Brand, "Near Level",
            "Price is within a tolerance percent of a specific level — a proximity gate rather than a directional cross."),
        ["BreaksAbove"] = new("BreaksAbove", "bi-arrow-up-circle", Green, "Breaks Above",
            "Trigger: price crosses from at-or-below to above this level on this bar — a breakout entry, fires only on the cross."),
        ["BreaksBelow"] = new("BreaksBelow", "bi-arrow-down-circle", Red, "Breaks Below",
            "Trigger: price crosses from at-or-above to below this level on this bar — a breakdown entry, fires only on the cross."),
        ["IsPriceBetween"] = new("IsPriceBetween", "bi-distribute-vertical", Brand, "Price Between",
            "Current price must sit inside a [min, max] band — stateless, so a brief excursion outside doesn't poison the rest of the session."),

        // ── Candlestick patterns ─────────────────────────────────────────────
        ["Breakout"] = new("Breakout", "bi-rocket-takeoff", Green, "Breakout Pattern",
            "Price traded at-or-above a level at some point in the visible window — the classic breakout pattern trigger."),
        ["Pullback"] = new("Pullback", "bi-arrow-90deg-down", Brand, "Pullback Pattern",
            "Price has retraced from the window high toward (or to) a support level — the continuation-entry pattern, usually paired with Breakout."),
        ["BullishEngulfing"] = new("BullishEngulfing", "bi-square-fill", Green, "Bullish Engulfing",
            "A candlestick reversal pattern: a bullish candle's body fully engulfs the prior bearish candle's body."),
        ["BearishEngulfing"] = new("BearishEngulfing", "bi-square-fill", Red, "Bearish Engulfing",
            "The bearish mirror of Bullish Engulfing — a down candle's body fully engulfs the prior up candle's body."),
        ["Hammer"] = new("Hammer", "bi-hammer", Green, "Hammer",
            "A candlestick with a small body and a long lower wick — a rejection-of-lower-prices reversal signal."),
        ["ShootingStar"] = new("ShootingStar", "bi-star", Red, "Shooting Star",
            "A candlestick with a small body and a long upper wick — a rejection-of-higher-prices reversal signal, the bearish mirror of Hammer."),
        ["Doji"] = new("Doji", "bi-dash-circle", Brand, "Doji",
            "A candlestick where open and close are nearly equal — indecision, often a precursor to a reversal."),

        // ── Setup ────────────────────────────────────────────────────────────
        ["Name"] = new("Name", "bi-tag", Brand, "Name",
            "A human-readable label for the strategy — cosmetic only, no effect on evaluation."),
        ["Session"] = new("Session", "bi-clock", Blue, "Session",
            "Restricts evaluation to a named trading session (Premarket, RTH, AfterHours, Extended)."),
        ["RequireEntryWindow"] = new("RequireEntryWindow", "bi-clock-history", Blue, "Entry Window",
            "Filters-phase gate: entries are only evaluated inside this [start, end) ET time-of-day window — outside it, the strategy never fires."),

        // ── Order / sizing ───────────────────────────────────────────────────
        ["Entry"] = new("Entry", "bi-flag", Brand, "Entry Price",
            "A simple fixed-price entry trigger — fires once price reaches this level."),
        ["Order"] = new("Order", "bi-arrow-left-right", Brand, "Direction",
            "Sets the trade direction (Long or Short) for every entry this strategy fires."),
        ["Long"] = new("Long", "bi-arrow-up-circle", Green, "Long",
            "This strategy buys to open and sells to close — profits when price rises."),
        ["Short"] = new("Short", "bi-arrow-down-circle", Red, "Short",
            "This strategy sells to open and buys to close — profits when price falls."),
        ["Quantity"] = new("Quantity", "bi-stack", Brand, "Quantity",
            "Position size — either a fixed share count, or a dollar (notional) amount when set with a decimal. The two are mutually exclusive."),
        ["WithVolumeConfirm"] = new("WithVolumeConfirm", "bi-bar-chart", Blue, "Volume Confirm",
            "The trigger bar's volume must be at least this multiple of the rolling average — filters out low-conviction triggers, commonly paired with OnReclaim."),

        // ── Risk / exit ──────────────────────────────────────────────────────
        ["TakeProfit"] = new("TakeProfit", "bi-bullseye", Green, "Take Profit",
            "One or more price targets to scale out at. A single price is T1; up to three prices split the exit across T1/T2/T3."),
        ["AddTarget"] = new("AddTarget", "bi-plus-circle", Green, "Add Target",
            "Adds one more scale-out target at a specific price and percent-to-sell — for building a custom multi-target ladder."),
        ["TakeProfitPercent"] = new("TakeProfitPercent", "bi-percent", Green, "Take Profit %",
            "A profit target expressed as a percent move from entry, instead of a fixed price."),
        ["StopLoss"] = new("StopLoss", "bi-shield-x", Red, "Stop Loss",
            "A fixed-price stop — the position is closed if price reaches this level against the trade."),
        ["StopLossPercent"] = new("StopLossPercent", "bi-shield-x", Red, "Stop Loss %",
            "A stop expressed as a percent move from entry, instead of a fixed price."),
        ["TrailingStopLoss"] = new("TrailingStopLoss", "bi-shield-fill-exclamation", Red, "Trailing Stop",
            "The stop follows price as it moves favorably, staying this percent behind the best price seen since entry — locks in gains without a fixed exit."),
        ["ExitStrategy"] = new("ExitStrategy", "bi-flag-fill", Brand, "Time Exit",
            "A hard time-of-day exit — the position is flattened at this ET time regardless of price. Same effect as SellBy."),
        ["PeakGiveback"] = new("PeakGiveback", "bi-arrow-90deg-down", Brand, "Peak Giveback",
            "Momentum-rollover exit: after entry, track the high-water mark, and sell once price gives back this percent of the run from entry to peak. Can be armed only from a given ET time."),
        ["ExitAtPriorHigh"] = new("ExitAtPriorHigh", "bi-flag-fill", Green, "Exit at Prior High",
            "Exit a long position into the pre-entry high-of-day — sell into the level the stock already proved it could reach."),
        ["ExitAtRollingHigh"] = new("ExitAtRollingHigh", "bi-graph-up", Green, "Exit at Rolling High",
            "Exit a long when price is within a buffer percent of the rolling N-trading-day high — e.g. sell near the 20-day peak. The Monitor recomputes the target every tick as the window rolls forward."),
        ["ExitAtRollingLow"] = new("ExitAtRollingLow", "bi-graph-down", Red, "Exit at Rolling Low",
            "Cut a long when price falls within a buffer percent above the rolling N-day low — a support-failure stop that adapts as the window rolls forward."),

        // ── Entry-side rolling gates ─────────────────────────────────────────
        ["EntryAtRollingLow"] = new("EntryAtRollingLow", "bi-box-arrow-in-down", Green, "Entry Near Rolling Low",
            "Entry gate: only enter when price is within a buffer percent above the rolling N-day low — buying near support."),
        ["EntryAtRollingHigh"] = new("EntryAtRollingHigh", "bi-box-arrow-in-up", Brand, "Entry Near Rolling High",
            "Entry gate: only enter when price is within a buffer percent below the rolling N-day high — a breakout-attempt entry near resistance."),

        // ── Advanced / execution flags ───────────────────────────────────────
        ["AutonomousTrading"] = new("AutonomousTrading", "bi-robot", Brand, "Autonomous Trading",
            "Marks the strategy as fully autonomous — no manual confirmation step before an order is placed once all gates clear."),
        ["AdaptiveOrder"] = new("AdaptiveOrder", "bi-cpu", Brand, "Adaptive Order",
            "Marks the order as adaptive — sizing/pricing may adjust to live conditions rather than using fixed values verbatim."),
        ["Repeat"] = new("Repeat", "bi-arrow-repeat", Brand, "Repeat",
            "Allows the strategy to fire again after a completed round-trip, instead of running once and stopping."),
        ["Then"] = new("Then", "bi-diagram-3", Brand, "Branch (Then)",
            "Starts a conditional branch: the condition immediately before .Then() becomes the branch's \"if\" — chain .ElseIf()/.Else() for alternate paths, each with its own Order/Risk/Exit overrides."),
    };

    // Builder methods that produce the exact same condition/field as a canonical
    // entry above, under a different name (natural-language phrasing, or an
    // explicit shares/notional disambiguator). Every alias GetVerbsByPhase()
    // reflects must resolve here so no Learning Center chip is a dead click.
    private static readonly Dictionary<string, string> aliases = new(StringComparer.Ordinal)
    {
        ["IsEmaAbove"] = "IsAboveEma",
        ["IsEmaBelow"] = "IsBelowEma",
        ["AboveVwap"] = "IsAboveVwap",
        ["BelowVwap"] = "IsBelowVwap",
        ["Oversold"] = "IsRsiOversold",
        ["Overbought"] = "IsRsiOverbought",
        ["BullishMacd"] = "IsMacdBullish",
        ["BearishMacd"] = "IsMacdBearish",
        ["Trending"] = "IsAdxAbove",
        ["RequireAdxAbove"] = "IsAdxAbove",
        ["GapUp"] = "IsGapUp",
        ["GapDown"] = "IsGapDown",
        ["VolumeSpike"] = "IsVolumeAbove",
        ["QuantityShares"] = "Quantity",
        ["QuantityNotional"] = "Quantity",
        ["EntryWindow"] = "RequireEntryWindow",
        ["IsNearRollingLow"] = "EntryAtRollingLow",
        ["EntryAtSupport"] = "EntryAtRollingLow",
        ["IsNearRollingHigh"] = "EntryAtRollingHigh",
        ["EntryAtResistance"] = "EntryAtRollingHigh",
        ["IsBreakingOut"] = "EntryAtRollingHigh",
        ["SellBy"] = "ExitStrategy",
        ["IsBullishEngulfing"] = "BullishEngulfing",
        ["IsBearishEngulfing"] = "BearishEngulfing",
        ["IsHammer"] = "Hammer",
        ["IsShootingStar"] = "ShootingStar",
        ["IsDoji"] = "Doji",
    };

    /// <summary>The canonical bare verb name for a condition — the text before "(" in its <c>ToScript()</c>.</summary>
    public static string KeyFor(ICondition c) => c.ToScript().Split('(')[0].Trim();

    public static Entry? Find(ICondition c) => Find(KeyFor(c));

    public static Entry? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (entries.TryGetValue(key, out var e)) return e;
        return aliases.TryGetValue(key, out var canonical) ? entries.GetValueOrDefault(canonical) : null;
    }
}
