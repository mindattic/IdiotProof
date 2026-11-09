# Gapper study: the exploratory round (train only)

Plan, 2026-11-07 (IP-T452). It starts once the current chain has finished (snapshots, labels and
the confirmatory train/validation tests of the frozen study and amendments 01-03 exist).

## Purpose

The confirmatory study asks 24 pre-registered questions once. This round is where we **search**:
many features, models, decision times, entries and exits, tried as often as needed, to find what
recognizes a gapper while it is forming and when its velocity runs out. Searching is fine as long
as it never touches the data used to judge what it finds. So:

- **Train only.** The explore stage refuses any split but `train` and never loads a validation or
  holdout label. This is a code guard, not a promise.
- **Every trial is counted.** One ledger row per configuration tried, so the final deflated Sharpe
  and multiple-testing corrections know how hard we searched.
- **Few finalists leave.** At most 5 finalists are registered in amendment 04, tested once on
  validation, and only survivors go to the single holdout run.

## Folds inside train

Rolling-origin folds, each fit on everything before it, with a 5-trading-day embargo (features read
D-1..D-5):

| fold | fit | evaluate |
|---|---|---|
| 1 | 2021-11 .. 2022-10 | 2022-11 .. 2023-04 |
| 2 | 2021-11 .. 2023-04 | 2023-05 .. 2023-10 |
| 3 | 2021-11 .. 2023-10 | 2023-11 .. 2024-04 |
| 4 | 2021-11 .. 2024-04 | 2024-05 .. 2024-10 |

The time-of-day volume profile stays the frozen one fit on all of train. It is fit on control-day
volume shapes and never sees an outcome, so it cannot leak a label into folds 1-3 (built: refitting it
per fold would mean four panel builds for no protection). Weights and population are amendment 01's:
candidates plus the 10% sample. Every estimate is reported per fold and pooled.

**Forming only.** Detection is scored on mornings still below +20% at t: a stock already up 20% has
formed, and counting it would make late decision times look far better than they are.

## Data

The DB holds sample-only days at 08:00, 08:30 and 09:00 only (LocalDB 10 GB cap). Exploration needs
other times, so the explore stage **computes features in memory** from the minute-bar file store
with the shared `GapperFormationFeatures.Compute` (the same code the live recorder runs) and writes
nothing to SQL except the ledger. Results go to `runs/explore/`.

## Tracks

**1. How early is a gapper visible?** (the core question)
- AUC and precision-at-top-50-per-day for reach20By0945 at every decision time from D-1 16:00 to
  09:25. Where does the curve leave the catalyst-latency benchmark, if ever?
- The same split by news vs no news and by gap tier.

**2. What makes a gapper** (detection models)
- Learners: weighted logistic, gradient-boosted trees (built as a pure, deterministic weighted
  learner in `IdiotProof.Evidence/Evaluation/GradientBoostedTrees.cs` instead of ML.NET, so the
  library stays dependency-free and repeatable), Corobs look-alike.
- Feature blocks added and removed one at a time (ablation): volume vs time of day, velocity and
  acceleration, prior-day setup (D-1..D-5 abnormal return and volume, prior top-mover rank), corporate
  actions (reverse split, 424B, S-1), catalyst (type, direction, age, source tier, novelty), spread.
- New candidate features: **sympathy** (number of Tier20 gappers and same-theme news already that
  morning), first-abnormal-minute timing, trade-count acceleration, distance to round numbers,
  float proxy from XBRL shares outstanding.

**3. Corobs variations** (after amendment 03's verdict, whatever it is)
- k (25/50/100/200), dimensions per ply (4/8/16), feature subsets, per-ply weighting.
- `PatternMiner` and `RuleMiner` on gapper mornings: are there tight clusters (setups) far beyond
  chance, and rules of the form "this premarket state leads to that outcome"?
- `Cue`: mornings inside a gapper cluster that did not run. What stopped them?

**4. Entry**
- Given a detection at t: buy now, buy the first pullback to VWAP, buy the premarket-high breakout,
  or buy only after news confirmation. Score: net return after the frozen cost model.

**5. Exit (velocity runs out)**
- Distribution of high-of-day time against velocityDeathTime by gap tier and catalyst type: does
  velocity die about 15 minutes before the bell, or into the auction?
- Exit families compared on the same entries: velocity death (window 2/3/5 bars, threshold),
  VWAP loss, extension from open, time stops (09:15, 09:28, 09:31, 09:45), peak giveback, and
  trade-count/volume imbalance collapse (H17's footprint) as an exhaustion mark.
- Halts: exits within the LULD band-halving window (09:45-09:50).

**6. Fade leg** (studied, not automated)
- After the high: lower high plus VWAP loss as a short entry on Tier50 days, net of borrow
  assumptions. Exploration only, as the plan says.

**7. News ride** (extends amendment 02)
- By catalyst type, source tier, headline direction, time of publication and latency; first item vs
  any item; whether a price confirmation helps.

## Scoring and finalist criteria

Per trial: weighted AUC per fold, its mean over the folds (not an AUC over the folds' concatenated
scores, which rewards ranking busy days over picking stocks; IP-T465), the within-morning AUC and
precision@10 per morning with its ceiling (detection); mean net %/trade with day-clustered 95% bounds
(strategies), per fold and pooled. Detection is scored against three benchmarks: news within 60
minutes (H11's), the top-gainers screen (gap so far at t), and gainers + news (fresh-news names
first, each group by gap). See methods-prediction.md. A finalist must:

1. Show the same sign in at least 3 of 4 folds and in the mean over folds.
2. Clear the frozen bars on train: +0.05 mean fold AUC over the benchmark, and beat gainers + news on
   mean fold AUC and within-morning precision@10 (detection), or >= +1.0%/trade net with lower bound
   > 0 on >= 150 trades over >= 40 days (strategies).
3. Survive leave-one-month-out: no single month carries the result.
4. Make sense economically: a short written reason why it should work.
5. Survive deflation by the ledger's trial count (DSR >= the frozen minimum).

## Path out

1. Exploration report `runs/explore/report.md`: every track, every trial count, including the dead ends.
2. Amendment 04 registered and pushed: the <= 5 finalists with exact definitions, thresholds and
   minimum effects, before any of them touches validation.
3. One validation test of amendment 04.
4. The single holdout run: frozen, A01-A04 together.
5. Survivors: thresholds into `formation-trigger.json` (arming stays off until the user says so),
   then 20 paper-shadow trading days before any live mode is offered.

## Build

Built (IP-T453): `research gappers explore <build|visibility|models|exits>` and
`GapperStudy.dll explore ...`; panel cache in `D:\IdiotProofData\gapper-study\explore`, reports and
the ledger in `runs/explore/`. Also built: `explore news` (track 7: gross next-open ride by catalyst
type, age and direction vs no-news mornings, day-clustered intervals, one ledger trial per group and
horizon), `explore fade` (track 6, descriptive: extension from the open at 09:45 vs close below open
and fade from the PM high, per tier and by sympathy), and the sympathy features
(`x.sympathyFormed20`, `x.sympathyForming10`: other symbols already up 20% / 10% at t that morning,
added when a panel loads, ablation block "sympathy"), and `explore entries` (track 4: after a
detection grid of volRatioTod >= 3/5/10 x v3 >= 0/0.5 at t, buy now, first VWAP pullback, premarket-high
breakout, or buy now only with news <= 60 min, all with one exit and net of the frozen cost model;
`GapperEntrySimulator` in IdiotProof.Evidence). `explore corobs` (track 3: k = 25/50/100/200 x four feature subsets). Halt history (IP-T457): the Nasdaq Trader feed for every trading day 2021-10-01..2026-11-06 (51,715 halts) sits in `D:IdiotProofDatahalts` (GAPPER_HALTS_DIR overrides); loading a panel adds `x.haltPausesD1`, `x.haltNewsSinceClose`, `x.haltPausesToday`, `x.haltMinutesSinceLast` (halts stamped at or before t; ablation block "halts") and the outcome `OpenWindowPauseMinEt` (first LULD pause 09:30-09:50), which `explore exits` reports per tier. Short-sale constraints (IP-T461): `explore ssr` caches D-1 low vs D-2 close from the daily bars (Rule 201 binds on D at a 10% drop), and the FINRA short-interest files (`D:IdiotProofDatainra-short-interest`, 2021-10..2026-10) give `x.siDaysToCover`, `x.siChangePct`, `x.siAgeDays` once published (settlement + 8 trading days, strictly before D); block "short-sale constraints". Float proxy (IP-T459): `explore float-import` streams SEC companyfacts.zip (`D:IdiotProofDatasec-companyfacts`) into shares-outstanding.csv (dei cover-page shares, us-gaap fallback; first run: 20,559 filers, 15,407 with shares, 381,562 facts) and the study CIK map (5,435 symbols); loading a panel adds `x.floatLogShares`, `x.floatRotation` (premarket volume / shares), `x.floatAgeDays`, `x.floatGrowthYear` from filings strictly before D (block "float proxy"). Trade flow (IP-T460): `explore tradeflow-pull` fetches SIP prints for every train gapper morning 08:00-10:30 (20 symbols per request, resumable, about 1.7 pages per morning) into minute files under `D:IdiotProofDatagapper-study	radeflow` (prints, shares, odd lots, off-exchange, BJZZ sub-penny retail buy/sell), and `explore tradeflow` reports when the 5-minute retail imbalance collapses against the high, the price there, and flow before vs after the high (H17, descriptive; only gapper mornings are pulled, so it is never a detection feature). Dilution filings (IP-T458, avenues A2): `pull --tier edgar-dilution` (exploration only, never part of `pull all`, frozen `edgarForms` and the `EdgarFilingIndex` table untouched) reads the quarterly form.idx for 6-K, 6-K/A, F-1, F-1/A, F-3, F-3/A, EFFECT, 424B1, 424B2, 424B7, S-1/A, SC 13G, SC 13G/A plus the 424B5/S-3 rows into `D:/IdiotProofData/edgar-dilution/filings.csv` (GAPPER_EDGAR_DILUTION_DIR overrides) and fetches acceptance times (ET) inside the frozen acceptance window of target days into headers.csv; `pull --tier edgar-dilution-text` reads each 424B5 (365 days back) and S-3/F-3 shelf (three years back) through the EDGAR gate and `AtmShelfDetector` (at-the-market phrases, offering size, I.B.6 baby shelf with stated public float and 12-month sales) into classified.csv. Loading a panel adds `x.dilAtmActive`, `x.dilDaysSinceAtm`, `x.dilAtmLogUsd`, `x.dilBabyShelf`, `x.dilShelfHeadroomFrac`, `x.dil6kCount30`, `x.dil6kSinceClose`, `x.dilDaysSinceEffect`, `x.dilDaysSinceS1A`, `x.dilDaysSinceF1F3`, `x.dilDaysSince424BOther`, `x.dilSc13gCount5` from filings accepted before t (or filed before the decision day when no header was fetched); block "dilution filings". The detector is regex only: a hand-labelled check set comes before reading anything into it; ATM termination 8-Ks are not detected yet. Reg SHO and daily short volume (IP-T469): `explore regsho-pull` saves the Nasdaq (`nasdaqthYYYYMMDD.txt`) and NYSE-group (NYSE, American, Arca, Chicago, National; `nyseYYYYMMDD.txt`) threshold lists into `D:/IdiotProofData/regsho-threshold` (GAPPER_REGSHO_DIR overrides) and the FINRA consolidated daily short-volume files (`CNMSshvolYYYYMMDD.txt`) into `D:/IdiotProofData/finra-short-volume` (GAPPER_SHORT_VOLUME_DIR overrides) for every trading day 2021-10-01..2026-11-06 (resumable, about two requests a second, NYSE one per 1.5 s; Nasdaq Trader has no other-listed list and Cboe's is not fetched); all are published the evening of their day, so loading a panel adds `x.regShoListedD1`, `x.regShoDaysListed` (consecutive listed days ending D-1), `x.shortVolShareD1`, `x.shortVolShareAvg5` (D-1..D-5) and `x.shortVolShareZ` (D-1 against D-2..D-21) from days strictly before D; block "reg sho and short volume". Exit routes (IP-T471): `explore exit-routes` (default `--times 08:30`) buys the next open after volRatioTod >= 5 and v3 >= 0 below +20% and sells by continuous 09:15/09:20, an `opg` limit 3%/5% under the 09:27 close (filled only when the 09:30 print is above it, else the 09:30:05 fallback at the worse of that bar's open and close), 09:31, 09:44 or 09:50 with and without the LULD 09:44 band exit, each alone and with the `DriftChangeExitRule` (Shiryaev posterior with the per-fold `HodHazard` and post-high drift, p* 0.5/0.7/0.9) and `GivebackExitRule` (k 3/5) overlays and the trade-count collapse state; it reports net with day-clustered bounds per fold and pooled over the eval windows, paired against the clock, plus a `ParticipationCap` capacity curve at $1k/$5k/$25k, one ledger trial per configuration. Onset detection (IP-T470, methods-onset P0/P1/P2/P4): `CountCusum` and `OnsetTrack` in IdiotProof.Evidence run a one-sided CUSUM (z capped at ln 20, k = ln 2) on minute volume and on minute trade count against the time-of-day profiles (a trade-count profile, `data/tod-trades-profile.json`, is fit on train control days like the volume one; ADT20 comes from the daily bars' trade counts), and a panel built after this adds `x.cusumVol`, `x.cusumTrades`, `x.minsSinceCusumAlarm` (provisional h = 5), `x.pmVwapRet`, `x.conf` (pmVwapRet / pmRet, undefined below a 0.1% gap) and `x.pmDollarShareAbove10`, while loading any panel adds `x.pmVolRatioMkt` and the weighted within-morning ranks `x.rankPmVolRatio`, `x.rankPmRet`, `x.rankCusumVol` (blocks "onset cusum", "cross-sectional rank", "volume-confirmed gap"); `explore alarms` sweeps every population morning minute by minute and scores 117 rules in 06:00-09:10 (CUSUM alone on volume/trades/either x k ln 1.5/2/3 x h 3/5/8/12 or calibrated per fold to 1 in 20 / 1 in 50 control mornings; rank top 3/5/10 by a composite rank or by cusumVol, gap [2%|5%, 10%|15%), $50k premarket dollar volume, conf >= 0.5, with and without a CUSUM gate; and the matched gainers + news benchmarks) by hit rate, lead to +20% (median, IQR), false alarms per 100 mornings and alarms per day, per fold against B3, one ledger trial per rule. Fade short (IP-T476): `explore fade-short` shorts at the next open after 09:45 the names whose premarket high was >= +30/50/100% over the adjusted prior close, still >= +20% at 09:45 and up < 10/5/0% from the 09:30 open (all from bars ending by 09:45), covering by 15:55 or 11:00 with a +10/+20% stop filled at the worse of the stop and the bar open and an optional VWAP reclaim, net of the frozen cost model (its premarket spread multiplier applied unchanged after the open), 0/50/200% APR borrow, Rule 201 as an uptick entry or a skip, with and without D-1 Reg SHO names, against a random short of every +20% name; `GapperShortSimulator` (IdiotProof.Evidence) holds the fills, borrow, margin, MAE, 100-share lots and the seeded locate draw, and a second pass stresses the best picks with 30%/60% locate failures, 1.0%/2.5% locate fees and round lots. Fade-short deep dive (IP-T479): `explore fade-short-deep` follows the top fade short (premarket high >= +100%, extension < 10%) and its +50% variant on train with the per-trade and per-day deflated Sharpe against the ledger total, the time-of-day capture and post-entry-high timing, `GapperShortPath` (IdiotProof.Evidence) exits (fixed +20/30/40% and catastrophe +50/75% stops, 2/3/4 x opening-range or 14-day-range stops, 11:00-14:00 time stops, half covers, -10/20/30% cover limits and armed trailing stops), the methods-short-risk.md three-tier shortability proxy with 90/50/10% seeded locate success, a 2.5% fee and 100-share lots, fixed-fraction day-block bootstrapped years with the worst 1% stressed to -100%/-150%, and single-feature squeeze AUCs at 09:45 with a fold-fitted quintile exclusion. Alarm ride (IP-T483): `explore alarm-ride` buys amendment 04's R4 alarm long on train eval windows (alone, gated by the fold-fitted boosted model's within-morning top 1/2/3 at the latest panel time at or before the alarm, or by fresh news), marketable at the next open or by a passive limit 1% under the alarm close within 5 minutes, and sells by take-profit +8/10/15/20% x stop -4/6/8% x a 15/30/60-minute time stop or `VelocityExitRule` death with a 09:30 opening-print or 09:31 clock, or by lottery exits (the same stops, a 15/25/35% trailing stop once +10%, optional half at +20%, out by 10:30 or 15:55) through `GapperLongPath` (IdiotProof.Evidence), net of the frozen cost model and at 0.5x/0.25x of its spread component, with precision, break-even cost, participation capacity, leave-one-month-out, the DSR against the ledger total and the +100%/+500% monster census. PatternMiner/RuleMiner setups and Cue are next.

- `explore` stage in `tools/GapperStudy` with the split guard, fold builder, in-memory feature
  computation, ledger (`runs/explore/ledger.jsonl`: trial id, track, config, fold metrics, time).
- Boosted-tree learner: pure and weighted (`GradientBoostedTrees`), no new dependency.
- Sympathy and float-proxy features through the existing `IGapperFeature` registry, so live and
  backtest share them.
- Tests: split guard refuses validation/holdout; folds respect the embargo; ledger counts trials;
  in-memory features equal stored snapshots at 08:00 on the same rows.
