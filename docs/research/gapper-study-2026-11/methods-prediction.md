# Gapper study: methods for predicting rare extreme moves (IP-T464)

Research pass, 2026-11-08. Web sources plus a read of the study's own files (`protocol.md`,
`hypotheses.json`, `params.json`, `sample.json`, `explore-plan.md`, the amendments, `literature.md`,
`avenues.md`, `methods-onset.md`, `methods-exit.md`) and of the universe and explore code they
describe. No code was changed, nothing was run, and no SEC/EDGAR or Alpaca call was made.

It covers three things: (1) what published work reports when it tries to predict extreme next-day
or intraday moves, and which features carried the signal; (2) how to evaluate a rare-event
classifier and limit multiple-testing damage so that what the exploration round finds survives
validation and the holdout; (3) survivorship and look-ahead traps in small-cap intraday data,
checked against this protocol. It does not repeat what is already in `literature.md` (MAX, Robinhood
herding, jump tests, pump detectors and so on appear there) or the onset and exit methods files; it
cites those only where a new number or a new consequence is added.

Ratings: **do first** (cheap and it changes how results are read), **worth testing**, **low
priority**, **not worth**. Every citation gives authors, year and venue. Section *Sources not
verified* lists what was cited from memory or only from secondary summaries.

---

## Summary

- **Nobody publishes the number this study needs.** No peer-reviewed paper found reports
  out-of-sample AUC or precision for "will this small cap be up 20% by mid-morning". The closest
  published work predicts *monthly* returns (Gu, Kelly & Xiu 2020: monthly stock-level R² of about
  0.4%), *one-year* jackpots (Conrad, Kapadia & Xing 2014), *firm crashes* from annual reports
  (Kaya et al. 2025), or *minute-ahead* returns (Chinco et al. 2019; Aït-Sahalia et al. 2022).
  Across all of them the signal sits in small, cheap, illiquid, volatile, attention-heavy stocks, and
  much of it disappears after trading costs or once microcaps are removed (Avramov, Cheng & Metzker
  2023; Hou, Xue & Zhang 2020).
- **Four protocol gaps were found** (details in section 3 and in the ranked list):
  1. **The unexplained-move guard looks at D's own close.** `GapperUniverseBuilder.HasUnexplainedMove`
     scans bars up to *and including* D, so a symbol whose close on D is more than +200% (or less
     than −66.7%) above (below) D−1's close is dropped from U(D) using D's outcome, and then for about
     17 more sessions. That removes some of the largest gappers from the study on the very day they
     happen. It is look-ahead in the universe definition.
  2. **The split exclusion window hides H7.** Days with a split ex-date in [D−25, D+1] are excluded,
     but H7 asks about reverse splits "within the prior 30 days". Only reverse splits 26–30 calendar
     days earlier can reach H7, and the whole class of post-reverse-split runners (a known gapper
     type) is outside the sample.
  3. **H11's benchmark is binary, so beating it by 0.05 AUC is close to automatic.** A yes/no
     feature has AUC = ½(1 + TPR − FPR). Any continuous detector that includes `pmRet(t)` (the
     top-gainers screen itself) is likely to clear it. Passing H11 then does not show that the
     detector adds anything beyond "it is already up and there is news". The exploration round
     should add a screen benchmark (section 1.6).
  4. **The candidate rule may miss premarket-only spikes.** Candidates use D's *daily* bar high.
     Alpaca forum answers say daily open/high/low/close cover regular hours only, while volume and
     VWAP include extended hours (not confirmed in Alpaca's own docs). If so, a stock that ran 30%
     premarket and faded before 09:30 becomes a candidate only through the 3× volume rule. The 10%
     sample of amendment 01 can measure how many are missed.
- **The base rate roughly doubles after train**, from `sample.json`. Tier20 per universe day:
  train 0.41%, validation 0.89%, holdout 0.82%. Per candidate day: 6.5%, 11.0%, 10.5%. Per trading
  day: about 9 Tier20 gappers in train against about 23 in validation. AUC is unaffected by this kind
  of shift. Precision, calibrated probabilities and fixed score thresholds are affected. The planned
  "precision at top 50 per day" cannot exceed about 18% on an average train day, because only about
  9 positives exist (fewer still under the forming-only filter).
- **The exploration round's main risk is selection, not leakage.** The folds and embargo are sound.
  What still needs adding: a lockbox inside train (fold 4 kept back for finalists), per-month metrics
  in the ledger so that the probability of backtest overfitting (CSCV) can be computed, an
  effective-trial count for the deflated Sharpe, a max-statistic null for AUC trials, within-day
  metrics, and fold-averaged rather than score-concatenated pooled AUCs.

---

## 1. Published prediction of extreme moves

### 1.1 Lottery stocks: MAX and predicted "jackpots"

- **Bali, Cakici & Whitelaw, "Maxing out: Stocks as lotteries and the cross-section of expected
  returns", *Journal of Financial Economics* 99(2):427–446, 2011.** The highest-MAX decile (largest
  single-day return in the past month) earns over 1% a month less than the lowest, raw and
  risk-adjusted, controlling for size, value, momentum, short-term reversal, liquidity and skewness.
  Gorman, Akhtar, Durand & Gould (*Critical Finance Review*, 2022, working-paper version read only as
  a summary) argue the effect reflects overreaction after any high, not lottery demand as such.
  **Mapping:** this is a monthly underperformance result, not a prediction of the next spike. It
  says a stock that has already spiked attracts buyers, and that is the reason to keep
  `priorDayTopMoverRank`, `ret_j` and `runUp5` in the feature set. It does not predict a gap.
- **Conrad, Kapadia & Xing, "Death and jackpot: Why do individual investors hold overpriced
  stocks?", *JFE* 113(3):455–475, 2014.** This is the one published model that *predicts* extreme
  positive returns cross-sectionally: a logit for a "jackpot" (a very large 12-month log return)
  built from firm characteristics. Stocks with high predicted jackpot probability underperform, and
  they overlap heavily with high predicted distress. A secondary summary reports that 63% of realized
  jackpots were in the top 1% of predicted probability; that number was not read in the paper.
  **Features that carried the signal** (from the paper's framing, not re-read in full): small size,
  low price, high volatility and skewness, distress, young firms, low institutional ownership.
  **Mapping:** the same "death and jackpot" profile describes the study's universe. The float proxy,
  dilution and reverse-split blocks are its closest equivalents on this stack. The rare event is
  concentrated, so ranking can work well even when absolute probabilities are tiny. That supports
  scoring on precision@k and lift, not accuracy.

### 1.2 Retail attention and herding

- **Barber, Huang, Odean & Schwarz, "Attention-induced trading and returns: Evidence from Robinhood
  users", *Journal of Finance* 77(6):3141–3190, 2022, doi:10.1111/jofi.13183.** Already in
  `literature.md` for its reversal numbers. The point that matters here: herding events (top 0.5% of
  daily user-count increases, or more than 1,000 users and +50%) can be **forecast from lagged
  attention measures: lagged absolute returns and lagged abnormal volume** (secondary summaries of
  the SSRN version). Herding-day abnormal return averages +14% (+42% for the extreme definition),
  then reverses. Top-bought stocks show −4.7% 20-day abnormal return. **Mapping:** this is the
  published support for H6 (D−1 abnormal volume) and H15 (prior-day top-mover rank). It is daily, and
  it comes from 2018–2020 Robinhood data.
- **Welch, "The wisdom of the Robinhood crowd", *Journal of Finance* 77(3):1489–1527, 2022,
  doi:10.1111/jofi.13128.** The aggregate Robinhood portfolio leaned toward stocks with high past
  share and dollar volume, mostly *large* stocks, and timed well in 2018–2020. **Mapping:** low
  relevance. It describes the crowd's average portfolio, not small-cap spikes. It is cited so that
  no one quotes it as evidence for gapper prediction.

### 1.3 Meme-stock prediction

The search found GME/AMC case studies only: Anand & Pathak (IIM Bangalore working paper 644, 2021),
in which WSB activity predicts GME returns and volatility out of sample; Cruz, Kinyua & Mutigwe
(*Intelligent Automation & Soft Computing* 36(3)), which combines Reddit sentiment with 85
indicators; and student theses. None reports cross-sectional out-of-sample precision for "which
stock surges next". Bradley et al. (RFS 2024, in `literature.md`) is still the best evidence, and it
shows the edge disappeared after GME. **Rating: not worth** pursuing as a model source. Reddit itself
is avenue A9 in `avenues.md`.

### 1.4 Jumps and crashes

- **Kaya, Reichmann & Reichmann, "Out-of-sample predictability of firm-specific stock price
  crashes: A machine learning approach", *Journal of Business Finance & Accounting* 52(2):1095–1115,
  2025.** Stochastic gradient boosting beats logistic regression consistently. Logistic regression
  on financial inputs is "reasonably good" and sometimes beats random forests and neural networks.
  Adding text features helps significantly. The AUC values were not readable (SSRN and the
  repository both blocked automated access). **Mapping:** this is the closest published analogue to
  the explore track's choice of logistic regression against boosted trees: expect boosted trees to
  win by a modest margin, and expect logistic regression to be a strong baseline. That backs the plan
  of keeping both and reporting the gap between them.
- **Tran, Iosifidis, Kanniainen & Gabbouj (arXiv 1810.10845, 2018), jump forecasting from
  limit-order-book data.** It scores with F1 because accuracy is meaningless when jumps are rare
  ("always predict no jump" scores well on accuracy). It needs order-book data, so it is not
  reproducible here, but its metric choice is the point (section 2).
- **Aït-Sahalia, Fan, Xue & Zhou, "How and when are high-frequency stock returns predictable?",
  NBER w30366, 2022; later in *Management Science* (the author list differs in that version, not
  checked).** High-frequency predictability is pervasive but **strongest in lower-priced, less
  liquid stocks** and **decays to a coin toss within about five minutes**. A small processing lag
  destroys it. **Mapping:** this supports the onset file's view that minute-level features carry
  information. It also warns that any edge found at a decision time t is only worth something if it
  can be acted on at t+1 bar (the protocol's next-bar-open entry already enforces this).
- **Chinco, Clark-Joseph & Ye, "Sparse signals in the cross-section of returns", *Journal of
  Finance* 74(1):449–492, 2019, doi:10.1111/jofi.12733.** A rolling LASSO over every stock's lagged
  one-minute returns raises out-of-sample one-minute-ahead fit by at least 1.2 percentage points over
  a benchmark. The useful predictors are "unexpected, short-lived and sparse", often the lagged
  returns of stocks with fundamental news. **Mapping:** this is the published form of the
  **sympathy** feature (`x.sympathyFormed20`, `x.sympathyForming10`): another stock's move carries
  information for minutes. It also explains why sympathy must be counted strictly at t.

### 1.5 Machine learning on cross-sections

- **Gu, Kelly & Xiu, "Empirical asset pricing via machine learning", *Review of Financial Studies*
  33(5):2223–2273, 2020.** Trees and neural networks are best. Monthly stock-level out-of-sample R²
  is about 0.33–0.40% for trees and networks against about 0.26% for penalized linear models
  (secondary summary). A linear model with about 900 predictors and no penalty has *negative*
  out-of-sample R². The dominant predictors are **momentum, liquidity and volatility**, and the gains
  come from nonlinear interactions. **Mapping:** use shallow trees with strong regularization, and
  expect the winning features to be the analogues of those three: `pmRet` and velocity, dollar volume
  against the time-of-day profile, and the spread proxy. An unregularized wide model will overfit.
- **Avramov, Cheng & Metzker, "Machine learning vs. economic restrictions: Evidence from stock
  return predictability", *Management Science* 69(5):2587–2619, 2023, doi:10.1287/mnsc.2022.4449.**
  ML predictability is concentrated in microcaps, distressed and unrated firms. Press summaries put
  the drop in adjusted returns at 62% when microcaps are excluded and 80% when distressed firms are
  excluded. Performance worsens further after realistic trading costs because of high turnover and
  extreme positions. **Mapping:** the study's universe is exactly where ML "works" on paper and where
  costs are worst. The frozen cost model (Corwin–Schultz spread × calibrated multiplier, square-root
  impact, 0.5% floor) is the right defence. No detection AUC should be presented without the
  matching net-of-cost entry result.
- **Leippold, Wang & Zhou, "Machine learning in the Chinese stock market", *JFE* 145(2):64–82, 2022,
  doi:10.1016/j.jfineco.2021.08.017.** In a retail-dominated market, liquidity is the top predictor,
  and retail presence raises short-term predictability, especially for small stocks. **Mapping:**
  the closest market structure to US sub-$20 premarket trading in the literature. It supports
  putting the liquidity block (ADV decile, spread, dollar-volume floor) early in the ablation order.
- **Krauss, Do & Huck, "Deep neural networks, gradient-boosted trees, random forests: Statistical
  arbitrage on the S&P 500", *European Journal of Operational Research* 259(2):689–702, 2017.** An
  ensemble of a network, boosted trees and a random forest makes over 0.45% a day before costs
  (1992–2015, working-paper figure), with returns declining in later years. It removed survivor bias
  with historical index membership. **Mapping:** a methods precedent: an ensemble of three simple
  learners, every variant reported, and an explicit survivorship fix.
- **Hou, Xue & Zhang, "Replicating anomalies", *RFS* 33(5):2019–2133, 2020.** With microcaps
  controlled through NYSE breakpoints and value weighting, 286 of 447 anomalies (64%) are
  insignificant at 5%, and 85% fail at Harvey–Liu–Zhu's t = 3. Microcaps are 3% of market cap but 60%
  of stocks, and have the widest dispersion. **Mapping:** the study is all microcap, so equal
  weighting by symbol-day is natural. The same fact means one wild stock or one wild day can produce
  a result; that is why leave-one-month-out (finalist criterion 3) and day-clustered intervals
  matter.

### 1.6 What AUC to expect, and the benchmark problem

No published small-cap gapper AUC exists. The nearest published numbers are crypto pump detection
at AUC above 0.9 (in `literature.md` and `methods-onset.md`; coordinators accumulate first, which does
not carry over) and bankruptcy prediction at about 0.9 (a different, slow-moving problem). On the
study's own setup, a model at 08:00 with `pmRet(t)` and `pmVolRatio(t)` can be expected to reach a
high AUC among forming mornings simply because the move has started. The protocol's own Purpose,
question (2), asks for information "that was not already on a 'top gainers' screen".

**Gap in H11 (protocol, not editable).** H11 compares a logistic detector with a **binary**
benchmark (`pubToTMinutes <= 60` exists). A binary score's ROC curve has a single interior point, so
AUC_benchmark = ½(1 + TPR − FPR). For example, if 40% of eventual gappers and 10% of others have a
fresh headline at 08:00, the benchmark's AUC is 0.65. Any continuous score that includes the current
gap will probably reach 0.70. H11 can pass while the detector adds nothing beyond "already up, and
there is news".

**Recommendation (exploration, train only; register in amendment 04): do first.** Score every
detector against three benchmarks:
- **B1 catalyst:** the H11 indicator, kept for continuity.
- **B2 screen:** rank by `pmRet(t)` within U(D) at t (a "top gainers" list), and a second version
  ranking by `pmRet(t) × log pmDollarVol(t)`.
- **B3 screen + catalyst:** a two-feature logistic regression on `pmRet(t)` and the B1 indicator,
  fit on the fold's fit window.

The claim worth carrying to validation is **ΔAUC and Δprecision@k over B3**, not over B1. If nothing
beats B3, the honest finding is "screen plus news is the edge". That is a sharper version of the
protocol's own prior, and it is cheap: every input already exists.

---

## 2. Rare-event classification practice for this study

### 2.1 Base rates, and what they do to each metric

From `sample.json` (universe build, 2026-11-06):

| split | universe days | candidates | Tier20 | Tier20 / universe | Tier20 / candidates | trading days (approx.) | Tier20 per day |
|---|---|---|---|---|---|---|---|
| train | 1,669,297 | 105,964 | 6,855 | 0.41% | 6.5% | ~750 | ~9.1 |
| validation | 640,612 | 51,797 | 5,679 | 0.89% | 11.0% | ~250 | ~22.7 |
| holdout | 714,512 | 56,009 | 5,880 | 0.82% | 10.5% | ~250 | ~23.5 |

(The protocol expected about 60,000 candidates and 12,000 Tier20 in total. The build produced 213,915
candidates; the Tier20 total of 18,414 is above expectation, and train clears the 3,000 kill floor.)

Consequences:

- **AUC does not depend on prevalence, but precision does.** For a fixed ranking, precision at a
  fixed score threshold rises with the base rate. A rule tuned on train therefore looks *better* on
  validation for a reason that has nothing to do with skill. Lift (precision ÷ base rate) is far more
  stable under this shift, and that is the measure H1/H9/H15 use. Keep lift as the headline number
  and always print the base rate next to precision.
- **Precision at top 50 per day has a ceiling.** With about 9 positives on an average train day, and
  fewer under the forming-only filter, the best possible precision@50 is about 18% in train and about
  45% in validation. The ceiling moves with the regime, so this number does not compare across folds
  or splits. **Recommendation: do first.** Report precision@k for k ∈ {5, 10, 20} and recall@k per
  day, plus **ceiling-normalized precision@k** = precision@k ÷ min(1, P_d ÷ k), where P_d is the
  number of positives that day, averaged over days. Also report **average precision**
  (precision–recall area): Saito & Rehmsmeier, "The precision-recall plot is more informative than
  the ROC plot when evaluating binary classifiers on imbalanced datasets", *PLOS ONE* 10(3):e0118432,
  2015, doi:10.1371/journal.pone.0118432, show that ROC can look reassuring on imbalanced data while
  precision–recall shows the real false-alarm burden.
- **Calibrated probabilities do not carry across a prior shift.** A model calibrated on 0.41% will
  under-predict on 0.89%. Saerens, Latinne & Decaestecker, "Adjusting the outputs of a classifier to
  new a priori probabilities: A simple procedure", *Neural Computation* 14(1):21–41, 2002,
  doi:10.1162/089976602753284446, give the EM correction that re-estimates the new prior from
  unlabeled scores and rescales the outputs without refitting. **Recommendation: worth testing.**
  Any live trigger in `formation-trigger.json` should be a **rank or within-day quantile** ("top 5 at
  t"), not a raw probability, because a probability threshold fires about twice as often in a
  regime like validation.

### 2.2 Do not resample; weight and calibrate

van den Goorbergh, van Smeden, Timmerman & Van Calster, "The harm of class imbalance corrections for
risk prediction models", *JAMIA* 29(9):1525–1534, 2022, doi:10.1093/jamia/ocac093: random under- or
over-sampling and SMOTE **did not raise AUC** and **badly overstated minority-class probabilities**.
A follow-up for ML models (Carriero et al., arXiv 2404.19494; *Statistics in Medicine* 2025, not
read) reports the same. The explore stage already fits weighted learners (amendment 01 weights) and
does not resample, which is correct. **Recommendation: already covered**, with one addition: report a
reliability curve and the calibration slope and intercept per fold. A slope well below 1 on the
evaluation window is the cheapest overfitting alarm for the boosted trees.

### 2.3 Events cluster by day (and by symbol)

- **Day clustering.** Gappers come in waves: hot sector mornings, sympathy runs, and the 2024–25
  regime visible in the base-rate table. Rows within a day are not independent. The effective sample
  size is closer to the number of days (about 125 per fold evaluation window) than the number of
  rows. Obuchowski, "Nonparametric analysis of clustered ROC curve data", *Biometrics* 53:567–578,
  1997, doi:10.2307/2533958, extends DeLong to clustered data. A day-block bootstrap (already used
  for H11) is the practical equivalent. Roberts et al., "Cross-validation strategies for data with
  temporal, spatial, hierarchical, or phylogenetic structure", *Ecography* 40(8):913–929, 2017,
  doi:10.1111/ecog.02881, show that random CV understates error whenever such structure exists, and
  recommend blocking even when residuals look independent. The rolling-origin folds block by time,
  which is correct.
- **Pooled AUC that mixes models.** `GapperExploreStage` computes "pooled" AUC by concatenating the
  four evaluation windows' scores, each from a *different* fitted model (around line 245). Two harms
  follow: score scales differ between models, and pooling rewards a model that ranks **hot days** above
  quiet days even if it cannot pick stocks within a morning. **Recommendation: do first.** Report
  (a) the mean of per-fold AUCs as the pooled figure, and (b) **within-day AUC**: AUC computed inside
  each morning, averaged over days with at least one positive and one negative, weighted. Keep (c) the
  global AUC, labelled as including day-level timing. A trader picks among stocks on one morning, so
  (b) and precision@k per day are the decision-relevant scores. Features such as sympathy or
  market-wide premarket volume can raise (c) without raising (b), and that should be visible.
- **Symbol clustering.** Some tickers gap repeatedly. Barber et al. 2022 report that a herded stock
  is about 10 percentage points more likely to be herded again the next day. Stable symbol
  fingerprints (ADV decile, price tier, float proxy) let a tree learn "this ticker gaps".
  **Recommendation: worth testing.** Report fold performance separately on symbols that had no Tier20
  day in the fit window ("new names"), and the share of positives from the top 1% of symbols. If most
  of the AUC comes from repeat names, say so; it is a real but different edge.

### 2.4 Purging and embargo (López de Prado)

López de Prado, *Advances in Financial Machine Learning*, Wiley, 2018 (ch. 7 purged k-fold and
embargo; ch. 12 combinatorial purged CV). Purging removes training rows whose **label windows
overlap** the test period. Here every label ends the same day (by 10:30 or 16:00), and features read
back at most to D−5 or D−30, so no training label overlaps a test label, and the 5-trading-day
embargo in `ExploreFolds` covers serial correlation of the run-up features. **Verdict: sound.** Three
places where information can still leak across the fold boundary:

1. **Anything fit on all of train and then used inside folds.** The time-of-day profile is argued to
   be outcome-free (fit on control days). That holds as far as labels go, though control days are
   chosen per gapper day, so *which days* contribute depends on outcomes; this effect is minor. The
   protocol also mentions **catalyst weights fit on train**. None was found in the explore code. If
   such weights, a `CatalystScorer` threshold, or any target encoding are ever fit on all of train,
   they must be refit per fold.
2. **Hyperparameters chosen on the same pooled fold metric that is then reported.** Choosing the
   boosted-tree depth, learning rate or feature set by pooled AUC and quoting that AUC is selection
   bias. See the lockbox in 2.6.
3. **Feature definitions changed after looking at fold results** (for example a threshold in
   `minsSinceFirstAbnormal`). That is legitimate in exploration, but each change is a new ledger
   trial.

### 2.5 Multiple testing and backtest overfitting

| method | what it answers | source | how to use it here |
|---|---|---|---|
| Deflated Sharpe ratio (DSR) | Is the best Sharpe still significant given N trials, their Sharpe variance, skew and kurtosis? | Bailey & López de Prado, "The deflated Sharpe ratio: Correcting for selection bias, backtest overfitting and non-normality", *J. Portfolio Management* 40(5):94–107, 2014, doi:10.3905/jpm.2014.40.5.094 | Already a finalist gate (criterion 5). N should be the **effective** number of trials, not the raw ledger count: cluster trials by the correlation of their per-day P&L, following López de Prado & Lewis, "Detection of false investment strategies using unsupervised learning methods", *Quantitative Finance* 19(9):1555–1565, 2019. The raw count overstates independence for grids that differ only slightly, and understates the search when trials were run but never logged. |
| Probability of backtest overfitting (PBO) via CSCV | How often does the in-sample best configuration rank below the median out of sample? | Bailey, Borwein, López de Prado & Zhu, "The probability of backtest overfitting", *J. Computational Finance* 20(4), 2017, doi:10.21314/JCF.2016.322 | Needs a matrix of trials × time blocks. **The ledger stores only 4 fold values per trial**, which is too few blocks. Store **per-month metric vectors** (36 train months, or the 24 evaluation months) so CSCV can run with S = 12–16 blocks. Report PBO per track. If PBO > 0.5 for a track, its best trial is no better than a random pick. |
| White's reality check / Hansen's SPA | Does the best of many rules beat a benchmark after accounting for the whole search? | White, "A reality check for data snooping", *Econometrica* 68(5):1097–1126, 2000; Hansen, "A test for superior predictive ability", *J. Business & Economic Statistics* 23(4):365–380, 2005 | Already used for F4 grids. In exploration, run SPA over **every strategy trial in the ledger for a track**, not only the final grid, with a stationary block bootstrap over **days**. Romano & Wolf's StepM (*Econometrica* 2005) then says which individual rules survive. |
| Higher t threshold | How large must t be once the whole field's search is counted? | Harvey, Liu & Zhu, "…and the cross-section of expected returns", *RFS* 29(1):5–68, 2016, doi:10.1093/rfs/hhv059 | Require a day-clustered **t ≥ 3** on pooled train for any finalist's effect, on top of the frozen bars. Cheap, and it drops marginal results before they use validation. |
| AUC trials (no Sharpe) | Is the best ΔAUC over the benchmark larger than the best you would get from noise? | (method; no single source) | The DSR does not apply to AUC. Use a **max-statistic null**: shuffle labels *within each day* (this keeps the day mix and the base rate), refit the K top configurations, record the maximum ΔAUC over B3, and repeat about 200 times. A finalist's ΔAUC must exceed the 95th percentile of that maximum. It is expensive, so run it only on finalists, but with the trial count of the whole track. |
| Research protocol | Process discipline | Arnott, Harvey & Markowitz, "A backtesting protocol in the era of machine learning", *J. Financial Data Science* 1(1):64–74, 2019, doi:10.3905/jfds.2019.1.064 | The study already follows most of it (prior hypotheses, a recorded trial count, out-of-sample replication). The one item not explicit here is "beware of the model you tweak after seeing the out-of-sample result": amendment 04 must be final before validation is touched, which `explore-plan.md` already says. |

**Expected decay.** McLean & Pontiff, "Does academic research destroy stock return
predictability?", *JF* 71(1), 2016: anomaly returns are about 26% lower out of sample and about 58%
lower after publication (figures from memory and a secondary table). Jensen, Kelly & Pedersen, "Is
there a replication crisis in finance?", *JF* 2023: most factors replicate, but the decay is larger
for predictors that look strongest in sample. **Mapping:** in amendment 04, set each finalist's
registered minimum effect *below* its train estimate. As a rule of thumb, register half the train
effect and check that validation has the power to detect that much (section 2.7). A finalist that
only clears the bar at its full train effect is likely to fail.

### 2.6 A lockbox inside train

The exploration round has a single shot at validation (amendment 04) and then the holdout. That is
right but unforgiving. **Recommendation: do first.** Search on folds 1–3 only. Keep **fold 4
(evaluate 2024-05..2024-10)** unread until the finalist list is drawn up, then score the at most 5
candidates on it once, logged in the ledger. A candidate that fails fold 4 is dropped before it uses
up validation. Fold 4 is also the regime closest to validation. The cost is losing about 125 days of
search data. Because some explore runs have already read fold 4, a cleaner alternative is to keep the
**last two months of fold 4** (2024-09..2024-10) as the lockbox from now on, and record that choice in
the ledger before any further run. (The ledger file `runs/explore/ledger.jsonl` is still empty today,
so this can be decided before the first logged trial.)

### 2.7 Power for validation

Validation has about 250 trading days and about 5,700 Tier20 events, so power is not the problem for
AUC. With a day-block bootstrap, ΔAUC of 0.02 is probably detectable; check by simulation on train
before registering. Strategy finalists are different: the bar is at least 150 trades on at least 40
days, and per-trade net returns on gappers have standard deviations of 10–20%. A +1%/trade effect
with SD 15% over 150 trades gives t ≈ 0.8 before clustering. **The strategy bar is very hard to clear
on validation unless the trade count is far above 150.** Compute the minimum detectable effect for
each registered strategy finalist from its train trade count, scaled to validation, before
registering it. If the minimum detectable effect is larger than the plausible effect, register the
detection claim alone.

---

## 3. Survivorship and look-ahead: checks against the protocol

How published work handles the usual problems: CRSP studies add delisting returns because
performance delistings are missing or biased upward in the data (Shumway, "The delisting bias in
CRSP data", *JF* 52(1):327–340, 1997; Shumway & Warther, *JF* 54(6):2361–2379, 1999, who suggest
−55% for missing Nasdaq performance delistings). Krauss et al. 2017 rebuild historical index
membership. Hou, Xue & Zhang 2020 limit microcap influence. Intraday studies clean trades by condition
code and remove bad prints (Barndorff-Nielsen, Hansen, Lunde & Shephard, *Econometrics Journal* 2009,
cleaning rules; Holden & Jacobsen, *JF* 2014, on TAQ quote and trade matching; both from memory). Since
this study enters and exits within D, delisting *returns* barely matter. Delisted *names*, symbol
identity, and outcome-dependent universe rules do matter.

| # | pitfall | protocol / code today | status | recommendation |
|---|---|---|---|---|
| G1 | **Outcome-dependent exclusion** | `HasUnexplainedMove` checks close-to-close moves for bars with `DateEt <= d`, so **D's own close is included**; the exclusion then persists for 25 calendar days after any such move. Train has 7,105 unexplained-move exclusions (all splits: 16,133). | **Gap (look-ahead).** A symbol that closes more than +200% on D (or below −66.7%) is removed from U(D) using D's outcome. These are among the largest Tier100 days. The following ~17 sessions are removed too, including day-2 continuation days, and the extreme D−1 movers that H15 is about. | Exploration: rebuild the exclusion with the window [D−25, D−1] in memory and report how many Tier20/50/100 days and how many H15 rows return. If material, register the corrected universe as a sensitivity analysis in amendment 04 (the frozen results stay as frozen). |
| G2 | **Split window vs H7** | Exclude when a split ex-date is in [D−25, D+1]. H7: `daysSinceReverseSplit <= 30`. | **Gap (design).** H7 can only see reverse splits 26–30 calendar days back, about 3–4 sessions. Post-reverse-split runners, a well-known gapper type with a small float right after the split, are outside the study. D+1 also uses a future ex-date (usually announced in advance, so mild). | Report H7's n per split, which will be small. In exploration, add a variant that keeps post-split days using the in-house split-adjusted prior close and excludes only days where the adjusted series fails the unexplained-move check on [D−25, D−1]. Then `daysSinceReverseSplit` can be tested across its full range. |
| G3 | **Candidate rule uses the daily bar** | Candidate = daily high ≥ +10% or daily volume ≥ 3× ADV20. Alpaca forum staff say daily OHLC follow regular-hours SIP rules while volume and VWAP include extended hours (not confirmed in official docs). Odd lots never set high or low. | **Possible gap.** Premarket-only spikes that faded before the open count as candidates only through the volume rule, so the frozen candidate-only hypotheses (H4, H5, H13, H3) under-sample premarket faders. Amendment 01 estimates stay unbiased because the 10% sample covers them, but with weight 10 they are noisy. | No-API check: in the 10% sample rows, count mornings whose *minute-bar* `gapMax` is ≥ 20% but that were not candidates. Multiply by 10 to estimate the missed Tier20 days per split. Also compare one known premarket-only spike's stored daily high with its minute-bar maximum. |
| G4 | **Ticker identity (renames, reuse, delisted)** | Bars requested without `asof`. The protocol counts symbols with a 90-session gap (125 found) and does not track symbol changes. | **Risk, unverified.** The Alpaca market-data FAQ says the historical-bars `asof` parameter is **on by default**: querying the new symbol after a rename also returns the old symbol's bars, and `asof=-` turns that off. The mapping works only from the day after the rename. For a **reused** ticker, the default maps to the current entity, so an earlier, delisted company using the same ticker may be missing or mixed in. The FAQ does not document how delisted symbols behave. | When the rate limit is free (not now): spot-check about 20 known 2021–2026 renames and reused tickers with default `asof`, `asof=-`, and `asof=<date before change>`. Compare with stored bars. Without the API: list inactive assets with zero stored bars, and symbols whose first stored bar is after an asset's listing date; both indicate entities that vanished. |
| G5 | **Odd lots and Form T in bars** | Labels come from Alpaca minute bars. Per Alpaca staff on the forum, odd lots (condition I) never set OHLC but do count in volume. Extended-hours handling in minute bars is not documented. | Partly a feature: live uses the same bars, so labels and live triggers agree. Still, the premarket in $10–20 names is odd-lot heavy, so `gapMax` and `hodTime` measure the round-lot high. | No-API check with the stored trade flow (IP-T460, gapper mornings): how often does the highest print of any size exceed the bar high by more than 2%, and how often does the odd-lot share of premarket volume exceed 50%? Report both; no change is needed unless the gap is large. |
| G6 | **Bad prints** | The tier counts only if *cumulative* premarket dollar volume from 04:00 is ≥ $50k. | Partial guard. A single erroneous print can set `gapMax` on a stock that traded $50k earlier at normal prices. Alpaca's forum has reports of historical spikes. | Robustness label (exploration): the tier counts only if prices at or above the tier level trade in **at least 2 distinct minutes**, or with at least $5k of dollar volume at or above the level. Report how many Tier20/Tier100 events change. |
| G7 | **Security type** | Universe = all `us_equity` with SIP bars. No filter for warrants, units, rights, preferreds, ETFs/ETNs or closed-end funds; only `isAdr` is flagged. | Gap (descriptive). De-SPAC warrants, rights and units gap for mechanical reasons and are not what a gapper trader trades. OTC names drop out naturally because SIP does not carry them. | No-API: flag rows whose asset name contains Warrant, Unit, Right, Preferred, ETF, ETN, Fund or Trust (and Nasdaq fifth-letter W/U/R suffixes as a cross-check). Report their share of Tier20, and run the main explore tracks with and without them. |
| G8 | **Delisted names lingering** | `lastDay = last bar + 35 calendar days` keeps a symbol in U(D) after its last bar while 5 of the last 20 sessions have bars. | Minor. These rows cannot be candidates (no bar on D) and can enter the 10% sample as negatives. They slightly inflate the negative class and shrink the base rate. | Report the count. Optionally exclude rows with no bar on D *and* no bar on D+1 from the population in exploration. |
| G9 | **SIP vs IEX** | `feed=sip` everywhere. `sipShare = 1.0`. | **OK.** IEX carries a small share of volume and a thin premarket, so a profile fit on SIP would be meaningless on IEX. | Keep the live Monitor on SIP (memory says the default feed is now `sip`). Make the live feature code refuse to compute `pmVolRatio` on a non-SIP bar. |
| G10 | **Premarket data gaps** | Forward-filled zero-volume minutes, trade count kept, snapshot uses `EndUtc <= t` with a unit test. | **OK.** | None. |
| G11 | **Regime shift** | Base rate doubles from train to validation and holdout (section 2.1). | Not survivorship, but it decides how train results transfer. | Report every fold's base rate next to every metric. Use rank or quantile triggers. Add the Saerens adjustment if probabilities are quoted. |
| G12 | **Orphan day** | 2024-11-01 falls between train (to 2024-10-31) and validation (from 2024-11-03): 2,266 universe days and 16 Tier20 are in split "none". | Harmless (a gap day between splits). | Mention it in the results doc so the counts reconcile. |

---

## Ranked recommendations

| rank | recommendation | type | cost | rating | why |
|---|---|---|---|---|---|
| 1 | **Screen benchmark** B2/B3 (`pmRet` rank; `pmRet` + catalyst logistic) beside H11's binary B1; carry ΔAUC and Δprecision@k **over B3** into amendment 04 | scoring | very low | do first | A binary benchmark's AUC is capped at ½(1+TPR−FPR); H11 can pass without any information beyond "already up, with news", which is the question the protocol itself asks. |
| 2 | **Within-day metrics**: within-day AUC, precision@k for k ∈ {5, 10, 20} (raw and ceiling-normalized), recall@k, average precision; pooled = mean of fold AUCs, not concatenated scores | scoring | low | do first | Precision@50 is capped near 18% in train; concatenating scores from four models mixes scales and rewards ranking hot days rather than picking stocks. |
| 3 | **G1 look-ahead fix** (unexplained-move window to D−1) as an in-memory exploration universe and a registered sensitivity analysis | data | low | do first | It removes some of the largest gappers using D's own close; the frozen run stays as frozen, but the size of the bias must be reported. |
| 4 | **Lockbox**: last two months of fold 4 unread until finalists; ledger stores **per-month metrics** so CSCV/PBO can run; effective-trial count for DSR; SPA/StepM over every strategy trial with a day block bootstrap; t ≥ 3; max-statistic within-day label-shuffle null for AUC finalists | process | low to medium | do first | Selection is the main remaining risk; the ledger is still empty, so the format can change before the first trial. |
| 5 | **G3 and G2 checks**: count premarket-only Tier20 missed by the candidate rule (10% sample × 10); report H7 n and test reverse splits over their full range in a split-adjusted variant | data | low | worth testing | Both decide whether the frozen candidate-only answers describe premarket gappers and post-split runners at all. |
| 6 | **Rank/quantile triggers and calibration**: reliability curve plus calibration slope per fold; no resampling; Saerens EM adjustment when quoting probabilities | modelling | low | worth testing | The base rate doubles after train; probability thresholds will fire about twice as often live as in train. |
| 7 | **Symbol concentration**: performance on "new names" and the share of positives from the top 1% of symbols | scoring | low | worth testing | Separates "this ticker gaps a lot" from "this morning looks like a gapper". |
| 8 | **Label robustness**: two-minute or $5k confirmation of tier touches; odd-lot vs bar high from the stored trade flow; security-type flag | data | low | worth testing | Bad prints and warrants can create Tier100 events that no trader could have traded. |
| 9 | **Power check** before registering strategy finalists (minimum detectable effect from the train trade count scaled to validation; register at about half the train effect) | process | very low | worth testing | +1%/trade on 150 trades with 15% SD is far from significant; such a finalist is set up to fail. |
| 10 | **`asof` spot check** of renames and reused tickers | data | low (API) | low priority now | Run after the current study job releases the rate limit; 125 symbols are already flagged. |

**Not worth:** resampling or SMOTE for the class imbalance (harms calibration, no AUC gain);
meme-stock or Reddit ML models as a source of features (no cross-sectional out-of-sample evidence);
deep networks on this sample size (Gu et al.'s gains need decades of monthly panels; Kaya et al.
find logistic regression competitive); combinatorial purged CV beyond the current rolling folds
(labels end the same day, so purging adds nothing; CSCV over months gives the overfitting estimate
more cheaply).

---

## Sources verified in this pass (title, authors and venue confirmed by search)

- Bali, Cakici & Whitelaw 2011, JFE 99(2):427–446 (EconPapers record; abstract-level finding).
- Conrad, Kapadia & Xing 2014, JFE 113(3):455–475 (EconPapers; findings from abstract and summaries).
- Barber, Huang, Odean & Schwarz 2022, JF 77(6):3141–3190, doi:10.1111/jofi.13183.
- Welch 2022, JF 77(3):1489–1527, doi:10.1111/jofi.13128.
- Gu, Kelly & Xiu 2020, RFS (title and findings via RePEc/AQR; R² magnitudes from a secondary summary).
- Avramov, Cheng & Metzker 2023, Management Science 69(5):2587–2619, doi:10.1287/mnsc.2022.4449.
- Leippold, Wang & Zhou 2022, JFE 145(2):64–82, doi:10.1016/j.jfineco.2021.08.017.
- Krauss, Do & Huck 2017, EJOR 259(2):689–702, doi:10.1016/j.ejor.2016.10.031.
- Hou, Xue & Zhang 2020, RFS 33(5):2019–2133.
- Chinco, Clark-Joseph & Ye 2019, JF 74(1):449–492, doi:10.1111/jofi.12733.
- Aït-Sahalia, Fan, Xue & Zhou 2022, NBER w30366.
- Kaya, Reichmann & Reichmann 2025, JBFA 52(2):1095–1115 (abstract-level only).
- Saito & Rehmsmeier 2015, PLOS ONE 10(3):e0118432.
- Saerens, Latinne & Decaestecker 2002, Neural Computation 14(1):21–41.
- van den Goorbergh, van Smeden, Timmerman & Van Calster 2022, JAMIA 29(9):1525–1534.
- Obuchowski 1997, Biometrics 53:567–578.
- Roberts et al. 2017, Ecography 40(8):913–929.
- Bailey & López de Prado 2014, JPM 40(5):94–107.
- Bailey, Borwein, López de Prado & Zhu 2017, J. Computational Finance 20(4).
- López de Prado & Lewis 2019, Quantitative Finance 19(9):1555–1565.
- White 2000, Econometrica 68(5):1097–1126; Hansen 2005, JBES 23:365–380 (page numbers from secondary citations).
- Harvey, Liu & Zhu 2016, RFS 29(1):5–68, doi:10.1093/rfs/hhv059.
- Arnott, Harvey & Markowitz 2019, JFDS 1(1):64–74.
- Alpaca Market Data FAQ (`asof` behaviour): https://docs.alpaca.markets/docs/market-data-faq
- Alpaca community forum on bar construction (odd lots excluded from OHLC; daily OHLC regular hours, volume/VWAP extended hours): https://forum.alpaca.markets/t/how-are-bars-calculated-different-bars-than-in-tradingview/9445 and https://forum.alpaca.markets/t/historical-vwap-data-very-wrong/14272. These are staff and user posts, not official documentation.

## Sources not verified (from memory or only through secondary summaries)

- Gorman, Akhtar, Durand & Gould 2022 (*Critical Finance Review*), the overreaction reading of MAX: summary only.
- Conrad et al. 2014, "63% of jackpots in the top 1% of predicted probability": secondary summary; not read in the paper.
- Gu, Kelly & Xiu 2020, page numbers 2223–2273 and the 0.26% / 0.33–0.40% monthly R² figures: from memory and a secondary summary.
- Avramov et al. 2023, the 62% / 68% / 80% declines: press and summary pages.
- Kaya et al. 2025: no AUC values read (full text blocked).
- Tran et al., arXiv 1810.10845 (author list from memory; metric discussion from the search snippet).
- Anand & Pathak 2021 and Cruz, Kinyua & Mutigwe: abstracts or snippets only.
- Carriero et al. (arXiv 2404.19494 / *Statistics in Medicine* 2025): snippet only.
- McLean & Pontiff 2016 (*JF* 71(1)) 26% / 58% decay figures and Jensen, Kelly & Pedersen 2023 (*JF*): from memory and secondary summaries.
- Romano & Wolf 2005 (*Econometrica*, StepM); Politis & Romano 1994 (stationary bootstrap); López de Prado 2018 book chapters: from memory.
- Shumway 1997 (*JF* 52(1)); Shumway & Warther 1999 −55% figure (confirmed by search); Shumway's own −30% figure not confirmed and not used.
- Barndorff-Nielsen, Hansen, Lunde & Shephard 2009 (*Econometrics Journal*, trade-cleaning rules); Holden & Jacobsen 2014 (*JF*): from memory.
- Whether Alpaca daily bars exclude extended-hours prices from high/low (G3), and how `asof` treats delisted or reused tickers (G4): community posts or undocumented; both need the checks proposed above.
