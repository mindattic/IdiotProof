# Gapper study, amendment 06: the detector as a watch-only alert, and its paper shadow

Recorded 2026-11-17 (IP-T447) on the user's decision to re-scope the formation arming ticket, before any live
08:00 row was scored. No earlier file is edited, no hypothesis or threshold of the study changes, and the holdout
stays locked. This file has no `.json` twin: it adds no hypothesis the test stage runs.

## Why there is no arming

IP-T447 was to fill `formation-trigger.json` from the rule(s) that survived and arm paper trades. None survived:
H12 (-3.45%/trade net on validation), H12p (-3.93%) and H18 (-2.36%) were refuted, so promotion conditions 3, 4, 6
and 7 cannot be met and the holdout was never opened for a strategy. The trigger stays all-null and
`formation.arming.enabled` stays off.

What did survive is the detector: H11 (AUC 0.870 vs 0.549) and H11p (weighted AUC 0.912 vs 0.558) on validation,
logistic on the 08:00 snapshot for "reaches +20% over the prior close by 09:45". It ranks; it is not a trade.

## The alert (frozen)

- **Model.** `research gappers detector-export` refits H11p exactly as its validation run did (weighted, train
  split only, `FormationDetector.Features`, `LogisticRegression.FitWeighted` defaults) and writes
  `IdiotProof.Core/Catalogs/formation-detector.json`. On export it reproduced the reported validation result:
  weighted AUC 0.912 vs benchmark 0.558 on the same 107,669 rows (253,115 train rows, train base rate 0.57%).
- **Inputs.** The live recorder computes the 08:00 snapshot with the study's own `GapperFormationFeatures.Compute`
  and, as of this amendment, the reverse-split and 424B / S-1 inputs the study's snapshots carried, read from the
  permanent history (IP-T454) with the snapshot stage's rules (365-day window; no acceptance time = 23:59:59 ET).
- **Output.** At 08:00 ET the recorder scores every universe member, stores the score on the row
  (`detectorScore` in its extras) and writes one `formation-alert` audit row listing the top 10. Nothing arms.

## Known differences from the study, stated before any result

1. **Universe.** The study scored every candidate (and, for H11p, a weighted sample of every listed stock). The
   live recorder scores its own universe (catalyst, mover, most-active and tracked names, price $0.10-$20, at most
   150). Most of the population's easy negatives are absent, so the live AUC is expected to be lower than 0.912.
2. **Time-of-day profile.** `VolRatioTod` needs `formation.todProfilePath` and decile edges; without them it is
   missing live (scored as the training mean). The drift check below reports it.
3. **Daily bars.** The study split-adjusted daily bars by its own split table; the live recorder uses its bar
   source's daily bars.

## Shadow (pre-registered)

`idiotproof gapper formation shadow` evaluates it; the first 20 labeled sessions after this amendment decide.

- **Unit.** One live 08:00 row with a detector score. Outcome: `GapperStudyLabeler.Reach` with the study's
  parameters (highest high of bars ending by 09:45 ET at least +20% over the prior daily close).
- **Benchmark.** The study's H11 benchmark: a catalyst 60 minutes old or newer at 08:00.
- **Pass.** After 20 labeled sessions: detector AUC minus benchmark AUC with a day-clustered bootstrap 95% interval
  (10,000 draws, seed 1337, the study's settings) whose lower bound is above 0, **and** no detector input missing
  live more than 20 points more often than in training (`TrainMissingShare` in the model file).
- **Fail.** Either condition missed. A fail retires the alert's claim; the alert may stay as a list, labeled as
  unvalidated live.
- **Reported, not tested.** Each day's top-10 hit rate against the day's base rate, the detector's AUC alone.
- **One evaluation** at 20 sessions; interim runs of the command show counts and running numbers but decide
  nothing.

## What a pass does and does not allow

A pass says the ranking carries over to the live universe. It does not make anything tradable: arming still needs
a rule that survives validation and the holdout (protocol, promotion conditions 3-7), which none has.
