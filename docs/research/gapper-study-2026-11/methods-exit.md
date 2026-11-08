# Gapper study: methods for exit timing (IP-T463)

A literature and web pass on *how to decide when to sell* a premarket gapper. It complements
`literature.md` (which already covers Berkman et al. 2012, Lou/Polk/Skouras 2019, Barber et al.
2022, Zarattini et al. 2024, Aït-Sahalia et al. 2024, the DERA LULD paper and the vendor base
rates) and `avenues.md` (data sources, including halts in A1 and auction prints in A6). Those are
referenced here, not repeated.

The exit rules in the code today: velocity death (`v_3 < 0` on two consecutive minutes after
`v_3 > 0.5 %/min`), VWAP loss, a time sell-by (09:28 in the H12 grid), a peak giveback and a stop.
The trade-flow check (`runs/explore/tradeflow-20261108-1643.md`) found the retail sub-penny
imbalance collapse fires a median 24.5 minutes before the high, at 86.6% of it.

Each section says what the method is, what the evidence says (with numbers where I could read
them), how it maps onto the rules above, and one concrete rule or parameter it suggests, with a
rating: **worth testing**, **not worth** or **already covered**. Anything not read from the source
itself is marked *(abstract only)* or *(secondary)*. Every rule suggested here is new relative to
the frozen protocol, so it can only enter the study through a registered amendment (like A01-A03),
on train first.

---

## 1. Optimal stopping: selling an asset whose trend may have ended

### 1.1 Trailing stops under a random walk (Glynn & Iglehart 1995)

- **Source.** Peter W. Glynn and Donald L. Iglehart, "Trading securities using trailing stops",
  *Management Science* 41(6), 1096-1106, 1995. DOI 10.1287/mnsc.41.6.1096.
  https://web.stanford.edu/~glynn/papers/1995/GI95a.html *(abstract only)*
- **What it is.** Closed-form distribution, mean and variance of the gain and of the holding time
  when a long position is closed by a trailing stop, in discrete and continuous time.
- **Evidence.** Theory only. A later paper summarises its continuous-time result as: for a
  diffusion with constant parameters, the optimal policy is never to sell *(secondary)*.
- **Mapping.** A trailing stop only earns its keep when the drift can *change*; under a constant
  drift it just converts variance into early exits. That is the theoretical case for the existing
  peak-giveback rule being a regime-change tool, not a profit-taking tool.
- **Suggestion.** None on its own; it is the baseline that 1.3 improves on. **Already covered**
  (peak giveback).

### 1.2 When stop-loss rules help (Kaminski & Lo 2014; Lo & Remorov 2017)

- **Sources.** Kathryn M. Kaminski and Andrew W. Lo, "When do stop-loss rules stop losses?",
  *Journal of Financial Markets* 18, 234-254, 2014. DOI 10.1016/j.finmar.2013.07.001.
  https://dspace.mit.edu/handle/1721.1/114876 *(abstract and working-paper summaries)*.
  Andrew W. Lo and Alexander Remorov, "Stop-loss strategies with serial correlation, regime
  switching, and transaction costs", *Journal of Financial Markets* 34, 1-15, 2017.
  DOI 10.1016/j.finmar.2017.02.003. https://dspace.mit.edu/handle/1721.1/107017 *(abstract;
  regime result via a CXO Advisory summary of the 2015 working paper, secondary)*.
- **Evidence.** Under a random walk a simple 0/1 stop-loss rule always lowers expected return.
  With momentum or regime switching it can add value; on US equities (monthly, 1950-2004 in the
  working paper) some rules added 50-100 bps/month during stop-out periods. Lo & Remorov: stops
  can beat buy-and-hold for stocks with enough serial correlation, but **tight stops lose to
  buy-and-hold in a large US sample, mainly because of trading costs**; wide stops work when the
  asset switches between a calm bull regime and a volatile bear regime and switches are rare.
- **Mapping.** A gapper morning is close to the favourable case: one rare switch (run, then
  fade), with volatility rising after the high. The cost warning is the binding constraint here:
  the frozen round-trip floor is 0.5% and premarket spreads are several times RTH spreads
  (section 5), so a giveback rule tight enough to fire on minute-to-minute noise will be eaten by
  cost.
- **Suggestion.** Scale the giveback to the stock's own noise instead of a fixed percent:
  sell when `price <= runningHigh - k * sigma1m * sqrt(m)`, with `sigma1m` the standard deviation
  of 1-minute log returns over the last 15 minutes, `m = 5`, `k in {3, 5}`. **Worth testing**
  (cheap; replaces a fixed-percent parameter with a scale-free one).

### 1.3 Selling when the drift switches at an unknown time (Ekström & Lindberg 2013; Shiryaev)

- **Sources.** Erik Ekström and Carl Lindberg, "Optimal closing of a momentum trade", *Journal of
  Applied Probability* 50(2), 374-387, 2013. DOI 10.1239/jap/1371648947 *(full text read)*.
  A. N. Shiryaev, "Quickest detection problems in the technical analysis of the financial data",
  in *Mathematical Finance - Bachelier Congress 2000*, Springer, 2002, 487-521 *(existence and
  pages verified; content not read)*. Beibel & Lerche, "A new look at optimal stopping problems
  related to mathematical finance", *Statistica Sinica* 7(1), 93-108, 1997 *(abstract only: a
  general method, applied to perpetual straddles/strangles; I found no Beibel-Lerche paper that is
  specifically about selling on a drift reversal)*. Ekström & Vaicenavicius, "Optimal liquidation
  of an asset under drift uncertainty", arXiv 1509.00686, 2015 *(abstract; journal venue not
  verified)*. Dai, Zhang & Zhu, "Trend following trading under a regime switching model",
  *SIAM J. Financial Mathematics* 1, 780-810, 2010, DOI 10.1137/090770552 *(abstract)*.
- **What it is.** The price is a geometric Brownian motion whose drift drops from `mu2` (above
  the discount rate) to `mu1` (below it) at an unobserved, exponentially distributed time
  (hazard `lambda`). Ekström & Lindberg reduce the problem to one dimension and solve it: **sell the
  first time the posterior probability that the switch has already happened exceeds a constant
  level.** That posterior is Shiryaev's quickest-detection statistic, updated each instant from the
  log-return and its expected value under each drift. The optimal level is *decreasing* in the
  hazard `lambda` (if the trend is known to die fast, sell on weaker evidence) and decreasing in
  volatility. Ekström & Vaicenavicius get the same shape with a continuous prior on the drift:
  sell when the posterior mean drift falls below a boundary. Dai, Zhang & Zhu use the same filtered
  bull-probability with two thresholds (buy and sell).
- **Evidence.** Theory, no empirical test on small caps found.
- **What it says about velocity rule vs time stop.** This is the one framework that answers the
  user's question directly. Two inputs decide it:
  1. *How predictable the switch time is.* If the high always printed at 09:29, a clock exit would
     be optimal and no price signal could beat it. The vendor figures (premarket high median 08:00,
     ~50% of RTH highs 09:30-09:45) say the switch time is widely spread, which favours detection.
  2. *How fast the switch can be detected from prices.* The information per minute is the
     Kullback-Leibler rate `(mu2 - mu1)^2 / (2 sigma^2)`. Worked example (my arithmetic, Brownian
     assumption, not from a paper): run drift +0.5 %/min, fade drift -0.3 %/min, 1-minute
     volatility 2 %/min gives 0.08 nats/min, so moving the odds from 1:9 to 9:1 (ln 81 = 4.4
     nats) takes about 55 minutes on average. At 1 %/min volatility it is 0.32 nats/min, about 14
     minutes. **In very noisy names the price path alone cannot locate the top within 15 minutes;
     the clock (the prior hazard) has to carry most of the decision.** That is a principled
     version of "take the money and run".
  3. The existing velocity-death rule is a detector with no volatility scaling and no memory. Same
     example: with a true drift of +0.5 %/min and sigma 2 %/min, `v_3` has standard deviation about
     1.15 %/min, so `P(v_3 < 0)` is about 0.33 in any minute *while the run is still on*. Two
     consecutive negative minutes will happen within a few minutes on noise alone. Expect the
     current rule to fire early and often in high-volatility gappers and to be close to right only
     in low-volatility ones.
- **Mapping.** Replaces or arms the velocity rule; absorbs the time sell-by as the hazard
  `lambda(t)`; gives the trade-flow imbalance collapse a role (it raises `lambda` from that minute on
  instead of being an exit by itself, which the explore run showed is too early).
- **Suggestion (the main one).** A discrete Shiryaev-Roberts exit on 1-minute log returns `r_t`:
  - `R_t = (1 + R_{t-1}) * L_t`, with likelihood ratio
    `L_t = exp( ((mu1 - mu2) * r_t - (mu1^2 - mu2^2) / 2) / sigma^2 )`, all per minute;
    equivalently track the posterior `p_t` with a per-minute prior hazard `lambda(t)`.
  - `mu2` = the 15-minute log drift at arming (`v_15` in log terms); `mu1` = the median 1-minute
    log drift in the 30 minutes after the full-session high on train gapper days (one number, fit on
    train); `sigma` = rolling 15-minute standard deviation of 1-minute log returns.
  - `lambda(t)` = the train hazard of "high prints at minute t given not yet printed", from the
    empirical `hodTime` distribution (so 09:30-09:31 gets its own spike if the data has one),
    optionally multiplied by `c in {1, 3}` after the imbalance collapse.
  - Sell when `p_t >= p*`, `p* in {0.5, 0.7, 0.9}`. Keep the stop and a hard sell-by as guards.
  - Diagnostic before any grid: split train events by `snr = mu2 / sigma` (median split) and check
    whether velocity death beats the 09:28 sell-by only in the high-`snr` half. If it does, that
    confirms the detection-speed argument above.
  **Worth testing** (rank 1).

### 1.4 "Thou shalt buy and hold" and the goodness index (Shiryaev, Xu & Zhou 2008; du Toit & Peskir 2009)

- **Sources.** Albert Shiryaev, Zuoquan Xu and Xun Yu Zhou, "Thou shalt buy and hold",
  *Quantitative Finance* 8(8), 765-776, 2008, DOI 10.1080/14697680802563732 *(abstract)*; comment
  by Majumdar & Bouchaud in the same issue, 753-760. Jacques du Toit and Goran Peskir, "Selling a
  stock at the ultimate maximum", *Annals of Applied Probability* 19(3), 983-1014, 2009,
  DOI 10.1214/08-AAP566, arXiv 0908.1014 *(abstract)*.
- **What it is.** With constant parameters, the best time to sell to be closest to the eventual
  maximum is bang-bang: hold to the horizon if the "goodness index" `alpha = mu / sigma^2` is high
  (`alpha >= 1/2` in SXZ), sell at once if it is low (`alpha <= 0`); du Toit & Peskir's
  ratio criterion says sell immediately when `mu <= sigma^2 / 2`, i.e. when the drift of the
  *log* price is not positive.
- **Mapping.** Read as a real-time rule, "sell when the estimated log drift turns non-positive" is
  what velocity death already does, apart from the `sigma^2 / 2` correction (0.02-0.05 %/min at
  2-3 %/min volatility, small against a 0.5 %/min arm). The theory assumes the parameters are
  known; estimating `mu` from 15 one-minute returns is exactly the noise problem in 1.3.
- **Suggestion.** Compute velocities on log prices and subtract `sigma^2 / 2`; otherwise nothing new.
  **Already covered** (velocity death).

---

## 2. The 09:30 open: auction, retail flow, reversal, opening range

### 2.1 Overnight up, intraday down (already in `literature.md`)

Berkman, Koch, Tuttle & Zhang (JFQA 2012), Lou, Polk & Skouras (JFE 2019) and Barber, Huang,
Odean & Schwarz (*Journal of Finance* 77(6), 3141-3190, 2022, DOI 10.1111/jofi.13183, verified
this pass) are covered in `literature.md` §1-2. The short version for exits: in attention
stocks the open is, on average, the retail overpay price and the day's return is lost
intraday. Barber et al. report a 20-day abnormal return of -4.7% for the most-bought Robinhood
stocks *(abstract)*; the "+11% of the +14% herding-day return comes at the open" figure is from
`literature.md` and was confirmed this pass only through a secondary summary.

Two additions:

- **Branch & Ma.** Ben Branch and Aixin Ma, "Overnight return, the invisible hand behind intraday
  returns?" (2018 retrospective; *secondary*, via CXO Advisory): the negative correlation between a
  stock's overnight return and the following intraday return persists in 2011-2017 but has
  disappeared among S&P 500 names and survives mainly in smaller stocks. Earlier 1994-2005 data:
  about -0.22 *(secondary)*. Supports the fade prior in exactly this universe; venue not verified.
- **Heston, Korajczyk & Sadka.** "Intraday patterns in the cross-section of stock returns",
  *Journal of Finance* 65(4), 1369-1407, 2010, DOI 10.1111/j.1540-6261.2010.01573.x, arXiv
  1005.3535 *(abstract)*: returns in a given half-hour continue at the same half-hour on later days
  for up to 40 days, strongest in the first and last half-hours; short-term reversal is driven by
  liquidity imbalances lasting **less than an hour** plus bid-ask bounce; timing trades can save
  about the effective spread. NYSE 2001-2005. Supports treating the first 30-60 minutes as a
  liquidity-imbalance window whose reversal is mechanical.

### 2.2 The opening cross itself

- **Mechanics (verified).** Nasdaq's 2021 change disseminates early opening imbalance information
  from 09:25 every 10 seconds; market-on-open, limit-on-open and imbalance-only orders can be
  entered in that window but on-open orders cannot be entered, cancelled or changed after 09:28.
  Full NOII dissemination starts at 09:28. Sources: Nasdaq Trader notice ETA2021-6
  (https://www.nasdaqtrader.com/TraderNews.aspx?id=ETA2021-6) and SR-NASDAQ-2021-004
  (https://listingcenter.nasdaqomx.com/assets/rulebook/nasdaq/filings/SR-NASDAQ-2021-004_Notice.pdf).
  The current Equity 4 Rule 4752 text was not re-read.
- **Alpaca.** Extended-hours orders must be limit orders with `time_in_force = day` and
  `extended_hours = true`; `opg` (market- or limit-on-open) orders execute only in the opening
  auction. Source: https://docs.alpaca.markets/docs/orders-at-alpaca. Alpaca's own cutoff for
  accepting `opg` orders before the 09:28 exchange cutoff was **not verified**.
- **Evidence on price quality.** Cao, Ghysels & Hatheway (JF 2000; in `avenues.md` A6). A CFS
  working paper on Nasdaq's 2004 calls finds they lowered opening volatility and transitory noise
  (https://gfk-cfs.de/media//08_45.pdf, *abstract only; authors not checked*). Exchange-published
  comparisons (Nasdaq 2021, NYSE) exist but are self-interested and not used. A finding that matters
  for thin names *(secondary)*: in inactive stocks, bid-ask bounce is almost all of the transitory
  opening volatility.
- **Mapping.** The H12 grid's 09:28 sell-by is a *continuous premarket* sale. It is not the same
  as selling in the cross, and 09:28 happens to be the last moment a sell can be committed *to*
  the Nasdaq cross. H14 compares the 09:30 open with 09:15 but uses the first minute bar's open as
  the cross price, and the frozen cost model charges half the premarket spread on it.
- **Suggestion.** Three-way paired comparison per Tier50 event: (a) continuous sale at 09:28,
  (b) sale *in* the cross at the official auction price (A6 data), (c) sale at the 09:31 close. Cost
  for (b) = impact only (one price, no half-spread), for (a) the premarket spread, for (c) the
  RTH spread. If (b) beats (a) the live system should submit an `opg` limit sell by 09:27 instead of
  a 09:28 continuous sell. **Worth testing** (rank 2; needs A6 auction prints).

### 2.3 Opening range (first 5-30 minutes)

- **Sources.** Zarattini, Barbon & Aziz, "A profitable day trading strategy for the U.S. equity
  market", Swiss Finance Institute RP 24-98 / SSRN 4729284, 2024 (in `literature.md`; *abstract and
  secondary summaries*): a 5-minute opening-range breakout on the top-20 relative-volume "stocks in
  play" 2016-2023, profitable after costs. Holmberg, Lönnbark & Lundström, "Assessing the
  profitability of intraday opening range breakout strategies", *Finance Research Letters* 10(1),
  27-33, 2013, DOI 10.1016/j.frl.2012.09.001 *(abstract; crude oil futures)*: returns above zero
  and a success rate above a fair game. An independent replication on five indices reports gross
  results reproduced, net zero (MQL5 blog, practitioner, not verified in depth).
- **Mapping.** Continuation after the opening range is the opposite of the fade prior (68% close
  below open). The two are not contradictory: ORB conditions on the *direction* of the first 5
  minutes, the fade base rate does not.
- **Suggestion.** Hold-past-the-bell switch: if the position is still open at 09:30, keep it only if
  the 09:30-09:34 bar closes above both the open and the premarket high; otherwise sell at 09:35;
  then hand over to the exit in 1.3. Compare against "sell at 09:31". **Worth testing**, low
  prior (rank 5).

---

## 3. LULD pauses and the 09:45 band contraction

### 3.1 What the SEC staff data say

- **Source (full text read).** Claudia E. Moise and Paca Flaherty, "'Limit Up-Limit Down' Pilot
  Plan and Associated Events", SEC DERA white paper, **March 2017** (data April 2013 - 2014;
  `literature.md` dates it 2015, probably an earlier draft; the figures below are from the 2017
  PDF). https://www.sec.gov/files/dera-luld-white-paper.pdf
- **Band table (Appendix A).** Tier 2 above $3: 10%; $0.75-$3.00: 20%; below $0.75: lesser of
  $0.15 or 75%. All **doubled 09:30-09:45** (20%, 40%, $0.30/150%). The reference price is the
  arithmetic mean of eligible trades over the previous five minutes (first reference = the
  primary listing's opening price if it prints within five minutes). A Limit State lasting 15
  seconds becomes a five-minute pause, reopened by an auction.
- **Timing (Phase II, Table 3).** The first 15 minutes are 3.85% of the session but hold 9.2% of
  Tier 2 Limit States and **24.3% of Tier 2 Trading Pauses**. The five minutes **09:45-09:50**
  (1.2% of the session) hold **36.4% of Tier 2 Trading Pauses** (18.7% of Tier 1) but only 2.1% of
  Limit States: the band contraction itself pushes quotes outside the narrower band.
- **After the pause (Table 5).** Comparing the last sale before the first pause with the price one
  minute after the last pause of the day: for Tier 1, 87% (Phase II) reverse to within 5% of the
  pre-pause price; **for Tier 2, 46% reverse to within 10%, about 1% continue beyond 10%, and 53% of
  Tier 2 pauses followed low-volume periods** (illiquid names, reported separately). The authors
  read most pauses as liquidity gaps, not news.
- **Amendment 10.** Paul Hughes, "The effect of Amendment 10 of the LULD Pilot Plan", SEC DERA,
  December 2017 (https://www.sec.gov/files/marketstructure/research/dera_wp_the_effect_of_amendment_10_of_the_luld_plan.pdf;
  abstract and introduction read): the 2016 change to how the first reference price is set when a
  stock does not open in an auction cut pauses in both tiers, most for Tier 2 and most in the first
  30 minutes. So the 2013-14 shares above overstate the post-2016 open; the 09:45 contraction
  effect is untouched by it.
- **Hautsch & Horvath**, "How effective are trading pauses?", *JFE* 131(2), 378-403, 2019
  *(abstract; in `avenues.md` A1)*: Nasdaq pauses help price discovery during the break but raise
  volatility and hurt liquidity after it, and traders change behaviour ahead of an expected pause.
- **Gap.** No peer-reviewed study of post-pause continuation vs reversal in US small caps after
  2016 was found (same conclusion as `avenues.md` A1). Price-limit studies from China and Japan
  (daily limits, a different mechanism) mostly find next-day continuation; not transferable.

### 3.2 Rules it suggests

- **09:45 contraction exit (rank 3).** At 09:43 and 09:44, compute `ref = mean(close over the last 5
  minutes)` (a bar-based proxy for the SIP reference price) and the post-09:45 band for the
  stock's price (10% / 20% / lesser of $0.15 or 75%). If the 1-minute high range of the last three
  minutes has reached more than 0.8 x that band from `ref`, sell before 09:45. Rationale: the stock
  is already moving at a speed that the narrower band will turn into a pause, and a Tier 2 pause is
  followed by reversal or by an illiquid reopen far more often than by continuation. Needs only
  minute bars; A1 halt data can confirm it afterwards. **Worth testing.**
- **Sell the up-pause reopen (rank 6).** If a long position is caught in an up-pause, sell in the
  reopening auction or the first minute after it, never add. Fill at the first bar open after the
  gap (already the frozen halt fill rule). Needs the A1 halt list to identify up-pauses properly
  (the bar-gap `haltFlag` cannot give direction). **Worth testing** once A1 exists; mostly a risk
  rule.

---

## 4. Momentum exhaustion signals

### 4.1 Volume climax

- **Evidence.** No peer-reviewed intraday test of "climax volume marks the top" was found;
  everything found is practitioner (CMT Association / Wyckoff write-ups; a StockSharp sample
  strategy with an unaudited return figure). The nearest rigorous work is daily:
  Campbell, Grossman & Wang, "Trading volume and serial correlation in stock returns", *QJE*
  108(4), 905-939, 1993 *(abstract)*: return autocorrelation falls with volume, high-volume price
  moves are more likely to reverse (indexes and large stocks). Llorente, Michaely, Saar & Wang,
  "Dynamic volume-return relation of individual stocks", *RFS* 15(4), 1005-1047, 2002
  *(abstract)*: high-volume moves driven by hedging/liquidity trades reverse, those driven by
  informed trades continue; the split depends on how much informed trading a stock has. For
  gappers that means a news-driven climax and a promotion-driven climax should behave oppositely,
  which the catalyst block can separate.
- **Suggestion.** Descriptive first, like H17: define a climax minute as volume >= 5x the
  time-of-day-detrended median of the prior 30 minutes with the close in the lower half of the bar's
  range; report its share within 5 minutes before the high vs a random 5-minute window, split by
  `hasCatalyst`. **Worth testing as descriptive only** (low prior; cheap).

### 4.2 RSI and divergence

- **Evidence.** No rigorous test of RSI divergence as an intraday top signal was found (searches
  returned student projects and vendor backtests). The relevant methodological paper is
  Sullivan, Timmermann & White, "Data-snooping, technical trading rule performance, and the
  bootstrap", *JF* 54(5), 1647-1691, 1999, DOI 10.1111/0022-1082.00163: technical-rule results must
  be judged against the whole universe of rules tried; the study already does this with SPA and
  deflated Sharpe.
- **Rating.** **Not worth.** RSI on 1-minute closes is a smoothed velocity; it adds a parameter
  without adding information over `v_3`/`v_15`.

### 4.3 Runs tests, Hurst exponent, variance ratios

- **Evidence.** None found that tests these as intraday exit signals. One study of persistence
  across frequencies (Cogent Economics & Finance 2024, DOI 10.1080/23322039.2024.2302639; authors
  and details not verified) reports intraday data as anti-persistent (mean-reverting).
- **Why not.** A variance ratio VR(5) estimated on a 30-minute rolling window has a standard error
  near 0.4 (Lo-MacKinlay asymptotics, my arithmetic), so it cannot tell trending from
  mean-reverting inside the window that matters. **Not worth.**

### 4.4 Order-flow toxicity and imbalance (VPIN; Chordia & Subrahmanyam)

- **Sources.** Easley, López de Prado & O'Hara, "Flow toxicity and liquidity in a high-frequency
  world", *RFS* 25(5), 1457-1493, 2012, DOI 10.1093/rfs/hhs053 *(abstract)*. Critique: Andersen &
  Bondarenko, "VPIN and the flash crash", *JFM* 17, 2014, and the authors' rejoinder in the same
  volume, 47-52 *(abstracts)*: VPIN's forecasting power comes largely from a mechanical link to
  trading intensity, it depends on where the volume buckets start, and it peaked *after* the flash
  crash. Chordia & Subrahmanyam, "Order imbalance and individual stock returns: theory and
  evidence", *JFE* 72, 485-518, 2004, DOI 10.1016/S0304-405X(03)00175-2 *(abstract)*: lagged
  imbalance predicts returns positively, reversing once current imbalance is controlled for; NYSE
  1988-1998; costs absorb most of the strategy profit.
- **Mapping.** H17 (trade-count collapse) and the trade-flow explore (retail imbalance collapse)
  already measure the useful part. The explore result (collapse 24.5 minutes before the high, at
  86.6% of it) fits the Chordia-Subrahmanyam picture: imbalance leads price, so it is an early
  warning, not a sell trigger.
- **Suggestion.** Use the collapse as a *state change*, not an exit: from the collapse minute on,
  multiply the hazard in 1.3 by `c = 3`, or switch the giveback in 1.2 from `k = 5` to `k = 3`.
  VPIN itself: **not worth**. Imbalance-as-arming: **worth testing** (folded into ranks 1 and 4).

---

## 5. Practical execution in the premarket

- **Spreads and depth.** Barclay & Hendershott, "Liquidity externalities and adverse selection:
  evidence from trading after hours", *JF* 59(2), 681-710, 2004 *(abstract)*: after hours there are
  fewer than 1/20 as many trades per unit of time and quoted and effective spreads are 3-4x the
  daytime level, from adverse selection rather than dealer profit. Late-1990s Nasdaq data; spreads
  for sub-$5 names in 2021-2026 are likely different, which is why the protocol calibrates
  `premarketSpreadMultiplier` once from real 08:00 quotes rather than borrowing this number.
- **Order types.** Alpaca accepts only `limit` + `day` in extended hours; no stops trigger there,
  so the stop and giveback rules must be enforced by the Monitor and sent as marketable limits.
  A sell limit at or above the bid on a falling stock may not fill. For backtests on minute bars,
  a fill rule of "fills only if a later bar's low reaches the limit" is the honest choice; the
  frozen model fills at the next bar open, which flatters fast exits in thin names.
- **Selling into the auction vs before it.** See 2.2. The cross concentrates the morning's
  liquidity into one price with no spread; a continuous 09:28 sale pays the premarket half-spread.
  Heston et al. (2.1) put the saving from good trade timing at roughly the effective spread.
- **Suggestion.** Two cost-model sensitivity runs on any exit rule that survives train: (a) the
  limit-touch fill rule above; (b) an extra 0.5x spread of slippage on exits that fire while
  `v_1 < 0` (selling into a falling book). Report the change in mean net per trade. This changes no
  frozen number; it is a robustness table. **Worth testing** (rank 4, alongside the scaled
  giveback, because it decides whether tight exits survive cost).

---

## Ranked shortlist

| rank | rule | source of the idea | needs | rating |
|---|---|---|---|---|
| 1 | Shiryaev-Roberts drift-change exit with a train-fit time-of-day hazard `lambda(t)`, hazard x3 after the imbalance collapse; sell at posterior `p* in {0.5, 0.7, 0.9}`. First run the `snr = mu2 / sigma` split diagnostic on the existing velocity rule | Ekström & Lindberg 2013; Shiryaev 2002; Kaminski & Lo 2014 | minute bars, train `hodTime` distribution, trade flow (optional) | worth testing |
| 2 | Sell *in* the opening cross (`opg` limit by 09:27) vs continuous 09:28 vs 09:31, paired, cross charged impact only | Berkman 2012; Barber 2022; Nasdaq 09:28 cutoff | A6 auction prints | worth testing |
| 3 | 09:45 band-contraction exit: sell at 09:43-09:44 if the last 3 minutes moved more than 0.8x the post-09:45 band from the 5-minute mean | DERA 2017 (36.4% of Tier 2 pauses in 09:45-09:50; 46% reverse vs 1% continue) | minute bars (A1 to confirm) | worth testing |
| 4 | Volatility-scaled giveback `k * sigma1m * sqrt(5)`, `k in {3, 5}`, tighten after imbalance collapse; plus the two fill/slippage sensitivity runs | Lo & Remorov 2017; Glynn & Iglehart 1995; Barclay & Hendershott 2004 | minute bars | worth testing |
| 5 | Opening-range hold switch: keep past the bell only if 09:30-09:34 closes above open and premarket high, else sell 09:35 | Zarattini et al. 2024; Holmberg et al. 2013 | minute bars | worth testing (low prior) |
| 6 | Sell the up-pause reopen, never add | DERA 2017; Hautsch & Horvath 2019 | A1 halt list | worth testing after A1 |
| - | Volume-climax footprint, split by catalyst | Campbell et al. 1993; Llorente et al. 2002 | minute bars | descriptive only |
| - | Goodness index / log-drift correction | Shiryaev, Xu & Zhou 2008; du Toit & Peskir 2009 | - | already covered |
| - | Trailing stop as such | Glynn & Iglehart 1995 | - | already covered |
| - | VPIN | Easley et al. 2012; Andersen & Bondarenko 2014 | - | not worth |
| - | RSI / divergence | none rigorous; Sullivan et al. 1999 | - | not worth |
| - | Hurst / variance ratio / runs | none found | - | not worth |

The single most useful finding is analytic, not empirical: in a gapper with 1-minute volatility
around 2%, the price path carries too little information per minute to locate a drift reversal
within 15 minutes, so a well-calibrated clock prior must do most of the work, and a raw
two-bar velocity sign rule will mostly fire on noise. Ranks 1 and 4 turn that into testable rules;
the `snr` split is the cheap check that tells whether it holds on this sample.

## Sources not verified in this pass

- **Full texts not read** (abstract, bibliographic record or secondary summary only): Glynn &
  Iglehart 1995; Kaminski & Lo 2014 (the 50-100 bps figure is from the working paper);
  Lo & Remorov 2017 (regime result via CXO Advisory); Shiryaev 2002 (content not seen at all);
  Beibel & Lerche 1997; Shiryaev, Xu & Zhou 2008; du Toit & Peskir 2009; Ekström & Vaicenavicius
  (journal venue unverified; arXiv only); Dai, Zhang & Zhu 2010; Heston, Korajczyk & Sadka 2010;
  Hautsch & Horvath 2019; Campbell, Grossman & Wang 1993; Llorente et al. 2002; Easley et al. 2012;
  Andersen & Bondarenko 2014; Chordia & Subrahmanyam 2004; Barclay & Hendershott 2004; Holmberg
  et al. 2013; Zarattini, Barbon & Aziz 2024; Barber et al. 2022 (the +11% opening figure is
  secondary).
- **Full texts read:** Ekström & Lindberg 2013; Moise & Flaherty (DERA, March 2017); Hughes (DERA
  Amendment 10, December 2017, abstract and introduction).
- **Venue or authors not verified:** Branch & Ma 2018 retrospective (CXO summary only); the CFS
  working paper on Nasdaq's 2004 opening/closing calls; the Cogent Economics & Finance 2024
  persistence study; the MQL5 ORB replication; the "bid-ask bounce explains opening volatility in
  inactive stocks" result (seen as a search summary, source paper not identified).
- **Not found at all:** a Beibel & Lerche paper specifically on selling at a drift reversal; any
  peer-reviewed test of RSI divergence, volume climax, Hurst or variance-ratio decay as an intraday
  exit; any post-2016 study of LULD post-pause continuation in US small caps; Alpaca's own `opg`
  submission cutoff.
- **My own arithmetic, not from a paper:** the KL detection-delay example (0.08 and 0.32 nats/min,
  ~55 and ~14 minutes), the `P(v_3 < 0) ~ 0.33` false-alarm figure, and the VR(5) standard error.
  All assume Brownian 1-minute returns and should be checked on train data before anyone leans on
  them.
