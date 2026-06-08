---
codex: 1
project: IdiotProof
code: IP
layer: stories
status: living
updated: 2026-06-08
counts: {done: 19, partial: 3, planned: 0, cut: 0}
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
  hallucinate syntax. *Backend verified by `StrategyScriptGeneratorTests` (verb-catalog reflection)
  and `LlmVotingServiceTests` (voting consensus) in `IdiotProof.Blazor.Tests`. E2E:
  `tests/IdiotProof.Cypress/cypress/e2e/02_strategies_describe.cy.ts` covers the
  describe-tab → generate → save → `/strategies` round-trip (intercepts the LLM call with
  a stub). Cypress suite must be run against a live server to mark this story done.* — implements
  [IP-LAW-4](BIBLE.md#IP-LAW-4).
- **IP-US-E2 🟡** As a trader, I see live per-condition progress (`3/5 · IsOnReclaim(9)`) on the
  Strategies page, polled from `ConditionProgress`. *`ConditionProgressRepository` upsert path
  now directly verified by `ConditionProgressRepositoryTests` (SQL Server LocalDB) in
  `IdiotProof.Blazor.Tests`: insert on first call (`UpsertAsync_FirstCall_InsertsRow`), update
  on second (`UpsertAsync_SecondCall_UpdatesExistingRow`), full-pass clears verb
  (`UpsertAsync_FullPass_ClearsFirstFailingVerb`), zero-condition fast path
  (`UpsertAsync_ZeroConditions_Stores_0_0_Null`), two strategies track independently
  (`UpsertAsync_TwoStrategies_TrackIndependently`), bulk read
  (`GetForStrategyIdsAsync_ReturnsDictionary_KeyedById`). The Strategies-page live-badge
  Cypress spec and the full MonitorWorker tick integration test remain ⬜.*
- **IP-US-E3 🟡** As a trader, paper is the default and live trading requires an explicit
  red-outline confirmation. *`BrokerRouter` Sandbox default verified by `BrokerRouterTests`
  in `IdiotProof.Brokers.Tests` ([IP-LAW-3](BIBLE.md#IP-LAW-3)). E2E:
  `tests/IdiotProof.Cypress/cypress/e2e/03_api_keys.cy.ts` covers the Paper-checked default,
  the live-trading confirmation modal, Cancel keeps Paper, Confirm flips to Live with danger
  banner. Cypress suite must be run against a live server to mark this story done.*

## Epic F — Doc/graph reconciliation
- **IP-US-F1 ✅** As a maintainer, the canon docs describe the project graph that actually builds,
  and the divergent `Core`/`Web`/IBKR narrative is gone. *[RFC 0001](rfc/0001-core-tree-reconciliation.md)
  resolved 2026-06-07: all out-of-solution trees deleted, README pruned to match `IdiotProof.slnx`.
  (verified by: `dotnet build IdiotProof.slnx` → 0 errors; `dotnet test IdiotProof.slnx` → 82/0;
  confirmed by [IP-A2](AMENDMENTS.md#IP-A2))*

## Priority backlog
1. **IP-US-E1 / E2 / E3** — Cypress specs exist for E1 + E3; E2 condition-progress upsert now
   verified by NUnit SQL Server integration tests. Run `npm run cypress:run` against a live
   Blazor server to prove E1 + E3 green and mark all done. A Cypress badge spec for E2 remains ⬜.
2. **Epic G (⬜) — Strategy ghost overlay + branching visualization** (from `TODO.md`): chart
   integration, simulator evaluation timeline, branch-fork rendering, scrub/playback.
3. **Roslyn-based IdiotScript parser** — exact line/col diagnostics replacing the regex parser.

### Audit log
No prior `user_stories.md` existed in this repo; these stories were authored fresh from the
README, `CLAUDE.md`, `TODO.md`, and the test tree on 2026-06-07. No story has been re-scoped
yet, so there is nothing to preserve as an original spec.
