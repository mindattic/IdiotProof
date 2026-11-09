# Gapper study, amendment 04: the detection alarm finalist

Registered 2026-11-09 11:45 ET (IP-T475), on the user's go-ahead, before any A04 hypothesis was
computed on validation or holdout data. The machine-readable form is `amendment-04.json`; where the
two differ, the JSON governs. No earlier file is edited.

## Why this one

The exploration round (train only, 599 ledger trials at registration) found one result that clears
the plan's bar: a within-morning rank alarm that flags a forming gapper about an hour before it
reaches +20% and catches more of them than the screen a trader already has, at the same
false-alarm rate. Every long trading rule tried lost money after costs (entries -3.6% to -5.9% per
trade, exit routes -3.0% to -3.75%), so A04 registers **detection only**.

## The two rules

Swept minute by minute 06:00-09:10 ET on mornings still below +20% at 06:00, first alarm per morning:

| | rule | train hit rate | train false alarms / 100 mornings | median lead to +20% |
|---|---|---|---|---|
| **R4** | top 5 by composite rank (time-of-day volume ratio, gap so far, volume CUSUM), gap 5-15%, >= $50k premarket dollar volume, most of the gap traded through (conf >= 0.5) | 32.6% | 0.83 | 62 min |
| **B4** | gainers + news benchmark: fresh news (<= 60 min) first, then by gap; top 10, gap 2-15%, >= $50k | 27.6% | 0.83 | 62 min |

Nothing is fitted on the split under test; the only learned input is the frozen train time-of-day
volume profile.

## Hypotheses

- **H25 (primary).** R4's hit rate beats B4's by at least 3 points, with the lower bound above 0, at
  no more than 0.10 extra false alarms per 100 mornings. Day-block bootstrap.
- **H26 (secondary, descriptive).** R4's median lead from alarm to the first +20% is at least 30 minutes.

## Rules

A04 is its own correction set. The train run only checks the code reproduces the exploration
numbers. A supported H25 on validation and then on the single holdout run makes R4 the live
watchlist and alert (paper, alerts only); it never places, sizes or exits a trade. The holdout run
needs the user's explicit go-ahead. The fade-short pattern seen in exploration is not registered here.
