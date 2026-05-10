using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Idempotent seeder for the Learning Center. Runs once on app startup; checks
/// each canonical article by slug and upserts only when missing or when the
/// authoring date in the seed has advanced past the row's UpdatedUtc.
///
/// The catalog below is the single source of truth for the encyclopedia. To
/// add a verb's documentation page, append a new <see cref="Article"/> entry
/// — the seeder will pick it up on next start.
///
/// Body content is Markdown-ish; <c>[[Stock.Ticker(...)]]</c> wikilinks render
/// as live strategy flow-charts via the <see cref="Components.Shared.WikiContent"/>
/// component on the /learn page.
/// </summary>
public static class LearningContentSeeder
{
    /// <summary>Bumped whenever the seed catalog changes — drives upsert.</summary>
    private static readonly DateTime CatalogVersion = new(2026, 5, 9, 0, 0, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.LearningArticles.ToDictionaryAsync(a => a.Slug, ct);

        foreach (var seed in Articles)
        {
            if (existing.TryGetValue(seed.Slug, out var current))
            {
                if (current.UpdatedUtc < CatalogVersion)
                {
                    current.Title        = seed.Title;
                    current.Summary      = seed.Summary;
                    current.Category     = seed.Category;
                    current.BodyMarkdown = seed.BodyMarkdown;
                    current.Order        = seed.Order;
                    current.UpdatedUtc   = CatalogVersion;
                }
            }
            else
            {
                db.LearningArticles.Add(new LearningArticle
                {
                    Slug         = seed.Slug,
                    Category     = seed.Category,
                    Title        = seed.Title,
                    Summary      = seed.Summary,
                    BodyMarkdown = seed.BodyMarkdown,
                    Order        = seed.Order,
                    UpdatedUtc   = CatalogVersion,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private sealed record Article(string Slug, string Category, int Order, string Title, string? Summary, string BodyMarkdown);

    /// <summary>
    /// Canonical article catalog. Categories render as sidebar sections in the
    /// Learning Center, in the alphabetical order of the first article's
    /// Category. Order field controls within-category ordering.
    /// </summary>
    private static readonly Article[] Articles =
    [
        // ───────── 1. OVERVIEW ─────────
        new(
            Slug: "overview",
            Category: "1. Overview",
            Order: 0,
            Title: "What is IdiotScript?",
            Summary: "The fluent DSL that turns plain-English trading rules into rules the Monitor can run.",
            BodyMarkdown: """
                IdiotScript is the fluent C# DSL that powers IdiotProof's strategy engine. You write a strategy as a chain of method calls; the parser turns it into a `StrategyDefinition`; the Monitor (`StrategyExecutionService` + the `IdiotProof.Monitor` console app) walks every active definition each tick and emits `TradeSignal`s when the conditions match.

                Three ways to author a strategy:

                1. **Guided** — fill out a multi-step wizard. Best for first-time users.
                2. **Script** — type IdiotScript directly. Best when you know exactly what you want.
                3. **Describe** — type plain English and let Claude (via the MindAttic.Legion high-tier voter panel) generate the script. Best when you have an idea but aren't sure of the syntax.

                A complete strategy looks like this:

                [[Stock.Ticker("TSLA").RequireAdxAbove(20).RequireEmaStack(9, 31).IsAboveVwap().IsBetweenEma(9, 31).OnReclaim(9).WithVolumeConfirm(1.2).Long().StopLoss(9.50).TakeProfit(12.00).Build()]]

                The renderer above is live: it parses the same `[[…]]` wikilink syntax you'll see throughout the Learning Center to embed examples inline with prose.

                Continue to **Phases** to learn how the six lifecycle stages organize every strategy.
                """),

        // ───────── 2. PHASES ─────────
        new(
            Slug: "phases-overview",
            Category: "2. Phases",
            Order: 0,
            Title: "The six lifecycle phases",
            Summary: "Setup → Filters → Entry → Order → Risk → Exit. Every verb belongs to exactly one phase.",
            BodyMarkdown: """
                Every IdiotScript strategy walks through six fixed phases in order. The visual builder renders one card per phase; the parser rejects verbs used in the wrong phase. This layered structure is the safety rail that keeps strategies legible — you always know what each verb is contributing.

                | # | Phase    | What it answers                                       | Example verbs |
                |---|----------|-------------------------------------------------------|---------------|
                | 1 | Setup    | What ticker, what session, what window?               | `Ticker`, `Session`, `Quantity` |
                | 2 | Filters  | What regime conditions must hold throughout?          | `RequireAdxAbove`, `RequireEmaStack` |
                | 3 | Entry    | What triggers a fire? (the AND of these conditions)   | `IsAboveVwap`, `OnReclaim`, `IsBullishEngulfing` |
                | 4 | Order    | What direction, what size?                            | `Long`, `Short`, `Quantity` |
                | 5 | Risk     | Where does the stop go?                               | `StopLoss`, `TrailingStopLoss` |
                | 6 | Exit     | Where does the trade close?                           | `TakeProfit`, `ExitStrategy` |

                Phases 2 (Filters) and 3 (Entry) are both condition phases — Filters are *always-on* gates (regime preconditions); Entry conditions are *triggers* (must all be true for the strategy to fire). Use `RequireFoo` for filters, `IsFoo` / `OnFoo` for entries.
                """),

        // ───────── 3. SETUP VERBS ─────────
        new(
            Slug: "setup-ticker",
            Category: "3. Setup Verbs",
            Order: 0,
            Title: "Ticker(symbol)",
            Summary: "Required first call. Pins the strategy to a single symbol.",
            BodyMarkdown: """
                **Signature:** `Stock.Ticker(string symbol)`

                Every strategy starts here. Stock.Ticker returns a `StrategyBuilder` you can continue chaining off of. Symbols are case-insensitive at parse time but stored upper-cased.

                [[Stock.Ticker("AAPL").Long().Build()]]

                A strategy without a Ticker call won't parse — the wikilink renderer here would fall back to "unparseable script" and show the raw text.
                """),

        new(
            Slug: "setup-session",
            Category: "3. Setup Verbs",
            Order: 1,
            Title: "Session(session)",
            Summary: "Restricts the strategy to a market session (RTH, Premarket, AfterHours, Extended).",
            BodyMarkdown: """
                **Signature:** `Session(TradingSession session)`

                Determines which session the Monitor evaluates the strategy in. Strategies authored in the Premarket session won't fire during regular hours and vice versa. Default is RTH.

                Sessions:
                - `Premarket` — 4:00 AM – 9:30 AM ET
                - `RTH` — 9:30 AM – 4:00 PM ET (Regular Trading Hours)
                - `AfterHours` — 4:00 PM – 8:00 PM ET
                - `Extended` — all sessions
                """),

        // ───────── 4. FILTER VERBS ─────────
        new(
            Slug: "filter-require-adx",
            Category: "4. Filter Verbs",
            Order: 0,
            Title: "RequireAdxAbove(threshold)",
            Summary: "Regime gate: ADX must be above threshold (trending market). Default 20.",
            BodyMarkdown: """
                **Signature:** `RequireAdxAbove(double threshold = 20)`
                **Phase:** Filters

                ADX measures trend strength. RequireAdxAbove(20) is the canonical "trending market" filter — it suppresses strategies during chop. Pair with `RequireEmaStack` to confirm trend direction.

                [[Stock.Ticker("SPY").RequireAdxAbove(20).RequireEmaStack(9, 31).IsAboveVwap().Long().Build()]]

                Without this filter, a pullback-continuation strategy will fire constantly in sideways markets where the same price-vs-EMA geometry exists with no edge. The 20 default matches Wilder's original definition; many traders use 25 for stricter regime confirmation.
                """),

        new(
            Slug: "filter-require-ema-stack",
            Category: "4. Filter Verbs",
            Order: 1,
            Title: "RequireEmaStack(fast, slow)",
            Summary: "Regime gate: fast EMA above slow EMA = uptrend. Inverted = downtrend.",
            BodyMarkdown: """
                **Signature:** `RequireEmaStack(int fast, int slow)`
                **Phase:** Filters

                Confirms the EMA stack direction. `RequireEmaStack(9, 31)` means the 9-period EMA must be above the 31-period EMA — that's an uptrend. Pair this with `IsBetweenEma(9, 31)` for the classic 9/30 pullback continuation. Without this filter, IsBetweenEma fires during downtrend rallies into resistance — the *opposite* of what you want.

                [[Stock.Ticker("AAPL").RequireAdxAbove(20).RequireEmaStack(9, 31).IsBetweenEma(9, 31).OnReclaim(9).Long().Build()]]
                """),

        // ───────── 5. ENTRY VERBS ─────────
        new(
            Slug: "entry-is-above-vwap",
            Category: "5. Entry Verbs",
            Order: 0,
            Title: "IsAboveVwap() / IsBelowVwap()",
            Summary: "Price relative to volume-weighted average price.",
            BodyMarkdown: """
                **Signatures:** `IsAboveVwap()` / `IsBelowVwap()`
                **Aliases:** `AboveVwap()` / `BelowVwap()`

                The single most common bias filter. VWAP resets at the start of each ET session (4 AM); above = institutional bullish bias, below = bearish bias.

                [[Stock.Ticker("TSLA").IsAboveVwap().IsAboveEma(9).Long().StopLossPercent(2).Build()]]
                """),

        new(
            Slug: "entry-on-reclaim",
            Category: "5. Entry Verbs",
            Order: 1,
            Title: "OnReclaim(period)",
            Summary: "Trigger: prior bar at-or-below N-EMA AND current bar closed back above. The classic pullback re-entry.",
            BodyMarkdown: """
                **Signature:** `OnReclaim(int emaPeriod)`

                The single best pullback re-entry trigger. OnReclaim(9) means: prior bar's close was at or below the 9-period EMA, AND the current bar's close is back above it. This is the textbook "9 EMA reclaim" — it gives you confirmation that the pullback is over without entering at the deepest point.

                Pair with regime filters to get a complete pullback continuation:

                [[Stock.Ticker("NVDA").RequireAdxAbove(20).RequireEmaStack(9, 31).IsBetweenEma(9, 31).OnReclaim(9).WithVolumeConfirm(1.2).Long().StopLoss(450).Build()]]

                The volume confirm is optional but worth its weight — low-volume reclaims fail far more often than high-volume ones.
                """),

        new(
            Slug: "entry-is-between-ema",
            Category: "5. Entry Verbs",
            Order: 2,
            Title: "IsBetweenEma(fast, slow)",
            Summary: "Price sits between the two EMAs — the pullback zone in a trending market.",
            BodyMarkdown: """
                **Signature:** `IsBetweenEma(int fast, int slow)`

                Marks price as being inside the pullback zone. By itself this is *not* a trade signal — in a downtrend, the same geometry means price is rallying into resistance. Always pair with `RequireEmaStack(fast, slow)` to confirm the trend direction *and* with a trigger like `OnReclaim` so you don't enter mid-pullback.

                [[Stock.Ticker("AMD").RequireEmaStack(9, 31).IsBetweenEma(9, 31).OnReclaim(9).Long().Build()]]
                """),

        new(
            Slug: "entry-rsi-divergence",
            Category: "5. Entry Verbs",
            Order: 3,
            Title: "IsRsiBullishDivergence() / IsRsiBearishDivergence()",
            Summary: "Detects classic momentum divergence — price makes lower low, RSI makes higher low (bullish), and vice versa.",
            BodyMarkdown: """
                **Signatures:** `IsRsiBullishDivergence()` / `IsRsiBearishDivergence()`

                Computed by the snapshot builder via the divergence flags on `IndicatorSnapshot`. Useful as a confluence signal alongside oversold/overbought:

                [[Stock.Ticker("META").IsRsiBullishDivergence().Oversold().Long().StopLossPercent(2).Build()]]
                """),

        new(
            Slug: "entry-candle-patterns",
            Category: "5. Entry Verbs",
            Order: 4,
            Title: "Candlestick patterns: Engulfing / Hammer / Shooting Star / Doji",
            Summary: "Single-bar and two-bar reversal candles, computed from the latest candle's OHLC.",
            BodyMarkdown: """
                **Signatures:** `IsBullishEngulfing()` / `IsBearishEngulfing()` / `IsHammer()` / `IsShootingStar()` / `IsDoji()`

                Detected by `IdiotProof.Indicators.CandlestickPatterns` against the most recent closed bar (and the prior bar for two-bar patterns).

                **Hammer** — small body in the top third of the bar's range, lower shadow ≥ 2× the body, tiny upper shadow. Reversal candidate after a downtrend:

                [[Stock.Ticker("BAC").IsAtSupport(0.5).IsHammer().Long().Build()]]

                **Bullish Engulfing** — a bullish bar whose body fully contains the prior bearish bar's body:

                [[Stock.Ticker("CRM").IsBullishEngulfing().IsRsiOversold().Long().Build()]]

                **Doji** alone is rarely actionable — it indicates indecision. Better as a *filter* (e.g. after Doji, wait for the next bar's direction).
                """),

        new(
            Slug: "entry-volume-confirm",
            Category: "5. Entry Verbs",
            Order: 5,
            Title: "WithVolumeConfirm(multiplier)",
            Summary: "Trigger-bar volume must be at least N× the rolling average.",
            BodyMarkdown: """
                **Signature:** `WithVolumeConfirm(double multiplier = 1.2)`
                **Aliases:** `IsVolumeAbove(multiplier)` / `VolumeSpike(2.0)` (preset)

                Volume confirms conviction. WithVolumeConfirm(1.2) is a soft filter — 1.2× average volume on the trigger bar — that meaningfully cuts low-conviction reclaims and breakouts. Use 2.0 (alias `VolumeSpike`) for breakout strategies where you want only the loudest bars.
                """),

        new(
            Slug: "entry-support-resistance",
            Category: "5. Entry Verbs",
            Order: 6,
            Title: "IsAtSupport / IsAtResistance",
            Summary: "Price within tolerance% of the recent swing low (support) or high (resistance).",
            BodyMarkdown: """
                **Signatures:** `IsAtSupport(double tolerancePercent = 0.5)` / `IsAtResistance(double tolerancePercent = 0.5)`

                Computed using the recent swing high/low captured by the snapshot builder over the last ~20 bars excluding the current bar (so "at support" doesn't fire on the bar that *makes* the new low). Tolerance defaults to 0.5%.

                [[Stock.Ticker("XOM").IsAtSupport(0.3).IsBullishEngulfing().Long().Build()]]
                """),

        // ───────── 6. ORDER VERBS ─────────
        new(
            Slug: "order-direction",
            Category: "6. Order Verbs",
            Order: 0,
            Title: "Long() / Short()",
            Summary: "Sets the trade direction. Required.",
            BodyMarkdown: """
                **Signatures:** `Long()` / `Short()` — both shorthand for `Order(TradeDirection.X)`.

                Every strategy must declare a direction. Branching strategies (`If/Then/Else`) can have *different* directions per branch — the resolved direction at fire time depends on which branch matched.

                [[Stock.Ticker("SPY").IsAboveVwap().Long().Build()]]
                [[Stock.Ticker("SPY").IsBelowVwap().Short().Build()]]
                """),

        new(
            Slug: "order-quantity",
            Category: "6. Order Verbs",
            Order: 1,
            Title: "Quantity(shares)",
            Summary: "Position size in shares.",
            BodyMarkdown: """
                **Signature:** `Quantity(int shares)`

                Sets a fixed share count. Notional sizing (`$1000` of TSLA) is on the roadmap via `Quantity.Notional($)` — for now use share count.

                [[Stock.Ticker("AAPL").Quantity(100).IsAboveVwap().Long().Build()]]
                """),

        // ───────── 7. RISK VERBS ─────────
        new(
            Slug: "risk-stop-loss",
            Category: "7. Risk Verbs",
            Order: 0,
            Title: "StopLoss(price) / StopLossPercent(percent)",
            Summary: "Where the trade exits at a loss.",
            BodyMarkdown: """
                **Signatures:** `StopLoss(double price)` / `StopLossPercent(double percent)`

                Two forms: a fixed price level, or a percentage below entry (above for shorts). Choose based on what your strategy logic anchors to — if your stop is "below the 31 EMA at fill time", use a fixed price computed from the 31 EMA at signal time; if it's "always 2% under entry", use the percent form.

                [[Stock.Ticker("MSFT").IsAboveVwap().Long().StopLossPercent(2).Build()]]
                """),

        new(
            Slug: "risk-trailing-stop",
            Category: "7. Risk Verbs",
            Order: 1,
            Title: "TrailingStopLoss(percent)",
            Summary: "Stop that ratchets up as the trade moves in your favor.",
            BodyMarkdown: """
                **Signature:** `TrailingStopLoss(double percent)`

                A percentage-based trailing stop that follows the high-water mark for longs (low-water mark for shorts). Useful when you want to ride trends rather than cap profit at a fixed target.

                [[Stock.Ticker("NVDA").IsAboveVwap().IsAboveEma(9).Long().TrailingStopLoss(3).Build()]]
                """),

        // ───────── 8. EXIT VERBS ─────────
        new(
            Slug: "exit-take-profit",
            Category: "8. Exit Verbs",
            Order: 0,
            Title: "TakeProfit(price) / multi-target scale-out",
            Summary: "Where the trade exits at a profit. Single target or multi-target scale.",
            BodyMarkdown: """
                **Signatures:** `TakeProfit(double price)` / `TakeProfit(double t1, double t2, double? t3 = null)` / `TakeProfitPercent(double percent)`

                Single-target form sets one price. Multi-target form scales out: 50/50 if you provide two prices, 33/33/34 if three. The Risk Guardian uses the *first* target for pre-trade risk:reward calculation.

                [[Stock.Ticker("TSLA").IsAboveVwap().Long().StopLoss(240).TakeProfit(260).Build()]]
                """),

        // ───────── 9. BRANCHING ─────────
        new(
            Slug: "branching-overview",
            Category: "9. Branching",
            Order: 0,
            Title: "If / ElseIf / Else expression branching",
            Summary: "Different actions for different conditions, all in one strategy.",
            BodyMarkdown: """
                IdiotScript supports branching via the static `Conditions` catalog and the `.If(...).Then(...).ElseIf(...).Then(...).Else(...)` chain. The first matching branch's overrides apply on top of the base strategy.

                Conditions compose with `.And() / .Or() / .Not()`:

                ```csharp
                using static IdiotProof.Scripting.Conditions;
                Stock.Ticker("SPY")
                    .RequireAdxAbove(20)
                    .If(IsAboveVwap.And(IsEmaAbove(9)))
                        .Then(b => b.Long().StopLossPercent(1).TakeProfitPercent(2))
                    .ElseIf(c => c.IsBelowVwap().IsEmaBelow(9),
                            b => b.Short().StopLossPercent(1).TakeProfitPercent(2))
                    .Else(b => b.Long().TakeProfitPercent(0.5))
                ```

                For the visual renderer, branches show as nested cards under Entry — Then / ElseIf / Else columns each show their condition + override chips.
                """),

        // ───────── 10. EXAMPLES ─────────
        new(
            Slug: "example-9-30-pullback",
            Category: "10. Worked Examples",
            Order: 0,
            Title: "9/30 pullback continuation",
            Summary: "The classic uptrend re-entry: regime → zone → reclaim → volume → long.",
            BodyMarkdown: """
                The textbook continuation pattern for a stock in an uptrend. Wait for a pullback into the 9/30 zone, confirm the trend with the EMA stack, enter on a 9 EMA reclaim with volume confirmation, stop below the 31 EMA, target 2× risk.

                [[Stock.Ticker("NVDA").RequireAdxAbove(20).RequireEmaStack(9, 31).IsBetweenEma(9, 31).OnReclaim(9).WithVolumeConfirm(1.2).Long().StopLoss(450).TakeProfit(485).Build()]]

                The regime filter (ADX > 20) is what keeps this from firing in chop. The EMA stack confirms direction — without it, the same geometry signals a *short* opportunity in a downtrend.
                """),

        new(
            Slug: "example-gap-up-fade",
            Category: "10. Worked Examples",
            Order: 1,
            Title: "Gap-up fade",
            Summary: "Premarket gap exhausts; short the failure of premarket high in RTH.",
            BodyMarkdown: """
                A premarket-driven setup. The stock gaps up on news; the gap stretches premarket; the open fails to hold above the premarket high. Often a clean fade back to VWAP.

                [[Stock.Ticker("AMC").IsGapUp(5).IsBelowVwap().IsRsiOverbought().Short().StopLossPercent(2).TakeProfit(12).Build()]]

                Pair with `IsAtResistance` if you've identified the premarket high as a level — that adds confluence beyond just "the chart looks toppy."
                """),

        new(
            Slug: "example-oversold-bounce",
            Category: "10. Worked Examples",
            Order: 2,
            Title: "Oversold bounce with divergence",
            Summary: "Capitulation low + RSI bullish divergence + bullish reversal candle = bounce.",
            BodyMarkdown: """
                A counter-trend long. Use sparingly and with tight stops — counter-trend trades have lower win rates but high R:R when they work.

                [[Stock.Ticker("META").IsAtSupport(0.5).IsRsiBullishDivergence().IsBullishEngulfing().Long().StopLossPercent(1.5).TakeProfit(295).Build()]]

                The three confluence requirements (support + divergence + reversal candle) make this a high-quality, low-frequency signal.
                """),
    ];
}
