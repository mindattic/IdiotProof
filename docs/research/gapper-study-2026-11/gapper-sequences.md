# Gapper sequences: do long-only multi-step gapper strategies beat the single-step gapper? (IP-T527)

Written 2026-11-17. Registered as [`amendment-07.md`](amendment-07.md) (commits a108a279 and 9133e46e, pushed
before anything was replayed). Train split only (2021-11-01..2024-10-31); validation and the locked holdout were
never read. Run output: [`runs/explore/gapper-sequences-20261117-2030.md`](runs/explore/gapper-sequences-20261117-2030.md)
and its `.json`; ledger trials 5655..5657 (the ledger stood at 5,654 before the run and is 5,657 after).

## The headline

**Neither template passes, and neither beats the single-step gapper.** Adding a second long leg after the bell
makes the strategy worse on the same symbol-days, by 0.64 and 0.33 points per run at the frozen cost. The
single-step classic gapper itself loses 8.26% per trade net on train. This is the prior amendment 07 wrote down
before the run, and it is the same answer the study gave for every other long rule (H12, H12p, H18, 4,424 long
trials). Nothing is added to `gapper-profiles.json`; nothing can arm these templates.

## What was run

- **Evaluator.** `StrategyBacktester.Run`, the shared evaluator the Monitor uses (IP-LAW-10), unchanged, through
  `research gappers explore --command gapper-sequences` (`GapperExploreStage.Sequences.cs`). No backtester defect
  was found: on all 15,216 runs the sequence's ride was the same trade (same entry bar, same return) as the
  baseline's.
- **Days.** 261,809 train population symbol-days (105,964 candidates at weight 1, 155,845 amendment-01 sample
  days at weight 10). 2,102 had no minute bars. 237,203 never closed +5% over the prior close inside $1-$50
  before 09:00, so step 0 could not enter (an exact skip). 22,504 were replayed, none failed; step 0 entered on
  15,216 of them, across 755 trading days (3,710 of the 15,216 are Tier20+ gapper days).
- **Cost.** The frozen model per leg at the leg's own entry, $1,000 notional: prior-days Corwin-Schultz spread x
  9.393, square-root impact on the volume since 04:00, floor 0.5%. It averaged 5.53% per ride.
- **Statistics.** Weighted means, day-clustered 95% intervals, four fold windows, per-run deflated Sharpe at
  N = 5,657.

## Results (train, net of the frozen cost)

Per sequence run (one symbol-day on which the ride entered; the sum of its legs):

| strategy | runs | days | gross mean | mean cost | **net mean [95%]** | net median | win rate | folds > 0 | DSR |
|---|---|---|---|---|---|---|---|---|---|
| baseline: single-step `classic-gapper` | 15,216 | 755 | -2.72% | 5.53% | **-8.26% [-8.56, -7.95]** | -7.45% | 8.2% | 0/4 | 0.000 |
| `gapper-ride-vwap-pullback` | 15,216 | 755 | -2.77% | 6.12% | **-8.89% [-9.20, -8.59]** | -7.87% | 7.2% | 0/4 | 0.000 |
| `gapper-ride-retest` | 15,216 | 755 | -2.71% | 5.87% | **-8.58% [-8.88, -8.28]** | -7.71% | 7.7% | 0/4 | 0.000 |

The later leg on its own, and the sequence against the baseline on the same symbol-days:

| template | hand-offs (`ThenWhen` held) | later-leg trades | later-leg gross [95%] | **later-leg net, frozen [95%]** | later-leg net median | later-leg win rate | later-leg net at regular-hours cost [95%] | **sequence minus baseline, paired [95%]** |
|---|---|---|---|---|---|---|---|---|
| `gapper-ride-vwap-pullback` | 14,247 | 9,461 | -0.12% [-0.17, -0.07] | **-1.47% [-1.55, -1.40]** | -0.98% | 13.3% | -0.42% [-0.48, -0.36] | **-0.64% [-0.67, -0.60]** |
| `gapper-ride-retest` | 13,252 | 5,492 | +0.04% [-0.04, +0.12] | **-1.35% [-1.45, -1.25]** | -1.04% | 20.4% | -0.25% [-0.33, -0.16] | **-0.33% [-0.36, -0.30]** |

Sensitivities that decide nothing: with the spread term at 0.5x and 0.25x the three strategies net -7.85 / -8.33 /
-8.09% and -7.65 / -8.06 / -7.85%; the order does not change.

## Amendment 07's criterion

| template | mean >= +1.0%, lower bound > 0 | 150 runs, 40 days | 150 later legs | 3 of 4 folds > 0 | beats the baseline (paired) | DSR >= 0.95 | verdict |
|---|---|---|---|---|---|---|---|
| `gapper-ride-vwap-pullback` | no | yes | yes | no (0/4) | no | no | **FAIL** |
| `gapper-ride-retest` | no | yes | yes | no (0/4) | no | no | **FAIL** |

## What this says

1. **The second leg has no edge even before costs.** The VWAP-pullback leg loses 0.12% gross; the retest leg is
   +0.04% with an interval that spans zero. Requiring strength after the open (above VWAP, a higher low, or the
   pre-bell high retested with speed) does not find the days that keep running. 95% and 92% of the later legs end
   on the 25% peak giveback: they are bought, tick up a little and give it back.
2. **Cost is not the excuse.** Charged a regular-hours cost instead of the premarket one, both later legs still
   lose (-0.42% and -0.25%, intervals below zero).
3. **The ride is the bigger problem.** The classic gapper's gross return through the shared evaluator is -2.72%
   per trade on train: 46% of rides hit the 5% stop, 43% leave on the giveback, 12% reach 09:28. Its conditions
   (`IsGapUp(5)`, `IsVolumeAbove(2)` on one bar against the 20 before it, $1-$50) fire on 15,216 symbol-days of
   which only 3,710 became +20% gappers. This is a train-only description of the profile the product ships, not a
   new test of it; it agrees with the study's earlier long-rule results and is worth the owner's attention on its
   own.
4. **Day two was not tested.** `gapper-ride-day-two` ships as a sample only, for the reasons registered in
   amendment 07 (D+1 minute bars exist only for days that were themselves candidates; the study's second-day
   findings are negative).

## Limits

- Train only, one run, fixed parameters. A different second leg could behave differently; any new one is a new
  trial in the ledger and needs its own amendment.
- Fills are the shared evaluator's (the satisfying bar's close), not the study's next-bar-open rule.
- `gapper-ride-retest` uses `IsAtResistance(1)` with `VelocityAbove(0.2, 3)` as a proxy for "reclaims the
  premarket high": the DSL has no session-high level.
- The run's JSON in this folder omits the per-trade rows (25 MB); the full file is
  `D:\IdiotProofData\gapper-study\explore\gapper-sequences\gapper-sequences-20261117-2030.full.json`.

## What ships

The three templates are `strategy samples` entries (`GapperSequenceTemplates`, 1 share per step, long only), so
they can be shown, saved inactive and replayed with `backtest run`. No gapper profile was added, `gapper arm`
cannot select them, and nothing was armed or activated.
