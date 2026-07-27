AUTHORITATIVE — full detail in docs/BIBLE.md

<!-- generatedFrom: IP-§1,IP-§3,IP-§5,IP-§9 + USER_STORIES status index. Generated 2026-07-26 by tools/codex.ps1. Do not hand-edit. -->

# IdiotProof — Bible Digest (generated)

## The one sentence
IdiotProof turns a plain-English trade idea into a runnable DSL strategy that a 24/7 console
Monitor evaluates against live market data, fires only when every condition matches, an LLM
voter panel approves, and the Risk Guardian clears it — then places the order through the
broker router and manages the position to its exit. The flagship flow is the **Gapper**
([IP-A8](AMENDMENTS.md#IP-A8)): buy the premarket gap at 4AM, sell it off before the 9:30 bell
once momentum rolls over.

## What it is NOT
- **Not a charting terminal.** It does not stream tick charts for manual discretionary trading;
  the chart/ghost-overlay work is [planned, not built](#IP-§7).
- **Not multi-broker today.** The active build is **Alpaca-only**. The IBKR adapter
  (`IdiotProof.Brokers.Ibkr/`) is dormant and is **not** in `IdiotProof.slnx`.
- **Not a direct-to-vendor LLM client.** No feature code calls an Anthropic/OpenAI SDK directly;
  all LLM traffic routes through MindAttic.Legion and all keys resolve through MindAttic.Vault.
- **Not a gate-bypassing autotrader.** The Monitor DOES place orders (since
  [IP-A8](AMENDMENTS.md#IP-A8)) — but only through `BrokerRouter`/`IBrokerClient` after all
  three gates clear, never around the Risk Guardian. Exit orders are risk-reducing: they skip
  the LLM panel by design but are always audit-logged. *(Supersedes the pre-IP-A8 "emits
  signals only" phrasing.)*
- **Not a `IdiotProof.Core`/`IdiotProof.Web` monolith.** Earlier docs (README §, the
  `.github/copilot-instructions.md`) describe a `Core`/`Web` split and an IBKR-first engine.
  That is **historical/aspirational** — see [IP-A1](AMENDMENTS.md#IP-A1) for the reconciliation
  to the real, shipped project graph below.

## The Laws
This bible **inherits** the org-wide laws in
[`MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md) by reference — they are not restated
here. Applicable house laws: whole-number versioning [see HOUSE-LAW-1], soft-disable over
hard-delete [see HOUSE-LAW-2], credentials via MindAttic.Vault [see HOUSE-LAW-3],
provider-agnostic LLMs via MindAttic.Legion [see HOUSE-LAW-4], one engine / many front doors
[see HOUSE-LAW-6], verified definition of done [see HOUSE-LAW-8], `psst` only on explicit
request [see HOUSE-LAW-9].

Project-specific laws below.

### {#IP-LAW-1} Three gates, in order, before any fire
A candidate signal fires only if: (1) every strategy condition matches, (2) the LLM voter
quorum approves, (3) the `RiskGuardian` clears it. Any gate blocks the fire and the reason is
recorded to the audit trail. (Verified at the Risk gate by the `RiskGuardian*` tests; the LLM
gate lives in `IdiotProof.Blazor/Services/LlmVotingService.cs`.)

### {#IP-LAW-2} Risk Guardian holds the final veto
No order is placed without a stop loss, with risk within `MaxLossPerTrade`, sized so the
worst case cannot exceed the limit, within `MinStopLossPercent`/`MaxStopLossPercent`, within
`MaxAccountRiskPercent`, and under the daily-loss circuit breaker. It can veto regardless of
strategy or LLM consensus. (`IdiotProof.Shared/Risk/RiskGuardian.cs`.)

### {#IP-LAW-3} Sandbox is the always-safe default broker
`BrokerRouter` is seeded with `BrokerType.Sandbox` as the active broker and falls back to
Sandbox rather than throwing or silently routing to a live broker. Live trading is an explicit
opt-in. (`IdiotProof.Brokers/BrokerRouter.cs`.)

### {#IP-LAW-4} The verb catalog is reflected, never hand-listed
`StrategyScriptGenerator` builds the LLM system prompt by reflecting on the real
`StrategyBuilder` + `Conditions` types so the documented/prompted DSL can never drift from the
code that actually compiles. (`IdiotProof.Blazor/Services/StrategyScriptGenerator.cs`.)

### {#IP-LAW-5} The Monitor loop survives its own failures
`SupervisedLoop` catches per-tick exceptions, applies capped exponential backoff, resets on the
next success, writes a heartbeat file each tick, and exits cleanly only on cancellation — the
unattended evaluator never dies on a single bad evaluation. (`IdiotProof.Engine/SupervisedLoop.cs`.)

### {#IP-LAW-6} No underscore-prefixed private fields
Private fields use `camelCase` with no leading underscore (project code-style convention).

### {#IP-LAW-7} JSON for static data, SQL Server for runtime state
Static catalogs (watchlists, indicator/strategy config, ticker profiles) are JSON; runtime
state (strategies, preferences, audit logs, condition progress) is SQL Server. No Python, no YAML.

### {#IP-LAW-8} The canonical strategy is strict JSON; script text is a view
The semantic model (`StrategyDefinition`) serialized as versioned, STRICT JSON
(`Strategy.ScriptJson`, written by `IdiotProof.Scripting/StrategyJson.cs`) is what evaluators
run. Reads fail closed: unknown schema version, condition type, or property →
`StrategyJsonException` and the strategy is **quarantined** (visible reason in
ConditionProgress), never partially evaluated. IdiotScript text is the human view — generated
from the model for display, and parsed (tolerantly, for now) only for hand-typed input and
legacy rows with no canon. LLM boundaries emit structured JSON against a schema, never DSL
text. ("Parse, don't validate"; no shotgun parsing on the money path — see
[IP-A13](AMENDMENTS.md#IP-A13). Verified by `StrategyJsonTests`.)

## Glossary
- **IdiotScript** — the fluent C# DSL (`Stock.Ticker("NVDA").RequireAdxAbove(20)...Build()`) that
  expresses a strategy as six lifecycle phases.
- **Phase** — one of the six fixed stages every strategy walks: Setup, Filters, Entry, Order,
  Risk, Exit. The parser rejects verbs used in the wrong phase.
- **Condition** — a single boolean check (`IsAboveVwap()`, `OnReclaim(9)`) composed with
  `.And()/.Or()/.Not()`.
- **Gate** — one of the three pre-fire checks: condition match → LLM voter quorum → Risk Guardian.
- **Risk Guardian** — `IdiotProof.Shared.Risk.RiskGuardian`, the final pre-trade veto.
- **Monitor** — `IdiotProof.Monitor`, the unattended 24/7 console evaluator.
- **SupervisedLoop** — the fault-tolerant tick loop the Monitor runs.
- **Voter panel / Legion** — the multi-LLM quorum (configured in `legion.json`) that approves
  or rejects a Claude-generated script / a candidate fire, via MindAttic.Legion.
- **ConditionProgress** — the SQL row (`N/M`, first failing verb) the Monitor upserts per tick
  and the Strategies page polls for live badges.
- **Sandbox broker** — the always-registered simulated broker (instant fills into an in-memory
  position book) that is the safe default in `BrokerRouter`.
- **Gapper** — a stock gapping up in premarket vs the previous close; the flagship trade:
  buy in the 4AM window, sell before the 9:30 bell.
- **Gapper profile** — the dialable template (gap %, volume ratio, price band, entry window,
  stops, giveback, arm/sell-by times, notional) in `wwwroot/data/gapper-profiles.json`; cloned
  and tuned per ticker on the `/gapper` tab.
- **Peak giveback** — the momentum-rollover exit: sell once price gives back N% of the run
  from entry to the post-entry peak; armed from a configured ET time ("the last 15 minutes").
- **Previous close** — the prior trading day's official close; the reference for gap %.
  Gap conditions fail closed without it.
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

## Status index (USER_STORIES.md)
- done: 37
- partial: 23
- planned: 27
- cut: 1

## Latest amendment
IP-A6 — Learning Center + Backtest UI enhancement planned {#IP-A6}
