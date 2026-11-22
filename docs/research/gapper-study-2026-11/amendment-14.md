# Gapper study, amendment 14: the cause census (what was true the evening before a gapper day)

Registered 2026-11-21 (IP-T632), before any A14 number existed: no tag below had been counted on any split and
the script that counts them (`tools/gapper-study/a14_cause_census.py`) had not been written. The
machine-readable form is `amendment-14.json`; where the two differ, the JSON governs. No earlier file is
edited, no frozen hypothesis, threshold or verdict changes, and the holdout stays locked. Detection and
description only: no trade is simulated, and nothing here arms, sizes, places or exits an order.

## Why

The owner's question is "why do they gap". Amendment 08's census answered it only by stored news: 59% of train
gapper days have **no stored item** and 12% have coverage only, so about 70% of gapper days have no named
cause. `methods-who-buys.md` ranked "tag every gapper by mechanism, by rule, not by fit" as the first thing to
test; it was never run. The feature-block ablation (`runs/explore/models-20261109-1619.md`) says which blocks
help a model at 08:00, not which plain, nameable conditions precede a gapper or how often each one is followed
by one. This amendment counts them.

## Question

Which conditions that are knowable the evening before (the D-1 close) are over-represented before a gapper
day, how much does each raise the odds, which combinations raise them most, and how many gapper days does no
condition explain?

## Honest prior

The census will mostly restate what the model already knows in plainer words: yesterday's runners run again,
recent reverse splits and tiny floats help, and the largest single group stays "nothing we can name". Lifts of
5-25x on base rates near 0.5% still mean a few percent absolute, so no single tag will be a recipe. The useful
output is the ranking, the combinations, and the size of the unexplained remainder.

## Definitions (frozen with this file)

- **Day and outcome.** A symbol-day D eligible at the D-1 close by amendment 13's eligibility rule (the frozen
  universe numbers from daily bars), D in train (2021-11-01..2024-10-31). Outcome **G20** = the D daily high is
  at least +20% over the split-adjusted D-1 close; **G50** and **G100** likewise. Daily bars are regular hours,
  so a premarket-only spike is not counted: that is a stated difference from the study's own tiers, chosen
  because it lets every eligible symbol-day be scored with no candidate selection. The share of the study's
  train Tier20 events that G20 recovers is reported.
- **Tags, each from data dated at or before the D-1 close:**
  - `ranYesterday`: D-1 high at least +20% over the D-2 close.
  - `ranLast5`: any of D-5..D-2 with a high at least +20% over its prior close.
  - `bigRunner60`: any of D-60..D-6 with a high at least +50% over its prior close.
  - `closedStrong`: D-1 close at least +10% over the D-2 close and in the top quarter of the D-1 range.
  - `volumeSurge`: D-1 volume at least 5x the 20-session average.
  - `reverseSplit90`: a reverse split with an ex-date in the 90 calendar days through D-1 (and outside the
    frozen exclusion window, which already removes the 25 days before D).
  - `newListing180`: first daily bar within 180 calendar days of D.
  - `subDollar`: D-1 close below $1.00.
  - `lowPriceLowAdv`: D-1 close below $5 and 20-session dollar volume below $2,000,000.
  - `ssrD1`: D-1 low at or below 0.9 x the D-2 close (Rule 201 in force on D).
  - `crashedLast5`: any close-to-close drop of 30% or more in D-5..D-1.
  - `hotTape`: the count of eligible symbols with `ranYesterday` on that date is in the top quarter of train
    dates up to then (an expanding quantile, so nothing later is read).
  - `dilutionFiling90`: an S-1, S-3, 424B3, 424B4 or 424B5 for the symbol's CIK accepted in the 90 days through
    D-1 16:00 ET (`EdgarFilingIndex`; symbols without a CIK are reported as unknown, not as false).
  - `eightK5`: an 8-K or 8-K/A accepted in the 5 calendar days through D-1 16:00 ET.
  - `insiderBuy30`: a Form 4 open-market purchase (code P) in the 30 days through D-1 (`InsiderTransactions`).
  - `tinyShares`: latest XBRL shares outstanding filed before D-1 under 10,000,000, where the float import
    holds one (unknown otherwise).
- **News tags are not universe-wide.** Stored news exists only around candidate and sample days, so a news
  tag over all eligible days would be biased toward days that ran. `freshNews` (a non-coverage stored item
  after D-1 16:00 and before 04:00 D) and `staleNews5` (one in D-5..D-1 16:00 only) are therefore reported
  only on the study's candidate days plus amendment 01's sample, with its weights, in their own table.

## What is reported (train only, rolling by the explore plan's four evaluation windows)

1. **Per tag:** eligible days carrying it, P(G20), P(G50), P(G100) with the tag and without, the lift with a
   day-clustered 95% interval, and the share of all G20/G50/G100 days that carry it (coverage).
2. **Combinations ("recipes"):** every pair and triple of tags with at least 500 eligible days in train. For
   each: P(G20), P(G50), lift, coverage, and the same in each of the four evaluation windows. A combination is
   listed as **stable** only if its lift's lower bound is above 2 overall and its P(G20) is above the base rate
   in all four windows. Benjamini-Hochberg q < 0.05 across every tag and combination tested (one-sided test of
   P(G20 | combination) above the base rate, day-clustered).
3. **The unexplained remainder:** G20, G50 and G100 days carrying none of the tags, and the exclusive
   hierarchy (first matching tag in the order above) so the shares add to 100%.
4. **Against the study's own events:** the same tag prevalence on the frozen train Tier20/50/100 event days
   versus their matched controls.
5. **The news table** described above.

Every tag and combination tested is a ledger trial. Nothing is promoted from this amendment: a stable
combination is a candidate for a later detection or trading amendment, registered before its own numbers.

## Decision rules

Descriptive: no pass or fail, no kill criterion, no correction shared with A01-A13. Validation is not read.
If the unexplained remainder of G50 days is above 30%, the next data ticket is the one that could shrink it
(sympathy groups by SIC code, a promotion-list or social feed, 6-K coverage), named in the result.

## Train result (2026-11-22, IP-T632)

Full report: `runs/explore/a14-cause-census-20261122-0259.md`. The base rates over 1,663,526 eligible
symbol-days are P(G20) 0.96%, P(G50) 0.15% and P(G100) 0.05%.

- **"It already ran" dominates.** The first three steps of the exclusive hierarchy are ranYesterday (lift 14.7),
  ranLast5 (7.9) and bigRunner60 (5.4). Together they hold 56% of G20 days and 71-73% of G50 and G100 days. Next
  come crashedLast5 (11.5), volumeSurge (7.5), closedStrong (7.2), ssrD1 (5.9) and reverseSplit90 (5.5).
  Against matched controls, subDollar, lowPriceLowAdv and hotTape do not separate gapper days at all (ratio 1.0):
  their lift is price-tier selection.
- **The best recipes reach P(G20) 22-25% and P(G50) 6-9%**, a lift of 22-26 on G20 and up to 61 on G50. Top
  by G50: ranLast5 + volumeSurge + subDollar (776 days, P(G50) 9.3%, P(G100) 3.7%). Each recipe covers only
  1-4% of G20 days. 407 of the 409 combinations pass "stable", so the flag does not discriminate.
- **Unexplained remainder.** No tag at all: 6.8% / 2.8% / 2.2% of G20 / G50 / G100 days, against 37% of all
  days. With the four broad tags left out (post hoc), it is 24% / 11% / 10%, against 79% of days. That is under
  the 30% follow-up trigger, so no data ticket is named.
- **News and insiders.** Fresh overnight news has lift 6.6 but is on only 9% of G20 days. 64% of G20 days have
  no stored news item from D-5 to 04:00 on D. Form 4 purchases have no lift (1.0).
- **Split artifacts (IP-T633).** 16 of the 20 largest D-high ratios are unrecorded reverse splits. A non-frozen
  sensitivity drops 70 such days, all G100; it changes no ranking and moves no headline by more than a few
  tenths of a point.
- **Deviations.** insiderBuy30 is in the stratified table only, because Form 4 detail exists only around candidate
  and sample days. hotTape uses strictly above the expanding 75th percentile; the threshold was changed once,
  before any result was read. The script reads read-only CSV exports. Its 852 trials, together with A13's 9, went
  into the ledger as ids 5668-6528.

A G20 high-water mark is not a trade: a name that ran yesterday swings 20% intraday today in both directions.
Nothing here is a strategy until a later amendment tests a recipe with entries, exits and the A12 cost.
