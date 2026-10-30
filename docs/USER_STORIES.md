---
codex: 1
project: IdiotProof
code: IP
layer: stories
status: living
updated: 2026-10-03
---
<!-- GENERATED from MindAttic.Tickets/data/Tickets.db by `tickets db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the tickets CLI. -->

# IdiotProof — User Stories
> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites the test.
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
  (circuit breaker), so a bad day can't compound — and a completed trade's loss counts toward
  today's total even if it's the Guardian's very first interaction of the day (an exit with no
  prior entry check that day). *(verified by `ValidateTrade_DailyLossAlreadyExceeded_IsBlocked`,
  `RecordTradePnL_AsFirstEverCall_InitializesDailyLossWithoutValidateTrade`,
  `RecordTradePnL_ThenValidateTrade_SameDay_ReflectsAccumulatedLoss`.)*
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
- **IP-US-C5 ✅** As a developer, a backtest over no candles returns an empty report without
  throwing. *(verified by `Run_OnEmptyCandles_ReturnsEmptyReport_NoThrow`.)*

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

## Epic K — Gapper: buy the gap at 4AM, sell before the bell {#Epic-K}
> The flagship flow ([BIBLE §4.4](BIBLE.md#IP-§4)).
> Pick up to 3 tickers on the `/gapper` tab, dial in a profile per ticker, queue; the console
> Monitor buys the gap through the three gates in the premarket window and sells it off before
> the 9:30 bell once momentum rolls over.

- **IP-US-K1 ✅** As a trader, I select a gapper profile, dial it in, and the generated
  IdiotScript survives a full parser round trip — every dialed value (session, entry window,
  gap band, volume, price band, notional, stop %, trailing %, giveback + arm time, sell-by)
  reaches the Monitor exactly as I set it. *(verified by
  `GapperScriptFactory_Script_SurvivesParserRoundTrip`, `GapperScriptFactory_OpenEndedGap_EmitsIsGapUp`,
  `GapperProfile_Validate_CatchesBadDialIns` in `IdiotProof.Strategies.Tests/GapperTests.cs`.)*
- **IP-US-K2 ✅** As a trader, gap conditions evaluate against the real previous close and fail
  closed when it is unknown, so an uncomputable gap can never wave a trade through — and the
  previous-close lookup itself picks the right calendar day regardless of what instant a
  daily bar is timestamped at. *(verified by `IsGapUp_FailsClosed_WithoutPreviousClose`,
  `IsGapUp_Passes_WhenGapMeetsThreshold`, `IsGapBetween_EnforcesBandAndFailsClosed`,
  `GetPreviousCloseAsync_UtcMidnightStampedDailyBars_PicksYesterdayNotToday`.)*
- **IP-US-K3 ✅** As a trader, my gapper only hunts entries inside its ET entry window
  (default 04:00–09:00), on the US-market clock regardless of host timezone. *(verified by
  `TimeWindowCondition_GatesOnEasternClock`, `TimeWindowCondition_WrapsOvernightWindows`.)*
- **IP-US-K4 ✅** As a trader, my held gapper is sold off before the bell: hard sell-by time
  always flattens; the momentum-rollover exit (giving back N% of the entry→peak run) arms in
  the final premarket minutes; hard/trailing stops protect the whole hold. *(verified by
  `Exit_PeakGiveback_SellsAfterMomentumRollsOver`, `Exit_PeakGiveback_NotArmedBeforeArmTime`,
  `Exit_SellBy_AlwaysFlatBeforeTheBell`, `Exit_HardStop_TripsOnEntryDrawdown`,
  `Exit_TrailingStop_TripsOffPeakBeforeArmTime`, `Exit_HoldsWhileMomentumIntact`.)*
- **IP-US-K5 ✅** As a trader, premarket orders are limit + DAY + `extended_hours` (Alpaca's
  hard requirement) — anything else is rejected locally instead of silently queueing to 9:30 —
  and the sandbox broker simulates fills into a real position book so the whole loop runs
  keyless. *(verified by `ExtendedHours_MarketOrder_IsRejectedLocally`,
  `ExtendedHours_LimitGtc_IsRejectedLocally`, `Buy_ThenGetPositions_ShowsTheFill`,
  `Sell_FullQuantity_FlattensThePosition`, `NotionalBuy_ConvertsToShares_AtTheLimitPrice`
  in `IdiotProof.Brokers.Tests`.)*
- **IP-US-K6 🟡** As a trader, the console Monitor is the one pipeline: it re-reads my queued
  gappers from SQL every tick (UI edits apply live), streams Alpaca data (websocket bars +
  last trades, REST backfill, Mock fallback with gap simulation), fires through the three
  gates, places the entry through `BrokerRouter`, tracks the position on the Strategy row,
  and exits it via the rollover brain — feeding realized P&L into the RiskGuardian daily
  circuit breaker. *The full mock-gap-day lifecycle (previous close → gap screen → premarket
  entry → hold → rollover sell before the bell) is proven by
  `MockGapDay_EntryFires_InPremarket_ThenGivebackExit_BeforeTheBell` and
  `MockGapDay_HardSellBy_FlattensEvenIfMomentumNeverRollsOver` in
  `IdiotProof.Strategies.Tests/GapperLifecycleTests.cs`. Remaining for ✅: a host-level harness
  test of MonitorWorker itself (wall-clock session gate + broker order path in one run).*
- **IP-US-K7 🟡** As a trader, the `/gapper` tab shows my queued gappers with live state
  (condition progress, HOLDING qty@price, sold @price · reason) polled from SQL every 5s.
  *Page built (`Components/Pages/Gapper.razor` + nav tab); Cypress spec remains ⬜.*
- **IP-US-K8 ✅** As one of several simultaneous users, my orders route to MY broker account:
  Alpaca only when I opted in and supplied both keys (my own paper/live flag), otherwise the
  global Sandbox-default router — a missing or undecryptable key can never route my order into
  someone else's account. *(verified by `Choose_AlpacaOptInWithBothKeys_RoutesToUserAccount`,
  `Choose_MissingEitherKey_FallsThroughToGlobalDefault`,
  `Choose_NoBrokerPreference_FallsThroughToGlobalDefault` in
  `IdiotProof.Blazor.Tests/UserBrokerResolverTests.cs`; see [BIBLE §4.4](BIBLE.md#IP-§4).)*
- **IP-US-K9 🟡** As an operator, the console runs as a real service: Windows Service hosting
  (`IdiotProof.Monitor`), and a SQL `sp_getapplock` leader lease so at most one instance
  evaluates/trades per database (standbys wait and take over on leader death). *Implemented
  (`MonitorLeaderLease`, `AddWindowsService`); an automated two-instance contention test
  remains ⬜.*
- **IP-US-K10 🟡** As a trader, I paste a video transcript (or any natural language) into the
  Gapper tab's "From a transcript" box and Claude — via Legion (HOUSE-LAW-4) — extracts gapper
  candidates with per-ticker dial-ins, which I **review as cards and queue individually**
  (nothing a transcript says can queue itself). Every model output is re-validated fail-closed:
  partial overlays keep base-profile defaults, invalid symbols and impossible dial-ins are
  skipped with warnings. *(Parse/validation contract verified by
  `Parse_PartialOverlay_ChangesOnlyThoseFields`, `Parse_ProseWrappedJson_ExtractsTheArray`,
  `Parse_InvalidSymbol_SkippedWithWarning_OthersSurvive`, `Parse_HallucinatedBadDialIns_FailClosed`,
  `Parse_Garbage_ReturnsEmptyWithWarning`, `Parse_CaseInsensitivePropertyNames_StillApply`,
  `SystemPrompt_CarriesTheLiveBaseDefaults` in `IdiotProof.Blazor.Tests/GapperInterpreterTests.cs`.
  Live LLM round trip + Cypress spec remain ⬜.)*
- **IP-US-K11 ✅** As a trader, what the console evaluates is a versioned, STRICT JSON canon of
  my strategy — losslessly round-tripping composition and branching the text format drops —
  and anything it can't FULLY understand is quarantined with a visible reason instead of
  partially evaluated; a valid canon always beats stale script text, and only canon-less
  legacy rows ever touch the tolerant text parser — implements [IP-LAW-8](BIBLE.md#IP-LAW-8).
  *(verified by `RoundTrip_Gapper_PreservesEveryField`,
  `RoundTrip_ComposedConditionsAndBranching_SurviveWhereTextDoesNot`,
  `Deserialize_UnknownSchemaVersion_Throws`, `Deserialize_UnknownConditionType_Throws`,
  `Deserialize_UnknownProperty_Throws`, `Deserialize_Garbage_Throws`,
  `Loader_PresentButBrokenCanon_QuarantinesInsteadOfTextFallback`,
  `Loader_LegacyRowWithoutCanon_FallsBackToTextParse`, `Loader_ValidCanon_WinsOverText` in
  `IdiotProof.Strategies.Tests/StrategyJsonTests.cs`.)*
- **IP-US-K12 ✅** As a trader, I replay my gapper dials over a past day (Alpaca bars when
  keyed, deterministic Mock otherwise) and see exactly what WOULD have happened — entries via
  the same condition walk and exits via the same `GapperExitEvaluator` the live console runs —
  then examine the peak/drawdown, a giveback grid, and hindsight suggestions, and apply the
  **tuned profile** back into my dials for a real trading day. No-entry days name the exact
  blocking condition; missing previous close fails closed like live. *(verified by
  `Replay_MockGapDay_EntersHoldsAndExitsBeforeTheBell`,
  `Replay_GivebackGrid_CoversTheDial_AndBestIsAtLeastActual`,
  `Replay_TunedProfile_IsValid_AndCarriesTheHindsightDials`,
  `Replay_ImpossibleGapScreen_ReportsNoEntryWithTheBlocker`,
  `Replay_NoPreviousClose_FailsClosedLikeLive`, `Replay_NoBars_ReportsCleanly` in
  `IdiotProof.Strategies.Tests/GapperDayBacktesterTests.cs`.)*
- **IP-US-K13 ✅** As a trader, a SHORT strategy that clears the conditions, the LLM panel and
  the Risk Guardian opens a real short on the strategy's own Paper/Live/Sandbox account
  (`sell_to_open` limit), is managed by the mirrored exit brain, and covers with a
  `buy_to_close`, with realized P&L inverted into the daily circuit breaker — the same three
  gates as a long. *(Order shapes and a short round trip on the Sandbox broker verified by
  `ShortEntry_IsASellToOpen_BelowThePrice`, `LongEntry_IsUnchanged_BuyAbovePrice_NoIntent`,
  `Exits_AlwaysClose_OnTheOppositeSide`, `HeldShares_CountsOnlyThePositionsOwnSide`,
  `RealizedPnl_IsInvertedForAShort`, `ShortRoundTrip_OnTheSandboxBroker_OpensThenFlattens` in
  `IdiotProof.Monitor.Tests/DirectionalOrdersTests.cs`.)*
- **IP-US-K14 ✅** As one of several users, the Monitor's LLM gate votes on MY Claude key from
  the API Keys page (falling back to the host key when I have none, never to another user's),
  and my "Enabled" toggle turns the gate on for my strategies. *(verified by
  `EachOwner_GetsTheirOwnKey_NeverAnotherUsers`, `OwnerWithoutKey_FallsBackToTheHostKey`,
  `NoOwnerKey_NoHostKey_ResolvesNone`, `OwnerToggle_AddsTheGate_ButCannotRemoveAHostWideOne`,
  `UnreadableOwnerRow_FallsBackToTheHostKey` in
  `IdiotProof.Blazor.Tests/UserClaudeKeyResolverTests.cs`,
  `Vote_PanelApproves_ConsensusApprove_AndClaudeVotesOnTheOwnersKey`, and — saving a key on the
  page stores it for me only, never for another user or as the host key —
  `SavingAClaudeKey_StoresItOnlyForThatUser_NeverForAnotherUserOrTheHost`,
  `ApiKeysPage_SaysTheKeyIsForThisAccountOnly` in
  `IdiotProof.Blazor.Tests/ClaudeKeyIsolationTests.cs`.)*
- **IP-US-K15 ✅** As a trader, a candidate fire is voted on by MindAttic.Legion's own panel —
  every keyed `legion.json` voter (claude, openai, gemini, deepseek) — and only an explicit
  approval quorum lets it through; a split, a malformed ballot or a dead panel blocks it —
  implements [IP-LAW-1](BIBLE.md#IP-LAW-1). *(verified over a fake vendor transport by
  `ShippedLegionJson_DeclaresTheFourVoterPanel_WithClaudeAsJudge`,
  `BuildPanel_SeatsClaudeOnTheOwnersKey_PlusEveryKeyedLegionVoter`,
  `Vote_PanelApproves_ConsensusApprove_AndClaudeVotesOnTheOwnersKey`,
  `Vote_SplitPanel_FailsClosedToAbstain`, `Vote_UnparseableBallot_NeverApproves`,
  `Vote_DisabledOrUnkeyed_SkipsThePanel` in `IdiotProof.Blazor.Tests/LlmVotingServiceTests.cs`.
  The live vendor round trip is not run.)*
- **IP-US-K16 🟡** As a trader, the short entry and cover that IP-US-K13 builds are orders the real
  Alpaca account accepts: `side: sell` + `position_intent: sell_to_open` to open and `side: buy` +
  `position_intent: buy_to_close` to cover, with a positive `qty`, and an account that cannot short
  gets Alpaca's reason as a failed entry. *(Wire shape, the 403 can't-short answer and negative short
  positions verified by `ShortOrders_SendSideAndIntent_WithPositiveQty`, `LongEntry_SendsNoIntent`,
  `AccountThatCannotShort_IsAFailedOrder_WithAlpacasReason`, `ShortPosition_ParsesAsNegativeQuantity`
  in `IdiotProof.Brokers.Tests/AlpacaEquityShortWireTests.cs`. Against the real paper account on
  2026-10-03, by the opt-in `[Explicit]` `AlpacaPaperShortIntegrationTests` in
  `IdiotProof.Monitor.Tests`: `LongExit_SellToClose_UnfillableLimit_IsAcceptedThenCancelled` passed,
  so Alpaca accepts `side` + `position_intent` on an equity and echoes the intent back.
  `ShortEntry_SellToOpen_UnfillableLimit_IsAcceptedThenCancelled` was Inconclusive: Alpaca returned
  `403 40310000 "account is not allowed to short"` because the paper account has shorting disabled
  and equity under $2,000. Open: an accepted `sell_to_open` and a filled short covered by
  `buy_to_close` on a short-enabled paper account.)*

## Epic R — Strategy replay, scanner & ML dataset {#Epic-R}
- **IP-US-R1 🟡** As a trader, I can replay any strategy against a past ET session and see the
  exact entry→exit round-trips (price, time, P&L, exit reason), evaluated by the same code the
  live Monitor runs, so a chart hunch is checked against what the rules actually do. Shipped
  (replay command); NUnit coverage pending.
- **IP-US-R2 🟡** As a trader, I can replay a ticker with no saved strategy by applying a gapper
  profile — or a built-in repeating momentum strategy — on the fly, so any scanner name is
  analysable immediately. Shipped; tests pending.
- **IP-US-R3 🟡** As the platform, every replay is persisted as a ReplayRun row and the whole
  published archive regenerates from SQL alone (replay-regen), so the database — not the file
  tree — is authoritative [IP-LAW-7]. Shipped; tests pending.
- **IP-US-R4 🟡** As a trader, one scan pulls the morning movers from Alpaca and auto-replays
  each gapper into the archive, so the board populates itself. Shipped; tests pending.
- **IP-US-R5 🟡** As an analyst, I can export the archive to ML-ready CSVs (per-trade features →
  P&L label, per-bar time series) so the accumulating replays can train models. Shipped
  (replay-export); tests pending.
- **IP-US-R6 🟡** As a trader, beyond gappers I can replay/scan non-gapper families — a reversal
  dip-buy (EMA9 reclaim off lows) and an EMA200 trend-break — so range/reversal setups (BE, SPCX,
  AMD) are analysable too. Shipped (--profile reversal | emabreak); tests pending.
- **IP-US-R7 🟡** As an analyst, replays land in a normalized SQL feature store (ReplayTrade /
  ReplayBar) I can query directly (features → win/P&L), not just in CSV blobs, so the dataset is a
  first-class store. Shipped; tests pending.
- **IP-US-R8 🟡** As a user, I could link my Alpaca account by OAuth (authorize on Alpaca, store a
  scoped revocable token) instead of pasting a raw key/secret. Foundation shipped
  (AlpacaOAuthClient); endpoints + Bearer broker wiring gated on app registration + paper testing.
 
- **IP-US-R9 🟡** As a trader, I can replay/scan SHORT setups (short a failed high below VWAP),
  with exit logic mirrored for shorts (stops above entry, cover on a bounce) and P&L inverted, so
  fade days (NVDA/PANW/BX) are analysable. Shipped (shortfade + EvaluateShort); tests pending.
- **IP-US-R10 🟡** As a trader, an RTH open-drive family catches the 9:30 rocket (trend-holding
  entry: above VWAP + above EMA9 + EMA-stacked), which a crossing trigger misses. Shipped
  (rthdrive); tests pending.
- **IP-US-R11 🟡** As a trader, an RSI-oversold-at-support dip-buy family exists (rsireversal).
  Shipped; tests pending. Still knife-prone: RSI divergence is declared but not computed.
- **IP-US-R12 🟡** As a trader, a swing-structure primitive (pivot-based higher-low/lower-high)
  powers a double-bottom family that buys a confirmed higher low and targets the prior high-of-day
  (swingreversal + IsHigherLow/IsLowerHigh + ExitAtPriorHigh). Shipped; tests pending.

## Epic S — Adaptive auto-strategy generation from observed behavior (planned) {#Epic-S}
> The system watches live price action and AUTO-GENERATES + arms strategies fitted to each
> ticker's own behavior, instead of a human authoring each one. The on-demand gapper generator
> (`auto-gapper` CLI + `AutoGapperScanner` adaptive synthesizer + `AutoGapperScan`/`AutoGapperCandidate`
> feature store) is the working seed. **Design principle: wait for enough information before
> generating** — a trigger in the first thin premarket minutes acts on data that has not locked
> in yet. Nothing here is built beyond the seed.
- **IP-US-S1 ⬜** As a trader, "start-of-period quick strategy generation" is a standardized flow:
  at the open of a period the system observes each candidate (volume, range, trend, gap) and
  generates a strategy only once it has *enough* information — never on the first thin minute.
- **IP-US-S2 ⬜** As a trader, the biggest payoff is intraday (RTH): the system watches a stock
  plummet and arms a mean-reversion LONG at the low-of-day, and watches a stock soar and arms a
  fade SHORT at the high-of-day — reversion/exhaustion setups the adaptive engine can template.
- **IP-US-S3 ⬜** As a trader, each generated strategy's stop/trailing/giveback/size is DETERMINED
  PER TICKER from its observed behavior (price tier, gap size, premarket/RTH volume, ATR), reusing
  the adaptive synthesizer prototyped by the on-demand gapper generator.
- **IP-US-S4 ⬜** As the platform, every observation and generation decision (the full feature
  vector + chosen parameters + arm/skip outcome) is captured to the auto-gapper feature store, so a
  model can learn which auto-strategies pay off — joined to the TradeDiary for the realized-P&L label.

## Epic T — Autonomous market-event research scanner {#Epic-T}
> The `/research` tab is a results review, not a search box: `IdiotProof.ResearchScanner`
> ([BIBLE §4.4](BIBLE.md#IP-§4)), a scheduled, silent console app, sweeps EDGAR/Alpaca/Federal
> Register on its own and ranks what it finds.
- **IP-US-T1 ✅** As a trader, I don't have to type a ticker for the system to find market-moving
  events — a scheduled scan sweeps my watchlist plus a rotating batch of the tracked universe on
  its own. See `TickerUniverseServiceTests`.
- **IP-US-T2 ✅** As a trader, an insider transaction claim reports the actual share count,
  price, and % of holdings changed — not "ownership changed." See `Form4ParserTests`.
- **IP-US-T3 ✅** As a trader, high-value 8-K filings (splits, M&A, material agreements) get their
  real document text fetched automatically instead of generic filing boilerplate, while routine
  8-Ks (director departures, exhibit-only filings) don't cost an extra fetch. See
  `CorporateActionDetectorTests`.
- **IP-US-T4 ✅** As a trader, an exchange rule change that affects many companies (not one
  ticker) shows up as its own macro event, with routine SRO fee-schedule paperwork filtered out
  by an LLM substantiveness check. Proven against the real Nasdaq $5M MVLS continued-listing rule
  in `RegulatoryScannerTests`.
- **IP-US-T5 ✅** As a trader, every claim gets a computed 0-100 significance score (magnitude,
  historical correlation, source trust, recency, watchlist membership) so the feed can rank
  itself instead of me filtering. See `SignificanceScorerTests`.
- **IP-US-T6 ✅** As a trader, claim summaries read like sober equity research — what happened,
  which tickers it affects and why, and when — never clickbait, because the display sentence is
  composed from structured fields rather than trusted as free LLM prose. See the composed-sentence
  assertions in `RegulatoryScannerTests`.
- **IP-US-T7 🟡** As a trader, the Research tab's primary view is a significance-ranked feed with
  a last-scan banner and a "my watchlist only" toggle, not a search box — built in
  `Research.razor`; no Cypress spec covers it yet.
- **IP-US-T8 ✅** As a trader, I can trust the significance score means something — the system
  actually checks whether past news correlated with what the stock did next, not just assumes
  it. `OutcomeBackfillService` fetches real price history and marks each old-enough claim
  Realized/Disproven against its Bullish/Bearish call. Proven by `OutcomeBackfillServiceTests`.

## Epic U — Options: buy the idea, sell the hype {#Epic-U}
> Manual single-leg calls and puts on the user's Alpaca account, presented so the premium cost,
> breakeven, and the intrinsic ("real") vs extrinsic ("hype") split are never mental math.
> Separate `/options` section, not the Stock Strategy pipeline ([BIBLE §4.4](BIBLE.md#IP-§4)).
> Manual only: no DSL, Monitor, or RiskGuardian involvement.
- **IP-US-U1 ✅** As a trader, any OCC option symbol (`BE251219C00038000`) is decoded into
  underlying / expiration / right / strike for me — and built back — so I never read one by hand.
  See `OptionContractOccTests`.
- **IP-US-U2 ✅** As a trader, every contract shows its real (intrinsic) value, its hype (extrinsic)
  value in $ and % of premium, its breakeven if held to expiration, days to expiration, and
  moneyness, computed from the live premium. See `IntrinsicValueCalculatorTests`.
- **IP-US-U3 ✅** As a trader, I get a Black-Scholes fair value and an implied volatility even when
  the broker doesn't supply Greeks — solved locally, badged `Model`, textbook-verified, with the
  European-exercise/no-dividend simplifications documented. See `BlackScholesCalculatorTests`.
- **IP-US-U4 ✅** As a trader, the Alpaca options wire format is right before it's ever sent:
  OCC symbol, whole contracts (notional rejected), DAY, no extended hours, Market/Limit only;
  chains page through `next_page_token`; snapshots come from the data host with Greeks/IV left
  null when Alpaca omits them; `us_option` positions decode into contracts. See `AlpacaOptionsWireTests`.
- **IP-US-U5 ✅** As a trader without options approval yet, the Sandbox account serves a realistic
  synthetic chain and fills contracts (× 100) into the position book so I can practise the whole
  flow today. See `SandboxOptionsTests`.
- **IP-US-U6 ✅** As a trader, the `/options` page shows CALLS | strike | PUTS with a colour-ramped
  hype meter per cell, a ticket that spells out "you're buying 2 BE $38 calls for about $1,900; $500
  of that is hype; breakeven $47.50 — you don't have to get there", and my open option positions
  with a real/hype split bar. E2E: `tests/IdiotProof.Cypress/cypress/e2e/08_options.cy.ts`
  (Sandbox: empty state → chain → ticket → confirm → position → close). Presenter math:
  `OptionsPresenterTests`, `OptionPositionViewTests` in `IdiotProof.UI.Tests`.
- **IP-US-U7 ✅** As a trader holding a long option, I'm told when extrinsic value is near its
  recent high AND the research tape is bullish on the underlying — "consider taking profit" —
  informationally, never as an auto-sell. See `SellSignalEvaluatorTests`.
- **IP-US-U8 🟡** As a trader, a Live options order requires the same 5-minute password elevation
  as promoting a strategy to Live, and the ticket locks itself with an explanation while Alpaca
  reports `options_trading_level = 0`. Lock rule proven by `OptionsTradingLevelTests`; field parsing
  by `TradingLevel_ReadsAlpacasPluralFieldNames`. The Live elevation path itself
  (`OptionsLiveElevationModal`) needs a live Alpaca account and is not Cypress-provable — 🟡.
- **IP-US-U9 ✅** As a trader, an announced S&P 500/100 addition or deletion I log in
  `sp-index-events.json` shows up in the Research feed as an `IndexEvent` claim (Bullish for a
  joiner, Bearish for a leaver), Pending until the effective date and then Realized, without
  duplicates on later passes. See `IndexEventScannerTests`.
- **IP-US-U10 🟡** As a trader, a small paper options order round-trips against my real Alpaca
  paper account. Place + cancel proven 2026-09-05 by the opt-in `AlpacaPaperOptionsIntegrationTests`
  (`[Explicit]`, `IdiotProof.Brokers.Tests`): level 3 read, full BE chain + snapshots, one
  buy-to-open limit at $0.01 accepted then cancelled, account flat.
  The fill-and-close half needs market hours and a deliberate user-initiated order — 🟡.
- **IP-US-U11 ✅** As a trader who doesn't speak options, every piece of jargon on the page (call,
  put, strike, hype, breakeven, IV, bid/ask, buy-to-open…) is a dotted word I can hover for a
  one-line hint or click for a plain-English card, and no Alpaca code (`buy_to_open`) ever reaches
  the screen. One source of truth: `OptionsGlossary` (`OptionsGlossaryTests`); rendering:
  `08_options.cy.ts` "explains its jargon".
- **IP-US-U12 ✅** As a trader on a level-1 Alpaca account, the ticket tells me I can't buy a call
  or put outright (covered-only) instead of letting Alpaca reject the order, and the level banner
  shows what each level allows. `OptionsTradingLevelTests`; Sandbox (level 3) path in `08_options.cy.ts`.

## Epic E — Authoring & generation (web)
- **IP-US-E1 🟡** As a trader, I describe a setup in prose and Claude generates valid IdiotScript
  through Legion (one Claude call, parse-checked), with the verb catalog reflected from code so
  it can't hallucinate syntax. *Backend verified by `StrategyScriptGeneratorTests` (verb-catalog
  reflection) in `IdiotProof.Blazor.Tests`.
  E2E: `tests/IdiotProof.Cypress/cypress/e2e/02_strategies_describe.cy.ts` covers the
  describe-tab → generate → save → `/strategies` round-trip and activate-toggle persistence;
  the server runs with `IDIOTPROOF_FAKE_LLM=1` so `FakeLlmHandler` answers the Legion call
  deterministically. Cypress suite must be run against a
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
- **IP-US-E7 ✅** As a user who forgot my password, I follow "Forgot password?" on `/login`, get
  an emailed single-use link to this app's `/account/reset` page (absolute, from
  `MindAttic:Auth:Reset:PublicBaseUrl`: `https://localhost:65025` in Development, the web app's
  own origin in `infra/main.bicep`), and setting a new password there replaces the old one. Mail
  is sent only when the Vault `Notifications` SMTP settings are configured. *(verified by
  `RequestReset_EmailsAnAbsoluteLinkToTheResetPage_WhichResetsThePassword`,
  `ForgotPasswordPage_PostsToTheLibraryRequestEndpoint`, `TheResetAndForgotPages_AreAnonymous`,
  `DevelopmentPublicBaseUrl_IsTheLaunchProfileOrigin`, `AzureWebApp_SetsItsOwnPublicBaseUrl` in
  `IdiotProof.Blazor.Tests/PasswordResetFlowTests.cs`.)*

- **IP-US-E8 ✅** As a signed-in user, the Describe tab, the Gapper transcript reader and the
  Research page's analysis call Claude with MY key from the API Keys page, then the host key,
  and otherwise tell me no key is configured; another user's key is never used for my request.
  *(verified by `SignedInKey_IsResolvedPerCall_ForWhoeverIsSignedIn`,
  `Describe_CallsClaudeWithTheUsersOwnKey`,
  `Describe_WithNoUserOrHostKey_SaysNoKeyConfigured_AndCallsNothing`,
  `GapperTranscript_CallsClaudeWithTheUsersOwnKey_ThenTheHostKey`,
  `ResearchAnalysis_ExtractsWithTheUsersOwnKey` in
  `IdiotProof.Blazor.Tests/UserClaudeKeyRoutingTests.cs`.)*

## Epic G — Strategy ghost overlay + branching visualization (planned)
> Author a strategy, press play, and watch it unfold on the chart as a
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

## Epic I — Learning Center {#Epic-I}
> In-app documentation hub at `/learn`. Every verb and phase is rendered from live reflection
> so the docs can never drift from the DSL ([IP-LAW-4](BIBLE.md#IP-LAW-4)). Covers the full
> workflow: Strategy Builder → Monitor → ConditionProgress → three gates → fire. Every section
> below is built in `Learn.razor`; 🟡 until a Cypress spec proves the page.

- **IP-US-I1 🟡** As a new trader, I visit `/learn` and see a visual overview diagram of the
  full IdiotProof workflow (Builder → Monitor → ConditionProgress → three gates → fire), so I
  understand the system end-to-end before writing my first strategy.
- **IP-US-I2 🟡** As a trader, the Learning Center shows the six IdiotScript phases (Setup,
  Filters, Entry, Order, Risk, Exit) with their reflected verb catalog, so the documentation
  always matches what the DSL parser actually accepts — implements [IP-LAW-4](BIBLE.md#IP-LAW-4).
- **IP-US-I3 🟡** As a trader, I see the three gates (Condition Match → LLM Voter Quorum →
  Risk Guardian) explained with a visual diagram and plain-English description of what each gate
  checks and why it can block a fire — implements [IP-LAW-1](BIBLE.md#IP-LAW-1).
- **IP-US-I4 🟡** As a trader, the Learning Center shows at least three annotated example
  strategies (NCI Breakout-Pullback, ERNA AH Momentum, SUNE Wedge Breakout) with each phase
  highlighted and an "Open in Builder" link that seeds the describe pane, so I can study and
  remix a known-good script.
- **IP-US-I5 🟡** As a trader, every Learning Center section has a contextual "try it" link
  (e.g. "Try the Builder", "View Strategies", "Run a Backtest") so I can move directly from
  reading to doing without navigating manually.

## Epic J — Backtest UI {#Epic-J}
> Enhances the existing `/backtest` page and `StrategyBacktester.Run()` /
> `BacktestReport` pipeline in `IdiotProof.Strategies`. Adds historical candle fetch,
> per-candle condition pass/fail table, and hypothetical P&L summary. Built in `Backtest.razor`
> + `BacktestReport.ConditionTable`; 🟡 until the Cypress run proves it end to end.

- **IP-US-J1 🟡** As a trader, when I run a backtest for a chosen date, the engine fetches
  that day's minute-resolution historical candles from Alpaca (Mock when unkeyed) and
  evaluates the strategy tick-by-tick via `StrategyBacktester.Run()`, so the result reflects
  real market data and not synthetic candles.
- **IP-US-J2 🟡** As a trader, the backtest results show a per-candle condition table
  (timestamp, price, each condition pass/fail column, and whether the signal fired at that
  candle), so I can see exactly where in the day the strategy triggered or stalled.
- **IP-US-J3 🟡** As a trader, the backtest summary shows total signal count, hypothetical
  entry/exit prices for each fired trade, and cumulative P&L for the day, so I can judge
  whether the strategy behaves as expected before enabling it on the live Monitor.

## Priority backlog
0. **IP-US-K6/K7/K9** — host-level MonitorWorker test (mock gap day: queue → 4AM fire → hold →
   rollover sell before 9:30), `/gapper` Cypress spec, two-instance leader-lease test.
1. **Cypress run (IP-US-E1–E6, Epic I, Epic J, IP-US-T7)** — start the server with
   `IDIOTPROOF_FAKE_LLM=1`, run `npm run cypress:run`, and graduate the stories the specs prove;
   add specs for `/learn`, `/research` and `/gapper`.
2. **Epic R tests** — NUnit coverage for replay, scan, export and the strategy families.
3. **IP-US-U10** — fill-and-close half of the real paper options round-trip, in market hours.
4. **Epic G (IP-US-G1…G4)** — Strategy ghost overlay + branching visualization: chart
   integration, simulator evaluation timeline, branch-fork rendering, scrub/playback.
5. **IP-US-H1** — Roslyn-based IdiotScript parser: exact line/col diagnostics replacing
   the regex parser.
6. **Epic S** — adaptive auto-strategy generation.


## Epic W — Command line & Hub — drive every IdiotProof service from the CLI and MCP through one Hub

## Tickets
> Permanent log of every change and fix — this repo's ticket system. Rows are append-only: never
> delete or reword one; only move its Status (open → closed / wont-do) and fill in Ref. Status is
> a word, not a glyph, so tickets don't count toward the story index. Keep this section last.

| ID | Date | Type | Status | Summary | Ref |
|---|---|---|---|---|---|
| IP-T1 | 2026-10-12 | docs | closed | Start the permanent ticket log in USER_STORIES.md | — |
| IP-T2 | 2026-10-12 | chore | closed | /commit now logs every commit as a ticket in this table | `.claude/skills/commit/SKILL.md` |
| IP-T3 | 2026-10-13 | chore | closed | Standard deploy convention: root deploy.ps1 (change-gated, -Force/-Launch) writes C:\Apps\IdiotProof\deploy.bat; removed deploy-idiotproof.bat, publish-all.bat, launch.bat and Start* leftovers | deploy.ps1 |
| IP-T4 | 2026-10-14 | chore | open | NUnit tests for untested indicators and candlestick detectors in IdiotProof.Indicators: SMA, BollingerBands, Stochastic, WilliamsR, CCI, OBV, Momentum and CandlestickPatterns have no direct tests (IndicatorTests only covers RSI/EMA/ATR/MACD/VWAP, AdxTests covers ADX). Cover hand-computed values on a small candle series, empty and shorter-than-period input, Stochastic warm-up placeholder 50 and %D excluding warm-up bars, flat-range (zero high-low) fallbacks, and CandlestickPatterns IsBullishEngulfing/IsBearishEngulfing/IsHammer/IsShootingStar/IsDoji true and false cases including zero-range and zero-body bars. These feed strategy conditions, so a wrong value silently changes entries. | — |
| IP-T5 | 2026-10-14 | chore | open | NUnit tests for the NYSE calendar in IdiotProof.Scripting: MarketTime.IsMarketHoliday (all 10 holidays incl. Good Friday via the Easter algorithm, Saturday-to-Friday and Sunday-to-Monday observed shifts, New Year observed on Dec 31 of the prior year), IsEarlyCloseDay (Dec 24 weekday, Black Friday, weekend Dec 24 false), IsInsideSession for Premarket/RTH/AfterHours/Extended boundaries at 04:00, 09:30, 16:00, 20:00 ET and weekends, and TradingSchedule.Classify (3:55 AM start, 8:05 PM end, 1:05 PM end on early-close days, holidays hibernate). MarketTimeTests only covers IsEquityTradingDay weekends; a calendar bug lets the Monitor trade on closed days or hibernate through an open session. | — |
| IP-T6 | 2026-10-14 | chore | open | NUnit tests for research claim similarity in IdiotProof.Blazor: ClaimVectorService.GetSignature (deterministic across instances from seed 1337, short feature array returns all-zero 8 bytes, similar vectors get small Hamming distance), HammingDistance (null or length-mismatch returns int.MaxValue, identical is 0), ScoreAsync JSON parsing via a fake Legion handler (array wrapped in prose, fewer than 20 floats returns null, values clamped to 0..1, no key returns null), ComputeAndSaveAsync not duplicating an existing vector, and ClaimCorrelationService FindSimilarOutcomesAsync / FindSimilarOutcomesBatchAsync on an in-memory AppDbContext (maxHamming cutoff, self excluded, nearest-first examples capped at 3, bullish/bearish/avg outcome aggregation, missing target vector returns Empty). | — |
| IP-T7 | 2026-10-14 | chore | open | NUnit tests for CatalystExtractor in IdiotProof.Blazor (only key routing is tested today): ExtractedCatalyst.ComposeSentence (no mechanism returns summary unchanged, blank ticker uses the affected tickers, ticker upper-cased, null timeline reads already priced in) and ExtractAsync parsing through a fake Legion handler: JSON wrapped in markdown or prose, missing fields fall back to News/Neutral/Low and source_tier 3, has_happened_already only true for JSON true, ticker fallback to the input, article text truncated to 8000 chars, malformed JSON or no API key returns null. This is the entry point for every research claim. | — |
| IP-T8 | 2026-10-14 | chore | open | NUnit tests for AutoGapperScanner.RunScanAsync in IdiotProof.Monitor (no tests exist): refuses to arm and logs auto-gapper-blocked when broker mode is paper but the resolved broker is live, returns early with no Alpaca keys, qualification by AutoGapperMinGapPercent and AutoGapperMinPrice, ranking by gap x (1 + volume ratio) with AutoGapperMaxCount cutoff (below-rank-cutoff), duplicate-active skip, dry-run writes no AutoGapperScan/AutoGapperCandidate rows and arms nothing, and one ticker whose synthesis throws does not abort the others. Also cover the synthesized profile tiers (penny under 5, mid under 20, large) for stop clamping to ATR, trailing stop null for large, giveback narrowing with bigger gaps and notional clamped to 200..3000. Needs a test seam for StrategyScanner.FetchMoversAsync and InternalsVisibleTo for IdiotProof.Monitor.Tests. This arms real strategies unattended. | — |
| IP-T9 | 2026-10-14 | chore | open | NUnit tests for ReplayFeatures.Extract in IdiotProof.Monitor: build ReplayRun.DataJson fixtures and assert ReplayBar rows (OHLCV, vwap, whigh, volx, Min from HH:mm, CondPassed counts only true entries in cnd, CondTotal from conditions length, fire/exit/inSession flags) and ReplayTrade rows (HoldMin, Won when pnlPct > 0, entry vwap/window-high/volx joined from the matching bar, DistVwapPct and DistWinHighPct, zero when vwap or whigh is 0 or the entry bar is missing), plus invalid JSON and missing arrays returning empty lists. This is the single source of the ML feature store and CSV export. | — |
| IP-T10 | 2026-10-14 | chore | open | NUnit tests for TradeDiaryRepository and AuditLogRepository in IdiotProof.Blazor on an in-memory AppDbContext: TradeDiaryRepository.OpenAsync marks an existing Open row for the same strategy as Orphaned, CloseAsync closes only the most recent Open row, overwrites Quantity on a partial fill (soldQty differs), computes ReturnPercent on that quantity and 0 when cost is 0, no-ops when nothing is open, MarkNotFilledAsync, GetForUserAsync ordering and limit; AuditLogRepository GetByCategoryAsync limit clamping 1..1000, GetErrorCountSinceAsync counting only strategy-error and order-rejected after the cutoff, and PruneAsync keeping at least minKeep rows (anchor walk-back when recent rows are fewer than minKeep; note DELETE TOP is SQL Server syntax so the delete step may need SQLite/SQL Server or a seam). The diary is the realized-PnL record. | — |
| IP-T11 | 2026-10-14 | chore | open | NUnit tests for auth guards in IdiotProof.Blazor: LiveModeElevationService (IsElevated false by default, Elevate opens a 5-minute window, RemainingWindow positive then null, Revoke clears it, expired entries are purged on the next Elevate, VerifyPasswordAsync passes PasswordPepperKeyId and LegacyHashScheme to IPasswordHasher.Verify and returns false for unknown user, null hash or a throwing hasher) and EmailDomainBlocklistService (DomainOf for null/blank, no @, trailing @, no dot, leading or trailing dot, spaces, mixed case; IsBlockedAsync rejecting malformed emails and matching seeded domains; SeedAsync idempotent, second run inserts 0). These gate Paper-to-Live promotion and registration. | — |
| IP-T12 | 2026-10-14 | chore | open | NUnit tests for the Alpaca consent gate in OptionsTradingService and AccountSummaryService (IdiotProof.Blazor): ResolveBrokerAsync returns Sandbox for Sandbox mode, falls back to Sandbox with a reason when DefaultBroker is not alpaca or the paper/live key pair is missing, prefers a complete Vault pair over DB keys, and caches the client per mode; GetRiskFreeRateAsync default 0.04 and SetRiskFreeRateAsync round-trip via SettingsRepository; GetRecentBullishClaimsAsync filters by ticker/Bullish/date and orders by significance; AccountSummaryService.GetSummaryAsync returns no account data when not opted into Alpaca, never mixes a Vault key with a DB secret, reports a decrypt error when a key is stored but empty, and degrades to an inline error when the key lookup throws. Prevents showing or trading an account the user never enabled. | — |
| IP-T13 | 2026-10-14 | chore | open | NUnit tests for TradingStateService in IdiotProof.Blazor: AddSignal keeps newest first and caps at 500, TotalSignalsToday counts only today-UTC signals, GetPositionsForUser returns only that user's user:broker entries keyed by broker and does not leak between users with prefix-like ids, GetTodaysPnlForUser sums UnrealizedPnl across brokers, GetSignalsFor case-insensitive symbol filter with and without userId, LivePrices/SignalVotes return snapshots (mutating them does not affect state), OnStateChanged fires on every mutation, and concurrent AddSignal/UpdatePrice calls do not throw. It is the singleton backing the live dashboard. | — |
| IP-T14 | 2026-10-14 | chore | open | NUnit tests for SwitchableMarketDataFeed (IdiotProof.DataFeeds) and JsonFileWorkspaceStore (IdiotProof.Engine): the feed routes to the registered active feed case-insensitively, SetActiveFeed switches routing, and an unregistered active name throws InvalidOperationException listing registered feeds instead of falling back to Mock; the workspace store round-trips Save/Load in a temp WorkspacesPath, sorts by DisplayOrder, skips corrupt JSON files, Delete returns false for missing tabs, the __global__ bucket reads flat files at the root and is excluded from EnumerateUserIds. Neither has tests (WorkspaceManagerTests uses an in-memory store). | — |
| IP-T15 | 2026-10-14 | chore | open | NUnit tests for StrategyHtml in IdiotProof.Scripting: ToHtml, ToMermaid and ToSvg on representative StrategyDefinitions (gapper profile, multi-phase with conditional blocks, empty exits) produce the expected phase cards/nodes, HTML-encode user text such as titles and symbols containing angle brackets, ampersands or quotes, and escape Mermaid-breaking characters in labels. These render user-authored strategy text into pages, so escaping regressions are an injection risk. | — |
| IP-T16 | 2026-10-14 | chore | open | Move Monitor/Blazor/ResearchScanner diagnostics (console-only ILogger, crash.log next to the exe) to MindAttic.Log at the Vault path; keep audit.log and per-day scan files as deliberate records | — |
| IP-T17 | 2026-10-15 | chore | closed | Remove duplicate command copies (/quickload, /quicksave) now centralized at the user level (~/.claude/commands) | M.FLOW-T14 |
| IP-T18 | 2026-10-16 | chore | closed | install.ps1 adds this repo's 8 .prose/commands stubs for the shared global commands, so any LLM client finds them | mindattic-agent-standard/install.ps1 run 2026-10-16 |
| IP-T19 | 2026-10-16 | chore | open | Bug hunt: run an exhaustive /bughunt over the whole repo and fix every real bug found (each fix under its own fix ticket), then hunt again; close this only when a full pass finds nothing | user request 2026-10-16: approved repo sweep |
| IP-T20 | 2026-10-16 | chore | open | Run the full unit test suite and fix every failure (each under its own fix ticket) until it passes; put the passing run in --ref. No test project: close this wont-do saying so | user request 2026-10-16: approved repo sweep |
| IP-T21 | 2026-10-16 | chore | open | Clean up code and docs: remove dead code, stale comments and outdated or duplicated docs, and simplify where behaviour doesn't change (/simplify); tests stay green | user request 2026-10-16: approved repo sweep |
| IP-T22 | 2026-10-16 | docs | open | Bring README.md up to date with the code as it is now (what it is, setup, usage, commands, current state), following codex-standard/README-STYLE.md; README.htm regenerated if the repo has one | user request 2026-10-16: approved repo sweep |
| IP-T23 | 2026-10-16 | feature | open | Log to MindAttic.Log (3.0.0): every app/CLI entry point writes to the standalone MindAttic.Log.db Vault assigns this app (VaultPaths.LoggingDbPath, MindAttic.Vault 6.0.0) through Serilog's WriteToMindAtticLog, tagged with its Component; log startup, user actions that change data, failures and unhandled exceptions (UI thread, AppDomain, unobserved tasks); tests point the log at a temp file. Pattern: MindAttic.Workflow WorkflowLog (M.FLOW-T92), MindAttic.Orchestrator OrchestratorLog | M.FLOW-T92 |
| IP-T24 | 2026-10-16 | docs | closed | Update USER_STORIES.md | — |
| IP-T25 | 2026-10-18 | chore | open | Retire tools/register-research-scan-task.ps1 (job now in MindAttic.Scheduler M.SCHD-T8) | scheduled-task consolidation 2026-10-18 |
| IP-T26 | 2026-10-19 | chore | wont-do | Convert tracked source files to LF line endings via .gitattributes, per HOUSE-LAW-13 (low priority) | — |
| IP-T27 | 2026-10-20 | docs | open | Publish IdiotProof docs to GitHub Pages via the shared Agency DocFX pipeline: run adopt-docs-site.ps1 (README.md as home page, docs/*.md as articles, API reference only where the code builds in CI), enable Pages with build_type=workflow, set the repo homepage URL, retire tools/build-readme.ps1 and README.htm, confirm the docs Actions run is green and https://mindattic.github.io/IdiotProof/ returns 200. Depends on MAT-T8. | — |
| IP-T28 | 2026-10-22 | docs | closed | Update USER_STORIES.md | — |
| IP-T29 | 2026-10-24 | docs | closed | Update USER_STORIES.md | — |
| IP-T30 | 2026-10-30 | feature | open | Create IdiotProof.Core class library (net10.0) in IdiotProof.slnx, referencing Models/Shared/Scripting/Strategies/Brokers/DataFeeds/Engine/Indicators | — |
| IP-T31 | 2026-10-30 | feature | open | Enable GenerateDocumentationFile for every library via Directory.Build.props (CS1591 suppressed) so XML doc comments ship for the knowledge index | — |
| IP-T32 | 2026-10-30 | feature | open | Move AppDbContext and every Data/* entity from IdiotProof.Blazor to IdiotProof.Core (namespaces unchanged in this move) | — |
| IP-T33 | 2026-10-30 | feature | open | Move EF Migrations + AppDbContextFactory to IdiotProof.Core with MigrationsAssembly set; migration IDs and snapshot unchanged | — |
| IP-T34 | 2026-10-30 | feature | open | Introduce IOwnerContext (current owner GUID) — web resolves from the NameIdentifier claim, Hub/CLI from --user, persisted default, or the single AuthUsers row | — |
| IP-T35 | 2026-10-30 | feature | open | Introduce IClaudeKeyContext replacing SignedInClaudeKey's AuthenticationStateProvider dependency for StrategyScriptGenerator, GapperInterpreter, ResearchService | — |
| IP-T36 | 2026-10-30 | feature | open | Introduce IStaticCatalogSource replacing IWebHostEnvironment in GapperProfileService; gapper-profiles.json and sp-index-events.json resolve from a shared content path | — |
| IP-T37 | 2026-10-30 | feature | open | Move strategy services to Core: StrategyRepository, ConditionProgressRepository, StrategyScriptGenerator, DslGlossary, SqlWorkspaceStore | — |
| IP-T38 | 2026-10-30 | feature | open | Move broker/account services to Core: UserBrokerResolver, AlpacaVaultKeys, AccountSummaryService, LiveModeElevationService, TradingStateService | — |
| IP-T39 | 2026-10-30 | feature | open | Move risk + LLM services to Core: RiskGuardianService, LlmVotingService (+AddSignalVotingPanel), UserClaudeKeyResolver, FakeLlmHandler | — |
| IP-T40 | 2026-10-30 | feature | open | Move research services to Core: ResearchService, EdgarService, Form4Parser, CorporateActionDetector, RegulatoryScanner, IndexEventScanner, UsSpendsService, AlpacaNewsService, CatalystExtractor, ClaimVectorService, ClaimCorrelationService, SignificanceScorer, OutcomeBackfillService, TickerUniverseService, AsyncThrottle | — |
| IP-T41 | 2026-10-30 | feature | open | Move remaining non-UI services to Core: OptionsTradingService, TradeDiaryRepository, AuditLogRepository, SettingsRepository, UserPreferencesService, UserKeyService, EmailDomainBlocklistService, LiveBarRepository, GapperProfileService, GapperInterpreter, EasternTime, DevCredentials | — |
| IP-T42 | 2026-10-30 | feature | open | AddIdiotProofCore(config): one DI graph — connection-string chain, Vault buckets, AppSettings overlay, shared DataProtection key ring, MindAttic.Authentication, every Core service | — |
| IP-T43 | 2026-10-30 | feature | open | Lift BrokerRouter + IMarketDataFeed construction (IDIOTPROOF_BROKER/FEED/ALPACA_FEED/STREAMING knobs) from Monitor/Program.cs into AddIdiotProofTrading() | — |
| IP-T44 | 2026-10-30 | feature | open | Move MonitorWorker, MonitorLeaderLease, DirectionalOrders, scanners (AutoGapper, PremarketFade, BeBexDecay), StrategyReplay*, ReplayFeatures/Templates, StrategyDataset, StrategyScanner, EmailSmsAlertSender into Core (Monitoring namespace) so any host can run them | — |
| IP-T45 | 2026-10-30 | feature | open | Re-point IdiotProof.Blazor Program.cs to AddIdiotProofCore; pushers, ToastService, LogAlertService stay in Blazor | — |
| IP-T46 | 2026-10-30 | feature | open | Re-point IdiotProof.Monitor to AddIdiotProofCore/AddIdiotProofTrading and drop its IdiotProof.Blazor project reference | — |
| IP-T47 | 2026-10-30 | feature | open | Re-point IdiotProof.ResearchScanner to AddIdiotProofCore and drop its IdiotProof.Blazor project reference | — |
| IP-T48 | 2026-10-30 | feature | open | Re-point Blazor.Tests and Monitor.Tests at IdiotProof.Core; add IdiotProof.Core.Tests project (NUnit) | — |
| IP-T49 | 2026-10-30 | feature | open | Rename moved namespaces IdiotProof.Blazor.Data/Services -> IdiotProof.Core.Data/Services across the solution | — |
| IP-T50 | 2026-10-30 | feature | open | Delete stale IdiotProof.sln (slnx is canonical) and fix any script that names it | — |
| IP-T51 | 2026-10-30 | feature | open | Create IdiotProof.Operations library: [Operation("noun verb")], [Option], [Argument], IOperation<TArgs> contract, risk class (Read/Write/Money/Destructive) | — |
| IP-T52 | 2026-10-30 | feature | open | OperationContext: owner, IServiceProvider scope, caller (cli/mcp/blazor), output mode, cancellation, ET clock | — |
| IP-T53 | 2026-10-30 | feature | open | OperationResult: status, data payload, human render, exit code, ConfirmationRequired variant | — |
| IP-T54 | 2026-10-30 | feature | open | OperationCatalog: reflect all operations, validate unique names + descriptions + arg docs, emit catalog JSON schema per op | — |
| IP-T55 | 2026-10-30 | feature | open | Args binder: JSON -> typed args record with validation errors mapped to exit 2 | — |
| IP-T56 | 2026-10-30 | feature | open | Text render kit: tables, key/value blocks, money/percent formatting, ET-only time display, color only on a TTY | — |
| IP-T57 | 2026-10-30 | feature | open | Create IdiotProof.Cli (idiotproof.exe): argv `noun verb --flags` parser bound to the catalog | — |
| IP-T58 | 2026-10-30 | feature | open | Global flags --json --user --mode --quiet --yes(no-op for Live) and stdin '-' for any text argument | — |
| IP-T59 | 2026-10-30 | feature | open | Generated help: `idiotproof help`, `idiotproof <noun> --help`, `idiotproof <noun> <verb> --help` from the catalog | — |
| IP-T60 | 2026-10-30 | feature | open | Offline catalog: CLI references IdiotProof.Operations so help, agent bootstrap and docs export work with the Hub down | — |
| IP-T61 | 2026-10-30 | feature | open | Exit-code contract 0 ok / 1 fail / 2 usage / 3 blocked by a gate / 4 confirmation required — documented and tested | — |
| IP-T62 | 2026-10-30 | feature | open | idiotproof.cmd shim at repo root (dotnet run fallback, published exe preferred) | — |
| IP-T63 | 2026-10-30 | feature | open | Owner resolution in CLI: --user, `users use` persisted default in %APPDATA%\MindAttic\IdiotProof\cli.json, single-user fallback | — |
| IP-T64 | 2026-10-30 | feature | open | `agent bootstrap [--json]`: Hub health, owner, catalog paths, protocol doc, laws summary — runs without the Hub | — |
| IP-T65 | 2026-10-30 | feature | open | Catalog completeness test: every operation has description, arg docs, JSON render; no name collisions | — |
| IP-T66 | 2026-10-30 | feature | open | Create IdiotProof.Hub (ASP.NET Core minimal API, Kestrel bound to 127.0.0.1:5910) using AddIdiotProofCore | — |
| IP-T67 | 2026-10-30 | feature | open | /api/health and /api/version endpoints (build, commit, uptime, db, feed, broker) | — |
| IP-T68 | 2026-10-30 | feature | open | Hub API key: generated once, stored in Vault Tokens bucket 'idiotproof-hub'; HubKeyFilter guards every mutating endpoint | — |
| IP-T69 | 2026-10-30 | feature | open | CLI HubClient: fail-closed health gate, X-IdiotProof-Key header, slow-op notice after 8s, 2h timeout, structured error output | — |
| IP-T70 | 2026-10-30 | feature | open | /api/op dispatch endpoint (operation name + JSON args) and /api/catalog | — |
| IP-T71 | 2026-10-30 | feature | open | CommandLedger entity + migration: op, redacted args, owner, caller, exit code, duration, UTC timestamp | — |
| IP-T72 | 2026-10-30 | feature | open | SecretRedactor for ledger args (keys, secrets, passwords, tokens) | — |
| IP-T73 | 2026-10-30 | feature | open | Ledger write on every /api/op call | — |
| IP-T74 | 2026-10-30 | feature | open | Host MonitorWorker in the Hub as an IHostedService with the SQL leader lease and SupervisedLoop unchanged | — |
| IP-T75 | 2026-10-30 | feature | open | Host AutoGapperScanner, PremarketFadeScanner and BeBexDecayScanner in the Hub with their existing enable flags | — |
| IP-T76 | 2026-10-30 | feature | open | Windows Service hosting (UseWindowsService) that runs as the USER (LocalDB + DataProtection are user-scoped) | — |
| IP-T77 | 2026-10-30 | feature | open | Single-instance guard + clear error when port 5910 is taken | — |
| IP-T78 | 2026-10-30 | feature | open | Hub logging to MindAttic.Log file + /api/logs tail endpoint | — |
| IP-T79 | 2026-10-30 | feature | open | Graceful shutdown: drain in-flight ops, stop the Monitor loop cleanly, release the lease | — |
| IP-T80 | 2026-10-30 | feature | open | Monitor pause/resume flag (SettingsKv) honored at the top of each tick | — |
| IP-T81 | 2026-10-30 | feature | open | IdiotProof.Monitor exe becomes a shim that runs the Hub host (backward-compatible start.bat) | — |
| IP-T82 | 2026-10-30 | feature | open | SessionStart hook (.claude/settings.json) that starts the Hub when it is not running | — |
| IP-T83 | 2026-10-30 | feature | open | `hub status\|start\|stop\|logs\|version` operations + CLI-side start/stop of the local process | — |
| IP-T84 | 2026-10-30 | feature | open | `hub install-service\|uninstall-service` operations | — |
| IP-T85 | 2026-10-30 | feature | open | OrderIntent model: asset (equity/option), side, qty/notional, type, limit, TIF, extended hours, BrokerMode, origin (monitor/manual/options/flatten), strategyId | — |
| IP-T86 | 2026-10-30 | feature | open | TransactionGateway.SubmitAsync skeleton with ordered, individually-tested gate steps and a GateVerdict trail | — |
| IP-T87 | 2026-10-30 | feature | open | Gate step: owner + BrokerMode resolution via UserBrokerResolver (IP-LAW-3, Sandbox default, never another user's keys) | — |
| IP-T88 | 2026-10-30 | feature | open | Gate step: Mock-feed guard refuses non-Sandbox entries (IP-LAW-9) | — |
| IP-T89 | 2026-10-30 | feature | open | Gate step: RiskGuardian (stop required on entries, size/limits, daily breaker; exits honor kill-switch) (IP-LAW-2) | — |
| IP-T90 | 2026-10-30 | feature | open | Gate step: LLM voter panel for automated entries only (IP-LAW-1) | — |
| IP-T91 | 2026-10-30 | feature | open | ConfirmationStore: 6-char single-use code, 120s TTL, bound to SHA-256 of the exact intent, persisted PendingConfirmation table | — |
| IP-T92 | 2026-10-30 | feature | open | Live preview: cost, worst-case loss, account, buying power after, gate verdicts — returned with exit 4 | — |
| IP-T93 | 2026-10-30 | feature | open | Every gateway outcome writes TradeDiary (fills), AuditLog (all) and CommandLedger linkage | — |
| IP-T94 | 2026-10-30 | feature | open | Reroute MonitorWorker entries and exits through the gateway with behavior-parity tests | — |
| IP-T95 | 2026-10-30 | feature | open | Reroute DirectionalOrders flatten / test-order through the gateway | — |
| IP-T96 | 2026-10-30 | feature | open | Reroute OptionsTradingService orders through the gateway (Blazor keeps password elevation; CLI/MCP use confirm codes) | — |
| IP-T97 | 2026-10-30 | feature | open | Activating a strategy in Live mode (or switching it to Live) requires a confirm code | — |
| IP-T98 | 2026-10-30 | feature | open | Gateway law tests: one test per law/step, including expired/edited-intent confirm refusal | — |
| IP-T99 | 2026-10-30 | feature | open | BIBLE: add laws 'Every order goes through TransactionGateway' and 'Live from CLI/MCP needs a confirm code' | — |
| IP-T100 | 2026-10-30 | feature | open | `account summary [--mode]` (equity, cash, buying power, day P&L) for sandbox/paper/live | — |
| IP-T101 | 2026-10-30 | feature | open | `account buying-power` and `account pdt` (day-trade count, PDT flag) | — |
| IP-T102 | 2026-10-30 | feature | open | `account activities [--since]` (fills, dividends, fees) | — |
| IP-T103 | 2026-10-30 | feature | open | `account clock` and `account calendar` (market session in ET) | — |
| IP-T104 | 2026-10-30 | feature | open | `positions list [--mode]` with unrealized P&L | — |
| IP-T105 | 2026-10-30 | feature | open | `positions show <symbol>` incl. owning strategy and diary row | — |
| IP-T106 | 2026-10-30 | feature | open | `positions reconcile` (broker vs strategy bookkeeping, read-only report) | — |
| IP-T107 | 2026-10-30 | feature | open | `orders list [--status]` and `orders show <id>` | — |
| IP-T108 | 2026-10-30 | feature | open | `orders cancel <id>` and `orders cancel-all [--symbol]` through the gateway | — |
| IP-T109 | 2026-10-30 | feature | open | `orders history [--days]` | — |
| IP-T110 | 2026-10-30 | feature | open | `trade buy\|sell <symbol> <qty> [--limit] [--stop] [--target] [--mode]` through the gateway | — |
| IP-T111 | 2026-10-30 | feature | open | `trade short\|cover <symbol> <qty>` through the gateway | — |
| IP-T112 | 2026-10-30 | feature | open | `trade flatten --symbol\|--strategy\|--all [--mode]` | — |
| IP-T113 | 2026-10-30 | feature | open | `trade preview …` (dry run: every gate verdict, no order) | — |
| IP-T114 | 2026-10-30 | feature | open | `trade confirm <code>` executes a pending Live intent | — |
| IP-T115 | 2026-10-30 | feature | open | `trade pending` lists unexpired Live confirmations | — |
| IP-T116 | 2026-10-30 | feature | open | `trade notional <symbol> <$>` (fractional/notional buy) through the gateway | — |
| IP-T117 | 2026-10-30 | feature | open | Create IdiotProof.Mcp (stdio, ModelContextProtocol SDK, logs to file — stdout is the wire) | — |
| IP-T118 | 2026-10-30 | feature | open | Generate one MCP tool per operation from the catalog (snake_case noun_verb, JSON schema from the args record) | — |
| IP-T119 | 2026-10-30 | feature | open | HubInvoker forwarding with structured errors: hub_unreachable, hub_timeout, hub_http_error, usage_error | — |
| IP-T120 | 2026-10-30 | feature | open | Tool annotations from operation risk class (readOnlyHint / destructiveHint / idempotentHint) | — |
| IP-T121 | 2026-10-30 | feature | open | ConfirmationRequired tool result tells the assistant to show the preview and ask the user before calling trade_confirm | — |
| IP-T122 | 2026-10-30 | feature | open | MCP resources: BIBLE, user stories, operation catalog, protocol doc | — |
| IP-T123 | 2026-10-30 | feature | open | tools/register-mcp.ps1 (`claude mcp add idiotproof …` against the Release build) + README | — |
| IP-T124 | 2026-10-30 | feature | open | .claude/settings.json allow-list for read-only idiotproof tools; trade_confirm and destructive tools stay prompt-only | — |
| IP-T125 | 2026-10-30 | feature | open | SessionStart hook keeps IdiotProof.Mcp Release build current so the schema is never stale | — |
| IP-T126 | 2026-10-30 | feature | open | MCP schema test: every operation round-trips catalog -> tool schema -> binder | — |
| IP-T127 | 2026-10-30 | feature | open | StrategyStamp short id IP-XXXX-XXXX (base32 of SHA-256 of the canonical JSON) | — |
| IP-T128 | 2026-10-30 | feature | open | Stamp header block: symbol, direction, session, BrokerMode, qty/notional, id | — |
| IP-T129 | 2026-10-30 | feature | open | Stamp phase flow: Setup -> Filters -> Entry -> Order -> Risk -> Exit boxes with every condition line | — |
| IP-T130 | 2026-10-30 | feature | open | Stamp branch forks for If/ElseIf/Else conditional blocks | — |
| IP-T131 | 2026-10-30 | feature | open | Stamp exit ladder: stop, TP targets, trailing, peak giveback@arm, sell-by | — |
| IP-T132 | 2026-10-30 | feature | open | Stamp fingerprint: 17x9 drunken-bishop randomart over the canon hash | — |
| IP-T133 | 2026-10-30 | feature | open | Stamp armored payload: BEGIN/END IDIOTPROOF STRATEGY, deflate + base64url + CRC32, fail-closed decode | — |
| IP-T134 | 2026-10-30 | feature | open | Terminal QR of the payload (QRCoder, Unicode half-blocks) behind --qr | — |
| IP-T135 | 2026-10-30 | feature | open | Stamp box-drawing variant behind --unicode (ASCII is the default) | — |
| IP-T136 | 2026-10-30 | feature | open | Golden-file tests for every sample strategy + stamp -> import round trip yields an identical canon hash | — |
| IP-T137 | 2026-10-30 | feature | open | `strategy list [--active] [--mode] [--symbol]` with live ConditionProgress badges | — |
| IP-T138 | 2026-10-30 | feature | open | MonitorCli commands forward to the equivalent operations (status, set-keys, flatten, replay, scan, …) | — |
| IP-T139 | 2026-10-30 | feature | open | `strategy show <id\|symbol>` (fields, canon summary, position, progress) | — |
| IP-T140 | 2026-10-30 | feature | open | `strategy stamp <id> [--qr] [--unicode]` | — |
| IP-T141 | 2026-10-30 | feature | open | `strategy export <id> --format json\|script\|stamp` | — |
| IP-T142 | 2026-10-30 | feature | open | `strategy import --stamp\|--json\|--script <file\|->` through StrategyRepository (fail-closed canon) | — |
| IP-T143 | 2026-10-30 | feature | open | `strategy create --script\|--json` with validation and Sandbox default | — |
| IP-T144 | 2026-10-30 | feature | open | `strategy from-text "<plain English>" [--ticker] [--mode]` via StrategyScriptGenerator + Legion; shows stamp before saving | — |
| IP-T145 | 2026-10-30 | feature | open | `strategy set <id> --field value` field-level edits (stop, target, qty, notional, times) re-canonicalized | — |
| IP-T146 | 2026-10-30 | feature | open | `strategy validate <id\|--script>` (parse, canon, phase rules, risk preview) | — |
| IP-T147 | 2026-10-30 | feature | open | `strategy activate\|deactivate <id>` respecting PositionOpen and gapper caps (IP-LAW-11) | — |
| IP-T148 | 2026-10-30 | feature | open | `strategy mode <id> sandbox\|paper\|live` (live requires confirm code) | — |
| IP-T149 | 2026-10-30 | feature | open | `strategy delete <id>` (soft-disable per house law, refuses open positions) | — |
| IP-T150 | 2026-10-30 | feature | open | `strategy progress <id> [--watch]` live condition N/M + first failing verb | — |
| IP-T151 | 2026-10-30 | feature | open | `strategy quarantine` lists quarantined strategies with reasons | — |
| IP-T152 | 2026-10-30 | feature | open | `strategy resync-canon` (port of MonitorCli resync-canon) | — |
| IP-T153 | 2026-10-30 | feature | open | `strategy clone <id> [--symbol]` | — |
| IP-T154 | 2026-10-30 | feature | open | `strategy diff <a> <b>` (canon-level diff) | — |
| IP-T155 | 2026-10-30 | feature | open | `strategy history <id>` (audit + diary trail) | — |
| IP-T156 | 2026-10-30 | feature | open | `strategy from-watchlist <file>` (port of create-strategies) | — |
| IP-T157 | 2026-10-30 | feature | open | `strategy rename <id> <name>` | — |
| IP-T158 | 2026-10-30 | feature | open | `strategy samples` lists and shows built-in sample strategies | — |
| IP-T159 | 2026-10-30 | feature | open | `strategy explain <id>` plain-English narrative generated deterministically from the canon | — |
| IP-T160 | 2026-10-30 | feature | open | `strategy fires <id>` recent gate verdicts (why it did/didn't fire) | — |
| IP-T161 | 2026-10-30 | feature | open | `strategy bulk activate\|deactivate --all\|--symbol` (quick actions) | — |
| IP-T162 | 2026-10-30 | feature | open | `gapper profiles` lists the static profile catalog | — |
| IP-T163 | 2026-10-30 | feature | open | `gapper profile show <name>` | — |
| IP-T164 | 2026-10-30 | feature | open | `gapper tune <symbol> --profile <name> [--gap --stop --giveback --sell-by …]` generates the gapper strategy via GapperScriptFactory | — |
| IP-T165 | 2026-10-30 | feature | open | `gapper arm\|disarm <symbol\|id>` honoring one-per-symbol and the 3-gapper cap | — |
| IP-T166 | 2026-10-30 | feature | open | `gapper list` queued gappers with live state | — |
| IP-T167 | 2026-10-30 | feature | open | `gapper from-transcript <file\|->` -> candidate cards (re-validated, not queued until `gapper arm`) | — |
| IP-T168 | 2026-10-30 | feature | open | `gapper backtest-day <symbol> <date> [--profile]` with MFE/MAE, giveback grid, hindsight suggestion | — |
| IP-T169 | 2026-10-30 | feature | open | `gapper auto status` (scan metrics from AutoGapperScan/Candidate) | — |
| IP-T170 | 2026-10-30 | feature | open | `gapper auto enable\|disable` (paper-only, owner-scoped) | — |
| IP-T171 | 2026-10-30 | feature | open | `gapper auto candidates [--date]` | — |
| IP-T172 | 2026-10-30 | feature | open | `gapper premarket-fade [run\|status]` (port of MonitorCli premarket-fade) | — |
| IP-T173 | 2026-10-30 | feature | open | `gapper be-bex status` (BeBexDecayScanner arm/disarm state) | — |
| IP-T174 | 2026-10-30 | feature | open | `gapper apply-tuned <backtest-id>` applies a backtest's tuned profile on explicit command | — |
