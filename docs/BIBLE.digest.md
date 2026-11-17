AUTHORITATIVE — full detail in docs/BIBLE.md

<!-- generatedFrom: IP-§1,IP-§3,IP-§5,IP-§9 + USER_STORIES status index. Generated 2026-11-17 by tools/codex.ps1. Do not hand-edit. -->

# IdiotProof — Bible Digest (generated)

## The one sentence
IdiotProof turns a plain-English trade idea into a runnable DSL strategy that the Monitor,
running 24/7 inside the IdiotProof Hub, evaluates against live market data, fires only when
every condition matches, an LLM voter panel approves, and the Risk Guardian clears it — then
places the order through the Hub's transaction gateway and manages the position to its exit.
A person (or an assistant on their behalf) drives all of it through the `idiotproof` CLI or its
MCP tools; there is no UI. The flagship flow is the **Gapper**
([§4.4](#IP-§4)): buy the premarket gap at 4AM, sell it off before the 9:30 bell once momentum
rolls over.

## What it is NOT
- **Not a charting terminal.** It draws static terminal charts (`market chart`, `account history`)
  but it is not a streaming tick-chart workstation for discretionary
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
- **Not an options autotrader.** Options orders are manual and user-initiated
  (`options buy|sell|spread|close|exercise` from the CLI/MCP); the DSL, Monitor, `RiskGuardian` and
  `Conditions` catalog know nothing about options.
- **Not a desktop or web app.** Its only front doors are the `idiotproof` CLI and the `idiotproof`
  MCP server, both over the one Hub ([§4](#IP-§4)); the Hub runs headless as a console window or a
  Windows Service and is the only process that talks to Alpaca. The Blazor web app, its UI
  libraries and the `idiotproof-monitor` shim were removed in IP-T504 (recoverable from git history).
- **Not a research daemon.** `IdiotProof.ResearchScanner` (and `research scan`) runs one pass and
  exits; it is fired by a scheduler job and never shares the Monitor's trading loop. The one
  long-running research worker, the catalyst intake ([§4.4](#IP-§4)), writes research rows only,
  is off by default, and holds no broker client.
- **Not a trained predictor.** Nothing in the trading path reads a catalyst row or a formation
  snapshot yet; the gapper study ([§4.4](#IP-§4)) decides whether anything ever will, and its
  pre-registered prior is that the honest answer may be "catalyst latency is the edge".

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
The one automatic way out of quarantine is a repair the Monitor makes itself, and only when nothing
can be lost: the strategy is flat (no position, no working order of its own, at step 1 of its
sequence), its stored script has no error diagnostics, and the canon re-derived from that script
passes the strict reader, is for the same symbol, session, direction and size, and has every exit
rule and no fewer conditions, branches, targets or steps than the stored canon (which must be a
schema-1 JSON object, so it can be counted); the repository makes the write
([IP-LAW-11](#IP-LAW-11)) with a `strategy-repaired` audit row holding the quarantine reason and
the canon before and after, one attempt per quarantine reason, Paper and Sandbox strategies only
unless `quarantine.autorepair.live` is true, and not at all when `quarantine.autorepair.enabled`
is false — in every other case the strategy stays quarantined and `strategy quarantine` says what
blocks it and shows the proposed repair. (`IdiotProof.Core/Services/CanonRepair.cs`;
`CanonRepairTests`.)

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
deletes a strategy's `ConditionProgress` with it, and is the only writer of an automatic canon
repair (`RepairQuarantinedCanonAsync`: the fresh row must pass [IP-LAW-8](#IP-LAW-8)'s repair
rule; canon and `UpdatedUtc` only, saved together with its audit row). No page writes strategy
rows around it.
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
- **Monitor** — `MonitorWorker`, the unattended 24/7 evaluator and executor hosted inside the Hub.
- **SupervisedLoop** — the fault-tolerant tick loop the Monitor runs.
- **Voter panel / Legion** — the multi-LLM quorum (configured in `legion.json`, shipped with both
  hosts) that approves or rejects a Claude-generated script / a candidate fire, via MindAttic.Legion.
- **ConditionProgress** — the SQL row (`N/M`, first failing verb) the Monitor upserts per tick
  and `strategy progress` reads.
- **Quarantine** — a strategy whose canon cannot be fully understood; never evaluated, reason shown.
  The Monitor lifts it by itself only through a lossless, audited canon repair of a flat strategy
  ([IP-LAW-8](#IP-LAW-8)); `strategy quarantine` lists the rest with what blocks each one.
- **Sandbox broker** — the always-registered simulated broker (instant fills into an in-memory
  position book) that is the safe default in `BrokerRouter`.
- **BrokerMode** — a strategy's own routing choice: Paper, Live or Sandbox.
- **Trade diary** — the `TradeDiary` table: one row per trade lifecycle (entry, risk plan, exit,
  realized P&L, paper/live), a permanent record that survives strategy deletion.
- **Gapper** — a stock gapping up in premarket vs the previous close; the flagship trade:
  buy in the 4AM window, sell before the 9:30 bell.
- **Gapper profile** — the dialable template (gap %, volume ratio, price band, entry window,
  stops, giveback, arm/sell-by times, notional) in `IdiotProof.Core/Catalogs/gapper-profiles.json`; cloned
  and tuned per ticker (`gapper tune`, `gapper apply-tuned`).
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

## Status index (USER_STORIES.md)
- done: 85
- partial: 15
- planned: 14
