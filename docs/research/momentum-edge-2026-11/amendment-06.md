# Amendment 06: after-hours sign for the gapper radar, forward test (IP-T501)

Written 2026-11-16, before any after-hours bar for the window below was pulled and before any outcome in it was
looked at. Sessions 2026-11-11 through 2026-11-14 had already happened, but nobody has read their after-hours data or
their radar results. The sign, the threshold and the pass rule below are fixed now and do not change after a look.

## Lead

Momentum.'s picks that he tagged "moving after hours" reached +20% 80% of the time on fit (lift 1.33 over his own
average pick, median high +52%; `narratives-20261110-1139.md`). That is a descriptive tag on his picks, not a test. The
radar does not read after-hours prints today. The question here is whether an after-hours move on the evening before
marks a gapper among the radar's own candidates, apart from his picks.

## Sign (frozen, `GapperRadar.AfterHoursSign`, commit naming IP-T501)

For candidate S and session D with basis session B (the trading day before D):

- Bars: Alpaca SIP 1-minute bars of S from 16:00 to 19:59 ET on B (bars starting at or after 16:00 and before 20:00).
- `ah_last` = close of the last such bar / B's regular close (Alpaca daily bar, raw prices) - 1, in percent.
- `ah_dollars` = sum over those bars of close x volume.
- **AH+** when `ah_last >= +10%` **and** `ah_dollars >= $250,000`. No after-hours bars is AH- (0% on $0), not unknown.
  No daily close on B, or a failed fetch, leaves the sign unknown and the row is dropped from the test.

In the live radar the sign is listed as a reason at lift 1 (0 points), so the frozen ranking (amendment 02) is not
changed by an unmeasured weight.

## Population and outcome

- Population: every candidate the radar scores for D in `gapper radar-backtest` (the basis session's top 300 movers by
  high, close and gap, plus reverse splits calendared for D or the session after; Momentum. picks and tradability are
  left out of the replay, as in amendment 02). Point in time: everything as of 20:00 ET on B.
- Outcome: S's high on D at least +50% over its prior close (the same daily cross-section as amendment 02; a name not
  in D's top 300 movers did not run).

## Window

Sessions 2026-11-11 through 2027-01-29. If fewer than 30 AH+ candidate-sessions exist at the end, the window extends
once, to 2027-03-31; still fewer than 30 there is **inconclusive**, and the sign stays at 0 points.

One evaluation at the end of the window (`tools/momentum/edge/afterhours_forward.py --final`). Interim runs of the
script print counts only (sessions, candidates, AH+ rows), never outcomes.

## Pass rule (primary)

Risk ratio RR = P(run | AH+) / P(run | AH-) among the candidates, pooled over the window. 90% interval by a
session-clustered bootstrap (sessions resampled with replacement; 5,000 draws, seed 501). **PASS** when n(AH+) >= 30
and the lower bound of RR > 1.

## Reported, not tested

- RR inside the top-10-mover group (RankHighD1 or RankCloseD1 <= 10): whether the sign adds anything to the radar's
  strongest group, since the two will often be the same names.
- Marginal lift = P(run | AH+) / base rate (runners / liquid names per session). On a PASS this becomes
  `GapperRadar.Lifts.AfterHours` and the sign starts scoring log2(lift); any such weight change needs its own forward
  window before it is called better (amendment 02).
- The same table at +20% and at the +10% / +5% after-hours thresholds.

## What would change the plan

Nothing short of a bug in the code that computes the sign or the outcome. A bug found after the evaluation is handled
like amendment 05: same test, recomputed once on corrected data, written up as its own amendment.
