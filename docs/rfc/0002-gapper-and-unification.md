---
codex: 1
project: IdiotProof
code: IP
layer: rfc
status: active
updated: 2026-07-18
---

# RFC 0002 — The Gapper epic + single-pipeline unification

## Problem
Session directives 2026-07-18 (verbatim intent, condensed):
1. **The one thing done well:** take a stock ticker at 4 AM ET, check whether it is a *gapper*
   (per an adjustable, selectable profile), buy in, hold through premarket, and in the last
   ~15 minutes before the 9:30 opening bell sell it off once it has lost a defined amount
   *relative to its previous momentum* — out before the bell. Pick up to **3 tickers** at a
   time. Own tab, built on the existing IdiotProof technology.
2. **Async UI ⇄ console:** the Blazor UI and the console Monitor run independently; changes
   made in the UI (queue/dial/toggle) apply automatically to the running console, which scans
   market data as fast as possible (streaming off the Alpaca API).
3. **Dial-in per gapper:** every gapper is different — stop loss, trailing stop, giveback,
   arm/sell times must be adjustable per queued ticker, not one-size-fits-all.
4. **Unify:** the codebase is "a lot of different ideas crammed together" — audit everything,
   fix what's broken, and converge on a single coherent vision.

Current reality (exploration + audit, 2026-07-18):
- **Two rival evaluation loops**: `MonitorWorker` (console; evaluates Strategy SQL rows;
  no orders; hand-rolled loop, not `SupervisedLoop`) vs `StrategyExecutionService` (Blazor;
  evaluates `WorkspaceTab` bindings; places orders; uses `SupervisedLoop`). Different inputs,
  different semantics, both claim to be "the" evaluator.
- `ScriptParser` silently **drops `Session()`**; `ExitTime` never serialized; `IsGapUp/IsGapDown`
  evaluated to constant-true (no gap data on `IndicatorSnapshot`). All fixed in this epic.
- Monitor hardcodes `MockDataFeed`; no streaming; no previous-close/daily-bar path.
- No exit management for open positions in either loop beyond bracket fields on the order.
- No static JSON catalogs exist yet (IP-LAW-7's "JSON for static data" is unexercised).

## Design

### D1. Gapper profiles — static JSON catalog (IP-LAW-7)
`IdiotProof.Blazor/wwwroot/data/gapper-profiles.json` ships **built-in profiles** (e.g.
Classic Gapper, Penny Runner, Large-Cap Gap). Each profile is a complete dial-in set:

| Group   | Fields |
|---------|--------|
| Screen  | `minGapPercent`, `maxGapPercent?`, `minVolumeRatio`, `minPrice`, `maxPrice` |
| Window  | `entryWindowStartEt` (default "04:00"), `entryWindowEndEt` (default "09:00") |
| Risk    | `stopLossPercent`, `trailingStopPercent?` |
| Exit    | `peakGivebackPercent` (% of the entry→peak run given back = rollover), `armExitAtEt` (default "09:15" — the "last 15 minutes"), `sellByEt` (default "09:28" — hard flatten before the bell) |
| Sizing  | `defaultNotional` (dollars) |

Profiles are **templates**: selecting one pre-fills the queue form; every value stays editable
per ticker ("all gappers are not the same"). The tuned result is denormalized into the
strategy's `ScriptText` — the Strategy SQL row remains the single runtime source of truth.

### D2. DSL — gapper lifecycle verbs (IP-LAW-4: reflected, parser-cased)
- `RequireEntryWindow("04:00","09:00")` / alias `EntryWindow` — new `TimeWindowCondition`
  (Filters phase, ET clock via `MarketTime`, wraps overnight windows).
- `SellBy("09:28")` — hard ET time exit (maps `StrategyDefinition.ExitTime`; now round-trips).
- `PeakGiveback(25, "09:15")` — momentum-rollover exit: sell when price gives back N% of the
  entry→peak run; optional arm time. New fields `PeakGivebackPercent`/`PeakGivebackArmTime`.
- `IsGapUp/IsGapDown` now actually evaluate: `IndicatorSnapshot.PreviousClose/GapPercent`
  added; **fail closed** when previous close is unavailable.
- Parser fixes: `Session()` recognized (was silently dropped), `StopLossPercent`/`SellBy`/
  `PeakGiveback`/`RequireEntryWindow` serialize + parse round-trip.

The canonical generated gapper script:
```
Ticker("ABCD")
    .Name("ABCD Gapper — Classic")
    .Session(IS.PREMARKET)
    .RequireEntryWindow("04:00", "09:00")
    .IsGapUp(5)
    .IsVolumeAbove(2)
    .Long()
    .QuantityNotional(1000)
    .StopLossPercent(5)
    .PeakGiveback(25, "09:15")
    .SellBy("09:28")
    .Repeat()
```

### D3. Momentum-rollover exit semantics
After entry, track the high-water mark (peak) from bar highs since fill. The *run* is
`peak − entry`. Once armed (`armExitAtEt`, default 09:15 ET), sell when
`price ≤ peak − run × peakGivebackPercent/100`. Giving back scales with the run — a big
momentum runner tolerates more absolute pullback before rollover than a grinder, which is
the "based on its previous momentum" rule. `SellBy` is the unconditional fallback so the
position is always flat before the bell. Implemented as `GapperExitEvaluator`
(IdiotProof.Strategies) — pure function over (definition, entry fill, candles-since-entry,
now) → exit decision; unit-testable without a broker.

### D4. Single pipeline — the unified vision
**One evaluator, two hosts, SQL as the bus.**
- `Strategy` SQL rows are the *only* strategy runtime state. The Gapper tab queues rows;
  the Strategies tab lists them; the console evaluates them. (WorkspaceTab bindings remain
  UI layout state only — not an evaluation input.)
- The console (`IdiotProof.Monitor`) is the always-on evaluator: re-reads active strategies
  every tick (UI changes apply automatically — no restart), evaluates conditions →
  ConditionProgress rows (UI badges), and routes fires through the three gates (IP-LAW-1).
- Order execution goes through `IBrokerClient` via `BrokerRouter` (IP-LAW-3) from whichever
  host is executing; premarket orders are **limit + extended_hours** on Alpaca.
- Open-position exit management (gapper sell-off) lives with the evaluator: entry fill price
  recorded on the Strategy row (`LastEntryPrice`, `PositionState`), exits evaluated per tick
  by `GapperExitEvaluator`; exit orders are risk-reducing and bypass the LLM panel but are
  always audit-logged (they still respect RiskGuardian's kill-switch state).
- Data: config-driven feed selection (Alpaca when keys exist; Mock fallback), plus an
  Alpaca websocket streaming client for subscribed symbols so evaluation reacts on bar
  arrival instead of a slow poll. Daily-bar fetch supplies previous close for gap math.
- `MonitorWorker` adopts `SupervisedLoop` (IP-LAW-5).

### D5. Gapper tab (UI)
`/gapper` page + `MainLayout` NavLink. Three ticker slots. Per slot: symbol input, profile
select (from D1 catalog), expandable "Dial in" panel exposing every profile field, live
preview of the generated IdiotScript, Queue button → `StrategyRepository.CreateAsync` +
activate. Below: queued gapper list with ConditionProgress badges, entry/exit state, and
deactivate/remove. Alpaca theme variables only.

## What NOT to do
- Do **not** let gapper orders bypass the three gates on entry (IP-LAW-1) or place any
  premarket order as a market order (Alpaca rejects; must be limit + extended_hours).
- Do **not** store profile catalogs in SQL or per-user tuned values in JSON (IP-LAW-7).
- Do **not** keep two evaluation semantics: any fix to condition walking happens in ONE place.
- Do **not** mark stories ✅ without a named green test (HOUSE-LAW-8).

## Phased plan (with risk)
1. DSL verbs + round-trip fixes + gap evaluation. *(low — pure library + tests)* ✅ started
2. GapperProfile JSON + loader + script factory. *(low)*
3. Gapper tab UI + queueing. *(medium — Blazor interactivity)*
4. Monitor: feed selection, previous-close, streaming, exit management, SupervisedLoop. *(high — touches live trading path; sandbox-first testing)*
5. Audit-driven fixes + dead-code removal. *(medium — guided by audit reports)*
6. Tests (NUnit per project; Cypress spec for the tab) + codex updates. *(low)*

## Graduates into
BIBLE §4/§5 (single-pipeline canon, gapper subsystem), new law candidate "one evaluator,
two hosts, SQL as the bus", USER_STORIES Epic K (Gapper), amendment IP-A8.
