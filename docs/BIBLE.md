---
codex: 1
project: IdiotProof
code: IP
layer: bible
status: living
updated: 2026-10-03
---

# IdiotProof — Project Bible
> Single source of truth for what IdiotProof IS, is NOT, and the rules that keep it coherent.
> README says how to build/run; this says how to think about the system.

## 1. The one sentence {#IP-§1}
IdiotProof turns a plain-English trade idea into a runnable DSL strategy that a 24/7 console
Monitor evaluates against live market data, fires only when every condition matches, an LLM
voter panel approves, and the Risk Guardian clears it — then places the order through the
broker router and manages the position to its exit. The flagship flow is the **Gapper**
([§4.4](#IP-§4)): buy the premarket gap at 4AM, sell it off before the 9:30 bell once momentum
rolls over.

## 2. The product promise {#IP-§2}
- **Describe, don't code.** A trader writes prose ("if NVDA pulls back to the 9 EMA in an
  uptrend with volume confirmation, go long with a 1% stop"); Claude (via MindAttic.Legion)
  translates it into **IdiotScript**, the project's fluent DSL. The verb catalog is produced by
  *reflecting* on the real `StrategyBuilder` + `Conditions` types, so a model can never invent
  syntax that does not compile; generated scripts are parse-checked and the user is told when
  a verb did not parse.
- **Set and forget.** `IdiotProof.Monitor` is an unattended console host that re-reads every
  active strategy from SQL each tick (default **1s**, `IDIOTPROOF_MONITOR_INTERVAL` overrides —
  UI edits apply to the running console automatically), evaluates against live Alpaca data
  (real-time SIP websocket stream + REST, Mock fallback), reports per-condition progress
  (`4/5 — waiting on OnReclaim(9)`), places gated entries, and manages open positions to their
  exit.
- **The Gapper, done well.** Queue up to 3 tickers on the `/gapper` tab, each with a dialable
  profile (gap %, volume, price band, entry window, stops, peak-giveback, sell-by). All gappers
  are not the same — every value is per-ticker adjustable; the tuned result is written into the
  strategy's canonical JSON so what you dialed is exactly what runs. A past day can be replayed
  with the same brain and its hindsight-tuned dials applied back.
- **Three gates before money moves.** All strategy conditions match → LLM voter quorum
  approves → Risk Guardian clears stop/daily-loss/per-trade-risk. Any gate blocks the fire and
  records the reasoning.
- **Paper by default, live by explicit opt-in.** The Sandbox broker is always the safe
  fallback; live trading requires explicit configuration plus a confirmation modal.
- **Research finds the news for you.** A scheduled scanner sweeps SEC filings, Alpaca news and
  Federal Register notices across the tracked ticker universe and ranks what it finds by
  significance on the `/research` tab — no search box required.
- **Options without mental math.** The manual `/options` section shows premium, breakeven and
  the intrinsic ("real") vs extrinsic ("hype") split for every contract, so the trade is "buy
  the idea, sell the hype".

## 3. What it is NOT {#IP-§3}
- **Not a charting terminal.** It does not stream tick charts for manual discretionary trading;
  the chart/ghost-overlay work is [planned, not built](#IP-§7).
- **Not multi-broker today.** The build is **Alpaca-only** (plus the simulated Sandbox).
  `IBrokerClient` is the seam a future broker would implement; no other adapter exists.
- **Not a direct-to-vendor LLM client.** No feature code calls an Anthropic/OpenAI SDK directly;
  all LLM traffic routes through MindAttic.Legion and all keys resolve through MindAttic.Vault.
- **Not a gate-bypassing autotrader.** The Monitor places orders only through
  `BrokerRouter`/`IBrokerClient` after all three gates clear, never around the Risk Guardian.
  Exit orders are risk-reducing: they skip the LLM panel by design but are always audit-logged.
  Short candidates are signal-only: they clear the gates and are recorded, but no short order is
  placed.
- **Not an options autotrader.** Options orders are manual and user-initiated on `/options`;
  the DSL, Monitor, `RiskGuardian` and `Conditions` catalog know nothing about options.
- **Not a desktop app.** `IdiotProof.Blazor` (Blazor Server) is the only UI host.
- **Not a research daemon.** `IdiotProof.ResearchScanner` runs one pass and exits; it is fired
  by a Windows Scheduled Task and never shares the Monitor's trading loop.

## 4. Architecture canon {#IP-§4}

```
                          Trader (browser)
                                 │
                  ┌──────────────▼───────────────┐
                  │       IdiotProof.Blazor       │◄──────  Cypress E2E (tests/IdiotProof.Cypress)
                  │ Strategies · Builder · Gapper │
                  │ Research · Options · Backtest │──► StrategyScriptGenerator ──► Legion
                  │ Learn · Settings · API Keys   │                                (legion.json)
                  │  + IdiotProof.UI (RCL)        │
                  └──────────────┬────────────────┘
                                 │  SQL Server (Strategies, ConditionProgress, TradeDiary, …)
          ┌──────────────────────┼──────────────────────────┐
          │                      │                          │
┌─────────▼─────────┐  ┌─────────▼──────────────┐  ┌────────▼──────────────────┐
│ IdiotProof.Monitor│  │ IdiotProof.Engine      │  │ IdiotProof.ResearchScanner│
│ 24/7 SupervisedLoop│ │ DI root · AppSettings  │  │ one-shot scheduled pass   │
│ three gates →      │ │ SupervisedLoop ·       │  └───────────────────────────┘
│ BrokerRouter →     │ │ AuditLogger            │
│ exit management    │ └────────────────────────┘
└──┬───────────┬─────┘
   │           │
┌──▼─────────┐ ┌▼───────────┐ ┌──────────────────┐ ┌────────────────────────┐
│ IdiotProof.│ │ IdiotProof.│ │ IdiotProof.      │ │ IdiotProof.Brokers     │
│ Scripting  │ │ Strategies │ │ DataFeeds        │ │ IBrokerClient ·        │
│ IdiotScript│ │ DslStrategy│ │ Alpaca (REST+ws) │ │ Alpaca · Sandbox ·     │
│ StrategyJson│ │ Backtester│ │ Mock · Switchable│ │ BrokerRouter · options │
└──────┬─────┘ └──────┬─────┘ └──────────────────┘ └────────────────────────┘
       │              │
┌──────▼─────┐ ┌──────▼─────────┐ ┌───────────────────────────────┐
│ IdiotProof.│ │ IdiotProof.    │ │       IdiotProof.Models       │
│ Indicators │ │ Shared         │ │  Candle TradeSignal Position  │
│ RSI EMA ATR│ │ RiskGuardian · │ │  OrderRequest OptionContract …│
│ MACD VWAP …│ │ Options math   │ └───────────────────────────────┘
└────────────┘ └────────────────┘
```

### 4.1 Projects (in `IdiotProof.slnx`)
| Project | Role |
|---|---|
| `IdiotProof.Blazor` | Blazor Server web app — Strategies, Strategy Builder (Guided/Script/Describe), Gapper, Research, Options, Learning Center, Backtest, Activity Log, Settings (incl. the six RiskGuardian limits), API Keys, Live chart. **MindAttic.Authentication** 6.0.0 (Argon2id + pepper, sessions, MFA scaffolding; security-alert email over SMTP from the Vault `Notifications` bucket when it is complete, else a startup warning; production self-service reset stays off) + EF Core 10 (SQL Server). Also hosts the shared services the Monitor and ResearchScanner reference (`AppDbContext`, repositories, `UserBrokerResolver`, `LlmVotingService`, research services). |
| `IdiotProof.Monitor` | **The one pipeline**: console host on `SupervisedLoop` (Windows-Service-installable, single-instance `sp_getapplock` leader lease) — re-reads active strategies every tick, evaluates conditions, upserts `ConditionProgress`, walks the three gates, places entries via `UserBrokerResolver`, manages open positions to exit via `GapperExitEvaluator`, feeds realized P&L into the RiskGuardian daily breaker, writes the trade diary. Also the operator CLI (`MonitorCli`) and the replay/scan/dataset commands ([§4.4](#IP-§4)). |
| `IdiotProof.Engine` | DI root (`ServiceRegistration`), `AppSettings` overlay chain, `SupervisedLoop`, `AuditLogger`, `WorkspaceManager` (UI layout state only). |
| `IdiotProof.Scripting` | The IdiotScript DSL: `Stock.Ticker(...)`, `StrategyBuilder`, the `Conditions` catalog, `ScriptParser`, `StrategyJson` + `StrategyLoader` (canonical JSON), branching algebra, `EmaPeriodCollector`, `GapperProfile` + `GapperScriptFactory`, `MarketTime` (ET clock, trading-day gate). |
| `IdiotProof.Strategies` | `IStrategy` + `DslStrategy` adapter + `IndicatorSnapshotBuilder` + `GapperExitEvaluator` (sell-off brain, long and short) + `StrategyBacktester`/`BacktestReport` + `GapperDayBacktester`. |
| `IdiotProof.Indicators` | Pure indicator math: ADX, ATR, Bollinger, CCI, EMA, MACD, OBV, RSI, SMA, Stochastic, VWAP, WilliamsR, `CandlestickPatterns`. |
| `IdiotProof.DataFeeds` | `IMarketDataFeed` (+ `GetPreviousCloseAsync` for gap math): `AlpacaDataFeed` (REST, sip default with iex fallback), `AlpacaStreamingClient` (websocket trades + minute bars), `MockDataFeed` (deterministic premarket-gap simulation, no weekend bars), `SwitchableMarketDataFeed`. |
| `IdiotProof.Brokers` | `IBrokerClient` (incl. `IsPaper`) + `AlpacaBrokerClient` + `SandboxBrokerClient` + `BrokerRouter` + `AlpacaOAuthClient`. Options-aware: contract catalog, data-host snapshots (Greeks/IV), single-leg option orders, `us_option` positions, options account level; Sandbox serves a synthetic chain. |
| `IdiotProof.Models` | Domain DTOs/enums (the nouns, see 4.2) — incl. `AssetClass`, `OptionContract` (OCC), `OptionQuote`. |
| `IdiotProof.Shared` | `RiskGuardian` + `RiskGuardianConfig`/`Result`, `IndicatorSnapshot`, `LogMessage`, `SettingsMetadata`, and `Options/` (pure math: `IntrinsicValueCalculator`, `BlackScholesCalculator`, `SellSignalEvaluator`, `OptionsTradingLevel`). |
| `IdiotProof.UI` | Shared Razor Class Library consumed by `IdiotProof.Blazor`: the Options components (`OptionsChainView`, `OptionOrderTicket`, `OptionPositionTracker`, `OptionsLiveElevationModal`, `OptionsGlossary`/`<Jargon>`). Presentational only — depends on Models/Brokers/Shared, never on a host. |
| `IdiotProof.ResearchScanner` | One-shot, Scheduled-Task-fired console app — sweeps EDGAR/Alpaca/Federal Register for market-moving events across the tracked ticker universe, scores significance, writes to the shared DB, exits. |

Test projects: `IdiotProof.Engine.Tests`, `IdiotProof.Indicators.Tests`, `IdiotProof.Strategies.Tests`,
`IdiotProof.Brokers.Tests`, `IdiotProof.Blazor.Tests`, `IdiotProof.UI.Tests`, `IdiotProof.Monitor.Tests`
(see [§6](#IP-§6)); Cypress E2E lives in `tests/IdiotProof.Cypress/`.

### 4.2 Domain model — the NOUNS (`IdiotProof.Models`, `IdiotProof.Shared`, `IdiotProof.Blazor/Data`)
- `Candle` — one OHLCV bar.
- `TradeSignal` — output of `IStrategy.Evaluate`; a candidate to fire, carrying the full
  take-profit ladder.
- `TradeSetup` / `RiskLimits` — decimal-priced inputs the `RiskGuardian` validates.
- `OrderRequest` / `OrderResult` / `Position` — broker-facing order lifecycle; carry
  `AssetClass` + `Option?` (equity by default).
- `OptionContract` (OCC symbol, underlying, expiration, strike, right, multiplier 100),
  `OptionQuote`/`OptionGreeks` (IV/Greeks null when the broker omits them).
- `StrategyDefinition` (in `IdiotProof.Scripting`) — the semantic model: phases + conditions +
  branches; persisted as canonical JSON ([IP-LAW-8](#IP-LAW-8)).
- SQL entities (`IdiotProof.Blazor/Data`): `Strategy` (owner, author, `BrokerMode`,
  `ScriptJson` canon + `ScriptText` view, `OriginTranscript`, open-position bookkeeping),
  `ConditionProgress`, `TradeDiaryEntry`, `AuditLog`, `UserApiKeys` (encrypted at rest),
  `UserPreferences`, `ReplayRun`/`ReplayTrade`/`ReplayBar`, `ResearchClaim`,
  `InsiderTransaction`, `TrackedTicker`, `BlockedEmailDomain`, `SettingsKv`, `Workspaces`.
- Enums: `TradeDirection`, `TradingSession`, `OrderType`, `OrderSide`, `PriceType`,
  `ConfidenceGrade`, `BrokerType {Alpaca, Sandbox}`,
  `StrategyType {Iti, BreakoutPullback, LowHigh, FluentDsl, Custom}`, `AssetClass {Equity, Option}`,
  `OptionRight {Call, Put}`, `WorkspaceState`.

### 4.3 Key services — the VERBS
- `Stock.Ticker(symbol)` → `StrategyBuilder` (`IdiotProof.Scripting`) — entry point to author IdiotScript.
- `StrategyLoader.Load(scriptJson, scriptText)` — the one materialization path: canon first;
  present-but-rejected canon quarantines; only canon-less legacy rows touch `ScriptParser`.
- `IStrategy.Evaluate(symbol, candles, context)` → `IReadOnlyList<TradeSignal>` (`IdiotProof.Strategies`).
- `DslStrategy` — adapts a parsed `StrategyDefinition` into an `IStrategy`.
- `IndicatorSnapshotBuilder.Build(...)` → `IndicatorSnapshot` (indicators, previous close/gap,
  window high/low, pivot higher-low/lower-high) consumed by condition evaluation.
- `StrategyBacktester.Run(...)` → `BacktestReport` incl. the per-candle `ConditionTable`
  (`IdiotProof.Strategies/Backtesting`); `GapperDayBacktester` replays a gapper day.
- `RiskGuardian.ValidateTrade(setup, ...)` → `RiskGuardianResult` (the final gate, `IdiotProof.Shared/Risk`);
  `RecordTradePnL(realized)` feeds every exit into the daily circuit breaker;
  `UpdateConfig` swaps limits without resetting the daily-loss counter.
- `SupervisedLoop.RunAsync(options, ct)` — fault-tolerant tick loop with backoff + heartbeat file.
- `UserBrokerResolver.ResolveAsync(userId, brokerMode)` — per-strategy broker routing ([§4.4](#IP-§4)).
- `IBrokerClient.PlaceOrderAsync(...)` via `BrokerRouter` (Sandbox is the always-registered fallback;
  the Monitor's `Program.cs` is the one construction site).
- `GapperScriptFactory.ToScript(symbol, profile)` — tuned profile → round-trip-safe IdiotScript.
- `GapperExitEvaluator.Evaluate` / `.EvaluateShort` — sell-by / stops / take-profit /
  peak-giveback verdict for a held position (pure, clock-free, unit-tested).
- `IMarketDataFeed.*` — Alpaca (REST + websocket stream), Mock, Switchable;
  `GetPreviousCloseAsync` supplies gap math's reference close.
- `StrategyRepository` — the only writer of strategy rows; owns the mutation guards
  ([IP-LAW-11](#IP-LAW-11)).
- **Research subsystem** (`IdiotProof.Blazor/Services`): `TickerUniverseService`, `EdgarService`,
  `Form4Parser`, `CorporateActionDetector`, `RegulatoryScanner`, `IndexEventScanner`,
  `CatalystExtractor`, `OutcomeBackfillService`, `SignificanceScorer`, `ResearchService`
  ([§4.4](#IP-§4)).

### 4.4 Subsystems
**The Monitor tick (one pipeline, SQL as the bus).** `Strategy` rows are the only strategy
runtime state; the Gapper and Strategies pages write them, the Monitor reads them each tick.
Per tick: trading-schedule gate (`TradingSchedule.Classify`; weekends and outside-session
moments never enter) → re-read active rows → candles from a rolling per-symbol cache (REST,
topped up by the websocket stream; empty windows and missing previous closes negative-cached
for 30s) → load canon → resolve branches → manage an open position or walk entry conditions →
upsert `ConditionProgress` → on a full pass, LLM gate then Risk gate → place the entry. Premarket
and after-hours entries are limit + DAY + `extended_hours`; regular-hours entries are a
marketable limit. Before any exit order the Monitor reconciles its bookkeeping with the broker's
positions (no broker position → phantom bookkeeping cleared, no order; after a 90s grace for a
still-working entry); when several of a user's strategies share a symbol, aggregate
reconciliation is skipped and per-strategy bookkeeping is trusted. A sell-by position that
outlived its entry's ET day flattens at the first evaluated instant; exit orders outside a
weekday 04:00–20:00 ET window defer visibly. Every buy/sell writes a `TradeDiary` row
(denormalized, FK-free, log-and-continue).

**Broker routing.** Each strategy declares `BrokerMode` (Paper | Live | Sandbox).
`UserBrokerResolver` sends Paper/Live to the owner's own Alpaca account only when the owner has
opted in on the API Keys page (`DefaultBroker = "alpaca"`) and a key pair for that mode exists
(MindAttic.Vault Brokers bucket first, then the DB-encrypted pair); otherwise, and always for
Sandbox, it returns the global router's active broker (Sandbox by default). Clients are cached
per (user, mode) for 5 minutes and rebuilt when the key fingerprint changes. The Monitor and the
Blazor host share one Data Protection key ring (app name "IdiotProof"; dev
`%APPDATA%\MindAttic\DataProtection\IdiotProof`; prod either Azure Blob + Key Vault via
`DataProtection:AzureBlobUri` + `DataProtection:KeyVaultKeyUri`, or a durable
`DataProtection:KeyRingPath`; production with neither fails closed) so the console can decrypt
the keys the UI writes. Market data is one global feed (sip by default,
`IDIOTPROOF_ALPACA_FEED=iex` for the free tier).

**The Gapper.** Profiles are a static JSON catalog
(`IdiotProof.Blazor/wwwroot/data/gapper-profiles.json`, [IP-LAW-7](#IP-LAW-7)) of templates:
screen (gap %, volume ratio, price band), ET entry window (default 04:00–09:00), stop and
trailing stop, peak giveback + arm time (default 09:15), hard sell-by (default 09:28), notional.
DSL verbs: `RequireEntryWindow`/`EntryWindow`, `IsGapUp`/`IsGapBetween` (fail closed without a
previous close), `PeakGiveback(pct, arm)`, `SellBy(time)`. Momentum-rollover exit: track the
post-entry peak; once armed, sell when price gives back N% of the entry→peak run; `SellBy`
always flattens before the bell. At most 3 active gappers per user and one active gapper per
symbol, enforced in SQL. The "From a transcript" panel (`GapperInterpreter`, via Legion) turns
natural language into candidate cards that are re-validated fail-closed and queued only by a
human click. "Backtest a day" (`GapperDayBacktester`) walks the same condition list and the same
`GapperExitEvaluator` as live, reports MFE/MAE, a giveback grid and hindsight suggestions, and
offers a tuned profile the user applies manually.

**Replay, scan and dataset (Monitor CLI).** `replay` walks a past ET session's Alpaca bars
through the live evaluator and exit brain (shared `MarketTime.IsInsideSession` gate) and reports
each round-trip; tickers without a saved strategy use a gapper profile or a built-in family
(`momentum`, `reversal`, `emabreak`, `rthdrive`, `rsireversal`, `swingreversal`, `shortfade`).
Each run is a `ReplayRun` row with normalized `ReplayTrade`/`ReplayBar` feature rows; the HTML
archive is a view regenerated from SQL (`replay-regen`); `scan` pulls Alpaca's movers and replays
each gapper; `replay-export` writes ML-ready CSVs from the feature tables. Replay never places
orders. Other operator commands: `status`, `set-keys` (validates prefix + live probe),
`create-strategies`, `create-account`, `test-order`, `flatten`, `resync-canon`, `auto-gapper`,
`premarket-fade`.

**Research.** `IdiotProof.ResearchScanner` runs one pass: watchlist tickers plus a rotating
batch of the tracked universe (`TrackedTicker`, refreshed daily from Alpaca's asset list); real
Form 4 transactions (`InsiderTransaction`); 8-K item-code triage with real document fetch only
for 1.01/2.01/3.02/3.03/5.03; Federal Register SRO notices triaged by an LLM into macro claims
(`IsMacro = true`, affected tickers only when resolvable — never fabricated); S&P index
add/remove events from the hand-maintained `wwwroot/data/sp-index-events.json`
(`ClaimType = "IndexEvent"`, Pending until effective). Claims are deduped by (ticker, source
URL); display sentences are composed deterministically from structured fields
(`"{Summary}. Affects {Ticker} because {Mechanism}. Expected impact: {ExpectedTimeline}."`);
`OutcomeBackfillService` marks old-enough claims Realized/Disproven from real price history
before `SignificanceScorer` ranks. `tools/register-research-scan-task.ps1` registers the
Scheduled Task by hand; nothing registers it automatically.

**Options (manual).** `/options` (`Options.razor` + `OptionsTradingService`) offers single-leg
calls and puts on Sandbox, Paper or Live, defaulting to Sandbox unless Alpaca routing is opted
in and keyed. The chain spans today to `AlpacaBrokerClient.ChainHorizon` (3 years). Pricing math
is pure (`IdiotProof.Shared/Options`): intrinsic/extrinsic/breakeven/DTE, Black-Scholes value +
implied-vol solver (European exercise, no dividends), and the informational
`SellSignalEvaluator` (extrinsic within 5% of its observed high after ≥3 samples **and** a
Bullish claim on the underlying in the last 7 days). Orders are whole contracts, DAY, no
extended hours, Market/Limit; the ticket locks per action by `OptionsTradingLevel`
(0 none · 1 covered only · 2 long calls/puts · 3 spreads) and Live needs the 5-minute password
elevation. Jargon has one source, `OptionsGlossary`. A user-initiated options order is outside
the Monitor, so the three gates do not apply to it ([IP-LAW-1](#IP-LAW-1)).

**Accounts.** Registration and the CLI `create-account` reject malformed and disposable email
domains (`BlockedEmailDomain`, seeded at startup). The password-reset and username-listing
endpoints exist in Development only. `/login` forwards only same-site return URLs. The SignalR
`TradingHub` requires authentication. `AlpacaOAuthClient` + `/connect/alpaca` store a scoped
token, which is not yet used for order routing.

## 5. The Laws {#IP-§5}
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
voting is disabled or no Claude key is configured. Exits are risk-reducing and skip the LLM
panel but are audit-logged and honor the Risk Guardian kill-switch. User-initiated manual orders
(the Options section) are not automated fires; they are governed by the Paper/Live consent rule
and Live password elevation instead. (Risk gate: `RiskGuardian*` tests; LLM gate:
`IdiotProof.Blazor/Services/LlmVotingService.cs`; condition layer: `ConditionFailClosedTests`.)

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

## 6. Verified state {#IP-§6}
Build/test evidence (recorded 2026-10-03, .NET 10 SDK): `dotnet test IdiotProof.slnx -c Debug`
→ build succeeded, **all green, 0 failed**: Engine 85 · Indicators 18 · Strategies 34,681
(dominated by generated parameter cases) · Brokers 33 (+3 `[Explicit]` real-paper tests not run)
· Blazor 201 · UI 62 · Monitor 10.

Test projects and what they pin:
- `IdiotProof.Engine.Tests` — RiskGuardian gate incl. `RecordTradePnL` day rollover and
  `UpdateConfig` preserving the daily loss; SupervisedLoop resilience; WorkspaceManager cache
  hydration; options pricing (`OptionsPricingTests`); AppSettings credential overlay.
- `IdiotProof.Indicators.Tests` — RSI/EMA/ATR/MACD/VWAP math + ADX Wilder-seed regression.
- `IdiotProof.Strategies.Tests` — DSL round-trip, backtester (ET time exits, trailing/giveback
  replay), gapper factory/conditions/exits (`GapperTests`), mock-gap-day lifecycle
  (`GapperLifecycleTests`), canonical JSON (`StrategyJsonTests`), day replay
  (`GapperDayBacktesterTests`), EMA collector, scale-out ladder (`DslStrategySignalTests`),
  trading-day gate (`MarketTimeTests`), fail-closed conditions (`ConditionFailClosedTests`),
  window-scoped latches (`WindowScopedConditionTests`), culture-safe text (`ScriptTextRoundTripTests`).
- `IdiotProof.Brokers.Tests` — BrokerRouter Sandbox default, sandbox fills, Alpaca extended-hours
  contract, options wire format + Sandbox chain/basis (`OptionsBrokerTests`); the opt-in
  `[Explicit]` `AlpacaPaperOptionsIntegrationTests` runs only by name against the real paper account.
- `IdiotProof.Blazor.Tests` — verb-catalog reflection, LLM voting consensus + fail-closed vote
  parsing, `ConditionProgressRepository` (SQL Server LocalDB), `StrategyRepositoryGuardTests`,
  `UserBrokerResolverTests`, `GapperInterpreterTests`, `LegionProviderContractTests`, research
  (`EdgarServiceTests`, `Form4ParserTests`, `CorporateActionDetectorTests`,
  `RegulatoryScannerTests`, `SignificanceScorerTests`, `OutcomeBackfillServiceTests`,
  `IndexEventScannerTests`, `TickerUniverseServiceTests`), `RiskGuardianServiceTests`,
  `UserPreferencesServiceTests`.
- `IdiotProof.UI.Tests` — Options presenter / position view / glossary logic.
- `IdiotProof.Monitor.Tests` — `PremarketFadeScanner` and `BeBexDecayScanner` math.

Not proven by the solution test run: the Blazor UI flows and the live LLM voting round-trip.
The Cypress suite (`tests/IdiotProof.Cypress/cypress/e2e/`, specs 01–09) runs deterministically
with `IDIOTPROOF_FAKE_LLM=1` (the Development-only `FakeLlmHandler` intercepts Legion calls
server-side) but needs a live server run to graduate the E-stories to ✅; `08_options.cy.ts` has
been run green. MonitorWorker itself has no host-level harness test.

## 7. Active frontier {#IP-§7}
- **Options, phase 2** — the fill-and-close half of a real paper round-trip during market hours
  (IP-US-U10); extract the duplicated Live elevation modal into one shared component; then option
  legs in the strict-JSON strategy schema (v2), IV/Greeks conditions in the `Conditions` catalog,
  and a non-linear `RiskGuardian` model (max loss = premium for long options) before the Monitor
  may ever fire an options order. Multi-leg spreads (`order_class: "mleg"`) after that.
- **Gapper hardening (Epic K tail)** — host-level MonitorWorker test (queue → 4AM fire → hold →
  rollover sell), `/gapper` Cypress spec, short-side order placement and position management,
  fill-price reconciliation against the broker's actual fill (entry is recorded at the limit
  price), full order-state tracking (pending orders as first-class rows).
- **Known debts** — `LlmVotingService` hand-rolls a 3-persona Claude-only panel instead of
  Legion's native voter-panel API (legion.json declares claude-api/openai/gemini/deepseek); DSL
  generation is single-shot and the Describe tab still emits text, not model JSON; per-user
  **Claude** keys are not merged in the Monitor; the unused `UserPreferences.OpenStrategyTabs`
  column awaits removal in a migration; the Azure infra (`tools/azure-provision.md`) is not
  provisioned; the OAuth token is not
  yet wired into order placement (needs a registered Alpaca OAuth app + paper testing).
- **Replay tests (Epic R)** — the replay/scan/export/family commands ship without NUnit coverage.
- **Adaptive auto-strategy generation (Epic S)** — standardize the `auto-gapper` seed into a
  wait-for-enough-information generator across sessions.
- **Strategy ghost overlay + branching visualization** — see `TODO.md`: chart integration,
  simulator timeline, branch fork rendering. (Epic G.)
- **Roslyn-based IdiotScript parser** — replace the tolerant regex parser with exact
  line/col diagnostics. (IP-US-H1.)
- **Cypress CI run** — run the suite against a live server with `IDIOTPROOF_FAKE_LLM=1` to
  graduate IP-US-E1–E6, the Learning Center (Epic I) and Backtest (Epic J) stories.

## 8. Quality bar {#IP-§8}
A feature is **done** (`✅`) only when: it builds clean in `IdiotProof.slnx`; it has a green
automated test (NUnit for backend, Cypress for UI) that is named in
[USER_STORIES.md](USER_STORIES.md); user-facing changes have an e2e or lifecycle assertion;
and it respects the laws in §5 (gates in order, Sandbox default, Vault/Legion routing, no
underscore fields). Anything not proven by a test is `🟡`/`⬜`. (Inherits [HOUSE-LAW-8].)

## 9. Glossary {#IP-§9}
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
