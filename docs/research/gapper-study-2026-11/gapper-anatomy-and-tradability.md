# Gappers: how they form, how traders find them, when to buy, and whether Alpaca can trade them (IP-T494)

Written 2026-11-10 01:00 ET. It answers the user's goal of the same day: *figure out how gappers are created, how
people find out about them, when to buy and when to ignore, validate all gappers, and one simple ask: is it tradable
on Alpaca, because if it isn't I don't care.*

Most of the first four answers already exist in this study. This note collects them in one place. It rechecks the
long-rule verdict against the trial ledger itself (§3). New here are the validation of every permitted gapper day
against independently pulled Alpaca bars (§5b) and the tradability check (§5). Both are descriptions, not tests.

**What "all gappers" covers.** It covers every gapper day the study's universe labels, except the locked holdout:

| part | days |
|---|---|
| train | 6,855 |
| validation | 5,679 |
| the 2024-11-01 boundary day | 16 |
| every Tier20+ day in the window amendment 05 opened (2026-09-10..10-30) | 741 |
| total | **13,291** |

The 210 case-study top-5 days (2026-09-10..11-09) are added on top, for **13,501 rows**. The locked holdout
(2025-11-03..2026-09-09, 5,139 gapper days) is **deliberately not read**. Its labels and prices stay sealed under the
frozen protocol and amendment 05 until the single registered holdout run. Validating them now would spend the
holdout. Lifting that lock is the user's decision, and this pass does not make it.

Data: [`runs/alpaca-tradability-20261110.csv`](runs/alpaca-tradability-20261110.csv) has one row per gapper day with:

- the bar check (§5b): verdict, Alpaca-measured gain, prior session used;
- Alpaca's current asset status (§5).

Sources, all from Alpaca, pulled 2026-11-10 00:47-02:30 ET:

- the live account's `/v2/assets?asset_class=us_equity`, active and inactive (14,413 + 19,152 assets);
- 2,305,428 split-adjusted SIP daily bars for every gapper symbol over 2021-10-15..2025-10-31 and
  2026-09-10..2026-11-09 (holdout dates not requested);
- 04:00-09:30 ET SIP minute bars for the 2,475 premarket-type days the daily bars could not settle.

## The short answer

1. **How gappers are created.** There are four mechanisms, and none needs a secret: calendared reverse splits and
   ADS ratio changes, coordinated promotion of tiny (mostly Asia-linked) Nasdaq listings, real company news (some of
   it leaked early), and herding on top-gainer lists. The typical recent +300% runner had **no rated catalyst**. It
   was a sub-$3 stock, often foreign-filed, with a float of a few million shares. It usually had a recent reverse
   split and a dilution filing, and it had already been a top mover in the past month.
2. **How people find out.** Mostly the same screens anyone can run: yesterday's top movers, scanners, reverse-split
   calendars, and the price move itself. A 16:00 ET watchlist built the evening before ranks 55% of next-day +100%
   runners into its top 50. The premarket R4 alarm leads a +20% move by a median of 67 minutes.
3. **When to buy.** On the evidence so far, **never on these names as a long**.
   - The ledger holds 4,424 long trials, recounted for this note (§3). 28 have a positive pooled net and 4 are
     positive in every fold. None survives the frozen cost or deflation: the best deflated Sharpe is 0.000. The
     pre-registered long strategies all lose on validation.
   - Detection is good. Making money from it is not.
4. **When to ignore.** Ignore every gapper on this list as a long. The one exception tested so far is a slow earnings
   beat, which drifts up into D+2 but is not significant after deflation. Ignore anything Alpaca will not trade
   (§5). It is also worth noting that the bigger the run, the more often the stock is delisted later.
5. **Validate all gappers.** Of 13,501 gapper days, **13,410 (99.3%) are confirmed** by bars pulled fresh from
   Alpaca: +20% or more over the prior close at the open or high, or at the premarket minute high. The rest:
   - 64 reach only +10-20% on Alpaca's bars;
   - 8 are not confirmed and look like data errors, among them FLG on 2024-07-12, a 1:3 reverse-split day labelled
     +224%;
   - 19 cannot be checked, because their prior close falls inside the locked holdout (§5b).
6. **Is it tradable on Alpaca?** **Yes for 89% of permitted gapper days, 98% of opened-window gappers and 97% of
   recent top runners, judged by Alpaca's status today.**
   - **Alpaca blocks a specific group of hot names that still trade on Nasdaq**: OMH, HOLO, MLGO, NXTT, WIMI, TOP,
     JZ, CHSN, JBDI and about 20 more. These are exactly the Asia-linked promotion names that mechanism 2 describes,
     and OMH is the study's flagship example.
   - The live code did not check for this. It does now (§6, IP-T495).
   - Status on each gapper's own day cannot be recovered for past days. Alpaca keeps no history of it, and the
     account's own orders prove it for only one gapper day. A daily snapshot now records it for every future day
     (§5c).

## 1. How gappers are created

From [`methods-who-buys.md`](methods-who-buys.md) (IP-T489) and the case studies (IP-T486):

| mechanism | what happens | footprint |
|---|---|---|
| Calendared mechanics | A reverse split or ADS ratio change. The date is public days ahead (Nasdaq requires notice 10 days before and a public announcement 2 business days before). | Split-day runs of +100% happen on 1.8% of effective days and 2.2% of D+1s, 8-42x the base rate. Ratios above 25:1 run on 6.3% of D+1s. They fade 6-14% by the close (IP-T490). |
| Coordinated promotion | Tiny recent IPOs, mostly Asia-linked. Buyers are told when to buy by holders who sell into them. | Regulators name this as the dominant pattern. The SEC has suspended about a dozen such Nasdaq microcaps since late September 2025. |
| Real news, sometimes leaked | Acquisitions, investments, FDA decisions, earnings. | 37-43% of news events had already run 5% or more before publication. Real news runs less far than squeezes do. |
| Herding on rank lists | A rise puts the stock on top-gainer lists and scanners, which brings in more buyers. | A +100% day in the last 10 sessions makes another one 25x more likely. 7.1% of +100% runners run +100% again within 5 sessions (IP-T484). |

The recent window (2026-09-10..11-09, 210 top-5 stock-days, IP-T486):

- 29% had no news or filing, and 23% had only a filing.
- 26% ran within 60 sessions of a reverse split.
- 55% had a dilution filing in the prior 90 days.
- 60% of known floats were under 10M shares, and 48% are foreign filers.
- 74% of the run was gone by D+2.

## 2. How people find out

| signal | how early | quality |
|---|---|---|
| D-1 16:00 watchlist (boosted, train) | The evening before | Recall of +100% runners is 25/38/55/74% at the top 10/20/50/100. Precision is only 2.6/2.2/1.4/1.0%. Its strongest inputs are a recent +100% day, a D-1 top-10 move, D-1 dollar volume 5x or more, a halt in the last 5 sessions, and a reverse split 11-90 days ago (IP-T484). |
| Premarket volume and velocity (H11) | Before +20% | Validation AUC 0.870 against 0.549 for a news-only benchmark (IP-T473). |
| R4 alarm (amendment 04) | Median 67 minutes before +20% | It catches 36% of future +20% movers, against 27% for gainers plus news. It gives 0.48 false alarms per 100 mornings, which fails its own 0.10 cap (IP-T475). |
| Ignition (volume burst) | A median of 96 minutes before the high | It catches 39% of runners on the top 50, with 16 false ignitions per runner caught (IP-T484). |

Detection carries real information. The study's problem has never been finding gappers.

## 3. When to buy

**Ledger recheck (this note).** This recount reads `runs/explore/ledger.jsonl` directly instead of the commit
messages:

- 5,613 trials in all. Of them, 4,424 are long rules scored on mean net return (all shorts and two-sided rows
  excluded).
- **28 have a positive pooled net**. 21 are in the catalyst tracks: 12 catalyst-ml trades, 6 hard-catalyst opens
  and 3 catalyst splits. The other 7 are ORB cells. Every alarm-ride, watchlist-ride, split-day, entry and
  exit-route trial is negative.
- **4 are positive in every fold**: the earnings-beat open-to-D+2 hold (ledger 5563) and three analyst-initiation
  holds (5609, 5610, 5613). All four turn negative at the frozen cost (-0.76% to -1.79%), and every one with a
  recorded DSR has DSR 0.000.

The pre-registered long strategies were tested on validation and lose (H12, H12p, H18 below).

Every long tested loses money. Each line below is train-only exploration unless it says validation.

- **Pre-registered strategies (validation):** H12 is -3.45%/trade, H12p is -3.93%, and the H18 news ride is -2.36%.
  The round trip costs about 3.4-4.6% (IP-T473).
- **Riding the R4 alarm:** 1,320 configurations. The best gross is +0.29% and the best net is -1.65%
  [-2.29, -1.02] (IP-T483).
- **Riding the watchlist ignition:** all 2,592 configurations lose, before costs included (IP-T484).
- **Split-day calendar rides:** 147 configurations, none positive net (IP-T490).
- **Opening-range breakout, $5-$20 stocks in play:** net +0.04% [-0.04, +0.13], which is noise (IP-T492).
- **Catalyst clusters:** the best is analyst initiation, held to D+2, at +1.50% [+0.65, +2.35] net. It is -0.76% at
  the frozen cost and has a DSR of 0.000 (IP-T488).
- **Slow earnings beat:** buying at the 09:30 open and holding to the D+2 close gives +0.79% [+0.20, +1.39] net at
  regular-hours cost. The ledger shows it positive in **all 4 folds** (+1.63/+0.32/+0.79/+0.13%); the IP-T492 commit
  message said 3 of 4. It is -1.06% at the frozen cost. This is the closest thing to a candidate. It is a swing hold,
  not a premarket gapper ride (IP-T492, ledger 5563).

Why the rides lose: the high comes after 09:46 on 40-45% of days. Velocity dies 35-42 minutes before the high. The
spread on these names eats 2-4% per round trip.

The only strong edge is the **fade short** (+8.16%/trade on train, IP-T476). It is out of scope: the user trades
long only. It is also mostly impossible on Alpaca. Of the gapper symbols Alpaca trades today, 52% are not
shortable, and every blocked name below is unshortable.

## 4. When to ignore

Ignore a gapper when:

- **Alpaca will not trade it** (§5). This check comes first and is free.
- **It is a long entry on a squeeze profile:** no rated catalyst, a reverse split in the last 60 sessions, a dilution
  filing on record, and a foreign filer. This profile produces the biggest runs and the worst longs. 74% of the run
  is gone by D+2.
- **It is FDA/clinical news.** This is the worst catalyst class as a long, at -0.8 to -1.1% at every intraday exit
  and -4.7% on the watchlist (IP-T492).
- **It is already +50% at the first bar.** That is 17% of runners. The cost on these names runs above any remaining
  edge.

## 5. Is it tradable on Alpaca? (new, IP-T494)

### Method

For every symbol, the classification uses Alpaca's current asset record. Active rows are preferred over inactive
ones.

| status | meaning |
|---|---|
| `tradable` | Active, `tradable: true` |
| `listed-but-alpaca-blocked` | Active on NASDAQ/NYSE, `tradable: false` |
| `moved-to-otc` | Active but now on OTC, so Alpaca will not trade it |
| `delisted` | Inactive |
| `not-in-alpaca` | No asset record |

**Limit:** Alpaca publishes only today's status. Every study gapper day had SIP minute bars, so the stock was
exchange-listed that day. A name that has since moved to OTC was very likely tradable on its gapper day. A
`listed-but-alpaca-blocked` name may or may not have been blocked on the day it ran. Alpaca keeps no history of its
blocks, so the only way to know going forward is to record them daily (§7).

### Results

| set | days | tradable now | blocked by Alpaca | moved to OTC | delisted | not in Alpaca |
|---|---|---|---|---|---|---|
| **All permitted study gapper days** | **13,291** | **11,830 (89.0%)** | **289 (2.2%)** | **1,027 (7.7%)** | **108 (0.8%)** | **37 (0.3%)** |
| train | 6,855 | 5,814 (84.8%) | 187 (2.7%) | 751 (11.0%) | 81 (1.2%) | 22 (0.3%) |
| validation | 5,679 | 5,276 (92.9%) | 92 (1.6%) | 271 (4.8%) | 27 (0.5%) | 13 (0.2%) |
| 2024-11-01 boundary day | 16 | 15 | 0 | 1 | 0 | 0 |
| opened window, every gapper, 2026-09-10..10-30 | 741 | 725 (97.8%) | 10 (1.3%) | 4 (0.5%) | 0 | 2 (0.3%) |
| train + validation, Tier20 | 10,034 | 89.3% | 2.2% | 7.4% | 0.8% | 0.3% |
| train + validation, Tier50 | 1,749 | 84.8% | 2.4% | 11.1% | 1.4% | 0.3% |
| train + validation, Tier100 | 751 | 85.5% | 1.7% | 11.6% | 0.7% | 0.5% |
| Case studies, top 5, 2026-09-10..11-09 | 210 | 203 (96.7%) | 5 (2.4%) | 2 (1.0%) | 0 | 0 |

The older the gapper day, the more often the name has since dropped to OTC (11.0% of train days against 4.8% of
validation days and 0.5% of the opened window). That is the listing attrition this table measures, not a change in
what Alpaca trades.

### Findings

1. **For gappers that were exchange-listed on the day, Alpaca tradability is close to complete.** The exceptions
   are a short, specific list.
2. **Alpaca blocks 29 symbols that still trade on Nasdaq.** On 2026-11-10 they are marked `tradable: false` and
   `shortable: false` on both the live and paper accounts. They are not halted: on 2026-11-09 OMH traded 31.1M
   shares and printed until 20:00 ET, and HOLO, JZ, CHSN, JBDI, MLGO, NXTT, TOP and WIMI all traded too.
   - By gapper days in train + validation: HOLO 42, NXTT 29, MLGO 26, EJH 25, WIMI 18, GDC 14, AKAN 13, PWM 13,
     TOP 11, ILAG 10, and 19 more.
   - In the opened window's gapper days: AKAN, GDC, JZ and NXTT.
   - Among the recent top-5 runners: JZ (10-08), JBDI (10-13), CHSN (11-03) and **OMH (11-04 and 11-09)**.
   - Nearly all are Asia-linked recent Nasdaq listings: the promotion group of §1. Alpaca has not published a
     reason. The SEC suspended about a dozen Asia-based Nasdaq microcaps from late September 2025 for suspected
     manipulation, and a broker-side block on the same group fits that pattern. That reading is an inference, not
     confirmed.
3. **The bigger the run, the more likely the stock later drops to OTC or is delisted:** 8.2% after Tier20, 12.5%
   after Tier50, and 12.3% after Tier100. A big run is often the last thing the listing does.
4. **Ticker reuse:** 188 study days have a symbol that maps to more than one Alpaca asset. Their status is the
   current company's, so treat them as uncertain.
5. **The six symbols not in Alpaca** are EZRA, INM, ACHV, PSQH, BWMN and SWAGW. They are probably renamed or merged.

### What it changes in the study

Very little. Removing the 2-11% of days that Alpaca cannot trade today cannot turn rules that lose before costs
into winners. It does change the recent case studies' poster child. **OMH, the +524% and +1,068% example, cannot
be bought on Alpaca today.**

## 5b. Validation of every gapper day (IP-T496)

**Question:** is each labelled gapper day real? The test is that the stock reached +20% or more over the prior
session's close that day, measured on bars pulled fresh from Alpaca rather than on the study's stored panel.

**Method:**

- **Prices.** Split-adjusted SIP daily bars as Alpaca serves them today. They carry adjustments for splits that
  happened after the study's pull.
- **Daily-bar gain.** max(open, high) / prior close - 1, where the prior close is the previous daily bar for the
  same symbol.
- **Premarket check.** Alpaca's daily bars cover regular hours. A premarket-type gapper whose daily bar fell short was
  re-measured on 04:00-09:30 ET minute bars, as premarket high / the same prior close - 1.
- **Holdout.** No holdout date was requested. For the 19 gapper days on 2026-09-10, the prior session is 2026-09-09,
  inside the locked holdout, so those days are left unchecked.

**Results (13,501 rows):**

| verdict | days | share |
|---|---|---|
| confirmed on the daily bar (open or high ≥ +20%) | 11,007 | 81.5% |
| confirmed on premarket minute bars (premarket high ≥ +20%) | 2,403 | 17.8% |
| **confirmed, total** | **13,410** | **99.3%** |
| partial: +10% to +20% on Alpaca's bars | 64 | 0.5% |
| not confirmed: under +10% | 8 | 0.06% |
| not checkable: prior close inside the locked holdout (2026-09-10) | 19 | 0.1% |

By gapper type:

- **open-type** (reached +20% from the open): 6,185 of 6,218 confirmed on the daily bar alone. The remaining 33 are
  24 partial, 1 not confirmed and 8 unchecked.
- **premarket-type:** 4,612 confirmed on the daily bar and 2,403 more on premarket minute bars, of 7,073. The
  remaining 58 are 40 partial, 7 not confirmed and 11 unchecked.
- **case-study top 5:** all 210 confirmed.

The 64 partial days have a median Alpaca gain of +18.4% and a minimum of +10.2%. They are borderline days where the
study's prior close or high differs slightly from Alpaca's current adjusted series.

The **8 not confirmed** are likely false gappers in the study's labels:

| day | symbol | study gain | Alpaca gain | note |
|---|---|---|---|---|
| 2024-07-12 | FLG | +224% (Tier100) | +7.9% | Flagstar's July 2024 1:3 reverse split; the study's split guard missed it |
| 2024-11-11 | RSLS | +96% (Tier50) | +7.1% | now delisted; probably a split artifact |
| 2022-08-11 | LIFE | +45% | +3.8% | premarket print not in Alpaca's adjusted minute bars |
| 2022-01-04 | JAN | +24% | +1.9% | same |
| 2022-01-04 | GNPX | +20.0% | -10.3% | exactly +20.0%: a single premarket print |
| 2023-05-03 | IMMX | +20.0% | -2.8% | same |
| 2024-11-26 | RGTI | +20.0% | -2.6% | same |
| 2025-08-27 | OTLK | +20.0% | +3.3% | same |

All eight are train or validation days. That is 0.06% of the labels, too few to move any study verdict. The
FLG and RSLS split misses belong in the split guard's to-do list.

## 5c. Tradability on the gapper's own day

Alpaca's assets endpoint has no history. A past day's status can only be proven by an order placed that day.

- **Orders checked:** the account's full order history, 148 live orders from 2026-07-16 and 25 paper orders from
  2026-07-20. No order was ever rejected.
- **What they prove:** one order lands on a permitted gapper day. **CLRO on 2026-11-02** filled on the live account,
  so CLRO was tradable that day. No other past day can be proven either way.
- **From now on:** the scheduled task `IdiotProof\AlpacaAssetsSnapshot` runs
  [`tools/gapper-tradability/snapshot_assets.py`](../../../tools/gapper-tradability/snapshot_assets.py) every day at
  03:30 ET, before premarket. It saves the full active and inactive asset lists to `D:/IdiotProofData/alpaca-assets/`.
  So every gapper from 2026-11-10 on has its Alpaca status recorded on its own morning.
- **Snapshots so far:** 2026-11-10 00:47 ET and 02:00 ET. The 02:00 ET snapshot counts 37 listed names Alpaca will
  not trade, against 29 among gapper symbols.

## 6. Live-code gap: the system never checks Alpaca tradability for gapper candidates

- **The formation universe.** `FormationUniverse.Build` (`IdiotProof.Core/Monitoring/Formation/FormationUniverse.cs`)
  drops a tracked ticker marked `!IsTradable`. But it adds every **mover**, **most-active** and **catalyst** symbol
  with no tradability check. OMH would have entered on 11-09 as a mover.
- **The ticker cache.** `TickerUniverseService.RefreshAsync` writes only tradable NASDAQ/NYSE rows. That causes
  three problems:
  - It never sets `IsTradable = false` on a name Alpaca later blocks.
  - It drops NYSE American (AMEX). 21 of the 210 recent runners (10%) are AMEX, and Alpaca trades all of them.
  - The live `TrackedTickers` table was last refreshed **2026-07-26**. The only refresh caller is the research scan
    pass, which has not run since, so the cache is 3.5 months stale.
- **AutoGapperScanner and gapper arm** do not check `tradable` either.
- **No harm today.** The formation recorder and the auto-gapper are both off on this Hub (2026-11-10 status). An
  armed blocked name would be rejected by Alpaca at order time, not filled. The gate should still sit at candidate
  time, so a blocked name never takes a slot.

**Fixed in IP-T495 (code only, not published to the Hub):**

- A new `AlpacaTradableAssets` (`IdiotProof.Core/Services`) builds the set of active, tradable symbols from one
  `/v2/assets` call and caches it for 6 hours. If the call fails it filters nothing, since Alpaca still rejects the
  order.
- `FormationUniverseBuilder` drops untradable tracked, catalyst, mover and most-active names before the cap is
  applied, and its note lists what it dropped.
- `AutoGapperScanner` skips untradable movers before it qualifies them.
- `TickerUniverseService` now includes AMEX, marks names that left Alpaca's tradable list as untradable, and returns
  only tradable rows.
- Not done:
  - A daily cache refresh before 03:55 ET. The new gate makes the stale cache harmless for tradability.
  - A check in manual `gapper arm`. A manual arm is the user's own choice, and Alpaca rejects the order anyway.

## 7. Next steps

1. **Remaining fix work:** add a daily `TrackedTickers` refresh before 03:55 ET, and optionally a tradability warning
   on manual `gapper arm`.
2. **Record forward (done, §5c):**
   - The daily 03:30 ET asset snapshot is scheduled. After 20 sessions, join it to that window's gappers to measure
     how often a gapper is blocked on its own day.
   - It also records borrow availability over time, which `avenues.md` A16 says has no free history.
3. **Fix the split guard** for the FLG (2024-07-12) and RSLS (2024-11-11) misses found in §5b.
4. **Holdout gappers (5,139 days, 2025-11-03..2026-09-09):** validate them only when the registered holdout run
   unseals that window, or if the user decides to lift the lock.
5. **Research, still open (from `avenues.md`):**
   - A2: 6-K exhibit text. 48% of recent runners are foreign filers, and the pre-exhibit classifier left about 1 in
     4 catalysts unresolved.
   - A4: SIP trade-level retail-flow measures (odd lots, TRF share) in the 08:30-09:50 window. This is the only
     avenue that adds new minute-level information where the long rides fail.
   - The slow earnings-beat swing (§3) is the one long candidate worth registering. It is not a premarket gapper.

Sources for §1's regulator context: Nasdaq rule proposals and SEC trading suspensions of Asia-based Nasdaq
microcaps, as reported by
[TipRanks](https://www.tipranks.com/news/nasdaq-targets-chinese-pump-and-dump-stocks-in-new-rules-crackdown) and
[Barron's via Tiger](https://www-web.itiger.com/news/2590860507). These were read at search-summary level only, so
re-read them before citing any number.
