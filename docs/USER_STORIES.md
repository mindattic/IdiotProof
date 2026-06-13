---
codex: 1
project: IdiotProof
code: IP
layer: stories
status: living
updated: 2026-06-09
counts: {done: 19, partial: 6, planned: 13, cut: 0}
---

# IdiotProof — User Stories
> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned · 🗑️ cut. Every ✅ cites the test.
> "Done" = proven by a test that runs in `IdiotProof.slnx` (see [BIBLE §6](BIBLE.md#IP-§6)).

## Epic A — Risk Guardian (the final gate)
- **IP-US-A1 ✅** As a trader, I am blocked from placing any trade without a stop loss, so I can
  never enter open-ended risk. *Given a setup with no stop, When validated, Then it is blocked.*
  *(verified by `ValidateTrade_NoStopLoss_IsBlocked`.)*
- **IP-US-A2 ✅** As a trader, a stop on the wrong side of entry is rejected, so a "stop" can't
  amplify my loss. *(verified by `ValidateTrade_LongStopAboveEntry_IsBlocked`,
  `ValidateTrade_ShortStopBelowEntry_IsBlocked`.)*
- **IP-US-A3 ✅** As a trader, a trade whose risk exceeds my per-trade cap is blocked and an
  adjusted quantity is suggested, so I stay within my limit. *(verified by
  `ValidateTrade_RiskExceedsMaxLossPerTrade_IsBlocked_AndSuggestsAdjustedQty`.)*
- **IP-US-A4 ✅** As a trader, micro-stops and absurdly wide stops are both rejected, so noise
  and over-exposure are excluded. *(verified by `ValidateTrade_StopTooTight_IsBlocked`,
  `ValidateTrade_StopTooWide_IsBlocked`.)*
- **IP-US-A5 ✅** As a trader, a trade risking too much of my account is blocked, so a single
  trade can't endanger the account. *(verified by `ValidateTrade_AccountRiskTooHigh_IsBlocked`.)*
- **IP-US-A6 ✅** As a trader, once my daily loss limit is hit, further trades are blocked
  (circuit breaker), so a bad day can't compound. *(verified by
  `ValidateTrade_DailyLossAlreadyExceeded_IsBlocked`.)*
- **IP-US-A7 ✅** As a trader, a well-formed setup is approved (with a warning on poor R:R), so
  the guardian doesn't block good trades. *(verified by `ValidateTrade_WellFormedSetup_IsApproved`,
  `ValidateTrade_LowRiskRewardRatio_ApprovesWithWarning`.)*
- **IP-US-A8 ✅** As a trader, the guardian sizes the max quantity against my per-trade cap,
  account percent, and remaining daily risk, so position size is always derived from my limits.
  *(verified by `CalculateMaxQuantity_RespectsPerTradeCap`, `_RespectsAccountPercent`,
  `_RespectsRemainingDailyRisk`, `_NoRiskPerShare_ReturnsZero`.)* — implements [IP-LAW-2](BIBLE.md#IP-LAW-2).

## Epic B — Unattended Monitor loop
- **IP-US-B1 ✅** As a trader, the Monitor keeps running after a tick succeeds and after a tick
  throws, so one bad evaluation never kills the 24/7 loop. *(verified by
  `SuccessfulTick_InvokesOnTickSucceeded`, `FailingTick_InvokesOnTickFailedWithCount_AndContinues`,
  `SuccessAfterFailures_ResetsConsecutiveCounter`.)* — implements [IP-LAW-5](BIBLE.md#IP-LAW-5).
- **IP-US-B2 ✅** As an operator, an external watchdog can detect a hung Monitor via a heartbeat
  file written every tick (success or failure). *(verified by `HeartbeatFile_IsWrittenAfterSuccessfulTick`,
  `HeartbeatFile_RecordsFailure`.)*
- **IP-US-B3 ✅** As an operator, the Monitor shuts down cleanly on cancellation rather than
  crashing. *(verified by `Cancellation_ExitsCleanly`,
  `TickThrowingOperationCanceled_DuringCancellation_ExitsCleanly`.)*

## Epic C — IdiotScript DSL & backtesting
- **IP-US-C1 ✅** As a trader, every sample IdiotScript builds with a long direction, stop, and
  targets, so the DSL surface is internally consistent. *(verified by
  `AllSamples_Build_WithLongDirection_StopAndTargets`.)*
- **IP-US-C2 ✅** As a trader, a script round-trips (parse → serialize → re-parse) preserving
  VWAP/EMA-stack conditions, notional sizing, and multi-target take-profit + stop, so editing
  never silently drops my intent. *(verified by `RoundTrip_PreservesVwapAndEmaStackConditions`,
  `RoundTrip_PreservesNotionalSizing`, `RoundTrip_PreservesMultiTargetTakeProfitAndStop`,
  `NciScript_ParsesBack_WithBreakoutPullbackTargetsAndStop`.)*
- **IP-US-C3 ✅** As a trader, the backtester runs over every sample without error and produces
  trades only when triggers actually fire. *(verified by `Backtest_RunsOnEverySample_WithoutError`,
  `Backtest_NciBreakoutTrigger_FiresWhenPriceCrossesLevel`, `NoBreakout_FiresNoTriggers_AndTradesNothing`.)*
- **IP-US-C4 ✅** As a trader, breakout-then-pullback fires both triggers and hits its target;
  a hit stop produces a losing trade; multi-target scales out across fills; a cycle can repeat;
  an open position closes at end of session. *(verified by
  `Breakout_Then_Pullback_FiresBothTriggers_AndHitsTarget`, `StopLoss_ProducesLosingTrade`,
  `MultiTarget_ScalesOut_AcrossTwoFills`, `Repeat_AllowsASecondCycle`,
  `OpenPosition_ClosesAtEndOfSession_WhenNeitherStopNorTargetHit`.)*
- **IP-US-C5 ✅** As a developer, the strategy registry returns null for unknown names and
  defaults to empty, and a backtest over no candles returns an empty report without throwing.
  *(verified by `StrategyRegistry_DefaultsToEmpty`, `StrategyRegistry_Get_UnknownName_ReturnsNull`,
  `Run_OnEmptyCandles_ReturnsEmptyReport_NoThrow`.)*

## Epic D — Indicator math
- **IP-US-D1 ✅** As a strategy author, RSI is bounded 0–100 (100 on all gains, 0 on all losses),
  same-length as input, and empty-safe. *(verified by `RSI_AllGains_Returns100`, `RSI_AllLosses_Returns0`,
  `RSI_MixedData_InRange`, `RSI_ReturnsSameLengthAsInput`, `RSI_EmptyInput_ReturnsEmpty`.)*
- **IP-US-D2 ✅** As a strategy author, EMA/ATR/MACD/VWAP compute correctly (EMA period-1 equals
  close and smooths price; ATR scales with volatility; MACD histogram = MACD − signal; VWAP equals
  the volume-weighted typical price and resets at the day boundary). *(verified by `EMA_Period1_EqualsClose`,
  `EMA_IsSmoothedVsRawPrice`, `ATR_FlatMarket_IsSmall`, `ATR_HighVolatility_IsLarger`,
  `MACD_Histogram_IsMacdMinusSignal`, `VWAP_EqualVolume_IsAverageTypicalPrice`,
  `VWAP_ResetsAtDayBoundary`, and the `*_ReturnsSameLengthAsInput` family.)*

## Epic E — Authoring & generation (web)
- **IP-US-E1 🟡** As a trader, I describe a setup in prose and Claude generates valid IdiotScript
  via the Legion high-tier voter panel, with the verb catalog reflected from code so it can't
  hallucinate syntax. *Backend verified by `StrategyScriptGeneratorTests` (verb-catalog
  reflection) and `LlmVotingServiceTests` (voting consensus) in `IdiotProof.Blazor.Tests`.
  E2E: `tests/IdiotProof.Cypress/cypress/e2e/02_strategies_describe.cy.ts` covers the
  describe-tab → generate → save → `/strategies` round-trip and activate-toggle persistence;
  the server runs with `IDIOTPROOF_FAKE_LLM=1` so `FakeLlmHandler` answers the Legion call
  deterministically (see [IP-A4](AMENDMENTS.md#IP-A4)). Cypress suite must be run against a
  live server to mark this story done.* — implements [IP-LAW-4](BIBLE.md#IP-LAW-4).
- **IP-US-E2 🟡** As a trader, I see live per-condition progress (`3/5 · IsOnReclaim(9)`) on the
  Strategies page, polled from `ConditionProgress`. *`ConditionProgressRepository` upsert path
  verified by `ConditionProgressRepositoryTests` (SQL Server LocalDB, 6 tests) in
  `IdiotProof.Blazor.Tests`. E2E: `tests/IdiotProof.Cypress/cypress/e2e/07_condition_progress.cy.ts`
  seeds the progress table via the `seedConditionProgress` Cypress task (direct SQL via
  `sqlcmd`) then asserts the Strategies page polls and renders the awaiting / partial / full-pass
  states without a reload. MonitorWorker tick integration test remains ⬜. Cypress suite must
  be run against a live server to mark this story done.*
- **IP-US-E3 🟡** As a trader, paper is the default and live trading requires an explicit
  red-outline confirmation. *`BrokerRouter` Sandbox default verified by `BrokerRouterTests`
  in `IdiotProof.Brokers.Tests` ([IP-LAW-3](BIBLE.md#IP-LAW-3)). E2E:
  `tests/IdiotProof.Cypress/cypress/e2e/03_api_keys.cy.ts` covers the Paper-checked default,
  live-trading confirmation modal (Cancel keeps Paper; Confirm flips to Live + danger banner),
  credential masking toggle for Claude key and Alpaca secret, and Save-All persistence.
  Cypress suite must be run against a live server to mark this story done.*
- **IP-US-E4 🟡** As a trader, clicking Generate dispatches through the Legion/Vault credential
  chain and renders an IdiotScript chain — the page never short-circuits with "No Claude API
  key configured". *E2E: `tests/IdiotProof.Cypress/cypress/e2e/04_vault_backed_ai.cy.ts` —
  always-on variant runs under `IDIOTPROOF_FAKE_LLM=1`; a refactor that drops credential
  resolution before dispatch surfaces as an error chip rather than a script, failing the
  assertion. Live variant (`CYPRESS_LIVE_LLM=1`) proves the real Anthropic endpoint accepted
  the key. Cypress suite must be run against a live server to mark this story done.*
- **IP-US-E5 🟡** As a trader, the bundled sample strategies (NCI Breakout-Pullback, ERNA AH
  Momentum, SUNE Wedge Breakout) build through the UI and appear on `/strategies`, so I can
  start from a known-good script. *E2E: `tests/IdiotProof.Cypress/cypress/e2e/05_build_samples.cy.ts`
  builds each sample via the AI-assist pane with the `[[script: ...]]` marker (deterministic,
  no live LLM), saves each, then asserts all three title/ticker pairs appear on the Strategies
  page. Cypress suite must be run against a live server to mark this story done.*
- **IP-US-E6 🟡** As a trader, I select a saved strategy, pick a date, run a backtest, and see
  a summary with ticker and P&L — the Run button is disabled until a strategy is selected.
  *E2E: `tests/IdiotProof.Cypress/cypress/e2e/06_backtest.cy.ts` seeds a strategy via the
  builder, navigates to `/backtest`, selects the strategy, sets a date, runs, and asserts
  `#backtest-results`, `#backtest-summary` (contains "AAPL"), and `#backtest-pnl` render;
  also verifies Run is disabled until a strategy is chosen. Cypress suite must be run against
  a live server to mark this story done.*

## Epic F — Doc/graph reconciliation
- **IP-US-F1 ✅** As a maintainer, the canon docs describe the project graph that actually builds,
  and the divergent `Core`/`Web`/IBKR narrative is gone. *[RFC 0001](rfc/0001-core-tree-reconciliation.md)
  resolved 2026-06-07: all out-of-solution trees deleted, README pruned to match `IdiotProof.slnx`.
  (verified by: `dotnet build IdiotProof.slnx` → 0 errors; `dotnet test IdiotProof.slnx` → 82/0;
  confirmed by [IP-A2](AMENDMENTS.md#IP-A2))*

## Epic G — Strategy ghost overlay + branching visualization (planned)
> From `TODO.md`. Author a strategy, press play, and watch it unfold on the chart as a
> translucent "ghost" trade path, forking at each branch point. Nothing here is built;
> prerequisites (chart component, candle feed into the UI) are not wired.

- **IP-US-G1 ⬜** As a trader, I see a price chart for my strategy's ticker inside the app
  (TradingView Lightweight Charts wrapped in a `Chart.razor` JS-interop component), so the
  ghost overlay has a surface to draw on.
- **IP-US-G2 ⬜** As a trader, I press play and the simulator replays my strategy over
  historical candles as a translucent ghost path (entries, exits, stops) with
  play/pause/step/speed controls, so I can watch the strategy unfold before risking money.
- **IP-US-G3 ⬜** As a trader, at each `If(...)` branch point the ghost forks into visible
  pass/fail paths (non-taken branch dashed), and hovering a fork shows the condition and the
  indicator values at that moment, so the full decision tree is visible in-place.
- **IP-US-G4 ⬜** As a trader, I compare multiple ghost runs side-by-side (same strategy,
  different tickers or dates) and export the branch tree as a diagram, so I can study how a
  strategy behaves across regimes. *(nice-to-have tail of the epic.)*

## Epic H — Tooling hardening (planned)
- **IP-US-H1 ⬜** As a strategy author, IdiotScript parse errors give exact line/column
  diagnostics from a Roslyn-based parser (replacing the tolerant regex parser), so I can fix
  a broken script without guessing.

## Epic I — Learning Center (planned) {#Epic-I}
> In-app documentation hub at `/learn`. Every verb and phase is rendered from live reflection
> so the docs can never drift from the DSL ([IP-LAW-4](BIBLE.md#IP-LAW-4)). Covers the full
> workflow: Strategy Builder → Monitor → ConditionProgress → three gates → fire.

- **IP-US-I1 ⬜** As a new trader, I visit `/learn` and see a visual overview diagram of the
  full IdiotProof workflow (Builder → Monitor → ConditionProgress → three gates → fire), so I
  understand the system end-to-end before writing my first strategy.
- **IP-US-I2 ⬜** As a trader, the Learning Center shows the six IdiotScript phases (Setup,
  Filters, Entry, Order, Risk, Exit) with their reflected verb catalog, so the documentation
  always matches what the DSL parser actually accepts — implements [IP-LAW-4](BIBLE.md#IP-LAW-4).
- **IP-US-I3 ⬜** As a trader, I see the three gates (Condition Match → LLM Voter Quorum →
  Risk Guardian) explained with a visual diagram and plain-English description of what each gate
  checks and why it can block a fire — implements [IP-LAW-1](BIBLE.md#IP-LAW-1).
- **IP-US-I4 ⬜** As a trader, the Learning Center shows at least three annotated example
  strategies (NCI Breakout-Pullback, ERNA AH Momentum, SUNE Wedge Breakout) with each phase
  highlighted and an "Open in Builder" link that seeds the describe pane, so I can study and
  remix a known-good script.
- **IP-US-I5 ⬜** As a trader, every Learning Center section has a contextual "try it" link
  (e.g. "Try the Builder", "View Strategies", "Run a Backtest") so I can move directly from
  reading to doing without navigating manually.

## Epic J — Backtest UI enhancement (planned) {#Epic-J}
> Enhances the existing `/backtest` page and `StrategyBacktester.Run()` /
> `BacktestReport` pipeline in `IdiotProof.Strategies`. Adds historical candle fetch,
> per-candle condition pass/fail table, and hypothetical P&L summary.

- **IP-US-J1 ⬜** As a trader, when I run a backtest for a chosen date, the engine fetches
  that day's minute-resolution historical candles from Alpaca (falling back to Polygon) and
  evaluates the strategy tick-by-tick via `StrategyBacktester.Run()`, so the result reflects
  real market data and not synthetic candles.
- **IP-US-J2 ⬜** As a trader, the backtest results show a per-candle condition table
  (timestamp, price, each condition pass/fail column, and whether the signal fired at that
  candle), so I can see exactly where in the day the strategy triggered or stalled.
- **IP-US-J3 ⬜** As a trader, the backtest summary shows total signal count, hypothetical
  entry/exit prices for each fired trade, and cumulative P&L for the day, so I can judge
  whether the strategy behaves as expected before enabling it on the live Monitor.

## Priority backlog
1. **IP-US-E1–E6** — Full Cypress suite (7 specs: 02–07) covers describe→generate→save,
   condition-progress badge, API keys, vault-backed AI wiring, sample builds, and backtest UI.
   Start the server with `IDIOTPROOF_FAKE_LLM=1`, then run `npm run cypress:run` (or open
   Cypress interactively) to prove all six E-stories green and mark them done. See [IP-A4](AMENDMENTS.md#IP-A4).
2. **Epic I (IP-US-I1…I5, all ⬜)** — Learning Center at `/learn`: workflow overview diagram,
   reflected six-phase verb catalog, three-gates diagram, annotated example strategies with
   "Open in Builder", contextual "try it" links. See [IP-A6](AMENDMENTS.md#IP-A6).
3. **Epic J (IP-US-J1…J3, all ⬜)** — Backtest UI enhancement: historical candle fetch from
   Alpaca/Polygon, per-candle condition pass/fail table, hypothetical P&L summary.
   See [IP-A6](AMENDMENTS.md#IP-A6).
4. **Epic G (IP-US-G1…G4, all ⬜)** — Strategy ghost overlay + branching visualization (from
   `TODO.md`): chart integration, simulator evaluation timeline, branch-fork rendering,
   scrub/playback.
5. **IP-US-H1 (⬜)** — Roslyn-based IdiotScript parser: exact line/col diagnostics replacing
   the regex parser.

### Audit log
No prior `user_stories.md` existed in this repo; these stories were authored fresh from the
README, `CLAUDE.md`, `TODO.md`, and the test tree on 2026-06-07. No story has been re-scoped
yet, so there is nothing to preserve as an original spec.
