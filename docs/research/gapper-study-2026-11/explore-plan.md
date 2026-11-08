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

Per trial: weighted AUC or precision@k (detection), mean net %/trade with day-clustered 95% bounds
(strategies), per fold and pooled. A finalist must:

1. Show the same sign in at least 3 of 4 folds and in the pooled estimate.
2. Clear the frozen bars on pooled train: +0.05 AUC over the benchmark (detection) or >= +1.0%/trade
   net with lower bound > 0 on >= 150 trades over >= 40 days (strategies).
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
`GapperEntrySimulator` in IdiotProof.Evidence). `explore corobs` (track 3: k = 25/50/100/200 x four feature subsets). Halt history (IP-T457): the Nasdaq Trader feed for every trading day 2021-10-01..2026-11-06 (51,715 halts) sits in `D:IdiotProofDatahalts` (GAPPER_HALTS_DIR overrides); loading a panel adds `x.haltPausesD1`, `x.haltNewsSinceClose`, `x.haltPausesToday`, `x.haltMinutesSinceLast` (halts stamped at or before t; ablation block "halts") and the outcome `OpenWindowPauseMinEt` (first LULD pause 09:30-09:50), which `explore exits` reports per tier. Short-sale constraints (IP-T461): `explore ssr` caches D-1 low vs D-2 close from the daily bars (Rule 201 binds on D at a 10% drop), and the FINRA short-interest files (`D:IdiotProofDatainra-short-interest`, 2021-10..2026-10) give `x.siDaysToCover`, `x.siChangePct`, `x.siAgeDays` once published (settlement + 8 trading days, strictly before D); block "short-sale constraints". Float proxy (IP-T459): `explore float-import` streams SEC companyfacts.zip (`D:IdiotProofDatasec-companyfacts`) into shares-outstanding.csv (dei cover-page shares, us-gaap fallback; first run: 20,559 filers, 15,407 with shares, 381,562 facts) and the study CIK map (5,435 symbols); loading a panel adds `x.floatLogShares`, `x.floatRotation` (premarket volume / shares), `x.floatAgeDays`, `x.floatGrowthYear` from filings strictly before D (block "float proxy"). Trade flow (IP-T460): `explore tradeflow-pull` fetches SIP prints for every train gapper morning 08:00-10:30 (20 symbols per request, resumable, about 1.7 pages per morning) into minute files under `D:IdiotProofDatagapper-study	radeflow` (prints, shares, odd lots, off-exchange, BJZZ sub-penny retail buy/sell), and `explore tradeflow` reports when the 5-minute retail imbalance collapses against the high, the price there, and flow before vs after the high (H17, descriptive; only gapper mornings are pulled, so it is never a detection feature). Dilution filings (IP-T458, avenues A2): `pull --tier edgar-dilution` (exploration only, never part of `pull all`, frozen `edgarForms` and the `EdgarFilingIndex` table untouched) reads the quarterly form.idx for 6-K, 6-K/A, F-1, F-1/A, F-3, F-3/A, EFFECT, 424B1, 424B2, 424B7, S-1/A, SC 13G, SC 13G/A plus the 424B5/S-3 rows into `D:/IdiotProofData/edgar-dilution/filings.csv` (GAPPER_EDGAR_DILUTION_DIR overrides) and fetches acceptance times (ET) inside the frozen acceptance window of target days into headers.csv; `pull --tier edgar-dilution-text` reads each 424B5 (365 days back) and S-3/F-3 shelf (three years back) through the EDGAR gate and `AtmShelfDetector` (at-the-market phrases, offering size, I.B.6 baby shelf with stated public float and 12-month sales) into classified.csv. Loading a panel adds `x.dilAtmActive`, `x.dilDaysSinceAtm`, `x.dilAtmLogUsd`, `x.dilBabyShelf`, `x.dilShelfHeadroomFrac`, `x.dil6kCount30`, `x.dil6kSinceClose`, `x.dilDaysSinceEffect`, `x.dilDaysSinceS1A`, `x.dilDaysSinceF1F3`, `x.dilDaysSince424BOther`, `x.dilSc13gCount5` from filings accepted before t (or filed before the decision day when no header was fetched); block "dilution filings". The detector is regex only: a hand-labelled check set comes before reading anything into it; ATM termination 8-Ks are not detected yet. Reg SHO threshold lists, PatternMiner/RuleMiner setups and Cue are next.

- `explore` stage in `tools/GapperStudy` with the split guard, fold builder, in-memory feature
  computation, ledger (`runs/explore/ledger.jsonl`: trial id, track, config, fold metrics, time).
- Boosted-tree learner: pure and weighted (`GradientBoostedTrees`), no new dependency.
- Sympathy and float-proxy features through the existing `IGapperFeature` registry, so live and
  backtest share them.
- Tests: split guard refuses validation/holdout; folds respect the embargo; ledger counts trials;
  in-memory features equal stored snapshots at 08:00 on the same rows.
