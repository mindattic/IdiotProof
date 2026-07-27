---
codex: 1
project: IdiotProof
code: IP
layer: bible
status: living
updated: 2026-07-19
---

# IdiotProof — Project Bible
> Single source of truth for what IdiotProof IS, is NOT, and the rules that keep it coherent.
> README says how to build/run; this says how to think about the system.

## 1. The one sentence {#IP-§1}
IdiotProof turns a plain-English trade idea into a runnable DSL strategy that a 24/7 console
Monitor evaluates against live market data, fires only when every condition matches, an LLM
voter panel approves, and the Risk Guardian clears it — then places the order through the
broker router and manages the position to its exit. The flagship flow is the **Gapper**
([IP-A8](AMENDMENTS.md#IP-A8)): buy the premarket gap at 4AM, sell it off before the 9:30 bell
once momentum rolls over.

## 2. The product promise {#IP-§2}
- **Describe, don't code.** A trader writes prose ("if NVDA pulls back to the 9 EMA in an
  uptrend with volume confirmation, go long with a 1% stop"); Claude (via the Legion voter
  panel) translates it into **IdiotScript**, the project's fluent DSL. The verb catalog is
  produced by *reflecting* on the real `StrategyBuilder` + `Conditions` types, so a model can
  never invent syntax that does not compile.
- **Set and forget.** `IdiotProof.Monitor` is an unattended console host that re-reads every
  active strategy from SQL each tick (default 5s — UI edits apply to the running console
  automatically), evaluates against live Alpaca data (websocket stream + REST, Mock fallback),
  reports per-condition progress (`4/5 — waiting on OnReclaim(9)`), places gated entries, and
  manages open positions to their exit.
- **The Gapper, done well.** Queue up to 3 tickers on the `/gapper` tab, each with a dialable
  profile (gap %, volume, price band, entry window, stops, peak-giveback, sell-by). All gappers
  are not the same — every value is per-ticker adjustable; the tuned result is denormalized
  into the strategy's script so what you dialed is exactly what runs.
- **Three gates before money moves.** All strategy conditions match → LLM voter quorum
  approves → Risk Guardian clears stop/daily-loss/per-trade-risk. Any gate blocks the fire and
  records the reasoning.
- **Paper by default, live by explicit opt-in.** The Sandbox broker is always the safe
  fallback; live trading requires explicit configuration plus a confirmation modal.

## 3. What it is NOT {#IP-§3}
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

## 4. Architecture canon {#IP-§4}

```
                          Trader (browser)
                                 │
                  ┌──────────────▼───────────────┐        Cypress E2E
                  │       IdiotProof.Blazor       │◄──────  Cypress E2E (tests/IdiotProof.Cypress, 7 specs)
                  │  Strategies · StrategyBuilder │
                  │  (Guided/Script/Describe)     │──► StrategyScriptGenerator ──► Legion voter panel
                  │  Learning Center · Settings   │                                (legion.json, high tier)
                  └──────────────┬────────────────┘
                                 │
                  ┌──────────────▼────────────────┐
                  │        IdiotProof.Engine       │  DI root · AppSettings overlay ·
                  │  SupervisedLoop · AuditLogger  │  WorkspaceManager · ServiceRegistration
                  └──┬───────────┬──────────┬──────┘
                     │           │          │
        ┌────────────▼─┐  ┌──────▼─────┐ ┌──▼───────────────┐
        │ IdiotProof.  │  │ IdiotProof.│ │ IdiotProof.      │
        │ Scripting    │  │ Strategies │ │ DataFeeds        │
        │ (IdiotScript │  │ IStrategy· │ │ Polygon / Mock / │
        │  DSL: Stock. │  │ DslStrategy│ │ Switchable       │
        │  Ticker(…))  │  │ Backtester │ └──────────────────┘
        └──────┬───────┘  └──────┬─────┘
               │                 │
        ┌──────▼─────┐    ┌───────▼────────┐    ┌────────────────────────┐
        │ IdiotProof.│    │ IdiotProof.    │    │ IdiotProof.Brokers     │
        │ Indicators │    │ Shared         │    │ IBrokerClient ·        │
        │ RSI EMA ATR│    │ RiskGuardian · │    │ Alpaca · Sandbox       │
        │ MACD VWAP …│    │ IndicatorSnap. │    │ (BrokerRouter)         │
        └────────────┘    └────────────────┘    └────────────────────────┘
               │                 │
        ┌──────▼─────────────────▼──────┐        ┌───────────────────────────┐
        │       IdiotProof.Models       │        │   IdiotProof.Monitor      │
        │  Candle TradeSignal Position  │◄───────┤   24/7 SupervisedLoop     │
        │  TradeSetup RiskLimits …      │        │   (console host)          │
        └───────────────────────────────┘        └───────────────────────────┘
```

> **Scope note.** This canon describes the projects in `IdiotProof.slnx`. All formerly dormant
> out-of-solution trees (`IdiotProof.Core`, `IdiotProof.Cli`, `IdiotProof.Brokers.Ibkr`,
> `tests/IdiotProof.NUnitTests`, `IdiotProof.Scripting.Tests`, `src/`) were deleted 2026-06-07
> per [IP-A2](AMENDMENTS.md#IP-A2); recoverable from git history if needed.

### 4.1 Projects (in `IdiotProof.slnx`)
| Project | Role |
|---|---|
| `IdiotProof.Blazor` | Blazor Server web app — Strategies page, Strategy Builder (Guided/Script/Describe), Learning Center, Backtest, Settings, API Keys. **MindAttic.Authentication** (Argon2id, sessions, MFA scaffolding) + EF Core 10 (SQL Server). |
| `IdiotProof.Monitor` | **The one pipeline** (IP-A8/A9): console host on `SupervisedLoop` (Windows-Service-installable, single-instance `sp_getapplock` leader lease) — re-reads active strategies every tick, evaluates conditions, upserts `ConditionProgress`, walks the three gates, places entries per-user via `UserBrokerResolver` (owner's own Alpaca when keyed, Sandbox-default router otherwise; limit + extended-hours premarket), manages open positions to exit via `GapperExitEvaluator`, feeds realized P&L into the RiskGuardian daily breaker. |
| `IdiotProof.Engine` | DI root (`ServiceRegistration`), `AppSettings` overlay chain, `SupervisedLoop`, `AuditLogger`, `WorkspaceManager` (UI layout state only). |
| `IdiotProof.Scripting` | The IdiotScript DSL: `Stock.Ticker(...)`, `StrategyBuilder`, the `Conditions` catalog, `ScriptParser`, branching algebra, `GapperProfile` + `GapperScriptFactory`, `MarketTime` (ET clock). |
| `IdiotProof.Strategies` | `IStrategy` + `DslStrategy` adapter + `IndicatorSnapshotBuilder` + `GapperExitEvaluator` (sell-off brain) + `StrategyBacktester`/`BacktestReport`. |
| `IdiotProof.Indicators` | Pure indicator math: ADX, ATR, Bollinger, CCI, EMA, MACD, OBV, RSI, SMA, Stochastic, VWAP, WilliamsR, `CandlestickPatterns`. |
| `IdiotProof.DataFeeds` | `IMarketDataFeed` (+ `GetPreviousCloseAsync` for gap math): `AlpacaDataFeed` (REST, sip→iex auto-downgrade), `AlpacaStreamingClient` (websocket trades + minute bars), `PolygonDataFeed`, `MockDataFeed` (deterministic premarket-gap simulation), `SwitchableMarketDataFeed`. |
| `IdiotProof.Brokers` | `IBrokerClient` + `AlpacaBrokerClient` + `SandboxBrokerClient` + `BrokerRouter`. |
| `IdiotProof.Models` | Domain DTOs/enums (the nouns, see 4.2). |
| `IdiotProof.Shared` | `RiskGuardian` + `RiskGuardianConfig`/`Result`, `IndicatorSnapshot`, `LogMessage`, `SettingsMetadata`. |
| `IdiotProof.ResearchScanner` | One-shot, Scheduled-Task-fired console app (IP-A32 / RFC 0003) — sweeps EDGAR/Alpaca/Federal-Register for market-moving events across the tracked ticker universe, scores significance, writes to the shared DB. Not a daemon; not part of the Monitor's trading loop. |

### 4.2 Domain model — the NOUNS (`IdiotProof.Models`, `IdiotProof.Shared`)
- `Candle` — one OHLCV bar.
- `TradeSignal` — output of `IStrategy.Evaluate`; a candidate to fire.
- `TradeSetup` / `RiskLimits` — decimal-priced inputs the `RiskGuardian` validates.
- `OrderRequest` / `OrderResult` / `Position` — broker-facing order lifecycle.
- `StrategyDefinition` (in `IdiotProof.Scripting`) — parsed IdiotScript: phases + conditions + branches.
- Enums: `TradeDirection`, `TradingSession`, `OrderType`, `OrderSide`, `PriceType`,
  `ConfidenceGrade`, `BrokerType {Alpaca, Sandbox}`, `FeedType {Polygon}`,
  `StrategyType {Iti, BreakoutPullback, LowHigh, FluentDsl, Custom}`, `WorkspaceState`.

### 4.3 Key services — the VERBS
- `Stock.Ticker(symbol)` → `StrategyBuilder` (`IdiotProof.Scripting`) — entry point to author IdiotScript.
- `ScriptParser` / `StrategyDefinition` — text ↔ object model.
- `IStrategy.Evaluate(symbol, candles, context)` → `IReadOnlyList<TradeSignal>` (`IdiotProof.Strategies`).
- `DslStrategy` — adapts a parsed `StrategyDefinition` into an `IStrategy`.
- `IndicatorSnapshotBuilder.Build(...)` → `IndicatorSnapshot` consumed by condition evaluation.
- `StrategyBacktester.Run(...)` → `BacktestReport` (`IdiotProof.Strategies/Backtesting`).
- `RiskGuardian.ValidateTrade(setup, ...)` → `RiskGuardianResult` (the final gate, `IdiotProof.Shared/Risk`);
  `RecordTradePnL(realized)` — the Monitor feeds every exit into the daily circuit breaker.
- `SupervisedLoop.RunAsync(options, ct)` — fault-tolerant tick loop with backoff + heartbeat file.
- `IBrokerClient.PlaceOrderAsync(...)` via `BrokerRouter` (Sandbox is the always-registered fallback;
  the Monitor's `Program.cs` is the ONE construction site — IP-A8).
- `GapperScriptFactory.ToScript(symbol, profile)` — tuned profile → round-trip-safe IdiotScript.
- `GapperExitEvaluator.Evaluate(def, entry, entryUtc, candles, now)` — sell-by / stops /
  take-profit / peak-giveback verdict for a held position (pure, clock-free, unit-tested).
- `IMarketDataFeed.*` — Alpaca (REST + websocket stream), Polygon, Mock (deterministic gap
  simulation), Switchable; `GetPreviousCloseAsync` supplies gap math's reference close.
- **Research subsystem** (IP-A32 / RFC 0003, `IdiotProof.Blazor/Services`): `TickerUniverseService`
  (cached NASDAQ/NYSE universe), `EdgarService` (SEC filings + real document fetch),
  `Form4Parser` (real insider-transaction magnitude), `CorporateActionDetector` (8-K item-code
  triage), `RegulatoryScanner` (Federal Register SRO notices → macro claims), `CatalystExtractor`
  (LLM extraction, sober-tone sentence composition), `OutcomeBackfillService` (fetches real
  price history to mark claims Realized/Disproven — what actually calibrates the score against
  reality), `SignificanceScorer` (0-100 ranking), `ResearchService` (orchestration + queries).
  `IdiotProof.ResearchScanner` is the scheduled driver; `/research` (`Research.razor`) is the
  read-mostly ranked-feed view.

## 5. The Laws {#IP-§5}
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

## 6. Verified state {#IP-§6}
Build/test evidence (recorded 2026-07-19, .NET 10 SDK, `IdiotProof.slnx`):

- **Build:** `dotnet build IdiotProof.slnx -c Debug` → **Build succeeded**, 0 errors,
  0 warnings.
- **Tests:** **all green, 168 passed / 0 failed** across the five solution test projects
  (build is **warning-free** as of IP-A11):
  - `IdiotProof.Engine.Tests` — 29 passed (RiskGuardian gate + SupervisedLoop resilience +
    `RecordTradePnL` day-rollover regression, IP-A10 + `UpdateConfig` limit-swap-preserves-
    daily-loss regression, IP-A16; WorkspaceManager cache-hydration + seed-once concurrency
    suite, `WorkspaceManagerTests`, IP-A15).
  - `IdiotProof.Indicators.Tests` — 18 passed (RSI/EMA/ATR/MACD/VWAP math + ADX Wilder-seed
    regression, `AdxTests`).
  - `IdiotProof.Strategies.Tests` — 58 passed (DSL round-trip incl. Name survival, backtester
    incl. ET time-exit regression, gapper profile factory + gap conditions + entry window +
    momentum-rollover exits + arm/sell-by cross-validation, `GapperTests`; full mock-gap-day
    lifecycle + previous-close date-comparison regression, `GapperLifecycleTests`; canonical
    JSON round-trip + fail-closed + loader-quarantine suite, `StrategyJsonTests`, IP-LAW-8;
    day-replay + giveback grid + tuned-profile suite incl. idempotent "(tuned)" suffix,
    `GapperDayBacktesterTests`, IP-A14/IP-A15; canonical EMA-period walk incl. ConditionalBlock
    coverage, `EmaPeriodCollectorTests`, and full multi-target scale-out ladder in live
    signals, `DslStrategySignalTests`, IP-A15; weekend/ET-rollover trading-day gate,
    `MarketTimeTests`, IP-A16).
  - `IdiotProof.Brokers.Tests` — 13 passed (BrokerRouter Sandbox default + sandbox fill
    simulation + Alpaca extended-hours contract).
  - `IdiotProof.Blazor.Tests` — 50 passed (StrategyScriptGenerator verb-catalog reflection +
    LlmVotingService consensus logic + JSON vote parsing incl. fail-closed-to-Abstain
    regressions (IP-A11) and the Abstain-default consensus pin (IP-A16) +
    ConditionProgressRepository upsert/read integration tests against SQL Server LocalDB +
    guarded strategy mutators (ownership + open-position refusal + per-symbol active count),
    `StrategyRepositoryGuardTests`, IP-A16 + per-user broker routing rule,
    `UserBrokerResolverTests` + the transcript→gapper extraction contract,
    `GapperInterpreterTests`, IP-A12 + the Legion provider-id/model-catalog canary,
    `LegionProviderContractTests`, IP-A15).

Proven-working subsystems: the Risk Guardian gate, the SupervisedLoop fault-tolerance, the core
indicator math, IdiotScript build/round-trip, the DSL backtester, BrokerRouter Sandbox-first
routing ([IP-LAW-3](BIBLE.md#IP-LAW-3)), the LLM system-prompt verb-catalog reflection law
([IP-LAW-4](BIBLE.md#IP-LAW-4)), the SQL-backed `SqlWorkspaceStore` (registered in the Blazor
host before the engine; one-shot JSON import on first user load), and
`ConditionProgressRepository` upsert/read (integration tests against SQL Server LocalDB; insert
on first call, update on second, full-pass clears verb, zero-condition fast path, two strategies
track independently). See [USER_STORIES.md](USER_STORIES.md) for per-capability test citations.

Not proven by the solution build/test: the Blazor UI flows and the LLM voting round-trip. The
Cypress suite (`tests/IdiotProof.Cypress/`) has 7 specs (02–07, see [IP-A4](AMENDMENTS.md#IP-A4));
they run deterministically with `IDIOTPROOF_FAKE_LLM=1` (the `FakeLlmHandler` test seam
registered in `Program.cs` intercepts Legion calls server-side) but need a live server run to
graduate stories E1–E6 to ✅.

## 7. Active frontier {#IP-§7}
- **Gapper hardening (Epic K tail)** — full-day integration test through the Monitor
  (mock gap day: queue → 4AM fire → hold → rollover sell), `/gapper` Cypress spec, short-side
  position management, fill-price reconciliation against the broker's actual fill (entry is
  recorded at the limit price today).
- **Audit debts (2026-07-18 audit, deliberately deferred)** — `LlmVotingService` still
  hand-rolls a 3-persona Claude-only panel instead of Legion's native voter-panel API
  (legion.json declares claude/openai/gemini/deepseek); DSL generation is single-shot;
  per-user **Claude** keys are not merged in the Monitor (broker keys ARE per-user since
  [IP-A9](AMENDMENTS.md#IP-A9)); the Settings page still doesn't expose RiskGuardian config
  (`SetRiskConfigAsync` uncalled); the write-only `OpenStrategyTabs` CSV and unused
  `SettingsKv` table await either a consumer or deletion; Azure Blob + Key Vault key-ring
  protection is the upgrade from the file-system `DataProtection:KeyRingPath` once the
  Azure infra (MindAttic.Deploy `idiotproof-web`) is provisioned.
- **Learning Center** — `/learn` in-app documentation hub: workflow overview diagram, six-phase
  walkthrough, live reflected verb catalog, three-gates explanation + diagram, annotated sample
  strategies. Verb catalog and phase reference rendered from live reflection (same path as
  `StrategyScriptGenerator`, [IP-LAW-4](BIBLE.md#IP-LAW-4)). (Epic I in the stories, all ⬜.)
- **Backtest UI enhancement** — full-depth backtest: fetch a day of historical candles from
  Alpaca/Polygon, evaluate the strategy tick-by-tick via `StrategyBacktester.Run()`, render a
  per-candle condition table (pass/fail per condition) and hypothetical P&L. Enhances the existing
  `BacktestReport` pipeline in `IdiotProof.Strategies` and the stub `Backtest.razor` UI. (Epic J
  in the stories, all ⬜. Stub wired in IP-US-E6.)
- **Strategy ghost overlay + branching visualization** — see `TODO.md`: chart integration,
  simulator timeline, branch fork rendering. (Epic G in the stories, all ⬜.)
- **Roslyn-based IdiotScript parser** — replace the tolerant regex parser with exact
  line/col diagnostics. (IP-US-H1.)
- **Cypress CI run** — 7 specs (02–07) cover IP-US-E1–E6; all run deterministically with
  `IDIOTPROOF_FAKE_LLM=1`. Run `npm run cypress:run` (or open the Cypress GUI) against a live
  server to graduate E1–E6 to ✅. See [IP-A4](AMENDMENTS.md#IP-A4).

## 8. Quality bar {#IP-§8}
A feature is **done** (`✅`) only when: it builds clean in `IdiotProof.slnx`; it has a green
automated test (NUnit/xUnit for backend, Cypress for UI) that is named in
[USER_STORIES.md](USER_STORIES.md); user-facing changes have an e2e or lifecycle assertion;
and it respects the laws in §5 (gates in order, Sandbox default, Vault/Legion routing, no
underscore fields). Anything not proven by a test is `🟡`/`⬜`. (Inherits [HOUSE-LAW-8].)

## 9. Glossary {#IP-§9}
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
