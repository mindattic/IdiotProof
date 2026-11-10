# Amendment 05: H1 recomputed after the ticker-resolution look-ahead fix (IP-T508)

Written 2026-11-10. Not a new test and not a new choice: the same pre-registered H1 comparison (protocol, amendment
03) recomputed once on corrected data, because a bug found after the holdout run touched its inputs.

## The bug

`MomentumTracker.ResolveSymbol` gave a misheard ticker's candidates a bonus when they were a top-100 mover by high on
the trade date itself, and drew one-edit variants from that day's movers. Neither was known when the video went up.
It resolved doubtful tickers toward names that ran, which can only flatter his hit rate. Fixed in 1b2d7e2c (prior
session's movers only); `momentum reresolve` re-resolved every stored claim without re-running the LLM and the Hub
re-verified the changed ones: 97 of 2,558 claims changed symbol, almost all to unresolved.

## Recomputed (same code paths: edge.py h1, trade dates through 2026-11-09)

| split | watchlist days | his high minus S1 | his high minus S2 | his close minus S1 |
|---|---|---|---|---|
| fit | 135 | +6.2 [-1.1, +13.6] | +7.6 [+0.1, +15.0] | +6.9 [+1.5, +12.0] |
| validation | 81 | +14.5 [+4.0, +25.2] | +19.7 [+8.9, +30.4] | +3.2 [-5.5, +11.6] |
| holdout | 61 | +24.2 [+13.0, +35.3] | +14.9 [+3.1, +27.1] | +22.7 [+14.0, +32.1] |

The amendment 03 pass rule (both holdout high lower bounds above 0) still holds: +13.0 and +3.1 (were +11.8 and
+2.3). The radar lift recomputed the same way is 171x on fit + validation (39.3% of 565 picks ran +50% vs 0.229% of
liquid names), against 167x before; `GapperRadar.Lifts.MomentumPick` stays at 167, inside the estimate's noise.

These numbers also use edge.py's two bughunt corrections (IP-T505): picks counted once per day, and H1 grouped by
trade date rather than by video (one or two dates per split had two evening videos).
