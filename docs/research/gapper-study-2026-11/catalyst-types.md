# Catalyst types: the new classification, and run size, duration and fade by type

2026-11-17, IP-T487. Registered first as gapper study amendment 08 (`amendment-08.md`; its text was pushed as
`amendment-07.md` in commits 275fbaf8 and 162ec07b before anything ran). Train split only for every outcome
number (2021-11-01 .. 2024-10-31); the holdout stays locked and was not read. Descriptive, long side only.

Run output beside this file:

- `runs/catalyst-reclassify-20261117.txt`: the classification of the stored rows (three command outputs).
- `runs/explore/catalyst-types-20261117-1951.md` and `.json`: the report by type, as the stage wrote it. Its
  header line "Weighted (amendment 01 population)" comes from the shared report header and is wrong for this
  report: nothing in it is weighted, as its next line says. The code no longer prints that line.

## 1. The type list

One type per news item or filing, assigned by rules only (headline keywords, form type, 8-K items; no model, no
LLM), plus a flag:

| type (`CatalystPreClass`) | what it is |
|---|---|
| OutsideInvestment | PIPE, private placement, strategic or equity investment, a named stake; 8-K item 3.02; SC 13D |
| AcquisitionTarget | to be acquired, take-private, tender or buyout offer for the company; SC TO-T, SC 14D9 |
| AcquisitionAcquirer | the company buys something; 8-K item 2.01 |
| Merger | merger, business combination, SPAC; form 425, merger proxies, S-4 / F-4 |
| EarningsBeat / EarningsMiss | results with a verdict word (the first verdict in the headline decides) |
| EarningsReport | results with no verdict word (year-over-year scorecards, "inline"); 8-K item 2.02 |
| Guidance | guidance raised, cut or reaffirmed, or stated as a number against the estimate |
| ContractPartnership | contract, order, award, partnership, licence, supply or distribution agreement; 8-K item 1.01 |
| FdaClinical | FDA action, clinical trial or data, designations, IND / NDA / BLA |
| ReverseSplit | reverse split, share consolidation, ADS ratio change; 8-K items 3.03 and 5.03 |
| Uplisting | approved to list or begins trading on Nasdaq / NYSE |
| ListingCompliance | deficiency or delisting notice, regained compliance; 8-K item 3.01; Form 25, Form 15 |
| Buyback | repurchase programme, self-tender; SC TO-I |
| AnalystUpgrade | upgrade, initiation, raised price target |
| OfferingDilution | public, registered direct, at-the-market or convertible offering, warrant inducement; S-1, S-3, F-1, F-3, 424B, FWP |
| ThemePivot | AI, crypto treasury, quantum, nuclear, drones, rebrand and similar |
| Other | company news of none of the types above (executive changes, product and operations updates, downgrades, lawsuits) |
| NoCatalyst | nothing happened at the company: coverage of the move or the market, movers lists, halts, a rating kept unchanged; insider and 13F / 13G ownership forms |

`NonBindingLoi` is a flag on the row (letter of intent, MOU, term sheet, "non-binding"), whatever its type; LOI
wording with no other type is a ContractPartnership.

When one item names several types, one wins by a fixed order written down in the amendment before any outcome was
computed: coverage, reverse split, target, merger, acquirer, FDA, offering, outside investment, earnings beat,
miss, guidance, report, uplisting, compliance, buyback, contract, upgrade, theme.

The old nine classes (Fda, ContractAward, ReverseSplit, OfferingDilution, Uplisting, Earnings, MA, PromoPr, Other)
are not gone: they are frozen with the gapper study, whose hypotheses, news ride and catalyst score read them. They
stay in the `PreClass` column, computed by the same rules as before (`CatalystLegacyClassifier`), and the new type
sits beside them in `CatalystType`. Re-running the frozen rules over all 226,742 research rows changed none of them.

## 2. Stored rows reclassified

| store | rows | written | old "Other" | now Other | now NoCatalyst | a listed type |
|---|---|---|---|---|---|---|
| research database, news (`CatalystEvents`) | 226,742 | 226,742 | 199,091 (87.8%) | 59,707 (26.3%) | 73,371 (32.4%) | 93,664 (41.3%) |
| permanent history, news | 227,314 | 227,314 | 199,485 (87.8%) | 59,996 (26.4%) | 73,394 (32.3%) | 93,924 (41.3%) |
| permanent history, filings | 777,069 | 777,069 | 90.7% | 195,166 (25.1%) | 498,145 (64.1%) | 83,758 (10.8%) |
| trading database, catalyst events | 0 | 0 | | | | |

- News: the share with a named catalyst type went from 12.2% to 41.3%. What is left in Other (26%) is mixed
  company news with no listed type; NoCatalyst (32%) is coverage: movers lists, market wraps, "why it is moving"
  wires that name no event, option alerts, and analyst notes that keep a rating.
- Filings had no class in the history before; "old Other" is what the old rules say about the same form and
  items. 64% are ownership forms (445,134 Form 4, 47,065 Form 3, 13G, 144, 13F), which are NoCatalyst. The 25%
  in Other is almost all 8-Ks: 187,173 of the history's 191,868 8-Ks have no item codes stored, so their event
  cannot be read from the row. An 8-K is retyped when its items are filled in.
- The first pass over the news left 45.6% in Other. Four text rules were then added, from headlines only and
  before any outcome was looked at (dated note in the amendment), which brought it to 26.3%.
- Time: 1.6 minutes for the research rows, 32 seconds for the history (17 s news, 15 s filings).
- Non-binding LOI flag: 802 research news rows, 805 history news rows.

News by type (permanent history, 227,314 items): NoCatalyst 73,394; Other 59,996; AnalystUpgrade 16,295;
EarningsReport 14,967; EarningsBeat 11,616; ContractPartnership 8,853; FdaClinical 8,802; Guidance 8,378;
EarningsMiss 6,952; ThemePivot 6,761; OfferingDilution 4,348; AcquisitionAcquirer 2,232; Merger 1,486;
Buyback 1,154; OutsideInvestment 621; ListingCompliance 603; ReverseSplit 498; AcquisitionTarget 319; Uplisting 39.

## 3. Per item: what the stock did after each kind of item (train)

247,423 items (202,535 news, 44,888 filings other than forms 3/4/5) on train sessions the study holds minute
bars for. Run = highest high from the first full minute after publication to 16:00, over the last trade before
publication. Minutes = time from that first minute to the high. Fade = the 16:00 close against that high. Kept =
the 16:00 close against the price before publication, before costs (mean with a day-clustered 95% interval).

| catalyst type | items | median run | run >= +20% | run >= +50% | median minutes to high | median fade | median kept | mean kept [95%] |
|---|---|---|---|---|---|---|---|---|
| all items | 247,423 | +3.03% | 6.4% | 1.1% | 150 | -3.39% | -0.21% | -0.25% [-0.42, -0.09] |
| buyback | 544 | +6.83% | 9.4% | 0.7% | 248 | -3.36% | +2.54% | +3.29% [+2.44, +4.14] |
| guidance change | 3,960 | +6.74% | 13.3% | 0.8% | 278 | -5.82% | +0.10% | +0.37% [-0.22, +0.97] |
| earnings beat | 6,371 | +6.15% | 11.4% | 0.6% | 315 | -4.66% | +0.77% | +1.61% [+1.15, +2.06] |
| uplisting | 14 | +5.35% | 7.1% | 0.0% | 173 | -2.75% | +0.53% | -1.01% [-5.50, +3.48] |
| FDA / clinical | 4,593 | +4.70% | 12.2% | 3.1% | 99 | -5.88% | -0.90% | -1.77% [-2.38, -1.16] |
| outside investment / PIPE | 2,962 | +4.19% | 9.7% | 2.7% | 322 | -4.64% | 0.00% | +0.22% [-0.30, +0.74] |
| earnings report (no verdict) | 19,246 | +3.93% | 7.9% | 0.7% | 264 | -4.18% | -0.11% | -0.04% [-0.40, +0.31] |
| acquisition (acquirer) | 1,317 | +3.82% | 7.4% | 2.1% | 193 | -3.63% | 0.00% | +0.14% [-0.47, +0.75] |
| listing compliance | 1,224 | +3.81% | 9.3% | 1.9% | 254 | -5.66% | -1.00% | -2.50% [-3.18, -1.83] |
| earnings miss | 3,299 | +3.55% | 7.2% | 0.5% | 297 | -6.06% | -2.61% | -2.84% [-3.47, -2.22] |
| analyst upgrade | 7,424 | +3.47% | 4.4% | 0.5% | 153 | -2.58% | +0.51% | +1.49% [+1.27, +1.72] |
| offering / dilution | 8,649 | +3.33% | 6.7% | 1.0% | 289 | -4.91% | -1.03% | -1.69% [-2.03, -1.35] |
| contract / partnership | 7,331 | +3.26% | 7.3% | 1.7% | 221 | -3.52% | -0.19% | -0.14% [-0.42, +0.15] |
| reverse split / ADS ratio | 1,497 | +3.06% | 6.9% | 1.5% | 330 | -3.58% | -0.28% | -0.93% [-1.53, -0.34] |
| no catalyst | 116,910 | +2.90% | 6.3% | 1.1% | 110 | -3.32% | -0.28% | -0.37% [-0.54, -0.20] |
| theme pivot | 2,924 | +2.75% | 4.7% | 0.8% | 98 | -3.07% | -0.25% | -0.09% [-0.54, +0.36] |
| acquisition (target) | 199 | +2.67% | 5.5% | 2.0% | 116 | -2.25% | +0.11% | +2.02% [-0.79, +4.84] |
| other company news | 58,221 | +2.49% | 4.6% | 0.9% | 175 | -2.71% | -0.13% | -0.11% [-0.27, +0.06] |
| merger | 738 | +2.12% | 7.6% | 1.9% | 101 | -2.56% | 0.00% | -0.42% [-1.31, +0.47] |
| flag: non-binding LOI (any type) | 228 | +3.83% | 14.9% | 2.6% | 66 | -6.88% | -1.80% | -3.66% [-5.58, -1.74] |

The 75th and 90th percentiles and the news / filing split of each row are in the run file.

## 4. Per gapper day: what each kind of gapper did (train)

9,537 train days that touched +20% over the prior close in the study's gapper window (2,099 symbols; 6,855 of
them also pass the study's premarket dollar-volume floor). The day's catalyst is the highest-priority type among
the stored news and filings (8-K, S-1, S-3, 424B3/4/5, SC 13D) published after the prior day's 16:00 ET and at or
before the first +20% touch, so a wire that only reports the move does not count as its cause. Run = the session
high (04:00-16:00) over the prior close. Minutes = from the first +20% touch to the high of day. Fade = the close
against the session high. Kept = the close against the prior close.

| catalyst | gapper days | share | median run | run >= +50% | run >= +100% | median minutes, touch to high | median high-of-day ET | median fade | median kept |
|---|---|---|---|---|---|---|---|---|---|
| all gapper days | 9,537 | 100% | +34.3% | 27.4% | 7.9% | 19 | 09:38 | -16.2% | +13.8% |
| no catalyst (no stored item) | 5,631 | 59.0% | +35.5% | 29.7% | 8.8% | 13 | 09:32 | -17.8% | +12.0% |
| coverage only | 1,119 | 11.7% | +35.0% | 29.8% | 9.0% | 27 | 09:48 | -17.2% | +14.0% |
| earnings beat | 684 | 7.2% | +28.8% | 8.5% | 0.7% | 56 | 09:57 | -6.5% | +19.9% |
| other company news | 518 | 5.4% | +33.6% | 28.2% | 8.9% | 19 | 09:39 | -15.5% | +14.8% |
| FDA / clinical | 290 | 3.0% | +37.7% | 31.4% | 8.6% | 21 | 08:14 | -19.4% | +10.8% |
| earnings report (no verdict) | 240 | 2.5% | +30.6% | 16.7% | 3.7% | 28 | 09:43 | -9.6% | +17.7% |
| contract / partnership | 213 | 2.2% | +38.2% | 33.8% | 8.9% | 24 | 09:06 | -18.0% | +15.8% |
| offering / dilution | 176 | 1.8% | +33.8% | 26.1% | 6.8% | 24 | 09:39 | -13.4% | +16.5% |
| analyst upgrade | 119 | 1.2% | +28.3% | 14.3% | 1.7% | 25 | 10:07 | -9.4% | +16.8% |
| outside investment / PIPE | 112 | 1.2% | +38.4% | 36.6% | 11.6% | 52 | 09:32 | -16.6% | +16.0% |
| earnings miss | 111 | 1.2% | +29.1% | 10.8% | 3.6% | 63 | 10:01 | -7.0% | +20.5% |
| guidance change | 72 | 0.8% | +33.1% | 19.4% | 4.2% | 96 | 09:53 | -9.7% | +19.1% |
| reverse split / ADS ratio | 57 | 0.6% | +38.9% | 31.6% | 7.0% | 49 | 09:47 | -18.1% | +17.2% |
| acquisition (acquirer) | 57 | 0.6% | +36.5% | 21.1% | 8.8% | 22 | 09:18 | -18.8% | +13.1% |
| theme pivot | 49 | 0.5% | +34.9% | 28.6% | 10.2% | 41 | 10:09 | -14.0% | +18.1% |
| listing compliance | 36 | 0.4% | +36.7% | 25.0% | 5.6% | 13 | 08:04 | -21.0% | +6.6% |
| merger | 29 | 0.3% | +35.1% | 37.9% | 10.3% | 6 | 09:34 | -18.7% | +14.2% |
| buyback | 15 | 0.2% | +32.3% | 26.7% | 0.0% | 91 | 09:51 | -11.9% | +22.5% |
| acquisition (target) | 8 | 0.1% | +37.5% | 25.0% | 25.0% | 19 | 10:12 | -2.9% | +31.5% |
| uplisting | 1 | 0.0% | +36.2% | 0% | 0% | 244 | 08:04 | -16.8% | +13.4% |
| flag: non-binding LOI (any type) | 17 | 0.2% | +37.2% | 35.3% | 11.8% | 9 | 09:32 | -25.0% | +11.0% |

## 5. What this says

- **Most gappers have no catalyst we store.** 59% of train gapper days had no news item or filing between the
  prior close and the first +20% touch, and another 12% had only coverage. 24% had an item of a listed type.
  Earnings beats are the largest named group (7%), then FDA / clinical (3%).
- **The types split into two shapes.** Earnings gappers (beat, miss, report, guidance) and analyst upgrades run
  less far (median high +28% to +33%, 8% to 19% reach +50%), top later (beats and misses about an hour after the
  +20% touch, around 10:00 ET) and give back the least (median fade -6% to -10%). FDA / clinical, contract, listing
  compliance and no-catalyst gappers run further (median +35% to +38%, about 30% reach +50%), top early (FDA and
  compliance at about 08:00 ET, no-catalyst 13 minutes after the touch) and give back the most (-18% to -21%).
- **Outside investment / PIPE days have the biggest tail** of the groups with at least 100 days: 36.6% reach +50%
  and 11.6% reach +100%, with a median 52 minutes from the touch to the high.
- **Per item, only three types leave a long ahead at the close on average, before costs:** buybacks (mean kept
  +3.3%), earnings beats (+1.6%) and analyst upgrades (+1.5%), each with an interval above zero. FDA items have
  the fastest and fattest runs (12% run +20% or more, 3% run +50% or more, the high a median 99 minutes after the
  item) but lose on average to the close (-1.8%). Earnings misses, offerings, listing-compliance items and
  LOI-flagged items lose.
- **A non-binding LOI runs and then fails.** Flagged items run +20% or more 15% of the time, top in about an
  hour and fade a median -6.9%; their mean kept is -3.7% [-5.6, -1.7]. The 17 gapper days driven by one faded a
  median -25%. Small samples (228 items, 17 days), consistent with the case studies.

## 6. What this does not say

- Nothing here is a trade. Section 3's "kept" is before costs, and the study's cost model takes several per cent
  round trip on these stocks; section 4 is conditional on the day having already become a gapper, which is not
  known in advance. A type that looks worth trading goes through exploration with costs, the ledger, a registered
  amendment, validation and the holdout like every other idea. 41 ledger trials were logged for this look
  (ids 5614-5654).
- Section 3's levels are higher than the market's: the sessions are the ones the study holds minute bars for
  (gapper candidate days, their neighbours, controls and the population sample). Compare types with each other.
- Small groups are noise: acquisition target (8 days), uplisting (1), buyback (15), merger (29), LOI (17).
- The classifier is rules on headlines. Its accuracy was not measured against a hand-labelled set; the rules
  were checked against the IP-T488 topic clusters and by reading samples. "No catalyst" means none stored in
  Alpaca / Benzinga news or the EDGAR index, not that nothing happened.
- Validation and the holdout were not read for any outcome. Classification is stored for rows of every date,
  which reads no price.
