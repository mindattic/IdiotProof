# Gapper study: methods for the fade-short's tail, sizing, squeeze and shortability proxies (IP-T478)

A web pass on what to do about the tail of the costed fade-short. The train-only exploration
(`runs/explore/fade-short-20261109-1733.md`; rule: premarket high >= +100% over the adjusted prior
close, still >= +20% at 09:45 and less than 10% above the 09:30 open, short at the 09:45/09:46 open, cover
at 15:55, no stop) nets +8.16%/trade [+5.71%, +10.61%] over 324 trades on 249 days at 50% APR borrow, 74.4%
winners, positive in all 4 folds, against +1.62% for the random short. The tail is the problem:
worst 1% of trades about -66%, MAE p50 +9.4%, p90 +38.4%, p95 +69.0%, p99 +112.9%, max +145.4% (random
short max +292.4%), 8.3% of trades above +50% MAE. A +10% stop cuts the mean to +1.85% [-0.08%, +3.78%]
(and to +0.45% on the +50% variant); a +20% stop keeps +4.20% [+1.79%, +6.60%] and cuts the worst 1% to
-25.5%. Historical easy-to-borrow (ETB) status is not available. This note complements
`methods-short.md` (borrow, SSR, Alpaca; its §2.3 sized the position to the worst move),
`methods-exit.md` §1.2 (Kaminski & Lo) and `methods-execution.md` §4.2 (Kelly). Those are referenced,
not repeated.

Labels: **peer-reviewed**, **working paper**, **regulator/exchange**, **vendor/anecdotal**.
*(abstract/snippet)* means I read only an abstract, a search summary or a catalogue record.
*(secondary)* means a number was reported by a third party. *(not verified)* means I did not read it
this pass and it must not be cited in results. Today is 2026-11-09. No Alpaca, SEC or idiotproof API or
CLI was called; this is web search and fetch only. The search tool summarises pages with a small
model, so any number that came only from a summary is *(snippet)* even when the source is good: re-read
the source before a number goes into a results report. Every parameter below is a candidate for a
registered amendment, on train first. None is a finding. Numbers from the run file are quoted as
printed; "my arithmetic" marks anything I derived.

---

## 1. Stop design for a mean-reverting short with a fat right tail

### 1.1 Why a fixed stop destroys a mean-reversion edge

- **Kaminski & Lo (2014, *J. Financial Markets* 18, 234-254), "When do stop-loss rules stop losses?",
  peer-reviewed** *(abstract and summaries; full theorems not read)*. A stop-loss is an overlay on the
  return process; its expected-return effect is the "stopping premium". Under a random walk (iid
  returns) the premium is **always negative**. For a mean-reverting process, a stop "often exits right
  before the reversal" and hurts expected return *(secondary summary)*. It can be positive only with
  momentum or regime switching: on US equities (monthly, 1950-2004) some rules added 50-100 bps a month
  during stop-out periods, with a bond as the stop asset *(snippet)*. A 2025 practitioner summary says the
  AR(1) coefficient must be at least the strategy's Sharpe ratio for stops to add value *(secondary; not
  verified against the paper)*.
- **Lo & Remorov (2017, *J. Financial Markets* 34, 1-15), peer-reviewed** *(abstract/snippet)*. Stops beat
  buy-and-hold only with enough positive serial correlation; tight stops lose mainly to trading costs;
  **wide stops can win when the asset switches between a calm and a volatile regime and switches are
  rare**; one summary notes that a stop with a one-day delay did better partly because of reversion
  during the delay, and that negative autocorrelation works against stops *(secondary)*.
- **Han, Zhou & Zhu (2016, *J. Financial Economics* 122), "Taming momentum crashes: a simple stop-loss
  strategy"** *(secondary; I read only summaries, versions differ in figures)*. A 10% monthly stop on
  individual momentum positions cut the worst monthly loss from about -50% to about -11% (equal-weighted,
  1926-2013) and raised the Sharpe ratio, gross of trading costs. Note the direction: the stop protects a
  **momentum** book against reversal crashes, the opposite of this trade.
- **The mapping, and what the run file shows.** The fade-short is a bet that a large premarket move
  mean-reverts within the session; the tail trades are the opposite regime, continuation (squeeze,
  promotion, news). A stop only pays if it fires mostly in the continuation regime and rarely in the
  reverting one. The data say a +10% stop fires mostly in the reverting one: MAE p50 is +9.4% and 48.8%
  of trades touch +10%, so 49.1% of trades are stopped (3.1% gapped through), most of which would have
  recovered; the win rate drops from 74.4% to 46.0%. The +20% stop fires on 27.5% of trades (2.2% gapped
  through). Cost of the tail cut (my arithmetic): +20% stop gives up 3.96 points of mean (8.16 to 4.20) to
  lift the worst 1% from -66.2% to -25.5%; +10% gives up 6.31 points to reach -15.9%. VWAP-reclaim exits,
  a signal-based stop, also hurt (+2.70%). Every price-based exit tested loses to no stop, which is the
  Kaminski-Lo prediction for a mean-reverting book.
- **Two further reasons a stop is worse than it looks here.** (a) The stop is a market order after the
  trigger (`methods-short.md` §2.2); the run's stop model fills at the worse of the stop and the next bar
  open, which is optimistic for a halt-up (an up-pause reopens through the stop). (b) The edge is
  late-accruing: cover at 11:00 earns +3.26% against +8.16% at 15:55, so any early exit, including a
  time stop before the afternoon, gives up edge.
- **No source found** that tests stops on a mean-reverting intraday book of sub-$20 gappers. Everything
  above is monthly or daily, large-cap or index data (a gap, flagged in section 5).

### 1.2 Alternatives that cut the tail without a tight price stop

All of these are candidates, not results. Evidence quality is stated for each.

1. **Catastrophe-only wide stop** (Lo & Remorov's "wide stop" case). Place the stop beyond the point where
   the reverting regime is statistically unlikely, say at the p90-p99 of MAE. From the run: p90 +38.4%,
   p95 +69.0%, p99 +112.9%; 8.3% of trades exceed +50%. A stop at +75% or +100% would fire on roughly the
   worst 5% and 2% of trades (my reading of the percentiles; not simulated). Whether the trades that
   reach +50% still revert is exactly what the data can answer and the literature cannot. A caveat: the
   +50/+75/+100 levels are selected from the same train sample's tail, so they need a held-out check
   (fold-wise: pick on fit, evaluate on the eval window).
2. **Volatility-scaled stop** (peer-reviewed motivation, my parameterisation). `methods-exit.md` §1.2
   proposed `price <= runningHigh - k*sigma1m*sqrt(m)` for a long. The short mirror:
   cover when `price >= entry + k*sigma*sqrt(T)` with `sigma` the 15-minute 1-minute-return standard
   deviation at entry. A scale-free stop avoids the fixed-percent problem that a +10% stop is inside one
   minute's noise for a name with a 9.4% median MAE.
3. **Time stop** (vendor lore; no source). Cover by a clock time. The run's 11:00 exit is a time stop and
   loses 4.9 points of mean; the profile of the decline by hour is not in the run (decline-profile test in
   `methods-short.md` §6). A time stop does not cut a squeeze that happens at 10:00.
4. **Condition the stop on a regime signal, not on price alone** (the Kaminski-Lo/Lo-Remorov lesson: stops
   pay when momentum is present). Candidate "continuation" flags available intraday: a new high of day
   after 10:00 on volume above the 09:30-09:45 average; a halt-up; price reclaiming the premarket high.
   Stop only if the flag is on. No source tests this for gappers; it is the one design the theory
   supports. Its risk is the multiple-comparisons budget (the ledger is at 1095 trials).
5. **Partial covers** (vendor/practitioner; no peer-reviewed source found). Cover half at a profit mark
   (e.g. -15% from entry) and carry the rest to 15:55. This reduces variance at the cost of the long tail
   of the right-hand profit side; the run shows 74.4% winners, so the median win is the fat part. Test.
6. **Option hedges.** Not available as a general rule: a +100% sub-$20 micro-cap gapper rarely has a listed
   option chain (my inference; I found no source giving the share of such names with listed options). If
   a name does have options, the premarket-to-09:45 implied volatility will be extreme. Defined-risk call
   spreads also need Alpaca options level 3 (the repo has it) but at wide spreads. Treat as not testable.
7. **Hard cap by position size instead of price.** Size so that the largest historical MAE costs no more
   than the risk budget (section 2). This is the one option with no bias against the mean-reversion edge,
   because it does not exit anything.

A stop at the broker is also not the same as the backtest stop: Alpaca converts a stop to a market order
once elected, with no price guarantee (`methods-short.md` §2.2), and may liquidate a margin shortfall
without notice (§5.2 there). So even the wide stop does not bound the loss; only size does.

### What to test (train only, via an amendment)

- **Stop grid on the pmHigh>=+100% ext<10% cover-15:55 cell** (and the +50% variant as a robustness
  cell): fixed stop at +30%, +40%, +50%, +75%, +100% over entry; fill at the **higher** of the stop and the
  next 1-minute bar open, and at the bar's high if the bar's high is more than 15% above the stop (the
  gap-through stand-in from `methods-short.md` §2). Report mean net, 95% day-clustered interval, worst
  1%, share stopped, share gapped, and the share of stopped trades that would have closed profitably
  without the stop (the "reverted after stop" rate).
- **Volatility-scaled stop:** `entry + k*sigma1m*sqrt(60)` with `sigma1m` over 09:30-09:45, k in {4, 6, 8}.
  The parameter count is small on purpose; log every cell as a trial.
- **Conditional stop:** a +25% stop active only after 10:00 and only when the 1-minute close is at a new
  high of day on volume above the 09:30-09:45 mean per minute. Compare with the same stop unconditional.
- **Partial cover:** cover 50% at 15% below entry, carry 50% to 15:55 (no stop); report mean and worst 1%.
- **Selection guard:** choose the stop level on the fit window of each fold only, evaluate on the eval
  window; report the leave-one-month-out minimum as the run already does. Do not choose among stops on
  pooled train data.

---

## 2. Sizing with fat tails and negative skew

### 2.1 What the run file implies before any model

- The loss is bounded only by position size and margin. The run's worst weighted 1% is -66.2% of notional,
  but the worst MAE is +145.4%: a mark-to-market loss of 145% of notional before the position recovered.
  The "combined" locate-stress cell (95 trades) prints a weighted worst 1% of **-108.83%**, so at least one
  realised loss exceeded 100% of notional in the subset. A short therefore needs a per-trade notional
  limit set from MAE, not from the stop (my arithmetic: with a risk budget `B` of equity per trade and
  worst credible MAE `m`, notional/equity <= B/m; for `B` = 2% and `m` = 1.45 that is 1.4% of equity; for
  `m` = 2.92, the random-short max, 0.7%).
- The sample is 324 trades; the worst 1% is about 3 trades and the p99 MAE about 3 trades. The tail is
  estimated from a handful of observations, and the random short (2,458 trades) already saw a +292% MAE.
  Expect the true worst case to be worse than the sample's.
- Margin: Alpaca's sub-$5 overnight requirement is the greater of $2.50/share or 100% (`methods-short.md`
  §5.2); an intraday short that rises 145% against a position at 1x leaves the account with less equity
  than the position was worth. Concurrent shorts correlate on hot-market days (my inference), so the
  aggregate cap matters more than the per-trade one.

### 2.2 Fractional Kelly under estimation error

- **MacLean, Thorp & Ziemba (2010, *Quantitative Finance*), "Long-term capital growth: the good and bad
  properties of the Kelly and fractional Kelly capital growth criteria", peer-reviewed** *(search
  summary)*. Full Kelly can produce severe drawdowns over realistic horizons and very large bets in
  short-run favourable situations; fractional Kelly (a fraction in Kelly, the rest in cash) mitigates
  it. A source cautions that fractional Kelly is no longer optimal once lognormality is dropped, which
  bears directly on fat tails.
- **Baker & McHale (2013, *Decision Analysis* 10(3), 189-199), "Optimal betting under parameter
  uncertainty: improving the Kelly criterion", peer-reviewed** *(abstract)*. With an estimated win
  probability, shrinking the Kelly bet improves out-of-sample performance; they give a back-of-envelope
  shrinkage factor and test on simulated and tennis betting data. Their setting is binary bets; I did not
  read the formula, so applying it to a continuous, skewed return is an extrapolation.
- **Fat-tail caveat for the continuous approximation.** `f* ~ mu/sigma^2` assumes a thin tail. For this
  trade, the empirical `g(f) = mean(log(1 + f*R))` is defined only for `f*R > -1`; with a realised
  loss near -109% the cap is `f < 1/1.09 = 0.92` of equity in notional and with the -66% worst 1% it is
  1.5 (my arithmetic). Because notional exposure is margin-bound, the empirical maximiser will sit far
  above any sane size; as in `methods-execution.md` §4.2, Kelly is only an upper bound and a check that
  the edge survives shrinkage.
- **Practical shrinkage.** Take the lower bound of the day-clustered interval, not the mean, as the edge
  input: the run's lower bound is +5.71% against +8.16%, and the LOMO minimum lower bound is +5.16%. Then
  apply a further fraction (a quarter to a half) to the empirical maximiser. These are conventions, not
  results.

### 2.3 Drawdown-constrained sizing

- **Busseti, Ryu & Boyd (2016, *J. Investing*), "Risk-constrained Kelly gambling", peer-reviewed**
  *(abstract; formula not read)*. Adds a constraint on the probability that wealth falls below a given
  fraction; derives an upper bound on that probability, giving a convex problem; Monte Carlo suggests the
  bound is "reasonably close" to the true risk, and the authors report it beating fractional Kelly at
  matched drawdown risk. It works with a finite set of outcomes, so the empirical trade distribution
  can be used directly.
- **Grossman & Zhou (1993, *Mathematical Finance* 3(3), 241-276), "Optimal investment strategies for
  controlling drawdowns", peer-reviewed** *(abstract)*. Risky exposure proportional to the cushion
  between wealth and a drawdown floor. A later result (Nowicki & Klass, 2005) says the strategy loses
  its optimality in discrete time *(secondary)*. Practical reading: scale the size down after losses,
  and back up on recovery; a CPPI-style rule.
- **Probability of ruin from the empirical distribution.** I found no source that estimates ruin for a
  short-gapper book. The method is Monte Carlo on the trade list (next section).

### 2.4 Bootstrapping the trade sequence to set the equity fraction

- **Politis & Romano (1994, *JASA*), "The stationary bootstrap", peer-reviewed** *(abstract)*. Resamples
  blocks of geometric random length so the resampled series is stationary; suited to weakly dependent
  data. Trades here cluster on days (the intervals are already day-clustered), so resample **whole
  days** (all trades on a day together), with a block length of a few days to preserve any regime
  persistence. Fold structure also matters: the 2022-2024 folds had very different means (+6.06% to
  +12.98%).
- **A tail-augmentation note (not read; from memory):** extreme value theory (a generalised Pareto fit to
  the loss tail above a threshold) is the standard tool for extrapolating beyond the worst observation.
  With roughly 3 exceedances at the p99 it is not credible, so use it only as a sensitivity
  (for example, add a synthetic loss of -100% and -150% at a stated frequency).
- **Procedure.** For a candidate fraction `f` of equity per trade: draw `N` days (N = days per year in
  the sample, about 83 per year on this signal, my arithmetic from 249 days over three years), take all
  trades on each, apply `equity *= 1 + f*R_i` with the exit rule and the borrow/locate costs of the cell,
  record the max drawdown and a ruin indicator (equity below 50% of start), repeat 10,000 times. Pick the
  largest `f` such that P(max drawdown > 30%) <= 5% and P(ruin) < 1%. Run it on the pooled train sample,
  on each fold's eval window with `f` fitted on its fit window, and with the worst 1% replaced by -109%
  and by -150% as a tail stress. Report the `f` that survives each.
- **Concurrency.** Count same-day concurrent shorts; the day-level bootstrap preserves it. Cap aggregate
  notional, not just per-trade.

### What to test (train only, via an amendment)

- Empirical `g(f)` on the cover-15:55 no-stop trades of the +100% cell and the +50% cell: report the
  argmax `f`, and 1/4, 1/2 of it, using the lower-bound edge (not the mean).
- **Stationary-bootstrap equity simulation** as in 2.4: block length 3 days, 10,000 paths, horizon 83
  trading days of signal events (a year), costs from the cell (50% APR, 2.5% locate fee, 100-share lots,
  60% locate failures as the combined stress). Report P(max drawdown > 20%/30%/50%), P(equity < 50%),
  median and 5th-percentile terminal equity, for `f` in {0.5%, 1%, 2%, 5%, 10%} of equity per trade.
- **Notional cap from MAE:** `size <= B / MAEmax` with B in {1%, 2%} and MAEmax in {1.45, 2.0, 2.92}.
  Report the dollar edge per year at the cap, so the reader sees whether the edge is worth taking.
- **Tail stress:** replace the lowest 3 trades (about the worst 1%) with -100% and -150% and repeat the
  bootstrap. The recommended `f` is the smallest that survives all stresses, not the best on the base.

---

## 3. Short-squeeze dynamics in low-float names

### 3.1 What the evidence says predicts a squeeze

- **Schultz (2024, *J. Financial and Quantitative Analysis* 59(1), 68-96), "Short squeezes and their
  consequences", peer-reviewed** *(abstract and secondary summaries; predictor tables not read)*. A squeeze
  is a **recall-forced** early exit: borrowed shares are recalled and the short seller cannot re-borrow.
  Squeezes are rare for most stocks and **common for the hardest-to-borrow stocks**, where expected
  squeeze costs reduce short returns and squeezes make the short miss abnormal returns. Secondary
  summaries report: more common for small firms; hard-to-borrow defined as utilisation of 25% or less
  was used in one summary; expected squeeze costs of 0.873%-1.039% consuming more than two-thirds of
  the excess return from shorting those stocks; 29-37 bps a month for stocks with fees of 25% or more
  *(all secondary, not verified, and the definitions differ between the summaries)*. The paper's two
  squeeze-risk metrics were not named in what I read. This is the most relevant primary source for
  "what makes a squeeze", and it is about the lending market, not price.
- **Engelberg, Reed & Ringgenberg (2018, *J. Finance* 73(2))** (cited in `methods-short.md` §1.1):
  fee variance and recall risk lower the returns of the short side and the shorting itself; fee jumps
  and recalls are linked.
- **Xu & Liu (2012), "Short squeeze", working paper** (via CXO Advisory summary; the paper not read).
  NYSE/AMEX/Nasdaq stocks 1995-2009, excluding very small and low-priced stocks, 26,343 one-day jumps of
  at least +15%. All events: average four-factor Day 1 reversal -0.30%. Top decile of both short interest
  and day-0 return (190 stocks): Day 1 reversal -3.25%. An intraday subsample of 153 high-short-interest
  stocks with an average 18% jump: price kept rising for about 1.5 days to about +27% over the pre-jump
  price, then gave back about 4 points. Returns are gross and average spreads of about 1.3% were noted.
  The two descriptions look inconsistent (reversal on the next day against continuation for 1.5 days);
  the summary does not reconcile them. **The sample excludes the population in this study (very small,
  low-priced).**
- **Lamont (2012, *Review of Asset Pricing Studies*), "Go down fighting: short sellers vs. firms"**
  *(snippet)*: firms that fight shorts (lawsuits, pushing holders to withdraw lending) have very low
  returns afterwards; the shares-not-lent mechanism is named. Relevance: recall behaviour of retail
  holders is a supply shock, not a price signal.
- **Brunnermeier & Pedersen (2005, *J. Finance*)** (cited in `methods-short.md` §1.2): a squeeze arises
  when price rises a lot and the loan is recalled; theory plus cases.
- **Days to cover, short-interest percentage, float rotation, retail attention** are the practitioner
  list. The sources are vendor and education pages. A 2026 vendor backtest of 2,269 stocks reported that
  very high short interest (>30%) was not a reliable squeeze signal and that the lowest days-to-cover
  quintile had the highest returns *(vendor/anecdotal, one unreviewed source, snippet)*. The
  conventional thresholds (days to cover above 5 or 10) are conventions, not tested. I found **no
  peer-reviewed study of float rotation or premarket volume as a squeeze predictor.**
- **Halts and manipulation** (`methods-short.md` §2): SEC suspensions of small Asian-listed names from
  late 2025, trading halts stuck for 10 days, are the extreme tail; these are not predicted by short
  interest.
- **IPO-specific lending constraints:** underwriters cannot lend for 30 days and the rest is locked up
  about 180 days; an S&P/IHS Markit note reports Lyft's fee briefly near 100% on the first settlement day
  *(vendor, snippet)*; Geczy, Musto & Reed (2002) found most IPOs shortable soon after listing and
  a later study found the tightest constraints near lockup expiry with indicative fees above 15%
  *(secondary, via a conference paper's literature review; not verified)*.

### 3.2 What can be computed at 09:45 from data we have (point-in-time)

| candidate | source | known at 09:45 on D? | verdict |
|---|---|---|---|
| FINRA short interest (SI) | FINRA twice-monthly files, published about 7 business days after each settlement date (Federal Register filing snippet); FINRA proposed weekly reporting in May 2026 (SR-FINRA-2026-012; approval not confirmed) | Only the **last published** value, stale by up to about 4 weeks; use the **publication** date as the as-of date, not the settlement date, or look-ahead enters | Usable as a slow state variable: SI / shares outstanding, days to cover = SI / 20-day average volume. Low weight; the vendor evidence is weak. |
| FINRA daily short-sale volume | Daily Reg SHO short-volume files (off-exchange trades only; includes market-maker flow; not short interest) | D-1 and earlier only; D's file is published after the close | Usable for D-1 ratio (short volume / total volume). Ambiguous sign (shorting activity means shares were available; it also means shorts are exposed). Test both directions. |
| Reg SHO threshold list | SRO lists; criteria: fails to deliver at least 10,000 shares and 0.5% of shares outstanding for 5 consecutive settlement days; close-out at 13 days (search summaries of the 2005-2008 rulemaking; current text not checked) | D-1 and earlier; already in the repo (`x.regShoListedD1`) | Usable. Persistent fails signal hard-to-borrow, and the repo run is positive for the short: dropping the 15 listed names (of 324) lowers the mean from +8.16% to +7.36%, which implies about +25% mean on those 15 names (my unweighted arithmetic from the table; 15 trades, not a finding). |
| Float proxy | XBRL cover-page shares outstanding (`dei:EntityCommonStockSharesOutstanding`) and `dei:EntityPublicFloat` (a dollar value at the last Q2 end); tag names from memory, **not verified this pass** | As of the latest filing; stale for names that did an ATM or offering since | Shares outstanding is not float. Treat as an upper bound on float; flag stale if the filing is over 120 days old (my threshold). |
| Premarket volume and float rotation | Own 04:00-09:45 bars; rotation = premarket shares / shares outstanding | Yes | Computable and cheap; no source tests it as a squeeze predictor, so it is a hypothesis with a mechanism (turnover above 100% of a tiny float). |
| Halts history | Nasdaq/NYSE trade-halt files; own zero-volume minutes | Prior-day and premarket halts yes | Flag prior halts in the last 5 sessions as a manipulation/continuation marker. |
| Borrow fee spikes | Markit, S3 or broker data | **No** | Not available. |
| Retail attention | News tags in the repo, premarket volume rank, prior-day top-mover rank | Partly (the repo has `priorDayTopMoverRank`) | Day-2 of a run is where the repeat-gapper split was already proposed (`methods-short.md` §3). |

### 3.3 Reading for this strategy

- The signal's own universe is the hard-to-borrow one: Schultz says squeezes concentrate there, so a
  squeeze-aware filter on this signal is also an availability filter. In the run, the +100% names that
  fade the most are probably the least lendable (`methods-short.md` §1.4). The tail trades (MAE above
  +50%, 8.3% of trades) are the candidate squeeze events and can be examined directly: compare their
  09:45 features (float proxy, premarket rotation, prior-day halt, threshold listing, day number of
  the run) with the rest, descriptively on train.
- With only about 27 trades above +50% MAE in 324 (8.3%, my arithmetic) a classifier has too few positives;
  use a tiny number of pre-specified binary flags, not a fitted model.

### What to test (train only, via an amendment)

- **Descriptive tail anatomy (no trial):** for the +100% ext<10% cell, list the trades with MAE above
  +50% (about 27) and compare with the others on: premarket volume / shares outstanding (rotation),
  shares outstanding (below 10M, 10-30M, above 30M), threshold-listed D-1, a halt in the prior 5 sessions,
  day number of the run (1, 2, 3 or more), D-1 short-volume ratio (terciles), SI/shares outstanding from
  the last published file. Report the share of MAE>50% trades in each bucket.
- **Pre-registered flags, three at most:** (A) rotation above 1.0 (premarket shares exceed shares
  outstanding); (B) shares outstanding below 10M or unknown; (C) a halt in the prior 5 sessions or
  threshold-listed. Run the fade with each flag excluded and with any-flag excluded; report mean net,
  worst 1% and trade count. Keep the p99 MAE as the main outcome, not the mean.
- **Point-in-time check:** assert that every SI value used has a publication date at or before D-1 and
  every XBRL value a filing date at or before D-1.

---

## 4. Historical shortability proxies when ETB history is missing

### 4.1 What the literature uses

- **Lending-market data** (Markit/IHS, formerly Data Explorers; one lender or several): D'Avolio (2002,
  *J. Financial Economics* 66, 271-306), Kolasinski, Reed & Ringgenberg (2013), Engelberg et al.
  (2018), Drechsler & Drechsler (2014), Saffi & Sigurdsson (2007, via a secondary mention: lending
  supply and fees as constraint proxies) *(all abstracts/snippets; figures in `methods-short.md` §1.1)*.
  D'Avolio's small, low-institutional-ownership, high-disagreement stocks are the ones that go on special
  (secondary). Drechsler & Drechsler: about 80% of stocks are cheap to short; the top fee decile averages
  about 5.8% a year (secondary, cxoadvisory summary). These are lendable stocks; a gapper on thin float
  is often not lendable and has no row (`methods-short.md` §1.1).
- **Institutional ownership and size** as availability proxies: D'Avolio (2002) uses them; the paper is
  cited in the Springer put-call paper I saw only as a snippet *(secondary)*. Not usable as is for a
  09:45 signal because the 13F data lag by a quarter and do not cover the float of micro-caps.
- **The Reg SHO threshold list as a proxy.** Diether, Lee & Werner (2009, *Review of Financial Studies*
  22(2), 575-607) report that threshold-list appearance **suppresses subsequent short selling** and does
  not significantly affect returns for stocks priced at $5 or more *(snippet, search summary of the
  paper)*. Evans, Geczy, Musto & Reed (2009, *RFS* 22(5), 1955-1980), "Failure is an option": a
  major options market maker mostly **fails to deliver rather than borrow** when shares are hard to
  borrow, and the link between borrow cost and option prices weakens when failing is cheaper
  *(abstract)*. Reading: a threshold listing means persistent fails, which indicate hard-to-borrow
  conditions; it is a **lagging** binary flag (five consecutive settlement days of fails) and misses a name
  that became hard to borrow this week.
- **Short interest level as a proxy.** Boehmer, Huszar & Jordan (2010, *J. Financial Economics* 96(1),
  80-97): high short interest is followed by negative abnormal returns but transient and of debatable size
  (abstract). Short interest is the stock of shorts, a result of availability, not a measure of it, and
  for names with a big float change after the last settlement date it is out of date.
- **Daily short volume share.** Diether, Lee & Werner (2009) use Reg SHO daily short-sale data (2005
  pilot era, NYSE and Nasdaq); short sales were 24% of NYSE and 31% of Nasdaq volume in the sample and
  mostly short-term (secondary). Boehmer, Jones & Zhang (2008, *J. Finance*) find shorting flow informative
  and stronger where constraints were relaxed (via a secondary paper's description). A Springer paper
  uses the short ratio as a robustness check for constraint proxies *(snippet)*. Practitioner and FINRA-cited
  cautions: the public daily file covers only off-exchange trades, includes market-maker hedging flow,
  and is not short interest (OTC Markets and Ortex posts, vendor/anecdotal).
- **Not found:** a paper that validates the FINRA daily short-volume share, the threshold list or short
  interest against actual ETB/HTB or locate success for sub-$20 gappers. The three are indirect.

### 4.2 Recommended proxy: an "availability tier", backtestable from point-in-time data

Build a tier for every signal at 09:45 from data the repo has or can derive. Thresholds are my
assumptions, chosen from the mechanisms above (lockups about 180 days, underwriter lending restriction 30
days, threshold criteria), not from a fit.

- **Tier 3 (likely unborrowable):** any of: threshold-listed on any of the last 10 sessions; shares
  outstanding (latest filing) below 10 million or unknown; listing or IPO age under 90 days; last close
  under $1; a halt in the prior 5 sessions.
- **Tier 1 (likely ETB or an easy locate):** none of the Tier 3 conditions **and** shares outstanding at
  least 30 million **and** listing age at least 180 days **and** last close at least $2.
- **Tier 2:** everything else.
- **Locate-success stress by tier** (assumptions, not estimates): Tier 1 90% success, Tier 2 50%, Tier 3
  10%, sampled by a seeded draw per symbol-day (the same names in every configuration, as in the
  existing stress). This replaces the flat 30%/60% random failure, which treats a +100% micro-cap like a
  mid-cap.
- Use the tier as a **gate**, not a score: a signal in Tier 3 is reported but not counted in the
  tradeable total.

Why this rule: it combines the availability mechanism (float, age, price), the only direct regulatory
flag (threshold list), and a stale-state flag (halt), and each input is point-in-time. It will
misclassify some names; the purpose is to bound the effect, and a live pilot (log `borrow_status` and the
locate quote at 09:40 and 09:46, no orders; `methods-short.md` §5) can score the tier against truth.

### What to test (train only, via an amendment)

- Fade result by tier (mean net, 95% interval, worst 1%, p99 MAE, trade count) for the +100% ext<10%
  cover-15:55 cell and the +50% cell. Expected: the tradeable edge is whatever Tier 1 and Tier 2 retain. If
  Tier 1 has fewer than about 40 trades, report that the cell cannot support a claim.
- Tier-weighted locate stress (90/50/10) with the 2.5% locate fee and 100-share lots, compared with the
  existing combined stress (+6.11% on 126 trades for the +100% ext<10% cell).
- Sensitivity of the tier thresholds (shares outstanding 5M/10M/20M; age 60/90/180 days; price $1/$2);
  report three variants and not a grid, to keep the trial count low.
- Each of the five inputs one at a time (leave-one-out) to see which carries the result.
- The Reg SHO D-1 split already in the run (included versus dropped) should be extended to
  "listed any of the last 10 sessions".

---

## 5. Is there any evidence of intraday fade-short P&L on runners or gappers, net of borrow?

**I found none that I would cite.** Searched: academic, working-paper, regulator and practitioner sources.

- **Academic, closest:** Diether, Lee & Werner (2009): daily short selling predicts next-month returns,
  but the working paper says costs of a daily-rebalanced long-short strategy "wipe out" profits and the
  published version reports abnormal returns of about 1.39% (NYSE) and 1.41% (Nasdaq) a month for the
  top-minus-bottom short-selling quintile spread *(snippet, secondary; the figures differ across
  versions)*. It is not a gapper fade and not net of borrow on small caps.
- **Closest event-study:** Xu & Liu (2012) above: continuation, not reversal, for 1.5 days after a
  high-short-interest jump; the sample excludes very small and low-priced stocks.
- **Closest cost study:** Schultz (2024): squeeze cost consumes most of the excess return of shorting
  hard-to-borrow stocks (secondary figures); monthly, not intraday.
- **Closest attention evidence:** Barber, Huang, Odean & Schwarz (2022) and Leuz et al. (see
  `methods-short.md` §3) show the buyers lose, not that a short earns net of borrow.
- **Zawadowski, Andor & Kertesz (arXiv cond-mat/0406696)**: short-term reversal after intraday jumps in
  NYSE/Nasdaq stocks priced above $10; NYSE spreads eliminate most profits, Nasdaq spreads are small
  *(snippet, working paper)*. Not the population.
- **Practitioner:** a Substack author (stonkscapital, vendor/anecdotal) backtests shorting the open of
  gaps above 50% with premarket dollar volume above $200K, 3,636 instances "since 2008", covering at the
  close with a wide stop. Borrow is modelled at $0.01 a share from nine months of the author's own
  fees; locate availability is not discussed; the stop is undisclosed; **results appear only as
  charts, with no win rate, mean or worst loss**. A QuantConnect forum thread and a QuantRocket
  notebook on borrow-fee costs describe modelling choices, not gapper results; a TradingView script's notes
  warn that small-cap backtests are optimistic because of slippage, spreads, halts, SSR and premarket
  data, and that locate cost varies from near zero to about $1 a share by broker and name (all
  vendor/anecdotal, snippets). None gives net numbers.
- **Conclusion:** the study's train result, with its limitations, is the only quantified intraday
  fade-short on +100% premarket gappers that I know of, and it has no ETB history. Say so in the report. The
  decisive check is the live no-order locate-logging pilot; a published net P&L does not exist to
  compare against.

### What to test (train only, via an amendment)

- Report the fade in three columns (gross; after the frozen costs; after borrow, locate fee and
  availability tier) for the +50% and +100% cells, so a reader can compare each column with whatever
  source appears later.
- Add a **cost-wedge table**: mean net at 0% APR, 50% APR, 200% APR, 500% APR; locate fee 0/1/2.5/5%;
  tier-weighted locate success. The wedge, not the headline, is the claim.

---

## Summary: what changes the decision

1. **A fixed price stop is the wrong tool** and the data show it (Kaminski-Lo predict it): +10% turns
   +8.16% into +1.85% with 49.1% of trades stopped. A wide, catastrophe-only or regime-conditional stop is
   the only version the theory supports; test it on the fit window and hold out the eval window.
2. **Size is the primary tail control.** The worst MAE (+145%, and +292% in the random short) and a
   realised loss above 100% in the combined stress mean notional must be capped from MAE, around 1-1.5% of
   equity per trade for a 1.5-2% risk budget, and aggregate exposure capped as well.
3. **Kelly is an upper bound only.** Use the lower bound of the interval, a fraction of the empirical
   maximiser, and a day-level stationary bootstrap for drawdown and ruin; stress by replacing the worst
   1% with -100% and -150%.
4. **The squeeze here is a lending-market event.** Schultz says squeezes concentrate in hard-to-borrow
   names; those are this signal's names. Pre-register three binary flags (rotation, tiny shares
   outstanding, prior halt or threshold listing); I found no peer-reviewed support for float rotation
   itself.
5. **Shortability needs a proxy until pilot data exist.** Use the three-tier availability rule with
   tier-weighted locate failure instead of random skipping.
6. **No published net intraday fade-short evidence exists** on gappers; the study's result is
   unreplicated. Treat the +8.16% as a gross-of-availability upper bound.

---

## Sources

Read this pass (page text), unless marked otherwise:
- stonkscapital, "The Science of Shorting: Using Backtesting to Find Small-Cap Strategies",
  https://stonkscapital.substack.com/p/the-science-of-shorting-using-backtesting (vendor/anecdotal; no numbers given).
- P. Schultz, "Short squeezes and their consequences", *J. Financial and Quantitative Analysis* 59(1),
  68-96, 2024, https://www.cambridge.org/core/journals/journal-of-financial-and-quantitative-analysis/article/short-squeezes-and-their-consequences/63F30135D28474EEFE7AC0C47967FE98
  (peer-reviewed; abstract page read; figures via secondary summaries at
  https://www.evidenceinvestor.com/post/the-consequences-of-short-squeezes and
  https://rpc.cfainstitute.org/research/financial-analysts-journal/2023/short-squeezes).
- W. Xu, B. Liu, "Short squeeze" (2012), CXO Advisory summary,
  https://cxoadvisory.com/short-selling/short-squeeze-timeline (working paper via secondary summary; paper not read).
- S. Boyd page for Busseti, Ryu & Boyd, "Risk-constrained Kelly gambling", *J. Investing* 2016,
  https://web.stanford.edu/~boyd/papers/kelly.html and https://arxiv.org/pdf/1603.06183
  (peer-reviewed; abstract read; the PDF text was not extractable).

Abstract, search summary or secondary only (do not cite figures without re-reading):
- K. Kaminski, A. Lo, "When do stop-loss rules stop losses?", *J. Financial Markets* 18, 234-254, 2014,
  https://dspace.mit.edu/bitstream/handle/1721.1/114876/Lo_When%20Do%20Stop-Loss.pdf ;
  https://econpapers.repec.org/RePEc:eee:finmar:v:18:y:2014:i:c:p:234-254 (peer-reviewed). Mean-reversion
  statement and AR(1) >= Sharpe condition: https://www.advisorperspectives.com/commentaries/2025/10/23/stop-losses (secondary).
- A. Lo, A. Remorov, "Stop-loss strategies with serial correlation, regime switching, and transaction
  costs", *J. Financial Markets* 34, 1-15, 2017, https://dspace.mit.edu/handle/1721.1/107017 ;
  https://www.cxoadvisory.com/equity-premium/stop-losses-on-stock-positions-in-depth (peer-reviewed; secondary).
- Y. Han, G. Zhou, Y. Zhu, "Taming momentum crashes: a simple stop-loss strategy" (*J. Financial
  Economics* 2016), summaries at https://alphaarchitect.com/2016/08/taming-the-momentum-roller-coaster-fact-or-fiction/ ;
  https://www.cxoadvisory.com/technical-trading/stop-losses-to-avoid-stock-momentum-crashes/ (secondary; versions differ).
- L. MacLean, E. Thorp, W. Ziemba, "Long-term capital growth: the good and bad properties of the Kelly
  and fractional Kelly capital growth criteria", *Quantitative Finance* 2010,
  https://www.stat.berkeley.edu/~aldous/157/Papers/Good_Bad_Kelly.pdf (peer-reviewed; summary only).
- J. Baker, I. McHale, "Optimal betting under parameter uncertainty: improving the Kelly criterion",
  *Decision Analysis* 10(3), 189-199, 2013, https://ideas.repec.org/a/inm/ordeca/v10y2013i3p189-199.html
  (peer-reviewed; abstract).
- S. Grossman, Z. Zhou, "Optimal investment strategies for controlling drawdowns", *Mathematical
  Finance* 3(3), 241-276, 1993, https://ideas.repec.org/a/bla/mathfi/v3y1993i3p241-276.html (peer-reviewed;
  abstract). Discrete-time caveat (Nowicki & Klass 2005) via https://portal.mardi4nfdi.de/wiki/Publication:2567182 (secondary).
- D. Politis, J. Romano, "The stationary bootstrap", *J. American Statistical Association* 89, 1994,
  https://gnosis.library.ucy.ac.cy/handle/7/57533 (peer-reviewed; abstract record).
- S. Diether, K.-H. Lee, I. Werner, "Short-sale strategies and return predictability", *Review of Financial
  Studies* 22(2), 575-607, 2009, https://econpapers.repec.org/RePEc:oup:rfinst:v:22:y:2009:i:2:p:575-607 ;
  working paper https://www.cis.upenn.edu/~mkearns/finread/short_sellers_predict_returns.pdf (peer-reviewed;
  search summary).
- R. Evans, C. Geczy, D. Musto, A. Reed, "Failure is an option: impediments to short selling and options
  prices", *Review of Financial Studies* 22(5), 1955-1980, 2009,
  https://repository.upenn.edu/fnce_papers/337 (peer-reviewed; abstract).
- J. Engelberg, A. Reed, M. Ringgenberg, "How are shorts informed? Short sellers, news, and information
  processing", *J. Financial Economics* 105(2), 260-278, 2012,
  https://ideas.repec.org/a/eee/jfinec/v105y2012i2p260-278.html (peer-reviewed; abstract).
- E. Boehmer, Z. Huszar, B. Jordan, "The good news in short interest", *J. Financial Economics* 96(1),
  80-97, 2010, https://ideas.repec.org/a/eee/jfinec/v96y2010i1p80-97.html (peer-reviewed; abstract).
- O. Lamont, "Go down fighting: short sellers vs. firms", NBER w10659 / *Review of Asset Pricing Studies*
  2012, https://nber.org/papers/w10659 (peer-reviewed/working paper; snippet).
- A. Zawadowski, G. Andor, J. Kertesz, "Short-term market reaction after extreme price changes of liquid
  stocks", arXiv cond-mat/0406696, https://arxiv.org/abs/cond-mat/0406696 (working paper; snippet).
- S&P Global Market Intelligence/IHS Markit, "Short selling recent IPOs",
  https://www.spglobal.com/marketintelligence/en/mi/research-analysis/short-selling-recent-ipos.html ;
  Geczy-Musto-Reed and Edwards-Hanley via https://efmaefm.org/0EFMAMEETINGS/EFMA%20ANNUAL%20MEETINGS/2017-Athens/papers/EFMA2017_0110_fullpaper.pdf
  (vendor; working-paper literature review; secondary).
- FINRA, Rule 4560 and short-interest data, https://www.finra.org/rules-guidance/rulebooks/finra-rules/4560 ;
  https://finra.org/finra-data/browse-catalog/equity-short-interest ; proposal SR-FINRA-2026-012,
  https://www.sec.gov/files/rules/sro/finra/2026/34-105482.pdf (regulator; publication lag from a Federal
  Register snippet; weekly-reporting approval status not confirmed).
- Reg SHO threshold-list criteria (10,000 shares, 0.5% of shares outstanding, 5 settlement days, 13-day
  close-out): https://www.willkie.com/publications/2007/08/sec-adopts-and-reproposes-amendments-to-regulati__ ;
  https://tapeboard.com/glossary/what-is-regulation-sho (law-firm summary and vendor glossary; current rule
  text not checked).
- Daily short-volume caveats: https://blog.otcmarkets.com/2023/05/08/what-investors-should-know-about-finra-daily-short-sale-volume-data/ ;
  https://public.ortex.com/why-daily-short-volumes-are-often-misinterpreted/ (vendor/anecdotal).
- Short-squeeze vendor pages (days to cover and a 2026 screener backtest): https://fffinstill.com/blog/short-interest-backtest-squeeze-candidates-sector-analysis ;
  https://cdn.ihsmarkit.com/www/pdf/Short_Squeeze_model.pdf (vendor; the PDF was not readable).
- Backtest-cost practitioner threads: https://www.quantconnect.com/forum/discussion/8643 ;
  https://quantrocket.com/codeload/borrow-fees-alpha/borrow_fees_alpha/Introduction.ipynb.html ;
  https://in.tradingview.com/script/nTO30u4C-Small-Cap-Sell-Every-Spike-Rendon1 (vendor/anecdotal; snippets).
- Earlier files in this study (not re-read for sources): `methods-short.md`, `methods-exit.md`,
  `methods-execution.md`, `runs/explore/fade-short-20261109-1733.md`.

Not read, cited from memory or mentioned only by other sources (not verified): Han, Zhou & Zhu full
paper; Boehmer, Jones & Zhang (2008); Saffi & Sigurdsson (2007); Geczy, Musto & Reed (2002);
Edwards & Hanley (2010); Nowicki & Klass (2005); the XBRL tag names
(`dei:EntityCommonStockSharesOutstanding`, `dei:EntityPublicFloat`); the generalised Pareto/EVT tail
method; Thorp's Kelly papers; Asquith, Pathak & Ritter (2005); Nagel (2005).

**Not found:** (1) any paper or credible practitioner source with intraday fade-short P&L on runners or
gappers net of borrow and locate; (2) a peer-reviewed predictor of squeezes in sub-$20 premarket gappers
(Schultz's predictors were not readable); (3) a peer-reviewed test of float rotation, premarket volume or
days to cover as a squeeze predictor; (4) a validation of the threshold list, short interest or the
FINRA daily short-volume share against actual borrow status for micro-caps; (5) a study of stops on
intraday mean-reverting small-cap books; (6) the share of +100% gappers with listed options; (7) a
confirmed formula for the Baker-McHale shrinkage or the Busseti-Ryu-Boyd drawdown bound.

My own arithmetic, not from a paper: the stop give-up differences (3.96, 6.31 points), the 1/1.09 and
1/0.66 Kelly caps, the notional-from-MAE cap (B/MAEmax: 1.4% and 0.7%), about 83 trades per year, the
implied +25% on 15 threshold-listed names, about 27 trades above +50% MAE, and every assumed threshold
or percentage in the "What to test" lists and the tier rule (tier cut-offs, 90/50/10 locate success,
k in {4, 6, 8}, +30 to +100% stops, block length 3 days, f grid). They are starting points for train, not
estimates.
