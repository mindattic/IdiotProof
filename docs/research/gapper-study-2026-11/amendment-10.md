# Gapper study, amendment 10: trading the late-runner move start (regular hours)

Registered 2026-11-20 02:27 ET (IP-T617), before any A10 number existed and before the code that runs it was
written: no trade had been simulated, no cost computed, and no count of how many move starts clear the threshold
had been looked at, on any split. The machine-readable form is `amendment-10.json`; where the two differ, the
JSON governs. No earlier file is edited, no frozen hypothesis, threshold or verdict changes, and the holdout stays
locked. Research only: nothing here arms, sizes, places or exits an order.

## Why

Amendment 09 found, on train and then once on validation (IP-T605, IP-T616), that everything knowable at a late
runner's move start m0 ranks the days that go on to +20% better than the gain alone (H27, validation AUC
difference +0.0152 [+0.0056, +0.0235]) and that a fresh catalyst at m0 triples the odds (H28, lift 3.22x). Its own
"What a pass does and does not allow" section says that is evidence the pattern is real, not a trading rule: no
entry, exit, cost model or sizing was specified. This amendment specifies one, completely, before any number.

## Question

Buy the move starts the H27 model rates more likely than not to reach +20%, sell on the study's frozen exit
ladder: does that net money after the frozen cost model in regular hours?

## Honest prior

Probably not. Reaching +20% is a label, not a profit. The model's most confident move starts are likely names
already at +15% to +19% at m0, whose remaining room to +20% is a few percent, about what a small cap's round trip
costs under the frozen model. Every long trading rule the study has tested so far lost money after costs (H12,
H12p, H18, explore entries and exit routes). A fail is the expected result and is reported as the headline.

## Population and score (frozen with this file)

- **Rows:** amendment 09's rows exactly (`GapperExploreStage.A09RowsAsync`): amendment 01's population of the
  split under test, restricted to symbol-days with an m0, at amendment 01's weights.
- **Score s:** the H27 full-feature model's predicted probability of y at the end of m0 (`LogisticRegression.
  FitWeighted` defaults, amendment 09's features with the drop/indicator selection made once on all train rows).
  On train, s is **out of fold**: amendment 09's five contiguous month blocks, each scored by the model fit on the
  other four (the same pooled scores the A09-H1 train run used). On validation and the holdout, s comes from the
  one model fit on all train rows, never refit on the split under test.

## Entry

- **Signal:** s >= 0.50 at the end of m0. One fixed threshold, chosen for a reason that needs no data: the model
  rates +20% more likely than not. It was not read off any score distribution (none was looked at), and it is not
  tuned after any result. Top-k per day was rejected because ranking a day's move starts needs the m0s that
  happen later that day.
- **Fill:** the open of the first bar with volume starting at or after the end of m0 (the protocol's
  `nextBarOpenAfterT`; the m0 bar is never traded), as `GapperRuleSimulator` enters.
- **No trade:** no such bar before the configuration's sell-by; or the fill is already >= 1.20 x the
  split-adjusted prior close (the +20% the signal predicts already happened). At most one trade per symbol-day.
  Long only.

## Exit ladder (H12/H18's frozen ladder, plus two registered knobs)

`GapperRuleSimulator` unchanged: intrabar stop 10% under the entry, VWAP loss on (04:00-anchored, after 3 closes
above), velocity death on the 3-minute velocity (the frozen label's arm threshold and two negative readings),
signal exits filled at the next real bar's open (halt fills by the bar-gap rule). The grid is 2 x 2 = **4
configurations**:

| knob | values |
|---|---|
| time stop | sell-by 15:55 ET; or entry bar start + 60 minutes, capped at 15:55 ET |
| profit target | none; or a resting sell at 1.20 x the prior close (the level y predicts) |

The target fills at the first bar, from the entry bar on and before the ladder's own exit bar, whose high reaches
it: at the target, or at the bar's open when it opened above. A bar that also hits the stop counts as the stop.

## Cost model (H18's, unchanged)

`GapperCostModel.Estimate` once per round trip: the prior-days Corwin-Schultz spread (the same symbol-day's 08:00
snapshot `csSpreadPrior`, prior days only) x the once-calibrated spread multiplier (`GapperSpreadMultiplier`,
calibrated on premarket quotes and applied unchanged here, which if anything over-charges a regular-hours
spread), half per side; square-root impact per side with ADV = cumulative volume 04:00 to the entry bar, sigma20
of the prior 20 sessions, $1,000 notional; round trip floored at 0.5%. A missing spread estimate adds no spread
(the floor still applies) and is counted.

## Hypothesis

| Id | Claim | Pass on train | Then on validation |
|---|---|---|---|
| H29 (A10, family F10) | s >= 0.50 entries on the frozen ladder net >= +1.0%/trade after costs, 4-config grid | selected config (highest weighted day-clustered lower bound among configs with >= 150 trades): weighted mean net >= +1.0%/trade, lower bound > 0, >= 150 unweighted trades on >= 40 days, SPA p < 0.05, DSR >= 0.95, positive mean in >= 3 of 5 month-block folds | the carried config, once: the same minimums, SPA p < 0.05, DSR >= 0.95 |

Statistics exactly as H12p/H18's `ScoreWeightedGrid`: weighted mean net per trade with a 10,000-draw
day-clustered bootstrap (seed 1337), DSR at the unweighted trade count over the 4 configurations, SPA on the
per-day weighted mean net per population cell. Each configuration is one ledger trial.

## Procedure

1. This file and its JSON are committed and pushed first, as their own commit, before any code.
2. Then `research gappers explore --command late-runner-ride` (train only; the explore stage refuses every other
   split) is written, committed and run once.
3. **Fail on train:** stop. Validation is not read, and no other threshold, target, time stop, stop size or cost
   model is tried under this amendment. A different idea needs its own new amendment registered before its
   numbers, and counts these 4 trials.
4. **Pass on train:** the selected configuration is written below as a dated note, then validation is read once
   (`research gappers test --hypothesis H29 --split validation`, implemented then under these definitions).

## What a pass does and does not allow

A pass on train and validation says this one entry and exit would have netted the bar after the frozen costs on
data the rule was not chosen on. It does not make anything live and changes no strategy, template, radar or
sell-by. The single holdout run (frozen and A01-A10 together, only with the user's explicit go-ahead) must net
>= +0.5%/trade with a lower bound > 0, then 20 sessions of paper shadow; only then could a paper-only
regular-hours template be offered, and only the user enables it. A fail says this entry/exit does not make the
real move-start signal tradable; it does not undo amendment 09.
