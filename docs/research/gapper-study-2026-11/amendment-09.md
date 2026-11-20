# Gapper study, amendment 09: runners that start after 09:30 (the whole day, not only to 09:45)

Registered 2026-11-19 (IP-T604), before any train or validation row was read for this question and before the
code that runs it was written. No earlier file is edited, no frozen hypothesis, threshold or verdict changes,
and the holdout (2025-11-03 .. 2026-09-09 after amendment 05) stays locked: this amendment never reads it. Like
amendments 05-08 it has no `.json` twin: its hypotheses are run by the explore stage (train, then validation
once), not by the frozen test stage. Research and labels only: nothing here arms, sizes or exits a trade.

## Why

Every label of the study ends by 09:45 ET (`reach20By0945`, the detector H11/H11p, R4) and the daily journal
(IP-T592) read the premarket. The first journal session (2026-11-19, opened window, a case study) found that 10
of the 20 top runners ignited after 09:30: MPU ran +75% from 12:15 to 15:54, LVWR squeezed at 10:15, NCPL peaked
at 10:49, BAER at 16:02, ELOG made its high after the close. Our gapper templates are premarket-only and sell by
09:28, so half of that session's runners were outside anything the study measures. One session is an anecdote;
this amendment asks the question on train, where it can be tested.

## Question

1. **How much of the gapper population starts after 09:30?** Descriptive, train only.
2. **Is a late move detectable at its start?** At the first abnormal-volume up minute of the regular session,
   do the facts known at that minute rank the days that go on to +20% better than the gain alone does?
3. **Does a fresh catalyst matter for late runners?** Among those starts, is a stored news item or filing
   published since the prior close (and by the start) followed by a +20% move more often than none?

## Honest prior

Probably small. Late moves on a name that was flat in the premarket are the hardest to explain: the 2026-11-19
late runners mostly had no fresh catalyst (MPU, LVWR, BAER) or one 1-2 days old. Abnormal volume at the start is
already in the benchmark (the move has begun), so H1 may add nothing beyond "it is already up". The expected
result is a pass on neither, with question 1 the useful output. That is reported as the headline if it happens.

## Definitions (frozen with this file)

All times ET, minute bars 04:00-20:00 as stored by T2 (protocol), prices over the split-adjusted prior close
`prevClose`. A bar at minute m is the bar starting at m; "by t" means bars with `EndUtc <= t`.

- **Session buckets** of a minute: premarket 04:00-09:29, open 09:30-09:44, morning 09:45-11:59, midday
  12:00-13:59, afternoon 14:00-15:59, post-market 16:00-19:59.
- **first20** = the first minute 04:00-19:59 whose high is >= +20% over `prevClose`; its bucket is the day's
  **start session**. `first10` likewise at +10%.
- **reach20Pre0930** = a high >= +20% in a bar ending by 09:30.
- **reach20Rth** = a high >= +20% in a bar starting 09:30-15:59.
- **lateRunner** = not `reach20Pre0930` and `reach20Rth`. (`reach20Post`, a first +20% at 16:00-19:59, is
  reported, not tested.)
- **Move start m0** = the first minute 09:30-15:59 such that (a) its volume is abnormal by the protocol's rule,
  `v_m >= 5 x ADV20 shares x the time-of-day profile's share for the minute ending at m+1, ADV$ decile`
  (`data/tod-profile.json`, fit on train); (b) its close is above `prevClose`; and (c) no high >= +20% printed in
  any bar before m (so a day with `reach20Pre0930` has no m0). The decision time is the end of m0.
- **Label y** = a high >= +20% over `prevClose` in a bar starting after m0 and ending by 16:00 (the bar at m0 is
  never counted, as the protocol never trades the bar at t).
- **Fresh catalyst at m0** = a stored news item (Alpaca / Benzinga) or EDGAR filing (8-K, 8-K/A, 6-K, S-1, S-3,
  424B3/4/5, SC 13D) for the symbol, published or accepted after D-1 16:00 and at or before the end of m0, whose
  amendment 08 type is not "no catalyst" (coverage). A filing with no acceptance time counts at 23:59:59 ET of its
  filing date (the snapshot stage's rule), so it is never early.

## Features at m0 (nothing after the end of m0 is read)

1. `gainAtStart` = close(m0) / `prevClose` - 1.
2. `volRatioTod` at m0 (protocol definition: cumulative volume 04:00..m0 over ADV20 shares x the profile's
   cumulative share by then).
3. `minutesSinceOpen` = m0 - 09:30.
4. `pmHighGain` = the premarket high (04:00-09:29) over `prevClose` - 1.
5. `highSoFarGain` = the highest high 04:00..m0 over `prevClose` - 1.
6. `freshCatalyst` (0/1) and `catalystAgeMin` (m0 end minus the latest fresh item's time; missing with none),
   and its amendment 08 group (real company event / story or PR / offering / reverse split / none).
7. `haltBeforeStart` = the protocol's bar-gap halt proxy firing between 09:30 and m0 (the Nasdaq halt files the
   case study reads are not in the train store).
8. `log ADV20$` and `log prevClose`.
9. `daysSinceReverseSplit <= 60` and a 424B / S-1 in the prior 90 days (the snapshot stage's inputs and rules).
10. `priorTopMoverDays` = sessions among the previous 20 with a daily high >= +20% over that day's prior close.
11. `sharesOutstanding` (log), the float proxy, point-in-time (latest XBRL value filed before D), when the store
    has it.

**Missing data, decided now.** A feature missing on a row is imputed with the train mean plus a 0/1 missing
indicator. A feature missing on more than 50% of train rows (expected for item 11) is dropped before fitting,
and the drop and its missing share are reported. Days whose stored bars do not reach 16:00 are excluded and
counted.

## Population and weights

Train universe days (2021-11-01 .. 2024-10-31) with an m0, under amendment 01's weighting: candidate days are a
census (weight 1; every day with a +20% high is a candidate, so every late runner is in), non-candidate days
enter through the seeded 10% sample at weight 10. Split-window and excluded universe days stay excluded.
Validation (2024-11-03 .. 2025-10-31) is read once per hypothesis, only for a hypothesis that passed on train.

## Hypotheses

| Id | Claim | Unit | Metric | Pass on train | Then on validation |
|---|---|---|---|---|---|
| A09-H1 | The features above rank late moves better than the gain alone | (s, D) with an m0 | weighted AUC of a weighted logistic on features 1-11 minus the weighted AUC of a weighted logistic on `gainAtStart` alone, for y | on train, 5 month-blocked folds (fit on 4, score the 5th, pooled): lower bound of the day-clustered bootstrap 95% interval of the AUC difference > 0 | fit on all train, score validation once: same bound > 0 |
| A09-H2 | A fresh catalyst at m0 raises the rate of y | (s, D) with an m0 | weighted lift = P(y \| fresh catalyst) / P(y \| none) | lower bound of the day-clustered bootstrap 95% interval of the lift > 1 | same, on validation |

Bootstrap: 10,000 day-clustered draws, seed 1337 (the study's settings). Logistic: `LogisticRegression.FitWeighted`
defaults, standardized features fit on the training folds only. The two hypotheses form one family; on each split
both are corrected together by Benjamini-Hochberg at q < 0.05 (the day-clustered bootstrap p of "difference <= 0"
and "lift <= 1"). A hypothesis must pass on train to be run on validation, and must pass on validation with the
same sign to count. Each run of each hypothesis is one ledger trial; each descriptive table below is one more.

## Reported, not tested (train only)

- **D1, when runners start.** All train days with a +20% high 04:00-19:59 (the census): count and share by start
  session, by high-of-day session, and the cross of the two; per start session the median run (session high over
  `prevClose`), fade to the 16:00 close and to the last print by 20:00, and minutes from first20 to the high.
  This answers question 1 and replaces the one-session "10 of 20" with a train number.
- **D2, catalyst timing for late runners.** Signed minutes from the latest fresh catalyst to m0 and to first20
  (negative = the move came first), and the share with no fresh catalyst at all.
- **D3, what followed m0.** For days with an m0: the forward high and the 16:00 close from the next bar's open,
  split by y. Gross, no costs, no exit rule: a description of the path, not a trade test.

## What a pass does and does not allow

A pass on train and validation says a late move's start carries information about whether it reaches +20%. It
does not make anything tradable and changes no strategy, template, radar or sell-by. A regular-hours template
(the journal's "regular-hours continuation" candidate) would need its own amendment: entry and exit rules, the
frozen cost model (prior-days Corwin-Schultz spread x 9.39 per leg), the protocol's worth-trading bar (mean net
>= +1.0% per trade on train and validation, day-clustered 2.5% bound > 0, >= 150 validation trades on >= 40
days), then the 20-session paper shadow. Long only.

## The opened window

`research gappers casestudy` computes the same first10 / first20, start session, `lateRunner`, m0 (with the same
profile and its ADV$ decile from the day's cross-section), y, the post-market high and the last print for the
case-study days (IP-T604), and the daily journal shows them. Those days are the opened window (amendment 05):
case studies, never a test of this amendment.

## Implementation

The train and validation runs need a new explore command (`research gappers explore` through the split guard,
reading the stored T2 bars and the snapshot stage's catalyst and structure inputs). It is not written by
IP-T604; it is a follow-up ticket and must implement exactly the definitions above. Any definition that turns
out not to be computable from the store is corrected in a dated note in this file before the first run, as
amendment 08 did, never after a number is seen.

**Implementation note, 2026-11-19 (IP-T605), written with the code and before its first run; no train or
validation number had been computed.** The command is `research gappers explore --command late-runners`. No
definition, threshold or hypothesis above changes; these are the readings the code takes where the text leaves a
choice, and one computability correction:

1. *Early-close sessions are left out and counted* (correction). On the 13:00 sessions (July 3, Black Friday,
   Christmas Eve) the bars 13:00-15:59 are post-market, so "09:30-15:59" would read after-hours prints as regular
   hours. About three train sessions a year.
2. *Bars reach 16:00* = the day has a stored bar starting at or after 15:59 (so one ends at or after 16:00).
3. *Amendment 08 group* of feature 6 = amendment 08's day rule over the fresh items at m0 (the highest-priority
   type among them, coverage ignored), mapped as the case study groups its catalysts: real company event =
   outside investment / PIPE, acquisition target, acquirer, merger, earnings beat / miss / report, guidance,
   FDA / clinical, uplisting, listing compliance, buyback; story or PR = contract / partnership, analyst upgrade,
   theme pivot, other; offering = offering / dilution; reverse split = reverse split / ADS ratio; none = no fresh
   item. It enters the model as four 0/1 columns (none = all zero). The LOI flag is not used.
4. *Month-blocked folds* = the 36 train months in order cut into five contiguous blocks (8, 7, 7, 7, 7 months).
   No embargo (y is same-day).
5. *Bootstrap* resamples whole days of the pooled out-of-fold rows with the fitted scores held fixed (the models
   are not refit per draw); each draw's AUCs and lift are weighted (amendment 01 weights x the day's draw count).
   One-sided p = (1 + draws at or below the null) / (draws + 1). A pass needs the bound and BH q < 0.05.
6. *Missing share* for the 50% drop rule is the unweighted share of the m0 rows; an imputed value is the fitting
   rows' weighted mean (`FitWeighted` treats a missing value as the training mean).
7. *haltBeforeStart* counts a proxy gap only when its empty minutes lie between 09:30 and the start of m0;
   activity starts at the first abnormal-volume minute from 04:00, as in the labeler.
8. *sharesOutstanding* comes from the SEC companyfacts file `explore float-import` writes (latest value filed
   before D, through the study's CIK map); a symbol without one is missing.
9. *D1* counts candidate days (the census) whose minute bars print +20% 04:00-19:59; the run is the day's high
   04:00-19:59 over the prior close. *D2*: signed minutes = the time point (start of the first +20% minute, end of
   m0) minus the latest fresh item at or before it, or, with none before it, minus the first fresh item after it
   by 20:00 (negative). *D3* is weighted.
10. *Validation* is not read by this command: the explore stage refuses every split but train. A hypothesis that
    passes on train gets its single validation run in a follow-up ticket, under the same definitions.
