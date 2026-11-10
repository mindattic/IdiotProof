# Amendment 03: one holdout test of H1 (his selection) (IP-T500)

Written 2026-11-10 after the fit pass (`runs/fit-20261110-1101.md`) and the one validation run
(`runs/validation-*.md`, choices 232e7da2c00588ed), before any holdout row is read.

## Why

The protocol tests H1 only on fit, and only as a kill rule: if the 90% CI of his picks' next-session high over the
screen's includes 0 on fit, "his selection adds nothing". On fit it did include 0 (vs S1 +2.8 [-3.9, +9.2], vs S2
+4.0 [-2.8, +10.7]), so by the letter of the protocol the radar uses the screen. On validation the same comparison
came out far from 0 (vs S1 +16.0 [+5.9, +26.3], vs S2 +21.3 [+11.5, +31.2]); closes beat on fit (+5.7 [+0.8, +10.4]
vs S1). Fit and validation disagree. The kill stays in force until a test nobody has looked at settles it: H1 on
the holdout, once.

No rule (H2, H3, H4) survived validation, so the holdout has no other use; this is the only holdout run.

## Test

- Window: holdout trade dates 2026-07-01 .. 2026-11-09 (2026-11-10 is unfinished and dropped).
- Data and method exactly as H1 in the protocol: his evening picks known before the 09:30 open (amendment 01), S1 and
  S2 with the same filter, outcomes winsorized at +300%, day-clustered bootstrap (params.json seed and draws).
- **Primary:** next-session high over prior close, his mean minus S1 and his mean minus S2.
- **Pass:** both 90% CI lower bounds above 0 (intersection test, so no multiplicity correction).
- Reported, not tested: close over prior close; share of his picks reaching +50%; overlap with the screen.

## What follows

- **Pass:** his picks are a selection signal a screen does not reproduce. The radar's Momentum. group gets a lift
  equal to his picks' rate of a next-session high >= +50% on fit + validation over the base rate of liquid names on
  the same sessions (estimated from data already seen; the holdout only decides yes or no). Still no trading rule:
  H3 found none on his picks that survives costs.
- **Fail:** the kill stands; the Momentum. group stays at 0 points and his picks are shown, not scored.

The run is `python tools/momentum/edge/edge.py holdout-h1`; it needs `holdout.unlock`, writes
`runs/holdout-run.json`, and refuses a second run.
