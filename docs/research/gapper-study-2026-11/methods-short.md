# Gapper study: methods for the fade-short, borrow, squeeze and Rule 201 (IP-T477)

A web pass on whether the costed fade-short (short about 09:46 after a +50% premarket gapper stalls
near its open, cover by the close or on a stop) could be executed and could survive its real costs.
It complements `methods-execution.md` §5 (SSR and borrow, brief) and §3 (LULD), `methods-exit.md`,
`strategy-refined.md` and `literature.md` §2 (Barber et al.). Those are referenced, not repeated.

The setup being tested: the long side loses about 3-6% per trade after 3.5-4.5% round-trip costs;
descriptively, +50% gappers that stall near the 09:30 open by 09:45 close below the open about 70% of
the time, with a median close about 31% under the premarket high. The backtest grids borrow at
0/50/200% APR, applies Rule 201 uptick entry and uses Alpaca's sub-$5 margin of max($2.50/share, 100%).

Labels: **peer-reviewed**, **working paper**, **regulator/exchange**, **vendor/anecdotal**.
*(abstract/snippet)* means I read only an abstract, a search summary or a press release, not the paper.
*(secondary)* means a number was reported by a third party. *(not verified)* means I could not read it
and it must not be cited in results. Today is 2026-11-09. No Alpaca, SEC or idiotproof API was called;
all of this is web search and fetch only. Every parameter below is a candidate for a registered
amendment, on train first. None is a finding.

**Reading note.** The search tool summarises pages with a small model, so figures that came only from
a summary are marked *(snippet)* even when the source is a good one. Re-read the source before any
number goes into a results report.

---

## 1. Borrow availability and fees for small, hard-to-borrow stocks

### 1.1 What the literature measures, and why it understates this universe

- **Engelberg, Reed & Ringgenberg (2018, *Journal of Finance* 73(2), 755-786), "Short-selling risk",
  peer-reviewed** *(abstract/snippet)*. Markit lending data. Fees are highly skewed: in the working
  paper, **median fee 11 bps a year, mean 85 bps, 99th percentile 1,479 bps** *(secondary, working-paper
  figures; not confirmed against the published version)*. Two risks matter to a short seller: **fee
  variance** and **recall risk** (variance of lending supply). Stocks with more of either have lower
  returns and less short selling; a long-short sort earns about 9.7% (fee risk) and 8.6% (recall risk)
  a year in the working paper *(snippet)*. The authors say recalls and fee jumps are not independent:
  both reflect the loan market leaving equilibrium *(snippet)*.
- **Drechsler & Drechsler (2014), "The shorting premium and asset pricing anomalies", NBER w20282,
  working paper** (abstract read; fee table not read). Cheap-minus-expensive-to-short earns 1.43% a
  month gross, 0.91% net of fees, 1.53% four-factor alpha; about 80% of stocks are cheap to short and
  anomalies vanish there. A summary of the paper (cxoadvisory, *secondary*) gives average annual fees,
  Jan 2004-Oct 2012: the eight cheapest deciles about 0.32%, the ninth about 0.78%, the tenth about
  5.8%, the top 5% of stocks about 9.1%. A different summary says about a quarter of "on special"
  stocks move to cheaper buckets each month *(secondary)*. Average gross next-month return in the
  tenth (most expensive) decile is negative (-0.71%) *(snippet)*: expensive-to-short stocks do
  underperform, but that is what the fee pays for.
- **D'Avolio (2002, *J. Financial Economics* 66, 271-306), "The market for borrowing stock",
  peer-reviewed** *(snippet, secondary)*. About 9% of stocks are "specials" at any time; the mean
  special fee is about 4.3% a year; **fewer than 1% become extremely special** (fee above the
  risk-free rate, so a negative rebate). Specials are more likely in low-institutional-ownership,
  small, high-disagreement stocks.
- **Muravyev, Pearson & Pollet (2025, *J. Financial Economics* 172), "Why does options market
  information predict stock returns?", peer-reviewed** *(abstract and press release)*. Option-implied
  volatility spreads embed borrow fees; the press release says only about 10% of stocks carry fees of
  2% or more, and that shorting on these signals often incurs fees high enough to push net
  performance near zero. Their 2022 *J. Finance* paper (borrow-fee risk premium, snippet) finds the
  fee-risk premium is small.
- **Kolasinski, Reed & Ringgenberg (2013, *J. Finance* 68(2), 559-595)**, multiple-lender view of
  supply and search in equity lending, peer-reviewed. Found by title only; findings *(not verified)*.

**The sampling problem.** These data sets are built from large lending agents' books. A stock that
rises 60% premarket on thin float is often not lendable at all (no row), so the fee distribution
above is conditional on being lendable. The tail that matters here, "no shares, no price", is
invisible in the medians and means. I found **no study of borrow fees or availability on the day of a
gap or promotion** in a peer-reviewed source. Treat 1.1 as the shape of the tail, not a fee for these
names.

### 1.2 Fee spikes and recalls: anecdote, not data

- Interactive Brokers education piece (vendor): a short opened at a 1% fee can be charged 100% once a
  squeeze starts; the SharpLink (SBET) example, May 2025, price from a $3 handle to $124, 100% fee
  *(snippet)*. Stated as a hypothetical risk, not a measured distribution.
- Benzinga/S3 Partners rankings (vendor/press, *snippet*): 2020 Nikola borrow fee above 600% a year
  (a Bloomberg report quoting S3); 2023 AMC 168% (highest among stocks with at least $100 million
  short interest). These are large caps with short-interest data. S3-type lists exclude exactly the
  sub-$20 micro-floats in this universe.
- A risk-desk note (tapeboard, vendor/anecdotal, *snippet*): sudden fee jumps are an early warning of
  recalls and buy-ins. No source gave a recall frequency for micro-caps.
- **Brunnermeier & Pedersen (2005, *J. Finance*), "Predatory trading", peer-reviewed** *(abstract)*:
  a short seller may have to cover when the price rises a lot and the share is recalled; the paper
  names this a short squeeze. Evidence is theoretical plus cases.

### 1.3 Fee level versus the cost that actually binds

At Alpaca's formula (daily fee = market value x rate / 360; see §5) a position held for **one day**
pays rate/360:

| APR | cost per day held |
|---|---|
| 50% | 0.14% |
| 200% | 0.56% |
| 500% | 1.39% |
| 1,000% | 2.78% (my arithmetic) |

So for an **intraday** short the 0/50/200% APR grid moves the result by under 0.6 point, small against
the 3.5-4.5% round-trip cost and the 3-6% long-side loss. What can kill the trade is not the APR on
paper; it is **(a) no locate at all, (b) a locate fee quoted per share (non-refundable; §5), and (c)
a recall or squeeze during the day.** Whether Alpaca charges the daily fee on a position closed the
same day is not stated (§5). The 0/50/200 grid is the right shape but the wrong axis: add
availability (locate success rate) and a per-share locate cost.

### 1.4 How borrow changes on gap and pump days

No source measures it. Mechanism only: demand to short rises, lendable supply is cut by recalls and
by retail holdings moving to non-lending accounts (an opinion piece, curvedtrading, vendor,
*snippet*). In the two cases where fee data exist (Nikola, SBET) the fee went from near zero to 100% or
more within days of the price spike. Expect the largest +50% gappers to be the least borrowable and
the most expensive, which is the same population with the largest measured fade. The backtest's
sample of tradable shorts is therefore **selected toward milder gappers** if availability is modelled
at all, and overstated if it is not.

### What to test (train only, via an amendment)

- **Availability gate (no borrow data exists in the repo, so proxy it).** Flag a name "likely
  unborrowable" if any of: float below 10 million shares (or unknown), listing or IPO under 180 days,
  priced under $1, premarket high at least +150% over the prior close, or a halt already in the
  morning. Report the fade result with these names (a) included and (b) excluded; the gap between the
  two is the cost of the unavailability assumption. Also run a random-skip stress: **30% and 60% of
  signals fail to get a locate**, skipped at random and, separately, skipped on the highest-gap
  decile. (The percentages are assumptions; no source gives a locate success rate.)
- **Borrow cost to assume.** Per-trade: locate fee of **1.0% of notional base, 2.5% stress** (my
  assumption; Alpaca's one published sample quote is $0.0123 a share, price unknown) plus daily borrow
  of **200% APR / 360 = 0.56%** on the HTB side, charged once. Keep the APR grid as is for continuity
  and add 500%.
- Exclude names whose entry price times 100 shares exceeds the position cap in §2 (the locate lot is
  100 shares).

---

## 2. Short squeeze and tail risk on low-float gappers

### 2.1 Distribution

- A short has a bounded gain (-100% of the price) and an unbounded loss. A stock at +50% premarket
  that continues can double again. The study's descriptive "70% close below the open" says nothing
  about the other 30%, and a mean return of a fade-short is set by that tail.
- **Manipulation episodes (regulator/press, *snippet*):** the SEC suspended trading in 12 Asian
  micro-cap Nasdaq names from late September 2025 over possible social-media manipulation (a law-firm
  summary counts 14 suspensions through early 2026, all foreign issuers). QMMM ran past $300 in
  weeks after announcing a crypto strategy; Smart Digital Group rose from a $4.00 IPO price to about
  $14.79 before its halt. A Bloomberg graphic (press, snippet, not verified) says about a quarter of the
  250-plus companies listing on Nasdaq's smallest tier since 2023 were promoted in WhatsApp groups and
  then crashed or were suspended. Common profile: boutique underwriter, tiny public float, Nasdaq
  Capital Market or NYSE American listing. I found no systematic post-suspension return study.
- **Aggarwal & Wu (2006, *J. Business* 79(4), 1915-1954), "Stock market manipulations",
  peer-reviewed** *(abstract)*: prices rise through the manipulation and fall after it; prices and
  liquidity are higher when the manipulator sells than when he buys. Fits "fade", but the paper is
  about the manipulator's profit, not a retail short's.

### 2.2 Halts and gap-through

- A stock that reaches the upper LULD band for 15 seconds is paused for 5 minutes, extended in
  5-minute steps with collars (`methods-execution.md` §3.2). A short held inside an **up-pause**
  cannot cover until the reopening auction, at a price set by the imbalance, which on a squeeze is
  above the pre-halt price. LULD is not active in premarket; it is from 09:30, and the doubled
  09:30-09:45 bands are over by the 09:46 entry. After 09:45 the Tier 2 bands are 10% (5% for
  Tier 1; *rulebook-level, not re-read this pass*).
- **Regulatory halts (SEC suspension, news pending) are not LULD** and last 10 trading days for an SEC
  suspension (rulebook, *not read this pass*). A short in a suspended stock cannot be covered
  (nor can a long be sold). The exit then depends on resumption, usually on an OTC quote.
- **Alpaca stop behaviour (vendor, read this pass):** "Once the order is elected, the stop order
  becomes a market order", with no price guarantee; the execution price "may be less favorable than
  the stop price" in fast markets. A buy stop for a short elects on a trade at or above the stop price
  on the tape. A stop-limit that gaps past its limit stays working as a limit order, so the cover
  never happens. Trailing stops do not trigger outside regular hours. Premarket there are no stops at
  all (`methods-execution.md` §1.1). A short taken at 09:46 and stopped in RTH is covered with a
  market order into a thin, fast book.
- No peer-reviewed study of stop-loss slippage or reopening prices for sub-$20 gappers was found.
  The SEC DERA white paper on Amendment 10 (regulator, *snippet*) says pause frequency fell over 75%
  with the change and the fall was largest in Tier 2; it is about frequency, not squeezes.

### 2.3 Sizing against it

- With no bound on loss, the position is sized to the **worst plausible move**, not to a stop. If
  the cover can gap +100% through a stop (halt up, then a reopening far higher), the loss is 100% of
  notional. With a 1% equity risk budget that implies a position of at most **1% of equity**, whatever
  the stop. (Arithmetic, my assumption; a smaller bound needs evidence on the gap-through
  distribution.)
- Kelly-style sizing (`methods-execution.md` §4.2) fails here for the same reason as long-side tails:
  the empirical `log(1 + f R)` is undefined for R at or below -1 for f=1, and f above 1/|worst loss|
  gives ruin. Use the train distribution of the worst trade to cap f, then shrink.
- Participation cap from `methods-execution.md` §4.1 (5% of five-minute volume) applies to the cover as
  well; a squeeze is a day with the most volume and the most competition to cover.

### What to test (train only, via an amendment)

- **Max adverse excursion (MAE).** From the fill at 09:46 to the close, report the share of shorts
  whose high exceeds entry by +10%, +25%, +50%, +100%, and the p95/p99 of the MAE.
- **Stop slippage model.** Stop = entry + max(8%, 1.5 x the 09:30-09:45 range); fill the cover at the
  **higher** of the stop and the next 1-minute bar's open; if the 1-minute high exceeds the stop by
  more than 15% in one bar, fill at the high (a stand-in for a gap-through). Report with the stop
  alone and with a hard cap: **exit all by 15:45** and a no-stop alternative sized at 1% of equity.
- **Halt flag.** Count shorts that see an up-pause (or a zero-volume minute bracketed by a +5% gap)
  after entry. Treat them at the reopening bar's high as the worst case.
- **Size cap.** Position = min(1% of equity / 1.0, 5% of the prior five minutes' dollar volume,
  $1,000-$2,500 for the backtest) and at least the 100-share locate lot; skip a signal if 100 shares
  exceed the cap.
- **Names to exclude.** Float below 10 million (or unknown), IPO under 180 days, Nasdaq Capital Market
  or NYSE American plus tiny float plus gap above 150% (the SEC-suspension profile), any premarket
  halt, price above $20 or below $1, news-driven positive catalyst (FDA, contract) where
  fundamentals can justify continuation.

---

## 3. Evidence on shorting attention-driven, pumped or promoted small caps

### 3.1 What survives, and what the evidence leaves open

- **Barber, Huang, Odean & Schwarz (2022, *J. Finance* 77(6), 3141-3190), peer-reviewed** *(abstract,
  snippet)*. Heavy Robinhood buying predicts poor returns: a version summarised here reports 20-day
  abnormal return -4.7% for the top-bought stocks and -19.6% on extreme herding events; `literature.md`
  §2 cites a 5-day -3% / -6% version and "losses traced to intraday" and "only for sub-$1B caps". The
  versions differ (working-paper revisions); check which one is quoted. None of the search results
  mention short interest, borrow fees or whether a short seller could capture it. **That is the
  missing link**: the paper shows the stock falls, not that a short can earn it net of borrow.
- **Leuz, Meyer, Muhn, Soltes & Hackethal (*Management Science*, forthcoming/2022 revision; NBER
  w24083), peer-reviewed/working paper** *(snippet)*. German bank clients; about a 2% annual chance
  that an investor takes a position in at least one tout campaign; portfolio composition and past
  trading explain participation; schemes cause considerable losses. Soltes via HBS Working Knowledge
  (press, *snippet*): 470 pump-and-dump schemes, participants lose one third on average. These measure
  the buyer's losses, which are the short's gross profit before borrow, and say nothing of the cost
  of getting short.
- **Frieder & Zittrain (2007), "Spam works"** and **Hanke & Hauser (2008)** on stock spam:
  *not read this pass; not verified*. A touting campaign raises volume (snippet from a search summary).
- **Berkman, Koch, Tuttle & Zhang (2012, *JFQA*), peer-reviewed**, cited in `literature.md` §1: the
  open is systematically the worst price of the day in attention stocks; more severe in
  hard-to-arbitrage names. The reading in this pass is secondary (a trading-education site's summary,
  consistent with `literature.md`). Hard-to-arbitrage names are also the hard-to-borrow ones, which is
  the economic reason the fade persists.
- **Drechsler & Drechsler (above):** the anomaly returns are concentrated in the high-fee decile and
  are compensation for short-selling risk. That is the general result, and a +50% gapper short is the
  extreme case: **where the gross fade is largest, the fee and the risk are largest.**
- **Jahan-Parvar / Corwin-Schultz concerns** in `methods-execution.md` §1.4 apply to the cost side
  here as well.

I found **no study that backtests shorting pumped or promoted micro-caps with realistic borrow, locate
and squeeze losses**, and none that reports short-seller profits in promotion events. The closest
quantitative statement is the Drechsler net-of-fee return in the expensive decile, at monthly horizons
and for names that are lendable.

### 3.2 SEC and regulatory work

The SEC's 2025-26 suspensions and the Nasdaq proposals (IM-5101-4 delisting authority; a rule to deny
IPOs with manipulation signs; law-firm summaries, *snippet*) show the regulator treating this
population as manipulation-prone. For a trader that means: more halts, more SEC suspensions
(position stuck), and a rising chance that the lender recalls or that the broker removes the name from
the shortable list overnight.

### What to test (train only, via an amendment)

- Fade stratified by **gap size** (50-100%, 100-200%, over 200%), **catalyst type** (the study's tags:
  none/promotion-like versus fundamental) and **float**, with the 70%-below-open rate, the mean net
  return after 1.0% / 2.5% locate and 0.56% borrow, and the p99 MAE per cell. Expect the cells with the
  largest fade to be the cells flagged unborrowable in §1; report them but do not trade them.
- **Repeat-gapper split.** A name that was a top mover yesterday (`priorDayTopMoverRank`) had its
  herding probability raised 6.7 points in Barber et al., and a prior-day SSR carries into today's
  premarket (§4). Check whether the fade is larger or smaller on day 2.
- Report **gross, after 3.5-4.5% costs, after borrow** in three columns so the reader sees where the
  edge goes.

---

## 4. Rule 201 (SSR)

### 4.1 The rule as written (regulator, read this pass)

From the SEC staff FAQ on Rule 201 (Q numbers as printed):

- **Trigger:** a 10% fall in a trade reported in the consolidated system from the **prior regular
  session close**; determined only during regular hours, 09:30-16:00 (Q1.1, Q1.2); the listing market
  determines it (Q1.3).
- **Duration:** the rest of that day and the **entire next trading day** (Q2.1); it can re-trigger
  any number of times (Q2.2).
- **Pre- and post-market:** once triggered, the restriction applies wherever NMS quotation data is
  disseminated, "which may extend beyond regular trading hours" (Q2.1).
- **Effect:** a short sale of a restricted stock may execute only **above the current national best
  bid**, including un-displayed orders (Q4.1). If the NBBO is crossed, a short may execute at or below
  the bid (Q6.1).
- **Short-exempt marking** (Rule 201(c), (d)) is for specific exceptions and is not available to
  orders that qualify only for a Rule 203 locate exception (Q5.1); not a retail tool.
- **Empirical effect (peer-reviewed, *snippet*):** Barardehi, Bird, Karolyi & Ruchti (*Management
  Science* 71(5), 2025), using the -10% trigger as a quasi-experiment: stocks just past the trigger
  show about **8% lower short-sale volume and 35 bps higher daily returns** (2019 SSRN version; price
  effects do not reverse). An EFMA 2011 conference paper (working paper, *snippet*) finds Rule 201 did
  not reduce short volume after controls. Mixed, but both say it is a minor friction for liquid stocks.
  **No study of fills, queue position or execution quality under SSR was found**.

### 4.2 How it interacts with this trade

1. **A +50% gapper is far above the trigger.** A name opens at +50%, stalls, and closes 31% under a
   premarket high that may sit at +60-100% over the prior close: say the premarket high is +70%
   (1.70) and the close is 31% under it (1.17). That is +17% over the prior close, not -10%. So for the
   **modal** fade SSR does not trigger today, and does not bind the 09:46 entry.
2. **It binds in three other cases:** (a) the name closed down 10% or more yesterday (a serial gapper
   that failed), so SSR is already on all day today, premarket included; (b) a deep fade, where the stock
   falls to 0.9 x the prior close intraday; SSR then switches on mid-day for any re-entry or add, but
   not for a short already open; (c) a stock that gapped up only a little (+10% to +20%) and fades.
   Case (a) is common in this population: day 2 of a pump. It is the case most associated with
   larger declines, so it matters.
3. **Interaction with "stalling near the open".** The stall is defined at 09:45 off the open; the
   10% trigger is defined off yesterday's close. For a +50% gap the stock must lose about 40% of its
   value after the gap before SSR starts. By then most of the fade profit is gone. SSR is therefore
   mostly an **entry** filter on day-2 names, and not a tail risk for day-1 shorts.
4. **Fills.** Under SSR a short must be priced above the national best bid. A marketable short cannot
   hit the bid; it must rest at the ask or above and wait for a buyer. A bar-based fill that assumes
   "sell at the next bar's open" overstates fills in two ways: it fills on a falling book that a real
   resting order would miss (the best shorts are those where the bid moves away), and it fills at a
   price a restricted order could not get. The correct simulation is "limit at ask (or ask + one
   tick); fills only if a later trade prints at or above it", as `methods-execution.md` §5 proposed.
   Resting at the ask is a *better price* than hitting the bid but no assured fill: adverse selection
   works in the shorter's favour on price and against it on selection.
5. Alpaca's docs on SSR: **not found.** The orders page I read says nothing of the uptick rule. A
   2023 forum thread (vendor/anecdotal) says Alpaca support updated its documentation after a user
   challenged the trigger's description; the new wording was not visible. How Alpaca marks and
   routes a restricted short, and whether it rejects at-the-bid shorts itself or leaves it to the
   wholesaler, is **not verified**.

### What to test (train only, via an amendment)

- Share of fade signals on names with **prior-day SSR** (prior low at or below 0.9 x the day-before
  close), and the fade result with and without them. Day-2 SSR names can be run as a labelled cell.
- Share of signals that **trigger SSR before** 09:46 (only possible if the open itself is below 0.9 x
  the prior close; rare for gappers) and after (a diagnostic for case 2b).
- Short fill rule under SSR: limit at ask (or one tick above); fills only if a later one-minute bar's
  high reaches it within 60 seconds, else skip. Report the trade count and mean return change against
  the next-open fill. If the skipped trades are the largest fades, the edge is smaller than the
  fill-at-open backtest says.

---

## 5. Alpaca specifics

All of this is **vendor** text, read this pass unless marked. Dates matter: Alpaca's HTB product is
new and the docs are inconsistent.

### 5.1 ETB versus HTB

- **Launch, 24 Jun 2026 (Alpaca blog, "Alpaca Launches Hard-to-Borrow Short Selling...")**: Alpaca now
  supports hard-to-borrow shorting through the Trading API (also its MCP server and CLI). The
  `borrow_status` field on `GET /v2/assets/{symbol}` is `easy_to_borrow` or `hard_to_borrow`.
  ETB names have no locate and **$0 borrow fees** for Trading API users (5,000+ ETB securities).
- **HTB flow:** `GET /v1/locates/quotes` (advisory, does not reserve shares), `POST /v1/locates`
  (`symbol`, `qty`, optional `limit_price` as a maximum fee per share, optional `all_or_none`), then
  track by `GET /v1/locates`. A recorded locate is `active` or `rejected` with a reason. Availability
  is **account-specific** and "can change quickly". Requests are in **round lots of 100 shares**.
  Locates are **single-use**: covering does not restore the locate and a new locate is needed to
  short again ("Currently, locates cannot be reused"). **Locate fees are not refunded if unused.**
  Locates are available 20:00 Sunday to 20:00 Friday ET, covering pre- and post-market.
- **HTB borrow fee (margin page):** daily fee = sum of (HTB short market value x that stock's HTB
  rate) / 360, **charged on any day you hold an HTB short**, with 3 days charged over a Friday
  settlement, and on the nearest round lot. **The rate is not stated.** Whether a position opened and
  closed inside one day pays it is not said.
- **Stale conflicting text:** the support page "short selling fees" (read this pass) still says Alpaca
  "currently only supports opening short positions in easy-to-borrow ('ETB') securities" and that HTB
  is coming "in the coming months". That predates the June 2026 launch; use the blog and docs, and
  confirm with Alpaca. It also says that if a stock moves **from ETB to HTB overnight, any open short
  order in that stock is cancelled before market open**.
- **How ETB is decided (staff forum replies, 12 May and 16 Sep 2024):** an asset is shortable if on the
  ETB list; Alpaca "relies on ETB lists" from third-party providers, and the list "changes daily (and
  potentially intra-day)". Another staff reply (as relayed in search results, *snippet*) says Alpaca
  does not close an existing short that turns non-ETB, so monitor `easy_to_borrow` yourself. Staff
  did not publish the criteria.
- **Inference (mine, not Alpaca's):** a +50% premarket sub-$20 micro-cap with a thin float is far more
  likely to be HTB or not shortable than ETB. So this strategy lives in the HTB path, which exists
  only since June 2026, has account-specific inventory, and per-name quotes that can be rejected.
  The paper account does not simulate locates (third-party claim, *not verified*), so a live
  test is needed. Alpaca's one example fee of $0.0123 a share is a sample, not a typical.

### 5.2 Margin and buying power

- Shorting needs at least **$2,000 equity** (margin page).
- **Overnight maintenance on shorts:** price under $5.00, the greater of **$2.50 a share or 100%** of
  market value; at $5.00 or more, the greater of $5.00 a share or 30%. Initial margin 50% (Reg T). So a
  $1 stock needs 250% of its value held against it overnight and a $2 stock 125%. Whether these apply
  intraday (the page says "overnight"; general buying power is up to 4x intraday) is **uncertain**; for
  a day trade covered by 16:00 the binding number may be the initial 50% or the order-entry check.
- **Order-entry check (orders page):** a limit short is valued at the greater of the limit price or
  3% above the ask; a market short at 3% above the ask; extended-hours orders use the midpoint, and
  pre-market the latest trade. Open short orders reduce buying power and covers do not free it until
  they execute.
- **Margin calls (margin page):** issued the morning after an end-of-day shortfall; Alpaca may
  liquidate without notice if equity is at risk of going negative. A squeeze can therefore close the
  position at Alpaca's choice of price, which is the worst version of a stop.

### 5.3 Other limits

- Extended-hours shorts are limit orders with `day` or `gtc` only (`methods-execution.md` §1.1);
  the 09:46 entry is regular session, so market and stop orders work then.
- Alpaca's docs do **not** mention locates under HTB in the orders page, the uptick rule, or halt
  handling for shorts ("not covered").
- PDT and day-trading margin rules for an account under $25,000: **not researched this pass**; check
  before any live run.

### What to test (train only, via an amendment)

- Fix the assumption "shortable on Alpaca" as an explicit gate. For a live pilot (not a backtest): log
  `borrow_status`, `shortable` and the locate quote for each signal name at 09:40 and at 09:46; report
  the share that are ETB, HTB with an active locate, HTB rejected, and not shortable.
- Backtest cost: locate fee per share as a percentage of price (1.0% base, 2.5% stress), applied once,
  and an assumed **100-share minimum**, which makes the smallest position $100 at $1 and $2,000 at
  $20. Skip any signal whose 100-share cost exceeds the size cap.
- Capital check: margin held = max($2.50, 100% x price) x shares for names under $5. Report the number
  of concurrent shorts a $25,000 account could carry on a bad morning.

---

## 6. Opening range, VWAP and failed-gap evidence; the time-of-day profile

### 6.1 What is documented

- **Zarattini, Barbon & Aziz (2024), "A profitable day trading strategy for the U.S. equity market",
  Swiss Finance Institute RP 24-98, working paper** *(snippet)*. Five-minute opening range breakout
  restricted to "stocks in play" (unusual volume, usually news); the top 20 reportedly earned over
  1,600% net with Sharpe 2.81 and alpha 36% over 2016-2023. A 2026 blog replication on five index CFDs
  found gross reproduced and net near zero (vendor/anecdotal, *snippet*). Direction there is
  **breakout continuation in stocks in play**, which is the opposite lean to a fade. The universe is
  7,000 US stocks with price and liquidity filters, probably excluding most of this study's names;
  that was not checked.
- **Heston, Korajczyk & Sadka (2010, *J. Finance* 65(4), 1369-1407), peer-reviewed** *(abstract)*:
  a stock's return in a given half hour predicts the same half hour at daily lags, strongest at
  the first and last half hours. The paper excludes the overnight gap. A 2025 follow-up (Haendler et
  al., SSRN, *snippet*) links open periodicity to VWAP trading. Not about gaps.
- **Gao, Han, Li & Zhou (2018, *J. Financial Economics*), "Market intraday momentum"**: first
  half-hour return predicts last half-hour return in index products *(secondary; not read; not verified)*.
- **Berkman et al. (2012)** (above) is the one study of the open as a retail-demand price in attention
  stocks; reversal is bigger in hard-to-value names.
- **Yuan (2015)**, intraday reversals after large opening moves in index futures *(secondary, snippet
  of a gap paper)*; **Plastun et al. (2020)** found continuation on index gap days and reject "gaps
  always fill" (cited in `strategy-refined.md` §1.1). Index-level evidence points both ways.
- **Failed-gap and VWAP-fade rules** ("failed gap and go", "short below VWAP/opening range low") are
  practitioner lore (daytradingtoolkit and similar; vendor/anecdotal). I found **no peer-reviewed
  intraday test of opening-range, VWAP or failed-gap fades in sub-$20 premarket gappers**, and no
  published time-of-day profile of their decline.

### 6.2 Time-of-day: what can and cannot be said

- The study's own number, about 70% closing below the open after stalling at 09:45, is a
  close-to-open comparison and says nothing about **when** the decline happens.
- General intraday volume and volatility are highest in the first and last half hours; most of the
  spread cost and most of the stop slippage are therefore in the 09:46-10:15 window where the short
  is entered. That is an inference from the Heston et al. pattern, not a measurement for gappers.
- The conditioning matters. A "stall by 09:45" filter removes names that already fell in the first
  15 minutes, so the decline captured by a 09:46 entry is the **later** decline. If most of the
  total fade happens 09:30-09:45 (selected out), the post-09:46 gain is smaller than the descriptive
  stat suggests; if it is slow, the 09:46 entry captures it. The data can answer this; the
  literature cannot.

### What to test (train only, via an amendment)

- **Decline profile.** For the stalled +50% cohort, mean and median return from the 09:46 entry to
  fixed marks: 10:00, 10:15, 10:30, 11:00, 12:00, 14:00, 15:45, close, and the share of the
  *total* 09:46-to-close move completed by each. Also the same for the **unconditional** +50% gappers,
  to show the selection effect.
- **Entry timing grid.** 09:46, 09:50, 10:00 and "first close below the opening-range low (09:30-09:45
  low)" or "first close below VWAP after 09:45"; compare fill risk and MAE.
- **Hold grid** (cover at 10:30, 11:30, 13:00, 15:45) with the §2 stop. If most of the gain is in the
  first hour the exit can be early and the overnight and squeeze exposure falls.
- **Continuation control.** The ORB-continuation side: the share of stalled names that break the
  opening-range high after 09:45 and close above the open, and the loss on those shorts.

---

## Verdict on feasibility

**Mechanically possible, economically unproven, and the backtest as designed is probably too
optimistic on the population.**

1. **Alpaca can now short HTB names (since 24 Jun 2026)**, but only through a per-100-share,
   single-use, non-refundable-fee locate with account-specific inventory that can reject. Sub-$20
   +50% micro-cap gappers with thin float are the population least likely to have inventory. A
   backtest that fills all signals assumes a borrow market that, for this population, is the least
   documented part of the evidence (§1.1, §5.1).
2. **The APR grid is the wrong test.** At 200% APR an intraday hold costs 0.56%. What costs money is
   not getting a locate, the locate fee, the 100-share minimum, and the squeeze (§1.3, §2).
3. **SSR is mostly a day-2 problem**, not a tail risk on day-1 fades, but it makes realistic fills
   worse exactly on the names with the biggest fade (§4.2).
4. **The tail is the strategy.** The fade has a bounded gain and an unbounded loss, and the population
   includes SEC-suspension and pump-and-dump profiles where a short can be stuck in a halt or squeezed.
   Retail stops become market orders and gap through; Alpaca can liquidate on its own terms (§2.2, §5.2).
   A 1%-of-equity position cap, or smaller, is what survives; at that size the dollar edge on a retail
   account is small.
5. **I found no study showing that shorting pumped micro-caps nets positive after borrow.** The
   papers show the stock falls (Barber et al.; Aggarwal & Wu) and that borrow fees take the premium in
   the expensive decile (Drechsler & Drechsler). The study can produce that evidence, on train, and
   should say what it cannot see: borrow data.

A retail account on Alpaca can place this trade on the subset of names that have an ETB flag or an
available locate, with sub-$5 margin of 125-250% of notional and positions sized to tolerate a total
loss. It cannot reliably short the largest, most promoted gappers, which are where the descriptive
fade is greatest. A live paper-style pilot that only logs `borrow_status` and locate quotes at 09:40
(no orders) is the cheapest decisive test of the availability question.

---

## Sources

Read this pass (page text, not just a snippet):
- Alpaca, "Alpaca Launches Hard-to-Borrow Short Selling Featuring Full API Support for Locates"
  (24 Jun 2026), https://alpaca.markets/blog/htb-trading-api-locates/ (vendor).
- Alpaca, "Short selling fees", https://alpaca.markets/support/short-selling-fees (vendor; text predates
  the HTB launch).
- Alpaca, "Margin and Short Selling", https://docs.alpaca.markets/docs/margin-and-short-selling (vendor).
- Alpaca, "Orders at Alpaca" (short-selling and stop sections), https://docs.alpaca.markets/docs/orders-at-alpaca (vendor).
- Alpaca forum, "Criteria for shortable" (staff replies May and Sep 2024),
  https://forum.alpaca.markets/t/criteria-for-shortable/14187 (vendor/anecdotal).
- Alpaca forum, "Alpaca has an incorrect interpretation of the uptick rule" (May 2023),
  https://forum.alpaca.markets/t/alpaca-has-an-incorrect-interpretation-of-the-uptick-rule/12396 (vendor/anecdotal; no staff reply visible).
- SEC Division of Trading and Markets, "Responses to FAQ concerning Rule 201 of Regulation SHO",
  https://www.sec.gov/divisions/marketreg/rule201faq.htm (regulator; staff views).
- Drechsler & Drechsler, "The shorting premium and asset pricing anomalies", NBER w20282 (2014),
  https://www.nber.org/papers/w20282 (working paper; abstract only).

Abstract, search summary or press release only (do not cite figures without re-reading):
- J. Engelberg, A. Reed, M. Ringgenberg, "Short-selling risk", *Journal of Finance* 73(2), 755-786, 2018,
  https://econpapers.repec.org/RePEc:bla:jfinan:v:73:y:2018:i:2:p:755-786 (peer-reviewed; fee statistics
  are working-paper figures via secondary summary).
- Cxoadvisory summary of Drechsler & Drechsler, https://www.cxoadvisory.com/short-selling/shorting-fee-as-a-stock-return-predictor/ (secondary; decile fees).
- S. D'Avolio, "The market for borrowing stock", *J. Financial Economics* 66, 271-306, 2002,
  https://www.johnhcochrane.com/s/davolio_borrowing_stock_JF.pdf (peer-reviewed; specials share from secondary sources).
- D. Muravyev, N. Pearson, J. Pollet, "Why does options market information predict stock returns?",
  *J. Financial Economics* 172, 2025, https://ideas.repec.org/a/eee/jfinec/v172y2025ics0304405x25001618.html;
  press release https://giesbusiness.illinois.edu/news/2026/01/07/study--borrowing-fee-is-the-variable-that-debunks-theory-that-options-predict-future-stock-returns;
  earlier paper https://econpapers.repec.org/RePEc:bla:jfinan:v:77:y:2022:i:3:p:1787-1828.
- A. Kolasinski, A. Reed, M. Ringgenberg, "A multiple lender approach to understanding supply and
  search in the equity lending market", *J. Finance* 68(2), 559-595, 2013,
  https://econpapers.repec.org/RePEc:bla:jfinan:v:68:y:2013:i:2:p:559-595 (findings not verified).
- M. Brunnermeier, L. Pedersen, "Predatory trading", *J. Finance* 60, 2005,
  https://www.nber.org/papers/w10755.pdf (peer-reviewed; abstract).
- R. Aggarwal, G. Wu, "Stock market manipulations", *J. Business* 79(4), 1915-1954, 2006,
  https://www.bauer.uh.edu/wu/Papers/JOB2006_StockManipu.pdf (peer-reviewed; abstract).
- C. Leuz, S. Meyer, M. Muhn, E. Soltes, A. Hackethal, "Who falls prey to the Wolf of Wall Street?",
  NBER w24083, https://www.nber.org/papers/w24083 (working paper / *Management Science*, forthcoming).
- B. Barber, X. Huang, T. Odean, C. Schwarz, "Attention-induced trading and returns: evidence from
  Robinhood users", *J. Finance* 77(6), 3141-3190, 2022,
  https://ideas.repec.org/a/bla/jfinan/v77y2022i6p3141-3190.html (peer-reviewed; versions differ in the
  quoted horizon and size).
- Barardehi, Bird, Karolyi, Ruchti, Rule 201 quasi-experiment, *Management Science* 71(5), 3829-3851,
  2025, https://econpapers.repec.org/RePEc:inm:ormnsc:v:71:y:2025:i:5:p:3829-3851 (peer-reviewed; figures
  from the 2019 SSRN version via snippet).
- "Short-selling: the impact of SEC Rule 201 of 2010", EFMA 2011 conference paper (authors not seen),
  https://www.efmaefm.org/0EFMAMEETINGS/EFMA%20ANNUAL%20MEETINGS/2011-Braga/papers/0393.pdf (working paper).
- C. Zarattini, A. Barbon, A. Aziz, "A profitable day trading strategy for the U.S. equity market",
  Swiss Finance Institute RP 24-98, https://ideas.repec.org/p/chf/rpseri/rp2498.html (working paper; snippet).
  Replication blog, https://www.mql5.com/en/blogs/post/776235 (vendor/anecdotal).
- S. Heston, R. Korajczyk, R. Sadka, "Intraday patterns in the cross-section of stock returns",
  *J. Finance* 65(4), 1369-1407, 2010, https://arxiv.org/pdf/1005.3535.pdf (peer-reviewed; abstract).
- SEC DERA white paper on LULD Amendment 10,
  https://www.sec.gov/dera/staff-papers/white-papers/dera_wp_effect_of_amendment_10_of_luld_pilot_plan (regulator; snippet).
- SEC suspensions 2025-26 (law-firm summaries, snippet):
  https://www.securitieslawyer101.com/2026/02/03/latest-sec-trading-suspensions-should-be-a-wake-up-call-for-foreign-issuers/;
  https://www.securitieslawyer101.com/2025/sec-trading-suspensions-of-qmmm-sdm-new-sec-cross-border-task-force/.
- Bloomberg, "2026 Wall Street apparent pump and dump investor scam" (graphic, press, not read),
  https://www.bloomberg.com/graphics/2026-wall-street-apparent-pump-and-dump-investor-scam/;
  Inquirer, https://www.inquirer.com/business/pump-and-dump-bancroft-capital-ipos-20260512.html (press).
- HBS Working Knowledge on Soltes (press, snippet),
  https://www.library.hbs.edu/working-knowledge/experienced-investors-who-think-they-can-beat-the-scam.
- Interactive Brokers Campus, "The risks of shorting, part II: borrow fees" (vendor, snippet),
  https://www.interactivebrokers.com/campus/traders-insight/securities/short-selling/the-risks-of-shorting-series-part-ii-borrow-fees/.
- Benzinga/S3 and Bloomberg on high borrow fees (press, snippet), https://www.benzinga.com/node/14445319;
  https://www.bnnbloomberg.ca/nikola-short-seller-frenzy-sends-stock-borrow-fees-soaring-1.1450854.
- Tapeboard glossary, https://tapeboard.com/glossary/what-is-the-borrow-fee (vendor/anecdotal).

Not read, cited from memory or via other study files (not verified): Berkman et al. (2012),
Gao et al. (2018), Yuan (2015), Plastun et al. (2020), Frieder & Zittrain (2007), Hanke & Hauser (2008);
Regulation SHO Rule 203 text (locate and close-out, search summaries only).

**Not found:** (1) any study of borrow fee or availability on gap or promotion days for sub-$20 stocks;
(2) any locate success rate or HTB rate for micro-caps at Alpaca, or Alpaca's HTB rate formula input;
(3) whether Alpaca charges the daily HTB fee on a position closed the same day; (4) Alpaca's SSR
handling of restricted shorts; (5) a peer-reviewed backtest of shorting pumped or promoted micro-caps net
of borrow; (6) a peer-reviewed time-of-day profile of gapper fades or opening-range/VWAP fades in
small caps; (7) stop-loss slippage or up-pause reopening distributions for sub-$20 names; (8) whether
the paper account simulates locates.

My own arithmetic, not from a paper: the rate/360 daily costs, the +17%-over-prior-close SSR example, the
125-250% margin multiples, the 1%-of-equity size bound and every assumed percentage in the "What to
test" lists (locate fee 1.0/2.5%, 30/60% skip, 8% / 1.5x-range stop, 10 million float, 180 days). They
are starting points for train, not estimates.
