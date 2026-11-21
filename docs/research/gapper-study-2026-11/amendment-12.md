# Gapper study, amendment 12: spread costs recalibrated from quoted NBBO, per session

Registered 2026-11-21 05:35 ET (IP-T625), before any A12 number existed. No session multiplier had been
computed by any method, and the script that computes one (`tools/gapper-study/a12_calibrate.py`) is committed
with this file and had never been run. The machine-readable form is `amendment-12.json`; where the two differ,
the JSON governs. No earlier file is edited, no frozen hypothesis, threshold, multiplier or verdict changes,
and the holdout stays locked. Research only: nothing here arms, sizes, places or exits an order.

## Why

Amendment 11's closing note, option 1, chosen by the owner. The descriptive quotes-check run (IP-T472,
`runs/explore/quotes-check-20261120-1532.md`) found the frozen spread charge well below the NBBO actually
quoted at simulated fills: per-trade medians of quoted/model of 2.81x overall, 1.45x for exits before 09:30
and 4.98x for exits at or after 09:30. A single multiplier, calibrated once at 08:00, cannot be right for both
sessions. Any future long rule that clears +1%/trade under the frozen model could be an artifact of
undercharging. This amendment fixes the cost model for later tests only.

What was already known when this was written, so nothing is hidden: those medians, which are a different
statistic from the one registered below (an unweighted median of per-trade ratios, on a file that held 5,538
duplicated legs; IP-T618, fixed in `3627fcce`).

## Method (frozen with this file)

- **Source.** The quotes on disk, `D:\IdiotProofData\gapper-study\quotes\entries-0830-buy-now.csv`
  (train only). One row per (symbol, date, leg); the first row on file wins, as `ReadQuoteEvents` does.
- **Eligible legs.** Weight above 0; a model spread above 0 (a prior-days Corwin-Schultz estimate existed); a
  valid quoted relative spread at the fill of at least 0 and at most 50% of mid. A wider quote is treated as no
  real market and excluded, and the count is reported.
- **Corwin-Schultz per leg.** The charged spread divided by the frozen multiplier quotes-pull charged with
  (`data/spread-multiplier.json`).
- **Session.** The leg's time in ET: premarket 04:00-09:29, regular 09:30-15:59. Anything else is excluded.
- **Statistic.** Per session, the amendment-01-weighted ratio of sums: `m = sum(w x quoted) / sum(w x cs)`.
  Chosen because every trading test is scored on a weighted mean net return per trade, and only the
  weighted ratio of sums makes a scalar on Corwin-Schultz reproduce the calibration legs' weighted mean quoted
  spread. A median of ratios does not, and was not chosen for that reason, not because of any number.
- **Rejection.** A session's multiplier is rejected and left unset if it has fewer than 1,000 eligible legs or
  fewer than 100 distinct days, or if it is not finite and positive. No A12-priced test may then simulate a
  leg in that session. The script runs once. Any different statistic, cap, minimum or source needs a new
  amendment.
- **Reported beside it, never used to choose it.** Legs, days, exclusions per rule, the unweighted median of
  quoted/cs, each multiplier over the frozen one, and on trades with both legs eligible the weighted mean
  quoted round trip against the frozen and the A12 spread charge.

## How it is applied

The frozen `GapperCostModel.Estimate` keeps its form. Its spread multiplier becomes, per trade,
`(m(entry session) + m(exit session)) / 2`, half the spread paid on each side. Impact, notional, floor and the
halt rule are unchanged. The exit session is only known after the exit, so an A12-priced test simulates the
trade and then re-prices it (`GapperQuotedSpread.Reprice`, net = gross - A12 round trip). The frozen ladder
decides exits on price, not on net, so re-pricing never changes an exit.

**Scope.** Only trading tests registered after this file, and only when their own amendment names
`"costModel": "A12"`. Every A01-A11 command keeps the frozen multiplier and every A01-A11 verdict stands as
reported. Nothing frozen is re-read under A12.

**Not covered.** Impact is not recalibrated. quotes-pull recorded touch size, not book depth, and the
quotes-check run's touch depth of about 0.02x of the $1,000 notional suggests impact may be understated too.
That needs depth data and its own amendment.

## Decision rules

A calibration is not a hypothesis test: no trial is logged, no family is corrected, and the kill criterion is
untouched. A12 trades nothing and changes no live template, radar weight or sell-by.

## Result (2026-11-21 05:28 ET, IP-T625)

Run once at `3a58ac7f`, the registration commit (`runs/explore/a12-calibration-20261121-1028.md`; the values are in
`data/spread-multiplier-a12.json`). Of 97,026 rows on file, 5,538 duplicates were dropped and 76,689 legs were eligible.
Excluded: 14,464 with no quote, 312 with no model spread, 19 quoted wider than 50%, 4 outside both sessions.

| session | legs (entry / exit) | days | A12 multiplier | over the frozen 9.393 | weighted mean quoted spread | weighted mean frozen charge |
|---|---|---|---|---|---|---|
| premarket | 48,191 (30,767 / 17,424) | 755 | **21.703** | 2.31x | 1.70% | 0.74% |
| regular | 28,498 (4,673 / 23,825) | 755 | **42.880** | 4.57x | 1.56% | 0.34% |

Both sessions clear the 1,000-leg and 100-day minimums and are accepted. On the 32,630 trades with both legs
eligible, the weighted mean quoted round-trip spread is 1.59%. The frozen model charged 0.54%, and A12 charges 1.55%.
The descriptive medians of quoted/cs (17.3 premarket, 37.5 regular) sit below the ratios of sums, as expected for a
right-skewed cost.

The de-duplicated quotes-check rerun (`runs/explore/quotes-check-20261121-1028.md`, on the Hub published with
`3627fcce`) changes the leg counts only: 45,744 per leg instead of 48,513. Every per-trade figure is unchanged,
because trades were already paired from one row per leg: 2.81x overall, 1.45x for exits before 09:30, 4.98x at or
after 09:30.

**In code.** `GapperSpreadMultiplier.ResolveA12` reads the file, and
`GapperQuotedSpread.Estimate`/`Reprice` (`IdiotProof.Evidence/Gappers/GapperQuotedSpread.cs`) price a trade
with the mean of its legs' session multipliers. No frozen command calls them. A later trading amendment opts in by
naming `"costModel": "A12"`, and its own explore code calls `Reprice` after simulating each trade.
