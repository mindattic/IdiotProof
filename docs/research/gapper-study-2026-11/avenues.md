# Gapper study: unused evidence avenues (IP-T456)

Written 2026-11-08, after `literature.md`, `protocol.md`, `explore-plan.md` and `hypotheses.json`.
It lists public data the study does **not** yet use that could help (a) recognize a gapper while it is
forming (below +20%), or (b) time the exit when velocity runs out (roughly 09:15-09:45 ET). It is a
research note, not an amendment: nothing here changes the frozen protocol, and anything adopted goes
through the explore round (train only, every trial in the ledger) and amendment 04.

Already in use, so not repeated: Alpaca SIP 1-min extended-hours bars and daily bars, Alpaca news,
EDGAR full-index plus acceptance timestamps for 8-K, 8-K/A, 4, 3, S-1, S-3, 424B3/4/5, SC 13D,
25-NSE, 15-12G (with `eightKItems`), Alpaca splits, an 08:00 quote snapshot for the spread. The
protocol's T7 tier (FINRA daily short volume, XBRL shares outstanding) is defined but **not built**
(no code references `GapperStudyShortVolume`, `GapperStudySharesOutstanding` or `companyfacts`).

## How to read each entry

- **History**: whether 2021-11 .. 2026-10 is freely obtainable, from where, format, limits.
- **Point in time (PIT)**: when each record became public, and whether a feature at time t on day D
  (04:00-09:30 ET) can use it without lookahead.
- **Literature**: peer-reviewed unless marked. *(verified)* = bibliographic record and the quoted
  finding were confirmed by web search in this pass (often abstract-level, not the full text).
  *(unverified)* = not confirmed; do not cite in a results document until checked.
- **Effort**: rough engineering cost on this stack. **Track** = explore-plan track it feeds.

## Summary

| # | avenue | 5-yr free? | PIT | effort | tracks | verdict |
|---|---|---|---|---|---|---|
| A1 | Halt / LULD pause history (Nasdaq Trader RSS by date) | yes | exact, second-stamped | low | 1, 2, 5, 6 | **shortlist #1** |
| A2 | EDGAR widening: 6-K, F-1/F-3, EFFECT, 424B1/2/7, SC 13G, S-1/A; ATM detection | yes | acceptance time | low / medium | 2, 5, 6, 7 | **shortlist #2** |
| A3 | XBRL shares outstanding and public float (float proxy, rotation, baby-shelf cap) | yes | `filed` date | low | 2, 5 | **shortlist #3** |
| A4 | Alpaca SIP trades and quotes: odd lots, TRF share, retail imbalance, trade size | yes | ns-stamped | high | 1, 2, 5 | **shortlist #4** |
| A5 | Short-sale constraint block: Rule 201 SSR (derived), FINRA short interest, Reg SHO threshold | yes | known before 04:00 | low | 2, 5, 6 | **shortlist #5** |
| A6 | Alpaca opening/closing auction prints | yes | 09:30 print | low | 4, 5 | useful add-on |
| A7 | FINRA daily short sale volume (T7) | yes (2018-08+) | ~18:00 ET on D-1 | low | 2, 6 | low priority |
| A8 | SEC fails-to-deliver | yes | ~2+ week lag | low | 2, 6 | low priority |
| A9 | Reddit (Arctic Shift dumps) | yes (dumps) | `created_utc` | high | 1, 6 | research only |
| A10 | FINRA monthly short-sale transaction files | partly verified | month lag | medium | 6 | research only |
| A11 | EDGAR server logs | 2020-05-19 .. 2025-06-30 | not public in real time | medium | 1 | not worth (live) |
| A12 | Stocktwits history | no | n/a | n/a | 1 | not worth |
| A13 | Google Trends | no (not PIT) | no | n/a | 1 | not worth |
| A14 | Wikipedia pageviews | yes | hourly | medium | 1 | not worth |
| A15 | Nasdaq NOII / opening imbalance | no (paid) | n/a | n/a | 5 | not worth |
| A16 | Borrow fee / easy-to-borrow history | no (official) | n/a | n/a | 6 | not worth; record forward |
| A17 | FINRA OTC transparency (ATS/non-ATS weekly) | yes | 2-4 week lag | low | 2 | not worth |

---

## A1. Trading halts and LULD pauses

**What.** Every US halt with symbol, halt date and time to the second, reason code (`LUDP` LULD
volatility pause, `LUDS` straddle pause, `T1` news pending, `T2` news released, `T3` news and
resumption times, `T12` additional information requested, `H10` SEC suspension, `M` volatility
pause on a non-Nasdaq market, `D` delisting, etc.), resumption quote time and resumption trade time,
and market (Q Nasdaq, N NYSE, A NYSE American, P Arca). This replaces the protocol's bar-gap
`haltFlag` proxy, which the protocol itself names as a proxy because "Alpaca has no historical LULD
feed".

**History (verified).** `https://www.nasdaqtrader.com/rss.aspx?feed=tradehalts&haltdate=MMDDYYYY`
returns that day's halts as RSS/XML. Checked 2021-11-01 (19 items; markets Q, N, P, A; codes LUDP, M,
D) and 2022-03-08 (70 items; e.g. `EDNC 07:28:46 T3`, `HYMC 09:30:40 LUDP`, resume 09:35:40). It
carries a `PauseThresholdPrice` field, empty on the dates checked. One request per trading day,
about 1,250 requests for the window; no published rate limit, so stay at ~1 request/second. The
interactive halt search (`Trader.aspx?id=TradingHaltSearch`) only shows the last year; use the RSS.
NYSE's own history page (nyse.com/trade-halt-historical) was not verified.

**PIT.** Exact: a halt at 09:30:40 is public at 09:30:40. Features at t use only rows with
`haltTime < t`. Premarket T1/T3 halts on D are catalyst timestamps; LULD pauses happen only
09:30-16:00, so they are exit-window and prior-day features, not premarket formation features.

**Literature.**
- Hautsch & Horvath, JFE 131(2), 2019, "How effective are trading pauses?" *(verified)*: Nasdaq
  pauses improve price discovery during the break but raise volatility and hurt liquidity after it;
  traders change behaviour in anticipation of a pause. Supports treating "approaching the band" and
  "just resumed" as distinct exit states.
- Christie, Corwin & Harris, JF 57(3), 2002, "Nasdaq trading halts" *(verified)*: news halts with a
  5-minute quote period roughly double spreads after reopening; longer quote periods dampen the
  volatility. Relevant to `T1`/`T3` premarket halts.
- SEC DERA LULD papers: 2015 (already in `literature.md` §11) and the staff paper "LULD pilot plan
  and extraordinary transitory volatility" *(title verified; findings at summary level)*: large price
  reversals were less frequent under LULD than without limits.
- No peer-reviewed paper found that tests post-pause continuation vs reversal in US small caps.

**Features.** `haltsD1` (LUDP count on D-1: a hot-stock marker for H15-style repeat behaviour),
`newsHaltPm` (T1/T3 before t on D, with its time as a catalyst-latency anchor that does not depend on
Alpaca news coverage), `pauseCount0930_0950`, `firstPauseTime`, `upPause` vs `downPause` (sign from
the bar before the halt), `resumedWithin5`. Exit families for Track 5: "sell at the first up-pause
resumption", "sell before the 09:45 band halving if a pause already happened".

**Effort.** Low: 1-2 days (fetcher, `GapperStudyHalts` table, join on symbol and ET date; watch
for symbol suffixes such as `ONL#` and unit/warrant tickers). **Tracks:** 1 (catalyst anchor),
2 (prior-day block), 5 (halts in the exit window, fill rule), 6 (halts are the short side's main risk).

## A2. EDGAR widening and ATM / shelf detection

**What.** Form types the study does not pull, all in the same `form.idx` it already reads:
- **6-K**: foreign private issuers do not file 8-Ks; their press releases arrive as 6-K exhibits.
  Many sub-$5 gappers are foreign (Chinese, Israeli, Greek shipping, Canadian cross-listings), so for
  them the study currently sees no EDGAR catalyst at all.
- **F-1, F-1/A, F-3**: the foreign counterparts of S-1/S-3. Without them `daysSinceS1` is blind for
  foreign issuers.
- **424B1, 424B2, 424B7**: other prospectus takedowns (424B4/B5 are already in).
- **EFFECT**: the SEC's notice that a registration statement went effective. An S-1/F-1 or S-3
  turning effective means supply can hit the market from that moment.
- **S-1/A, S-3/A**: amendments; a fresh S-1/A after a run often precedes pricing.
- **SC 13G, SC 13G/A**: passive >5% holders; financing investors in small-cap registered directs
  often file these right after a deal.

**ATM detection.** An at-the-market program shows up as a 424B5 whose cover text names an "at the
market" offering or a "sales agreement" / "equity distribution agreement", often with an 8-K item 1.01
the same day. Detecting it needs the document text (one fetch per 424B5, not just the header).

**Baby-shelf cap (derived, with A3).** Under Form S-3 General Instruction I.B.6, an issuer with public
float below $75M may sell at most one third of its public float per 12 months off the shelf, and ATM
sales count toward that cap; practitioner sources (Dorsey 2016, law-firm notes 2026) state the float
is measured immediately before a takedown *(regulatory fact; secondary sources)*. A gap raises the
public float and therefore the remaining capacity, which is a concrete mechanism for an issuer with
an active ATM to sell into the spike. No empirical paper testing "ATM sales cap the premarket run"
was found.

**History.** Yes, same source and same 10 req/s limit as T4. The header fetch for added forms is
another few tens of thousands of requests (the current pull runs 8 concurrent at 120 ms spacing).
The 6-K/424B5 document text is one more request per filing of interest.

**PIT.** `ACCEPTANCE-DATETIME` as already used (Rogers, Skinner & Zechman 2017: public within ~40 s).
EFFECT notices carry their own acceptance time. ATM classification uses only the filing's own text.

**Literature.**
- Billett, Floros & Garfinkel, JFQA 54(3), 2019, "At-the-market offerings" *(bibliography verified;
  findings not read in this pass)*.
- Lerman & Livnat 2010 (8-K items) and Dilution Tracker's −20 to −30% offering reaction: already in
  `literature.md` §8, §12.
- 6-K: a Harvard Law School Forum summary (2015, "SEC disclosures by foreign firms") reports that
  6-K filings move abnormal volume and absolute returns, more for firms from weak information
  environments *(forum summary; the underlying journal article was not identified: unverified)*.

**Features.** `has6kPm` (6-K accepted 16:00 D-1 .. t), `daysSinceEffect`, `atmActive` (an ATM 424B5
in the last 12 months with no termination 8-K), `babyShelfHeadroom` (A3 float × 1/3 minus 424B
takedowns in 12 months, sign only), `freshS1A`, `sc13gD5`. Exit: "ATM active and gap > X%" as an
earlier time stop (Track 5); fade-leg filter (Track 6).

**Effort.** Low for the index widening (a filter change and more headers); medium for ATM text
classification (fetch, regex plus the existing LLM pre-classifier, a hand-labelled check set).
**Tracks:** 2 (corporate-actions block), 5, 6, 7 (6-K as a news source the Alpaca feed misses).

## A3. XBRL shares outstanding and public float

**What.** `dei:EntityCommonStockSharesOutstanding` (cover page of 10-K, 10-Q, 20-F, 40-F, with its
as-of date) and `dei:EntityPublicFloat` (USD, as of the last day of Q2, in annual reports). Already
named in the protocol as T7 and in explore-plan Track 2 ("float proxy from XBRL shares outstanding").

**History (endpoints verified on sec.gov).** `https://data.sec.gov/api/xbrl/companyfacts/CIK##########.json`
(per company), `https://data.sec.gov/api/xbrl/companyconcept/CIK##########/dei/EntityCommonStockSharesOutstanding.json`,
`https://data.sec.gov/api/xbrl/frames/...` (cross-section per period), and the bulk
`https://www.sec.gov/Archives/edgar/daily-index/xbrl/companyfacts.zip`, republished nightly at
about 03:00 ET. No key; SEC fair-access rules (declared User-Agent, <= 10 req/s). The bulk ZIP
avoids per-CIK requests entirely. Coverage depends on T5's CIK map.

**PIT.** Each fact carries the accession (`accn`) and `filed` date; use only facts whose filing was
accepted before t (the acceptance time can come from the existing header fetch). Caveats: the cover
date is often weeks before the filing; shares issued since (ATM, registered directs, warrant
exercises) are missing, so the proxy understates supply exactly when dilution is active; reverse
splits after the cover date must be applied from T6; facts are occasionally restated by later
filings, so always pick the value from the latest filing *accepted before t*, not the latest value.

**Literature.**
- Hong, Scheinkman & Xiong, JF 61(3), 2006, "Asset float and speculative bubbles" *(verified)*:
  with short-sale constraints and heterogeneous beliefs, bubble size, turnover and volatility fall as
  float rises; prices drop at lockup expiry. The theory link from small float to speculative spikes.
- Practitioner screens (float < 10-20M, `literature.md` §5) are the only gap-specific evidence; no
  peer-reviewed premarket test of float or float rotation exists (`literature.md` §5 says the same).

**Features.** `sharesOut`, `logSharesOut`, `pmRotation(t)` = premarket shares traded to t divided by
shares outstanding (float rotation), `rotationRoc` (does rotation decelerate before the high? a
Track 5 exhaustion candidate), `publicFloatUsd` (feeds A2's baby-shelf cap).

**Effort.** Low: one bulk ZIP, a parser, `GapperStudySharesOutstanding`. **Tracks:** 2, 5.

## A4. Alpaca SIP trades and quotes (trade-level microstructure)

**What.** The study uses minute bars plus one 08:00 quote. Trade-level SIP data adds what bars hide:
trade size distribution, odd-lot share, venue (exchange code `D` = FINRA TRF, i.e. off-exchange,
where wholesalers internalize retail), condition codes, and with quotes the side of each trade.

**History (fields verified).** `GET /v2/stocks/trades` returns `t` (ns RFC-3339), `x` exchange,
`p`, `s`, `c` conditions, `i` id, `z` tape, plus `u` for cancels/corrections; up to 10,000 rows per
page. `GET /v2/stocks/{s}/quotes` (already used at 08:00) gives NBBO updates. The account has SIP
entitlement. Volume is the cost: a hot gapper prints tens of thousands of trades between 04:00 and
10:30, and quotes are several times larger. A ~12k Tier20 sample plus controls, restricted to
04:00-10:30, is order of 10^8-10^9 trades (estimate, not measured): hours to a day of requests at
200/min and tens of GB on disk. Store in the minute-bar file store pattern, not LocalDB.

**PIT.** Timestamps are SIP times. Filter late and out-of-sequence reports by condition code, drop
`u` rows that are cancellations, and compute features only from rows with `t < t_decision`. TRF
prints can be reported with a short delay; use the SIP timestamp, never the trade-through time.

**Literature.**
- Boehmer, Jones, Zhang & Zhang, JF 2021: sub-penny TRF prints identify marketable retail flow;
  already in `literature.md` §2 as a weekly signal.
- Barber, Huang, Jorion, Odean & Schwarz, JF 79(4), 2024, "A (Sub)penny for your thoughts"
  *(verified)*: on 85,000 of the authors' own retail trades (Dec 2021 - Jun 2022), the BJZZ method
  identifies 35% of retail trades and mis-signs 28%; signing against the quote midpoint cuts the
  error to ~5%. So retail imbalance needs quotes, not just trades.
- O'Hara, Yao & Ye, JF 69(5), 2014, "What's not there: odd lots and market data" *(verified)*:
  odd lots are a median 24% of trades and carry ~35% of price discovery; leaving them out distorts
  order-imbalance measures.
- Aït-Sahalia, Li & Li 2024 (trade-to-quote imbalance collapse at the jump) and Boudt & Petitjean
  2014 are already in `literature.md` §7, §10; this avenue is the data H17 actually needs.
- Comerton-Forde, Jones & Putniņš, JFE 121(3), 2016 *(verified)*: liquidity-supplying short
  sellers are contrarian and active when spreads are wide (context for who sells into a spike).

**Features.** Formation: `oddLotShare(t)` and `trfShare(t)` (retail participation building before
the screen notices), `retailImbalance(t)` with midpoint signing, `medianTradeSize`, `tradeCountRoc`.
Exit: `retailImbalanceFlip` (retail buy imbalance turns negative), `largePrintSellShare` (blocks
hitting the bid), quoted-spread widening, trade-to-quote ratio collapse (H17), all at 1-minute
resolution between 09:00 and 09:45.

**Effort.** High: new pull with its own resumable store, condition-code filtering, quote alignment
(as-of join), and a feature library. Start with trades only (odd-lot, TRF, size) for the gapper
mornings; add quotes for the 08:30-09:50 window only. **Tracks:** 1, 2 (spread/flow block), 5.

## A5. Short-sale constraint block: SSR, short interest, threshold list

### A5a. Rule 201 short-sale restriction (derived from bars)

**What.** SEC Rule 201 FAQ *(verified on sec.gov)*: the circuit breaker triggers when the price falls
10% or more from the prior day's closing price as determined by the listing market, using
consolidated trades during regular trading hours only; it then restricts short sales (no execution
at or below the national best bid) for the rest of that day **and the whole following day**,
including extended hours whenever quotes are disseminated.

**History.** Derivable: SSR on D if the D-1 RTH low <= 0.9 × D-2 official close. The study already
has D-1..D-5 minute bars for every candidate. Caveat: the official close is the listing market's
closing print, which can differ slightly from the last SIP minute close; Alpaca's auctions endpoint
(A6) gives the closing cross price. Nasdaq's Short Sale Monitor exports only 30 days of SSR lists, so
no free official archive was found.

**PIT.** Fully known before 04:00 on D (from D-1). An intraday trigger on D is known at the
triggering trade.

**Literature.** Barardehi, Bird, Karolyi & Ruchti, Management Science 71(5), 2025, "Are
short-selling restrictions effective?" *(verified)*: regression-discontinuity around the −10%
trigger; restricted stocks see ~8% lower short volume and ~35 bps higher daily returns that do not
reverse after the restriction lifts (figures from the SSRN version). Switzer & Yue, J. Int.
Financial Management & Accounting 30, 2019 *(abstract verified)*: the breaker did not dampen intraday
volatility or declines; not a small-cap phenomenon.

**Relevance.** A gapper that crashed on D-1 (often an offering day) and gaps on D does so with shorts
unable to hit bids: a candidate "fewer fades at the open" moderator for H4/H13 and a direct input to
the fade leg (Track 6).

### A5b. FINRA short interest (all listed stocks)

**History (verified on finra.org).** FINRA publishes short interest for all exchange-listed and OTC
equities; the interactive grid, the Equity API and historical files cover **five rolling years**.
Reporting settlement dates are mid-month and month-end; data is published on the 7th business day
after. **Time-sensitive:** with five rolling years, November 2021 is at the edge now; download the
historical files before the start of the sample rolls off. Whether the API needs FINRA API
credentials was not verified.

**PIT.** Use the publication date, not the settlement date (lag ~2-4 weeks). Divide by A3 shares
outstanding for short interest ratio; by ADV20 for days-to-cover.

**Literature.** Asquith, Pathak & Ritter, JFE 78(2), 2005 *(verified)*: high short interest plus low
institutional ownership underperforms by 215 bps/month equal-weighted (39 bps value-weighted, n.s.).
Boehmer, Huszar & Jordan, JFE 96(1), 2010 *(verified)*: high short interest predicts negative
abnormal returns (transient); heavily traded low-short-interest stocks earn positive abnormal
returns. Squeeze papers are in `literature.md` §6. All monthly horizons; for gappers the plausible
role is a moderator (crowded short + catalyst = squeeze-type continuation), not a predictor.

### A5c. Reg SHO threshold securities

**What.** Securities with persistent fails to deliver (five consecutive settlement days, >= 10,000
shares and >= 0.5% of shares outstanding), for which brokers must close out fails.

**History (partly verified).** Nasdaq-listed: `http://www.nasdaqtrader.com/dynamic/symdir/regsho/nasdaqth{yyyymmdd}.txt`,
archive folder `ftp://ftp.nasdaqtrader.com/SymbolDirectory/regsho/` (archive depth not verified).
The 2026-11-06 file was created at 23:00 ET on the trade date. NYSE, NYSE American and Arca publish
their own lists (URL not verified).

**PIT.** The file dated D is created late on D, so it is usable from 04:00 on D+1.

**Literature.** Fotak, Raman & Yadav, JFE 114(3), 2014, "Fails-to-deliver, short selling, and
market quality" *(bibliography verified; findings not read)*. No gap-specific evidence.

**Effort (A5 overall).** Low: SSR is a derived column; short interest is a few hundred files; the
threshold list is ~1,250 small files. **Tracks:** 2 (prior-day/setup block), 5 (moderator of
open fade), 6 (fade-leg feasibility: SSR and threshold status both make shorting harder).

## A6. Opening and closing auction prints

**What.** Alpaca `GET /v2/stocks/auctions` returns, per symbol and day, the opening (`o`) and closing
(`c`) auction prints with price, condition (`O` = opening trade), exchange and time; field names were
seen in forum posts and client libraries, not in Alpaca's reference page *(partly verified)*.

**Use.** The minute bar's 09:30 open is the first trade, which Alpaca staff say often but not always
equals the auction price. H14 ("sell into the open") and the 09:28 sell-by in H12 are about the cross
itself. The official cross price, plus the cross size versus premarket volume, makes the
"sell into the auction" fill realistic and adds `crossSize / pmVolume` and `crossPrice / pmHigh` as
exit-state features after 09:30. The closing print also gives the listing market's official close
for A5a.

**PIT.** Known at 09:30; exit-side only.

**Literature.** Barclay & Hendershott 2003 (already §5). Cao, Ghysels & Hatheway, JF 55(3), 2000,
"Price discovery without trading: evidence from the Nasdaq preopening" *(verified)*: before the
open, dealers signal direction with crossed and locked quotes, not trades (50 active Nasdaq stocks,
a pre-2000 market structure).

**Effort.** Low. **Tracks:** 4, 5.

## A7. FINRA daily short sale volume (protocol T7, not built)

**History (verified).** `https://cdn.finra.org/equity/regsho/daily/CNMSshvol{yyyymmdd}.txt`,
pipe-delimited `Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market` (checked 2021-11-02).
Consolidated files start 2018-08-01. Posted no later than 18:00 ET on the trade date.

**PIT.** D-1's file is public before D's premarket. Risk: FINRA occasionally re-posts a file marked
"Updated" on a later day, and the historical copy is the updated one; small lookahead, document it.

**Literature.** Diether, Lee & Werner, RFS 2009 (already §11). Comerton-Forde, Jones & Putniņš 2016
(above): much short volume is market-maker liquidity provision. FINRA's own 2019 information notice
warns the files cover only off-exchange, publicly disseminated short sales and are often misread as
short interest. On a run-up day the ratio mostly measures wholesalers selling short to retail buyers.

**Verdict.** Low priority: cheap, but the signal on gapper days is expected to be market-making
noise. Worth one ablation trial in Track 2 because it is already pre-registered as T7.

## A8. SEC fails-to-deliver

Per-security, per-settlement-day FTD quantities, released twice monthly with a lag of roughly two
weeks or more (secondary sources; the sec.gov page itself was not fetched). Lagged, so only usable as
a slow setup variable (e.g. FTD spike in the prior month). SEC's own disclaimer: fails are not
evidence of naked shorting. Mostly redundant with A5c, which is daily and PIT. Low priority.

## A9. Reddit (r/wallstreetbets, r/pennystocks, r/Shortsqueeze, r/smallstreetbets)

**History.** Pushshift's public API is gone; the Arctic Shift project publishes per-subreddit
`.zst` JSON-lines dumps (submissions and comments) on Academic Torrents, plus a keyless API with no
uptime guarantee *(project verified; per-subreddit coverage of 2021-2026 not checked)*.

**PIT.** `created_utc` is the post time. Dumps miss posts deleted before capture and keep some edited
text in its later form; score/comment counts are as of capture, not as of t (lookahead if used).
Use post counts and first-mention times only. No equivalent live feed was evaluated (Reddit's API
terms and rate limits not checked).

**Literature.** Bradley, Hanousek, Jame & Xiao RFS 2024 and Warkulat & Pelster 2024 (already §14).
Cookson, Lu, Mullins & Niessner, JFE 158, 2024, "The social signal" *(verified, abstract level)*:
attention on social platforms is two-thirds common across StockTwits, Twitter and Seeking Alpha;
attention predicts lower next-day returns (driven by StockTwits), sentiment higher. Renault 2018:
tweets often follow the move.

**Verdict.** Research only. High effort (ticker extraction from free text with collisions such as
ALL, CEO, ON), likely to show mentions lag the move, and no clean live path. One worthwhile
question for Track 1: on Tier50 mornings, does the first mention precede the first abnormal minute?

## A10. FINRA monthly short-sale transaction files

Trade-level off-exchange short sales (time, price, size, short/short-exempt), published monthly;
FINRA pages list such files for 2009 onward *(existence verified for older years; 2021-2026
availability not checked)*. Month lag, so never a live feature, but it could label who sold into the
09:30-09:45 high (short vs long sellers) for the fade-leg study. Research only.

## A11. EDGAR server logs (not worth for a live signal)

The SEC publishes EDGAR log files for 2003-01-01..2017-06-30 and 2020-05-19..2025-06-30
*(verified)*, with less detail in the second set (some fields withheld under FOIA, SEC-internal IPs
excluded). Abnormal EDGAR searching predicts returns at monthly horizons (Li & Sun, J. Economic
Dynamics and Control 2022, found via search; not read). The logs are released long after the fact,
so there is no live equivalent and the window ends 16 months before the sample does. Could answer a
research question (did EDGAR traffic on the issuer rise overnight before no-news gaps), nothing more.

## A12. Stocktwits history (not worth)

The official API serves recent streams only; no free historical archive was found, and third-party
scrapers that pull history are unofficial and likely against the terms. Cookson et al. 2024 shows
the signal is real at daily horizons, but it cannot be backtested here.

## A13. Google Trends (not worth)

Not point-in-time: each request is rescaled 0-100 within the requested window and re-sampled, hourly
resolution exists only for short recent windows, and small-cap tickers fall below Google's volume
floor. Da, Engelberg & Gao 2011 (already §2) is a two-week horizon. *(Mechanics from general
knowledge; not re-verified in this pass.)*

## A14. Wikipedia pageviews (not worth)

Hourly dumps are free (not re-verified here), but most sub-$20 small caps have no article or one
with negligible traffic, and the large-cap attention literature does not transfer.

## A15. Nasdaq Net Order Imbalance Indicator (not worth)

Opening-cross NOII (paired shares, imbalance side and size, indicative price) runs 09:25-09:30 and
is subscription-only; Nasdaq mentions archived imbalance messages on its EMI site, but no free 5-year
history was found. It would be the best exit-at-the-open signal if it were free. A6's auction prints
are the free substitute after the fact.

## A16. Borrow fee and easy-to-borrow history (not worth historically; record forward)

No official free history. Alpaca's `easy_to_borrow`/`shortable` asset flags are current-state only;
IBKR's shortable-shares file is described by users as a current snapshot (its history is claimed by
some and denied by others); CurvedTrading, ChartExchange, ORTEX and QuantRocket sell or show derived
history under their own terms. Literature: Engelberg, Reed & Ringgenberg, JF 73(2), 2018,
"Short-selling risk" *(verified)*: stocks with volatile loan fees earn lower returns and are less
efficiently priced. Recommendation: a daily 08:00 ET snapshot of Alpaca asset flags for the
watch universe, so the fade leg has its own borrow history in a year.

## A17. FINRA OTC transparency (ATS and non-ATS weekly volume) (not worth)

Weekly aggregates published two to four weeks after the fact. Too slow and too coarse for either
question.

## Other avenues considered briefly

- **SEC trading suspensions** (34-xxxxx orders): rare in listed stocks; A1 already carries `H10`.
- **FDA advisory committee notices** (Federal Register API, dated notices): scheduled-catalyst
  calendar for a handful of biotech gappers; date-level timing only. Low value per hour of effort.
- **8-K item 3.01 (listing deficiency)**: already in the data through `eightKItems`; no peer-reviewed
  market-reaction study was found. Use it as a type prior; it pairs with reverse splits.

---

## Ranked shortlist (top 5)

1. **A1 Halt and LULD pause history.** It is the cheapest avenue with exact, second-stamped,
   point-in-time data for the whole window (verified back to 2021-11-01 via the RSS `haltdate`
   query), and it touches both questions: premarket `T1`/`T3` halts are a catalyst timestamp that
   does not depend on Alpaca's news coverage (formation, Track 1/7), and the 09:30-09:50 up-pauses
   are the exit window itself (Track 5), where the protocol now uses a bar-gap proxy and its own fill
   rule. Hautsch & Horvath (JFE 2019) give a reason to expect post-pause volatility, not calm. One
   request per day, one or two days of work.

2. **A2 EDGAR widening plus ATM / shelf detection.** The pipeline and rate gate already exist, so
   adding 6-K, F-1/F-3, EFFECT, 424B1/2/7, S-1/A and SC 13G is mostly a filter change. 6-K closes the
   biggest blind spot: foreign small caps are frequent gappers and file no 8-Ks. ATM status plus
   baby-shelf headroom is the clearest *mechanism* for velocity dying: an issuer with an active ATM
   and fresh capacity sells into the spike. That gives Track 5 a supply-side exit feature with an
   economic reason (finalist criterion 4), not just a price-shape rule. ATM text classification is
   the only medium-effort part.

3. **A3 XBRL shares outstanding and public float.** Already planned in Track 2 and pre-registered as
   T7, a single nightly bulk ZIP, and point-in-time by filing. It turns premarket volume into float
   rotation, the practitioner's main gapper filter that has no peer-reviewed test (`literature.md`
   §5), and Hong, Scheinkman & Xiong (JF 2006) give the theory for why small float makes spikes
   larger. It also feeds A2's baby-shelf cap. Its known weakness (stale after dilution) is
   measurable by comparing against A2's takedowns.

4. **A4 Alpaca SIP trades (then quotes) for the gapper mornings.** The only avenue that adds new
   information at minute resolution inside the 09:00-09:45 exit window. H17's exhaustion footprint
   (trade-to-quote imbalance collapse) cannot be measured from bars, and retail participation
   (odd-lot share, TRF share, midpoint-signed retail imbalance per Barber et al. JF 2024) is the
   natural measure of the herding that Barber et al. 2022 tie to the open. It is the most expensive
   item (storage and pull time), so restrict it to candidate mornings and the 08:30-09:50 window
   first.

5. **A5 Short-sale constraint block (SSR derived, FINRA short interest, Reg SHO threshold).** SSR is
   free from bars already held and is known before 04:00; Barardehi et al. (Management Science 2025)
   show the restriction causally lowers short volume and raises returns, which makes it a plausible
   moderator of the open fade (H4/H13) and a hard input to the fade leg. FINRA short interest and the
   threshold list are cheap daily/bi-monthly files that complete the squeeze setup the study
   currently lists as unavailable. **Act now on short interest:** FINRA keeps five rolling years, so
   the 2021-11 start of the sample is about to roll off.

Honourable mention: **A6 auction prints** (low effort, makes H14's "sell into the open" fill exact).

## Not worth it (with reasons)

- **Stocktwits history (A12):** no free, terms-compliant archive.
- **Google Trends (A13):** not point-in-time (per-request rescaling), too coarse, small caps under the
  volume floor.
- **Wikipedia pageviews (A14):** most small caps have no article.
- **Nasdaq NOII (A15):** paid; no free history.
- **Borrow fee / ETB history (A16):** no official free history; start a forward recorder instead.
- **EDGAR server logs (A11):** no live equivalent, ends 2025-06-30; research curiosity only.
- **FINRA OTC transparency (A17):** weekly, 2-4 week lag.
- **Reddit (A9) and FINRA monthly short-sale transactions (A10):** research-only; high effort for
  questions the live system cannot use.
- **FINRA daily short volume (A7) and FTDs (A8):** not "not worth", but low priority: cheap, already
  pre-registered (A7), expected to be market-making noise on gapper days.

## Sources not verified in this pass

- NYSE historical halts page and CSV (nyse.com/trade-halt-historical): not fetched.
- Nasdaq Reg SHO threshold archive depth on `ftp.nasdaqtrader.com`, and NYSE threshold list URLs.
- Alpaca auctions endpoint field definitions (seen in forum posts and client libraries, not the
  official reference); Alpaca SIP trade history depth before 2021 (not needed for this window).
- FINRA Equity API authentication requirements for short interest.
- SEC FTD release lag (from secondary sources; sec.gov page not fetched).
- Arctic Shift per-subreddit coverage for 2021-2026; Reddit live API terms.
- Billett, Floros & Garfinkel 2019 and Fotak, Raman & Yadav 2014: bibliographic records verified,
  findings not read.
- The journal article behind the 2015 Harvard Law School Forum 6-K summary: not identified.
- Li & Sun 2022 (EDGAR search traffic): found via search, not read.
- Google Trends and Wikipedia pageview mechanics: stated from general knowledge.
- Barardehi et al. effect sizes (8% / 35 bps) come from the SSRN version; the published figures may
  differ.
