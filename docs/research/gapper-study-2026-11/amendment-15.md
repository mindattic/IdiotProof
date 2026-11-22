# Gapper study, amendment 15: foreign issuers in the cause census

Registered 2026-11-22 (IP-T632), before any A15 number existed. No foreign-issuer filing (6-K, F-1, F-3) had been
pulled into the study yet, so no count of this kind existed on any split. The machine-readable form is
`amendment-15.json`, and where the two differ the JSON governs. No earlier file is edited, and the holdout stays
locked. Descriptive and detection only: nothing here arms, sizes, places or exits an order.

## Why

Runner journal review 01 (`journal/review-01.md`, candidate C1) found 47% of the 180 runner-days from 2026-11-10 to
11-20 were foreign filers. Amendment 14 had no tag for this, because the study's EDGAR pull holds no foreign-issuer
forms. The regulator material in `methods-who-buys.md` §2 names recent foreign (mostly Asia-linked) micro-cap
listings as the dominant pump pattern. Does that show up in five years of train data, beyond the price tier those
names trade in?

## Honest prior

Foreign issuers will be over-represented on gapper days universe-wide, as subDollar was. Against matched controls,
which are matched on ADV decile and price tier, the ratio will be modest, around 1.2-1.6. Most of the effect is
probably the price and liquidity tier, as amendment 14 found for subDollar and lowPriceLowAdv.

## Definitions (frozen with this file)

- **Data.** `research gappers pull --tier edgar-dilution` (6-K, F-1, F-1/A, F-3, F-3/A rows with acceptance times),
  joined to symbols through `GapperStudyCikMap`, read-only.
- **Tag `foreignIssuer`.** A 6-K, F-1, F-1/A, F-3 or F-3/A by the symbol's CIK accepted in the 365 calendar days
  through D-1 16:00 ET. With no acceptance time, the 17:30 ET rule on the filing date applies. A symbol with no CIK
  is unknown, not false. 6-K is filed only by foreign private issuers, so the tag needs no country field.
- **Tag `fresh6K`.** A 6-K accepted from D-1 16:00 ET to 04:00 ET on D: an overnight foreign press release that
  Alpaca news may miss.
- **Population, outcomes, windows and statistics.** Exactly amendment 14's: its eligibility, G20/G50/G100, the four
  evaluation windows, day-clustered bootstrap (10,000 draws for single tags, 2,000 for combinations, seed 1337), and
  BH across this amendment's own trials. Both of amendment 14's versions are reported: the frozen headline, and the
  non-frozen sensitivity that drops split artifacts (IP-T633).

## What is reported (train only)

1. Each tag's prevalence, P(G20/G50/G100) with and without, lift with its interval, coverage, and the share of days
   with an unknown CIK.
2. `foreignIssuer` against the frozen train Tier20/50/100 events and their matched controls: the like-for-like
   test of whether it is more than price tier.
3. `foreignIssuer` paired with each of amendment 14's 15 universe tags, and in triples with amendment 14's top 10
   stable pairs: P(G20), P(G50), lift, coverage and stability, as amendment 14 defines them.
4. `fresh6K` prevalence among G20 days that have no stored Alpaca news item (amendment 14's 64%): how much of
   "no news" is news Alpaca does not carry.

Every tag and combination is a ledger trial. Nothing is promoted. Validation is not read.
