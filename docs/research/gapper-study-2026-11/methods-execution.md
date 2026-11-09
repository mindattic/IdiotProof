# Gapper study: methods for execution, getting filled in and out (IP-T467)

A web pass on what it takes to actually get filled in a premarket gapper: buying between 04:00
and 09:25 ET and selling at about 09:15, at about 09:28 or around the 09:30 bell. It complements
`methods-exit.md` (when to sell; its §2.2 has the Nasdaq 09:28 cutoff and §5 has Barclay &
Hendershott's 3-4x after-hours spreads), `methods-onset.md` (when to buy) and `literature.md`
§11 (DERA LULD figures). Those are referenced here, not repeated.

The frozen cost model (`params.json` `costModel`, `GapperCostModel.cs`): spread = Corwin-Schultz
over the prior 5 days' 04:00-20:00 minute bars x a premarket multiplier calibrated once (median
quoted spread / CS on 500 Tier20 train events at 08:00), half paid per side; impact per side =
`1.0 x sigma20_daily x sqrt(shares / pmCumVol(t))` for a $1,000 trade; round trip =
max(spread + 2 x impact, 0.5%); fills at the next bar open; a halt fills at the first bar open after
the gap.

Labels: **rulebook** (exchange or SEC/FINRA text), **vendor** (Alpaca docs, staff forum posts,
exchange marketing), **peer-reviewed**, *(abstract only)* or *(secondary)* when I did not read the
source itself. All times ET. Every rule suggested here is new relative to the frozen protocol, so it
can only enter through a registered amendment, on train first.

---

## 1. Getting in: extended-hours orders

### 1.1 Alpaca's rules (vendor)

- Sessions: overnight 20:00-04:00 Sunday-Friday, premarket 04:00-09:30, after-hours 16:00-20:00.
  All symbols tradable in regular hours are tradable in extended hours.
- An order is eligible outside 09:30-16:00 only with `extended_hours = true`, `type = limit` and
  `time_in_force` of `day` or `gtc`; anything else is rejected. (The same docs site still has older
  pages that say `day` only; the current "Orders at Alpaca" page says `day` or `gtc`.)
- No stop, stop-limit or trailing-stop order triggers outside regular hours ("Trailing stop will not
  trigger outside of the regular market hours"). So every protective exit in premarket has to be
  computed by the Monitor and sent as a fresh limit order.
- Short sales in extended hours follow the same rules (limit only).
- Source: https://docs.alpaca.markets/docs/orders-at-alpaca (read this pass).

### 1.2 Where the order actually goes (vendor, regulatory filing)

Alpaca's Rule 606 report for Q3 2024 shows 100% of held NMS orders as non-directed, all routed to
three wholesalers: Citadel Securities (51%), Virtu (25%) and Jane Street (24%) for July 2024 S&P 500
names. The "material aspects" text says extended-hours orders routed to Virtu and filled outside the
core session receive no payment, and orders filled in the primary opening and closing auctions
neither earn a rebate nor pay a fee. Alpaca's extended-hours disclosure (secondary, via search
snippet) says these orders may be routed as *not held*.

Two consequences:

- A premarket limit order does not sit on a lit exchange book under Alpaca's name. Whether it is
  shown, how it queues, and whether a resting extended-hours `day` sell joins the opening cross are
  the wholesaler's choices, not documented by Alpaca. Nasdaq's FAQ (Q22) says only interest on the
  Nasdaq book is eligible for the Nasdaq Opening Cross.
- Fill quality in premarket cannot be learned from the paper account. Alpaca's paper engine fills
  against the NBBO, does not check size against displayed quantity, gives random partial fills 10% of
  the time, and does not simulate market impact, queue position or latency slippage
  (https://docs.alpaca.markets/docs/paper-trading). A paper premarket fill is a quote touch, nothing
  more.

### 1.3 How liquid the premarket is

- **Barclay & Hendershott (2003, RFS)**, *Price discovery and trading after hours* *(abstract
  only)*: after-hours trades are fewer but carry more private information per trade; prices are
  less efficient. **Barclay & Hendershott (2004, JF)** (spreads 3-4x daytime, adverse selection)
  is in `methods-exit.md` §5.
- **Barclay & Hendershott (2008, J. Empirical Finance)**, Nasdaq pre-open *(abstract only)*: more
  pre-open volume made the opening price more efficient, but **price discovery moved into the
  pre-open only for the highest-volume stocks**; a critical volume threshold is needed before the
  pre-open price carries more information. In a thin gapper, the premarket print is a noisier guide
  to the 09:30 price than in a busy one.
- **Cao, Ghysels & Hatheway (2000, JF)** on non-binding Nasdaq pre-open quotes is in `avenues.md` A6.
- **NYSE, "The early bird gets the worm" (Bazinas, 10 Feb 2025; exchange research, vendor)**:
  premarket is now over 55% of extended-hours shares (Q1 2025) and grew 15x since 2019; trading
  before 07:00 went from under 5% of premarket in 2019 to over 28%; the last 30 minutes before the
  open fell from 37% to just over 16%; **over 3.7% of 2024 premarket volume printed between
  08:00:00 and 08:00:01, over 8% by 08:00:05**; premarket 2025 VWAP is $18 against $46 in the core
  session. So the premarket is dominated by low-priced names and has a sharp liquidity spike at
  08:00, when many retail brokers open their premarket. No spread or depth figures are given.
- **Gap.** I found no peer-reviewed study after 2010 of premarket quoted spreads, depth at the touch
  or limit-order fill rates for US small caps. The protocol's own 08:00 quote calibration is the
  best evidence available, which is why its limits matter (1.4).

### 1.4 Is the frozen cost model plausible?

At the frozen size ($1,000) it is **plausible for the median trade at a liquid moment and
optimistic in the tails**, for six reasons:

1. **The impact term is close to zero, so the model is almost all spread or floor.** Illustrative
   arithmetic (hypothetical inputs, mine): $2.00 stock, 500 shares, pmCumVol 1,000,000 shares,
   sigma20 8% a day gives `0.08 x sqrt(0.0005)` = 0.18% per side. The square-root law was fit on
   institutional metaorders in liquid stocks (Tóth et al. 2011); Bucci et al. (2019) show impact is
   *linear*, so smaller still, at low participation. For a single marketable $1,000 order the real
   question is whether the touch holds 500 shares, not a square-root law.
2. **The 08:00 calibration point is the most liquid premarket second of the day** (NYSE above).
   Entries before 08:00, which is where onset detection fires early, probably face wider spreads
   than the multiplier implies.
3. **A median multiplier understates the mean cost.** Spreads are right-skewed and P&L averages
   costs, so the mean round trip exceeds the median-calibrated one.
4. **Corwin-Schultz is volatility-biased.** Jahan-Parvar & Zikes (2023, RFS) *(abstract)* show CS
   and similar high-low estimators mostly measure volatility when the true spread is small relative
   to it, and the bias has grown over time. One multiplier absorbs an average bias but not one that
   varies with each stock's quiet-day volatility.
5. **Sub-$1 names hit tick and floor effects.** Under Rule 612 sub-$1 stocks quote in $0.0001, but a
   $0.30 stock with a one-cent spread already costs 3.3% round trip. The 0.5% floor never binds
   there; the CS estimate has to be right on its own.
6. **The fill rule assumes the fill.** Next-bar-open fills ignore non-fills and adverse selection. A
   buy limit that fills during a fast run fills on the minute that reverses; one that does not fill
   misses the best trades. A sell limit at the bid in a falling book may not fill at all. Neither is
   in the model. This is likely the largest gap and is untestable on minute bars alone.

At larger size (anything over a few percent of premarket volume) the model is no longer a guide:
the impact formula pairs the prior 20 days' sigma (calm days) with premarket-only volume, two errors
that point in opposite directions by unknown amounts.

### What to test (train only, via an amendment)

- **Quote-based cost at the decision minute.** For every train trade, take the actual NBBO from
  `/v2/stocks/{s}/quotes` at the entry and exit seconds; report mean and 90th-percentile quoted
  half-spread against the frozen model's half-spread, split by price tier and by entry hour
  (04-07, 07-08, 08-09:30). If the 04:00-08:00 ratio is above about 1.5x the 08:00 ratio, add an
  hour-of-day multiplier.
- **Depth at the touch.** Share of entries where the displayed ask size at the entry second is
  under the order's shares (check the units of Alpaca quote sizes first: lots or shares).
- **Fill realism.** Re-run H12's selected config with (a) "buy fills only if a later bar's high
  reaches the limit within 60 s, else skip" and (b) "sell at limit = bid; if no later bar's low
  reaches it within 60 s, sell at the next bar low". Report the change in mean net per trade and in
  trade count. Already proposed as sensitivity in `methods-exit.md` §5; here it is the main check.
- **Mean vs median multiplier.** Recompute the calibration with the mean instead of the median and
  report how many configs cross the 1.0% worth-trading bar either way.

---

## 2. Getting out: selling into the open

### 2.1 Nasdaq Opening Cross (rulebook, exchange FAQ)

From the Nasdaq Opening and Closing Crosses FAQ (copyright 2025, file dated 4 Nov 2025):

- The cross runs at 09:30 and merges the on-open book with the Nasdaq continuous book, extended-hours
  `day`/`gtc` orders included (extended IOC excluded). Price: maximise executed shares, then minimise
  imbalance, then minimise distance from the Nasdaq midpoint.
- **MOO must be received before 09:28.** LOO can be entered until 09:29:30, but a late LOO is
  re-priced if more aggressive than the 09:28 reference price or the prior close. Market orders after
  09:28 do not join the cross; they wait for the open.
- **Imbalance (NOII):** paired shares, imbalance shares and side every 10 s from 09:25 to 09:28; from
  09:28 every second, adding near and far indicative clearing prices. Distributed through TotalView
  ITCH. (The FAQ is internally inconsistent: one footnote says full NOII "currently" starts at 09:28
  pending SEC approval, and Q25 says opening-cross orders can be changed only before 09:25. The
  2021 filing in `methods-exit.md` §2.2 has the 09:25/09:28 schedule. Current Rule 4752 not read.)
- **If there is no cross**, the Nasdaq Official Opening Price is the first last-sale-eligible trade
  at or after 09:30.
- **Price validation (Nasdaq System Settings page, marked pending SEC approval).** The cross price
  is checked in sequence: (A) within the greater of $0.50 or 10% of the prior close; failing that,
  (B) within the same distance of the last Nasdaq trade after 09:15; failing that, (C) within the
  same distance of the Nasdaq best bid (if above the prior close) or best offer (if below). **If all
  three fail, every MOO, LOO and imbalance-only order in that symbol's opening book is cancelled
  back**; orders already on the continuous book stay. A gapper always fails check A by definition,
  so it relies on B and C. That is a real risk in a name whose last Nasdaq trade after 09:15 is
  stale or off-Nasdaq.
- Thresholds: the cross price must also lie within the greater of $0.50 or 10% of the Nasdaq
  midpoint, added to the offer and subtracted from the bid.

### 2.2 NYSE American and NYSE Arca (rulebook pages, exchange site)

- **NYSE Arca:** pre-opening from 02:30; Early Open Auction at 04:00; MOO/LOO cancellations rejected
  from 09:29; imbalance freeze 09:29:55-09:30; Core Open Auction 09:30.
- **NYSE American:** core-open imbalance publication from 08:00; MOO/LOO cancels rejected from
  09:29; at 09:29:55 the freeze begins and new MOO/LOO orders are rejected; the auction executes
  within auction collars at 09:30. **NYSE** (rarely a gapper venue): imbalances from 08:00 every
  second, DMM-opened, and the open "may occur after 09:30".
- So the 09:28 MOO cutoff is a Nasdaq rule. Alpaca applies 09:28 to all `opg` orders anyway (2.3).
  Arca/American Rule 7.35 text was not read.

### 2.3 Alpaca's on-open handling (vendor)

- `time_in_force = opg` (market- or limit-on-open) executes only in the opening auction; unfilled
  remainders are cancelled after the open. **`opg` orders submitted after 09:28 and before 19:00
  are rejected**; after 19:00 they queue for the next day. This closes the open question in
  `methods-exit.md` §2.2: Alpaca's cutoff is 09:28, the same as Nasdaq's, so there is no buffer.
  Submit by 09:27 to allow for latency.
- "Any market orders received before 9:28 will be filled at the Nasdaq Official Opening Price";
  orders between 09:28:01 and 09:29:59 on a Nasdaq name will not get it.
- **Not found:** whether an `opg` order can carry `extended_hours = true` (the extended-hours rules
  say `day`/`gtc` only, so probably not, meaning a position cannot hold a working premarket sell
  and an `opg` sell for the same shares at once; Alpaca rejects sells that exceed available
  quantity). **Not found:** any NOII or imbalance feed from Alpaca. The live system cannot see the
  imbalance it would be selling into.

### 2.4 Evidence on the auction price against 09:29 and 09:31

- **Pagano, Peng & Schwartz (2013, JFM)** *(abstract)*: Nasdaq's 2004 calls lowered spreads and,
  more weakly, volatility in the continuous market after the open, across market-cap groups. Says
  nothing about the gap between the cross price and the 09:29 premarket price.
- **Barclay & Hendershott (2008)** above: in low-volume names the premarket price is a weak guide to
  the open.
- **Barber et al. (2022)** in `literature.md` §2: retail market orders queued overnight execute at
  the open in attention stocks, so the open is the retail overpay price. That favours selling *in*
  the cross over selling at 09:31, but only on average and only where the cross prints.
- **Gap.** No study found that compares the official opening price with the 09:29 premarket price
  and the 09:31 price for small or high-attention stocks. This study's A6 auction prints can measure
  it directly.

### 2.5 The three exit routes

| route | how | main risks |
|---|---|---|
| (a) sell 09:15-09:28 in premarket | `extended_hours` limit at or below the bid | pays the premarket half-spread; may not fill in a falling book; the last 30 minutes are now only ~16% of premarket volume (NYSE) |
| (b) sell in the cross | `opg` market or limit sell by 09:27 | blind (no NOII via Alpaca); a Nasdaq cross that fails price validation cancels the order back at 09:30; a thin name may have no cross; Alpaca routing and the exact handling of `opg` at wholesalers are undocumented; a limit-on-open above the cross price does not fill and is then cancelled |
| (c) sell at 09:31 | regular-hours limit or market | LULD bands are doubled 09:30-09:45 and the first reference price is the open, so a fast drop can pause the stock with the position inside it; the first minute is the most volatile; RTH spread is narrower than premarket |

A practical rule that needs no new data: if (b) is used, use an `opg` *limit* sell priced a fixed
distance below the last trade, and arm a fallback that sends a regular limit sell at 09:30:05 if the
`opg` order comes back cancelled. A market-on-open has no protection against a cross that prints
far below the premarket.

### What to test (train only, via an amendment)

- The three-way paired comparison (a)/(b)/(c) proposed in `methods-exit.md` §2.2, with two
  additions: (1) report the share of Tier50 events with **no official opening auction print** or an
  auction volume under the position size x 20, by price tier; (2) for Nasdaq-listed events,
  approximate check B (official open within the greater of $0.50 or 10% of the last trade after
  09:15) and report how often the cross would have been cancelled.
- Cost per route: (a) quoted premarket half-spread at 09:28 from quotes, (b) zero spread plus
  impact, (c) quoted RTH half-spread at 09:31.

---

## 3. LULD halts and other halts

### 3.1 What can halt a premarket position

- **LULD bands apply only 09:30-16:00** (LULD Plan 2025 annual report, rulebook-level). No LULD pause
  can happen in premarket. The first reference price of the day is the primary listing market's
  opening price, or the prior official close if the stock opens on a quote; it is then updated when
  a new value is at least 1% away. The Plan intends a separate filing for overnight bands.
- **Regulatory halts** (news pending, SEC suspension, a listing-market halt for unusual activity)
  can happen at any time, including premarket. They are not LULD pauses and have no fixed length.

### 3.2 How an LULD pause reopens (rulebook)

Nasdaq Rule 4120(c)(10), as summarised in SR-NASDAQ-2020-012 (Release 34-88383, 13 Mar 2020, read
this pass): a 5-minute initial Display Only Period, during which orders can be entered, an auction
reference price and LULD Auction Collars (based on the band that triggered the pause) are set. The
stock is released at the end of the period **unless there is an order imbalance, in which case the
period is extended by 5 minutes and the collar is widened by 5% (or $0.15 for reference prices of
$3 or less) in the direction of the imbalance, and so on every 5 minutes until the stock is released
at the first point with no imbalance**. NYSE describes the same 5-minute extensions with 5% collar
widening (NYSE LULD notice, secondary snippet). Non-LULD halts on Nasdaq reopen through the Halt
Cross (Rule 4753) after a 5-minute Display Only Period, extended in 1-minute steps for volatility;
a 2025 Nasdaq filing (34-102336) proposes collars for that cross too *(abstract)*.

### 3.3 How often (rulebook-level report)

LULD Plan 2025 annual report: 10,763 LULD pauses in 2025 (8,787 in 2024, +22.5%); **1,823 (17%) in
09:30-09:45**, median 7 per day in that window; 11.1% of limit states became pauses; about 65% of
pauses were in Tier 2 non-ETPs priced at $3 or more (41% of symbols). The report gives no pause
durations or extension counts. The 09:45-09:50 cluster and the Tier 2 reversal rates are in
`methods-exit.md` §3.

### 3.4 A resting order during a halt (vendor)

- An Alpaca staff reply (forum, 13 Oct 2025, vendor) says orders for a halted security are accepted
  and held pending until trading resumes. It does not say what happens to an order already resting.
- Because Alpaca routes to wholesalers, a resting limit order is most likely held by the wholesaler
  during the halt (off-exchange trading in a halted NMS stock is prohibited; FINRA Rule 6120 *(not
  read this pass)*) and does **not** join the primary market's reopening auction unless the
  wholesaler sends it there. Not documented by Alpaca.
- Alpaca incident report, 28 Mar 2024: stale halt flags from an OTC data change blocked market
  orders in non-halted symbols until fixed (vendor). The broker's own halt state can be wrong.

### 3.5 Time lost

At least 5 minutes per LULD pause, in 5-minute steps while the imbalance persists, so 10 or 15
minutes in a one-sided name, and several pauses can chain. During that time the price can move by
the widened collar each step (5% or $0.15). For a strategy whose edge lives in the minutes around
09:30, one pause can turn a 1-minute exit into a 10-minute one at a reopening price set by the
imbalance. The frozen `haltFillRule` (first bar open after the gap) is the right default; it is the
reopen price, not the pre-halt price.

### What to test (train only, via an amendment)

- With A1 halt data: share of train trades still open at 09:30 that see a pause before the planned
  exit, and the distribution of the exit-price shortfall (first bar open after the pause minus the
  planned exit-minute price), by direction of the pause.
- Bar-based proxy without A1: count minutes with zero volume between 09:30 and 09:45 that are
  bracketed by bars more than 5% apart; compare the frozen halt fill rule with "fill at the open of
  the bar 10 minutes after the gap" as a worst-case sensitivity.

---

## 4. Position sizing and capacity

### 4.1 Participation cap

The practical rule is to cap the order at a share `p` of the volume the market will trade in the
window you trade in. The square-root law (Tóth et al. 2011) and the Almgren-Chriss framework (2000)
*(not re-read)* both scale cost with participation; Bucci et al. (2019, PRL) find real metaorders
mostly sit at 0.1-10% participation, where impact is closer to linear than square-root. Practitioner
caps of 5-10% of volume are common; I found no peer-reviewed number for premarket small caps.

A concrete cap for this strategy: entry shares <= `p x` (shares traded in the 5 minutes before the
order), and exit shares <= `p x` (expected shares in the exit window: the 09:25-09:30 premarket
minutes for route (a), the auction print for (b)). With `p = 5%` and the frozen
`premarketDollarVolumeFloor` of $50,000, the floor case allows about $2,500 over the whole premarket
(my arithmetic), so the $1,000 trade size is already near the cap for the thinnest qualifying names.

### 4.2 Kelly and fractional Kelly

- Kelly maximises expected log wealth: choose `f` to maximise the sample mean of `log(1 + f R)`
  over trade returns `R`. For small edges `f* ~ mu / sigma^2`; growth at `c` x Kelly is `c(2 - c)`
  of the maximum in that approximation (my derivation, standard), so half-Kelly keeps 75% of the
  growth with half the volatility.
- **MacLean, Thorp & Ziemba (2010, Quantitative Finance)** *(bibliographic record not verified this
  pass)*: full Kelly gives very large drawdowns over realistic horizons; fractional Kelly trades
  growth for security.
- **Baker & McHale (2013, Decision Analysis 10(3), 189-199)** *(abstract)*: with an estimated win
  probability it is always better out of sample to shrink the Kelly bet; they give a back-of-envelope
  shrinkage.
- **Busseti, Ryu & Boyd (2016)**, risk-constrained Kelly *(not verified this pass)*: a drawdown
  constraint gives a smaller, tractable bet.
- **Fat tails.** `mu / sigma^2` is meaningless when one trade can lose 50-100% (a halt that reopens
  far down, a trading suspension). Use the empirical `log(1 + f R)` on train trades, which caps `f`
  below `1 / |worst loss|`, then shrink. With a mean net of 1% per trade and a per-trade standard
  deviation of 10-20%, `mu / sigma^2` gives 0.25-1.0 of equity per trade (my arithmetic,
  hypothetical numbers). That is far above anything the premarket book can absorb, and the
  estimate's own error is about as large as `mu`. Capacity (4.1), not Kelly, binds first; Kelly is
  only useful as an upper bound and as a check that the edge survives shrinkage.

### 4.3 Capacity

Capacity per event = `p x` the dollar volume in the entry and exit windows, the smaller of the two.
Strategy capacity per day = that summed over the events that fire. The binding side is the exit:
the exit concentrates the whole morning's position into the last minutes or the auction, where many
other gapper traders are selling too. No published capacity estimate for premarket gappers was found.

### What to test (train only, via an amendment)

- For the selected H12 config, the distribution over trades of `1,000 / (price x shares traded in
  the 5 minutes before entry)` and the same at the exit window. Report the share of trades where a
  $1,000, $5,000 and $25,000 order exceeds 5% and 10%.
- Capacity curve: mean net per trade under the frozen model at $1,000, $5,000 and $25,000 notional
  (the impact term scales with it), with trades that breach the 10% cap skipped.
- Empirical fractional Kelly from train trades with a Baker-McHale-style shrinkage; report it, do not
  trade on it.

---

## 5. The short leg: SSR and borrow (brief)

- **Rule 201 (SEC FAQ, rulebook-level).** Triggered by a decline of 10% or more from the prior
  regular-session close; the trigger is determined only during regular hours; once triggered, the
  price test applies for the rest of the day and the whole next day, **including outside regular
  hours** wherever a national best bid exists. A gapper fading from +50% does not trigger it unless it
  falls below the prior close by 10%. A name that fell 10% the day before is already restricted in
  today's premarket: shorts must be priced above the national best bid, so a short cannot hit bids.
- **Alpaca borrow (vendor).** Easy-to-borrow names have no locate or borrow fee; hard-to-borrow
  shorts need an approved single-use locate per short and pay a daily fee charged in round lots;
  the `borrow_status` field on the assets endpoint gives the status. Margin on shorts under $5 is the
  greater of $2.50 per share or 100%, so a $1 short ties up 250% of its value. Shorting and margin
  need $2,000 equity. Whether a name can flip from ETB to HTB during the morning was not found.
- Premarket shorts are limit-only like longs, so a short squeeze in premarket cannot be stopped out
  by the broker.

### What to test (train only, via an amendment)

- If a short arm is ever registered: the share of short signals on names under SSR from the prior day
  (prior day's low at or below 0.9 x the day-before close), and the short fill rate under "limit one
  tick above the bid, fills only if a later bar's high reaches it".

---

## Summary: what changes the frozen cost model's plausibility

1. Alpaca has no premarket stops and its paper engine does not simulate depth, queue or latency, so
   neither the backtest's next-bar fill nor paper trading measures real fills. (Plausibility:
   optimistic; untested.)
2. The 08:00 calibration point is the liquidity spike of the premarket; earlier entries likely face
   wider spreads. (Optimistic for early entries.)
3. Median calibration and the volatility bias of Corwin-Schultz both push the modelled mean cost
   below the real one. (Optimistic.)
4. At $1,000 the impact term is negligible, so the model is spread-only; it is not valid for size.
   (Neutral at $1,000, wrong at size.)
5. Selling in the Nasdaq cross needs an `opg` order by 09:28 at Alpaca, with no imbalance data, and a
   gapper's cross depends on validation checks B/C; failure cancels the order back.
6. LULD cannot pause a stock in premarket, but anything held past 09:30 faces doubled bands and
   5-minute pause steps; the frozen halt fill rule is right.

## Sources

Read this pass (full page or the relevant section):
- Alpaca, "Orders at Alpaca", https://docs.alpaca.markets/docs/orders-at-alpaca (vendor).
- Alpaca, "Paper Trading", https://docs.alpaca.markets/docs/paper-trading (vendor).
- Alpaca, "Margin and Short Selling", https://docs.alpaca.markets/docs/margin-and-short-selling (vendor).
- Alpaca Securities LLC, Rule 606 report, Q3 2024,
  https://cdn.finra.org/606/2024/288202_606_NMS_2024_Q3_V3.pdf (regulatory filing; first pages read).
- Alpaca forum, "Order status during halt", staff reply 13 Oct 2025,
  https://forum.alpaca.markets/t/order-status-during-halt/17962 (vendor, anecdotal).
- Alpaca status page, incident 28 Mar 2024, https://status.alpaca.markets/incidents/qdh7ttv119c1 (vendor).
- Nasdaq, "The Nasdaq Opening and Closing Crosses: Frequently Asked Questions" (2025),
  https://www.nasdaqtrader.com/content/productsservices/trading/crosses/openclose_faqs.pdf (exchange).
- Nasdaq Trader, "System Settings" (Opening Cross thresholds and price validation),
  https://nasdaqtrader.com/Trader.aspx?id=SYSTEMSETTINGS (exchange; validation marked pending SEC approval).
- SEC Release 34-88383, SR-NASDAQ-2020-012 (13 Mar 2020), describing Rule 4120(c)(10),
  https://www.sec.gov/files/rules/sro/nasdaq/2020/34-88383.pdf (rulebook).
- NYSE Arca trading information, https://www.nyse.com/markets/nyse-arca/trading-info (exchange).
- NYSE auctions page (NYSE and NYSE American timetables), https://www.nyse.com/trade/auctions (exchange).
- LULD Plan Operating Committee, 2025 Annual Report, https://cdn.luldplan.com/reports/LULD-2025-Annual-Report.pdf.
- S. Bazinas, "The early bird gets the worm: a new normal in off-hours US equities trading", NYSE,
  10 Feb 2025, https://www.nyse.com/research/insights/the-early-bird-gets-the-worm-a-new-normal-in-off-hours-us-equities-trading (exchange research).

Abstract, bibliographic record or search snippet only:
- M. J. Barclay and T. Hendershott, "Price discovery and trading after hours", *Review of Financial
  Studies* 16(4), 1041-1073, 2003 (bibliographic details from memory).
- M. J. Barclay and T. Hendershott, "A comparison of trading and non-trading mechanisms for price
  discovery", *Journal of Empirical Finance* 15(5), 839-849, 2008, DOI 10.1016/j.jempfin.2008.03.001,
  https://ideas.repec.org/a/eee/empfin/v15y2008i5p839-849.html.
- M. S. Pagano, L. Peng and R. A. Schwartz, "A call auction's impact on price formation and order
  routing: evidence from the NASDAQ stock market", *Journal of Financial Markets* 16(2), 331-361,
  2013, DOI 10.1016/j.finmar.2012.11.001, https://ideas.repec.org/a/eee/finmar/v16y2013i2p331-361.html.
- M. R. Jahan-Parvar and F. Zikes, "When do low-frequency measures really measure effective spreads?
  Evidence from equity and foreign exchange markets", *Review of Financial Studies* 36(10),
  4190-4232, 2023, https://ideas.repec.org/a/oup/rfinst/v36y2023i10p4190-4232..html; working paper
  https://www.federalreserve.gov/econres/feds/files/2019051pap.pdf.
- B. Tóth, Y. Lempérière, C. Deremble, J. de Lataillade, J. Kockelkoren and J.-P. Bouchaud,
  "Anomalous price impact and the critical nature of liquidity in financial markets", *Physical
  Review X* 1, 021006, 2011, https://arxiv.org/abs/1105.1694.
- F. Bucci, M. Benzaquen, F. Lillo and J.-P. Bouchaud, "Crossover from linear to square-root market
  impact", *Physical Review Letters* 122, 108302, 2019, https://arxiv.org/abs/1811.05230.
- R. D. Baker and I. G. McHale, "Optimal betting under parameter uncertainty: improving the Kelly
  criterion", *Decision Analysis* 10(3), 189-199, 2013, DOI 10.1287/deca.2013.0271,
  https://ideas.repec.org/a/inm/ordeca/v10y2013i3p189-199.html.
- SEC Division of Trading and Markets, "Responses to Frequently Asked Questions Concerning Rule 201 of
  Regulation SHO", https://www.sec.gov/divisions/marketreg/rule201faq.htm (answers 1.2 and 2.1 seen
  as search snippets).
- Nasdaq, Release 34-102336 (2025) on Halt Cross collars,
  https://www.sec.gov/files/rules/sro/nasdaq/2025/34-102336.pdf.

Cited from memory, not verified this pass:
- C. Cao, E. Ghysels and F. Hatheway, "Price discovery without trading: evidence from the Nasdaq
  preopening", *Journal of Finance* 55(3), 1339-1365, 2000.
- S. A. Corwin and P. Schultz, "A simple way to estimate bid-ask spreads from daily high and low
  prices", *Journal of Finance* 67(2), 719-760, 2012.
- R. Almgren and N. Chriss, "Optimal execution of portfolio transactions", *Journal of Risk* 3(2),
  5-39, 2000.
- L. C. MacLean, E. O. Thorp and W. T. Ziemba, "Long-term capital growth: the good and bad properties
  of the Kelly and fractional Kelly capital growth criteria", *Quantitative Finance* 10(7), 681-687, 2010.
- E. Busseti, E. K. Ryu and S. Boyd, "Risk-constrained Kelly gambling", arXiv 1603.06183, 2016.
- FINRA Rule 6120 (trading halts in NMS stocks).

Not found: a post-2010 peer-reviewed study of premarket spreads, depth or fill rates in US small
caps; any study comparing the official opening price with the 09:29 and 09:31 prices for small or
attention stocks; Alpaca documentation of how wholesalers handle resting extended-hours orders at the
cross or during a halt; any Alpaca imbalance (NOII) feed; whether `opg` can be combined with
`extended_hours`; LULD pause durations or extension counts after 2017.

My own arithmetic, not from a paper: the impact example (0.18% per side), the $2,500 capacity at the
$50,000 floor and 5% participation, the `c(2 - c)` fractional-Kelly growth, and the 0.25-1.0 Kelly
fraction example. All inputs are hypothetical and should be replaced with train numbers.
