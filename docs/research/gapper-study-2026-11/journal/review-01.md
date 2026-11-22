# Runner journal review 01: 9 sessions, 2026-11-10 .. 2026-11-20 (IP-T632)

Case study (opened window, not a test), per the journal README. Input: `review-2026-11-10_2026-11-20.generated.md`
(180 ranked runner-days, the top 20 per session). Read beside the 5-year cause census
(amendment 14, `../runs/explore/a14-cause-census-20261122-0259.md`): every pattern below is checked against train
before it is called a pattern. Nothing here changes a strategy. Times ET.

**Data caveat first.** Sessions 11-10 .. 11-18 were journaled on 2026-11-21, a Saturday. The radar refuses to run
outside the database online window, so those rows have **no radar rank**: their "0 on radar" is missing data, not a
miss. Paper fills exist only from 11-18 on, because the order list holds the latest 500. Radar and our-P/L
statements therefore rest on 11-18 .. 11-20 only. They can be refilled with
`tools/gapper-journal.ps1 -Date D -Force -ReuseCaseStudy` on a weekday. Doing that loses any hand-written
post-mortem, and there is none yet for these dates.

## Patterns seen in 3+ sessions, and what 5 years of train says

| # | Pattern in the 9 sessions | Share of runners | Train (A14) agrees? |
|---|---|---|---|
| P1 | A prior top-mover day in the window ("it already ran") | 47% | **Yes, strongly.** ranYesterday lift 14.7, ranLast5 7.9, bigRunner60 5.4; together 56% of G20 and ~72% of G50/G100 days. |
| P2 | No catalyst found | 46% | **Yes.** 64% of train G20 days have no stored news item from D-5 to 04:00 on D. Fresh news has lift 6.6 but is on only 9% of G20 days. |
| P3 | Moved before a fresh catalyst was published | 7% | **Consistent.** H19: 31-33% of the hour's move precedes the headline, but a full +20% before publication is rare. "Somebody knew" is not the common case. |
| P4 | Shares outstanding under 10M | 43% | **Partly.** tinyShares lift 3.2, ratio 2.0-2.6 against matched controls (XBRL known for 69% of days). |
| P5 | Reverse split in the last 60 sessions | 21% | **Yes.** reverseSplit90 lift 5.5, ratio 3-4 against controls. |
| P6 | Dilution filing in the last 90 days | 38% | **Weakly.** Lift 2.2, ratio 1.5-1.9. It is on 19% of all small-cap days. |
| P7 | Foreign filer | 47% | **Not tested.** No foreign-filer tag in A14 (6-K/20-F/F-1 filers). Candidate C1. |
| P8 | Late runner: first +20% after 09:30 | 42% | **Yes.** Amendment 09's move-start signal passed train and validation. Both rides off it failed after costs (A10, A11). |
| P9 | High of day in the post-market | 16% | **Not tested on train.** The after-hours radar sign is IP-T501. |
| P10 | Fade: median last print 9-27% under the high | nearly all | **Yes.** Every long rule tested loses 2-8%/trade after costs, and the evening-list overnight hold loses too (A13). |

## What this says about "why they gap"

1. **The best single answer is reflexive: they gap because they just gapped.** Stocks that ran in the last 1-5
   sessions or crashed in the last 5, closed strong on a volume surge, are under SSR, or had a recent reverse
   split are 5-15x more likely to run, and combinations reach 22-25% for +20% and 6-9% for +50%. This is the
   scanner/herding mechanism plus the reverse-split float mechanism (`methods-who-buys.md` §3-4). Neither needs a
   secret.
2. **News explains a minority.** Most runners in both the live window and 5 years of train have no news before
   they run. Where news exists it usually lands before the move, not after.
3. **Predictable is not tradable.** The same conditions that make a run likely make the fade likely. Every
   tested way to be long one (premarket ride, news ride, ignition ride, late-runner ride, overnight hold) loses
   after costs on train.

## Candidates for the next registered amendments (not registered here)

- **C1. Foreign-filer tag (P7).** Add `foreignFiler` (any 20-F, 40-F, 6-K or F-1/F-3 by the CIK in the prior
  365 days, from `EdgarFilingIndex` plus the edgar-dilution pull) to the census as a new trial set. Cheapest
  question here. Data mostly held. 6-K coverage needs `research gappers pull --tier edgar-dilution`.
- **C2. The recipes as a watch-only alert.** Feed the top A14 recipes (for example ranLast5 + volumeSurge +
  subDollar, P(G50) 9.3%) into the evening radar as a scored group and let the runner journal measure forward
  hit rates. No order, like A09's move start in IP-T628. This needs owner approval (it changes the radar).
- **C3. Repair the split table first (IP-T633)** before any further daily-bar test. A13 showed an unrecorded
  reverse split can manufacture a +9,990% "return".
- **Not recommended:** another long entry or exit on recipe names. Ten tested variants have all failed for the
  same reason (P10).
