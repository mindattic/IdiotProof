---
codex: 1
project: IdiotProof
code: IP
layer: bible
status: living
updated: 2026-06-08
---

# IdiotProof — Project Bible
> Single source of truth for what IdiotProof IS, is NOT, and the rules that keep it coherent.
> README says how to build/run; this says how to think about the system.

## 1. The one sentence {#IP-§1}
IdiotProof turns a plain-English trade idea into a runnable DSL strategy that a 24/7 console
Monitor evaluates against live market data and fires only when every condition matches, an
LLM voter panel approves, and the Risk Guardian clears it.

## 2. The product promise {#IP-§2}
- **Describe, don't code.** A trader writes prose ("if NVDA pulls back to the 9 EMA in an
  uptrend with volume confirmation, go long with a 1% stop"); Claude (via the Legion voter
  panel) translates it into **IdiotScript**, the project's fluent DSL. The verb catalog is
  produced by *reflecting* on the real `StrategyBuilder` + `Conditions` types, so a model can
  never invent syntax that does not compile.
- **Set and forget.** `IdiotProof.Monitor` is an unattended console host that loads every
  active strategy and evaluates it on a fixed cadence, reporting per-condition progress
  (`4/5 — waiting on OnReclaim(9)`).
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
- **Not an autotrader that places its own orders.** The Monitor *emits signals*; order
  placement flows through the `IBrokerClient` abstraction behind the gates — it never bypasses
  the Risk Guardian.
- **Not a `IdiotProof.Core`/`IdiotProof.Web` monolith.** Earlier docs (README §, the
  `.github/copilot-instructions.md`) describe a `Core`/`Web` split and an IBKR-first engine.
  That is **historical/aspirational** — see [IP-A1](AMENDMENTS.md#IP-A1) for the reconciliation
  to the real, shipped project graph below.

## 4. Architecture canon {#IP-§4}

```
                          Trader (browser)
                                 │
                  ┌──────────────▼───────────────┐        Cypress E2E
                  │       IdiotProof.Blazor       │◄──────  (tests/IdiotProof.Cypress, planned-status)
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
| `IdiotProof.Blazor` | Blazor Server web app — Strategies page, Strategy Builder (Guided/Script/Describe), Learning Center, Settings, API Keys. ASP.NET Identity + EF Core (SQL Server). |
| `IdiotProof.Monitor` | Console host running `SupervisedLoop` — loads active strategies, evaluates per-condition on a cadence, upserts `ConditionProgress`. |
| `IdiotProof.Engine` | DI root (`ServiceRegistration`), `AppSettings` overlay chain, `SupervisedLoop`, `AuditLogger`, `WorkspaceManager` (JSON-on-disk). |
| `IdiotProof.Scripting` | The IdiotScript DSL: `Stock.Ticker(...)`, `StrategyBuilder`, the `Conditions` catalog, `ScriptParser`, branching algebra. |
| `IdiotProof.Strategies` | `IStrategy` + `DslStrategy` adapter + `StrategyRegistry` + `IndicatorSnapshotBuilder` + `StrategyBacktester`/`BacktestReport`. |
| `IdiotProof.Indicators` | Pure indicator math: ADX, ATR, Bollinger, CCI, EMA, MACD, OBV, RSI, SMA, Stochastic, VWAP, WilliamsR, `CandlestickPatterns`. |
| `IdiotProof.DataFeeds` | `IMarketDataFeed`: `PolygonDataFeed`, `MockDataFeed`, `SwitchableMarketDataFeed`. |
| `IdiotProof.Brokers` | `IBrokerClient` + `AlpacaBrokerClient` + `SandboxBrokerClient` + `BrokerRouter`. |
| `IdiotProof.Models` | Domain DTOs/enums (the nouns, see 4.2). |
| `IdiotProof.Shared` | `RiskGuardian` + `RiskGuardianConfig`/`Result`, `IndicatorSnapshot`, `LogMessage`, `SettingsMetadata`. |

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
- `RiskGuardian.ValidateTrade(setup, ...)` → `RiskGuardianResult` (the final gate, `IdiotProof.Shared/Risk`).
- `SupervisedLoop.RunAsync(options, ct)` — fault-tolerant tick loop with backoff + heartbeat file.
- `IBrokerClient.PlaceOrderAsync(...)` via `BrokerRouter` (Sandbox is the always-registered fallback).
- `IMarketDataFeed.*` — Polygon (live), Mock (deterministic), Switchable (runtime selectable).

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

## 6. Verified state {#IP-§6}
Build/test evidence (recorded 2026-06-08, .NET 10 SDK, `IdiotProof.slnx`):

- **Build:** `dotnet build IdiotProof.slnx -c Debug` → **Build succeeded**, 0 errors, 4 nullability
  warnings (pre-existing: CS8619 in `IdiotProof.DataFeeds/MockDataFeed.cs`, CS8629 ×3 in
  `IdiotProof.Scripting/IdiotScript.cs`).
- **Tests:** `dotnet test IdiotProof.slnx -c Debug` → **all green, 90 passed / 0 failed** across the
  five solution test projects:
  - `IdiotProof.Engine.Tests` — 22 passed (RiskGuardian gate + SupervisedLoop resilience).
  - `IdiotProof.Indicators.Tests` — 15 passed (RSI/EMA/ATR/MACD/VWAP math).
  - `IdiotProof.Strategies.Tests` — 16 passed (DSL round-trip, backtester, registry).
  - `IdiotProof.Brokers.Tests` — 8 passed (BrokerRouter Sandbox default + safe fallback).
  - `IdiotProof.Blazor.Tests` — 29 passed (StrategyScriptGenerator verb-catalog reflection +
    LlmVotingService consensus logic + JSON vote parsing + ConditionProgressRepository
    upsert/read integration tests against SQL Server LocalDB).

Proven-working subsystems: the Risk Guardian gate, the SupervisedLoop fault-tolerance, the core
indicator math, IdiotScript build/round-trip, the DSL backtester, BrokerRouter Sandbox-first
routing ([IP-LAW-3](BIBLE.md#IP-LAW-3)), the LLM system-prompt verb-catalog reflection law
([IP-LAW-4](BIBLE.md#IP-LAW-4)), the SQL-backed `SqlWorkspaceStore` (registered in the Blazor
host before the engine; one-shot JSON import on first user load), and
`ConditionProgressRepository` upsert/read (integration tests against SQL Server LocalDB; insert
on first call, update on second, full-pass clears verb, zero-condition fast path, two strategies
track independently). See [USER_STORIES.md](USER_STORIES.md) for per-capability test citations.

Not proven by the solution build/test: the Blazor UI flows, the LLM voting round-trip, and the
Cypress E2E suite (specs exist in `tests/IdiotProof.Cypress/`; need a live server run). Those
are 🟡/⬜ in the stories.

## 7. Active frontier {#IP-§7}
- **Strategy ghost overlay + branching visualization** — see `TODO.md`: chart integration,
  simulator timeline, branch fork rendering. (Epic D in the stories, all ⬜.)
- **Roslyn-based IdiotScript parser** — replace the tolerant regex parser with exact
  line/col diagnostics.
- **Cypress CI run** — `02_strategies_describe.cy.ts` and `03_api_keys.cy.ts` exist and
  cover IP-US-E1 and IP-US-E3; they need a live server run to graduate those stories to ✅.

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
- **Sandbox broker** — the always-registered no-op broker that is the safe default in `BrokerRouter`.
