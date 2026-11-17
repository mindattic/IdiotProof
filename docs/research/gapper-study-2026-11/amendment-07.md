# Gapper study, amendment 07: long-only multi-step gapper templates (Steps), train only

**Numbering.** IP-T487 registered its catalyst-taxonomy amendment under this number first (commits 275fbaf8 and
162ec07b, the same afternoon). This ticket then wrote this file over it by mistake, in the working tree only. IP-T487
renumbered its text to `amendment-08.md` without changing a word of it, so 07 is this amendment and 08 is the catalyst
taxonomy. Nothing of this amendment had been computed when that happened.

Registered 2026-11-17 19:37 ET (IP-T527), before any template was replayed on any study day. The code that
runs it (`research gappers explore --command gapper-sequences`) was written and compiled before this file was
committed, and it was not run. No earlier file is edited, no frozen verdict changes, and the holdout
(2025-11-03..2026-09-09 after amendment 05) stays locked: this amendment never reads it or validation. Like
amendments 05, 06 and 08 it has no `.json` twin, because it adds no hypothesis the test stage runs.

## Question

The DSL can now sequence one strategy as ordered Steps (`Step`, `ThenWhen`, `Within`, `Repeat`; IP-T445,
IP-LAW-14). The owner wants multi-state gapper strategies built on them. Measurable form: **does a long-only
sequence that adds a second leg after the premarket ride earn more, net of the frozen cost model, than the
single-step gapper on the same train days, and is the sequence worth trading at all?**

## Honest prior

No. Every long rule this study has tried loses after costs (H12 -3.45%/trade, H12p -3.93%, H18 -2.36% on
validation; 4,424 long trials in the ledger, none surviving the frozen cost or deflation). The second legs here
buy after the open, where `gapper-anatomy-and-tradability.md` §3 found that a gapper already up 20% at the open
loses 5.2% to the close, and the frozen cost model charges the premarket spread (prior-days Corwin-Schultz x 9.39)
on every leg. The expected result is that step 0 loses about what the single-step gapper loses and the second
leg adds a further loss. That is reported as the headline if it happens.

## What the study found, and how each template uses it

| finding (source) | used as |
|---|---|
| The high prints after 09:46 on 40-45% of gapper days; velocity dies 35-42 minutes before the high (H4/H5, `gapper-anatomy-and-tradability.md` §3) | a second leg after the bell is the only place a long could still catch the high |
| VWAP loss is the frozen exit ladder's guard; the only continuation arm with literature behind it is the hold-past-bell switch: stay only if the first minutes after the open hold above the open and the premarket high (`strategy-refined.md` §2.5, `methods-exit.md` rank 5, low prior) | both second legs require price above VWAP; template 2 requires the pre-bell high retested with positive velocity |
| Nothing should be held into 09:45-09:50, where 36.4% of LULD pauses fall (`strategy-refined.md` §2.5) | template 2 is flat by 09:44 |
| Guard stop `s` in {8%, 12%}; 1-minute volatility is about 2% (`strategy-refined.md` §2.5) | second legs use an 8% stop, not the ride's 5% |
| Second-day longs lose: the gapper day's close to the next open -0.09%, to the D+2 close -1.18% [-1.57, -0.77]; 74% of a recent runner's move is gone by D+2 (`gapper-anatomy-and-tradability.md` §1, §3) | the day-two template is shipped as a sample only and is **not evaluated** (see below) |

## The strategies (frozen)

All are replayed as written by `GapperSequenceTemplates` (`IdiotProof.Scripting`), with the ticker substituted.
Step 0 of every template is the catalog's `classic-gapper` profile with its own numbers, so a sequence differs
from the baseline only by what it does once the ride is flat. No parameter is searched: there is no grid.

**Baseline (single step):** `GapperScriptFactory.Compose(symbol, classic-gapper)` from
`IdiotProof.Core/Catalogs/gapper-profiles.json`:

```
Ticker(SYM).Session(IS.PREMARKET).RequireEntryWindow("04:00", "09:00").IsGapUp(5).IsVolumeAbove(2).IsPriceBetween(1, 50)
  .Long().QuantityNotional(1000).StopLossPercent(5).PeakGiveback(25, "09:15").SellBy("09:28")
```

**Template 1, `gapper-ride-vwap-pullback`** (ride, flat into the bell, then the first higher low above VWAP
after the open):

```
Ticker(SYM).Session(IS.EXTENDED).Quantity(1).Step("ride")
  .RequireEntryWindow("04:00", "09:00").IsGapUp(5).IsVolumeAbove(2).IsPriceBetween(1, 50)
  .Long().StopLossPercent(5).PeakGiveback(25, "09:15").SellBy("09:28")
  .ThenWhen(IsAboveVwap()).Within(1)
  .Step("pullback").Quantity(1)
  .RequireEntryWindow("09:35", "10:30").IsAboveVwap().IsHigherLow().IsGapUp(5)
  .Long().StopLossPercent(8).PeakGiveback(25).SellBy("11:00")
```

**Template 2, `gapper-ride-retest`** (ride, flat into the bell, then a second leg only if the open retests the
pre-bell high with speed):

```
Ticker(SYM).Session(IS.EXTENDED).Quantity(1).Step("ride")
  ...the same ride...
  .ThenWhen(IsAboveVwap()).Within(1)
  .Step("retest").Quantity(1)
  .RequireEntryWindow("09:31", "09:40").IsAboveVwap().IsAtResistance(1).VelocityAbove(0.2, 3).IsGapUp(5)
  .Long().StopLossPercent(8).PeakGiveback(25).SellBy("09:44")
```

The ticket's wording for this one was "only if the stock reclaims the premarket high after the open". The DSL has
no session-high level, so the template uses what it has: `IsAtResistance(1)` (price within 1% of the high of the
20 bars before the current one, which between 09:31 and 09:40 spans the last premarket minutes and the open) with
`VelocityAbove(0.2, 3)`. It is a proxy and is named as one.

**Template 3, `gapper-ride-day-two`** (ride on day one, a second premarket ride within one day): shipped as a
sample, **not evaluated**, for two reasons stated before any data: (1) the study's minute bars cover a candidate
day and its D-1..D-5; D+1 is present only when D+1 was itself a candidate, so any D+1 replay would be selected on
the outcome; (2) the finding in the table above already argues against it. Its DSL also cannot say "next
session": the second step's 04:00-09:00 window reopens the next morning after a ride that held to 09:15 or later,
but a ride stopped out early can re-enter the same morning.

Sizing: the templates carry 1 share per step (the repo's default for a generated strategy). Returns are per cent
of the entry, so the share count does not change them; costs are computed at the frozen $1,000 notional per leg.

## Evaluation (frozen)

- **Evaluator.** `StrategyBacktester.Run`, the shared evaluator the Monitor uses (IP-LAW-10), unchanged. Entries
  and signal exits fill at the close of the bar that satisfies them; a stop fills at the stop, or at the open of
  a bar that gaps through it. This differs from the study's own next-bar-open rule and is not adjusted.
- **Days.** Train only, 2021-11-01..2024-10-31, through `GapperStudyServices.StudyDaysAsync("train")`, the split
  guard the explore stage already uses (it refuses any other split). The population is the one `explore build`
  uses: every candidate day at weight 1 plus amendment 01's seeded sample of all other listed days at its inverse
  probability weight, so selection on the day's own outcome is weighted out.
- **Bars.** The symbol-day's stored minute bars from 04:00 ET through the first bar at or after the strategy's
  last sell-by; prior close = the study's split-adjusted prior close. A symbol-day none of whose bars before
  09:00 closes at least +5% over the prior close inside $1-$50 cannot satisfy step 0 and is skipped without a
  replay. That skip is exact, not a filter.
- **Unit.** One sequence run = one symbol-day on which step 0 entered. Its return is the sum of its legs' net
  returns, each leg on the same notional. The baseline's unit is its single trade.
- **Cost (frozen model, per leg, at the leg's own entry).** Prior-days Corwin-Schultz spread x the calibrated
  premarket multiplier (9.393, `data/spread-multiplier.json`), plus 2 x square-root impact on the volume since
  04:00 with the 20-session sigma at $1,000, floor 0.5%. **This decides.** Reported beside it, deciding nothing:
  the spread term at 0.5x and 0.25x (as the alarm-ride report did), and the later leg at a regular-hours cost
  (prior-days Corwin-Schultz with no multiplier plus $0.0035/share each side, as the ORB report did), because
  the later leg trades after 09:30 and the frozen model is a premarket one.
- **Statistics.** Weighted mean and median net return, day-clustered 95% interval (`ExploreDerived.ClusteredMean`,
  the estimator of the earlier explore reports), the four rolling fold windows inside train (no model is fitted,
  so pooled = every train day), the paired difference against the baseline on the same symbol-days, the later leg
  on its own, and the per-run deflated Sharpe at N = the ledger total after the run.
- **Trials.** 3 ledger rows in `runs/explore/ledger.jsonl` (the baseline and the two templates), track
  `gapper-sequences`. The ledger stands at 5,613 before the run. One run; a rerun would be logged again.

## Pass criterion (per template, on train, at the frozen cost; all must hold)

1. Mean net return per sequence run >= +1.0% with the day-clustered 95% lower bound above 0 (the protocol's
   worth-trading bar).
2. At least 150 runs on at least 40 distinct days, and at least 150 later-leg trades (otherwise the template is
   the baseline with a decoration).
3. Mean net above 0 in at least 3 of the 4 fold windows.
4. Sequence minus baseline, paired on the same symbol-days, above 0 with the lower bound above 0.
5. Deflated Sharpe >= 0.95 at N = the ledger total after the run (params `statistics.dsrMin`).

## What a pass and a fail do

- **Pass:** the template is added to `gapper-profiles.json` as a selectable profile and the profile reader is
  taught to compose it. Nothing is armed or activated. A train pass is not a validated rule: it would still owe
  validation (once, through a further amendment), the single holdout run and the 20-day paper shadow before any
  live mode could be offered (protocol, promotion path).
- **Fail:** the templates ship as documented, backtestable samples (`strategy samples`, `backtest run`) and no
  profile is added, so `gapper arm` cannot select them.

Either way the result is written to `gapper-sequences.md` in this folder with the run output beside it.
