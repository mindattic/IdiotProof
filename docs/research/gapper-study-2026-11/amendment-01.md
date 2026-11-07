# Gapper study, amendment 01: population-weighted detection and strategy tests

Registered 2026-11-07 00:40 ET (IP-T448), before any hypothesis was tested on study data. The
machine-readable form is `amendment-01.json`; where the two differ, the JSON governs. `protocol.md`,
`hypotheses.json` and `params.json` are not edited, and every frozen hypothesis is run and reported
exactly as registered.

## The problem

The protocol asks its prediction questions on *candidate days*: symbol-days whose daily high reached
at least +10% over the adjusted prior close, or whose full-day volume reached 3x ADV20. That set is
defined by the day's own outcome. H1, H9 and H11 measure precision and AUC among candidates only, and
H12 simulates its entry rule only on candidates, so every trade it takes is on a day already known to
have run at least +10% at some point or to have traded unusual volume.

A live detector at 08:00 does not know which symbols will become candidates. It scans every member
of the universe. Measured on candidates alone, a rule's precision and per-trade return are therefore
upper bounds on what it achieves live, and the bound can be loose: an entry rule with no skill at all
looks profitable when it is only ever allowed to buy stocks that are about to rise. The frozen
hypotheses answer a narrower conditional question ("among the days that turned out to be candidates")
than the one the study exists to answer ("among every stock at 08:00").

The matched controls cannot repair this, because they are matched to gapper days on ADV decile and
price tier rather than drawn uniformly, so they do not represent the non-candidate population.

## The fix

A uniform random sample of non-candidate universe days, drawn by a seeded hash so that membership is
fixed before any outcome is seen and can be recomputed by anyone:

- **Eligible:** every member of U(D) that is not excluded and not a candidate, in every split.
- **Rate:** 10%. A symbol-day is a member when the first 8 hex digits of
  `SHA-256("{symbol}|{yyyy-MM-dd}|gapper-study-2026-11-amendment-01")`, read as an unsigned 32-bit
  integer and divided by 2^32, are below 0.10.
- **Data:** exactly the tiers candidate days receive (minute bars for D and D-1..D-5, news, CIK
  mapping, EDGAR index and acceptance headers, Form 4), so that no feature is present or absent
  depending on candidate status. A detector could otherwise learn "missing data means non-candidate".
- **Weighting:** candidate days are a census (weight 1) and sample days carry weight 10, so each
  amended estimate is a stratified estimate over the whole universe under the same row filters as
  the frozen hypothesis.

Nothing else moves: the time-of-day profile is still fit on train control days only, controls and
gapper classification are unchanged, and no frozen hypothesis sees a sample row.

## Amended hypotheses

| Amended | Amends | Same thresholds | What changes |
|---|---|---|---|
| H1p | H1 | top quintile precision >= 3x base rate for reach20By0945 at 08:00 | population-weighted edges, rates and base rate |
| H9p | H9 | v_3 > 1%/min and pmVolRatio > 5 at 09:00 → fwdMax_09:45 >= 10%, lift >= 2 | population-weighted rates |
| H11p | H11 | detector beats the catalyst-latency benchmark by >= 0.05 AUC | weighted fit and weighted AUC |
| H12p | H12 | >= +1.0%/trade net, lower bound > 0, >= 150 trades on >= 40 days | trades every pass in the population |

Methods are stated in full in `amendment-01.json`. Two departures from the frozen primitives are
forced by the weights and are stated in advance: bucket intervals and p-values come from a
day-clustered bootstrap over per-day weighted sums (not the exact binomial, which would treat
weighted rows as independent trials), and H12p's SPA runs on per-day weighted mean returns per
configuration.

## Decision rules

- The H11 kill criterion applies to H11 **and** H11p.
- Promotion to the paper shadow requires every frozen condition **and** H11p and H12p meeting their
  minimum effects on validation, and H12p netting >= +0.5%/trade with a lower bound above zero at
  the single holdout run. The amendment can only make promotion harder.
- The single holdout invocation runs frozen and amended hypotheses together.
- Frozen results are reported unchanged, each amended result beside the one it amends.

## Why this is not a forking path

The amendment was registered before any test ran on study data, it adds no threshold, grid point or
feature, and it narrows rather than widens the conditions under which anything is promoted. Its only
effect on a frozen result is to place a population estimate beside it.
