# Gapper study — frozen protocol (IP-T442)

This file is the pre-registration for the 5-year blind gapper backtest. It was written **before any
bar, article or filing was pulled** for the study, and it is frozen at the commit that carries it
together with `hypotheses.json` and `params.json`. Results go in a separate document
(`docs/research/gapper-study-2026-11.md`) that mirrors `evidence-sweep-2026-11.md`: protocol first,
results appended, protocol never edited. Any later change to this file, to `hypotheses.json` or to
`params.json` is a visible commit after the freeze, and the harness flags it (see *Holdout lock*).

Everything here is paper-only research. No orders. Read-only market data.

## Purpose

Three questions, asked on the same sample. (1) **What makes a gapper a gapper**: among the low-priced
names that could have gapped on a given day, which observable facts before the open separate the ones
that reached +20% / +50% / +100% from the ones that did not? (2) **Can it be recognized while it is
forming**: at 06:00, 07:00, 08:00, 08:30 and 09:00 ET, does premarket volume measured against the right
time-of-day baseline, price velocity, or a catalyst flag carry information about where the stock is at
09:45 that was not already on a "top gainers" screen? (3) **When does velocity die**: does the premarket
run's deceleration mark the high of the day, and does the high print before, at, or after the 09:30
auction? All three are asked under one honest prior, stated now so it cannot be softened later: the
most likely outcome is that a premarket volume/velocity signal at 08:00 mostly re-encodes "a catalyst
was published 10–90 minutes ago." H11 is built to report that answer cleanly. If it fails, the product
of this study is a catalyst-latency pipeline, not a volume detector, and strategy work stops.

## Prior work and what it did not test

The evidence sweep (IP-T432, IP-T437; `docs/research/evidence-sweep-2026-11.md`) ran every evidence
tool once on 20 already-volatile sub-$5 names over Aug–Oct 2026 and found that, after each tool's own
correction, **no intraday timing signal survived** (sweep doc lines 101–107: topology, order flow,
Hawkes ignition, conformal calls, the VWAP-reclaim grid all at or below a coin flip net of cost), and
that what held was descriptive: calibrated conformal intervals, strong self-excitation (branching
≈ 0.4), co-crashing tails, raided round numbers (lines 111–118). The penny walk-forward (IP-T375) found
the same for minute-shape analogs. Neither studied **gap events**, **pre-move prediction**,
**premarket volume at time of day**, or **insiders** (`InsiderTransactions` was empty at the time,
claims-event-study doc line 12). This study asks those questions and reuses the sweep's discipline:
pre-declared sample, one stage per process, research-DB guard, verdict rule fixed in advance.

## Universe, candidates, gappers, controls

**Universe U(D).** For each trading day D in the study range, every Alpaca `us_equity` asset, active
or inactive, such that from daily bars dated **strictly before D**: split-adjusted prior close in
[$0.10, $20); 20-session average dollar volume (ADV20$) >= $300,000; at least 5 of the last 20
sessions have a bar. A symbol-day is excluded when a forward or reverse split ex-date falls in
[D−25, D+1], or when a daily close-to-close move above +200% or below −66.7% in [D−25, D] has no
matching corporate action (unadjusted-split guard, as in IP-T375 amendment 2). ADRs stay in; a
descriptive `isAdr` column is set when the asset name contains "ADR" or "ADS".

**Candidate day C(s, D).** s ∈ U(D) and, from D's daily bar, `high / adjPrevClose − 1 >= 10%` OR
`volume >= 3 × ADV20 shares`. Prediction questions are asked on candidate days so that near-misses are
inside the sample; nothing is conditioned on having gapped.

**Gapper day G(s, D).** From the extended-hours minute bars 04:00–10:30 ET:
`gapMax = max(high) / adjPrevClose − 1`. Tiers: Tier20 (>= 20%), Tier50 (>= 50%), Tier100 (>= 100%),
nested. Sub-type: `premarketGapper` if the 20% level is first touched by a bar ending before 09:30,
else `openGapper`. A tier counts only if **premarket dollar volume** (sum of close × volume over bars
04:00 to the bar that first touches the tier) is at least **$50,000**; a tier touched on no volume
is recorded but excluded from the tier's counts. Expected sizes, written here so the kill criterion
is checkable: ~60,000 candidate symbol-days, ~12,000 Tier20.

**Controls.** One same-day non-candidate per gapper day: a member of U(D) that is not a candidate on
D, in the same ADV20$ decile (deciles computed within U(D) on D) and the same price tier (frozen tiers
in `params.json`), chosen as the member with the smallest SHA-256 of `"{s}|{D:yyyy-MM-dd}|{salt}"`
with the salt in `params.json`. If the cell is empty, the nearest decile at the same price tier, then
the nearest price tier. The control's identity is a function of (s, D) only, so it is reproducible
and cannot be re-drawn.

**Survivorship.** Inactive assets come from `GET /v2/assets?status=inactive` and are pulled alongside
active ones, so delisted names are in U(D) on the days they existed. Caveat carried from IP-T375: a
ticker reused by a newer company maps to the newer entity's history; the universe build counts
symbols whose first and last bar dates bracket a gap of more than 90 sessions and reports the count.

**Corporate actions.** Splits (T6) are applied in-house to prior closes and ADV shares
(`adjustment=raw` on every bar request; nothing relies on Alpaca's adjusted series). Dividends are
not adjusted. Symbol changes are not tracked beyond what Alpaca's asset list encodes.

## Data tiers

Alpaca data API limit 200 requests/minute, `feed=sip` on every request; EDGAR 10 requests/second
through the existing `edgar` HttpClient with the declared `User-Agent`. All pulls are resumable by
coverage marks; a killed pull is rerun, not restarted.

| tier | content | source | storage |
|---|---|---|---|
| T1 | daily bars, all active + inactive `us_equity`, 2021-09-01 .. 2026-11-06 | `GET https://data.alpaca.markets/v2/stocks/bars?symbols=…&timeframe=1Day&adjustment=raw&feed=sip` (multi-symbol, ~400 calls) | research DB `IdiotProof_Research_GAP`, table `GapperStudyDailyBars` |
| T2 | 1-min bars 04:00–20:00 ET for every candidate day, its D−1..D−5, and the matched control day (~130k symbol-days) | `GET /v2/stocks/bars?symbols=…&timeframe=1Min&adjustment=raw&feed=sip&limit=10000` with ET-converted `start`/`end` | file store `D:\IdiotProofData\gapper-study\bars\{SYMBOL}\{yyyy-MM}.bin` behind `FileBackedBarStore : IHistoricalBarStore`; LocalDB's 10 GB cap keeps minute bars out of SQL |
| T3 | news for candidate symbols in [D−5, D+1], paged, `created_at` and `updated_at` kept as full timestamps | `GET https://data.alpaca.markets/v1beta1/news?symbols=…&start=…&end=…&limit=50&include_content=true` (+ `page_token`) | `GapperStudyCatalysts` (raw articles) |
| T4 | EDGAR form index, 22 quarterly files; per-filing acceptance time within [D−30, D+1] of candidate days | `https://www.sec.gov/Archives/edgar/full-index/{YYYY}/QTR{q}/form.idx`; `https://www.sec.gov/Archives/edgar/data/{cik}/{accessionNoDashes}/{accession}-index-headers.html` (`ACCEPTANCE-DATETIME`) | `EdgarFilingIndex` |
| T5 | ticker ↔ CIK | `https://www.sec.gov/files/company_tickers.json`, Form 4 `issuerTradingSymbol`, then exact issuer-name match | `GapperStudyCikMap`; coverage % of candidate symbols reported |
| T6 | splits | `AlpacaSplitSource` (`/v1beta1/corporate-actions`, forward and reverse splits) | `GapperStudySplits` |
| T7 (optional, literature hooks) | FINRA daily short-sale volume; XBRL `EntityCommonStockSharesOutstanding` as a float proxy | `https://cdn.finra.org/equity/regsho/daily/CNMSshvol{yyyymmdd}.txt`; `https://data.sec.gov/api/xbrl/companyfacts/CIK{cik:D10}.json` | `GapperStudyShortVolume`, `GapperStudySharesOutstanding`; absence is reported, never imputed |

EDGAR forms in T4: 8-K, 8-K/A, 4, 3, S-1, S-3, 424B3, 424B4, 424B5, SC 13D, 25-NSE, 15-12G.

Panel tables (small, SQL): `GapperStudyUniverseDays`, `GapperStudyEvents`, `GapperStudySnapshots`,
`GapperStudyLabels`, `GapperStudyCatalysts`, `EdgarFilingIndex`. The research DB name must contain
`_Research_` or the harness refuses to run (same guard as `tools/EvidenceSweep/Program.cs:26-33`).
**SIP provenance** is recorded per bar (`Alpaca:sip`) and the share of study minute bars that carry
it is reported; see *Kill criteria*.

## Decision-time schedule

T = { D−1 16:00, 04:00, 06:00, 07:00, 08:00, 08:30, 09:00, 09:15, 09:25, 09:30, 09:31, 09:35, 09:45 }
ET. A snapshot at t uses only minute bars with `EndUtc <= t`, news with `created_at <= t`, filings
with `ACCEPTANCE-DATETIME <= t`, and daily bars dated < D. The feature code trims to `EndUtc <= t`
itself and a unit test asserts that appending post-t bars changes nothing. Minute bars are
forward-filled as zero-volume bars so every window is wall-clock.

## Feature definitions

Prices are from minute bars (`c` close, `h` high, `v` volume, `n` trade count). `prevClose` is the
split-adjusted close of D−1. ADV20 is in shares; ADV20$ in dollars. All ratios use the bars available
at t only.

**Premarket price and volume.**
- `pmRet(t) = c_t / prevClose − 1`.
- `pmCumVol(t) = Σ v over bars 04:00..t`; `pmDollarVol(t) = Σ c·v`.
- `pmVolRatio(t) = pmCumVol(t) / (ADV20 × profile_decile(t))`, where `profile_decile(t)` is the
  **median fraction of a day's 04:00–20:00 volume printed by clock time t on control days**, per
  ADV20$ decile, fit on the **training period only** and stored as `data/tod-profile.json`. The same
  file is shipped to the live Monitor so live and backtest ratios share one baseline. The profile is
  evaluated at every minute of the day (not only at T) so `minsSinceFirstAbnormal` can use it.
- `ownProfileRatio(t) = pmCumVol(t) / ownExpected(t)`, with
  `ownExpected(t) = w × ownMedian(t) + (1 − w) × ADV20 × profile_decile(t)`, `w = n / (n + 5)`, where
  `ownMedian(t)` is the median of the symbol's own cumulative volume by clock time t over D−1..D−5 and
  n is the number of those days with bars (0..5).

**Velocity and acceleration** (percent per minute, on forward-filled closes):
- `v_k(t) = 100 × (c_t / c_{t−k} − 1) / k` for k ∈ {1, 3, 5, 15}.
- `accel(t) = v_3(t) − v_3(t − 3)`.
- `volRoc(t) = Σ v over (t−5, t] / Σ v over (t−10, t−5] − 1` (null if the denominator is zero).
- `trades_k(t) = Σ n over (t−k, t]`, k ∈ {1, 5}; `tradesRoc(t) = trades_5(t) / trades_5(t−5) − 1`.
- `csSpread = TradingCosts.CorwinSchultzSpread` over the 04:00–20:00 minute bars of D−1..D−5
  (consecutive-bar high/low pairs, as the function consumes them); null if fewer than 60 bars.
- `distToPmHigh(t) = c_t / max(h over 04:00..t) − 1`.
- `minsSinceFirstAbnormal(t) = t − m₀` where m₀ is the first minute on D with
  `v_m >= 5 × ADV20 × (profile_decile(m) − profile_decile(m − 1))`; null if no such minute <= t.

**Run-up (daily, dated < D).** For j = 1..5: `ret_j = ln(close_{D−j} / close_{D−j−1})`;
`abnVol_j = volume_{D−j} / ADV20(D−j)` with ADV20 taken over the 20 sessions before D−j;
`overnight_j = open_{D−j} / close_{D−j−1} − 1`; `intraday_j = close_{D−j} / open_{D−j} − 1`
(the Lou/Polk/Skouras decomposition); `runUp5 = Σ ret_j`.

**Filings and structure.** `daysSinceReverseSplit` = days from the last reverse-split ex-date (T6)
to D, null if none within 365 days. `daysSince424B`, `daysSinceS1` = days from the last filing of
that form family (T4, filing date; acceptance time where available) to t, null if none within 365
days. `form4BuysD30` = number of Form 4 filings accepted in [D−30, t] whose transaction code is `P`
(open-market purchase) for the symbol's CIK; null when the symbol has no CIK mapping.

**Catalyst block** (any news item or 8-K with publication/acceptance in [t − 24h, t]):
`hasCatalyst`; `catalystType` ∈ {Fda, ContractAward, ReverseSplit, OfferingDilution, Uplisting,
Earnings, MA, PromoPr, Other} from `CatalystExtractor` extended with the five new types, classified
once per unique article/filing id and cached; `catalystSentiment`; `sourceTier` ∈ {wire, filing,
news, promotional}; `pubToTMinutes` = t − publication time of the most recent qualifying item;
`novelty = 1 / (1 + articles about s in [t − 90d, t − 24h])`; `eightKItems` (item codes).

**Literature add-ons** (secondary; `Extra` dictionary on the snapshot): `priorDayTopMoverRank` = rank
of s by daily return among U(D−1) on D−1 (1 = largest gain; null if not in the top 100);
`promoSourceShare` = share of articles in [t − 90d, t] that are promotional (deterministic
`CatalystPreClassifier` class `PromoPr`, frozen with the code, or a source on the `promotionalSources`
list in `params.json`, which may be empty);
`jumpFlag(t)` = Lee–Mykland statistic on 1-min log returns, local bipower window K from
`params.json`, exceeds the 1% critical value at any minute on D before t.

**Not available on this stack, by design stated now**: float, short interest, dark-pool prints,
options flow. No feature stands in for them; `sharesOutstanding` (T7) is a float proxy and is
labelled as such.

## Labels

For a snapshot at t, **entry** is the open of the first bar with `StartUtc >= t` (next-bar open; the
bar at t is never traded). Horizons h ∈ {09:30, 09:45, 10:30, 16:00} ET, only those with h > t.
- `fwdMax_h = max(high over bars after entry through h) / entry − 1`.
- `fwdClose_h = close of the last bar with EndUtc <= h / entry − 1`.
- `reach20By0945 = max(high over 04:00..09:45) / prevClose − 1 >= 0.20` (relative to prior close,
  so it is the tier event, not an entry return).
- `pmHigh = max(h over 04:00..09:29)`; `hodTimePm` = its minute; `hodTimeRth` = minute of
  `max(h over 09:30..16:00)`; `hodTime` = minute of the full-session high 04:00–16:00;
  `hodBucket(hodTime)` ∈ {<09:15, 09:15–09:29, 09:30–09:31, 09:32–09:45, 09:46–10:30, later}.
- `fade = close_16:00 / pmHigh − 1`; `fadeFlag30 = fade <= −0.30`.
- `closeBelowOpen = close_16:00 < open_09:30` (the vendor's "fade" definition, used by H13).
- `ext0945 = max(h over 09:30..09:45) / open_09:30 − 1` (extension from open at 09:45).
- `velocityDeathTime` = the first minute t_d >= 08:30 such that `v_3 < 0` at t_d and at t_d − 1, and
  some earlier minute t_a with 08:30 <= t_a < t_d had `v_3(t_a) > 0.5`. Null if never armed or never
  dies before 16:00.
- `deathToHodMin = hodTime − velocityDeathTime` in minutes (positive: the high printed after
  velocity died). Null when either side is null.
- `haltFlag` = at least 5 consecutive RTH minutes with no trades after the first abnormal minute on a
  symbol that printed in each of the 5 preceding minutes (a **bar-gap proxy**; Alpaca has no
  historical LULD feed and the proxy is named as such everywhere it is reported).
- `vwapLossTime` = first minute >= 09:30 with `close < VWAP` after at least 3 consecutive minutes
  above it, VWAP anchored at 04:00; null if never.

Labels are computed once per (s, D, t), stored in `GapperStudyLabels`, and never recomputed with a
different definition inside this study.

## Statistical families and corrections

Every hypothesis in `hypotheses.json` belongs to exactly one family. The family fixes the test and
the correction. Seeds and draw counts are in `params.json`.

| family | question shape | tool | correction |
|---|---|---|---|
| F1 prediction buckets | does a feature quintile/condition lift the base rate of a label? | `HoldoutBucketEvaluator` (5 bins), lift and precision with day-clustered bootstrap CIs; H11 uses AUC with a DeLong-style day-block bootstrap | BH q < 0.05 within the family |
| F2 catalyst groups | do catalyst groups differ in forward return / fade? | `EventStudyEngine` BMP statistic with a 1,000-draw placebo that reassigns group labels within the same day | BH q < 0.05 within the family |
| F3 timing (descriptive) | when does the high print; where is velocity death relative to it? | Wilson 95% intervals for proportions; 10,000-draw day-clustered bootstrap intervals for medians | none; **F3 never uses the word "significant"**. A verdict is *supported* when the interval lies entirely on the claimed side of the threshold, *refuted* when entirely on the other side, otherwise *undecided* |
| F4 strategy grids | does a rule net more than cost? | `evidence search` machinery: reality check / SPA (`RealityCheck`) and deflated Sharpe (`MultipleTesting.DeflatedSharpe`) over the pre-declared grid | SPA p < 0.05 and DSR >= 0.95 |
| F5 structural | does a filing/structure fact shift a label? | group difference with day-clustered bootstrap p | BH q < 0.05 within the family |

**Across families.** The primaries that yield a p-value (H1, H2, H3, H6, H7, H8, H9, H10, H11, H12)
additionally enter a Holm step-down at α = 0.05. A primary is *confirmed* only if it passes both its
family correction and Holm. H4 and H5 are descriptive primaries and are judged by their intervals
alone. Secondaries (H13–H17) are corrected within family only and are reported as leads, never as
confirmations.

**Replication rule.** A hypothesis is tested on train; if it passes there it is re-tested on
validation with the same definitions and must keep its sign and pass the same correction. Only
hypotheses that pass validation are carried to the holdout. The verdict table lists every
hypothesis on every split it was run on, including the failures.

## Cost model (frozen)

- **Spread.** `csSpread` (above) × `premarketSpreadMultiplier`. The multiplier is calibrated **once**:
  on the first 500 training-period Tier20 events in (D, s) order, the median of
  (quoted relative spread from `GET /v2/stocks/{s}/quotes` within ±60 s of 08:00 ET) / `csSpread`;
  the resulting number is written into `params.json` as the value of `premarketSpreadMultiplier`
  (currently `"calibrated-once"`) in one commit and never changed. Half the spread is paid per side.
- **Impact.** `TradingCosts.SquareRootImpact(shares, averageDailyVolume = pmCumVol(t),
  dailyVolatility = σ20, impactConstant = 1.0)` per side, with σ20 the standard deviation of daily log
  returns over the 20 sessions before D and `shares = tradeNotional / entry`.
- **Floor.** Round-trip cost = max(spread + 2 × impact, 0.5%).
- **Halts.** A fill requested in a minute flagged by the halt proxy fills at the open of the first
  bar after the gap.
- **Worth trading** (used by H12 and the promotion rule): mean net >= +1.0%/trade on train and
  validation; day-clustered 2.5% bound > 0; >= 150 validation trades on >= 40 distinct days.

## Splits

| split | days | use |
|---|---|---|
| train | 2021-11-01 .. 2024-10-31 | profile fit, catalyst-weight fit, all exploratory tests, grid selection |
| validation | 2024-11-03 .. 2025-10-31 | replication of train survivors; H11; H12 SPA |
| holdout | 2025-11-03 .. 2026-10-30 | opened once, for the surviving set only |

Daily bars start 2021-09-01 so that the first training day has 20 prior sessions. Nothing fit on
train is refit on validation or holdout; `data/tod-profile.json` and the catalyst weights are
versioned with the freeze commit.

## Holdout lock

1. `holdout-unlock.txt` must exist in this directory and contain the full 40-character hash of the
   commit that last touched `hypotheses.json` or `params.json`
   (`git log -1 --format=%H -- hypotheses.json params.json`). The harness computes the same value
   from the repository it runs in and refuses when they differ, or when either file has uncommitted
   changes, or when `hypotheses.frozenUtc` is null.
2. On a successful unlock the harness writes `holdout-run.json` containing the unlock hash, HEAD,
   run start/end UTC, the SHA-256 of `hypotheses.json` and `params.json`, the list of hypotheses
   run, and the `manifest.json` hashes of every input CSV. It writes this file **before** scoring
   the first holdout label.
3. If `holdout-run.json` already exists the harness refuses to run the holdout again, regardless of
   flags. There is no `--force`.
4. The report step lists every commit touching `protocol.md`, `hypotheses.json` or `params.json`
   whose author date is later than `holdout-run.json.runStartUtc`; any hypothesis whose definition
   changed in such a commit is marked **post-hoc** in the results doc and excluded from the verdict.

## Promotion and the live gate

A rule (H12's winner, or nothing) is promoted to **paper shadow** only when all of the following hold
in order: the feature claim it rests on survives train (q < 0.05) → replicates on validation with the
same sign → H12 survives SPA on validation → the single holdout run nets >= +0.5%/trade with the
day-clustered 2.5% bound > 0. Paper shadow means the live `GapperFormationWorker` records snapshots
and the rule's decisions, paper mode, no arming beyond paper. **Live gate**: holdout net >=
+0.5%/trade with lower bound > 0 **and** 20 paper-shadow trading days with live snapshots whose
features agree with the backtest's (same code, so disagreement is a data problem to be explained,
not a parameter to tune). Only then may a Live `BrokerMode` be offered. The short leg is studied
here but is not built until the long exit has passed the shadow period.

## Kill criteria

The study stops and reports a negative result, without any protocol edit, if any of these hold:
- fewer than 3,000 Tier20 gapper days in the training split;
- SIP provenance below 95% of study minute bars;
- H11 fails on validation (the detector does not beat the catalyst-latency benchmark by >= 0.05 AUC)
  — strategy work stops, the write-up says "catalyst latency is the edge";
- any post-unlock hypothesis or parameter edit (auto-flagged by commit date) — the affected
  hypotheses are void.

H8 is **dropped, not redefined**, if CIK coverage of candidate symbols is below 50%.

## Data limits, stated plainly

- No float, no short interest, no dark-pool volume, no options flow. The literature's strongest
  pre-move footprint (OTM call volume before M&A/FDA) cannot be tested here.
- Alpaca's news stream is reported by users to carry Benzinga-authored items and to miss raw wire
  press releases; no published latency measurement exists. `pubToTMinutes` is therefore an upper
  bound on how early a catalyst could have been seen on this stack. The study reports, per catalyst
  type, how often the first abnormal-volume minute precedes the first Alpaca headline.
- EDGAR `form.idx` carries filing dates only; the acceptance timestamp needs the per-filing
  `-index-headers.html` fetch, which the study makes only within [D−30, D+1] of candidate days.
  Outside that window, `daysSince*` features are day-granular.
- CIK coverage is a reported number. Form 4 features exist only for mapped symbols.
- Halts are a bar-gap proxy. LULD band mechanics (doubling 09:30–09:45, 20% below $3) are cited from
  the DERA white paper, not observed.
- Minute bars before the SIP provenance flag was recorded cannot prove SIP; the study records the
  flag on every bar it pulls and reports the share.
- Survivorship: reused tickers, as above.

## Reproducibility

- Every stage (`build-universe`, `pull`, `catalysts`, `snapshot`, `test`, `report`) runs as its own
  process (`tools/GapperStudy/Program.cs`), from a clean export of `origin/main` at a recorded hash,
  against `IdiotProof_Research_GAP` only (the research-DB guard refuses any other connection string).
- `sample.json` (universe, candidate, gapper and control identities with counts) is committed after
  the universe build and before any snapshot is computed.
- Results are exported as CSV next to the results doc with a `manifest.json` listing each file's
  row count and SHA-256 of its rows, mirroring `dataset export`.
- Seeds: every bootstrap and placebo uses the seed in `params.json`; no sampling step is unseeded.
- The results doc reports, for each hypothesis: split, n, raw effect, corrected statistic, verdict,
  and the commit hash of the harness that produced it.
