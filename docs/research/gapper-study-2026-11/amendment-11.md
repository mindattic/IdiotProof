# Gapper study, amendment 11: a liquidity floor on the late-runner ride (regular hours)

Registered 2026-11-20 05:30 ET (IP-T619), before any A11 number existed and before the code that applies the
liquidity floor was written: no trade had been simulated with it, no cost computed, and no count of how many
amendment-10-eligible move starts it removes had been looked at, on any split. The machine-readable form is
`amendment-11.json`; where the two differ, the JSON governs. No earlier file is edited, no frozen hypothesis,
threshold or verdict changes, and the holdout stays locked. Research only: nothing here arms, sizes, places or
exits an order.

## Why

Amendment 10 (H29, IP-T617) failed decisively on train: all 4 exit configurations lost -3.12% to -3.44%/trade
after costs, DSR 0.000, SPA p 1.000, 0 of 5 month-block folds positive. Two facts from that same run point at one
mechanism rather than a bad exit choice:

- The mean round-trip cost was **2.83%** (median 2.58%) -- the frozen cost model's spread term, driven by the
  prior day's Corwin-Schultz estimate, dominates on thin names.
- The single most common exit, across every configuration, was **velocity death** (170-252 of 389 entries) --
  more entries died to the move stalling within minutes than reached target, stop, or sell-by combined.

Both symptoms point at the same cause: the move starts the H27 model is most confident about are disproportionately
thin, illiquid names, which carry both the highest spread cost and the highest odds of being a false start that
dies before it goes anywhere. Amendment 10's own entries were never filtered on liquidity at all.

## Question

Leaving amendment 10's score threshold, fill rule, exit ladder, and cost model exactly as tested, and changing
only one thing -- requiring at least $2,000,000 of 20-day average dollar volume at m0 -- does the late-runner ride
net money after costs?

## Honest prior

Better odds than amendment 10, but still probably not. $2,000,000 ADV is a modest floor, not a hard liquidity
screen; it will remove the thinnest tail but most of amendment 10's 389 entries were already real stocks, not
pure shells. The dominant failure mode (velocity death) may simply mean most move starts are false starts
regardless of liquidity, in which case this filter trims some bad trades and some good ones in about the same
proportion and the result stays negative. A fail is the more likely result and will be reported as the headline
if it happens.

## Population, score and entry (frozen with this file)

Amendment 10's population, score (H27, pooled out-of-fold on train), fill rule and no-trade rules, unchanged --
see `amendment-11.json`. The one addition: **logAdv20Dollars corresponds to at least $2,000,000 of 20-day average
dollar volume at m0**, applied as an additional no-trade rule. $2,000,000 is a round number chosen now, for a
reason that needs no data (a common minimum liquidity screen used across microcap trading desks), not read off
any score, cost, or return distribution from amendment 10 or anywhere else.

## Exit ladder and cost model

Amendment 10's exit ladder and H18's frozen cost model, completely unchanged. **One configuration only** --
target label20, sell-by 15:55 ET, the configuration amendment 10's own selection rule favored on train -- not a
grid. This amendment spends one trial, isolating the liquidity floor as the only new variable against amendment
10's own best-performing exit choice.

## Hypothesis H30 (frozen)

Buying filtered late-runner move starts and exiting on amendment 10's ladder nets at least +1.0%/trade after
costs, with a day-clustered 95% lower bound above 0, on at least 60 unweighted trades over at least 20 days, DSR
>= 0.95, and a positive weighted mean net in at least 3 of amendment 09's 5 month-block folds. minTrades/minDays
are lower than amendment 10's (60/20 vs 150/40) because the liquidity floor is expected to shrink the entry count
materially from 389; a smaller honest sample is accepted as the cost of testing this mechanism, not relaxed
because of any number already seen.

## Decision rules

- **Train fail -> stop.** Validation is not read. No other liquidity floor, score threshold, exit, or cost model
  is tried under this amendment. The move-start signal stays real (amendment 09); this specific filtered
  entry/exit is reported as not tradable. A further idea needs its own new amendment.
- **Train pass -> one validation read**, model fit on all train rows, liquidity floor carried unchanged, never
  re-chosen on validation.
- **Corrections:** A11 is its own family (F11, one hypothesis); never enters the frozen A01-A10 corrections or
  Holm set.
- **Promotion:** a pass on train and validation still trades nothing -- the single holdout run (frozen, A01-A11
  together, user's explicit go-ahead only), then 20 sessions of paper shadow, before any paper-only template could
  even be offered, and only the user enables it.
- **Kill criterion:** unaffected by A11 either way.

## Next step (not done yet)

The liquidity filter is not implemented: `research gappers explore --command late-runner-ride` has no ADV floor
option today. Adding it (reading amendment 09's own logAdv20Dollars feature already computed per row, no new
data pull needed) and running it once on train is the next step under IP-T619, following this registration.

## Train result (2026-11-20, IP-T619)

**H30 fails on train**: of amendment 10's 437 signals, 137 (31%) fell below the $2,000,000 ADV floor, leaving
300; 271 entries after the no-trade rules. Mean net -3.13% [-4.10%, -2.26%], DSR 0.000, 0 of 5 month-block folds
positive. Full report: `runs/explore/late-runner-ride-liquidity-20261120-1026.md`.

This is nearly identical to amendment 10's unfiltered -3.12% -- removing the thinnest third of entries barely
moved the result at all. That rules out the mechanism this amendment bet on: it is not that illiquid names alone
drag the average down with outsized cost or outsized false-start odds. Velocity death stayed the dominant exit
(128 of 271, 47%, versus amendment 10's 44%) in about the same proportion as before, filtered or not. The honest
reading is that most of these move starts -- liquid or thin -- are false starts the velocity-death rule correctly
cuts off, not that the signal is somehow fine except for a liquidity confound. Per this amendment's own stop
rule: validation is not read, and no other liquidity floor, score threshold, exit or cost model is tried under
amendment 11. A further idea needs its own new amendment with a different mechanism -- not another liquidity
cut.
