# IdiotProof

**Plain-English trading strategies, evaluated 24/7, executed only when every safeguard agrees.**

Describe a setup the way you'd say it out loud — _"if NVDA pulls back to the 9 EMA in an
uptrend with volume confirmation, go long with a 1% stop"_ — and IdiotProof turns it into a
real, runnable strategy. A multi-LLM voter panel (Claude, GPT, Gemini, DeepSeek, via
`MindAttic.Legion`) cross-checks the translation against the live verb catalog so no model can
hallucinate syntax that costs you money. A standalone console **Monitor** evaluates every active
strategy against live market data roughly once a second, fires only when three independent gates
agree, places the order, and manages the exit. **Set and forget.**

> _"I think TSLA will gap up premarket and consolidate near the high. When that happens, go long
> with a stop at the premarket low and target 1.5 ATR above entry."_
>
> Queue it at 8 PM. Go to bed. The Monitor catches it at 4:07 AM ET when the conditions match.

### Why IdiotProof

- **No code, no chart-watching.** Describe your edge in prose; Claude writes the IdiotScript
  strategy; you can tweak the visual flowchart or the raw script if you want.
- **Near-real-time unattended evaluation.** A console process re-reads every active strategy from
  SQL on every pass (default ~1s), evaluates it against a live Alpaca websocket + REST feed, and
  reports per-condition progress (`4/5 — waiting on OnReclaim(9)`) live on the Strategies page.
- **Three gates before money moves.** Strategy conditions must all match → LLM voter quorum must
  approve → Risk Guardian must clear stop / daily-loss / per-trade-risk limits. Any one of them
  blocks the fire and logs the reasoning to the audit trail.
- **Paper by default, live by explicit opt-in.** The Sandbox broker is always the safe fallback;
  live trading requires a red-outline confirmation modal and per-strategy broker-mode selection.
  You can't accidentally route real capital.
- **Your strategies are yours.** SQL-backed, owned per user, encrypted broker keys, full audit log
  of every signal, veto, and order.

---

## Table of contents

0. [Quick start — trade a gapper](#0-quick-start--trade-a-gapper)
1. [What this system is](#1-what-this-system-is)
2. [Architecture](#2-architecture)
3. [Project reference](#3-project-reference)
4. [Domain model — the nouns and verbs](#4-domain-model--the-nouns-and-verbs)
5. [Storage layout](#5-storage-layout)
6. [IdiotScript — the DSL](#6-idiotscript--the-dsl)
7. [The Monitor console — the one pipeline](#7-the-monitor-console--the-one-pipeline)
8. [The Blazor web app](#8-the-blazor-web-app)
9. [The MAUI desktop shell and the shared UI library](#9-the-maui-desktop-shell-and-the-shared-ui-library)
10. [The Research Scanner](#10-the-research-scanner)
11. [The MindAttic family](#11-the-mindattic-family)
12. [Building, running, and testing](#12-building-running-and-testing)
13. [Export.ps1 and the other root scripts](#13-exportps1-and-the-other-root-scripts)
14. [Directory layout](#14-directory-layout)
15. [Glossary](#15-glossary)
16. [Documentation canon and further reading](#16-documentation-canon-and-further-reading)
17. [Known gaps and roadmap](#17-known-gaps-and-roadmap)

---

## 0. Quick start — trade a gapper

> The condensed runbook. The full field-by-field walkthrough lives in
> **[user-guide.htm](IdiotProof.Blazor/wwwroot/user-guide.htm)** — also served by the running app
> at `/user-guide.htm` and linked from the Gapper tab.

The system is **two .NET processes sharing one SQL database** — the database *is* the
communication channel. Anything you change in the UI is picked up by the console on its next
pass (roughly a second later); no restart, no extra wiring.

### Step 1 — start both processes (two terminals)

```bash
# Terminal 1 — the web UI
dotnet run --project IdiotProof.Blazor

# Terminal 2 — the trading console
dotnet run --project IdiotProof.Monitor
```

Both default to the same LocalDB database (`IdiotProof`). The console logs a startup banner,
acquires the SQL leader lease, and starts ticking. **Register an account** in the UI on first run.

### Step 2 — real market data (the console's eyes)

The console picks its **data feed** from the global settings chain. Put Alpaca keys in the
MindAttic broker keyring at `%APPDATA%\MindAttic\Brokers\providers.json`:

```json
{ "alpaca-paper": { "type": "alpaca", "apiKey": "PK...", "secret": "..." } }
```

(or set `AlpacaApiKeyId` / `AlpacaApiSecretKey` env vars before starting the Monitor). With keys
present the Monitor uses Alpaca's REST API plus a live websocket stream automatically (real-time
SIP consolidated tape by default — see [IP-A29](docs/AMENDMENTS.md#IP-A29)); without keys it
falls back to a deterministic mock feed.

### Step 3 — real order routing (your account, your money)

In the UI: **user menu → API Keys** → enter your Alpaca key/secret → leave **Paper** checked →
enable **"Route my orders to this Alpaca account"** → Save All. The console decrypts those keys
through the shared ASP.NET Core Data Protection key ring and places *your* orders on *your*
account. Without the toggle, orders go to the built-in **Sandbox** broker (simulated fills) — the
safe default by design. Live trading = uncheck Paper and confirm the red modal; the strategy's
own `BrokerMode` (Paper / Live / Sandbox) then overrides the global flag per strategy.

### Step 4 — queue gappers

Two ways. **From a transcript:** paste a video transcript (or any natural language) into the
Gapper tab's "From a transcript" box → **Interpret** → Claude (via Legion, through
`GapperInterpreter`) extracts the gap plays as reviewable candidate cards with inferred dial-ins →
**Queue** the ones you like, or **Load into dials** to tweak first. Nothing queues itself — you
click every queue. **By hand:** ticker → pick a profile (from
`IdiotProof.Blazor/wwwroot/data/gapper-profiles.json`) → **Dial in** (gap %, volume, price band,
SL/TSL, peak-giveback, arm + sell-by times) → **Queue Gapper**. From then on it's hands-off: the
Monitor screens the ticker every pass in the **4:00–9:00 AM ET premarket window**, buys through
the three gates when the criteria hit (premarket orders are limit + extended-hours automatically),
shows **HOLDING qty @ price** live, and sells before the 9:30 bell — momentum giveback first, hard
sell-by as the backstop.

### Step 5 — watch it / dry-run it

The console log narrates every decision; the Gapper and Strategies tabs mirror it live. To
rehearse without keys or money: start the Monitor with `IDIOTPROOF_FEED=mock`, queue a ticker
starting with **GAP** (e.g. `GAPT` — the mock feed fabricates a gap day for it), and leave routing
off (Sandbox). The session gate still runs on the real ET clock even in mock mode — entries only
fire during actual premarket hours, Monday–Friday. The Monitor also refuses to place a **real**
order against **mock** market data even if a broker is configured — see
[§7](#7-the-monitor-console--the-one-pipeline).

### Monitor env knobs

| Env var | Default | Notes |
|---|---|---|
| `IDIOTPROOF_MONITOR_INTERVAL` | `1s` | Evaluation cadence (`30s`, `5m`, bare seconds). The websocket stream keeps prices fresh between REST refreshes regardless of this value. |
| `IDIOTPROOF_FEED` | auto | `alpaca` \| `mock` (auto = Alpaca when keys resolve). |
| `IDIOTPROOF_BROKER` | `sandbox` | `alpaca` opts the *global* account into real routing (per-strategy routing is `BrokerMode` / the API Keys toggle). |
| `IDIOTPROOF_ALPACA_FEED` | `sip` | Data tier for both REST and streaming; `sip` requires an Algo Trader Plus subscription and falls back on rejection — set `iex` explicitly for the free tier. |
| `IDIOTPROOF_STREAMING` | on | `0` disables the websocket stream (REST-only). |
| `IDIOTPROOF_SELFPING` | `30m` | Liveness line cadence; `0` disables it. |
| `IDIOTPROOF_PRINT_FILLS` | on | `0` silences the framed ENTRY/EXIT console blocks (the structured logger line still fires). |

Unattended operation: `sc.exe create IdiotProof.Monitor binPath="<path>\idiotproof-monitor.exe"` —
the console is Windows-Service-ready (`AddWindowsService`), and a SQL `sp_getapplock` leader lease
guarantees only one instance trades per database (a second instance waits in standby and takes
over automatically if the leader dies).

---

## 1. What this system is

IdiotProof is **one solution, several front doors, one shared engine** — a trading-strategy
platform, not a single app:

- **`IdiotProof.Blazor`** — a Blazor Server web app where a trader authors strategies (visual
  flowchart, raw IdiotScript text, or a plain-English description handed to Claude), watches them
  live, and manages accounts/keys/research.
- **`IdiotProof.Maui`** — a .NET MAUI Blazor Hybrid desktop shell intended to present the *same*
  pages as the Blazor host without a browser (see [§9](#9-the-maui-desktop-shell-and-the-shared-ui-library)
  for its actual current state — it is a scaffold today, not yet wired to real IdiotProof pages).
- **`IdiotProof.Monitor`** — a standalone console that runs unattended, loads every active
  strategy from SQL, evaluates it continuously, walks the three gates, places orders, and manages
  open positions to their exit — so the trader doesn't have to be at the computer at 4 AM ET when
  their setup fires.
- **`IdiotProof.ResearchScanner`** — a one-shot console (meant for a Scheduled Task, not a daemon)
  that sweeps EDGAR filings, Alpaca news, and Federal Register regulatory notices for
  market-moving events, scores their significance, and writes them to the same database for the
  `/research` tab to read.

Everything shares one SQL database as the single source of truth, one settings/credential overlay
chain (`IdiotProof.Engine.Settings.AppSettings`), and the same domain libraries
(`IdiotProof.Models`, `IdiotProof.Shared`, `IdiotProof.Indicators`, `IdiotProof.Scripting`,
`IdiotProof.Strategies`) for evaluating a strategy identically everywhere it's evaluated.

It is **Alpaca-only** in the active build. A new broker plugs in by implementing `IBrokerClient`
and registering it with `BrokerRouter`; a dormant IBKR adapter tree exists outside the solution
(`IdiotProof.Brokers.Ibkr/`, not referenced by `IdiotProof.slnx`).

---

## 2. Architecture

Data flows in one direction from market data down to an order, and audit/state flows back up
through SQL to every UI that's watching:

```
                                ┌───────────────────────────────┐
                                │        Trader (browser)        │
                                └───────────────┬─────────────────┘
                                                │
                              ┌─────────────────▼──────────────────┐      ┌─────────────────────┐
                              │          IdiotProof.Blazor          │◄─────┤   Cypress E2E       │
                              │  Strategies · Strategy Builder      │      │ (7 specs, tests/)   │
                              │  (Guided / Script / Describe)       │      └─────────────────────┘
                              │  Gapper · Learn · Research          │
                              │  Backtest · Settings · API Keys     │
                              └─┬──────────┬─────────────┬──────────┘
                                │          │             │
                       ┌────────▼──┐ ┌─────▼─────┐ ┌─────▼───────────┐
                       │StrategyBu-│ │ Wikilink  │ │StrategyScript-  │
                       │ilderRend- │ │ Parser    │ │Generator        │──► MindAttic.Legion
                       │erer       │ └───────────┘ │(reflects verb   │    (voter panel, legion.json)
                       └─┬─────────┘               │catalog, sends  │───►┌──────────────────┐
                         │                          │to Claude/GPT/  │    │ %APPDATA%\       │
                         │                          │Gemini/DeepSeek)│    │ MindAttic\LLM\   │
                         │                          └────────────────┘    │ providers.json   │
                         │                                                └──────────────────┘
              ┌──────────▼──────────────────────────┐
              │           IdiotProof.Engine          │
              │  AppSettings (disk→env→Vault→config) │
              │  ServiceRegistration · SupervisedLoop │
              │  AuditLogger · WorkspaceManager       │
              └─┬───────────┬───────────┬─────────────┘
                │           │           │
          ┌─────▼───┐  ┌────▼─────┐ ┌───▼─────────────────┐
          │ Models  │  │Indicators│ │     Scripting        │
          │ (nouns) │  │ RSI EMA  │ │  IdiotScript DSL,     │
          │         │  │ ATR MACD │ │  StrategyBuilder,     │
          │         │  │ VWAP ... │ │  ScriptParser,        │
          │         │  │          │ │  GapperProfile,       │
          │         │  │          │ │  TradingSchedule (ET) │
          └────┬────┘  └────┬─────┘ └──────────┬────────────┘
               │            │                   │
          ┌────▼────────────▼───────┐   ┌───────▼─────────────┐
          │       Shared            │   │     Strategies       │
          │  RiskGuardian (final    │   │  IStrategy·DslStr-    │
          │  veto) · IndicatorSnap  │   │  ategy·IndicatorSnap- │
          └────┬─────────────────── ┘   │  shotBuilder·Gapper-  │
               │                        │  ExitEvaluator·       │
               │                        │  StrategyBacktester   │
               │                        └──────────┬────────────┘
               │                                   │
          ┌────▼──────────────┐          ┌─────────▼────────────┐
          │   DataFeeds        │         │      Brokers          │
          │ IMarketDataFeed:    │        │ IBrokerClient:         │
          │  AlpacaDataFeed     │        │  AlpacaBrokerClient    │
          │  (REST, sip/iex)    │        │  SandboxBrokerClient   │
          │  AlpacaStreamingCl- │        │  BrokerRouter          │
          │  ient (websocket)   │        │  (Sandbox is always    │
          │  MockDataFeed       │        │   the safe default)    │
          │  SwitchableFeed     │        └────────────────────────┘
          └────────┬───────────┘
                   │
          ┌────────▼──────────────────────────────────────────┐
          │                SQL Server (LocalDB)                 │
          │                  IdiotProof database                 │
          │  Strategies · UserPreferences · ConditionProgress    │
          │  UserApiKeys · AuditLogs · Workspaces · LiveBars      │
          │  ResearchClaim · TrackedTicker · TradeDiaryEntry ...  │
          └────────▲───────────────────────────────────┬─────────┘
                   │                                   │
        ┌──────────┴───────────┐             ┌─────────▼──────────────┐
        │  IdiotProof.Monitor    │            │  IdiotProof.ResearchSc- │
        │  (console, 24/7)       │            │  anner (one-shot,       │
        │  evaluates → 3 gates   │            │  Scheduled-Task-fired)  │
        │  → order → exit mgmt   │            └─────────────────────────┘
        └────────────────────────┘
```

### Reading the diagram

1. A trader authors a strategy in `IdiotProof.Blazor` (or, in principle, `IdiotProof.Maui`); it's
   saved to the `Strategies` table as canonical strict JSON (`ScriptJson`) with IdiotScript text
   (`ScriptText`) kept only as a human-readable view — see [IP-LAW-8](docs/BIBLE.md#IP-LAW-8).
2. `IdiotProof.Monitor` re-reads every `IsActive = true` row on every evaluation pass, builds an
   `IndicatorSnapshot` per symbol from live candles (`IdiotProof.Indicators` math over
   `IdiotProof.DataFeeds` data), and walks each strategy's entry conditions.
3. A full pass on all conditions is a **candidate signal**, not an order — it must still clear the
   LLM voter panel and the `RiskGuardian` (`IdiotProof.Shared.Risk`) before `IdiotProof.Brokers`
   places anything.
4. Every step — condition progress, votes, vetoes, fills, exits — is written back to SQL
   (`ConditionProgress`, `AuditLogs`, `LiveBar`), which is what the Blazor UI polls to show live
   badges without any direct connection to the Monitor process.
5. `IdiotProof.ResearchScanner` is architecturally separate: it doesn't touch strategies or orders
   at all, it only populates `ResearchClaim`/`TrackedTicker` rows that the `/research` page reads.

---

## 3. Project reference

Every project below is registered in `IdiotProof.slnx` (verified by reading the solution file and
each project's actual source tree — nothing here is inferred from documentation alone).

| Project | Type | Responsibility | Key types |
|---|---|---|---|
| `IdiotProof.Models` | Class library | Domain DTOs and enums — the nouns everything else shares. | `Candle`, `TradeSignal`, `TradeSetup`, `OrderRequest`/`OrderResult`, `Position`, `TradeDirection`, `TradingSession`, `BrokerType {Alpaca, Sandbox}`, `StrategyType`; options ([IP-A33](docs/AMENDMENTS.md#IP-A33)): `AssetClass {Equity, Option}`, `OptionRight`, `OptionContract` (OCC parse/build), `OptionQuote`, `OptionGreeks` |
| `IdiotProof.Shared` | Class library | Cross-cutting primitives used by almost every other project. | `RiskGuardian` (+ `RiskGuardianConfig`/`Result`) — the final pre-trade veto; `IndicatorSnapshot`; `LogMessage`; `SettingsMetadata`; `Branding` (console ASCII banner); `Options/` — `IntrinsicValueCalculator` (real vs hype split, breakeven, DTE), `BlackScholesCalculator` (theoretical price + implied-vol solver), `SellSignalEvaluator` (informational "consider taking profit") |
| `IdiotProof.Indicators` | Class library | Pure indicator math, no I/O. | `ADX`, `ATR`, `BollingerBands`, `CCI`, `EMA`, `MACD`, `Momentum`, `OBV`, `RSI`, `SMA`, `Stochastic`, `VWAP`, `WilliamsR`, `CandlestickPatterns` |
| `IdiotProof.Scripting` | Class library | The IdiotScript DSL itself: authoring, parsing, serializing, scheduling. | `IdiotScript`/`StrategyBuilder`/`Conditions` (fluent authoring), `ScriptParser` (tolerant text → model), `StrategyJson` (canonical strict-JSON codec), `StrategyLoader` (fail-closed load), `StrategyHtml` (render), `GapperProfile` (dialable template), `EmaPeriodCollector`, `TradingSchedule`/`MarketTime` (ET session clock) |
| `IdiotProof.Strategies` | Class library | Turns a parsed definition into something evaluatable and testable. | `IStrategy`, `DslStrategy` (adapter), `IndicatorSnapshotBuilder`, `StrategyBranchResolver` (If/ElseIf/Else), `GapperExitEvaluator` (sell-by/stop/target/peak-giveback), `GapperDayBacktester`, `Backtesting/StrategyBacktester` + `BacktestReport` |
| `IdiotProof.DataFeeds` | Class library | Market data abstraction and providers. | `IMarketDataFeed` (+ default `GetPreviousCloseAsync`), `AlpacaDataFeed` (REST, sip/iex), `AlpacaStreamingClient` (websocket trades + minute bars), `MockDataFeed` (deterministic gap simulation), `SwitchableMarketDataFeed` |
| `IdiotProof.Brokers` | Class library | Order routing abstraction and providers. | `IBrokerClient` (equity members + default-implemented options members: `SupportsOptions`, `GetOptionTradingLevelAsync`, `GetOptionChainAsync`, `GetOptionQuotesAsync`), `AlpacaBrokerClient` (orders/positions/account + `/v2/options/contracts`, data-host `/v1beta1/options/snapshots`, single-leg option orders), `AlpacaOAuthClient`, `SandboxBrokerClient` (in-memory fills + a synthetic options chain), `BrokerRouter` (Sandbox always registered as the safe fallback) |
| `IdiotProof.Engine` | Class library | The DI root shared by every host. | `ServiceRegistration.AddIdiotProofEngine(...)`, `Settings/AppSettings` (disk → env → MindAttic keyrings → `IConfiguration` overlay chain), `Storage/IStorageProvider`/`StorageLocation`, `SupervisedLoop` (fault-tolerant tick loop with backoff + heartbeat), `AuditLogger`, `Workspace/WorkspaceManager` + `JsonFileWorkspaceStore` (legacy JSON path; the Blazor host swaps in a SQL-backed store) |
| `IdiotProof.Blazor` | ASP.NET Core Blazor Server app | The primary web front door: strategy authoring/monitoring, accounts, keys, research, learning. | See [§8](#8-the-blazor-web-app) |
| `IdiotProof.Maui` | .NET MAUI Blazor Hybrid app | Desktop shell intended to reuse `IdiotProof.UI` for the same pages, offline of a browser. | See [§9](#9-the-maui-desktop-shell-and-the-shared-ui-library) |
| `IdiotProof.UI` | Razor Class Library | Home for UI shared between `Blazor` and `Maui` hosts (parity by construction, [IP-A28](docs/AMENDMENTS.md#IP-A28)). Referenced by `IdiotProof.Blazor` since [IP-A33](docs/AMENDMENTS.md#IP-A33). | `Components/Options/`: `OptionsChainView`, `OptionOrderTicket`, `OptionPositionTracker`, `OptionsLiveElevationModal`, `OptionsPresenter` + view models; `wwwroot/css/options.css`. Presentational only — see [§9](#9-the-maui-desktop-shell-and-the-shared-ui-library) |
| `IdiotProof.Monitor` | .NET generic host / console, Windows-Service-ready | The 24/7 evaluator and executor — "the one pipeline." | `Program.cs` (composition root), `MonitorWorker` (the tick loop), `MonitorLeaderLease` (`sp_getapplock` single-instance lease), `MonitorCli` (operator subcommands), `AutoGapperScanner`, `PremarketFadeScanner`, `EmailSmsAlertSender`, `StrategyScanner`/`StrategyReplay`/`StrategyReplayLive`/`ReplayFeatures`/`ReplayTemplates`/`StrategyDataset` (offline replay/ML-dataset tooling) |
| `IdiotProof.ResearchScanner` | Console (one-shot) | Autonomous market-event research sweep; not a daemon, not part of the trading loop. | `Program.cs`, `ScanPassRunner` |
| `IdiotProof.Engine.Tests` | NUnit | RiskGuardian gate, SupervisedLoop resilience, WorkspaceManager, options pricing math (`OptionsPricingTests`: OCC, intrinsic/extrinsic, Black-Scholes, IV round-trips, sell signal). | — |
| `IdiotProof.Indicators.Tests` | NUnit | RSI/EMA/ATR/MACD/VWAP math + ADX Wilder-seed regression. | — |
| `IdiotProof.Strategies.Tests` | NUnit | DSL round-trip, backtester, gapper lifecycle, canonical-JSON contract, and a large family of exhaustive combinatorial matrix tests (phase/condition/branch permutations). | — |
| `IdiotProof.Brokers.Tests` | NUnit | BrokerRouter Sandbox-default + safe fallback, Sandbox fill simulation, Sandbox synthetic options chain, Alpaca options wire format against canned responses (`OptionsBrokerTests`). | — |
| `IdiotProof.Blazor.Tests` | NUnit | `StrategyScriptGenerator` verb-catalog reflection, `LlmVotingService` consensus logic, research-pipeline services (incl. `IndexEventScannerTests`), repository guard rails. | — |
| `IdiotProof.Monitor.Tests` | NUnit | `PremarketFadeScanner`. | — |
| `tests/IdiotProof.Cypress` | Cypress 13 | End-to-end Blazor UI tests (7 specs). | — |

Not in the solution and not part of the active build (verified absent from `IdiotProof.slnx`):
`IdiotProof.Brokers.Ibkr` (dormant IBKR adapter). `docs/BIBLE.md §3` records other historical
trees (`IdiotProof.Core`, `IdiotProof.Cli`, `src/`) as deleted 2026-06-07.

---

## 4. Domain model — the nouns and verbs

**Nouns** (`IdiotProof.Models`, `IdiotProof.Shared`):

- `Candle` — one OHLCV bar.
- `TradeSignal` — the output of `IStrategy.Evaluate`; a candidate to fire.
- `TradeSetup` / `RiskLimits` — decimal-priced inputs `RiskGuardian` validates.
- `OrderRequest` / `OrderResult` / `Position` — broker-facing order lifecycle.
- `StrategyDefinition` (`IdiotProof.Scripting`) — a parsed strategy: phases + conditions + branches.
- Enums verified in `IdiotProof.Models/Enums.cs`: `TradeDirection {Long, Short}`,
  `TradingSession {Premarket, RTH, AfterHours, Extended}`, `OrderType`, `OrderSide`, `PriceType`,
  `ConfidenceGrade`, `BrokerType {Alpaca, Sandbox}`, `StrategyType {Iti, BreakoutPullback, LowHigh,
  FluentDsl, Custom}`, `WorkspaceState`.

**Verbs** (the key services, by call site):

- `Stock.Ticker(symbol)` → `StrategyBuilder` (`IdiotProof.Scripting`) — the entry point to author
  IdiotScript.
- `ScriptParser.Parse(...)` / `StrategyJson.Serialize`/`Deserialize` — text/JSON ↔ object model.
- `IStrategy.Evaluate(symbol, candles, context)` → `IReadOnlyList<TradeSignal>`.
- `DslStrategy` — adapts a parsed `StrategyDefinition` into an `IStrategy`.
- `IndicatorSnapshotBuilder.Build(...)` / `.BuildWithEmas(...)` → `IndicatorSnapshot` — what every
  condition evaluates against.
- `StrategyBranchResolver.Resolve(def, snapshot)` — applies `If/Then/ElseIf/Else` overrides before
  the entry conditions are read.
- `StrategyBacktester.Run(...)` → `BacktestReport`; `GapperDayBacktester` — the gapper-specific
  day-replay variant.
- `RiskGuardian.ValidateTrade(setup)` → a verdict with `IsApproved` + `BlockReasons`;
  `RecordTradePnL(realized)` feeds the daily circuit breaker.
- `SupervisedLoop.RunAsync(options, ct)` — the fault-tolerant tick loop every long-running host
  (currently just the Monitor) runs its work under.
- `IBrokerClient.PlaceOrderAsync(...)` via `BrokerRouter.PlaceOrderAsync(...)` — Sandbox is the
  always-registered fallback.
- `GapperExitEvaluator.Evaluate(...)` / `.EvaluateShort(...)` — pure, clock-parameterized sell-by /
  stop / take-profit / peak-giveback verdict for a held position.
- `IMarketDataFeed.GetHistoricalCandlesAsync(...)`, `.GetLatestPriceAsync(...)`,
  `.GetPreviousCloseAsync(...)` — Alpaca (REST + websocket) or Mock, selected by
  `SwitchableMarketDataFeed`.
- Research subsystem (`IdiotProof.Blazor/Services`, driven by `IdiotProof.ResearchScanner`):
  `TickerUniverseService`, `EdgarService`, `Form4Parser`, `CorporateActionDetector`,
  `RegulatoryScanner`, `CatalystExtractor`, `OutcomeBackfillService`, `SignificanceScorer`,
  `ResearchService` — see [IP-A32](docs/AMENDMENTS.md#IP-A32) for the full narrative.

---

## 5. Storage layout

```
%LOCALAPPDATA%\MindAttic\IdiotProof\           ← per-app state
└── Settings\app-settings.json                 (legacy disk overlay; SQL is canonical for runtime state)

%APPDATA%\MindAttic\                            ← shared keyrings (shared across the MindAttic family)
├── LLM\providers.json                         (Claude/OpenAI/Gemini/DeepSeek keys — MindAttic.Legion's home)
├── Brokers\providers.json                     (alpaca-paper, alpaca-live — IdiotProof's own bucket)
└── Security\providers.json                    (pepper.v1, bootstrap-token — MindAttic.Authentication)

SQL Server (LocalDB by default)                ← canonical runtime state, shared by Blazor + Monitor + ResearchScanner
└── IdiotProof database
    ├── AuthUsers, ...                          (MindAttic.Authentication — Argon2id, sessions, MFA scaffolding)
    ├── UserApiKeys                             (per-user encrypted broker/data keys)
    ├── Strategies                              (UUIDv7 id, OwnerUserId, Title, ScriptJson canonical, ScriptText view, IsActive, BrokerMode, PositionQty, ...)
    ├── UserPreferences                         (Theme, ActiveAccountId, OpenStrategyTabs, RiskGuardian config, UiStateJson)
    ├── LearningArticles                        (Slug, Category, Title, BodyMarkdown, Order — seeded by LearningContentSeeder)
    ├── SettingsKv                               (generic KV store — currently unconsumed; see §17)
    ├── Workspaces                               (per-user containers — Watchlist + Strategies + risk params, schema-tolerant BodyJson)
    ├── AuditLogs                                (append-only: signal fires, orders, broker switches, risk vetoes, monitor start/stop)
    ├── ConditionProgress                        (one row per Strategy — Monitor's most recent N/M evaluation snapshot)
    ├── LiveBar                                  (per-strategy per-tick OHLCV + condition bits, feeds any live chart)
    ├── TradeDiaryEntry                          (operator-facing trade journal)
    ├── ResearchClaim, TrackedTicker,
    │   InsiderTransaction, ...                  (ResearchScanner output tables)
    └── ReplayRun, ReplayFeatureRows, ...         (offline strategy replay + ML feature store)
```

**Connection string priority chain** (identical for the Blazor host, the Monitor, and the
ResearchScanner — verified in all three `Program.cs` files):

1. `ConnectionStrings__IdiotProof` env var
2. `ConnectionStrings:IdiotProof` from `IConfiguration` (`appsettings.json`)
3. LocalDB fallback: `Server=(localdb)\MSSQLLocalDB;Database=IdiotProof;Trusted_Connection=True;TrustServerCertificate=True;`

**`IDIOTPROOF_DATA_DIR`** overrides the per-app state root (useful for parallel test/dev runs).

**Settings overlay chain** (`IdiotProof.Engine.Settings.AppSettings`, applied by every host in the
same order — later wins): disk → environment variables → MindAttic LLM keyring → MindAttic broker
keyring → `IConfiguration` (User Secrets / App Service Application Settings / Azure Key Vault).

---

## 6. IdiotScript — the DSL

### Six lifecycle phases

Every strategy walks through fixed phases. The visual builder renders one card per phase; the
parser rejects verbs used in the wrong phase.

| # | Phase | What it answers | Example verbs |
|---|---|---|---|
| 1 | Setup | Ticker / session / window | `Stock.Ticker`, `Session`, `Quantity` |
| 2 | Filters | Regime gates (always-on) | `RequireAdxAbove`, `RequireEmaStack` |
| 3 | Entry | Triggers (AND of conditions) | `IsAboveVwap`, `OnReclaim`, `IsBullishEngulfing` |
| 4 | Order | Direction + size | `Long`, `Short`, `Quantity` |
| 5 | Risk | Stop placement | `StopLoss`, `TrailingStopLoss` |
| 6 | Exit | Targets, time exit | `TakeProfit`, `ExitStrategy` |

### Verb catalog

The verb catalog below is illustrative, drawn from the fluent builder and the Learning Center
seed content. `IP-LAW-4` requires `StrategyScriptGenerator` to build its LLM system prompt by
*reflecting* on the real `StrategyBuilder`/`Conditions` types rather than a hand-maintained list —
so the live, ground-truth catalog is whatever compiles in `IdiotProof.Scripting` today, not this
table. Treat this as a tour, not a spec.

#### Setup
- `Stock.Ticker(symbol)` — required first call
- `.Session(TradingSession)` — `Premarket / RTH / AfterHours / Extended`
- `.Quantity(int)` — share count

#### Filters (regime gates)
- `.RequireAdxAbove(threshold = 20)` — trending market only
- `.RequireEmaStack(fast, slow)` — fast EMA above slow = uptrend confirmation

#### Entry — VWAP
- `.IsAboveVwap()` / `.IsBelowVwap()` (alias: `AboveVwap`, `BelowVwap`)
- `.OnVwapReclaim()` / `.OnVwapLoss()`

#### Entry — EMA family
- `.IsAboveEma(period)` / `.IsBelowEma(period)`
- `.IsBetweenEma(fast, slow)` — pullback zone
- `.OnReclaim(period)` — prior bar at-or-below N-EMA, current bar back above

#### Entry — RSI / MACD / ADX / DI
- `.IsRsiOversold(threshold = 30)` / `.IsRsiOverbought(threshold = 70)`
- `.IsRsiBullishDivergence()` / `.IsRsiBearishDivergence()`
- `.IsMacdBullish()` / `.IsMacdBearish()`
- `.IsAdxAbove(threshold)` / `.IsDiPositive()` / `.IsDiNegative()`

#### Entry — Volume / Gap / Levels
- `.WithVolumeConfirm(multiplier = 1.2)` / `.IsVolumeAbove(multiplier)` / `.VolumeSpike(multiplier = 2.0)`
- `.IsGapUp(minPercent = 3)` / `.IsGapDown(minPercent = 3)`
- `.IsAtSupport(tolerancePercent = 0.5)` / `.IsAtResistance(tolerancePercent = 0.5)`
- `.HoldsAbove(price)` / `.HoldsBelow(price)` / `.IsNear(price, tolerance)` / `.BreaksAbove(price)` / `.BreaksBelow(price)`

#### Entry — Candlestick patterns
- `.IsBullishEngulfing()` / `.IsBearishEngulfing()`
- `.IsHammer()` / `.IsShootingStar()`
- `.IsDoji()`

#### Order / Risk / Exit
- `.Long()` / `.Short()` / `.Order(TradeDirection)`
- `.Quantity(int shares)` / `.Quantity(decimal dollars)` — overloaded; share count or notional
  dollars (Alpaca's `notional` field). Mutually exclusive — setting one clears the other.
- `.StopLoss(price)` / `.StopLossPercent(percent)` / `.TrailingStopLoss(percent)`
- `.TakeProfit(price)` / `.TakeProfit(t1, t2, t3?)` / `.TakeProfitPercent(percent)`
- `.ExitStrategy(timeOfDay)`

### Branching — expression syntax

```csharp
using static IdiotProof.Scripting.Conditions;

Stock.Ticker("SPY")
    .RequireAdxAbove(20)
    .If(IsAboveVwap.And(IsEmaAbove(9)))
        .Then(b => b.Long().StopLossPercent(1).TakeProfitPercent(2))
    .ElseIf(c => c.IsBelowVwap().IsEmaBelow(9),
            b => b.Short().StopLossPercent(1).TakeProfitPercent(2))
    .Else(b => b.Long().TakeProfitPercent(0.5))
    .Build();
```

Conditions compose with `.And()` / `.Or()` / `.Not()`. Branching blocks evaluate top-down; the
first match's overrides apply on top of the base strategy — resolved at evaluation time by
`StrategyBranchResolver.Resolve(def, snapshot)`, which the Monitor calls on every pass so branches
work identically live and in the backtester.

### Worked example — 9/30 pullback continuation

```csharp
Stock.Ticker("NVDA")
    .RequireAdxAbove(20)               // regime gate: trending market
    .RequireEmaStack(9, 31)            // confirm uptrend (9 above 31)
    .IsAboveVwap()                     // institutional bullish bias
    .IsBetweenEma(9, 31)               // price in pullback zone
    .OnReclaim(9)                      // trigger: closed back above 9
    .WithVolumeConfirm(1.2)            // 1.2x avg volume on trigger bar
    .Long()
    .StopLoss(450)                     // below the 31 EMA at signal time
    .TakeProfit(485)                   // ~2x risk
    .Build();
```

### The canonical layer — JSON, not text (IP-LAW-8)

The semantic model (`StrategyDefinition`), serialized as versioned strict JSON
(`Strategy.ScriptJson`, via `IdiotProof.Scripting/StrategyJson.cs`), is what every evaluator
actually runs. Reads fail closed: an unknown schema version, condition type, or property throws
`StrategyJsonException` and the strategy is **quarantined** (a visible reason surfaces in
`ConditionProgress` — see the `EvaluateOneAsync` handling in `MonitorWorker.cs`), never partially
evaluated. IdiotScript text (`ScriptText`) is a generated human view plus the tolerant input path
for hand-typed/legacy rows — never the money-path source of truth. `StrategyLoader.Load(json,
text)` implements the "canonical-first, tolerant-text-fallback" contract.

---

## 7. The Monitor console — the one pipeline

`IdiotProof.Monitor` is the unattended evaluator **and executor** ([RFC 0002](docs/rfc/0002-gapper-and-unification.md) /
[IP-A8](docs/AMENDMENTS.md#IP-A8)). It is a `BackgroundService` (`MonitorWorker`) hosted by the
generic host in `Program.cs`, running under `SupervisedLoop` so a single bad tick backs off and
retries rather than crashing the process ([IP-LAW-5](docs/BIBLE.md#IP-LAW-5)).

### What one pass does (verified against `MonitorWorker.TickAsync`/`EvaluateOneAsync`/`FireAsync`)

1. **Trading-schedule gate** — `TradingSchedule.Classify(nowUtc)` classifies the moment into
   `Hibernate` / an active window. Outside active hours the loop just emits a liveness ping every
   5 minutes; nothing else runs.
2. **Re-read active strategies** — every `IsActive = true` row, grouped by symbol. UI edits
   (queue, toggle, dial-in change) apply on the very next pass — no restart.
3. **Fetch candles** — a rolling 240-minute-bar cache per symbol (Alpaca REST, refreshed every 5
   minutes, or every 30 seconds if the last fetch came back empty), continuously topped up between
   REST refreshes by the Alpaca websocket stream (`AlpacaStreamingClient`) when keys are present.
   Plus the previous daily close, cached per ET day, for gap math.
4. **Per strategy:** load the canonical `ScriptJson` (fail-closed quarantine on rejection —
   escalated loudly if the strategy is currently holding a position, since quarantine means its
   exit rules stop running too), resolve `If/ElseIf/Else` branches against the indicator snapshot,
   then either manage an existing open position's exit or walk entry conditions one by one.
5. **Per-condition progress** is upserted to `ConditionProgress` every pass (`(PassedCount,
   TotalCount, FirstFailingVerb)`) — what the Strategies page polls for its live badges — and a
   throttled `LiveBar` row is written for chart consumers.
6. **On a full pass, three gates before any order** ([IP-LAW-1](docs/BIBLE.md#IP-LAW-1)):
   - **Gate 1 — `LlmVotingService`** (the Legion voter panel from `legion.json`). The quorum must
     explicitly **Approve**; zero votes, a Reject, an Abstain-only result, or a below-threshold
     split all fail closed (no fire). Skipped only when voting is disabled or no Claude key is
     configured.
   - **Gate 2 — `RiskGuardian`** (`IdiotProof.Shared.Risk`, the final pre-trade veto,
     [IP-LAW-2](docs/BIBLE.md#IP-LAW-2)), a per-user instance cached by `RiskGuardianService` so
     the in-memory daily-loss circuit breaker survives across signals. Validates stop-loss
     presence and side, per-trade and daily loss limits, stop-distance bounds, and account-risk
     percent.
   - Both gates must pass before `RecordFiredAsync` bumps `LastFiredUtc`/`FireCount`.
7. **Placing the order** — `UserBrokerResolver` resolves the *strategy's own* `BrokerMode`
   (Paper/Live/Sandbox), independent of any other strategy's routing. Premarket/after-hours orders
   go in as limit + `extended_hours` (an Alpaca requirement); regular-hours entries go in as a
   marketable limit (entry price + 0.2%) so a thin book can't fill far off the evaluated price. As
   a hard safety interlock, the Monitor refuses to place a **non-Sandbox** order while the
   configured market-data feed is **Mock** — evaluating against synthetic prices can never drive a
   real fill.
8. **Managing the open position** every pass via `GapperExitEvaluator.Evaluate`/`.EvaluateShort` —
   hard sell-by time, hard/trailing stops, take-profit, and the peak-giveback momentum rollover.
   Realized P&L feeds back into `RiskGuardian`'s daily circuit breaker. Exit orders are
   risk-reducing: they skip the LLM panel by design but are always audit-logged. **Shorts are
   currently signal-only** — the exit-management brain is long-shaped; a qualifying short candidate
   clears both gates and is recorded, but no order is placed (verified in `FireAsync`).

### Auxiliary jobs riding the same loop

- **`PremarketFadeScanner`** — a detection-only blow-off/fade alert, scanned every 5 minutes
  between 9:00–10:00 AM ET for every registered user (not just owners of active strategies); it
  never creates a strategy or places an order.
- **`AutoGapperScanner`** — resolved on demand by the `auto-gapper` operator CLI subcommand; no
  scheduled trigger of its own today.
- **Daily audit-log pruning** — every 24 hours, keeps 30 days of history with a 2,000-row floor.
- **Duplicate-fire guard** — at most one gapper-type strategy per symbol may fire per pass, even if
  two active rows for the same symbol both pass their conditions in the same tick.

### Run

```bash
dotnet run --project IdiotProof.Monitor
```

Operator CLI subcommands (status / set-keys / create-strategies / auto-gapper / ...) run against
the same built DI container and exit without starting the trading loop — see `MonitorCli.cs`.
Windows-Service-ready via `sc.exe create`; graceful shutdown via `IHostApplicationLifetime`.

---

## 8. The Blazor web app

`IdiotProof.Blazor` is a Blazor Server (interactive server components) app on ASP.NET Core, using
`MindAttic.Authentication` (Argon2id + pepper, sessions, MFA scaffolding — not ASP.NET Core
Identity) and EF Core 10 against SQL Server. Verified pages under `Components/Pages/`:

| Route (by component name) | Purpose |
|---|---|
| `Strategies.razor` | Front door — every saved strategy for the signed-in user, active toggle, live `N/M` progress badge, edit/delete, expand to see the rendered flowchart + raw script. |
| `StrategyBuilder.razor` | Multi-tab editor (Guided / Script / Describe) over open-strategy tabs (`BuilderTabBar`), persisted to `UserPreferences.OpenStrategyTabs` + `localStorage`. |
| `Gapper.razor` | Queue/dial-in gapper strategies; "From a transcript" free-text extraction via `GapperInterpreter`. |
| `Learn.razor` | The Learning Center — seeded articles with inline live-rendered strategy examples via `[[...]]` wikilinks (`WikilinkParser`, `<WikiContent>`). |
| `Research.razor` | Ranked "Today's High-Impact Events" feed over `ResearchScanner` output; collapsed Advanced panel for the older manual ticker/paste flow. |
| `Options.razor` | **Manual options section** (`/options`, [IP-A33](docs/AMENDMENTS.md#IP-A33) / RFC 0004) — deliberately separate from the strategy pipeline. Sandbox / Paper / Live account switch, options chain (CALLS \| strike \| PUTS) with a per-cell **breakeven** and **real-vs-hype** (intrinsic vs extrinsic) meter, a plain-English order ticket, open option positions with a real/hype split bar and an informational "extrinsic near its high + bullish news → consider taking profit" callout. Live orders reuse the 5-minute password elevation; the ticket locks itself while Alpaca reports `option_trading_level = 0`. Composes RCL components from `IdiotProof.UI`; host logic in `Services/OptionsTradingService.cs`. |
| `Backtest.razor` | Backtest UI (stub-level per the bible's active frontier — see [§17](#17-known-gaps-and-roadmap)). |
| `ActivityLog.razor` | Audit trail viewer. |
| `ApiKeys.razor` | Per-user broker/data key entry, live-mode danger modal. |
| `Settings.razor` | Preferences, theme, RiskGuardian config surface (partially wired — see §17). |
| `Login.razor` / `Register.razor` / `ForgotPassword.razor` / `ForgotUsername.razor` | Auth flows against `MindAttic.Authentication`. |
| `LiveChart.razor` | Live chart surface. |

Shared components (`Components/Shared/`): `AccountSummaryBar`, `GlossaryModal`, `LogsBadge`,
`StrategyBlueprintViz`, `StrategyBuilderRenderer`, `ToastContainer`. `Hubs/TradingHub.cs` is a
SignalR hub. `Auth/AuthService.cs` wraps the auth stack for the UI.

### The Describe tab — Claude-driven generation

1. User types ticker + title + plain-English description.
2. `StrategyScriptGenerator` builds a system prompt by **reflecting** on `StrategyBuilder` +
   `Conditions` (so the prompted verb catalog can never drift from what actually compiles —
   [IP-LAW-4](docs/BIBLE.md#IP-LAW-4)), sends it through `LegionClient` to the voter panel declared
   in `legion.json`.
3. The generated IdiotScript is parsed by `WikilinkParser.ParseScript` into a `StrategyDefinition`
   and rendered live via `<StrategyBuilderRenderer>`.
4. Save writes the row to SQL with a UUIDv7 id, paper-by-default.

### Theme

Alpaca palette only today (`--brand #FFCD00`, `--green #00C853`, `--red #EF4444`, ...), scoped
under `:root[data-theme="alpaca"]` in `wwwroot/css/_theme-alpaca.css`. New themes drop in as
additional `_theme-{name}.css` files plus a `<link>` in `Components/App.razor`; components
reference CSS custom properties, never raw colors, so switching is a single attribute flip.

### Account selector

The **AccountPill** mirrors Alpaca's UI: label + type ("Paper"/"Live") + masked account ID. Live
accounts render with a red outline; paper accounts with the brand-yellow outline. Credentials come
from the shared MindAttic broker keyring, overlaid onto `AppSettings` at startup.

---

## 9. The MAUI desktop shell and the shared UI library

`CLAUDE.md`'s rule ([IP-A28](docs/AMENDMENTS.md#IP-A28)) is "dual-host UI off ONE shared Razor
Class Library" — `IdiotProof.Blazor` and `IdiotProof.Maui` are meant to render the *same*
`IdiotProof.UI` components, never a forked copy of a page per host.

**Verified current state (read directly from both projects' source trees):**

- `IdiotProof.UI` has its first real occupants ([IP-A33](docs/AMENDMENTS.md#IP-A33), 2026-09-05):
  the Options section's components under `Components/Options/` (`OptionsChainView`,
  `OptionOrderTicket`, `OptionPositionTracker`, `OptionsLiveElevationModal`, plus
  `OptionsPresenter` and the view-model records) and `wwwroot/css/options.css`. The RCL references
  only `IdiotProof.Models`, `IdiotProof.Brokers`, and `IdiotProof.Shared` — never a host — and its
  components are presentational (data in via parameters, actions out via `EventCallback`s).
  `IdiotProof.Blazor` now has a `ProjectReference` to it and composes those components from the
  thin host page `Components/Pages/Options.razor`. The RCL template files (`Component1.razor`,
  `ExampleJsInterop.cs`) were removed.
- `IdiotProof.Maui/Components/Pages/` still contains only `Home.razor`, `Counter.razor`,
  `Weather.razor`, `NotFound.razor` — the stock MAUI Blazor Hybrid sample pages. `NavMenu.razor`
  links only to Home/Counter/Weather. The MAUI host has **not** been wired to the Options
  components (MAUI is deferred; the auth story there is unsolved).

Both projects build and are registered in `IdiotProof.slnx`. The dual-host plumbing now carries
one real feature on the Blazor side; the Strategies/Gapper/Learn pages have not been moved into
the RCL, and nothing is reachable from the MAUI shell yet. Treat `IdiotProof.Maui` as a scaffold
proving the hosting model compiles, not as a usable desktop client today.

---

## 10. The Research Scanner

`IdiotProof.ResearchScanner` (`Program.cs` + `ScanPassRunner`) runs **one scan pass and exits** —
designed to be fired by a Windows Scheduled Task
(`tools/register-research-scan-task.ps1`, written but not registered by default), decoupled from
both the Monitor's real-time trading loop and the Blazor host's request lifecycle. Per the
narrative in [IP-A32](docs/AMENDMENTS.md#IP-A32):

- Sweeps watchlist tickers plus a rotating batch of the tracked universe
  (`TickerUniverseService`/`TrackedTicker`, refreshed daily from Alpaca's asset list).
- `EdgarService`/`Form4Parser`/`CorporateActionDetector` pull real SEC filing content (Form 4
  insider transactions, 8-K item-code triage) rather than boilerplate summaries.
- `RegulatoryScanner` polls the Federal Register for SEC/SRO notices and has an LLM triage out
  routine noise, persisting substantive ones as macro `ResearchClaim` rows.
- `CatalystExtractor` composes a deterministic, sober-toned sentence per claim instead of trusting
  a single LLM-authored paragraph.
- `SignificanceScorer` combines magnitude/confidence/history/source-trust/recency/watchlist
  membership into a 0–100 score that `Research.razor`'s primary feed sorts by.
- `OutcomeBackfillService` fetches real historical prices to mark older claims
  Realized/Disproven, closing the loop between a claim and what the market actually did.

```bash
dotnet run --project IdiotProof.ResearchScanner
```

| Env var | Default | Notes |
|---|---|---|
| `IDIOTPROOF_RESEARCHSCAN_BATCHSIZE` | 300 | Tickers swept per pass beyond the watchlist. |
| `IDIOTPROOF_RESEARCHSCAN_DAYSBACK` | 2 | Lookback window per source per pass. |
| `IDIOTPROOF_RESEARCHSCAN_REGULATORY_HOURS` | 24 | Minimum hours between regulatory-scan cadences. |

---

## 11. The MindAttic family

IdiotProof is one of several MindAttic projects and follows two shared conventions:

- **Shared keyrings live in Roaming** (`%APPDATA%\MindAttic\<Subsystem>\providers.json`):
  `LLM` (owned by `MindAttic.Legion`), `Brokers` (owned by IdiotProof: `alpaca-paper`/`alpaca-live`),
  `Security` (owned by `MindAttic.Authentication`: pepper, bootstrap token).
- **Per-app state lives in Local** (`%LOCALAPPDATA%\MindAttic\<AppName>\`).

`legion.json` at the repo root configures the LLM voter panel and judge:

```json
{
  "voters": ["claude-api", "openai", "gemini", "deepseek"],
  "judge": "claude-api",
  "tier": "high"
}
```

Strategy generation is high-stakes (a single LLM hallucinating a verb costs real money), so the
panel cross-verifies before a script is ever shown to the user. All LLM traffic routes through
`MindAttic.Legion`; all LLM credential reads route through `MindAttic.Vault` — no feature code
calls an Anthropic/OpenAI SDK directly.

---

## 12. Building, running, and testing

### Prerequisites

- .NET 10 SDK
- SQL Server LocalDB (ships with Visual Studio, or install the SQL Server Express LocalDB package)
- Node 18+ (for Cypress only)

### First run

```bash
git clone <repo>
cd IdiotProof

# Restore + build the whole solution
dotnet build IdiotProof.slnx

# Apply migrations to LocalDB (creates the IdiotProof database)
dotnet ef database update --project IdiotProof.Blazor

# Run the Blazor host
dotnet run --project IdiotProof.Blazor
# → https://localhost:5001
```

In another terminal, optionally run the Monitor and/or the Research Scanner:

```bash
dotnet run --project IdiotProof.Monitor
dotnet run --project IdiotProof.ResearchScanner
```

To try the desktop shell (scaffold only today — see [§9](#9-the-maui-desktop-shell-and-the-shared-ui-library)):

```bash
dotnet run --project IdiotProof.Maui
```

### Configuration

- **Claude API key** — `%APPDATA%\MindAttic\LLM\providers.json` (the canonical MindAttic keyring),
  or paste into the API Keys page in-app.
- **Alpaca keys** — `%APPDATA%\MindAttic\Brokers\providers.json` with `alpaca-paper` and
  `alpaca-live` entries (see [§8](#8-the-blazor-web-app)).
- **`.env` (Development only)** — `%APPDATA%\MindAttic\IdiotProof\.env` prefills `DEV_USERNAME` /
  `DEV_PASSWORD` on the Login page; never loaded outside `Development`.

### Tests

Run the whole solution's NUnit suite:

```bash
dotnet test IdiotProof.slnx
```

Actual counts from a fresh Release run of this repo (see [§17](#17-known-gaps-and-roadmap) for the
two known-failing tests):

| Project | Passed | Failed | Notes |
|---|---:|---:|---|
| `IdiotProof.Engine.Tests` | 33 | 0 | RiskGuardian gate, SupervisedLoop resilience, WorkspaceManager |
| `IdiotProof.Indicators.Tests` | 18 | 0 | RSI/EMA/ATR/MACD/VWAP math, ADX Wilder-seed regression |
| `IdiotProof.Strategies.Tests` | 34,679 | 0 | DSL round-trip, backtester, gapper lifecycle, canonical-JSON contract — plus a large family of exhaustive combinatorial matrix test classes (`StrategyPermutationMatrixTests`, `StrategyThreeWayAndMatrixTests`, `ConditionalBlockOverridePermutationTests`, ...) that expand to tens of thousands of generated cases from a compact set of source files |
| `IdiotProof.Brokers.Tests` | 13 | 0 | BrokerRouter Sandbox-default + safe fallback, Sandbox fill simulation |
| `IdiotProof.Blazor.Tests` | 194 | 2 | Two pre-existing failures in `RegulatoryScannerTests` (`ScanAsync_AlreadySeenSourceUrl_IsSkippedOnSecondScan`, `ScanAsync_SubstantiveNotice_PersistsMacroClaim`) — not introduced or fixed by this README pass; everything else in the project is green |
| `IdiotProof.Monitor.Tests` | 4 | 0 | `PremarketFadeScanner` |

The large `IdiotProof.Strategies.Tests` count is a real, verified property of this codebase, not a
typo: the project deliberately generates exhaustive matrices over phase/condition/branch
combinations rather than hand-writing each case.

### Cypress (frontend E2E)

```bash
cd tests/IdiotProof.Cypress
npm install
npm run open    # interactive
npm run ci      # headless (Chrome, uses CYPRESS_BASE_URL or defaults to https://localhost:5001)
```

Seven specs (02–07 cover the strategy-authoring/save/activate round-trip, API-key masking + live
danger modal, Vault-backed AI generation, sample-strategy round-trips, backtest replay, and the
live `N/M` condition-progress badge). They run deterministically against `IDIOTPROOF_FAKE_LLM=1`
(the `FakeLlmHandler` test seam intercepts Legion calls server-side) but need a live server run to
count as fully proven end to end.

---

## 13. Export.ps1 and the other root scripts

`Export.ps1` (repo root) is a **generic source-export utility**, not IdiotProof-specific logic —
its comment header still calls itself a "Unity project" exporter, a leftover from the template it
was copied from. It walks the repo, hashes every matching file, and writes one flat text bundle
(`ExportedScripts.txt` in the current directory) containing a JSON manifest (path, SHA-256, byte
count, line count) followed by fenced `<<<FILE START>>> ... <<<FILE END>>>` blocks per file — handy
for pasting an entire codebase (or a slice of it) into an LLM context window.

```powershell
# From the repo root — exports every .cs file by default
powershell -NoProfile -ExecutionPolicy Bypass -File Export.ps1
```

Configuration lives at the top of the script itself:

- `$includeExtensions` — defaults to `.cs` only; `.prefab` / `.meta` / `.unity` / `.scene` lines
  are present but commented out (Unity-era leftovers, irrelevant to this .NET solution).
- `$excludeDirs` — skips `Library`, `Temp`, `Logs`, `Obj`/`obj`, `.git`, `.vs`, `Build(s)`,
  `Packages` (again, mostly Unity-shaped noise; `bin`/`obj` under each project are still walked
  unless they happen to match one of these literal segment names, so a fresh export can be large —
  point it at a narrower `$searchPath` or clean build output first if you want a lean bundle).
- `$assetsOnly` — `$false` by default (scans the whole repo, not just an `Assets/` folder).

Other root-level helper scripts (`zzz_Export.bat`, `zzz_Backup.bat`, `publish-all.bat`,
`tools/publish-all.ps1`) are local convenience wrappers; `tools/azure-provision.md` and
`tools/register-research-scan-task.ps1` document/automate one-time infra setup steps.
`tools/codex.ps1` is the documentation-canon tool described in [§16](#16-documentation-canon-and-further-reading).

`index.htm` + `package.json` at the repo root are an unrelated static-site pipeline: a
`README.md`(-flavored) marketing landing page renderer for `mindattic.com` (`marked` +
`highlight.js`, built via `node scripts/cli/build-html.js`) — **not** the codex-standard
`README.md → README.htm` doc-canon renderer this task's `tools/build-readme.ps1` wraps. The two
"HTML from Markdown" pipelines are separate systems that happen to share a repo.

---

## 14. Directory layout

```
IdiotProof/
├── IdiotProof.slnx / .sln                    ← Active solution (slnx is canonical; .sln kept for older tooling)
├── legion.json                               ← Legion voter panel (high tier)
├── Export.ps1                                ← Generic repo → text-bundle exporter (see §13)
├── CLAUDE.md                                 ← Project rules for AI tooling
├── README.md                                 ← You are here
├── TODO.md                                   ← Aspirational ghost-overlay/branching-viz plan (references the deleted IdiotProof.Core tree — historical, not current architecture)
├── docker-compose.yml / infra/                ← Azure deployment scaffolding (references paths/keys — e.g. src/, PolygonApiKey — that predate the current tree; verify before relying on it)
│
├── IdiotProof.Models/                        ← Domain DTOs
├── IdiotProof.Shared/                        ← RiskGuardian, IndicatorSnapshot
├── IdiotProof.Indicators/                    ← Pure indicator math + CandlestickPatterns
├── IdiotProof.Scripting/                     ← IdiotScript DSL
├── IdiotProof.Strategies/                    ← IStrategy, DslStrategy adapter, IndicatorSnapshotBuilder, GapperExitEvaluator, Backtester
├── IdiotProof.DataFeeds/                     ← IMarketDataFeed (Alpaca REST + streaming, Mock)
├── IdiotProof.Brokers/                       ← Alpaca + Sandbox + IBrokerClient + BrokerRouter
├── IdiotProof.Engine/                        ← DI root, AppSettings, BrokerCredentialStore-equivalent overlay, SupervisedLoop
├── IdiotProof.Blazor/                        ← Web app (the primary front door)
│   ├── Data/                                 ← AppDbContext, Strategy, UserPreferences, LearningArticle, ...
│   ├── Migrations/                           ← EF migrations
│   ├── Services/                             ← StrategyScriptGenerator, LlmVotingService, repositories, research pipeline
│   ├── Components/Pages/                     ← Strategies.razor, StrategyBuilder.razor, Gapper.razor, Learn.razor, Research.razor, ...
│   ├── Components/Shared/                    ← AccountSummaryBar.razor, StrategyBuilderRenderer.razor, WikiContent-equivalent, ...
│   └── wwwroot/css/_theme-alpaca.css, wwwroot/data/gapper-profiles.json
├── IdiotProof.Maui/                          ← Desktop shell (currently the default MAUI Blazor Hybrid template — see §9)
├── IdiotProof.UI/                            ← Shared Razor Class Library (currently the default RCL template — see §9)
├── IdiotProof.Monitor/                       ← 24/7 evaluator + executor console
├── IdiotProof.ResearchScanner/               ← One-shot research sweep console
├── IdiotProof.Engine.Tests/                  ← RiskGuardian + SupervisedLoop + Workspace (NUnit)
├── IdiotProof.Indicators.Tests/              ← Indicator math (NUnit)
├── IdiotProof.Strategies.Tests/              ← DSL round-trip + backtester + combinatorial matrices (NUnit)
├── IdiotProof.Brokers.Tests/                 ← BrokerRouter (NUnit)
├── IdiotProof.Blazor.Tests/                  ← StrategyScriptGenerator, LlmVotingService, research pipeline (NUnit)
├── IdiotProof.Monitor.Tests/                 ← PremarketFadeScanner (NUnit)
├── docs/                                     ← Codex canon (BIBLE.md, AMENDMENTS.md, USER_STORIES.md, rfc/) — see §16
├── tools/                                    ← codex.ps1, publish-all.ps1, azure-provision.md, build-readme.ps1, seed-*.sql
└── tests/
    └── IdiotProof.Cypress/                   ← End-to-end UI tests (Cypress 13, 7 specs)
```

---

## 15. Glossary

- **IdiotScript** — the fluent DSL (`Stock.Ticker("NVDA").RequireAdxAbove(20)...Build()`) that
  expresses a strategy as six lifecycle phases.
- **Phase** — one of the six fixed stages every strategy walks: Setup, Filters, Entry, Order, Risk,
  Exit. The parser rejects verbs used in the wrong phase.
- **Condition** — a single boolean check (`IsAboveVwap()`, `OnReclaim(9)`) composed with
  `.And()`/`.Or()`/`.Not()`.
- **Gate** — one of the three pre-fire checks: condition match → LLM voter quorum → Risk Guardian.
- **Risk Guardian** — `IdiotProof.Shared.Risk.RiskGuardian`, the final pre-trade veto; can block
  regardless of strategy or LLM consensus.
- **Monitor** — `IdiotProof.Monitor`, the unattended 24/7 console evaluator and executor.
- **SupervisedLoop** — the fault-tolerant tick loop the Monitor runs its per-pass work under.
- **Voter panel / Legion** — the multi-LLM quorum (`legion.json`) that approves or rejects a
  Claude-generated script or a candidate fire, via `MindAttic.Legion`.
- **ConditionProgress** — the SQL row (`N/M`, first failing verb) the Monitor upserts every pass
  and the Strategies page polls for live badges.
- **Sandbox broker** — the always-registered simulated broker (instant fills, in-memory position
  book) that is the safe default in `BrokerRouter`.
- **Gapper** — a stock gapping up in premarket vs. the previous close; the flagship trade: buy in
  the 4 AM window, sell before the 9:30 bell.
- **Gapper profile** — a dialable template (gap %, volume ratio, price band, entry window, stops,
  giveback, arm/sell-by times, notional), stored in `wwwroot/data/gapper-profiles.json`, cloned and
  tuned per ticker on the Gapper tab.
- **Peak giveback** — the momentum-rollover exit: sell once price gives back N% of the run from
  entry to the post-entry peak; armed from a configured ET time.
- **Previous close** — the prior trading day's official close; the reference for gap %. Gap
  conditions fail closed without it.
- **Research claim** — one `ResearchClaim` row: a catalyst or portent extracted from a filing,
  news article, or regulatory notice, with sentiment/magnitude/timing and a significance score.
- **Macro claim** — a `ResearchClaim` with `IsMacro = true`: a regulatory/exchange-rule event that
  isn't about one company.
- **Significance score** — the 0–100 value `SignificanceScorer` computes per claim; the Research
  tab's ranked feed sorts by it.
- **Tracked ticker** — a cached row in `TrackedTicker` (symbol, exchange, latest price) forming the
  research scanner's ticker universe, refreshed daily from Alpaca's asset list.
- **BrokerMode** — the per-strategy routing choice (Paper / Live / Sandbox) that overrides the
  global account's paper/live flag for that one strategy.

---

## 16. Documentation canon and further reading

This README is the practical "how to build/run/tour the system" layer. `docs/` carries the
authoritative, versioned canon under the MindAttic Codex convention:

- **[docs/BIBLE.md](docs/BIBLE.md)** (L0) — what IdiotProof IS / is NOT, the architecture canon,
  the project laws (`IP-LAW-n`), verified build/test state, and the full glossary. Stable section
  IDs (`{#IP-§N}`), stable law IDs (`{#IP-LAW-n}`).
- **[docs/AMENDMENTS.md](docs/AMENDMENTS.md)** (L1) — the append-only change log (`IP-A<n>`); an
  amendment **wins** over the bible where they disagree. 32 amendments as of this writing, from
  the initial README/graph reconciliation ([IP-A1](docs/AMENDMENTS.md#IP-A1)/[IP-A2](docs/AMENDMENTS.md#IP-A2))
  through the autonomous research scanner ([IP-A32](docs/AMENDMENTS.md#IP-A32)).
- **[docs/USER_STORIES.md](docs/USER_STORIES.md)** (L2) — stories `IP-US-<Epic><n>`; every `✅`
  cites its verifying NUnit/Cypress test. Epics observed: A (Risk Guardian), B (Monitor loop), C
  (DSL & backtesting), D (indicator math), E (web authoring/generation), F (doc/graph
  reconciliation), G (ghost overlay — planned), H (tooling hardening — planned), I (Learning
  Center — planned), J (Backtest UI — planned), K (Gapper), R (replay/scanner/ML dataset), S
  (adaptive auto-strategy generation — planned), T (autonomous research scanner).
- **[docs/rfc/](docs/rfc/)** — design notes: core tree reconciliation (0001), the Gapper +
  pipeline unification (0002), the autonomous research scanner (0003).
- **[docs/BIBLE.digest.md](docs/BIBLE.digest.md)** — GENERATED by `tools/codex.ps1 digest`. Never
  hand-edit.

Rules of engagement (from `CLAUDE.md`): a fact lives in exactly one layer, referenced by ID rather
than by line number; after editing canon, run `powershell -File tools/codex.ps1 doctor` (must exit
0); mark `✅` only when a test or build actually proves it.

---

## 17. Known gaps and roadmap

Verified, in-code or in-canon items worth knowing about before you build on top of this system:

- **The MAUI half of the dual-host story is scaffolding only.** `IdiotProof.UI` now holds the
  Options components, but `IdiotProof.Maui` is still the default template and doesn't render them —
  see [§9](#9-the-maui-desktop-shell-and-the-shared-ui-library).
- **Options are manual-only (Phase 1, [IP-A33](docs/AMENDMENTS.md#IP-A33)).** No option legs in
  the strategy schema, no IV/Greeks conditions, no options-aware `RiskGuardian` math, no
  multi-leg spreads — the Monitor never fires an options order. As of 2026-09-05 neither the paper
  nor the live Alpaca account reports an `option_trading_level` (options not enabled), so a real
  paper round-trip (IP-US-U10) is still open; the ticket locks itself on Alpaca modes until
  approval and Sandbox serves a synthetic chain in the meantime. `sp-index-events.json` is
  hand-maintained (the 2026-09-21 rebalance batch is verified against the S&P DJI press release).
- **`dotnet run` on `IdiotProof.Blazor` needs a `wwwroot` folder next to the built exe.**
  `Program.cs` mounts a `PhysicalFileProvider` on `AppContext.BaseDirectory/wwwroot` (so a
  published exe serves its own static files), which throws `DirectoryNotFoundException` on a plain
  Debug build where that folder doesn't exist. Run from a publish output, or create the folder
  (`bin/Debug/net10.0/wwwroot`) before `dotnet run`.
- **Shorts are signal-only.** A short candidate can clear both gates and gets recorded
  (`RecordFiredAsync`/`RecordEntryFillAsync`), but no order is placed — the exit-management brain
  (`GapperExitEvaluator`) is long-shaped. Order placement for shorts is future work.
- **`docs/BIBLE.md` §4.1/§4.2 references `PolygonDataFeed` and a `FeedType {Polygon}` enum that do
  not exist in the current `IdiotProof.DataFeeds`/`IdiotProof.Models` source** (verified: only
  `AlpacaDataFeed`, `AlpacaStreamingClient`, `MockDataFeed`, `SwitchableMarketDataFeed` exist; the
  `BrokerType`/enum set in `Enums.cs` has no `FeedType` at all). Blazor migrations show a
  `PolygonApiKey` column was added and later removed (`AddPolygonApiKey` →
  `RemovePolygonApiKey`), so Polygon support appears to have existed at some point and been pulled;
  this README describes only what's in the tree today. `docker-compose.yml`/`infra/` still
  reference a `PolygonApiKey` env var and a `src/IdiotProof.Blazor/Dockerfile` path that doesn't
  exist in the current layout — that infra scaffolding predates the present project structure and
  should be verified/updated before relying on it for a real deploy.
- **`TODO.md`'s ghost-overlay plan references `IdiotProof.Core`** — a project tree the bible
  records as deleted 2026-06-07 ([IP-A2](docs/AMENDMENTS.md#IP-A2)). The feature intent (chart
  playback with branch forking) is still live in `docs/USER_STORIES.md` Epic G; the concrete file
  paths in `TODO.md` are stale.
- **`SettingsKv` and `UserPreferences.OpenStrategyTabs`** — present in the schema, not fully
  consumed yet (per the bible's active-frontier notes); either wire up a consumer or remove them.
- **RiskGuardian config isn't exposed on the Settings page yet** — `SetRiskConfigAsync` exists but
  is uncalled from the UI as of the bible's last audit.
- **Backtest UI is stub-level** — `Backtest.razor` exists and the `StrategyBacktester`/
  `BacktestReport` pipeline is real and tested, but the per-candle condition-table UI enhancement
  described in Epic J is not built.
- **Roslyn-based parser** — `ScriptParser` is intentionally a tolerant, regex-driven parser today;
  a proper Roslyn-based parser with exact line/column diagnostics is future work (`IP-US-H1`).

For the living, authoritative version of this list, read `docs/BIBLE.md §7` ("Active frontier")
and the most recent entries in `docs/AMENDMENTS.md` — this README summarizes them but the canon
wins if they ever disagree.

---

## License

Internal MindAttic project.
