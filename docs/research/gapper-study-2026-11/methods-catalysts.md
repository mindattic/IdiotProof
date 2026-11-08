# Gapper study: scheduled catalysts and the news-latency pipeline (IP-T464)

Written 2026-11-08 after `protocol.md`, `hypotheses.json` (H1-H24), `literature.md`, `avenues.md`,
`methods-onset.md` and `methods-exit.md`. It is a research note, not an amendment. Nothing here
changes the frozen protocol; anything adopted goes through the explore round (train only, every trial
in the ledger) and a later amendment.

Two questions:

1. **Which gapper causes are known the day before?** These could go on a watchlist at D-1 16:00
   instead of being found by a scanner at 08:00.
2. **The news-latency pipeline.** When small-cap releases actually arrive, how late the feeds this
   stack uses are, how fast prices react, and how many minutes a live news pipeline could gain over a
   "top gainers" scanner.

Not repeated here: the catalyst block (`catalystType`, `pubToTMinutes`, `sourceTier`, `novelty`,
`eightKItems`), H2/H3/H16 (catalyst groups), H7 (`daysSinceReverseSplit`), H10 (`daysSince424B`),
amendment 02 (H18 news ride, H19 share of the move before publication, H20 entry-delay decay), A1
halts, A2 EDGAR widening, and P3 (clock-time onset prior, `methods-onset.md`).

**How to read the entries.** *(verified)* means the bibliographic record and the quoted finding were
confirmed in this pass, usually at abstract level. *(secondary)* means the figure comes from a
summary, not the paper. *(unverified)* means do not cite in a results document until checked.
Rating: **worth testing**, **descriptive only**, **not worth**, or **already covered**.

## Summary

| # | item | known before D? | PIT data, free, 2021-2026 | evidence of a pre-event run-up | rating |
|---|---|---|---|---|---|
| C1 | FDA PDUFA dates | yes, the date (the decision can come early) | issuer text (news, 8-K, 10-Q) before D; vendor calendars are not PIT | weak/none in stock price; options yes | **worth testing** (precision, low recall) |
| C2 | FDA advisory committees | yes, >= 15 days ahead | Federal Register notices | none in stock volume; options yes | descriptive only |
| C3 | Clinical-trial readouts | only a window | ClinicalTrials.gov dates (version history needed); issuer guidance text | mixed; one oncology study shows separation before the result | descriptive only (medium-high effort) |
| C4 | Earnings dates | yes, usually 1-3 weeks ahead | own history of 8-K item 2.02 acceptances; "to report on" news items | earnings premium and volume surge (all caps) | **worth testing** |
| C5 | Investor conferences | yes | issuer news items | small, large-cap | not worth |
| C6 | Reverse-split effective dates | yes, days ahead | 8-K 5.03/3.03 text, Alpaca corporate actions (if declaration date is kept) | negative on average; practitioner "post-split squeeze" unstudied | descriptive only (extends H7) |
| C7 | Russell reconstitution | yes, fixed calendar | FTSE Russell lists | ~1% over 20 days, then reversal | not worth |
| C8 | IPO lock-up expiry | yes (prospectus) | IPO date + 180 days from 424B4 | negative (-1.5%, 3 days) | not worth as a gapper cause |
| C9 | SPAC vote / de-SPAC close | yes (proxy) | DEFM14A, 425, 8-K 5.07 and 2.01 in EDGAR | 2021-22 low-float spikes, anecdotal | descriptive only (regime-limited) |
| C10 | Uplisting date | yes (exchange approval PR, Form 8-A12B) | EDGAR 8-A12B, CERT filings | ~5% announcement return, 1980s | not worth here (no prior close on Alpaca) |
| N1 | Wire release times (repair batch stamps) | n/a | wire page shows minute time in ET | n/a | **worth testing, do first** |
| N2 | Scanner-lead measurement | n/a | data already held | n/a | **worth testing** |
| N3 | Benzinga vs wire vs EDGAR delay | n/a | data already held + N1 | n/a | **worth testing** (descriptive) |
| N4 | Exchange pre-notice and T1 halts | n/a | A1 halts RSS | n/a | already covered by A1; adds a rule fact |

---

## Part 1. Catalysts known in advance

### C1. FDA PDUFA target action dates

**What.** When FDA accepts an NDA or BLA, it sets a PDUFA goal date (about 10 months out, or 6 for
priority review). FDA does not publish these dates. The issuer usually does, in an "FDA accepts
filing, PDUFA date set for ..." press release and again in 10-Q/10-K risk text. FDA can act before
the date, and decisions often come after the close, so the gap lands on D or D+1.

**Evidence.**
- Rothenstein, Tomlinson, Tannock & Detsky, *JNCI* 103(20):1498-1506, 2011, "Company stock prices
  before and after public announcements related to oncology drugs" *(verified, abstract and press
  summaries)*. 2000-2009. **Before FDA decisions, price changes did not differ between approvals
  (41) and rejections (9).** Before phase III results they did (post hoc: +9.4% vs -4.5%, P = .03).
  The DOI is listed as both 10.1093/jnci/djr310 and djr338 by different sources; check before
  citing.
- Sarkar & de Jong, *Quarterly Review of Economics and Finance* 46(4):586-597, 2006, "Market
  response to FDA announcements" *(bibliography verified; findings secondary)*: reactions at four
  stages of review; negative signals move prices more than positive ones.
- Neuhierl, Scherbina & Schlusche, *JFQA* 48(4):1207-1240, 2013, "Market reaction to corporate
  press releases" *(verified, abstract)*: 285,917 wire releases 2006-2009; FDA approvals are among
  the categories with the largest positive reactions.
- Wu, Borochin & Golec, *J. Corporate Finance* 84, 2024 (online Nov 2023), DOI
  10.1016/j.jcorpfin.2023.102495, "Informed options trading before FDA drug advisory meetings"
  *(verified, abstract)*, and Bohmann & Patel, *JBFA* 49(7-8):1211-1236, 2022, "Informed options
  trading prior to FDA announcements" *(verified, abstract)*: abnormal option activity predicts the
  outcome, strongest for small firms. `literature.md` §3 already records that stock volume shows no
  such footprint.
- GitHub event study (234 novel approvals 2015-2024) *(practitioner, already in `literature.md`
  §12)*: small caps react strongly and "usually start rising before approval", yet no single day
  from -5 to -1 is significant; run-up size vs reaction is weakly negative (p = 0.07). Small caps
  +8.5% with 36% negative.
- BiopharmaWatch (vendor, 1,191 approvals and 231 CRLs since 2015) *(vendor, unaudited)*: about one
  approved stock in five rose >= 20% in the 90 trading days before approval; median two-day move
  around approval about zero; stocks up >= 30% in 60 days before a rejection had a median drop of
  about 25%. This is the "sell the news" pattern in numbers, but unaudited.

**Reading.** The date is knowable; the direction is not, and the run-up, where it exists, is weeks
long, not overnight. For this study the value of a PDUFA calendar is **precision on D and D+1**:
it says "a binary event can land tonight", which a volume detector cannot see.

**PIT data.** Vendor calendars (BiopharmCatalyst, RTTNews, BiopharmaWatch, fdatracker) show today's
view, with revised and early-action dates written over old ones; using them historically is
lookahead. The PIT route is the issuer's own text: regex "PDUFA" plus a date in news items already
in `GapperStudyCatalysts` (Alpaca/Benzinga, including the fiscal.ai wire copies) and in 8-K/10-Q
text, keeping the latest stated date published before D-1 16:00. FDA's Drugs@FDA gives the actual
action date afterwards (label, not feature).

**Features.** `x.cal.pdufaInDays` (calendar days from D to the latest stated PDUFA date known at
D-1 16:00, null if none), `x.cal.pdufaWithin1` (date in [D-1, D+1]), `x.cal.pdufaWithin7`.

**Mapping.** H2 (Fda group), `catalystType = Fda`. New: a *pre-registered* biotech watchlist.

**Rating: worth testing**, as a lift on `isTier20Gapper` for the window [D-1, D+1] and a
descriptive count of how many Tier20 gappers had a stated PDUFA date within 7 days. Expect high lift
and a tiny share of all gappers (a few dozen small-cap PDUFA events a year against thousands of
gapper days; the exact count is unknown and is what the test would report). Effort: low-medium (text
extraction over data held, no new pulls).

### C2. FDA advisory committee meetings

**What.** Public meetings where outside experts vote on an application. 21 CFR 14.20 requires a
Federal Register notice at least 15 days before the meeting (late notices must explain why)
*(regulation text via secondary site; check eCFR)*. So the meeting date is PIT by its Federal
Register publication date.

**Evidence.** Golec & Vernon, NBER Working Paper 14932, 2009 *(verified, abstract)*: meetings have
weak or insignificant stock-price effects on average. Wu, Borochin & Golec 2024 (above): about 32%
of 319 drug meetings 1997-2017 show abnormal call volume beforehand, strongest for small firms.
BiopharmaWatch's figure that approval followed 97% of positive votes and 67% of negative votes
(2010-2021) is attributed to a JAMA Health Forum study that was not opened *(unverified)*.

**PIT data.** Federal Register documents API (federalregister.gov, keyless) filtered by agency FDA
and "advisory committee" *(API exists by general knowledge; the docs page refused automated fetch
in this pass: unverified)*. The notice names the drug and sponsor; mapping sponsor to ticker is
manual or fuzzy.

**Features.** `x.cal.adcomWithin1`.

**Rating: descriptive only.** Few drug AdComs per year and fewer for sub-$20 sponsors; the
evidence says the stock does not move ahead of them. Cheap, but low value per hour (the same
verdict `avenues.md` reached).

### C3. Clinical-trial readouts

**What.** Topline phase 2/3 results are the single most common large biotech gap (both directions).
The date is not scheduled; companies guide a window ("topline in Q3", "mid-2024").
ClinicalTrials.gov records a `primaryCompletionDate`: the last patient's last primary-outcome
visit, which precedes topline by weeks to months.

**Evidence.**
- Rothenstein et al. 2011 (above): before phase III results, positive-trial stocks rose and
  negative-trial stocks fell (+9.4% vs -4.5%, post hoc); the accompanying JNCI commentary
  (Feuerstein & Ratain, "Oncology micro-cap stocks: caveat emptor!") reads this as a small-cap
  effect rather than insider knowledge *(secondary)*.
- PLOS ONE 2024 biopharma study (already `literature.md` §12): product-development releases show
  pre-release abnormal returns.
- Registry dates go stale: a 2015 Northwest Biotherapeutics 8-K exhibit says its ClinicalTrials.gov
  completion date was "very out of date" and was changed to match what it had told investors
  *(verified, primary SEC document)*. Anecdote, but it shows the registry is a weak clock.

**PIT data.** ClinicalTrials.gov keeps every record version (the "record history" archive) *(NLM
support article verified)*; whether the modernized v2 API exposes versions officially is
**unverified** (third-party tools describe using undocumented history endpoints). Without versions,
today's dates are lookahead. Sponsor-to-ticker mapping is also needed. Issuer guidance text in news
is the cheaper PIT source but only gives quarters.

**Features.** `x.cal.pcdPassedDays` (days since the latest PIT primary completion date of a
phase 2/3 trial sponsored by the issuer, with no "topline" release since), `x.cal.toplineGuidedQ`
(issuer guided topline in the current quarter, from news text).

**Rating: descriptive only.** The window is too wide to put a name on a one-day watchlist; at best
it lifts the prior for "biotech with a pending readout". Medium-high effort (registry history,
sponsor mapping). Revisit only if C1 shows biotech gappers dominate Tier50.

### C4. Earnings dates for small caps

**What.** Earnings are scheduled, and most small caps announce the date 1-3 weeks ahead by press
release ("X to report Q3 results on ..."). Premarket releases produce gaps on D; after-close ones
gap on D+1.

**Evidence.**
- Frazzini & Lamont, NBER WP 13090, 2007, "The earnings announcement premium and trading volume"
  *(verified, abstract)*: prices rise around scheduled announcements, tied to volume surges and
  attributed to small-investor attention. CRSP 1972-2004, all caps.
- Michaely, Rubin & Vedrashko, *Review of Finance* 18(6):2003-2044, 2014 *(verified, abstract)*:
  with time-stamped newswire data, earnings released inside trading hours get about **half** the
  immediate reaction of those released outside; worse news tends to come during trading hours.
  Same authors, *JAE* 62(1):24-45, 2016: the worst news comes Friday evening.
- Jiang, Likitapiwat & McInish 2012 (already `methods-onset.md` §3.3): most price discovery right
  after the release, S&P 500.
- Tripathi & Rengifo, *JRFM* 18(2):75, 2025 *(verified, abstract)*: announcements in thin extended
  hours have larger price impact.
- No source gives the share of >= 20% small-cap gappers that are earnings days. The study can
  report it from `catalystType = Earnings`.

**PIT data.** Vendor calendars overwrite estimated dates with actual ones (one scraper vendor says
so outright), so they are lookahead. Two PIT routes on data already held:
1. **Seasonal estimate.** From `EdgarFilingIndex`, the issuer's past 8-K item 2.02 acceptance times.
   Expected date for this quarter = the same quarter last year + 364 days (or the prior quarter +
   91), using only filings accepted before D. Also gives the usual session (pre-open vs after-close)
   from the acceptance hour.
2. **Announced date.** Regex "to report|will report|to announce ... results on <date>" in news items
   published before D-1 16:00.

**Features.** `x.cal.earnExpectedWithin1` (seasonal estimate in [D-2, D+1]),
`x.cal.earnAnnouncedForD` (a stated date equal to D, or D-1 with an after-close history),
`x.cal.earnSessionPre` (share of past 2.02s accepted 06:00-09:30).

**Mapping.** Catalyst block (`Earnings`), H2 (earnings is not in H2's continuation group, but
Kargarzadeh et al. in `literature.md` §12 say earnings coverage continues). New: earnings as a
**pre-known** flag, so a detector can be scored separately on "expected" vs "surprise" mornings.

**Rating: worth testing.** Cheap (no new pull), PIT, and it splits the population in a way that
matters for H11: if a large share of early-alarm wins are earnings mornings, the "edge" is a
calendar, not a detector. Expect high lift on gap *probability*; direction stays unknown.

### C5. Investor conferences

**What.** Small-cap banks' conferences (Roth, H.C. Wainwright, LD Micro and others); issuers announce
presentation slots days ahead.

**Evidence.** Bushee, Jung & Miller, *JAR* 49(5):1163-1192, 2011, "Conference presentations and
the disclosure milieu" *(verified, abstract)*: reactions vary with conference sponsor, location,
size and focus; the sample is not micro-cap and the effects are not gap-sized.

**Rating: not worth.** No evidence of 20% moves; presentations rarely carry new material news
(Reg FD pushes it into a release, which the catalyst block already sees).

### C6. Reverse-split effective dates

**What.** Shareholder approval (DEF 14A), then a board-set ratio and an effective date announced a
few days ahead (8-K item 5.03 and/or 3.03, a press release, and the exchange daily list). The
post-split float is smaller by the ratio.

**Evidence.** Martell & Webb, *Review of Quantitative Finance and Accounting* 30(3):253-279, 2008
*(bibliography verified; findings not read)*. Kim, Klein & Rosenfeld 2008 *(secondary)*: negative
abnormal returns over three years. A 1981-2010 conference paper (IAES) reports negative effective-
month returns *(secondary)*. No peer-reviewed study of the practitioner "post-split low-float
squeeze" (2022-2024) was found.

**PIT data.** Effective date from 8-K text accepted before D, or from Alpaca corporate actions if
the declaration/announcement date is stored (whether `AlpacaSplitSource` keeps it was not checked;
the protocol uses the ex-date only).

**Features.** `x.cal.rsEffectiveInDays` (days until a known effective date, negative after),
`x.cal.rsRatio`.

**Mapping.** H7 (prior 30 days, fade). **Important:** the protocol excludes every symbol-day whose
split ex-date falls in [D-25, D+1] (`protocol.md`, universe rule, the unadjusted-split guard). So
the effective day and the next 25 sessions, exactly where a "post-split low-float squeeze" would
sit, are not in the sample at all, and H7's "within 30 days" can only see days 26-30 after a split.
Studying C6 needs a separate, split-adjusted sample for those days (outside the frozen population,
explore-only), not a new feature on the existing one.

**Rating: descriptive only.** Cheap as a feature, but blocked by the exclusion window; worth a note
in the write-up that H7 effectively tests days 26-30 only.

### C7. Russell reconstitution

**Evidence.** Dimensional (vendor research, 2017-2023): Russell 2000 migrating additions +1.2% over
the 20 days before reconstitution, then -0.95%; volume 10-12x on the day *(vendor; verified page)*.
Madhavan 2003 and Petajisto were not retrieved *(unverified)*.

**Rating: not worth.** One date a year, effects of about 1%, closing-auction volume rather than a
premarket gap. Microcaps that enter the Russell 2000 or Microcap index are rarely sub-$1.

### C8. IPO lock-up expiry

**Evidence.** Field & Hanka, *JF* 56(2):471-500, 2001 *(verified, abstract)*: 1,948 lockups
1988-1997; volume +40% permanently, three-day abnormal return -1.5%, worse for VC-backed firms.

**Rating: not worth as a gapper cause.** The expected sign is down. A possible moderator for the fade
leg (supply arriving): `x.cal.lockupExpiryWithin5` from IPO date + lock-up days (usually 180, stated
in the 424B4). Low priority.

### C9. SPAC votes and de-SPAC closings

**What.** The merger vote date is set in the DEFM14A; the redemption deadline is two business days
before; the close comes days later (8-K items 2.01 and 5.07). Very high redemptions left tiny
floats in 2021-2022.

**Evidence.** Klausner, Ohlrogge & Ruan, *Yale J. on Regulation* 39:228, 2022, "A sober look at
SPACs" *(verified via the authors' summaries)*: 47 mergers 2019-2020; mean/median redemptions 58% /
73%; median post-merger return -14.5% at three months, worse later. Follow-up "Was the SPAC crash
predictable?", 40 Yale J. on Reg. Bulletin 101, 2023 *(verified, PDF listing)*. The 2021 low-float
de-SPAC spikes (redemptions 91-97%) are documented only in newsletters and forums
*(anecdotal)*.

**Features.** `x.cal.deSpacDaysSinceClose`, `x.cal.redemptionPct` (from the 8-K or press release
before D).

**Rating: descriptive only.** Mostly a 2021-2022 regime, which sits in train and would not
generalize to validation/holdout. Needs more EDGAR form types (DEFM14A, 425), so it would wait for
the rate gate anyway.

### C10. Uplisting date

**Evidence.** Older listing studies: about +5% abnormal return at the announcement of OTC-to-NYSE
listing (270 firms, 1980s, Purdue dissertation) and smaller effects after Nasdaq's arrival (Sanger
& McConnell) *(secondary, pre-2000 market)*.

**PIT data.** Form 8-A12B and exchange CERT filings in `form.idx` (PIT by acceptance).

**Rating: not worth here.** The first exchange-listed day has no prior SIP close on this stack
(Alpaca's SIP history covers listed trading; OTC prior closes were not checked), so the day is not
a candidate under the protocol's ">= 20% over prior close" definition. Uplisting can still appear
as a news type (`catalystType = Uplisting`) on later days, which is already covered.

---

## Part 2. The news-latency pipeline

### What is known about when releases hit

- **Exchange pre-notice rules set the clock.** Nasdaq IM-5250-1 (from 2009): listed companies must
  notify Nasdaq MarketWatch at least 10 minutes before releasing material news between 7:00 a.m.
  and 8:00 p.m. ET, and **before 6:50 a.m.** for releases outside those hours *(verified, SEC
  filings SR-NASDAQ 34-61521 and the 2026 exhibit 34-104563)*. NYSE Listed Company Manual 202.06
  (from 2015-09-28): call NYSE at least 10 minutes before material news between 7:00 a.m. and
  4:00 p.m. ET *(verified, law-firm summaries)*. So a premarket release is known to the exchange 10
  minutes early, and the exchange can halt (T1) the stock before it crosses. A1's halt RSS already
  captures that timestamp.
- **Clustering on the hour.** RealWire (50,000 releases, March 2011): about a quarter were sent
  exactly on the hour *(vendor study)*. PR Gun (2026, 22,167 time-stamped releases across ten
  wires): noon most common, 41% between 11:00 and 15:00 *(vendor study; all releases, not
  issuer IR)*. A PR Newswire listing page read in this pass showed minute-precision "HH:MM ET"
  times; 6 of 25 sat on :00 or :30 (a weekend sample, anecdotal). **No source measures the
  distribution of small-cap issuer releases between 04:00 and 09:30.** The practitioner belief in
  07:00 / 08:00 / 08:30 clusters is unverified; the study can measure it (N1).
- **Earnings timing.** Michaely, Rubin & Vedrashko 2014/2016 (above) use newswire time stamps and
  show strategic timing (outside hours, Friday evening). White & Case: the earnings release
  "typically" goes out early morning on the call day, with the 8-K the same day *(law-firm
  checklist)*.
- **EDGAR.** EDGAR accepts filings 06:00-22:00 ET; most live filings started after 17:30 get the
  next business day's date and **are not disseminated until the next business day**; Section 16
  forms and a few others are exceptions *(verified, sec.gov "Determine the status of my filing")*.
  Public posting follows acceptance by about 40 s (Rogers, Skinner & Zechman, *JAR* 55(2):459-505,
  2017, "Run EDGAR run", *verified*; prices moved about 30 s before posting while PDS subscribers
  had a head start, since removed). An 8-K with the press release attached usually comes at or after
  the wire time, so EDGAR is a backstop, not a lead, except for 6-Ks of foreign issuers that may
  have no US wire release (A2).

### What is known about feed delays

- **Alpaca news is Benzinga** ("all news data is currently provided directly by Benzinga",
  Alpaca docs, *verified*). Two forum threads say the real-time stream carries Benzinga newsroom
  items, not raw press releases *(anecdotal, already in `literature.md` §13)*.
- **This stack's own data contradicts "no press releases" for history.** `CatalystStampKind`
  classifies Alpaca items sourced `fiscal.ai` or linking to globenewswire/prnewswire/businesswire/
  accesswire/newsfile as **batch-stamped** (time is an upper bound), and Benzinga newsroom items as
  real-time; `GapperFormationFeatures` clusters a Benzinga flash with its wire copy. So the history
  holds wire copies, but with unreliable times.
- **Benzinga vs other vendors.** Benzinga claims <0.1 s API latency (vendor). A review site's
  90-day test puts Benzinga Pro about 8 s behind Bloomberg; another says TradeTheNews is "a few
  seconds" faster *(unaudited reviews)*. A forum user reports Benzinga headlines 2-4 s late or after
  the price move *(anecdotal)*. QuoteMedia says wire releases can take up to 1 minute to appear in
  its apps *(vendor support page)*. **No independent measurement of Benzinga vs wire delay was
  found.**
- **Direct wire feeds.** Business Wire stopped licensing direct feeds to high-frequency firms in
  February 2014 after a WSJ report; PR Newswire said it did not offer them *(trade press, 2014;
  current status unverified)*.

### How fast prices react

- Busse & Green, *JFE* 65(3):415-437, 2002, "Market efficiency in real time" *(verified,
  abstract)*: CNBC mentions move prices within seconds; positive reports fully priced within one
  minute; trading intensity doubles in the first minute; only trades within 15 s of the mention
  profit. Large caps, regular hours, 2000.
- Groß-Klußmann & Hautsch, *J. Empirical Finance* 18(2):321-340, 2011 *(verified, abstract)*:
  20-second VAR on LSE stocks; relevant news moves returns, volume and spreads; widening spreads
  after news eat the profit of trading on it.
- Aït-Sahalia, Li & Li 2024 (already `literature.md` §10): relevant news produces a jump within
  about 2 minutes. Rogers et al. 2017: prices react within about 30 s of a filing reaching any
  trader.
- arXiv 2601.08962 (already `literature.md` §13): faster post-earnings price discovery after 2016;
  a 15-minute initial-adjustment window, with after-close reactions faster in the first minutes and
  pre-open ones larger by the end of the session *(abstract summary)*.
- **No study measures premarket reaction speed for sub-$20 small caps.** Amendment 02's H19 (share
  of the move before publication) and H20 (decay over 1/2/5/10/15 minutes) are that measurement.

### What a live pipeline would need, and the margin over a scanner

A "top gainers" scanner fires only after the price has moved (for example, last price >= +10% over
the prior close, or top-N rank by premarket change). A news pipeline can be earlier only by the time
between publication and that first price threshold. From the evidence above, for liquid stocks that
gap is seconds to about 2 minutes. In thin premarket small caps it may be longer, because the
first prints need a seller, but no one has measured it.

Budget for a pipeline that could beat a scanner:

| stage | target | stack today |
|---|---|---|
| source | wire publication + <= 2-5 s | Alpaca/Benzinga stream: Benzinga-written flashes real-time; wire copies may lag (size unknown, N3); EDGAR ~40 s after acceptance, often after the wire |
| parse + classify | <= 1 s | `CatalystPreClassifier` keyword rules are fast; an LLM call is seconds and should not gate the order |
| decide + route | <= 1 s | premarket limit orders only; spreads 3-4x daytime (Barclay & Hendershott 2004) |
| total | <= 5-10 s | unknown until N1-N3 are measured |

Without a direct wire connection, the realistic edge is the **Benzinga flash vs the scanner's
first threshold minute**, and it is only worth anything if H20's decay curve shows the 1-minute
entry still nets a profit after the frozen cost model. If H19 shows most of the move comes before
publication, no pipeline helps.

### N1. Repair wire release times (do first)

**What.** For every batch-stamped item, the stored URL points at the wire page, which shows the
release time to the minute in ET (verified for a PR Newswire listing: "15:01 ET"; GlobeNewswire
refused automated fetch in this pass, so its page format is **unverified**). One fetch per release
from the wire site (not SEC or Alpaca, so outside the current rate gate) turns upper-bound stamps
into real times. Respect each site's terms and pace at about 1 request/second.

**Why.** It fixes the H11 benchmark and amendment 02 (which currently **excludes** batch-stamped
items) for exactly the press releases small caps live on. It also yields the first measured
distribution of small-cap release times 04:00-09:30, which tests the 07:00/08:00/08:30 belief and
feeds P3's clock-time prior.

**Features.** `x.news.wireTimeEt` (repaired publication minute), `x.news.minuteOfHour`,
`x.news.onRoundMark` (:00 or :30).

**Rating: worth testing.** Low effort, removes the largest known measurement error in the catalyst
block. Report what share of batch items could be repaired.

### N2. Scanner-lead measurement

**What.** On Tier20 mornings with a news item, compute
`scannerLeadMin = t(first minute pmRet >= +10%) - t(publication)` (and for +5% and top-20 rank in
U(D)), using N1's repaired time where available. Negative means the price got there first.

**Why.** It is the number the user asked for: "by how many minutes would a news pipeline beat a
gainers scanner". It sits on the same axis as P0 (alarm-time evaluation in `methods-onset.md`) and
complements H19 (share of move) with a time.

**Rating: worth testing (descriptive).** No new data beyond N1. Report the median and IQR by
`sourceTier`, `catalystType` and time of day, day-clustered intervals.

### N3. Benzinga vs wire vs EDGAR delay

**What.** For clusters with both a Benzinga flash and a repaired wire time (N1), and for 8-Ks
matched to a release, report `benzingaMinusWireSec` and `edgarMinusWireSec`. Minute precision on
the wire side limits this to "same minute / 1-2 minutes / later".

**Rating: worth testing (descriptive).** It tells whether a live system needs its own wire
monitoring or whether Alpaca's stream is close enough.

### N4. Exchange pre-notice and T1 halts

The 10-minute pre-notice rule means a material premarket release can produce a T1 halt on the
listing exchange with a second-stamped time, sometimes before the stock trades on the news.
**Already covered** by A1 (`newsHaltPm`); this note only adds the rule that explains why.

---

## Ranked shortlist

| rank | item | type | cost | rating | why |
|---|---|---|---|---|---|
| 1 | **N1 repair wire release times** | data fix | low (one wire-page fetch per batch item; not SEC/Alpaca) | worth testing | Batch stamps are excluded from amendment 02 and blur H11; repaired minute times fix both and give the first measured small-cap release-time distribution. |
| 2 | **N2 scanner-lead minutes** (+ N3 feed delays) | descriptive | very low after N1 | worth testing | Directly answers "how many minutes over a gainers scanner"; same axis as P0 and H19/H20. |
| 3 | **C4 earnings calendar, PIT** (seasonal 8-K 2.02 estimate + "to report on" text) | feature | low (data held) | worth testing | Splits mornings into expected vs surprise; if early-alarm wins are earnings mornings, the edge is a calendar. |
| 4 | **C1 PDUFA dates from issuer text** | feature | low-medium (text over data held) | worth testing | The cleanest scheduled binary event; high precision, low recall; vendor calendars are lookahead. |
| 5 | **C6 reverse-split effective dates** | finding + feature | very low | descriptive only | The protocol's [D-25, D+1] split exclusion hides the post-split window entirely, so H7 can only see days 26-30; a separate explore-only sample would be needed. |

**Descriptive only:** C2 AdComs, C3 trial readouts (needs registry version history), C9 de-SPAC
(2021-22 regime). **Not worth:** C5 conferences, C7 Russell reconstitution, C8 lock-ups (sign is
down), C10 uplisting (not a candidate day on this stack). **Already covered:** N4 (A1 halts).

**Honest prior.** The scheduled causes worth having (earnings, PDUFA) tell *when* a stock may gap,
not which way; the literature finds no stock-price leak before FDA events and only small attention
premia before earnings. They should raise precision of a watchlist, not create lead time on
unscheduled news, which remains the bulk of small-cap gappers. On the news side, every measured
price reaction is seconds to two minutes; a pipeline without a direct wire feed should expect a
lead of a minute or two at best, and H20 decides whether that pays.

## Sources (verified in this pass: record and quoted finding, usually abstract level)

- Rothenstein, Tomlinson, Tannock & Detsky 2011, *JNCI* 103(20):1498-1506 — https://academic.oup.com/jnci/issue/103/20 (DOI given as djr310 and djr338 by different sources)
- Sarkar & de Jong 2006, *QREF* 46(4):586-597 — https://ideas.repec.org/a/eee/quaeco/v46y2006i4p586-597.html (bibliography only)
- Neuhierl, Scherbina & Schlusche 2013, *JFQA* 48(4):1207-1240 — https://ideas.repec.org/a/cup/jfinqa/v48y2013i04p1207-1240_00.html
- Wu, Borochin & Golec 2024, *J. Corporate Finance* 84, DOI 10.1016/j.jcorpfin.2023.102495 — https://clsbluesky.law.columbia.edu/2022/01/11/informed-options-trading-before-fda-evaluations/
- Bohmann & Patel 2022, *JBFA* 49(7-8):1211-1236 — https://ideas.repec.org/a/bla/jbfnac/v49y2022i7-8p1211-1236.html
- Golec & Vernon 2009, NBER WP 14932 — https://www.nber.org/papers/w14932
- Frazzini & Lamont 2007, NBER WP 13090 — https://www.nber.org/papers/w13090
- Michaely, Rubin & Vedrashko 2014, *Review of Finance* 18(6):2003-2044 — https://econpapers.repec.org/RePEc:oup:revfin:v:18:y:2014:i:6:p:2003-2044.
- Michaely, Rubin & Vedrashko 2016, *JAE* 62(1):24-45 — https://ideas.repec.org/a/eee/jaecon/v62y2016i1p24-45.html
- Tripathi & Rengifo 2025, *JRFM* 18(2):75 — https://www.mdpi.com/1911-8074/18/2/75
- Bushee, Jung & Miller 2011, *JAR* 49(5):1163-1192 — https://repository.upenn.edu/accounting_papers/61
- Field & Hanka 2001, *JF* 56(2):471-500 — https://ideas.repec.org/a/bla/jfinan/v56y2001i2p471-500.html
- Klausner, Ohlrogge & Ruan 2022, 39 *Yale J. on Reg.* 228 — https://corpgov.law.harvard.edu/2020/11/19/a-sober-look-at-spacs/ ; Klausner & Ohlrogge 2023 — https://www.yalejreg.com/bulletin/was-the-spac-crash-predictable/
- Busse & Green 2002, *JFE* 65(3):415-437 — https://econbiz.de/Record/market-efficiency-in-real-time-busse-jeffrey/10006508942
- Groß-Klußmann & Hautsch 2011, *J. Empirical Finance* 18(2):321-340 — https://econpapers.repec.org/RePEc:eee:empfin:v:18:y:2011:i:2:p:321-340
- Rogers, Skinner & Zechman 2017, *JAR* 55(2):459-505 — https://econpapers.repec.org/RePEc:bla:joares:v:55:y:2017:i:2:p:459-505
- Nasdaq 10-minute / 6:50 a.m. pre-notice — https://www.sec.gov/file/34-61521 ; https://www.sec.gov/files/rules/sro/nasdaq/2026/34-104563-ex5.pdf
- NYSE 202.06 timely alert 7:00-16:00 — https://hunton.com/insights/legal/nyse-expands-material-news-notification-policy-and-trading-halt-authority-effective-september-28-2015
- EDGAR hours and 17:30 cutoff — https://www.sec.gov/page/edgar-how-do-i-determine-status-my-filing
- Alpaca news provided by Benzinga — https://docs.alpaca.markets/us/docs/streaming-real-time-news.md
- Northwest Biotherapeutics 8-K exhibit (stale registry date) — https://www.sec.gov/Archives/edgar/data/0001072379/000114420415058675/v421895_ex99-1.htm
- NLM: ClinicalTrials.gov record history in the archive — https://support.nlm.nih.gov/knowledgebase/article/KA-03861/en-us
- Dimensional, Russell reconstitution — https://www.dimensional.com/us-en/insights/even-migrating-stocks-face-index-reconstitution-costs (vendor)
- PR Newswire listing with "HH:MM ET" times — https://www.prnewswire.com/news-releases/news-releases-list/
- Business Wire ends direct feeds to HFT (2014) — https://www.odwyerpr.com/story/public/1934/2014-02-21/business-wire-pulls-plug-high-speed-traders.html (trade press)

## Sources not verified (do not cite in results until checked)

- **BiopharmaWatch** approval/CRL statistics (1-in-5 run-up >= 20%, ~0 median two-day move, ~25%
  median drop after run-up into rejection) and its attribution of 97%/67% vote-to-decision rates to
  a JAMA Health Forum study — vendor pages, unaudited; the JAMA article was not opened.
- **GitHub FDA event study** (234 approvals) — practitioner, already in `literature.md`.
- **Martell & Webb 2008, Kim, Klein & Rosenfeld 2008**, the IAES 1981-2010 reverse-split paper —
  bibliographies or summaries only.
- **Madhavan 2003, Petajisto** (index reconstitution) — not retrieved.
- **Uplisting studies** (Purdue dissertation, Sanger & McConnell) — summaries only, pre-2000.
- **21 CFR 14.20** 15-day notice — text via a third-party site; check eCFR.
- **Federal Register API** — exists by general knowledge; docs page blocked automated fetch.
- **ClinicalTrials.gov v2 version-history endpoint** — not confirmed in official docs.
- **GlobeNewswire / Business Wire / Accesswire page time formats** — not fetched (GlobeNewswire
  returned 403). Whether wire pages show the original or an updated time after corrections is
  unknown.
- **Benzinga latency figures** (+8 s vs Bloomberg, "a few seconds" vs TradeTheNews, 2-4 s forum
  reports) — unaudited reviews and forums. QuoteMedia "up to 1 minute" — vendor support page.
- **RealWire 2011 and PR Gun 2026** release-time studies — vendor studies of all releases, not
  issuer investor-relations news.
- **Practitioner 07:00 / 08:00 / 08:30 clustering** of small-cap releases — no source found; N1
  measures it.
- **arXiv 2601.08962** BMO/AMC adjustment details — abstract summary only.
- **Feuerstein & Ratain JNCI commentary** reading — via a Nature blog summary.
- **2021 de-SPAC redemption/spike figures** — newsletters and forums only.
