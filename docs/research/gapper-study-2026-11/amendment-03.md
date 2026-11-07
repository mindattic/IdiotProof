# Gapper study, amendment 03: a Corobs look-alike detector, alone and plugged in

Registered 2026-11-07 10:58 ET (IP-T451), before any hypothesis was tested on study data. The
machine-readable form is `amendment-03.json`; where the two differ, the JSON governs. No earlier
file is edited.

## The question

Corobs implements correlithm object technology (P. Nick Lawrence, *Correlithm Object Technology*,
2004). Its premise is that in a unit cube of more than about twenty dimensions, any two random
points lie almost exactly the same distance apart, so a point that lands *close* to a known example
almost certainly came from the same process. That is a recognizer by resemblance, and the question
of this study ("recognize a gapper while it is forming") is a resemblance question: does this 08:00
morning look like the 08:00 mornings that became gappers?

The method has no track record on market data, so this amendment does not let it touch any existing
score. It asks three separate questions: does Corobs work **alone**, does it add anything when
**plugged into** the frozen logistic detector, and which of the two is better **head to head**. A
fourth score, **novelty** ("unlike anything seen before"), tests Lawrence's anomaly-detection claim.

## Encoding

Each of the 28 H11 features at 08:00 (gap, premarket volume against the time-of-day profile,
velocities, acceleration, spread proxy, distance to the premarket high, the five prior days'
abnormal returns and volumes, days since a reverse split, 424B or S-1) becomes one 8-dimension ply
on a line string CO over its train 1st-99th percentile range; a missing value sits at the ply
midpoint. The state point is the 224-dimension concatenation. The seed is the frozen study seed,
derived per feature, so every coordinate rebuilds bit for bit.

## Scores

| Score | What it is |
|---|---|
| benchmark | H11's catalyst-latency indicator: news or an 8-K within 60 minutes |
| logistic | H11p's weighted logistic detector, unchanged |
| corobs | weighted reach-20%-by-09:45 rate among the 100 nearest training mornings, shrunk to the base rate by one sample day's weight |
| combined | the logistic detector refit with logit(corobs) as a 29th feature (train rows use leave-one-day-out corobs scores) |
| novelty | mean distance to those 100 nearest training mornings |

Population and weights are amendment 01's (candidates plus the seeded 10% sample), so every AUC is
an estimate for the live universe, not for days already known to have run.

## Hypotheses

- **H21 (primary).** Corobs alone beats the catalyst-latency benchmark by >= 0.05 weighted AUC: the
  H11p bar with Corobs in place of the logistic detector.
- **H22 (primary).** The combined model beats the logistic detector alone by >= 0.01 weighted AUC.
- **H23 (secondary).** Head to head, Corobs alone is at least as good as the logistic detector.
- **H24 (secondary).** The top novelty decile has precision >= 2x the base rate.

## Reading the result

- H21 supported: Corobs works on its own.
- H22 supported: it adds information the logistic detector lacks.
- H23 refuted and H22 not supported: it is no better than the existing logic.
- Neither H21 nor H22 supported: it does nothing useful here, and the write-up says so.

## Rules

A03 runs are corrected as their own set (Holm across H21 and H22). A03 tests detection only. It
cannot promote a trading rule, does not trigger or lift the H11 kill criterion, and wires nothing
into a live path. A supported H21 or H22 earns at most a Corobs-gated trading grid, registered in a
later amendment before it is tested. The single holdout run includes A03. Every A03 result is
reported, failures included, with the four AUCs side by side.
