---
codex: 1
project: IdiotProof
code: IP
layer: bible
status: living
updated: 2026-11-06
---

# IdiotProof — Project Bible
> Single source of truth for what IdiotProof IS, is NOT, and the rules that keep it coherent.
> README says how to build/run; this says how to think about the system.

## 1. The one sentence {#IP-§1}
IdiotProof turns a plain-English trade idea into a runnable DSL strategy that the Monitor,
running 24/7 inside the IdiotProof Hub, evaluates against live market data, fires only when
every condition matches, an LLM voter panel approves, and the Risk Guardian clears it — then
places the order through the Hub's transaction gateway and manages the position to its exit.
A person (or an assistant on their behalf) drives all of it through the `idiotproof` CLI, its
MCP tools or the web app. The flagship flow is the **Gapper**
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
- **Not a charting terminal.** It draws static ASCII charts (`market chart`, `account history`)
  and a web live chart, but it is not a streaming tick-chart workstation for discretionary
  trading; the ghost-overlay work is [planned, not built](#IP-§7).
- **Not multi-broker today.** The build is **Alpaca-only** (plus the simulated Sandbox).
  `IBrokerClient` is the seam a future broker would implement; no other adapter exists.
- **Not a direct-to-vendor LLM client.** No feature code calls an Anthropic/OpenAI SDK directly;
  all LLM traffic routes through MindAttic.Legion and all keys resolve through MindAttic.Vault.
- **Not a gate-bypassing autotrader.** Every order, automated or by hand, goes through the
  Hub's `TransactionGateway` ([IP-LAW-12](#IP-LAW-12)); automated entries clear all three gates,
  never around the Risk Guardian. Exit orders are risk-reducing: they skip the LLM panel by design
  but are always audit-logged.
- **Not an agent that moves real money on its own.** An assistant drives IdiotProof through the
  CLI or MCP, but a Live action only previews and returns a confirmation code; it happens only when
  the person approves and the code comes back ([IP-LAW-13](#IP-LAW-13)).
- **Not an options autotrader.** Options orders are manual and user-initiated (`/options`, or
  `options buy|sell|spread|close|exercise` from the CLI/MCP); the DSL, Monitor, `RiskGuardian` and
  `Conditions` catalog know nothing about options.
- **Not a desktop app.** Its front doors are the `idiotproof` CLI, the `idiotproof` MCP server
  and the `IdiotProof.Blazor` web app, all over the one Hub ([§4](#IP-§4)); the Hub runs headless
  as a console window or a Windows Service.
- **Not a research daemon.** `IdiotProof.ResearchScanner` (and `research scan`) runs one pass and
  exits; it is fired by a scheduler job and never shares the Monitor's trading loop. The one
  long-running research worker, the catalyst intake ([§4.4](#IP-§4)), writes research rows only,
  is off by default, and holds no broker client.
- **Not a trained predictor.** Nothing in the trading path reads a catalyst row or a formation
  snapshot yet; the gapper study ([§4.4](#IP-§4)) decides whether anything ever will, and its
  pre-registered prior is that the honest answer may be "catalyst latency is the edge".

## 4. Architecture canon {#IP-§4}

One long-running process, the **IdiotProof Hub**, owns everything that touches money or state.
Every front door is a thin client over the same reflected operation catalog; nothing places an
order except through the Hub's `TransactionGateway` ([IP-LAW-12](#IP-LAW-12)).

```
 person ─► Claude / any agent ─┬─► idiotproof (CLI, IdiotProof.Cli) ─┐
                               └─► idiotproof MCP (IdiotProof.Mcp) ──┤  HTTP 127.0.0.1:5910
 person ─► browser ─► IdiotProof.Blazor (same Core services) ────────┤  X-IdiotProof-Key
                                                                     ▼
 ┌───────────────────────────── IdiotProof.Hub ──────────────────────────────────────┐
 │ /api/op ─► OperationDispatcher ─► [Operation] handlers (IdiotProof.Operations)    │
 │            owner resolution · CommandLedger (redacted) · exit codes 0/1/2/3/4     │
 │ MonitorWorker (SupervisedLoop, SQL leader lease) + scanners   ── IP-LAW-10        │
 │ TransactionGateway ─► shape → route → Mock guard → RiskGuardian → LLM panel       │
 │                       → Live confirm (ConfirmationStore codes) → place → audit    │
 │ KnowledgeIndex (BIBLE, stories, tickets, XML docs, catalogs) for `docs *`         │
 └──────────────┬──────────────────────────────┬─────────────────────────────────────┘
                │ IdiotProof.Core (AddIdiotProofCore: AppDbContext, repositories,
                │ services, UserBrokerResolver, AlpacaApiFactory, Vault, DataProtection)
      ┌─────────▼─────────┐   ┌────────────────▼───────┐   ┌──────────────────────────┐
      │ SQL Server        │   │ IdiotProof.Brokers      │   │ IdiotProof.DataFeeds     │
      │ Strategies,       │   │ AlpacaApi (raw REST) ·  │   │ Alpaca REST + stream ·   │
      │ ConditionProgress,│   │ AlpacaBrokerClient ·    │   │ Mock · Switchable        │
      │ TradeDiary, Audit,│   │ Sandbox · BrokerRouter  │   └──────────────────────────┘
      │ CommandLedger …   │   └─────────────────────────┘
      └───────────────────┘
   Scripting (IdiotScript, StrategyJson, StrategyStamp) · Strategies (DslStrategy, backtesters)
   Indicators · Shared (RiskGuardian, options math) · Models · Engine (SupervisedLoop, AppSettings)
```

### 4.1 Projects (in `IdiotProof.slnx`)
| Project | Role |
|---|---|
| `IdiotProof.Hub` | **The one long-running process** (ASP.NET Core minimal API on `127.0.0.1:5910`, `IDIOTPROOF_HUB_PORT`). Hosts the Monitor (`MonitorWorker` on `SupervisedLoop` with the `sp_getapplock` leader lease; `IDIOTPROOF_HUB_MONITOR=0` serves operations without the trading loop) and its scanners, and serves `/api/health`, `/api/catalog`, `/api/op` (dispatch by argv or by name + JSON args), `/api/logs` and `/api/shutdown` behind the shared `X-IdiotProof-Key` (Vault `Tokens` bucket `idiotproof-hub`, or `IDIOTPROOF_HUB_KEY`). Runs in its own visible window that echoes every command, or as a Windows Service installed to run as the user (`hub install-service`, `tools/install-hub-service.ps1`) because LocalDB and the Data Protection ring are per-user. |
| `IdiotProof.Core` | The one service graph every host shares (`AddIdiotProofCore`): `AppDbContext` + migrations, repositories, every non-UI service (strategies, gapper, research, options, LLM voting, risk), `UserBrokerResolver`, `AlpacaApiFactory`, the `TransactionGateway` + `ConfirmationStore` + `OrderIntent` (`IdiotProof.Core/Trading`), `CommandLedgerRepository` + `SecretRedactor` (`IdiotProof.Core/Ledger`), `MonitorWorker` and the replay/scan/dataset engines (`IdiotProof.Core/Monitoring`), and the static JSON catalogs (`IdiotProof.Core/Catalogs`). |
| `IdiotProof.Operations` | Every capability as one `[Operation("noun verb")]` class with a typed args record (`[Argument]`/`[Option]`); `OperationCatalog` reflects them, `OperationDispatcher` binds argv or JSON, resolves the owner and maps the result to an exit code. The CLI flags, the MCP tool schemas and `docs/CLI_COMMANDS.md` / `docs/MCP_TOOLS.md` are all generated from it. Also the render kit (`Rendering/TextTable`, `KeyValueBlock`, `Fmt`, ET via `EasternClock`) and the knowledge index (`IdiotProof.Operations/Docs`). Runs only inside the Hub. |
| `IdiotProof.Cli` | `idiotproof` thin client: parses `noun verb --flags`, POSTs to the Hub, prints the result, exits 0 ok · 1 failed · 2 usage · 3 blocked · 4 confirmation required. Runs `help`, `version`, `agent bootstrap`, `agent export`, `hub status/start/stop/logs/version` and `users use/whoami` itself, so they work with the Hub down. `idiotproof.cmd` shim at the repo root. |
| `IdiotProof.Mcp` | `idiotproof-mcp` stdio MCP server: one tool per operation (`noun_verb`, schema from the args record), forwarded to `/api/op`; money tools take a `confirmation_code`. Resources: the BIBLE, user stories, agent protocol and live catalog. |
| `IdiotProof.Blazor` | Blazor Server web front door over the same `IdiotProof.Core` services — Strategies, Strategy Builder (Guided/Script/Describe), Gapper, Research, Options, Learning Center, Backtest, Activity Log, Settings, API Keys, Live chart. **MindAttic.Authentication** 6.0.0 (Argon2id + pepper, sessions, MFA scaffolding, self-service reset at `/forgot-password` → `/account/reset`). New work is CLI-first; the web keeps working (HOUSE-LAW-6). |
| `IdiotProof.Monitor` | `idiotproof-monitor`: a backward-compatible shim that starts the Hub (the Monitor lives there now); its operator subcommands forward to the matching Hub operations (`--local` keeps the old in-process path for replay/scan). |
| `IdiotProof.Engine` | `AppSettings` overlay chain, `SupervisedLoop`, `AuditLogger`, `WorkspaceManager` (UI layout state only). |
| `IdiotProof.Scripting` | The IdiotScript DSL: `Stock.Ticker(...)`, `StrategyBuilder`, the `Conditions` catalog, `ScriptParser`, `StrategyJson` + `StrategyLoader` (canonical JSON), `StrategyStamp` (the ASCII stamp), `StrategyHtml`, branching algebra, `EmaPeriodCollector`, `GapperProfile` + `GapperScriptFactory`, `MarketTime` / `TradingSchedule` (ET clock, trading-day gate). |
| `IdiotProof.Strategies` | `IStrategy` + `DslStrategy` adapter + `IndicatorSnapshotBuilder` + `GapperExitEvaluator` (sell-off brain, long and short, calling the shared `VelocityExitRule`) + `StrategyBacktester`/`BacktestReport` (replays sequences) + `GapperDayBacktester`. |
| `IdiotProof.Evidence` | Pure statistics and shared market math; references `IdiotProof.Models` only, so Strategies, Core and Operations can all call it. `Gappers/` is the one gapper feature and exit brain (`GapperFormationFeatures`, `TimeOfDayProfile`, `VelocityExitRule`, `MinuteBarMath`, `GapperVolumeRatio`) used by the live Monitor, `gapper backtest-day` and the study ([IP-LAW-10](#IP-LAW-10)); `Evaluation/`, `Search/` (BH, DSR, SPA), `EventStudy/`, `Economics/TradingCosts` and the rest back the `evidence *` operations and the research harnesses (`tools/EvidenceSweep`, `tools/EvidenceRun`, `tools/GapperStudy`). No I/O, no clocks. |
| `IdiotProof.Indicators` | Pure indicator math: ADX, ATR, Bollinger, CCI, EMA, MACD, OBV, RSI, SMA, Stochastic, VWAP, WilliamsR, `CandlestickPatterns`. |
| `IdiotProof.DataFeeds` | `IMarketDataFeed` (+ `GetPreviousCloseAsync`): `AlpacaDataFeed` (REST, sip default with iex fallback), `AlpacaStreamingClient` (websocket trades + minute bars), `MockDataFeed`, `SwitchableMarketDataFeed`. |
| `IdiotProof.Brokers` | `AlpacaApi` (raw Trading + Market Data REST: paging, 429 retry, `AlpacaApiException`), `IBrokerClient` + `AlpacaBrokerClient` + `SandboxBrokerClient` + `BrokerRouter` + `AlpacaOAuthClient`. Options-aware (contracts, snapshots, single-leg and multi-leg orders, exercise). |
| `IdiotProof.Models` | Domain DTOs/enums (the nouns, see 4.2) — incl. `AssetClass` (Equity, Option, Crypto), `OptionContract` (OCC), `OptionQuote`. |
| `IdiotProof.Shared` | `RiskGuardian` + `RiskGuardianConfig`/`Result`, `IndicatorSnapshot`, `LogMessage`, `SettingsMetadata`, and `Options/` (pure math: `IntrinsicValueCalculator`, `BlackScholesCalculator`, `SellSignalEvaluator`, `OptionsTradingLevel`). |
| `IdiotProof.UI` | Shared Razor Class Library for the Options components. Presentational only. |
| `IdiotProof.Settings` | The settings tab as a portable Razor class library, hostable by the web app or MindAttic.Vault.Desktop. |
| `IdiotProof.ResearchScanner` | One-shot research pass (EDGAR, Alpaca, Federal Register) that writes claims and exits; also reachable as `idiotproof research scan`. |

Test projects: `IdiotProof.Engine.Tests`, `IdiotProof.Indicators.Tests`, `IdiotProof.Strategies.Tests`,
`IdiotProof.Evidence.Tests`, `IdiotProof.Brokers.Tests`, `IdiotProof.Core.Tests`, `IdiotProof.Operations.Tests`, `IdiotProof.Cli.Tests`,
`IdiotProof.Mcp.Tests`, `IdiotProof.Blazor.Tests`, `IdiotProof.UI.Tests`, `IdiotProof.Monitor.Tests`
(see [§6](#IP-§6)); Cypress E2E lives in `tests/IdiotProof.Cypress/`.

### 4.2 Domain model — the NOUNS (`IdiotProof.Models`, `IdiotProof.Shared`, `IdiotProof.Core/Data`)
- `Candle` — one OHLCV bar.
- `TradeSignal` — output of `IStrategy.Evaluate`; a candidate to fire, carrying the full
  take-profit ladder.
- `TradeSetup` / `RiskLimits` — decimal-priced inputs the `RiskGuardian` validates.
- `OrderRequest` / `OrderResult` / `Position` — broker-facing order lifecycle; carry
  `AssetClass` + `Option?` (equity by default).
- `OptionContract` (OCC symbol, underlying, expiration, strike, right, multiplier 100),
  `OptionQuote`/`OptionGreeks` (IV/Greeks null when the broker omits them).
- `StrategyDefinition` (in `IdiotProof.Scripting`) — the semantic model: phases + conditions +
  branches; persisted as canonical JSON ([IP-LAW-8](#IP-LAW-8)). The root definition is step 0 of
  a sequence; `NextSteps` holds steps 1..N-1 and is empty for every single-step strategy
  ([IP-LAW-14](#IP-LAW-14)).
- `GapperFormationSnapshot` (in `IdiotProof.Evidence/Gappers`) — one forming-gapper feature row
  at one decision instant (gap, premarket volume, time-of-day volume ratio, 1/3/5/15-minute
  velocity, acceleration, volume rate-of-change, session VWAP, distance to the premarket high,
  run-up, days since reverse split / 424B / S-1, latest catalyst), plus an `Extra` dictionary for
  registered literature features.
- SQL entities (`IdiotProof.Core/Data`, namespace `IdiotProof.Core.Data`): `CommandLedgerEntry` (one row per CLI/MCP call), `PendingConfirmation` (Live codes), `Strategy` (owner, author, `BrokerMode`,
  `ScriptJson` canon + `ScriptText` view, `OriginTranscript`, open-position bookkeeping),
  `ConditionProgress`, `TradeDiaryEntry`, `AuditLog`, `UserApiKeys` (encrypted at rest),
  `UserPreferences`, `ReplayRun`/`ReplayTrade`/`ReplayBar`, `ResearchClaim`,
  `InsiderTransaction`, `TrackedTicker`, `BlockedEmailDomain`, `SettingsKv`, `Workspaces`,
  `CatalystEvent`/`CatalystEventSymbol` (research rows: one news item or filing with
  `PublishedUtc` and `ReceivedUtc`, pre-class, LLM fields, score; `IdiotProof.Core/Data/CatalystRows.cs`).
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
- `LlmVotingService.VoteOnSignalAsync(...)` — the LLM gate on MindAttic.Legion's voter panel;
  `UserClaudeKeyResolver.ResolveAsync(ownerId)` supplies the owner's Claude key ([§4.4](#IP-§4)).
- `DirectionalOrders` (`IdiotProof.Core/Monitoring`) — long/short entry and exit order shapes, held-share
  reconciliation and direction-aware realized P&L for the Monitor and `trade flatten`.
- `TransactionGateway.SubmitAsync(OrderIntent)` (`IdiotProof.Core/Trading`) — the only caller of
  `IBrokerClient.PlaceOrderAsync` / replace / cancel ([IP-LAW-12](#IP-LAW-12));
  `ConfirmationStore` issues and redeems Live codes ([IP-LAW-13](#IP-LAW-13)).
- `IBrokerClient` via `BrokerRouter` (Sandbox is the always-registered fallback; constructed once in
  `TradingServiceRegistration`).
- `OperationDispatcher.DispatchAsync(...)` (`IdiotProof.Operations`) — binds a call to its
  `[Operation]`, resolves the owner, runs it, ledgers it, maps the result to an exit code.
- `AlpacaApiFactory.ForOwnerAsync(owner, mode)` / `ForDataAsync(owner)` — raw Alpaca REST with the
  owner's own keys, for read operations and the gateway's broker calls.
- `StrategyStamp.ToStamp(definition)` / `.Decode(text)` — the ASCII stamp and its fail-closed paste-back.
- `KnowledgeIndexBuilder.Build()` → `KnowledgeIndex` — the `docs *` operations' index.
- `GapperScriptFactory.ToScript(symbol, profile)` — tuned profile → round-trip-safe IdiotScript.
- `GapperExitEvaluator.Evaluate` / `.EvaluateShort` — sell-by / stops / take-profit /
  velocity-exhaustion / peak-giveback verdict for a held position (pure, clock-free, unit-tested).
- `VelocityExitRule.Evaluate(barsSinceEntry, entry, now, ...)` → `VelocityExitVerdict`
  (`IdiotProof.Evidence/Gappers`) — "sell when the run's velocity dies"; fails closed (missing
  input = hold); mirrored for shorts. The one velocity exit for live, `gapper backtest-day` and the study.
- `GapperFormationFeatures.Compute(GapperFeatureInputs)` → `GapperFormationSnapshot`
  (`IdiotProof.Evidence/Gappers`) — the one no-lookahead feature computation for a forming gapper:
  trims every input to `EndUtc <= t` itself, forward-fills missing minutes as zero-volume bars so
  windows are wall-clock (`GapperFormationTests` appends post-t bars and asserts nothing changes).
- `CatalystIngestor.IngestAsync(CatalystIntake)` (`IdiotProof.Core/Catalysts`) — one news item or
  filing → dedupe on (Source, ExternalId) → `CatalystPreClassifier.Classify` → novelty count →
  `CatalystScorer.Score` → `CatalystEvent` row; live intake and backfill both come through it.
- `IMarketDataFeed.*` — Alpaca (REST + websocket stream), Mock, Switchable;
  `GetPreviousCloseAsync` supplies gap math's reference close.
- `StrategyRepository` — the only writer of strategy rows; owns the mutation guards
  ([IP-LAW-11](#IP-LAW-11)).
- **Research subsystem** (`IdiotProof.Core/Services`): `TickerUniverseService`, `EdgarService`,
  `Form4Parser`, `CorporateActionDetector`, `RegulatoryScanner`, `IndexEventScanner`,
  `CatalystExtractor`, `OutcomeBackfillService`, `SignificanceScorer`, `ResearchService`
  ([§4.4](#IP-§4)).

### 4.4 Subsystems
**The Hub and its front doors.** The Hub is the only process that opens the database for
trading, holds broker clients and places orders. The CLI and the MCP server hold no state: they
send `{argv | name + args, owner, output, confirmation code}` to `/api/op` with the shared key and
print or return the envelope (`status`, `exit_code`, `message`, `data`, `confirmation`). The owner
comes from `--user`, else the CLI's saved default (`users use`), else `IDIOTPROOF_OWNER`, else the
only user; it is never guessed among several. Every call writes one `CommandLedger` row (caller,
argv with secrets redacted by `SecretRedactor`, outcome, duration) that the gateway's AuditLog
rows link to. Operations are discovered by reflection, so adding an `[Operation]` adds a CLI
command, an MCP tool and a docs entry at once; `CatalogCompletenessTests` proves each one is
documented, has examples that bind and survives a JSON and an argv round trip.
`idiotproof agent bootstrap` (runs without the Hub) tells a fresh agent the Hub's health, its
owner, the catalog and the etiquette in [docs/agent/IDIOTPROOF_PROTOCOL.md](agent/IDIOTPROOF_PROTOCOL.md);
`idiotproof agent export` regenerates `docs/CLI_COMMANDS.md`, `docs/MCP_TOOLS.md` and
`docs/agent/operation-catalog.json`.

**The transaction gateway (the money path).** `TransactionGateway.SubmitAsync(OrderIntent)` is
the one path to a broker for every order-like action: Monitor entries and exits, CLI/MCP orders,
replaces, cancels, position closes and flattens, options and crypto orders, the web Options
ticket ([IP-LAW-12](#IP-LAW-12)). Gates in order, failing closed: shape → route by `BrokerMode`
(Sandbox default, Live never downgrades, [IP-LAW-3](#IP-LAW-3)) → Mock-feed guard
([IP-LAW-9](#IP-LAW-9)) → Risk Guardian for entries ([IP-LAW-2](#IP-LAW-2)) → LLM panel for
automated entries ([IP-LAW-1](#IP-LAW-1)) → Live confirmation → place → audit (+ TradeDiary).
A Live action from the CLI or MCP returns exit 4 with a preview and a 6-character
`ConfirmationStore` code bound to the SHA-256 of the exact intent, single-use, 120 s, same owner
([IP-LAW-13](#IP-LAW-13)); the web confirms with the 5-minute password elevation instead, and the
Monitor needs none (activating a Live strategy was the confirmed act, and itself needs a code from
the CLI). Non-order Live mutations that move money (crypto withdrawals, whitelist changes) use the
same codes; Live account-config and strategy-mode changes too. Raw Alpaca access for read
operations goes through `AlpacaApiFactory.ForOwnerAsync(owner, mode)` / `ForDataAsync(owner)`
(the owner's own keys from Vault); [docs/ALPACA_COVERAGE.md](ALPACA_COVERAGE.md) maps every
Alpaca endpoint to its operation.

**The strategy stamp.** `StrategyStamp` (`IdiotProof.Scripting/StrategyStamp.cs`) renders a
strategy as a deterministic 72-column card generated only from its canonical JSON
([IP-LAW-8](#IP-LAW-8)): header (symbol, side, size, session, name, short id `IP-XXXX-XXXX` from
the canon's SHA-256), the six-phase flow with branches, the exit ladder, a drunken-bishop
fingerprint, and a `BEGIN IDIOTPROOF STRATEGY` armored payload (deflate + base64url + CRC-32).
`strategy stamp <id>` prints it; `strategy import` reads the armor back out of pasted text,
fails closed on any damage, and creates the strategy inactive in Sandbox through the repository
([IP-LAW-11](#IP-LAW-11)). Same canon → byte-identical stamp. A sequenced strategy renders one
`STEP k/N` header per step with a `THEN WHEN` connector card between steps; a single-step
strategy's stamp is unchanged (`StrategyStamp.cs`, `StrategyHtml.cs`).

**IdiotScript Steps (sequenced strategies, IP-T445).** One strategy can be written as ordered
Steps, each with its own direction, entry conditions, risk and exits, so "short NVDA into
earnings; once covered and price reclaims, go long" is ONE strategy, not two rows and an external
arm/disarm. The root `StrategyDefinition` is step 0; `Step("name")` right after the setup verbs
(`Ticker`, `Name`, `Session`, `Quantity*`) names it, and any later `Step("name")` closes the
current step and opens the next, seeded with the root's ticker and session
(`IdiotScript.cs`, `StrategyBuilder.Step`). `ThenWhen(condition)` is the transition out of a step:
evaluated only while flat after that step's exit; when every `ThenWhen` condition holds the next
step is armed; a step with no `ThenWhen` arms the next step as soon as it is flat. `Within(days)`
is the deadline: if the transition has not fired within that many days of the step's exit the
sequence ends. `Repeat` / `Repeat(n)` is root-only (the parser treats one inside a later step as
the root's, diagnostic IPS022): a cycle is one full run of the sequence, and the sequence restarts
at step 0 when its last step exits or a `Within` expires. A step never nests steps. In the canon
the keys `stepName`, `thenWhen`, `thenWithinDays` and `nextSteps` are written only when used
(`nextSteps` only on the root; `StrategyJson.RequireOnly` rejects it inside a step), so every
pre-existing strategy's JSON, stamp and script stay byte-identical — goldens for the seven samples
plus the gapper, velocity, branching and looped canons are asserted in `DslSequenceTests`
([IP-LAW-8](#IP-LAW-8), [IP-LAW-14](#IP-LAW-14)). `ScriptDiagnostics` adds IPS020 (`ThenWhen`
without a readable condition), IPS021 (`Step` inside `If`/`While`), IPS022 (`Repeat` in a later
step, warning), IPS023 (duplicate step name), IPS024 (a later step without `Long()`/`Short()`),
IPS025 (`ThenWhen`/`Within` with no following `Step`). Two worked examples
(`DslSequenceTests.cs`):

```
Ticker("NVDA").Step("short-into-earnings").Session(IS.RTH).Quantity(50)
  .IsBelowVwap().IsRsiBearishDivergence().Short().StopLossPercent(3).TakeProfitPercent(6).SellBy("15:55")
  .ThenWhen(HigherLows(3).And(RequireEmaStack(9, 21)).And(OnVwapReclaim())).Within(7)
  .Step("buy-back").Quantity(50).IsAboveVwap().IsEmaAbove(9).Long().StopLossPercent(2).TakeProfitPercent(5)

Ticker("GAPT").Session(IS.PREMARKET).Step("ride")
  .RequireEntryWindow("04:00", "09:00").IsGapBetween(5, 20).IsVolumeAbove(2).IsPriceBetween(1, 50)
  .Long().QuantityNotional(1000).StopLossPercent(5).PeakGiveback(25, "09:35").SellBy("11:00")
  .ThenWhen(IsLowerHigh().And(IsBelowVwap())).Within(1)
  .Step("fade").RequireEntryWindow("09:35", "11:30").IsBelowVwap().VelocityBelow(0.2, 3)
  .Short().QuantityNotional(1000).StopLossPercent(4).TakeProfitPercent(8).SellBy("15:55").Repeat(2)
```

Shipped: the scripting model, builder verbs and parser (`StepReader` beside `BlockReader` in
`ScriptChain.cs`), the canonical JSON, diagnostics, stamp/HTML/`strategy explain`, `EmaPeriodCollector`
recursion, `StrategyBranchResolver`, and the backtester (`StrategyBacktester.cs`: per-step condition
latches, the flat transition wait, `Within` expiry, `Repeat` restarting at step 0, `BacktestTrade.Step`
and `report.StepTransitions`, across multi-day candle lists; `StrategyBacktesterSequenceTests`).
In progress (IP-T445): the Monitor half — `Strategy` row step state (`StepIndex`,
`AwaitingTransition`, `StepArmedUtc`), `StrategyRepository` advance/reset, the per-tick transition
block in `MonitorWorker`, `strategy show`'s `sequence: step k/N` row, `strategy step <id> --reset`,
and one builder card group per step. Until it lands the Monitor evaluates a sequence's step 0 as a
plain strategy and never arms a later step; only the backtester runs a sequence end to end.

**Knowledge.** The Hub indexes the BIBLE (by section and law), AMENDMENTS, README, the agent
docs, every user story and ticket (the USER_STORIES table plus `tickets ticket list --json`,
read-only), every library's XML doc comments with their source `path:line`, and the static
catalogs (gapper profiles, index events, the Legion panel, AppSettings). `docs search`, `docs
law`, `docs section`, `docs story`, `docs explain`, `docs ticket` and `docs dsl` answer from it;
the MCP server also serves the BIBLE, stories, protocol and catalog as resources.

**The Monitor tick (one pipeline, SQL as the bus).** `Strategy` rows are the only strategy
runtime state; the Gapper and Strategies pages write them, the Monitor reads them each tick.
Per tick: trading-schedule gate (`TradingSchedule.Classify`; weekends and outside-session
moments never enter) → re-read active rows → candles from a rolling per-symbol cache (REST,
topped up by the websocket stream; empty windows and missing previous closes negative-cached
for 30s) → load canon → resolve branches → manage an open position or walk entry conditions →
upsert `ConditionProgress` → on a full pass, LLM gate then Risk gate → place the entry. Premarket
and after-hours entries are limit + DAY + `extended_hours`; regular-hours entries are a
marketable limit. Longs and shorts both trade: a long buys at +0.2% and exits with a
`sell_to_close` at -0.5%; a short opens with a `sell_to_open` at -0.2%, is managed by
`GapperExitEvaluator.EvaluateShort`, covers with a `buy_to_close` at +0.5%, and its realized P&L
is inverted. On the wire (`AlpacaBrokerClient`) a short entry is `side: sell` +
`position_intent: sell_to_open` and a cover is `side: buy` + `position_intent: buy_to_close`, with a
positive `qty` (the side carries the direction); null fields are left out of the body, so a long
entry sends no intent. Alpaca opens shorts only in shortable, easy-to-borrow names on a margin
account with shorting enabled (equity of at least $2,000); any other account gets
`403 40310000 "account is not allowed to short"`, which the Monitor sees as a failed entry. Before any exit order the Monitor reconciles its bookkeeping with the broker's
position on the strategy's own side (a short is a negative broker quantity; no broker position → phantom bookkeeping cleared, no order; after a 90s grace for a
still-working entry); when several of a user's strategies share a symbol, aggregate
reconciliation is skipped and per-strategy bookkeeping is trusted. A sell-by position that
outlived its entry's ET day flattens at the first evaluated instant; exit orders outside a
weekday 04:00–20:00 ET window defer visibly. Every buy/sell writes a `TradeDiary` row
(denormalized, FK-free, log-and-continue).

**The LLM gate.** `LlmVotingService` puts the candidate to MindAttic.Legion's voter panel
(`VoteWithProfilesAsync`): every `legion.json` voter (claude, openai, gemini, deepseek) that has a
key, each through a rotating trading lens (Risk Manager, Momentum Trader, Technical Analyst),
casts an Approve/Reject/Abstain ballot; Legion snaps answers to the ballot and refills failed
seats from providers that answered. The Claude voter uses the strategy owner's key from the API
Keys page (`UserApiKeys`, decrypted through `UserKeyService`), falling back to the host key
(`AppSettings.ClaudeApiKey`: Vault own-scoped `idiotproof-claude`, then shared `claude`, then
env/config); the other voters use the shared MindAttic LLM keyring. An owner row that cannot be
read falls back to the host key, never to another user's. A key saved on the API Keys page is
stored only on that user's row; no user page writes the host key, which changes only through
Vault or configuration (the app has no admin path for it). The interactive Claude features use
the same rule for the signed-in user: the Describe tab (`StrategyScriptGenerator`), the Gapper
transcript reader (`GapperInterpreter`) and the Research page's analysis (`ResearchService` →
`CatalystExtractor`, `ClaimVectorService`) get the key from `IClaudeKeyContext`
(`OwnerClaudeKeyContext` over the signed-in owner in the web app, over the operation's owner in the
Hub; resolved on every call, never cached) and pass it per call; with neither a user nor a
host key they show `UserClaudeKeyResolver.NoKeyMessage`. The scheduled research scan has no
signed-in user and uses the host key. Voting is on when the host
(`LlmVotingEnabled`) or the owner enabled it. Approve needs `LlmConsensusThreshold` (default
0.66) of the counted votes, each weighted equally.

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
(`IdiotProof.Core/Catalogs/gapper-profiles.json`, [IP-LAW-7](#IP-LAW-7)) of templates:
screen (gap %, volume ratio, price band), ET entry window (default 04:00–09:00), stop and
trailing stop, peak giveback + arm time (default 09:15), hard sell-by (default 09:28), notional.
DSL verbs: `RequireEntryWindow`/`EntryWindow`, `IsGapUp`/`IsGapBetween` (fail closed without a
previous close), `PeakGiveback(pct, arm)`, `SellBy(time)`,
`VelocityExhaustion(pctPerMin, windowMin = 3, armAtEt?)` with the refinements
`VelocityExhaustionMinRun(pct)`, `VelocityExhaustionRequireLowerHigh()`,
`VelocityExhaustionRequireVwapLoss()` and `VelocityExhaustionExtensionSwitch(pctAboveOpen)`;
entry conditions `VelocityBelow(pctPerMin, windowMin)`, `VelocityAbove(...)`, `Decelerating()`,
`VolumeRocBelow(ratio)` read the `Velocity1m/3m/5m`, `Acceleration` and `VolumeRoc` fields
`IndicatorSnapshotBuilder` fills from `MinuteBarMath` (window 1, 3 or 5 clock minutes; null =
fail closed). Momentum-rollover exit: track the post-entry peak; once armed, sell when price
gives back N% of the entry→peak run; `SellBy` always flattens before the bell. The exit ladder
(`GapperExitEvaluator.Evaluate`, first match wins): SellBy → hard stop → trailing stop →
take-profit targets, prior HOD, rolling highs/lows → **VelocityExhaustion** (the shared
`VelocityExitRule`: the favorable velocity over the window fell under the configured %/min, after
the optional arm time, minimum run, lower-high and VWAP-loss gates; above the extension-from-open
switch it stands down and the trail takes over) → PeakGiveback; `EvaluateShort` is the mirror
(a higher low instead of a lower high, a VWAP reclaim instead of a loss), in the same order. The
time-of-day volume ratio (`IndicatorSnapshot.VolumeRatioTod`, `GapperVolumeRatio.Compute` over a
`TimeOfDayProfile`) is opt-in: without a profile it is null and `IsVolumeAbove` keeps its 20-bar
baseline; `AutoGapperScanner` still screens on cumulative volume / ADV until the study ships
`data/tod-profile.json` (`VelocityExhaustionTests`, `GapperFormationTests`). `IdiotProof.Evidence/Gappers`
is the one code path: the live Monitor, `gapper backtest-day` and the study compute velocity,
features and the velocity exit from the same classes ([IP-LAW-10](#IP-LAW-10)). At most 3
active gappers per user and one active gapper per
symbol, enforced in SQL. The "From a transcript" panel (`GapperInterpreter`, via Legion) turns
natural language into candidate cards that are re-validated fail-closed and queued only by a
human click. "Backtest a day" (`GapperDayBacktester`) walks the same condition list and the same
`GapperExitEvaluator` as live, reports MFE/MAE, a giveback grid and hindsight suggestions, and
offers a tuned profile the user applies manually.

**Replay, scan and dataset (`replay *`, `scan movers`, `dataset export`).** `replay` walks a past ET session's Alpaca bars
through the live evaluator and exit brain (shared `MarketTime.IsInsideSession` gate) and reports
each round-trip; tickers without a saved strategy use a gapper profile or a built-in family
(`momentum`, `reversal`, `emabreak`, `rthdrive`, `rsireversal`, `swingreversal`, `shortfade`).
Each run is a `ReplayRun` row with normalized `ReplayTrade`/`ReplayBar` feature rows; the HTML
archive is a view regenerated from SQL (`replay-regen`); `scan` pulls Alpaca's movers and replays
each gapper; `replay-export` writes ML-ready CSVs from the feature tables. Replay never places
orders. The old `idiotproof-monitor` operator commands are operations now (`monitor status`,
`keys set`, `strategy from-watchlist`, `users create`, `trade flatten`, `strategy resync-canon`,
`gapper auto *`, `gapper premarket-fade`); the shim forwards to them.

**Research.** `IdiotProof.ResearchScanner` runs one pass: watchlist tickers plus a rotating
batch of the tracked universe (`TrackedTicker`, refreshed daily from Alpaca's asset list); real
Form 4 transactions (`InsiderTransaction`); 8-K item-code triage with real document fetch only
for 1.01/2.01/3.02/3.03/5.03; Federal Register SRO notices triaged by an LLM into macro claims
(`IsMacro = true`, affected tickers only when resolvable — never fabricated); S&P index
add/remove events from the hand-maintained `IdiotProof.Core/Catalogs/sp-index-events.json`
(`ClaimType = "IndexEvent"`, Pending until effective). Claims are deduped by (ticker, source
URL); display sentences are composed deterministically from structured fields
(`"{Summary}. Affects {Ticker} because {Mechanism}. Expected impact: {ExpectedTimeline}."`);
`OutcomeBackfillService` marks old-enough claims Realized/Disproven from real price history
before `SignificanceScorer` ranks. The recurring pass runs as a MindAttic.Scheduler (WatchDog)
job (M.SCHD-T8); IdiotProof itself registers no scheduled task.

**Catalyst intake (IP-T443, in progress: formation recorder, voter context).** The Hub hosts a
leader-only `CatalystIntakeWorker` (`IdiotProof.Core/Catalysts`, registered beside the Monitor's
workers in `MonitorServiceRegistration.cs`, in the shape of `ExhaustionScanWorker`) that turns a
published catalyst into a timestamped, scored SQL row the Monitor can read per tick — SQL is the
bus ([IP-LAW-10](#IP-LAW-10)); it is not the batch scanner above and never runs inside
`MonitorWorker.TickAsync`. When on, it keeps one Alpaca news websocket open (`v1beta1/news`,
`["*"]`, the owner's data keys, `AlpacaStreamSession` now in `IdiotProof.DataFeeds/Streaming`) and,
with `catalyst.intake.edgarPolling`, polls EDGAR's latest-filings feed every 60 s for 8-K/4/S-1/424B
with the acceptance time from each filing's index headers, through a 120 ms `EdgarRateGate` and a
daily-cached CIK→ticker map. Every `CatalystEvent` carries `PublishedUtc` (the source's time) and
`ReceivedUtc` (this process's time), so catalyst latency is measurable; a backfilled row has
`ReceivedUtc == PublishedUtc` and `Backfilled = true`. `CatalystPreClassifier` is deterministic
and LLM-free, driven by `IdiotProof.Core/Catalogs/catalyst-preclass.json` ([IP-LAW-7](#IP-LAW-7)):
offering form prefixes → 8-K item codes and combos → ordered keyword rules → a promotional-wire
test, into {Fda, ContractAward, ReverseSplit, OfferingDilution, Uplisting, Earnings, MA, PromoPr,
Other} with a direction (-1/0/+1), a confidence and the rule that fired; `SourceTier` is 1
EDGAR, 2 editorial, 3 wire/promotional. `CatalystScorer` is pure:
`clamp((TypePrior × Magnitude × SourceWeight + NoveltyBonus) × Freshness(ageMinutes), 0, 100)`,
direction carried beside the score (a dilution scores high and bearish), weights in
`catalyst-scoring.json` as placeholders until the study sets them, `Explain` for the breakdown.
`CatalystClassifier` is the optional LLM half: a bounded channel of event ids that Haiku via
MindAttic.Legion (`CatalystExtractor`) classifies in the background, writing the `Llm*` columns
and re-scoring; headline and body travel in the user message only, never the system prompt;
gated by `catalyst.intake.llmClassify`. `CatalystBackfillService` replays Alpaca news (paged, by
date range) and EDGAR filings by date range through the same ingestor, pre-classifier and scorer,
so history and live rows agree. Operations: `catalyst list|show|score|intake-status|backfill`.
Everything is off by default behind `catalyst.intake.enabled|ownerUserId|edgarPolling|llmClassify`
(`SettingsKv`, `CatalystIntakeSettings`), needs an owner GUID with Alpaca data keys, audits
`catalyst-intake-start|stop|error`, and backs off 5 minutes after a refusal. The worker holds no
broker client and places nothing. Nothing in the trading path reads these rows yet: the voter
panel still sees only candles. Not built: the live `GapperFormationWorker` (04:00–09:45 ET
snapshot recorder), the `formation-trigger.json` arming rule, and the CATALYST/MOMENTUM facts in
`LlmVotingService.BuildSignalContext`.

**Gapper study (IP-T442, in progress).** Pre-registered 2026-11-06, before any bar, article or
filing was pulled: `docs/research/gapper-study-2026-11/protocol.md` (frozen), `hypotheses.json`
(H1–H17, 12 primary, `frozenUtc` set), `params.json` (every frozen number) and `literature.md`
(the ~70-source review). It asks three questions on one sample of sub-$20 names with ADV20 ≥ $300k
(candidate vs Tier20/50/100 gapper days, seeded-hash controls): what separates the gappers from the
near-misses, whether a gapper can be recognized while it is forming (premarket volume against a
time-of-day baseline, velocity, a catalyst flag, at the 13 decision times from D−1 16:00 to 09:45
ET), and when velocity dies relative to the high of the day (H4/H5/H13/H14 on exit timing,
descriptive only, never "significant"). H11 is the catalyst-latency benchmark: the feature
detector must beat "a news item was published within 60 minutes" by ≥ 0.05 AUC on validation or
strategy work stops and the write-up says "catalyst latency is the edge" — the honest prior,
stated in the protocol so it cannot be softened later. Families F1–F5 with BH/Holm/SPA/DSR
corrections and a frozen cost model (Corwin-Schultz spread × a once-calibrated premarket
multiplier, square-root impact, 0.5% floor). Splits: train 2021-11-01..2024-10-31, validation
2024-11-03..2025-10-31, holdout 2025-11-03..2026-10-30, opened once: `HoldoutLock`
(`IdiotProof.Core/Research/Gappers`) refuses holdout labels unless `holdout-unlock.txt` holds the
hash of the commit that froze `hypotheses.json`/`params.json`, writes `holdout-run.json` before the
first holdout row, and refuses a second run; no force flag. Live gate: a rule trades real money
only after it nets ≥ +0.5%/trade on the holdout with the day-clustered lower bound > 0 **and** 20
paper-shadow trading days whose live snapshots agree with the backtest's (same code, so drift is
data, not logic); the short leg is studied but not built until the long exit holds up. Features
and labels come from the shared `IdiotProof.Evidence/Gappers` code; the stages
(`research gappers build-universe|pull|catalysts|snapshot|label|status`, harness
`tools/GapperStudy`, minute bars in a `FileBackedBarStore` under `D:\IdiotProofData\gapper-study`,
panel tables in a `_Research_` database the harness refuses to run without) are landing under
IP-T442. The prior evidence sweep (IP-T432/T437, `docs/research/evidence-sweep-2026-11.md`)
falsified intraday timing signals on 20 already-volatile names and never studied gap events; this
study reuses its discipline (pre-declared sample, one stage per process, research-DB guard,
verdict rule fixed in advance).

**How to research a feature.** [`docs/RESEARCH_PLAYBOOK.md`](RESEARCH_PLAYBOOK.md) records this
study's method as the template for any feature that rests on a market claim: survey, pre-register,
locked holdout, look-ahead audit, shared live/backtest code, counted corrections, amendments before
results, train-only exploration with a trial ledger, honest reporting, paper shadow before live.

**Study amendments (registered before any test on study data; frozen files never edited).** Each
`amendment-NN.json`/`.md` beside the protocol is committed before its tests run, loaded in order by
`GapperAmendment.LoadAll`, and corrected as its own set so no frozen q or Holm p moves.
*Amendment 01 (IP-T448)* fixes a selection problem in the frozen design: H1, H9, H11 and H12 are
asked only on candidate days, which are chosen by the day's own outcome, so they overstate what a
live 08:00 scan achieves. A seeded 10% SHA-256 sample of non-candidate universe days (weight 10)
gets the candidates' data tiers, and H1p/H9p/H11p/H12p re-ask the same questions with the same
thresholds over the whole universe (weighted primitives in `IdiotProof.Evidence/Evaluation/WeightedPopulation.cs`).
Sample-only days are snapshotted only at the three decision times those hypotheses read, to fit
LocalDB's 10 GB cap. Promotion needs the frozen conditions **and** H11p/H12p. *Amendment 02
(IP-T450)* tests the catalyst-latency edge as a trade: H18 buys the first realtime premarket news item
second-hand (1 or 5 minutes after publication) and exits by 09:15, 09:28 or 09:45 through the
frozen simulator and cost model; H19 measures how much of a news-driven gapper's hour comes before
the headline (does someone trade ahead of it); H20 measures how fast the second-hand edge decays.
H18 is not stopped by the H11 kill criterion, because it is how that edge would be traded, but it
reaches paper trading only through the same validation, holdout and paper-shadow gate.

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
domains (`BlockedEmailDomain`, seeded at startup). Self-service password reset is the
MindAttic.Authentication token flow: `/forgot-password` (`MaForgotPassword`) emails a single-use
link to `MindAttic:Auth:Reset:PublicBaseUrl` + `/account/reset` (`MaResetPassword`); the base URL
is `https://localhost:65025` in `appsettings.Development.json` and the web app's own
`azurewebsites.net` origin in `infra/main.bicep`. The username listing exists in Development
only. `/login` forwards only same-site return URLs. The SignalR
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
`IdiotProof.Core/Services/UserBrokerResolver.cs`.)

### {#IP-LAW-4} The verb catalog is reflected, never hand-listed
`StrategyScriptGenerator` builds the LLM system prompt — and the Learning Center renders its
phase/verb reference — by reflecting on the real `StrategyBuilder` + `Conditions` types so the
documented/prompted DSL can never drift from the code that actually compiles.
(`IdiotProof.Core/Services/StrategyScriptGenerator.cs`.)

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
When the market-data feed is Mock, every non-Sandbox entry is refused — Monitor fires, manual
orders, web options tickets and replaces that raise an entry's size alike; Mock data pairs only
with the Sandbox broker. Exits (risk-reducing) are still allowed. The guard is a gate inside the
`TransactionGateway` ([IP-LAW-12](#IP-LAW-12)), so every order path and any future broker
inherits it. (`IdiotProof.Core/Trading/TransactionGateway.cs`.)

### {#IP-LAW-10} One evaluator; SQL is the bus
The Monitor is the only live evaluator and order placer for strategies. UI changes reach it only
through SQL rows, read each tick — no restart, no second evaluation path. Backtest and replay
surfaces call the same evaluator and exit brain (`IndicatorSnapshotBuilder`, conditions,
`GapperExitEvaluator`, `EmaPeriodCollector`) so a backtest cannot run different code than live.
A SQL leader lease keeps at most one Monitor trading per database; the Monitor re-confirms it at
the top of every tick and the `TransactionGateway` re-confirms it again immediately before each
Monitor order reaches a broker (`GatewayOptions.LeaderCheck`), refusing the order if it is lost.

### {#IP-LAW-11} The repository owns strategy-mutation invariants
`StrategyRepository` enforces ownership (`NotOwner`), refuses to deactivate, delete or change the
symbol of a strategy holding a position (`PositionOpen`), writes only editor-owned columns on
update (never the Monitor's position bookkeeping), enforces one active gapper per symbol and the
3-gapper cap against SQL on every activation path — single, bulk, editor save — with a post-write
recheck (`SymbolAlreadyActive` / `GapperCapReached`; auto-armed gappers obey their own per-run
limit instead of the cap), refuses a Live activation the caller has not confirmed
(`LiveNotConfirmed`: the web asks for the password per activation, as the CLI asks for a code),
and deletes a strategy's `ConditionProgress` with it. No page writes strategy rows around it.
(`StrategyRepositoryGuardTests`.)

### {#IP-LAW-12} Every order goes through the TransactionGateway
Every order-like action — Monitor entries and exits, CLI/MCP orders, replaces, cancels,
cancel-alls, position closes and flattens, crypto orders, the web Options ticket, the operator
`flatten`/`test-order` — reaches a broker only through `TransactionGateway`. Its gates run in a
fixed order and fail closed: well-formed order → route (Live never quietly downgrades, IP-LAW-3)
→ synthetic-data guard (IP-LAW-9) → Risk Guardian for entries and for a replace that raises an
entry's size (IP-LAW-2) → LLM panel for automated entries (IP-LAW-1) → Live confirmation
([IP-LAW-13](#IP-LAW-13)) → place → audit. Every outcome but a dry run writes an AuditLog row
(linked to the Hub's CommandLedger row); a person's accepted order also opens or closes a
TradeDiary row. No other production code calls a broker's place / replace / cancel /
cancel-all, and only the gateway can mark a Live action confirmed.
(`IdiotProof.Core/Trading/TransactionGateway.cs`; `MoneyPathLawTests`,
`TransactionGatewayTests`, `MonitorGatewayParityTests`.)

### {#IP-LAW-13} Live from the CLI or MCP needs a confirm code
A person's Live action from the CLI or MCP is previewed and held: the gateway stores the exact
intent and returns a 6-character code that works once, for the same owner, within 120 seconds,
and replays exactly what was previewed — an edited, swapped, expired or reused code places
nothing. Several Live actions (close-all) share one code bound to exactly that set. The web
confirms Live with the owner's password elevation (re-checked on the server) instead of a code;
the Monitor needs none, because activating a strategy in Live was the confirmed act. Non-order
Live mutations that move money (funding, transfers) use the same `ConfirmationStore` codes.
(`IdiotProof.Core/Trading/ConfirmationStore.cs`; `MoneyPathLawTests`,
`TransactionGatewayTests`.)

### {#IP-LAW-14} A sequence is one strategy
A sequenced strategy is one `Strategy` row and one canon: the root definition is step 0, later
steps live in the root's `nextSteps` inside the same canonical JSON, a step never nests steps,
and the sequence keys are written only when used, so a strategy with no later steps serializes,
stamps and scripts byte-identically to a strategy written before steps existed. Runtime sequence
state (which step is armed, since when) belongs on that same row, never on a second row or a
settings key, so the repository's guards ([IP-LAW-11](#IP-LAW-11)) and the gapper cap see one
strategy. (`IdiotProof.Scripting/StrategyJson.cs` — `nextSteps` allowed on the root only;
`DslSequenceTests` goldens.)

## 6. Verified state {#IP-§6}
Build/test evidence (recorded 2026-10-03, .NET 10 SDK): `dotnet test IdiotProof.slnx -c Debug`
→ build succeeded, **all green, 0 failed**: Engine 85 · Indicators 18 · Strategies 34,681
(dominated by generated parameter cases) · Brokers 38 (+3 `[Explicit]` real-paper tests) · Blazor
217 · UI 62 · Monitor 16 (+2 `[Explicit]` real-paper tests).

Real Alpaca paper account, 2026-10-03 (market closed, run by name): the 3 options tests pass
(level 3, BE chain + snapshots, a $0.01 buy-to-open placed and cancelled). The short probe
(`AlpacaPaperShortIntegrationTests`) sent the Monitor's `DirectionalOrders.Entry` short (1 AAPL,
shortable + ETB, sell limit at about 2× the market) through `AlpacaBrokerClient`. Alpaca answered `403 40310000 "account
is not allowed to short"` because the paper account has `shorting_enabled=false` and equity
under $2,000, so the test reported Inconclusive. The same `side` + `position_intent` shape on an
equity is proven by the `sell_to_close` probe: the Monitor's long exit for 1 held share was
`accepted` with `position_intent=sell_to_close`, `qty=1`, then `canceled`. **Not yet proven:** an
accepted `sell_to_open`, a filled short and a `buy_to_close` cover against Alpaca; those need a
short-enabled paper account.

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
  window-scoped latches (`WindowScopedConditionTests`), culture-safe text (`ScriptTextRoundTripTests`),
  the velocity exit's ladder position and the new conditions (`VelocityExhaustionTests`),
  sequences and the non-sequence byte-identity goldens (`DslSequenceTests`,
  `StrategyBacktesterSequenceTests`).
- `IdiotProof.Evidence.Tests` — `GapperFormationTests`: no-lookahead (post-t bars change nothing),
  velocity math over gaps and zero-volume minutes, `TimeOfDayProfile` round trip and version
  guard, `GapperVolumeRatio` with and without a profile.
- `IdiotProof.Brokers.Tests` — BrokerRouter Sandbox default, sandbox fills, Alpaca extended-hours
  contract, options wire format + Sandbox chain/basis (`OptionsBrokerTests`), equity short wire
  format, the can't-short 403 and negative short positions (`AlpacaEquityShortWireTests`); the opt-in
  `[Explicit]` `AlpacaPaperOptionsIntegrationTests` runs only by name against the real paper account.
- `IdiotProof.Blazor.Tests` — verb-catalog reflection, the LLM gate on Legion's voter panel over
  a fake transport (`LlmVotingServiceTests`), per-owner Claude keys and their isolation (`UserClaudeKeyResolverTests`,
  `ClaudeKeyIsolationTests`, `UserClaudeKeyRoutingTests`),
  self-service password reset (`PasswordResetFlowTests`), `ConditionProgressRepository` (SQL Server LocalDB), `StrategyRepositoryGuardTests`,
  `UserBrokerResolverTests`, `GapperInterpreterTests`, `LegionProviderContractTests`, research
  (`EdgarServiceTests`, `Form4ParserTests`, `CorporateActionDetectorTests`,
  `RegulatoryScannerTests`, `SignificanceScorerTests`, `OutcomeBackfillServiceTests`,
  `IndexEventScannerTests`, `TickerUniverseServiceTests`), `RiskGuardianServiceTests`,
  `UserPreferencesServiceTests`.
- `IdiotProof.UI.Tests` — Options presenter / position view / glossary logic.
- `IdiotProof.Monitor.Tests` — long/short order shapes and a short round trip on the Sandbox
  broker (`DirectionalOrdersTests`); `PremarketFadeScanner` and `BeBexDecayScanner` math; the
  opt-in `[Explicit]` `AlpacaPaperShortIntegrationTests` (keys from the `alpaca-paper` keyring
  entry, skipped without it, refuses any host but `paper-api.alpaca.markets`) sends a never-fillable
  1-share short and a never-fillable `sell_to_close`, reads each back and cancels it.

**Hub, CLI and MCP (Epic W, 2026-10-30/31).** Each ticket landed with its own tests, run per
ticket (`dotnet test <project> --filter ...`) plus `CatalogCompletenessTests`; the whole solution
has not been re-run since the 2026-10-03 record above.
- `IdiotProof.Core.Tests` — the money path: `TransactionGatewayTests` (gate order, Live hold and
  confirm, audit), `MoneyPathLawTests` (no production broker call outside the gateway,
  [IP-LAW-12](#IP-LAW-12)/[IP-LAW-13](#IP-LAW-13)), `ConfirmationStoreTests` (single use, 120 s,
  owner- and intent-bound), `MonitorGatewayParityTests`, `OptionsGatewayTests`,
  `GatewayDiaryTests`, `MonitorHostingTests`, `MonitorPauseFlagTests`, `OwnerResolverTests`,
  `SecretRedactorTests`, `CoreServiceRegistrationTests`, plus the services moved from Blazor
  (research, LLM voting, Claude keys, repositories, `UserBrokerResolver*Tests`); the catalyst
  intake (`CatalystPreClassifierTests` fixtures for FDA, 424B, 5.03 reverse split and promo wire,
  `CatalystScorerTests` monotonic freshness decay, `CatalystIngestorTests`,
  `CatalystIntakeParsingTests`, `AlpacaNewsServiceRangeTests`).
- `IdiotProof.Operations.Tests` — the operation framework (`OperationCatalogTests`,
  `ArgsBinderTests`, `OperationDispatcherTests`, `OperationContractTests`,
  `CatalogCompletenessTests`: every operation documented, examples bind, JSON and argv round-trip),
  one fixture per operation family run against scripted Alpaca `HttpMessageHandler`s or in-memory
  services (account, orders, positions, options, crypto, market, reference, strategies, gapper,
  replay, risk, LLM, research, diary, audit, ledger, keys, settings, users, monitor, DSL), the
  knowledge index and `docs *` (`Docs/*Tests`), and `AlpacaCoverageTests` (every endpoint in
  [docs/ALPACA_COVERAGE.md](ALPACA_COVERAGE.md) has an existing operation, every called path is
  inventoried).
- `IdiotProof.Cli.Tests` — exit codes, argv/global flags, stdin `-` values, help with the Hub up
  or down, local commands, owner defaults, `AgentExportTests`, and `PublicDocsPrivacyTests` (no
  key-shaped string or account id in README.md or docs/).
- `IdiotProof.Mcp.Tests` — one tool per operation with the catalog's schema, confirmation input
  on money tools, risk hints, Hub-down errors (`McpToolTests`), and the doc resources
  (`DocResourceTests`).

Not proven by the solution test run: the Blazor UI flows and the live LLM voting round-trip; a
Live CLI/MCP order confirmed end to end against a real Live account (the confirm path is proven
against scripted brokers only, by design: no test touches real money).
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
- **Shorts on a real account** — enable shorting on the paper account (margin, equity ≥ $2,000),
  then re-run `AlpacaPaperShortIntegrationTests` to prove an accepted `sell_to_open`, and during
  market hours a filled short covered by `buy_to_close` (IP-US-K16).
- **Gapper hardening (Epic K tail)** — host-level MonitorWorker test (queue → 4AM fire → hold →
  rollover sell), `/gapper` Cypress spec, fill-price reconciliation against the broker's actual fill (entry is recorded at the limit
  price), full order-state tracking (pending orders as first-class rows).
- **Gapper program (IP-T442–T445)** — the study pipeline (universe, tiered pull, snapshots,
  labels, `test H1..H17`, report) and its verdict; the Monitor half of Steps (row step state,
  transition block, `strategy step --reset`, builder cards); the live formation recorder and the
  `formation-trigger.json` arming rule (all thresholds null = never arms); catalyst and momentum
  facts for the voter panel; the time-of-day profile replacing the 20-bar `IsVolumeAbove`
  baseline once `data/tod-profile.json` exists; the short leg only after the long exit survives
  the 20-day paper shadow ([§4.4](#IP-§4)).
- **Known debts** — DSL generation is single-shot and the Describe tab still emits text, not
  model JSON; the unused `UserPreferences.OpenStrategyTabs`
  column awaits removal in a migration; the Azure infra (`tools/azure-provision.md`) is not
  provisioned; the OAuth token is not
  yet wired into order placement (needs a registered Alpaca OAuth app + paper testing).
- **Replay tests (Epic R)** — the replay/scan/export/family commands ship without NUnit coverage.
- **Adaptive auto-strategy generation (Epic S)** — standardize the `auto-gapper` seed into a
  wait-for-enough-information generator across sessions.
- **Strategy ghost overlay + branching visualization** — chart integration, simulator timeline,
  branch fork rendering. (Epic G in [USER_STORIES.md](USER_STORIES.md).)
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
  stops, giveback, arm/sell-by times, notional) in `IdiotProof.Core/Catalogs/gapper-profiles.json`; cloned
  and tuned per ticker on the `/gapper` tab.
- **Peak giveback** — the momentum-rollover exit: sell once price gives back N% of the run
  from entry to the post-entry peak; armed from a configured ET time ("the last 15 minutes").
- **Velocity exhaustion** — the exit that fires when the favorable price velocity over a 1/3/5
  minute window falls under N %/min after a run (`VelocityExitRule`); sits after the take-profit
  targets and before peak giveback in the exit ladder, mirrored for shorts.
- **Step / sequence** — one ordered stage of a sequenced strategy, with its own direction,
  conditions and exits; the root definition is step 0, `ThenWhen` is the flat-only transition to
  the next step and `Within(days)` its deadline. One row, one canon ([IP-LAW-14](#IP-LAW-14)).
- **Catalyst event** — one `CatalystEvent` row: a news item or SEC filing as the intake saw it,
  with `PublishedUtc` and `ReceivedUtc`, a deterministic pre-class and direction, optional LLM
  fields and a 0-100 catalyst score. Research data; nothing in trading reads it yet.
- **Time-of-day profile** — `TimeOfDayProfile`: the median fraction of a day's 04:00–20:00 volume
  printed by each clock minute, per ADV decile, fit on the study's training controls; the
  baseline for `VolumeRatioTod`. Not shipped yet; without it volume ratios are plain cumVol / ADV.
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
  addition or deletion logged in `IdiotProof.Core/Catalogs/sp-index-events.json`, Pending until effective.
