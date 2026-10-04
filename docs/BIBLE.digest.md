AUTHORITATIVE — full detail in docs/BIBLE.md

<!-- generatedFrom: IP-§1,IP-§3,IP-§5,IP-§9 + USER_STORIES status index. Generated 2026-10-03 by tools/codex.ps1. Do not hand-edit. -->

# IdiotProof — Bible Digest (generated)

## The one sentence
IdiotProof turns a plain-English trade idea into a runnable DSL strategy that a 24/7 console
Monitor evaluates against live market data, fires only when every condition matches, an LLM
voter panel approves, and the Risk Guardian clears it — then places the order through the
broker router and manages the position to its exit. The flagship flow is the **Gapper**
([§4.4](#IP-§4)): buy the premarket gap at 4AM, sell it off before the 9:30 bell once momentum
rolls over.

## What it is NOT
- **Not a charting terminal.** It does not stream tick charts for manual discretionary trading;
  the chart/ghost-overlay work is [planned, not built](#IP-§7).
- **Not multi-broker today.** The build is **Alpaca-only** (plus the simulated Sandbox).
  `IBrokerClient` is the seam a future broker would implement; no other adapter exists.
- **Not a direct-to-vendor LLM client.** No feature code calls an Anthropic/OpenAI SDK directly;
  all LLM traffic routes through MindAttic.Legion and all keys resolve through MindAttic.Vault.
- **Not a gate-bypassing autotrader.** The Monitor places orders only through
  `BrokerRouter`/`IBrokerClient` after all three gates clear, never around the Risk Guardian.
  Exit orders are risk-reducing: they skip the LLM panel by design but are always audit-logged.
- **Not an options autotrader.** Options orders are manual and user-initiated on `/options`;
  the DSL, Monitor, `RiskGuardian` and `Conditions` catalog know nothing about options.
- **Not a desktop app.** `IdiotProof.Blazor` (Blazor Server) is the only UI host.
- **Not a research daemon.** `IdiotProof.ResearchScanner` runs one pass and exits; it is fired
  by a Windows Scheduled Task and never shares the Monitor's trading loop.

## The Laws
This bible **inherits** the org-wide laws in
[`MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md) by reference — they are not restated
here. Applicable house laws: whole-number versioning [see HOUSE-LAW-1], soft-disable over
hard-delete [see HOUSE-LAW-2], credentials via MindAttic.Vault [see HOUSE-LAW-3],
provider-agnostic LLMs via MindAttic.Legion [see HOUSE-LAW-4], one engine / many front doors
[see HOUSE-LAW-6], verified definition of done [see HOUSE-LAW-8], `psst` only on explicit
request [see HOUSE-LAW-9].

Project-specific laws below.

### {#IP-LAW-1} Three gates, in order, before any automated fire
A candidate signal fires only if: (1) every strategy condition matches, (2) the LLM voter
quorum explicitly approves, (3) the `RiskGuardian` clears it. Any gate blocks the fire and the
reason is recorded to the audit trail. Every gate fails closed: a condition whose inputs are
absent or whose type is unrecognized blocks; zero votes, abstain-only, unparseable votes or a
below-threshold split block (a vote defaults to Abstain). The LLM gate is skipped only when
voting is off for both the host and the strategy owner, or no Claude key resolves (the owner's,
then the host's). Exits are risk-reducing and skip the LLM
panel but are audit-logged and honor the Risk Guardian kill-switch. User-initiated manual orders
(the Options section) are not automated fires; they are governed by the Paper/Live consent rule
and Live password elevation instead. (Risk gate: `RiskGuardian*` tests; LLM gate:
`LlmVotingServiceTests`, `UserClaudeKeyResolverTests`; condition layer: `ConditionFailClosedTests`.)

### {#IP-LAW-2} Risk Guardian holds the final veto
No order is placed without a stop loss on the correct side, with risk within `MaxLossPerTrade`,
sized so the worst case cannot exceed the limit, within `MinStopLossPercent`/`MaxStopLossPercent`
(default max 10%), within `MaxAccountRiskPercent`, and under the daily-loss circuit breaker. It
can veto regardless of strategy or LLM consensus. Limits are user-editable on the Settings page;
config refreshes on a 2-minute TTL without resetting the daily-loss counter.
(`IdiotProof.Shared/Risk/RiskGuardian.cs`.)

### {#IP-LAW-3} Sandbox is the always-safe default broker
`BrokerRouter` is seeded with `BrokerType.Sandbox` as the active broker and falls back to
Sandbox rather than throwing or silently routing to a live broker. Routing to a real account
needs the owner's explicit opt-in plus a key pair; a missing or undecryptable key falls through
to Sandbox, never into another user's account. Live trading is an explicit opt-in with a
red-outline confirmation. (`IdiotProof.Brokers/BrokerRouter.cs`,
`IdiotProof.Blazor/Services/UserBrokerResolver.cs`.)

### {#IP-LAW-4} The verb catalog is reflected, never hand-listed
`StrategyScriptGenerator` builds the LLM system prompt — and the Learning Center renders its
phase/verb reference — by reflecting on the real `StrategyBuilder` + `Conditions` types so the
documented/prompted DSL can never drift from the code that actually compiles.
(`IdiotProof.Blazor/Services/StrategyScriptGenerator.cs`.)

### {#IP-LAW-5} The Monitor loop survives its own failures
`SupervisedLoop` catches per-tick exceptions, applies capped exponential backoff, resets on the
next success, writes a heartbeat file each tick, and exits cleanly only on cancellation — the
unattended evaluator never dies on a single bad evaluation. (`IdiotProof.Engine/SupervisedLoop.cs`.)

### {#IP-LAW-6} No underscore-prefixed private fields
Private fields use `camelCase` with no leading underscore (project code-style convention).

### {#IP-LAW-7} JSON for static data, SQL Server for runtime state
Static catalogs (gapper profiles, index events, indicator/strategy config) are JSON; runtime
state (strategies, preferences, audit logs, condition progress, trade diary, replays, research
claims, workspaces) is SQL Server. No Python, no YAML.

### {#IP-LAW-8} The canonical strategy is strict JSON; script text is a view
The semantic model (`StrategyDefinition`) serialized as versioned, STRICT JSON
(`Strategy.ScriptJson`, written by `IdiotProof.Scripting/StrategyJson.cs`) is what evaluators
run. Reads fail closed: unknown schema version, condition type, property, wrong-kind or
unrepresentable value → `StrategyJsonException` and the strategy is **quarantined** (visible
reason in ConditionProgress; escalated loudly when it holds a position), never partially
evaluated. IdiotScript text is the human view — generated from the model for display (always
InvariantCulture), and parsed (tolerantly, for now) only for hand-typed input and legacy rows
with no canon. LLM boundaries emit structured JSON against a schema, never DSL text.
Attribution metadata (`Author`, `OriginTranscript`) is not part of the canon. ("Parse, don't
validate"; no shotgun parsing on the money path. Verified by `StrategyJsonTests`.)

### {#IP-LAW-9} Synthetic data never drives a real order
When the market-data feed is Mock, the Monitor refuses every non-Sandbox entry; Mock data pairs
only with the Sandbox broker. Exits (risk-reducing) are still allowed. The guard sits at the
`IBrokerClient`/`IMarketDataFeed` seams so a future broker inherits it.
(`IdiotProof.Monitor/MonitorWorker.cs`.)

### {#IP-LAW-10} One evaluator; SQL is the bus
The Monitor is the only live evaluator and order placer for strategies. UI changes reach it only
through SQL rows, read each tick — no restart, no second evaluation path. Backtest and replay
surfaces call the same evaluator and exit brain (`IndicatorSnapshotBuilder`, conditions,
`GapperExitEvaluator`, `EmaPeriodCollector`) so a backtest cannot run different code than live.
A SQL leader lease keeps at most one Monitor trading per database.

### {#IP-LAW-11} The repository owns strategy-mutation invariants
`StrategyRepository` enforces ownership (`NotOwner`), refuses to deactivate or delete a strategy
holding a position (`PositionOpen`), writes only editor-owned columns on update (never the
Monitor's position bookkeeping), enforces one active gapper per symbol and the 3-gapper cap
against SQL (with a post-write recheck), and deletes a strategy's `ConditionProgress` with it.
No page writes strategy rows around it. (`StrategyRepositoryGuardTests`.)

## Glossary
- **IdiotScript** — the fluent C# DSL (`Stock.Ticker("NVDA").RequireAdxAbove(20)...Build()`) that
  expresses a strategy as six lifecycle phases; the human view of the canonical JSON.
- **Phase** — one of the six fixed stages every strategy walks: Setup, Filters, Entry, Order,
  Risk, Exit. The parser rejects verbs used in the wrong phase.
- **Condition** — a single boolean check (`IsAboveVwap()`, `OnReclaim(9)`) composed with
  `.And()/.Or()/.Not()`. Latch verbs (`Breakout`, `Pullback`, `HoldsAbove`) evaluate live
  against the snapshot's window high/low.
- **Gate** — one of the three pre-fire checks: condition match → LLM voter quorum → Risk Guardian.
- **Risk Guardian** — `IdiotProof.Shared.Risk.RiskGuardian`, the final pre-trade veto.
- **Monitor** — `IdiotProof.Monitor`, the unattended 24/7 console evaluator and executor.
- **SupervisedLoop** — the fault-tolerant tick loop the Monitor runs.
- **Voter panel / Legion** — the multi-LLM quorum (configured in `legion.json`, shipped with both
  hosts) that approves or rejects a Claude-generated script / a candidate fire, via MindAttic.Legion.
- **ConditionProgress** — the SQL row (`N/M`, first failing verb) the Monitor upserts per tick
  and the Strategies page polls for live badges.
- **Quarantine** — a strategy whose canon cannot be fully understood; never evaluated, reason shown.
- **Sandbox broker** — the always-registered simulated broker (instant fills into an in-memory
  position book) that is the safe default in `BrokerRouter`.
- **BrokerMode** — a strategy's own routing choice: Paper, Live or Sandbox.
- **Trade diary** — the `TradeDiary` table: one row per trade lifecycle (entry, risk plan, exit,
  realized P&L, paper/live), a permanent record that survives strategy deletion.
- **Gapper** — a stock gapping up in premarket vs the previous close; the flagship trade:
  buy in the 4AM window, sell before the 9:30 bell.
- **Gapper profile** — the dialable template (gap %, volume ratio, price band, entry window,
  stops, giveback, arm/sell-by times, notional) in `wwwroot/data/gapper-profiles.json`; cloned
  and tuned per ticker on the `/gapper` tab.
- **Peak giveback** — the momentum-rollover exit: sell once price gives back N% of the run
  from entry to the post-entry peak; armed from a configured ET time ("the last 15 minutes").
- **Previous close** — the prior trading day's official close; the reference for gap %.
  Gap conditions fail closed without it.
- **Replay** — re-running a strategy over a past session's bars with the live evaluator; stored
  as a `ReplayRun` with `ReplayTrade`/`ReplayBar` feature rows.
- **Strategy family** — a built-in replay template (`momentum`, `reversal`, `emabreak`,
  `rthdrive`, `rsireversal`, `swingreversal`, `shortfade`) used when a ticker has no saved strategy.
- **Research claim** — one `ResearchClaim` row: a catalyst or portent extracted from a filing,
  news article, or regulatory notice, with sentiment/magnitude/timing and a significance score.
- **Macro claim** — a `ResearchClaim` with `IsMacro = true`: a regulatory/exchange-rule event
  that isn't about one company (`Ticker` blank; affected tickers, when resolvable, live in
  `AffectedTickersJson`).
- **Significance score** — the 0-100 value `SignificanceScorer` computes per claim (magnitude ×
  confidence, historical correlation strength, source trust, recency, watchlist boost); the
  Research tab's ranked feed sorts by it.
- **Tracked ticker** — a cached row in `TrackedTicker` (symbol, exchange, latest price) forming
  the research scanner's ticker universe; refreshed daily from Alpaca's asset list.
- **OCC symbol** — the exchange-standard option identifier (`BE251219C00038000` = BE, 2025-12-19,
  Call, $38.00): root + `YYMMDD` + `C|P` + strike×1000 zero-padded to 8. Self-describing; decoded
  by `OptionContract.ParseOcc`.
- **Intrinsic value** — what an option is worth if exercised right now: `max(0, S−K)` for a call,
  `max(0, K−S)` for a put. The "real" part of the premium.
- **Extrinsic value (hype / time value)** — premium minus intrinsic: what the market pays for the
  *possibility* of a move. Inflates when the stock runs, decays to zero by expiration. Selling the
  contract while it is high is the trade; it is what evaporates if you wait for reality.
- **Breakeven** — the underlying price at expiration where the trade nets zero (`K + premium` for
  a call, `K − premium` for a put). Only matters if you hold to expiration.
- **DTE** — calendar days to expiration.
- **IV (implied volatility)** — the volatility that makes the Black-Scholes price equal the live
  premium; supplied by Alpaca's snapshots when available, otherwise solved locally and badged `Model`.
- **Options trading level** — Alpaca's per-account permission (`options_trading_level`): 0 none,
  1 covered calls / cash-secured puts, 2 long calls/puts, 3 spreads.
- **Sell signal** — `SellSignalEvaluator`'s informational nudge on an open long option: extrinsic
  value within 5% of its observed high (after at least 3 samples) **and** a Bullish research
  claim on the underlying in the last 7 days → "consider taking profit". Never places anything.
- **Index event** — a `ResearchClaim` with `ClaimType = "IndexEvent"`: an announced S&P 500/100
  addition or deletion logged in `wwwroot/data/sp-index-events.json`, Pending until effective.

## Status index (USER_STORIES.md)
- done: 51
- partial: 39
- planned: 14
