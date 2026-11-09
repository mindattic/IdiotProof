# Gapper study: what the day-trading evidence says about a long-only small-cap entry (IP-T491)

A web pass on one question: the study detects gappers (an evening watchlist of 100 holds 74% of the
next day's +100% runners; the premarket alarm fires about an hour before a +20% move) but every
long-only way of trading them lost money on train, before costs. Is that result normal for day
traders, and does any credible evidence point to an entry or exit family we have not tried? It
complements `literature.md` (which already holds Berkman et al. 2012, Barber et al. 2022, Gao et
al. 2018, Zarattini et al. 2024, the DERA LULD paper, Aït-Sahalia et al. 2024), `methods-exit.md`
and `methods-execution.md`. Those are referenced, not repeated.

The study's own findings this note is read against (from `runs/explore/watchlist-20261109-1945.md`
and `runs/explore/alarm-ride-20261109-1929.md`):

- Runners (high >= +100% over the adjusted prior close) close a median 36.23% under their high;
  21.7% close more than 50% under it. Median high is +137.6%; the median time from ignition (k=10)
  to the high is 96 min with a median +77.9% left after ignition.
- All 2,592 watchlist ride configurations lose, gross included (best gross -0.32%, best net
  -2.20% at 0.25x spread). All 1,320 alarm-ride configurations lose net; the best gross is +0.29%
  per trade against a mean frozen cost of 1.94%.
- Ignition on the top 50 catches 39% of eval runners with 16 false ignitions per caught runner.
- The user's view ("catch it while it forms, sell when velocity dies") is the thing under test.

**Evidence labels used throughout.** *Peer-reviewed* (journal), *working paper* (SSRN/NBER/SFI
series, not refereed), *regulator* (SEC, FTC, FINRA, a national securities regulator's own data),
*broker-verified* (statements from a broker, with or without an independent accountant),
*vendor/anecdotal* (educator, broker blog, forum, tool maker). Anything I could not read in the
source is marked *(abstract only)* or *(secondary)*. No figure here is estimated by me.

**Read vs not read.** Read in full text this pass: Barber, Lee, Liu & Odean working paper (Odean
faculty-page PDF, "Day Trading Skill", May 2011 version) and Zarattini, Barbon & Aziz (SFI
24-98 PDF). Everything else is from abstracts, regulator pages or search summaries and is marked.

---

## 1. Day-trader profitability studies

### 1.1 Barber, Lee, Liu & Odean, Taiwan 1992-2006 (peer-reviewed lineage; this version a working paper)

- **Source.** Barber, Lee, Liu and Odean, "Do Day Traders Rationally Learn About Their Ability?"
  (later circulated as "Learning Fast or Slow" and "Day Trading Skill"). The full text I read is
  the May 2011 working-paper version. Their related 2009 paper "Just how much do individual
  investors lose by trading?" is in the *Review of Financial Studies*, which I did not re-read.
  The 2011 text says it uses complete Taiwan Stock Exchange transaction data, 1992-2006.
- **What fraction profits.** "In the average year, about 360,000 Taiwanese individuals engage in
  day trading and about 15% of these day traders earn abnormal returns net of fees"; in the
  persistence section the same text says "about 13% earn profits net of fees in the typical
  year". Both numbers appear in the paper; they are different measures (abnormal return vs raw
  profit). The paper's own words: "the vast majority of day traders lose money".
- **Do winners persist.** Yes, but only a sliver. Traders are ranked on year y and followed in
  y+1: "only the 1,000 most profitable day traders (less than 1 percent ...)" go on to earn
  reliably positive abnormal returns net of costs. The top 500 earn 49.5 bps/day gross and 28.1
  bps/day net; the worst-ranked earn -17.5 gross and -34.2 bps/day net. Costs assumed: 10 bps
  round-trip commission plus a 30 bps transaction tax on sales.
- **Who the winners are.** Heavy traders with a past record of profit, experience, willingness
  to short, and concentration in few stocks. A trader at the 75th percentile on every
  characteristic has only about a 25% probability of a net-positive year. Past profitability is
  "by a large margin" the best predictor.
- **Mechanism.** The winners are **not** liquidity providers: even the top 500 place aggressive
  (marketable) orders (71% of their trades versus 67% for all individuals) and gain by anticipating
  short-term moves. Little of the profit is concentrated around earnings announcements.
- **Relevance.** (a) The base rate for a rule-free retail day trader is about 85% losing. (b) The
  persistent winners are a tiny group trading a liquid, low-friction market: Taiwan's average
  effective spread is 64 bps, while our frozen round trip is 2-4.5%. Taiwanese day traders cleared
  about 40 bps/day in costs; our cost line is 5-10 times that. (c) "Willing to short" and
  "concentrated in few stocks" are predictors of success, which sits oddly with a long-only,
  many-name design. Our short side is deferred (`methods-short.md`), so this is a documented
  gap in the study, not a conclusion.

### 1.2 Chague, De-Losso & Giovannetti, Brazil equity-index futures (working paper; regulator data)

- **Source.** "Day Trading for a Living?" (SSRN 3423101, 2019, revised 2020). *(abstract and
  secondary summaries only)* The data come from the Brazilian securities regulator and cover
  19,646 individuals who began day trading mini-Ibovespa futures between 2013 and 2015.
- **What fraction profits.** Of those who persisted more than 300 days, 97% lost money; only 1.1%
  earned more than the Brazilian minimum wage and 0.5% more than a bank teller's starting salary.
  The 97% applies to the persistent subgroup, not to every entrant.
- **Persistence/learning.** The authors report no evidence of learning by day trading: persisting
  does not turn losers into winners.
- **Caveats.** One futures market, one period; futures are cheap to trade so costs do not explain
  the result. It says nothing about single-stock gappers.

### 1.3 US evidence

- **Jordan & Diltz, "The Profitability of Day Traders", *Financial Analysts Journal* 59(6), 2003
  (peer-reviewed).** *(abstract only)* Feb 1998 - Oct 1999, seven branch offices of a day-trading
  firm (one secondary source: 334 active accounts). Losers outnumber winners about two to one;
  about 20% were "more than marginally profitable"; profitability was linked to Nasdaq movement,
  i.e. a bull-market sample. The industry's claim they test: "60 percent of day traders are
  profitable after an initial learning period".
- **Harris & Schultz 1998, Garvey & Murphy 2005a/b** (cited in Barber et al.). Small samples of SOES
  bandits and proprietary traders who made money mostly by placing limit orders inside the quote
  on ECNs, at zero commission with profit-share. Garvey & Murphy analyse round trips only, which
  the Barber paper notes biases results toward winners. *(secondary, via Barber et al. text)*
- **FINRA pattern-day-trader rule (regulator).** FINRA Rule 4210's $25,000 minimum equity and the
  PDT designation were replaced by an intraday margin standard: SEC approval is dated 14 April
  2026 (Release 34-105226) with an effective date of 4 June 2026 *(secondary: trade press and
  broker blogs; check FINRA Regulatory Notice 26-10)*. This is a rule change, not a profitability
  finding. I found no FINRA, SEC or state-regulator study of what fraction of US retail day
  traders profit. The regulators' position that most lose is asserted in the Jordan & Diltz
  framing, not measured in a document I could read.
- **Implication for us.** The only large-sample US-era profitability evidence I could find is from
  Taiwan and Brazil. For US small caps specifically there is no independent population study.

### What to test (train only, via an amendment)

- **Benchmark the strategy against the base rate, not against zero.** Report the share of
  train *days* on which each ride family is net positive, and the share of *months*. A
  "profitable day trader" in Barber et al. is a person-year; the closest equivalent is a
  month-level hit rate with a binomial confidence interval. Parameters: monthly net > 0, at the
  frozen cost and at 0.5x/0.25x spread.
- **Persistence screen on the strategy's own months.** Rank folds' months by net return in fold
  k, check the same configuration in fold k+1 (the Barber design). With all 3,900+ configurations
  losing this will show nothing; it is a cheap guard against the one config that wins a fold.
- **Short-willingness check.** Re-run the best ride family with a mirrored short leg only where
  `methods-short-risk.md` allows (borrow available, SSR rules); the Taiwan winners shorted. Report
  as a comparison only.

---

## 2. Small-cap momentum educators' verified results

### 2.1 Warrior Trading and the FTC (regulator; vendor)

- **Sources.** FTC press release of April 2022 and complaint, FTC v. Warrior Trading, Inc. and
  Ross Cameron (D. Mass. 3:22-cv-30048); stipulated order; January 2023 refund notice.
  *(search summaries of the FTC pages; I did not open the complaint PDF)*
- **What the FTC alleged.** Warrior's advertising presented Cameron's results as "profitable" and
  "scalable"; the complaint cites ads claiming he turned $583 into more than $1,000,000 and made
  $3.3 million in profit over several years; the FTC said the vast majority of customer accounts
  lost money, in some cases more than the course cost. No admission of wrongdoing.
- **Outcome.** $3 million of consumer redress plus an order barring unsubstantiated earnings
  claims; more than $2.9 million was returned to 20,402 people in January 2023. The FTC did not
  allege Cameron's own trades were fabricated; the case is about the representativeness of his
  results and the marketing around them *(secondary commentary)*.
- **What is audited versus claimed.** Warrior's own site publishes monthly broker statements for
  Cameron's account and links an "Independent Accountant's Report" on them *(pages listed in
  search results: warriortrading.com 2024 and 2025 "Verified Earnings Statements"; I did not open
  the report itself)*. A third-party blog quotes a cumulative figure of $18,810,638 for 2017-2025
  from that page; I could not confirm it from the primary source and do not rely on it. So: the
  **instructor's own account** is broker-verified (per the vendor's page), **student outcomes** are
  not, and the one independent finding about students is the FTC's, which is adverse. Neither
  disclosure gives per-trade data, setups, cost, size or capacity, so no rule can be reproduced.
- **Independent verification of a small-cap gapper momentum edge.** None found. A verified P&L
  for one trader is evidence that one person made money, which is what the Barber et al. top
  500 also show; it does not show the setup is an edge in the population.

### 2.2 Other educators

- Vendor/anecdotal throughout (course sites, YouTube, forums). Not read. Treated as zero
  evidence, consistent with how `literature.md` treats the "70% open day 2 below the day-1 open"
  Reddit backtest.

### What to test (train only, via an amendment)

- **Nothing to replicate from the educators** because no rule set with numbers is verifiable. One
  falsifiable item: educator folklore says the edge is concentrated in a few days a year. Report
  concentration for every ride family: share of total gross P&L from the top 1%, 5% and 10% of
  trades and days, and the leave-top-k-days-out net (the harness already reports P&L
  concentration and leave-one-month-out; add leave-top-k-days-out for k = 1, 3, 5, 10).

---

## 3. Measured results for named setups

The honest summary: **no peer-reviewed, reproducible, net-of-cost study exists for the named
momentum-trader setups on sub-$20 small caps.** What exists is below, with the universe and the
cost assumption stated so it can be compared to ours.

### 3.1 Opening-range breakout (Zarattini, Barbon & Aziz 2024; working paper, SFI 24-98)

- **Source.** "A Profitable Day Trading Strategy For The U.S. Equity Market", Swiss Finance
  Institute Research Paper 24-98 (full text read). A companion paper, "Can Day Trading Really Be
  Profitable?" (Zarattini & Aziz 2023, SSRN 4416622), covers QQQ; *(abstract only)*.
- **Rules.** First 5-minute candle sets direction: bullish -> only long on a break of its high;
  bearish -> only short on a break of its low. Stop at 10% of the 14-day ATR from entry; exit at
  16:00. Commission $0.0035 per share (Interactive Brokers tiered entry level); 1% of capital
  risked per trade; 4x leverage cap; 2016-2023.
- **Universe filters (read from the text).** Price > $5, 14-day average volume > 1,000,000 shares,
  14-day ATR > $0.50; "Stocks in Play" add relative volume >= 100% and a top-20 daily rank.
- **Results (net of commission).** All stocks passing the base filters, long and short: 3.2% a
  year, volatility about 6.6%, **Sharpe 0.48** versus 0.78 for the S&P 500; the $25,000 account
  grew by about 30% in eight years. Top-20 relative-volume Stocks in Play: 41.6% a year, Sharpe
  2.81, alpha 36% a year, total net above 1,600%. Of the 5/15/30/60-minute ranges the 5-minute is
  the best; the authors say the reason is unclear.
- **What it means for us.**
  - The edge is a *selection* edge: the same entry with no relative-volume filter is a Sharpe 0.48
    strategy. Our watchlist and alarm are selection tools, so this is the closest literature to
    our setting.
  - It is **long and short**, direction decided by the first candle; ours is long only. About
    half of the result would be unavailable to a long-only variant.
  - Our $0.10-$20 band overlaps theirs only at $5-$20. Their commission model (0.35 cents/share)
    is 0.07% at $5 and is a fraction of our modelled 2-4.5% round trip, which is driven by the
    premarket spread multiplier (9.393) and impact; they enter at 09:35 with a stop order in
    regular hours. Their cost model is probably the single largest difference and is not
    stress-tested in the paper beyond commission.
  - A later blog replication on five index CFDs reports "gross reproduced, net zero"
    *(vendor/anecdotal; mql5.com blog; not the same universe)*. A QuantConnect forum recreation
    reports a Sharpe of about 2.4 *(vendor/anecdotal)*.
- **Caveat.** Working paper, one author is a day-trading educator (Aziz), the stops are optimized
  on the same sample, and there is no holdout beyond 2023. The year-by-year and cost-doubling
  tables were not checked.

### 3.2 Intraday momentum (Gao, Han, Li & Zhou, *JFE* 129(2), 2018; peer-reviewed)

- *(abstract plus working-paper summaries)* The first half-hour return, measured from the prior
  close, predicts the last half-hour return on the S&P 500 ETF, 1993-2013; one working-paper
  draft reports an in-sample R-squared of 1.6% and out-of-sample 1.2% (figures vary by version).
  Predictability is stronger on high-volatility and high-volume days. International replications
  find it in 12 of 16 markets *(secondary)*.
- **Relevance.** Index-level, last 30 minutes. It does not say a small-cap's first-30-minute
  strength continues over the morning. The only direct use here is as a reason to test a late-day
  (15:30) component, which `literature.md` already lists as a horizon (Baltussen et al.).

### 3.3 LULD up-pauses and halt-resume continuation

- **What exists.** SEC DERA white paper on the 2013-14 pilot (already in `literature.md`:
  frequencies and clustering, not post-pause returns). Christie, Corwin & Harris, *Journal of
  Finance* 57(3), 2002, on Nasdaq news halts *(abstract/secondary only)*: after five-minute
  intraday halts, volatility, volume and bid-ask spreads rise; secondary summaries say spreads
  more than double and volatility rises to more than nine times the normal level, with no such
  effect after overnight halts. Corwin & Lipson 2000 (NYSE halts): traders use the halt to revise
  orders and the reopening price is informative about the future price *(secondary)*.
- **What does not exist.** I found no peer-reviewed or working-paper study of the return after a
  US LULD *up* pause on small caps (continuation vs reversal). The only continuation/reversal
  evidence for price-limit hits is for China's daily limits (arXiv 1503.03548): continuation was
  more likely than reversal the next day, especially after down-limit hits. Different mechanism
  and a market with no short selling and T+1; do not transfer it.
- **Reading.** What is documented points *against* buying the resume: a reopening with doubled
  spreads and several-fold volatility is a cost trap for a marketable long. A halt-resume edge is
  therefore an open empirical question, and our data (the A1 halts block) is the first place it
  can be measured.

### 3.4 The other named setups (first pullback, VWAP reclaim, ABCD, micro-pullback, flush dip-buy, red-to-green, high-of-day break)

- **No academic or reproducible backtest found** for any of these on small caps. They appear in
  educator material (vendor/anecdotal) only. Where a relation to documented facts exists:
  - *VWAP reclaim / VWAP-pullback.* Already tested here and negative; the vendor base rate that
    ~75% of gappers close below VWAP is in `literature.md`.
  - *High-of-day break / premarket-high breakout.* Already tested here (premarket-high breakout)
    and negative.
  - *Red-to-green.* Is a long entry when the day's price crosses the prior close from below. In
    our sample 22.7% of runners stay under +20% premarket (OMH day-1 style) and 17.4% are already
    +50% at the first bar; a red-to-green gapper is a subset of the "gap down then reverse"
    population, which the watchlist does not hold. No evidence either way.
  - *Flush dip-buy / micro-pullback / ABCD.* Pullback-and-resume entries are variants of the
    pullback-to-VWAP we tested; the ride result (every config loses gross) already says that the
    path after the entry is net adverse on average.
- **The statistic that matters.** The fade is large and fast: median close -36.2% below the high
  and a median of 96 minutes from ignition to the high with +78% left, but only 74% of runners
  have ignition precede the high, and 16 false ignitions fire per caught runner. A pullback entry
  does not escape the 16:1 false-positive burden unless the pullback itself is a better
  discriminator, which is a question to test, not a documented fact.

### What to test (train only, via an amendment)

- **Zarattini-style ORB on the overlap band.** Restrict to price $5-$20 on train; direction from the
  first 5-minute candle (09:30-09:35, long only and long+short reported separately); entry on the
  break of the first candle's high; stop = 10% of the 14-day ATR (also 5% and 20%); exit 15:55 or
  stop; universe = the day's top 20 by relative volume in the first five minutes over the prior
  14-day average of the same window (also top 10 and top 50). Costs: frozen model, plus the
  paper's $0.0035/share for a direct comparison, plus 0.5x/0.25x spread. This is the one setup
  with a published positive net result near our universe.
- **Selection ablation.** Same ORB on (a) the evening watchlist, (b) premarket-alarm names, (c)
  top-20 relative volume at 09:35. If (a) or (b) beat (c), the study's detectors add value beyond
  Zarattini's filter; if not, the detectors are not the bottleneck.
- **Halt descriptive table.** For every train halt (A1) with an up-pause on a watchlist name: return
  from the reopening print to +5, +15, +30 min and to the close; spread at reopening against the
  prior minute. No trading rule until the table exists.
- **Named setups.** Only if a rule is written as a pure function of minute bars and a cost is
  applied; each is a variant of tested families and rates **not worth** a separate amendment
  unless the pullback discriminator is shown to cut false ignitions (test: share of runners among
  pullback entries versus ignition entries, same day sample).

---

## 4. Microstructure: why the retail long side loses on these names

### 4.1 Buying attention stocks earns negative returns (Barber, Huang, Odean & Schwarz 2022, *JF*; peer-reviewed)

- Already in `literature.md` (section 2): herding days +14% of which the opening return is +11%;
  five-day abnormal return -3% after top-bought days and -6% after extreme herding, sub-$1B caps
  only; losses concentrated in the intraday session. The relevance to a long-only gapper ride is
  direct: the paper's herded names are the same population (top-movers lists), and the profit is
  earned *at the open* by whoever sells to the herd, i.e. the long side pays the open.
- **Gloss for this study.** The retail long who buys after the alarm is the counterparty the paper
  describes. Our train result (gross -0.2% to -0.5% per trade) is the same sign as the paper's
  intraday loss.

### 4.2 Payment for order flow and wholesaler internalization (peer-reviewed; regulator; vendor)

- **Barber, Huang, Jorion, Odean & Schwarz, "A (Sub)penny for Your Thoughts", *JF* 79(4), 2024
  (peer-reviewed)** *(abstract/secondary)*: a validation with 85,000 trades in six retail
  accounts found the older sub-penny method identifies 35% of trades as retail and mis-signs 28%
  of those; midpoint signing cuts that to 5%. Press coverage reports no relationship between
  paid order flow and execution price in their own trades, and flags a small order size
  (a few shares per order). It is a **measurement** paper; it does not measure small-cap
  execution quality at our sizes.
- **Wholesaler adverse selection.** A working-paper result: wholesaler-bound retail flow has 31%
  less price impact than exchange-bound flow, and price improvement is 44% of the quoted spread
  in average S&P 500 stocks versus 3-6% on exchanges *(search summary; paper not identified
  precisely, treat as secondary)*. In low-volume small caps competition between wholesalers
  looks different and inventory costs are high *(secondary)*. So the documented price
  improvement is a large-cap fact; **I found no source that measures it for sub-$20 small caps
  in premarket.** This matters because our entries are premarket and Alpaca routes extended-hours
  orders to the venues described in `methods-execution.md` section 1.2.
- **Adverse selection against fast traders.** The argument that a marketable retail order on a
  momentum name is filled just before the price moves further (the dealer widens or lifts first)
  is consistent with the Barber et al. finding that successful Taiwanese day traders still pay
  aggressive-order costs; I did not find a direct measurement on US gappers. Opposing evidence:
  the Aït-Sahalia et al. (2024) imbalance collapse before the high (24.5 min median in our
  tradeflow run) suggests that the flow on the buy side is what disappears first.
- **Spread widening on momentum.** The Nasdaq halt studies above (spreads more than double after
  five-minute halts) and the existing repo evidence (premarket multiplier 9.393, frozen model
  floored at 0.5%) are the only quantitative support. The retail-side loss is not a mystery:
  Corwin-Schultz plus impact at the 2-4.5% level exceeds the best observed gross of +0.29%.

### 4.3 News versus no-news moves (Chan 2003; Savor 2012; peer-reviewed)

- Chan, *JFE* 70(2), 2003: stocks with public news drift (strong after bad news); no-news extreme
  moves reverse; effects mainly in small, illiquid stocks. Savor, *JFE* 106(3), 2012: price shocks
  with analyst reports drift, shocks without them reverse. *(abstract and secondary summaries)*
- **Relevance.** A gapper without a catalyst is the reversal case, i.e. the long loses. A gapper
  with a scheduled or hard catalyst is the drift case. `methods-catalysts.md` already builds a
  catalyst classifier; the explore watchlist run found 14x lift for a non-binding LOI and a
  catalyst classifier exists, but no ride was split by catalyst class.

### What to test (train only, via an amendment)

- **Cost realism, premarket versus regular hours.** Per `methods-execution.md` section 1.4:
  actual NBBO spread at the entry and exit seconds by hour, so the 9.393x multiplier can be
  checked against the 09:30-10:00 spread for the same names. If the regular-hours spread is below
  one third of the premarket one, rerun the ORB and halt tests at the lower figure before
  concluding they lose.
- **Split every ride result by catalyst class** (hard catalyst: 8-K item 1.01/2.02/8.01 with
  matching keyword, FDA/clinical, contract award; soft: none, social only). Hypothesis from Chan
  and Savor: gross is positive for the hard class and negative for the no-news class. Parameters:
  entry = alarm or ignition, news within 60 minutes (already built) versus no news within 24 h.
- **Opening-print dependence.** For each entry family report gross when entering at the 09:30
  opening print versus 09:31 versus 09:35 (the Berkman and Barber result says 09:30 is the worst
  price for attention names).

---

## 5. Untested entries with credible positive evidence

Ranking is by strength of positive evidence, and the honest ranking is that **only item 1 has a
published positive net result; items 2-5 have mixed or adverse evidence and are listed because
they are the cheapest tests of the user's thesis.**

### 5.1 ORB-5 on stocks in play, price $5-$20 (strongest)

- **Evidence.** Zarattini et al.: Sharpe 2.81 net for the top-20 relative-volume Stocks in Play,
  Sharpe 0.48 with no relative-volume filter; price > $5 floor. Working paper.
- **Rule.** Day D, 09:30-09:35 bar: relative volume = first-5-min volume / mean of the same window
  over the prior 14 days. Candidates: price in [$5,$20], prior-day volume > 1M, ATR14 > $0.50, rel
  vol >= 1.0, top N (N = 10, 20, 50) by rel vol. Direction by the 09:30 candle's color. Entry:
  stop-buy on the break of the candle's high (long) after 09:35, filled at the stop or the next
  bar open, whichever is worse; no entry after 10:30. Stop: x * ATR14 with x in {0.05, 0.10, 0.20}.
  Exit: 15:55 close. Report: long only, short only, both.
- **Why it might differ here.** Prior train results all start premarket on a +20% move; this
  starts at 09:35 on any name and long runners are a small subset. Expect weak results long
  only; the test is cheap and the published number makes it the right control.

### 5.2 News-confirmed drift with entry at or just after the open

- **Evidence.** Chan 2003 and Savor 2012 (drift after news, reversal without), restricted to
  small, illiquid stocks; Keown-Pinkerton/Chan for the up-move. Peer-reviewed, but daily or
  weekly horizons, not intraday. Counter-evidence: Berkman et al. (the open is the worst price
  for attention stocks).
- **Rule.** Candidate = watchlist name with a hard catalyst (8-K item in {1.01, 2.01, 7.01, 8.01}
  with a keyword for merger, acquisition, contract, FDA, partnership, or a press release flagged
  by `CatalystTypes`) published after the D-1 16:00 close and before 08:00. Entry variants: (a)
  opg order at the 09:30 auction (via the exit-route simulator's fill rule, mirrored), (b) limit
  at the opening print + 0.5% within the first 5 minutes, (c) 09:35 close. Exit: sell at 09:45,
  10:30, 15:55, and next-day open (multi-day hold arm). Parameters: gap bracket {5-20%,
  20-50%, > 50%}. Gate: only if the catalyst class has a train sample >= 150 events.

### 5.3 Day-2 dip-buy on day-1 runners

- **Evidence.** Mixed to adverse. Chan: no-news extreme moves reverse (so the first-day high is
  likely above day-2 prices; a bounce long loses). The repo's own number: 7.1% of runners run
  >= +100% again within five sessions (44x base), which is a continuation fact, but a 7% hit
  rate against a -36% median day-1 fade is not a long edge by itself. Anecdotal educator
  material says "buy the -30% flush for a day-2 bounce"; no source tests it.
- **Rule.** Day-1 runner = high >= +100%. Day 2: entry when price <= 0.70 x day-1 high (and also
  0.60, 0.50), only after 09:35 and only if day-1 had a hard catalyst (5.2) or a reverse split
  in the last 5 sessions; exit: +10%, +20% rebound to the day-1 close, or 15:55; stop -10%.
  Report both long (bounce) and its mirror (continued fade) so the sign is clear.

### 5.4 Halt-resume continuation

- **Evidence.** None positive; Christie et al. say reopenings carry doubled spreads and
  several-fold volatility. Marketable entries pay both. **Descriptive test only** (see 3).
- **Rule.** After an LULD up-pause on a name already at >= +50% over the prior close, buy a limit
  at the reopening print - 1% within 2 minutes (passive; never at the print); exit at +10%, a
  stop at the pre-pause price - 5%, or a 10-minute time stop. Count fills (the limit may never
  fill).

### 5.5 First pullback after the first 5-minute leg

- **Evidence.** None found. A variant of the VWAP-pullback entry already tested (negative).
  Listed because the user asked; a cheap re-run of the existing pullback grid with the pullback
  defined as 30-50% of the 09:30-09:35 range and entry at the next higher high is the rule.
  Rated **not worth** a separate amendment unless 5.1 shows the opening range itself carries edge.

### 5.6 Multi-day hold after merger or PIPE catalyst

- **Evidence.** Adverse. Hillion & Vermaelen 2004 (*JFE*, peer-reviewed): floating-priced
  convertibles are followed by negative abnormal returns (85% negative one-year returns in their
  sample *(secondary)*), driven by the discount. Chaplinsky 2010 (*RFS*) on PIPE returns and
  Augustin et al. (already in `literature.md`) are not read. Merger-target jumps are largely
  priced to the offer, so there is little left to hold for. This is the **short** side's
  evidence (`methods-short-risk.md`, amendment 04's toxic-convertible feature), not the long's.
- **Rule (for completeness).** Hold from the D-1 close or the open of D to D+1, D+3, D+5
  after a catalyst-class event; report by class, with merger-cash-deal gaps (target near the
  offer) shown separately. Expect negative for financing events and near zero for deals.

### What to test (train only, via an amendment)

Order of priority and what would kill each:

1. **ORB-5 on stocks in play, price $5-$20** (5.1). Kill if long-only Sharpe is below zero at
   the paper's commission and at 0.25x spread.
2. **Catalyst-class split of existing rides and the 5.2 open entries.** Kill if the hard-catalyst
   class has gross at or below zero.
3. **Halt descriptive table**, then the passive halt-resume entry only if the table shows
   positive mean return after the reopening (5.4).
4. **Day-2 flush table** (5.3) as a descriptive table first; a trade rule only if the mean
   day-2 return from -30% below the day-1 high is positive at 0.5x spread.
5. Skip 5.5 and 5.6 as long entries.

**Overall reading.** The evidence supports the study's finding rather than contradicting it: in
the best-documented population (Taiwan) about 15% of day traders are net-positive in a year, the
persistent winners number under 1% and short, concentrate and pay aggressive costs of 30-40 bps;
in Brazil 97% of those who persisted lost; and the one published net-positive intraday momentum
result (Zarattini) is a relative-volume selection effect with a Sharpe of 0.48 without
selection, long and short, on stocks above $5, with commission only. A long-only rule that catches
runners cannot earn more than the 2-4.5% round trip allows unless it picks the 16:1 false-ignition
problem apart, which no source offers.

---

## Sources

Read in full text this pass:
- Barber, Lee, Liu & Odean, "Day Trading Skill" (May 2011 working-paper version of "Do Day Traders
  Rationally Learn About Their Ability?"), working paper, Taiwan data 1992-2006:
  https://faculty.haas.berkeley.edu/odean/papers/day%20traders/Day%20Trading%20Skill%20110523.pdf
- Zarattini, Barbon & Aziz, "A Profitable Day Trading Strategy For The U.S. Equity Market", Swiss
  Finance Institute Research Paper 24-98, working paper:
  https://www.alexandria.unisg.ch/server/api/core/bitstreams/3c2989c4-688d-4d78-8a71-f02690990d51/content
  (record: https://ideas.repec.org/p/chf/rpseri/rp2498.html)

Read as abstracts, regulator pages or search summaries (marked in the text):
- Barber, Lee, Liu & Odean, later version "Learning Fast or Slow" (working paper):
  https://www.econ.puc-rio.br/uploads/adm/seminarios/Learning_fast_or_slow_180606.pdf
- Chague, De-Losso & Giovannetti, "Day Trading for a Living?", working paper, SSRN 3423101:
  https://papers.ssrn.com/sol3/papers.cfm?abstract_id=3423101 ;
  https://econpapers.repec.org/RePEc:spa:wpaper:2019wpecon47 ;
  summary: https://www.cxoadvisory.com/investing-expertise/day-trading-a-bust/
- Jordan & Diltz, "The Profitability of Day Traders", Financial Analysts Journal 59(6), 2003,
  peer-reviewed: https://ideas.repec.org/a/taf/ufajxx/v59y2003i6p85-94.html
- FTC, "Federal Trade Commission Cracks Down on Warrior Trading For Misleading Consumers With
  False Investment Promises", April 2022, regulator:
  https://www.ftc.gov/news-events/news/press-releases/2022/04/federal-trade-commission-cracks-down-warrior-trading-misleading-consumers-false-investment-promises
- FTC v. Warrior Trading complaint (regulator):
  https://www.ftc.gov/system/files/ftc_gov/pdf/2023198WarriorTradingComplaint_0.pdf
- FTC case page: https://www.ftc.gov/legal-library/browse/cases-proceedings/2023198-warrior-trading-inc-ftc-v
- FTC refund notice, January 2023 (regulator):
  https://www.ftc.gov/news-events/news/press-releases/2023/01/ftc-returns-more-29-million-consumers-harmed-warrior-trading
- Warrior Trading verified earnings pages (vendor; broker statements plus an accountant's report
  for the instructor's own account): https://www.warriortrading.com/?p=66741 ;
  https://www.warriortrading.com/?p=68672
- Gao, Han, Li & Zhou, "Market intraday momentum", Journal of Financial Economics 129(2), 2018,
  peer-reviewed: https://econpapers.repec.org/RePEc:eee:jfinec:v:129:y:2018:i:2:p:394-414 ;
  working-paper summary: https://alphaarchitect.com/2014/08/attention-prop-traders-the-first-half-hour-of-trading-predicts-the-last-half-hour/
- Zarattini & Aziz, "Can Day Trading Really Be Profitable?", SSRN 4416622, working paper:
  https://papers.ssrn.com/abstract=4416622
- ORB replication blog (vendor/anecdotal): https://www.mql5.com/en/blogs/post/776235 ;
  QuantConnect recreation (vendor/anecdotal): https://quantconnect.com/forum/discussion/18444
- Christie, Corwin & Harris, "Nasdaq trading halts: the impact of market mechanisms on prices,
  trading activity, and execution costs", Journal of Finance 57(3), 2002, peer-reviewed:
  https://ideas.repec.org/a/bla/jfinan/v57y2002i3p1443-1478.html
- Price-limit hits in Chinese markets (arXiv, working paper; daily limits, not LULD):
  https://arxiv.org/pdf/1503.03548
- Barber, Huang, Jorion, Odean & Schwarz, "A (Sub)penny for Your Thoughts", Journal of Finance
  79(4), 2024, peer-reviewed: https://econpapers.repec.org/RePEc:bla:jfinan:v:79:y:2024:i:4:p:2403-2427 ;
  https://papers.ssrn.com/abstract=4202874
- Wholesaler adverse selection and routing (working paper, not precisely identified):
  https://foster.uw.edu/wp-content/uploads/2024/07/Huang_Routing-July-2024.pdf
- Hillion & Vermaelen, "Death spiral convertibles", Journal of Financial Economics 71(2), 2004,
  peer-reviewed: https://ideas.repec.org/a/eee/jfinec/v71y2004i2p381-415.html
- Chan, "Stock price reaction to news and no-news: drift and reversal after headlines",
  Journal of Financial Economics 70(2), 2003, peer-reviewed:
  https://academicnewsletter.sufe.edu.cn/info/357702
- Savor, "Stock returns after major price shocks: the impact of information", Journal of
  Financial Economics 106(3), 2012, peer-reviewed: https://repository.upenn.edu/fnce_papers/387
- FINRA Rule 4210 pattern-day-trader replacement (regulator; read via secondary coverage):
  https://www.thinkadvisor.com/amp/2026/04/14/sec-approves-plan-removing-day-trading-limit-for-small-investors/ ;
  https://public-inspection.federalregister.gov/2026-07485.pdf

Not read, named from `literature.md` or other sources: Barber, Lee, Liu & Odean (2009) RFS;
Harris & Schultz (1998); Garvey & Murphy (2005a/b); Corwin & Lipson (2000); Chaplinsky (2010) RFS;
Madura, Richie & Tucker (2006). Check them before citing figures.
