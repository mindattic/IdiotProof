# Gapper study: methods for real-time onset detection (IP-T463)

Literature pass, 2026-11-08. Web research only: no code, data pull, test or build was run for this
document. It is the companion to `literature.md` (what predicts a gapper) and `avenues.md` (data
sources not yet used). This one asks a narrower question: **which published methods detect the
onset of an abnormal move while it is happening, how early do they fire, and what would they add to
the features this study already has** (`pmVolRatio` by time of day, `ownProfileRatio`, `v_k`,
`accel`, `volRoc`, `trades_k`/`tradesRoc`, `minsSinceFirstAbnormal`, `jumpFlag`/`x.leeMykland`,
catalyst latency `pubToTMinutes`, sympathy).

Two facts from this repo shape every rating below:

- **BOCPD already exists** (`IdiotProof.Evidence/Regime/Bocpd.cs`) and the evidence sweep found it
  over-flags on sub-$5 minute returns (6–25 change points per symbol per 5 sessions, unchanged after
  the tick-aware fix IP-T435; `evidence-sweep-2026-11.md` lines 71, 108, 119). Change detection on
  **penny-stock returns** has been tried and failed. Change detection on **volume and trade counts
  against a time-of-day baseline** has not.
- **`minsSinceFirstAbnormal` is already a one-minute threshold detector** (first minute with volume
  >= 5x its time-of-day expectation). In change-point terms it is a Shewhart chart. The open question
  is whether an accumulating detector (CUSUM-type) catches the slow-building mornings earlier at the
  same false-alarm rate.

Ratings: **worth testing** (a concrete feature or evaluation the explore round could add cheaply),
**already covered** (an existing feature or hypothesis does this), **not worth** (with the reason).

---

## 1. Online change-point and sequential detection

### 1.1 What the methods are

| method | primary source | what it does | standing |
|---|---|---|---|
| CUSUM | Page, *Biometrika* 1954, "Continuous inspection schemes" | Accumulate the log-likelihood ratio of "changed" vs "normal", reset at zero, alarm at threshold h: `S_m = max(0, S_{m−1} + z_m − k)` | classic; optimality below |
| CUSUM optimality | Lorden, *Ann. Math. Stat.* 1971; Moustakides, *Ann. Stat.* 14(4):1379–1387, 1986, doi:10.1214/aos/1176350164 | CUSUM minimises the worst-case detection delay among all detectors with the same average run length to false alarm (ARL), for known before/after distributions | verified (Moustakides) |
| Shiryaev–Roberts (SR) | Shiryaev 1963; Roberts 1966; Pollak, *Ann. Stat.* 1985 | Sums likelihood ratios instead of taking the max; optimal for average (not worst-case) delay, and exactly optimal in the "multi-cyclic" setting where the detector runs for a long time and restarts after each alarm | classic |
| SR applied to finance | Pepelyshev & Polunchenko, *Statistics and Its Interface* 10(1):93–106, 2017, arXiv:1509.01570 | SR vs CUSUM on one stock (daily returns, one structural break) | verified; see 1.2 |
| GLR (unknown size of change) | Siegmund & Venkatraman, *Ann. Stat.* 23(1):255–271, 1995, doi:10.1214/aos/1176324466 | Maximises over the unknown post-change magnitude instead of fixing k; compared to CUSUM and CUSUM-Shewhart | verified |
| Page–Hinkley | Hinkley, *Biometrika* 1971 | CUSUM of deviations from the running mean; the streaming-ML community's name for a one-sided CUSUM with an estimated baseline | classic, not re-read |
| Bayesian online change-point (BOCPD) | Adams & MacKay 2007, arXiv:0710.3742; Fearnhead & Liu, *JRSS B* 2007 | Exact posterior over "run length since last change", hazard H(τ) as the prior on change timing, conjugate models | verified (Adams & MacKay) |
| BOCPD with model selection / robust | Knoblauch & Damoulas, ICML 2018 (PMLR 80:2718–2727); Knoblauch, Jewson & Damoulas, NeurIPS 2018 ("Doubly robust … β-divergences") | Unifies Adams–MacKay and Fearnhead–Liu; the robust version uses β-divergence so outliers do not trigger changes | verified |
| Count charts with a seasonal baseline | Höhle & Paul, *CSDA* 52(9):4357–4368, 2008 (R `surveillance::algo.glrnb`); review: Unkel, Farrington, Garthwaite, Robertson & Andrews, *JRSS A* 175(1):49–82, 2012, doi:10.1111/j.1467-985X.2011.00714.x | Poisson / negative-binomial GLR charts whose in-control mean is time-varying (log-linear with seasonal terms); built for disease-outbreak surveillance | verified |
| Seasonal Hybrid ESD | Hochenbaum, Vallis & Kejariwal (Twitter), arXiv:1704.07706, 2017 | Remove the seasonal component, then a robust (median/MAD) extreme-studentised-deviate test on the residual | verified; production anomaly detection, not finance |
| Kleinberg bursts | Kleinberg, KDD 2002 / *DMKD* 7:373–397, 2003 | Two-state (or infinite-state) automaton emitting events at a low or high rate; finds burst intervals in an event stream | verified (secondary) |

### 1.2 Evidence and numbers

- **Delay vs false alarm is a fixed trade-off, and the method matters less than the baseline.** For
  any detector, Lorden's bound gives delay ≈ log(ARL) / I, where I is the per-observation
  Kullback–Leibler distance between "normal" and "changed". CUSUM attains it (Moustakides 1986). The
  practical reading for this study (back-of-envelope, assuming the per-minute log-volume residual
  against the time-of-day profile has standard deviation about 1, which is **an assumption to be
  measured on control mornings, not a sourced number**):
  - ARL = 2,000 premarket minutes (one false alarm per ~6 mornings of 04:00–09:30) gives log(ARL) ≈ 7.6.
  - A sustained **3x** rise (shift ln 3 ≈ 1.1 sd, I ≈ 0.6) takes about **13 minutes** to confirm.
  - A sustained **10x** rise (shift ln 10 ≈ 2.3 sd, I ≈ 2.65) takes about **3 minutes**.
  - A one-minute 5x Shewhart rule (today's `minsSinceFirstAbnormal`) fires instantly on a single
    spike, but it has no memory: a morning that runs at 2–3x for 20 minutes may never trip it, and a
    lone block print trips it falsely.
  So no sequential method will give a large lead on a headline-driven jump (the jump itself is the
  first abnormal minute); the only place an accumulating detector can win is the **slow-building
  morning**, which is exactly what "detect while it forms" means.
- **Pepelyshev & Polunchenko (2017)** is the only finance application of SR vs CUSUM found. It is
  weak: one stock (HST), daily returns, one break located offline by Brodsky–Darkhovsky, thresholds
  set for ARL ≈ 77 samples, both detectors flag the break about one day late. Seasonality is
  "noted but left unmodeled". Useful as a template for calibrating h by Monte Carlo, not as evidence.
- **Adams & MacKay (2007)** report no detection-delay numbers; evaluation is qualitative (including
  daily Dow Jones returns 1972–75). Cost per step grows with run length unless pruned (they suggest
  dropping run lengths with probability below ~1e-4). The hazard H(τ) is where a prior on *when*
  changes happen can be put in, which matters here because onsets are not uniform in clock time.
- **Robust BOCPD** (Knoblauch et al. 2018) reports false-discovery rates of change points falling
  from "up to 99%" to 0% on real data (abstract figure; secondary listings quote different numbers).
  This is the documented cure for the failure mode this repo already hit (outliers and tick noise
  read as change points), but it is still a returns/Gaussian tool unless re-specified.
- **Seasonality handling, the key design point.** None of the generic change-point papers handle
  intraday seasonality themselves; they assume the in-control distribution is known. The fields that
  do handle it (disease surveillance, cloud monitoring) all do the same thing: **model the seasonal
  expected count first, then run the chart on the residual or with a time-varying in-control mean**
  (Höhle & Paul 2008; Unkel et al. 2012; Hochenbaum et al. 2017). The intraday-volume literature
  agrees: Brownlees, Cipollini & Gallo (*J. Fin. Econometrics* 9(3):489–518, 2011,
  doi:10.1093/jjfinec/nbq024) split volume into a daily component × an intraday periodic component ×
  an intraday dynamic component (CMEM) and show it beats rolling means for ETFs; Satish, Saxena &
  Palmer (*Journal of Trading* 9(3), 2014, FlexTrade, self-reported) report a 29% improvement over
  historical averages for remaining-day volume. The study's `profile_decile(t)` is the periodic
  component and `ownProfileRatio` mixes in the symbol's own history. **What it lacks is the daily
  (market-wide) component**: on a CPI morning or a market-wide news morning, every symbol's
  premarket volume is high and `pmVolRatio` inflates for all of them.

### 1.3 Mapping and proposals

**P1. Count CUSUM on minute volume and on minute trade count against the time-of-day profile.
Worth testing (top of the list).**

Definition, using only quantities the protocol already computes:

```
e_m   = ADV20 × (profile_decile(m) − profile_decile(m − 1))       # expected volume in minute m
c     = pseudocount, e.g. ADV20 × 1e-5 (frozen on train)           # keeps 04:00 minutes finite
z_m   = ln((v_m + c) / (e_m + c))                                   # log excess, 0 = normal
z'_m  = min(z_m, ln 20)                                             # cap: one block print cannot alarm alone
S_m   = max(0, S_{m−1} + z'_m − k),  k = ln 2                        # tuned to detect a doubling
alarm = first m with S_m >= h
```

`h` is set **on train control mornings only** so that the false-alarm rate on non-candidate U(D)
members, 04:00–09:30, is a pre-declared number (e.g. one alarm per 20 control mornings). The same
chart on trade count `n_m` needs a trade-count time-of-day profile (one more file next to
`data/tod-profile.json`, fit the same way). New features: `cusumVol(t)` (current S),
`minsSinceCusumAlarmVol(t)`, `cusumTrades(t)`, `minsSinceCusumAlarmTrades(t)`. Test: on forming
mornings (still below +20% at t), does the CUSUM alarm come earlier than `minsSinceFirstAbnormal`'s
first minute at equal false-alarm rate, and does `cusumVol(t)` add AUC over `pmVolRatio(t)` in the
ablation? Prior expectation, stated now: on headline mornings the two fire within a minute of each
other; any gain is on no-headline or slow mornings. Why trade count too: in the crypto P&D papers
(section 2) trade-count and aggressive-order features carry more weight than volume or price, and a
trade count is not moved by one large cross.

**P2. Market-day component / cross-sectional rank. Worth testing (cheap).** Following the CMEM
decomposition, divide `pmVolRatio(t)` by the median `pmVolRatio(t)` across U(D) at the same t
(`pmVolRatioMkt`), and add the cross-sectional percentile rank of `pmVolRatio(t)` and of `pmRet(t)`
within U(D) at t. Scanners rank by relative volume across the market at each moment; the study's
ratio is absolute. This also absorbs 08:30 macro releases. It is a few lines over data already in
the panel.

**P3. Clock-time hazard. Low priority.** In SR/BOCPD the prior on when a change occurs can depend on
clock time. Gap onsets are not uniform: press releases cluster at the top and bottom of the hour
before the open. A Shiryaev-style detector whose prior rises at 07:00, 07:30, 08:00, 08:30 would
alarm a little sooner at those minutes. The study's catalyst block already timestamps the
headline, so this only helps no-headline mornings; the train set can give the empirical onset-time
histogram first (a descriptive count, no new model) and that decides whether this is worth more.

**Not worth: BOCPD, HMM or CUSUM on minute returns of sub-$20 names.** Already tried here and failed
(sweep lines 71, 108–120). Returns on a 1-cent grid with many zero-volume, forward-filled minutes
are not Gaussian and not stationary in variance; the change-point machinery reads every tick
cluster as a regime. If a price-side chart is ever wanted, run it on the level `pmRet(t)` crossing
pre-declared steps (which `v_k` and `distToPmHigh` already describe), not on minute returns.

**Not worth separately: Kleinberg bursts, Page–Hinkley, S-H-ESD.** Each is a variant of P1 (bursts =
a two-state rate model, i.e. an HMM on counts; Page–Hinkley = CUSUM with a running-mean baseline,
which is worse than the study's profile baseline; S-H-ESD = a robust Shewhart test on residuals,
which is `minsSinceFirstAbnormal` with a median/MAD scale). If P1 shows nothing, these will not.

---

## 2. Early detectors of pump-and-dumps and attention spikes

### 2.1 Crypto pump-and-dump detectors (the best-measured onset detectors in the literature)

| paper | data | method | earliest features | detection time | result |
|---|---|---|---|---|---|
| Kamps & Kleinberg, *Crime Science* 7:18, 2018, doi:10.1186/s40163-018-0093-5 | exchange candles, several exchanges | flag a candle when **both** volume and price exceed thresholds computed from a rolling window of recent history; three settings (Initial/Balanced/Strict) | volume and price jointly | hourly candles: up to 60 min, ~30 min expected | no ground truth in the original; on La Morgia's labelled Binance set the Strict setting scores precision 50.1%, recall 75.0%, F1 60.5% (Balanced 38.4/93.5/54.4) |
| La Morgia, Mei, Sassi & Stefa, ICCCN 2020, arXiv:2005.06610 | 104 Binance pumps from 12 Telegram groups, ~7 days around each | random forest on 5/15/25-second chunks with moving-window features | **std of rush orders** (importance 0.40), std of trade count (0.20), mean rush orders (0.15); volume 0.10/0.08; price features ≤ 0.03 each | 25 s chunks best (F1 ~92%); 5 s chunks fastest (F1 ~84%) | "in some cases even before the start of the pump" when there is pre-pump activity; generally *after* the signal |
| La Morgia et al., *ACM TOIT* 23(1):11, 2023, doi:10.1145/3561300, arXiv:2105.00733 | ~900 events over 3+ years, 20 groups | extended version | as above | "detect a pump-and-dump in 25 seconds from the moment it starts" | F1 94.5% (abstract) |
| Xu & Livshits, USENIX Security 2019 | 412 pumps from 300+ Telegram channels, Jun 2018–Feb 2019 | predicts **which coin** will be pumped before the announcement, from pre-pump market data | pre-pump accumulation by organisers | before the announcement (hours) | AUC above 0.9 (secondary summary); trading strategy up to 60% over ~2.5 months (version-dependent) |
| Fantazzini & Xiao, *Econometrics* 11(3):22, 2023 (MPRA 118435) | 351 Binance pump signals 2021–22 | random forest, 30 s chunks, 1-hour moving-window regressors; positive label from the announcement back **60 minutes** | insiders' anticipated purchases | up to 60 min before the announcement | positive class < 0.2% of rows; best setup = original imbalanced data + 60-min early flag + RF; choosing the probability threshold near the prevalence beat resampling |

**What transfers and what does not.**

- **Rush orders → aggressive-order intensity.** The single most informative crypto feature is the
  dispersion of "rush orders" (many fills in the same millisecond = market buys sweeping the book).
  The equity analogue is sweep activity in SIP trades: several prints in the same millisecond at
  rising prices, or ISO-flagged prints. The study's trade-flow pull (IP-T460) has prints for **train
  gapper mornings only**, so this cannot be a detection feature without pulling the same window for
  controls/non-gappers, which competes with the rate limit the long job is using. Rating: **worth
  testing later**, once a control-morning trade pull is affordable; until then, trade count per
  minute (`n` from the bars, P1 above) is the available proxy and is already in the data.
- **Joint volume-and-price thresholds (Kamps & Kleinberg)** are `pmVolRatio > x AND pmRet/v_k > y`:
  **already covered** by H9 and the explore entry grid (volRatioTod × v3).
- **Detection "within 25 seconds" is not lead time.** Crypto pumps are announced at a known second
  and peak within minutes; the detectors confirm a pump that has started. The only genuine *lead*
  in this literature comes from **organisers buying before the announcement** (Xu & Livshits;
  Fantazzini & Xiao). The equity analogue is volume before the first headline. The study already
  measures it: the protocol reports how often the first abnormal minute precedes the first Alpaca
  headline, and H19 (amendment 02) tests the share of the move before publication. **Already
  covered.** If H19 finds most of the move follows the headline, the crypto result does not
  transfer, and that is consistent with `literature.md`'s FDA/insider priors (no abnormal stock
  volume before the catalyst).
- **Class imbalance handling (Fantazzini & Xiao).** Positives under 0.2% of rows; setting the
  decision threshold near the prevalence beat SMOTE-style resampling, and threshold-free metrics
  were unchanged by resampling. Relevant to the explore round's weighted learners: **already
  covered** in spirit (weighted AUC, no resampling), noted so nobody adds resampling later.

### 2.2 Stock-market manipulation and surveillance

- **Aggarwal & Wu, *Journal of Business* 79(4):1915–1954, 2006.** SEC manipulation cases
  1990–Oct 2001: prices rise during the manipulation and fall after; volatility, liquidity and
  returns are higher during it; small, illiquid stocks; insiders, brokers and market makers are
  likely manipulators. Daily granularity; no onset timing. Supports the universe and the fade
  hypotheses; adds no minute-level detector.
- **Leangarun, Tangamchit & Thajchayapong** (Stock Exchange of Thailand): 2016 conference paper,
  feed-forward net on level-1 data detects pump-and-dump at 88.28% (spoofing not detected); 2021
  *IEEE Access*, autoencoder/GAN on the limit order book trained on normal trading, 5 of 6
  prosecuted cases found with a low false-positive rate; 2023 *ESWA* ensemble (Chullamonthon &
  Tangamchit) 6 of 6. Tiny case counts, order-book data the study does not have. **Not worth** for
  this stack; noted as the only equity minute-level detectors found.
- **FINRA/NASD SONAR** (Goldberg, Kirkland, Lee, Shyr & Thakker, IAAI 2003): in production since
  December 2001, combines text mining of ~10,000 news stories and filings a day with price/volume
  regression models over ~25,000 securities, producing 50–60 "breaks" a day for analysts. It is a
  **post-hoc** surveillance design (news joined to abnormal price/volume), not an early warning.
  Its structure — abnormal price/volume model plus a news join — is what the study's catalyst block
  plus `pmVolRatio` already is. **Already covered.**
- No paper was found that measures **minute-level lead time of stock (not crypto) pump detection**
  from public bar data. Renault 2018 (in `literature.md`) found tweets often follow the move.

---

## 3. Abnormal volume and abnormal return tests at intraday frequency

### 3.1 Abnormal volume (event-study style)

- **Campbell & Wasley, *Rev. Quant. Fin. Acc.* 6(3):309–326, 1996.** For NASDAQ samples a
  **non-parametric (rank) test detects abnormal volume more powerfully** than the parametric
  standardized test; abnormal volume is detected less often in NASDAQ than NYSE/ASE samples. The
  log-volume specification often attributed to this paper was not checked against its text.
- **Mapping.** `pmVolRatio` is a ratio to a median profile (already robust to the mean being
  dragged by spikes). A rank version is P2 (cross-sectional rank at t). A **time-series rank** —
  today's cumulative volume at t ranked among the symbol's own last N days at t — would follow
  Campbell & Wasley more closely, but the study pulls only D−1..D−5 minute bars, so N = 5 and the
  rank has six possible values; `ownProfileRatio` already uses those five days. **Already covered**
  for the time-series side; the cross-sectional rank is the worthwhile addition (P2).

### 3.2 Jump tests

- **Lee & Mykland, *RFS* 21:2535–2563, 2008.** Each return divided by local bipower volatility over
  a window K; maximum statistic compared to a Gumbel critical value. Secondary sources give K = 10
  in the paper's numerical work and the R `yuima` default K = floor(sqrt(252·n)); the recommended-K
  table by sampling frequency was **not re-read**. Already in the study as `jumpFlag` /
  `x.leeMykland`.
- **Barndorff-Nielsen & Shephard (2004, 2006)**: bipower variation separates jump from diffusive
  variance at the day level (BNS test). Day-level, so it cannot time an onset. **Not worth** for
  onset.
- **Boudt, Croux & Laurent, *J. Empirical Finance* 18(2):353–367, 2011, doi:10.1016/j.jempfin.2010.11.005.**
  Opening/lunch/closing cause periodic volatility; jumps bias the usual periodicity estimators;
  **accounting for periodicity "greatly improves" intraday jump detection** (5-min FX data).
- **Bajgrowicz, Scaillet & Treccani, *Management Science* 62(8):2198–2217, 2016, doi:10.1287/mnsc.2015.2234.**
  Multiple testing across many intraday returns produces many spurious jumps — up to 90% of initial
  detections in Dow stocks 2006–08 (published abstract; an earlier version said 50%); most news does
  not cause jumps but bursts of volatility; most jumps have no news.
- **Maneesoonthorn, Martin & Forbes, arXiv:1708.09520 (forthcoming *J. Econometrics*, 2020)** compare
  jump tests' size and power under noise and volatility jumps; full text was not readable in this
  pass.
- **Mapping and rating.** The study's `x.leeMykland` uses a local bipower window on forward-filled
  1-minute premarket closes. Two of these papers say why it will be noisy here: (a) no periodicity
  correction (Boudt et al.) — premarket volatility per minute differs hugely between 04:00 and 09:25;
  (b) forward-filled zero-return minutes shrink the bipower estimate, so the next real print looks
  like a jump (the same tick-grid problem that broke BOCPD). A cheap repair if the flag is kept:
  compute it in **trade time** (only minutes with volume) and divide returns by a time-of-day
  volatility profile fit on control mornings, the way `profile_decile` does for volume. Even
  repaired, a jump test **confirms** a move rather than leading it (Lee–Mykland over-flags the first
  30 minutes; `literature.md` §7). **Not worth** as an onset detector; the repair is worth doing only
  if the flag stays in the ablation, so its "nothing" result is not just a broken estimator.

### 3.3 Predictive content of early premarket / opening volume and return

- **Barclay & Hendershott, *RFS* 16(4):1041–1073, 2003.** Pre-open price changes are larger, carry
  more private information and are less noisy than after-close changes; low after-hours volume
  still produces significant (but inefficient) price discovery; more volume, more efficient prices.
  **Barclay & Hendershott, *JF* 59(2):681–710, 2004**: after-hours spreads are 3–4x daytime spreads
  (adverse selection, order persistence), with far fewer trades per unit time (<1/20, secondary).
- **Jiang, Likitapiwat & McInish, *JFQA* 47(6):1303–1330, 2012.** S&P 500, 2004–08: after-hours
  trading rises on earnings days and much of the price discovery happens right after the release.
  Large caps; the pre-open breakdown was seen only in a related dissertation.
- **Gao, Han, Li & Zhou, *JFE* 129(2):394–414, 2018.** SPY: first half-hour return (from prior close)
  predicts the last half-hour; R² about 1.6% in a working-paper version (2.6% adding the 12th
  half-hour; out-of-sample ~1.2%). The published table values were not re-read. Index only;
  `literature.md` already lists it as not a small-cap result.
- **Zarattini, Barbon & Aziz, SFI Research Paper 24-98 / SSRN 4729284, 2024** (the RTH
  "stocks in play" ORB paper): see section 4.
- No peer-reviewed paper was found that tests premarket volume (scaled by time of day) as a
  predictor of the same day's range or return in small caps. `literature.md` reached the same
  conclusion. H1/H9 remain first-of-kind.
- **Mapping and proposal. P4. Volume-confirmed gap. Worth testing (cheap).** Barclay & Hendershott's
  point is that a premarket price is informative in proportion to the trading behind it. The study's
  `pmRet(t)` is the last print, which on a thin morning can be one odd-lot. Add
  `pmVwapRet(t) = pmVWAP(04:00..t) / prevClose − 1` and the ratio `pmVwapRet / pmRet` (how much of
  the gap has been traded through, vs printed on a few trades), and the share of `pmDollarVol(t)`
  done above `prevClose × 1.10`. These are from minute bars already in the store. Expectation: they
  sharpen `pmRet` as a predictor and reduce false positives from thin early prints; they do not
  create lead time.

---

## 4. Practitioner "gap and go" / relative-volume scanner backtests

| source | criteria | claim | standing |
|---|---|---|---|
| Zarattini, Barbon & Aziz 2024 (SFI 24-98, SSRN 4729284) | RTH 5-min opening-range breakout on the top 20 "stocks in play" by relative volume, US stocks 2016–2023 | >1,600% total net, Sharpe 2.81, annualised alpha 36% after costs (abstract) | working paper, the only credible test found; **co-author Aziz runs a day-trading education business** (Bear Bull Traders); universe filters (price/ATR/volume floors) not re-read; RTH, not premarket; broad universe, not sub-$20 gappers |
| Warrior Trading criteria (price $2–20, float < 10–20M, RVOL > 5x, gap > 5–10%, news) | scanner rules | no published win rate or methodology | **unaudited vendor rules** (already in `literature.md`) |
| Mometic 30-day "Warrior-style" backtest | momentum scanner entries | 64.29% win rate; ~4x entry volume did better than 5x | **unaudited vendor**, 30-day sample |
| QuantConnect community backtests (gap-up / premarket gapper variants) | various; one with a $500k premarket dollar-volume filter | 53% win, P/L ratio 1.18 (one run); 43% win, P/L 1.45, ~2% net, 14% drawdown over 296 orders (another) | **unaudited**, undocumented universes and periods; both near break-even |
| SmallCapLab, ChartMath, TradingView scripts, QuantSync | gap statistics, win rates | see `literature.md` §5, §8 | **unaudited vendor**, already recorded |

**Rating: already covered.** H9 tests the practitioner `RVOL > 5` number, the explore entry grid tests
volRatioTod >= 3/5/10, and the float proxy (IP-T459, `x.floatRotation`, `x.floatLogShares`) stands in
for the float filter. The one result with a stated method (Zarattini et al.) is a regular-hours
continuation effect on a broad universe ranked by **cross-sectional** relative volume — which is the
argument for P2, not for any new practitioner rule. No published, audited backtest of a premarket
small-cap gap-and-go scanner was found. This study is that backtest.

---

## 5. A missing evaluation: lead time vs false alarms

**P0. Alarm-time evaluation. Worth testing (do this first; it is a scoring method, not a feature).**

Everything in sections 1–2 is judged in the change-point literature by **detection delay against
false-alarm rate**, not by AUC at a fixed clock time. The study's tracks score AUC/precision at
decision times (04:00 … 09:25) on forming mornings, which answers "how separable at 08:00", but not
"how many minutes before the stock was up 10% / 20% did the detector fire, and how many times did it
fire on mornings that went nowhere". Proposal for the explore round (train only):

- For each detector D (Shewhart `minsSinceFirstAbnormal`, CUSUM P1 on volume, CUSUM on trades,
  H9's `v_3 > 1 AND pmVolRatio > 5`, the logistic/GBT score crossing a threshold, and the
  catalyst-latency benchmark "headline with pubToTMinutes <= 60"), record the **first alarm time**
  τ_D on every population morning 04:00–09:30.
- On Tier20 mornings: **lead20 = t(first touch of +20%) − τ_D** and **lead10** likewise; also the
  price paid: `pmRet(τ_D)`, i.e. how much of the move was already gone at the alarm.
- On non-gapper mornings: **false alarms per 100 mornings**.
- Plot lead vs false-alarm rate as h varies (the operating curve), per fold, by news vs no news.
- A detector is interesting only where it alarms **with `pmRet(τ) < +10%`** at a false-alarm rate a
  human could triage (pre-declare, e.g., at most 20 alarms per morning across U(D)).

This is a direct, honest version of "detect while forming", and it puts the catalyst benchmark and
the volume detectors on the same axis. It needs no new data.

---

## Ranked shortlist

| rank | item | type | cost | rating | why |
|---|---|---|---|---|---|
| 1 | **P0 alarm-time evaluation** (lead to +10%/+20% vs false alarms per 100 mornings, price already moved at alarm) | scoring | low (bars in store) | worth testing | The metric the whole change-point literature uses; AUC at fixed times cannot show lead. Puts every detector and the catalyst benchmark on one curve. |
| 2 | **P1 count CUSUM on minute volume and trade count vs the time-of-day profile** (log excess, capped, k = ln 2, h fixed on control mornings) | feature | low (needs a trade-count ToD profile) | worth testing | Optimal accumulator for a given false-alarm budget (Lorden/Moustakides); the only place a sequential method can beat the existing one-minute 5x rule is the slow-building morning. Trade count mirrors the crypto finding that order-count features fire before price. |
| 3 | **P2 market-day normalisation and cross-sectional rank** of `pmVolRatio(t)` and `pmRet(t)` within U(D) | feature | very low | worth testing | Missing daily component of the CMEM decomposition; scanners (and the one credible practitioner paper) rank cross-sectionally; absorbs 08:30 macro mornings. |
| 4 | **P4 volume-confirmed gap**: `pmVwapRet(t)`, `pmVwapRet/pmRet`, share of dollar volume above +10% | feature | very low | worth testing | Barclay–Hendershott: premarket price is informative in proportion to trading behind it; cuts thin-print false positives. Expect sharper, not earlier. |
| 5 | Rush-order / sweep intensity from SIP prints | feature | high (needs control-morning trade pulls; rate-limited) | worth testing later | Strongest crypto onset feature (importance 0.40), but cannot be a detection feature while only gapper mornings are pulled. |
| 6 | P3 clock-time hazard (onset prior peaking at :00/:30 before the open) | feature | medium | low priority | Run the descriptive onset-time histogram first; only helps no-headline mornings. |

**Not worth (with reason):** BOCPD/HMM/CUSUM on penny minute **returns** (already failed here, tick
grid); Lee–Mykland/BNS as onset detectors (confirm, not lead; spurious-jump rates up to 90%; repair
only if the flag stays in ablation); Kleinberg bursts, Page–Hinkley, S-H-ESD (variants of P1 with a
weaker baseline); Gao et al. intraday momentum (index, end-of-day); equity LOB manipulation detectors
(need order-book data, 6 cases).

**Already covered:** joint volume+price thresholds (H9, entry grid); pre-announcement accumulation
(H19, first-abnormal-minute vs first-headline report); SONAR-style news + abnormal volume join
(catalyst block + `pmVolRatio`); practitioner RVOL/float rules (H9, entry grid, float proxy);
time-series own-history baseline (`ownProfileRatio`); imbalanced-class handling (weighted learners).

**Honest prior for P1–P4.** A volume CUSUM cannot see a move before its first abnormal trading,
and on headline mornings that trading starts with the headline. The realistic best case is a few
minutes' gain on no-headline, slow-building mornings plus fewer false alarms than the one-minute
rule. If P0 shows the CUSUM curve sitting on top of the Shewhart curve, drop it.

---

## Sources (verified in this pass: title, authors, venue confirmed by search or fetch)

- Adams & MacKay 2007, arXiv:0710.3742 — https://arxiv.org/abs/0710.3742 (abstract and body read via ar5iv)
- Moustakides 1986, *Ann. Stat.* 14(4):1379–1387 — https://doi.org/10.1214/aos/1176350164
- Siegmund & Venkatraman 1995, *Ann. Stat.* 23(1):255–271 — https://doi.org/10.1214/aos/1176324466
- Pepelyshev & Polunchenko 2017, *Statistics and Its Interface* 10(1):93–106 — https://arxiv.org/abs/1509.01570 (read via ar5iv)
- Knoblauch & Damoulas, ICML 2018 — https://proceedings.mlr.press/v80/knoblauch18a.html
- Knoblauch, Jewson & Damoulas, NeurIPS 2018 — https://proceedings.neurips.cc/paper/2018/hash/a3f390d88e4c41f2747bfa2f1b5f87db-Abstract.html
- Höhle & Paul 2008, *CSDA* 52(9):4357–4368 — https://econpapers.repec.org/RePEc:eee:csdana:v:52:y:2008:i:9:p:4357-4368
- Unkel et al. 2012, *JRSS A* 175(1):49–82 — https://doi.org/10.1111/j.1467-985X.2011.00714.x
- Hochenbaum, Vallis & Kejariwal 2017 — https://arxiv.org/abs/1704.07706
- Kleinberg 2002/2003 — bibliographic record via secondary sources (KDD 2002; *DMKD* 7:373–397)
- Brownlees, Cipollini & Gallo 2011, *JFEc* 9(3):489–518 — https://doi.org/10.1093/jjfinec/nbq024
- Satish, Saxena & Palmer 2014, *Journal of Trading* 9(3) — https://flextrade.com/resources/predicting-intraday-trading-volume-and-volume-percentages/
- Kamps & Kleinberg 2018, *Crime Science* 7:18 — https://doi.org/10.1186/s40163-018-0093-5 (abstract only; numbers from La Morgia et al.'s re-evaluation)
- La Morgia, Mei, Sassi & Stefa 2020, ICCCN — https://arxiv.org/abs/2005.06610 (read via ar5iv, tables III–IV)
- La Morgia et al. 2023, *ACM TOIT* 23(1):11 — https://arxiv.org/abs/2105.00733 (abstract)
- Xu & Livshits 2019, USENIX Security — https://www.usenix.org/conference/usenixsecurity19/presentation/xu-jiahua
- Fantazzini & Xiao 2023, *Econometrics* 11(3):22 — https://mdpi.com/2225-1146/11/3/22
- Aggarwal & Wu 2006, *J. Business* 79(4):1915–1954 — https://www.bauer.uh.edu/wu/Papers/JOB2006_StockManipu.pdf
- Leangarun / Thajchayapong / Tangamchit (SET) — https://kirim.kmutt.ac.th/converis/portal/detail/Publication/825155564?lang=en_GB
- Goldberg et al. 2003, SONAR, IAAI — https://www.aaai.org/Papers/IAAI/2003/IAAI03-002.pdf
- Campbell & Wasley 1996, *RQFA* 6(3):309–326 — https://ideas.repec.org/a/kap/rqfnac/v6y1996i3p309-26.html
- Boudt, Croux & Laurent 2011, *JEF* 18(2):353–367 — https://doi.org/10.1016/j.jempfin.2010.11.005
- Bajgrowicz, Scaillet & Treccani 2016, *Mgmt Sci* 62(8):2198–2217 — https://doi.org/10.1287/mnsc.2015.2234
- Maneesoonthorn, Martin & Forbes — https://arxiv.org/abs/1708.09520
- Barclay & Hendershott 2003, *RFS* 16(4):1041–1073 — https://ideas.repec.org/a/oup/rfinst/v16y2003i4p1041-1073.html
- Barclay & Hendershott 2004, *JF* 59(2):681–710 — https://doi.org/10.1111/j.1540-6261.2004.00646.x
- Jiang, Likitapiwat & McInish 2012, *JFQA* 47(6):1303–1330 — https://doi.org/10.1017/S002210901200049X
- Gao, Han, Li & Zhou 2018, *JFE* 129(2):394–414 — https://econpapers.repec.org/RePEc:eee:jfinec:v:129:y:2018:i:2:p:394-414
- Zarattini, Barbon & Aziz 2024, SFI 24-98 — https://ideas.repec.org/p/chf/rpseri/rp2498.html

## Sources not verified (cited from memory or only through secondary summaries)

- **Page 1954 (*Biometrika*), Lorden 1971 (*Ann. Math. Stat.*), Shiryaev 1963, Roberts 1966,
  Pollak 1985 (*Ann. Stat.*), Hinkley 1971 (*Biometrika*), Fearnhead & Liu 2007 (*JRSS B*)**:
  standard references, venue/year from memory, not re-checked in this pass.
- **Barndorff-Nielsen & Shephard 2004/2006** (bipower variation, *JFEc*): from memory.
- **Lee & Mykland 2008**: the recommended-K-by-frequency table and the "over-flags the first 30
  minutes" point are from secondary sources and `literature.md`, not re-read.
- **Kamps & Kleinberg 2018**: the exact rolling-window lengths and threshold multipliers were not
  retrievable (publisher redirect / repository 403); only the structure and La Morgia et al.'s
  re-scoring are reported.
- **La Morgia et al.**: the 2020 paper states both "25 seconds" (introduction) and "within 5 seconds"
  (results); the 2023 abstract says 25 s and F1 94.5%. The inconsistency is the papers', not resolved.
- **Xu & Livshits 2019**: "AUC above 0.9" comes from a secondary summary; event counts and returns
  differ between arXiv and USENIX versions.
- **Fantazzini & Xiao 2023**: the abstract was read; no performance numbers (AUC, precision) were
  extracted.
- **Gao et al. 2018**: R² values (1.6% / 2.6% / 1.2% OOS) are from a working-paper version; the
  published tables were not read.
- **Zarattini, Barbon & Aziz 2024**: headline numbers from the abstract; the stocks-in-play filter
  details (price, ATR and volume floors, relative-volume cutoff) were not read.
- **Barclay & Hendershott 2004**: the "<1/20 the trades per unit time" figure is from a secondary
  abstract.
- **Campbell & Wasley 1996**: the log-volume specification is from a secondary source.
- **Leangarun et al. 2016** 88.28% figure and the 2021/2023 case counts: from repository abstracts.
- **QuantConnect, Mometic** backtests: snippets only; universes, periods and costs undocumented.
- **The delay arithmetic in §1.2** (13 min for a 3x rise, 3 min for 10x) assumes a residual sd of 1
  and Gaussian-like residuals; it is an illustration of Lorden's bound, not a sourced or measured
  number. Measure the residual distribution on train control mornings before relying on it.
