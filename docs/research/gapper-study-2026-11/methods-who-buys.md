# Gapper study: who buys, and how do they know? Leakage, promotion, reverse-split squeezes and herding (IP-T489)

A web pass on the user's question of 2026-11-09: "How is it that people know to suddenly buy all this
stock in a no-name company for just one day? If this isn't insider trading, it's pretty damn close."
It complements `literature.md` §3 (pre-announcement run-ups), §4 (pump-and-dump), §14 (social media),
`methods-catalysts.md` C6 (reverse-split dates) and Part 2 (news latency), and `methods-short.md` §3
(SEC work, shorting promoted names). Those are referenced, not repeated.

The five examples are the user's, from the 2026-11-09 tape. I did not verify any of them (no market
data or filing was fetched), so each is treated as a *case to explain*, not as a fact:

| ticker | as reported | first-pass reading from this pass |
|---|---|---|
| OMH | 1:50 reverse split in August, $40M offering prospectus, +520% on 11-04 with no premarket trades, +300% on 11-09 after volume ignited 07:45 ET | reverse split + dilution financing + scanner/retail herding (§3, §4); an offering prospectus is a reason the issuer wants a high price |
| WCT | 1:10 reverse split effective 11-09, +99% | public, calendared mechanical event (§3); effective date was public days earlier |
| NWGL | ADS ratio change equal to 1:80 reverse split, +22% on 52.5M shares | same family as a reverse split; the underlying shares did not change (§3) |
| CKX | $1B investment led by Brad Jacobs | a real, public catalyst; leakage is testable by the pre-publication run (§1) |
| STAK | non-binding LOI with an unnamed buyer | a low-information press release; the classic promotion-or-leak ambiguity (§1, §2) |

Labels: **peer-reviewed**, **working paper**, **regulator/exchange** (SEC, DOJ, FINRA, Nasdaq, FBI, FDIC
OIG), **law firm / press** (secondary), **vendor/anecdotal**. *(abstract/snippet)* means I read only an
abstract, search summary or press release. *(secondary)* means a number came through a third party.
*(not verified)* means do not cite it in results. Today is 2026-11-09. Times are ET. No Alpaca, SEC or
idiotproof API or CLI was called; everything is web search and fetch only. Every parameter below is a
candidate for a registered amendment, on train first. None is a finding.

**Reading note.** The search tool summarises pages with a small model, so figures that came only from a
summary are marked *(snippet)* even when the source is good. Re-read the source before any number goes
into a results report. Several sec.gov pages could be fetched directly; most statute and rule text could
not, and is marked.

---

## Summary: what the evidence says about "how do they know"

There is no single answer. Four different mechanisms produce the same chart (a no-name stock up several
hundred percent, one session, then gone), and they have different footprints and different legality.

1. **Public, calendared mechanical events** (reverse splits, ADS ratio changes). The information is not
   secret. Nasdaq requires the issuer to notify the exchange 10 calendar days ahead and announce
   publicly at least two business days ahead (§3). Many people "know" because the effective date is on
   an 8-K, a press release and an exchange list. What looks sudden is a scanner and a thin float.
2. **Coordinated promotion** (ramp-and-dump, touting, social-media "investment clubs"). Here the buyers
   are told when to buy, by someone who already holds the stock. The SEC, FBI, FINRA and DOJ say this is
   now the dominant manipulation pattern in tiny Nasdaq listings, mostly Asia-linked recent IPOs (§2).
3. **Information leakage** (insider trading or tipping). Peer-reviewed work finds it is real and
   pervasive around takeovers, through options and strong social ties, and that stock-volume proxies
   detect it poorly (§1). Evidence on press-release timing in micro-caps is thin.
4. **Herding on public rank lists** (top gainers, scanners, Robinhood Top Movers). A price rise is itself
   the signal. This is a feedback loop, not information, and it reverses (§4).

None of these needs a secret. Most of the "knowing" in the user's five examples can be explained by
mechanisms 1, 2 and 4 without any insider. Mechanism 3 cannot be excluded for CKX and STAK, but it is
testable from the study's own 5-year data (see "What to test" in §1): that is the 37-43% of events that
had already run 5% or more before publication.

---

## 1. Information leakage and pre-announcement trading

### 1.1 Academic evidence

- **Augustin, Brenner & Subrahmanyam (2019, *Management Science* 65(12), 5697-5720), "Informed options
  trading prior to takeover announcements: insider trading?", peer-reviewed** *(abstract/snippet)*.
  Target-company equity options before **1,859 U.S. takeovers, 1996-2012**. About a quarter of deals
  show abnormal pre-announcement option volume, concentrated in short-dated out-of-the-money calls; the
  abstract says over half of it is unlikely to be explained by speculation, rumours, insiders, leakage
  through other channels, deal predictability or activist filings. The SEC litigated about **8%** of the
  deals in the sample. The authors say the trading is consistent with informed trading, which includes
  research as well as inside information; a NYU Stern summary that says a quarter of deals "involve
  insider trading" goes further than the abstract. Already in `literature.md` §3; options flow is not
  available here, which is why it is not repeated as a feature.
- **Ahern (2017, *J. Financial Economics* 125(1), 26-47), "Information networks: evidence from illegal
  insider trading tips", peer-reviewed** *(abstract, snippet and a Dando Diary summary, secondary)*.
  **183 networks, 1,139 tips, 622 insiders, about $928M of illegal profit**, from SEC and DOJ cases filed
  2009-2013. Tips travel along strong personal ties (family, friends, same neighbourhood). Mergers are
  51% of tips and earnings 26% *(secondary)*. Relevance: the tipping chain is people, not wire services,
  so it leaves a footprint in the target's tape before publication rather than in any public feed.
- **Ahern (2020, *Review of Asset Pricing Studies*; NBER w24297), "Do proxies for informed trading
  measure informed trading? Evidence from illegal insider trades", peer-reviewed** (already in
  `literature.md` §3). Stock-volume proxies detect only short-lived information; patient insiders evade
  spread, Amihud and Kyle-lambda detection. This is the honest prior for H6/H11: **a stock-volume
  footprint is a weak detector of leakage, by the best test available.**
- **Rogers, Skinner & Zechman (2017, *J. Accounting Research* 55(2), 459-505), "Run EDGAR run: SEC
  dissemination in a high-frequency world", peer-reviewed** *(abstract, snippet)*. On Form 4 filings in
  2012-2013, just over half reached paying subscribers of the SEC's public dissemination feed before they
  appeared on EDGAR, on average **about 18 seconds earlier**; prices, volume and spreads reacted about 30
  seconds before the public posting in those cases. The SEC said in December 2014 it would remove the
  advantage. Relevance: legal early access to *filings* is a documented seconds-scale lead; it is not a
  minutes-scale one.
- **Prevoo & ter Weel (2010, *De Economist*; CPB discussion paper), peer-reviewed** *(snippet)*. About
  5,000 announcements; pre-announcement abnormal returns and volume fell after the EU Market Abuse
  Directive took effect, but did not vanish; **small-cap firms and alliance/takeover/M&A news drive the
  effect**. A Bank of Canada working paper on 420 Canadian takeovers reads its pre-bid run-up as fitting
  market anticipation better than illegal trading *(snippet, working paper)*. An ASX study of unscheduled
  announcements (Heggen, Gannon & Van Eekelen 2007) treats pre-announcement abnormal returns as leakage
  evidence *(snippet, working paper)*. These are daily-window designs: they cannot say whether the run
  began a minute, an hour or a day before the release.
- **Keown & Pinkerton (1981); Meulbroek (1992)** are in `literature.md` §3. Not repeated.

**What the literature does not give us.** I found **no peer-reviewed study of intraday timing between a
micro-cap press release and the trading before it**, none on wire-service or vendor timestamps in
micro-caps, and none that splits a pre-publication run into leakage versus anticipation versus a
scanner-driven run that the release then happens to land on. This study's own 5-year result (about a
third of an hour's move before the headline; 37-43% of events already up 5% or more) is therefore
unusual evidence, and also not attributable: it is consistent with leakage, with a vendor timestamp that
lags the true release (`methods-catalysts.md` N1), with anticipation of a scheduled event, and with
momentum that the news merely joined.

### 1.2 SEC enforcement on micro-cap catalysts

Recent insider-trading actions found (law-firm and press summaries of SEC releases, **regulator
originals not all read**; verify each release number on sec.gov before citing):

- **Kindred Biosciences**: SEC complaint July 2025 against Trijya Vakil and Neeraj Visen; the tippee
  bought 38,000 shares, gain about $109,437; stock rose about 46% on the announcement
  (https://www.sec.gov/enforcement-litigation/litigation-releases/lr-26629 and /lr-26348).
- **US Xpress Enterprises**: settled complaint, July 2025, Brian Kashman; bought 18,200 shares at $1.67
  the day after a meeting with an insider; stock later rose nearly 300% on the offer
  (https://www.sec.gov/enforcement-litigation/litigation-releases/lr-26359).
- **Blue Apron**: settled administrative order, August 2025, David Minson, information from a family
  member; the stock closed above +130% on announcement day (https://www.sec.gov/enforcement-litigation/administrative-proceedings/34-103764-s).
- **Chimerix** (settled order; six accounts before the 2025-03-05 Jazz announcement), **Chinook** and
  **Goodness Growth** were mid-sized, not micro-cap.

What these show: the SEC does bring small-target cases and they are mostly **M&A tips through family and
friends, with trades of five or six figures**, which matches Ahern's network picture. What they do not
show: any case where the catalyst was a *financing, LOI or reverse split* press release in a sub-$10
name. The search found **no SEC action on pre-release trading of an LOI, a PIPE or an ADS ratio change**.
That is absence of a found case, not evidence of absence.

For the enforcement filter: an LOI like STAK's ("non-binding, unnamed buyer") is cheap to publish and
unfalsifiable for days, which is exactly the profile the promotion cases in §2 exploit. A leak requires
the release to be *true and material*; a promotion does not.

### What to test (train only, via an amendment)

- **Pre-publication run, by catalyst type.** Reuse the 37-43% pre-run result and split it by catalyst tag:
  M&A / strategic investment / LOI, financing (offering, PIPE, ELOC, ATM), reverse split / ADS ratio,
  clinical, earnings, none. Prediction from the literature: the pre-run should be larger in M&A-type
  tags than in scheduled or financing tags. If it is equal across tags, leakage is not the mechanism.
- **Run timing relative to a corrected publish time.** Only after `methods-catalysts.md` N1 repairs wire
  release times: the share of the pre-run that falls in the last 1, 5, 15 and 60 minutes before the true
  first-public timestamp. Leakage predicts concentration near the release; herding and momentum predict
  diffusion earlier.
- **Volume versus price lead.** Ahern (2020) predicts stock volume will not lead for patient information.
  Compute whether 1-minute volume ratio (VolRatioTod) leads price in the pre-run, by tag. Report a null.
- **Placebo.** Same measurement on days with *no* news but the same prior-day gap, so a pre-run created
  by selection (we only look at news that moved) is not read as leakage.

---

## 2. Ramp-and-dump and promotion schemes, 2024-2026

### 2.1 Regulator picture

- **SEC Cross-Border Task Force, announced 2025-09-05** (sec.gov press release 2025-113; the page itself
  returned 404 on direct fetch, content via Cleary Gottlieb, Alston and Mondo Visione, **law firm / press,
  secondary**). Sits in the Division of Enforcement; first priority is investigations of foreign-based
  companies potentially engaged in "pump-and-dump" or "ramp-and-dump" schemes; focus on gatekeepers
  (auditors, underwriters; lawyers per commentators). Chair Atkins: "we will not tolerate bad actors
  whether companies, intermediaries, gatekeepers or exploitative traders" using borders to avoid
  protections.
- **SEC trading suspensions, sec.gov, read this pass.** Smart Digital Group (SDM), Cayman holding company
  with principal offices in Singapore: order dated **2025-09-26**, suspended 04:00 ET 2025-09-29 through
  23:59 ET 2025-10-10, Section 12(k); stated basis, "potential manipulation... effectuated through
  recommendations made to investors by unknown persons via social media" that "appear to be designed to
  artificially inflate the price and volume" (https://www.sec.gov/enforcement-litigation/trading-suspensions/34-104112-ts).
  QMMM Holdings (HK-based) was suspended the same day (34-104113; the title and date confirmed, order
  text not read). Press reported QMMM up about **959% in three weeks** before the halt (oninvest, press,
  *secondary*). Later orders found: **JM Group (Hong Kong), suspended 2026-01-15 to 2026-01-29**; and
  **HCHL** (BVI, HK-headquartered), release 34-105675 dated 2026-06-11, suspended 2026-06-12 to
  2026-06-26 (sec.gov file paths in the sources; order text not read).
- **Counts differ by source.** Bloomberg Government and a law-firm blog say **14 suspensions** since
  September 2025, all foreign issuers on Nasdaq or NYSE; Cooley (April 2026) counts 14 Asia-based
  issuers that listed within the prior two years. An October 2025 Cooley alert counted nine at that date.
  Hong Kong (QMMM, Pitanium, Etoiles Capital, Charming Medical, JM Group, HCHL), Singapore (SDM) and
  Malaysia (a Cayman holding company listed 2025-07-02) are all represented *(law firm, secondary)*.
- **The suspension is not the end of the halt.** SEC suspensions last at most 10 business days, but
  Nasdaq and NYSE have kept stocks halted afterwards until the issuer answers information requests
  (Bloomberg Government, Cooley; *secondary*). A February 2026 Nasdaq proposal would let it delist a
  company under SEC suspension; I did not confirm its approval.
- **FINRA investor alert, 2025-12-09** (read this pass): "investment group" imposter scams. Fall 2023,
  sharp rise in complaints; July 2025 FBI PSA reports **at least a 300% increase in complaints about
  ramp-and-dump fraud versus 2024**. Method: ads on Instagram/Facebook, impersonation of real registered
  professionals (sometimes deepfake video), move to WhatsApp groups, start with a well-known stock, then
  steer to a low-priced thinly traded one, direct victims to a specific broker-dealer with what to buy,
  how much and when, victims cannot sell as it collapses, then pressure for more funds
  (https://www.finra.org/investors/insights/investment-group-imposter-scams). Same-pattern notices from
  the Canadian Securities Administrators (April 2026) and Washington DFI (a WhatsApp group posing as a
  registered adviser steering investors into a low-priced Chinese stock) *(snippet)*.
- **FDIC Office of Inspector General alert** (read this pass, undated): micro/small-cap non-U.S. issuers,
  typically **valued at $15M or less, IPO share price $4-6**; promoters receive **millions of pre-IPO
  shares at little or no cost**; before the IPO they solicit investors in encrypted group chats and tell
  them to **buy at a set time on IPO day**; after listing, promoters sell pre-IPO shares at inflated
  prices, including through **"match trades"**; a variant uses malware to buy in victims' accounts while
  matching sales come from fraudster-controlled accounts; weak-compliance transfer and clearing agents
  permit wash trades (https://fdicoig.gov/alerts/ramp-and-dump). Regulator, descriptive, no counts.
- **DOJ, November 2025**: a D.C. grand jury indicted Guanhua ("Michael") Su, a Hong Kong businessman, for
  filing false SEC adviser forms for at least 10 sham entities (Feb 2023-Mar 2025); at least two were
  used to lure retail investors, via social media and WhatsApp, into Nasdaq-listed Chinese companies; one
  promoted stock fell about **88% on 2024-04-17**; prosecutors cite **$211M of sale proceeds** (DOJ
  release via search summary; the defendant is presumed innocent; parallel SEC civil actions). The
  Global Relay item on a co-CEO and adviser charged over $110M in illicit profit has no date or docket
  found *(not verified)*.
- **Nasdaq rule changes.** Proposed 2025-09-03: raise the minimum market value of unrestricted publicly
  held shares to **$15M for the net-income listing standard (from $5M)**, a **$25M gross-proceeds floor
  for China-based IPOs**, faster delisting of thinly traded issuers. A law-firm report (Greenberg Traurig,
  *secondary*) says the SEC **approved the China-based $25M rule on 2026-05-14**, effective about 30 days
  later (mid-June 2026), applying to issuers listing at least 30 days after approval. I did not read the
  SEC order, and sources differ on the effective date. Related: Nasdaq IM-5101-4 delisting authority
  (`methods-short.md` §3.2, snippet).
- **Bloomberg (2026) "apparent pump and dump investor scam" graphic and an Inquirer piece on Bancroft
  Capital IPOs, 2026-05-12**: press, *not read*; listed in `methods-short.md`.

### 2.2 How the scheme works (mechanics, assembled from the regulator sources above)

| stage | what happens | what is visible on the tape |
|---|---|---|
| float control | insiders and nominees hold nearly all shares; tiny public float after a small IPO | float/shares outstanding; IPO size; lock-up; recent listing date |
| pre-IPO or pre-run allocation | promoters take cheap shares | none, except later share-count and registration filings |
| coordinated buy | a group told to buy at an exact time | **one-minute volume step with a round-lot, same-side signature; no news; opening print at a time-synchronised instant** |
| social push | ads, WhatsApp/Telegram, X, "investment clubs" | StockTwits/X message spike *after or with* the first move; no catalyst tag |
| ramp | price moves on thin volume; match trades, wash trades | **high price change per share traded; high off-exchange share; repeated round prices** |
| dump | promoters sell into retail demand | volume peaks and price fades within the session or next day; high short-volume share afterwards |
| aftermath | SEC suspension (10 days) then exchange halt; or collapse | a halt flag; locked position |

Earlier academic evidence (already in `literature.md` §4): Leuz et al. 470 schemes, mean return -28 to
-30%; Hanke & Hauser, Frieder & Zittrain (spam: heavy-tout-day buyers lose about 5-7% over two days
*secondary*); Renault (arXiv 2018) 635 Twitter-spike events in 315 OTC micro-caps, promoter-driven CAR
[+1,+5] about -5.3%, only 18% had a press release and tweets often followed the move; Xu & Livshits,
Dhawan & Putnins (crypto pumps predictable before the announcement because coordinators accumulate first).

### 2.3 What this means for a Nasdaq/NYSE gapper universe

- The 2024-26 cases are **listed** securities, not OTC. They enter our universe. They tend to have:
  non-US (Hong Kong, Singapore, Malaysia, China) principal office; IPO within about two years at about
  $4-6; market value of about $15M or less at IPO; a Cayman or BVI holding company; underwriters that
  are small or regional (law-firm summaries, *secondary*).
- Their gap days can have **no news at all** (SDM's stated basis was social-media recommendation, not a
  release). So "no catalyst + foreign micro-IPO + sudden volume" is a *promotion* signature, not a
  leakage one. A model that treats "no catalyst" as "unknown catalyst, maybe leak" is mixing both.
- Their risk is a **trading suspension**: the trade is not a fade that closes out; the position is
  frozen for 10 business days plus an exchange halt. That is an additional tail for both longs and
  shorts, not covered in `methods-short.md` §2.2.
- A trader who sees these names cannot tell from the tape alone whether a buy is coordinated; the
  regulator language itself says "unknown persons", i.e. the SEC could not name the promoter at the
  time of suspension.

### What to test (train only, via an amendment)

- **Define a `promoProfile` flag, point-in-time:** non-US principal office (from EDGAR company address,
  6-K/20-F filer status), listed within 730 days, last IPO priced $4-6 (if recoverable), shares
  outstanding or float small, no wire or 8-K/6-K in the 24 hours before. Report gap-day outcome, MAE,
  and halt incidence for flagged versus unflagged names, train only.
- **Suspension and long-halt incidence.** From the SEC suspensions list (public, dated) and exchange
  halt codes in the study's halt data, count how many flagged names were later suspended, and what the
  gap-day return was for them. If the count is nonzero in train, add suspended-after-signal as a loss
  case in the short and long sims (position stuck at the last print).
- **Social push timing.** Where free StockTwits/X counts exist (§5), measure whether message volume rises
  before or after the first 5% minute move on flagged names. The literature (Renault 2018, Barber et al.)
  says after; test it rather than assume.
- **Match-trade proxies.** Round-price and same-size repeated prints in flagged names versus controls
  (needs trade prints, so an explore-only sample).

---

## 3. Reverse-split float squeezes and the dilution cycle

### 3.1 Why splits and ADS ratio changes precede runs

- **Mechanics.** A 1:50 reverse split cuts the share count by 50 and raises the price by about 50 times
  with no change in market value. A **float of tens of millions can become a float of well under a
  million tradable shares** while the headline price is no longer sub-$1. Scanners rank by percent change
  and price, so the stock first appears in the "low-price, low-float" bucket that day-trader scanners
  favour (a mechanism consistent with §4; no study measured it for reverse splits).
- **ADS ratio change.** For a foreign issuer's ADSs, changing the ADS-to-ordinary ratio is economically a
  reverse split of the ADS (e.g. Xiao-I's 2026 change to one-for-twenty equivalent; Lion's one-for-thirteen,
  November 2025; Oatly's February 2025 change; DBV, SaverOne, Nano Dimension) without touching the
  ordinary shares; fractional entitlements are sold and paid in cash. Issuers state the compliance motive
  (Nasdaq minimum bid) and warn that price may not rise proportionally *(8-K/6-K and press, secondary)*.
  I found **no study** of low-float spikes after ADS ratio changes. NWGL, as reported, would be this type.
- **It is announced before it happens.** Nasdaq Rule 5250(e)(7): the company must submit a complete
  Company Event Notification Form by 12:00 ET **at least ten calendar days** before the market effective
  date (was five business days before 2025-01-30). Rule 5250(b)(4): public disclosure by 12:00 ET **at
  least two business days** before the effective date, with MarketWatch notice. Nasdaq will not process a
  split, and halts trading, if these are missed. Rule 5810(c)(3)(A)(iv): a company that did a reverse
  split in the prior year, or splits with a cumulative ratio of **250:1 or more in two years**, gets no
  bid-price compliance period (NYSE threshold 200:1) (K&L Gates, Hogan Lovells, Norton Rose; **law firm,
  secondary**; rule text not read). So the effective date is public for at least two business days, and
  usually on an 8-K item 5.03 and the exchange's daily list. *Anyone watching those lists "knows".* This
  is a calendar-based edge, not insider information. It also means the market-wide *anticipation* is
  priced before the effective day by the people watching, and the effective-day gap is the visible part.
- **Also a public filing trail.** Proxy (DEF 14A) approval, then an 8-K with the ratio; Reviva's October
  2025 DEF 14A is an example of the proxy text *(sec.gov, not read)*.

### 3.2 Returns after reverse splits

- **Kim, Klein & Rosenfeld (2008, *Financial Management* 37(2), 173-192), "Return performance
  surrounding reverse stock splits: can investors profit?", peer-reviewed** *(abstract/snippet)*:
  **over 1,600 reverse splits**, statistically significant **negative abnormal returns over the three
  years** after the split month; the stocks have characteristics that make them **very hard to short**.
  The 2006 EFMA working paper reads this as limited exploitable opportunity. Already cited in
  `methods-catalysts.md` C6 as secondary; the abstract is now confirmed.
- A study following Ikenberry & Ramnath (2002) finds the underperformance is stronger when post-split
  price is below $5 *(snippet, authors not seen)*. A 1981-2010 conference paper (Ogwu, IAES) finds the
  effective-month abnormal return negative and significant *(working paper, snippet)*. Earnings-
  management work finds negative accruals before reverse splits *(snippet)*.
- A **press account of a Merrill Lynch study** says reverse-split tech stocks beat their index by about 9%
  in month one and trailed by about 21% after twelve months (**press, secondary; not verified**).
- **Horizon mismatch.** All of the above are monthly or longer. **None measures the first session** of
  the effective day, which is the only window this study trades. So "returns after reverse splits are
  negative" is evidence about months, not about 09:30-10:30 on the effective day.
- **Protocol constraint** (`methods-catalysts.md` C6): the frozen sample excludes symbol-days whose split
  ex-date falls in [D-25, D+1], so WCT-type days are *not in the frozen population*. This pass only
  adds hypotheses for an explore-only sample.

### 3.3 The dilution link: run the price up, then sell into it

- **Why issuers want a higher price.** A reverse split restores the bid-price test and market access; an
  offering, ATM or equity line needs shares investors will buy, and larger proceeds per share issued.
  Practitioner commentary says reverse-splitting companies are often in financial trouble and need more
  capital soon (investmentexecutive; **press/vendor, not evidence**).
- **Equity lines and standby purchase agreements** are structurally similar to ATMs but the investor
  buys at a discount to a short-window price (a Mayer Brown event summary, 2022, *law firm*); one
  commentary notes pricing off the lowest daily price rather than the average increases dilution
  *(vendor, snippet)*. These instruments let the counterparty **sell registered resale shares during
  spikes**, so a price spike on volume is when proceeds are largest.
- **AMF (France) study, 69 companies using dilutive "toxic" financing, regulator** *(snippet)*: the
  companies with the largest price falls ran more transactions, with more counterparties and more
  instruments, and had more share consolidations and par-value reductions. A French-market study, not
  Nasdaq, but the closest regulator evidence of the consolidation-then-dilute pattern.
- **Gap.** I found **no SEC staff paper and no peer-reviewed study of U.S. micro-cap price and volume
  around ELOC/ATM/PIPE/offering-prospectus filings**, and none on whether the issuer's counterparties
  sell into one-day spikes. The OMH sequence (August 1:50 split, $40M prospectus, +520% on 11-04) is a
  hypothesis consistent with that pattern, not evidence for it.
- **What a prospectus filing tells a model.** An S-1/F-1/424B filing, an effective registration of a
  resale shelf, or an 8-K 1.01 for a purchase agreement is a *dilution-supply* signal: it says
  large-volume selling shares exist at today's price. EDGAR acceptance time is a free timestamp.

### What to test (train only, via an amendment)

- **Explore-only reverse-split sample.** Build the sample the protocol excludes: every symbol-day with a
  split or ADS ratio change effective on D, from corporate actions plus 8-K 5.03 / 6-K text. Report open
  gap, 09:30-10:30 and close returns, hit-rate of the +50% premarket gap, and halt incidence. Prediction
  from the months-scale literature: the long side does not survive costs beyond the first hour; the sign
  of the first hour is unknown. Say so.
- **Ratio and float interaction.** Split ratio bucket (<1:10, 1:10-1:49, 1:50+), post-split float if
  recoverable, and the cumulative two-year ratio (the 250:1 rule). Does a larger ratio raise next-day
  reversal?
- **Dilution-supply flag.** Count filings in D-90..D-1 (S-1/F-1/424B5, resale shelf effectiveness, 8-K
  1.01 with "purchase agreement", ATM prospectus supplement) and test gap-day close-to-high give-back
  and next-day return for flagged versus unflagged reverse-split days. Report the effect of the flag
  alone, not a causal claim.
- **Split-to-prospectus lag.** Days between the split effective date and the next offering prospectus,
  to see whether OMH-type sequences are common (descriptive).

---

## 4. Retail herding through scanners, social media and momentum algorithms

### 4.1 Evidence

- **Barber, Huang, Odean & Schwarz (2022, *J. Finance* 77(6), 3141-3190), "Attention-induced trading and
  returns: evidence from Robinhood users", peer-reviewed** *(abstract/snippet; already in
  `methods-short.md` §3.1 and `literature.md` §2)*. Robinhood users show more attention-induced trading
  than other retail investors; intense buying forecasts negative returns; Robinhood outages
  disproportionately reduce trading in high-attention stocks; the app's **Top Movers list** is argued to
  amplify the behaviour; losses are concentrated in names under $1B. Versions differ on magnitudes
  (5-day -3%, 20-day -4.7%, extreme herding -19.6% *secondary*); re-read the published numbers before use.
- **Boehmer, Jones, Zhang & Zhang (2021, *J. Finance* 76(5), 2249-2305), "Tracking retail investor
  activity", peer-reviewed** *(abstract/snippet)*. Off-exchange trades priced just below a round penny
  are classified as retail buys, just above as retail sells; stocks with net retail buying outperform net
  selling by about **10 bps over the following week**; suggestive evidence retail orders hold some
  firm-level information.
- **Barber, Huang, Jorion, Odean & Schwarz (2024, *J. Finance* 79(4), 2403-2427), "A (sub)penny for your
  thoughts: tracking retail investor activity in TAQ", peer-reviewed** *(abstract/snippet)*. With 85,000
  actual retail trades in six brokerage accounts (Dec 2021-Jun 2022): the sub-penny algorithm labels
  about **35%** of retail trades, misclassifies about **28%** of what it labels, and gives uninformative
  order-imbalance measures for about **30% of stocks**; signing at quote midpoints cuts signing error to
  about **5%** and gives informative measures for all stocks. A retail-footprint feature must use the
  corrected signing, not the original rule.
- **StockTwits.** Divernois & Filipovic (*Digital Finance*, 2023; *snippet*): polarity does not predict
  next-day returns unconditionally; in an event study of **sudden message-volume peaks**, bullish and
  bearish peaks match same-direction abnormal returns that **normalise immediately after the event
  date**; the prior 20-day return does not predict the event type. An older message-board study
  *(authors and year not confirmed)* finds bullish tone matters for small-caps and predicts future news
  more strongly in small-caps. Large-cap StockTwits work (six stocks) ties message volume to trading
  volume. SFI Research Paper 21-33 on StockTwits classified sentiment *(listed, not read)*.
- **Already in `literature.md` §14:** Bradley et al. (RFS 2024, WSB pre-GME posts), Warkulat & Pelster
  (IRFA 2024, -8.5% at peak WSB attention), Renault (2017/2018), and Barber et al. on Robintrack lags of
  30-45 minutes. The statement there that "no study documents minute-scale social lead time" still
  holds; I found none this pass either.

### 4.2 How fast is the loop?

No source measured the top-gainers feedback loop in minutes. What is established: attention is
concentrated on a short ranked list (Robinhood's about 20 Top Movers); herding probability rises on
lists; outages remove high-attention trading. My own reasoning, **not a finding**: a scanner list is a
*function of price change*, so a stock that moves 10-20% on thin early volume enters the list, which
produces more retail buying, which produces more price change; premarket data vendors' "top gainer"
windows give the same effect. The loop has no information content; it is a latency game, and in the
OMH-type case ("volume ignited at 07:45 ET") the ignition is likely a scanner threshold crossing.

### What to test (train only, via an amendment)

- **Rank-list entry as an event.** The minute a name first appears in the premarket top-N by percent
  change (the study's own `scan_movers`-style feed, already stored?) and the next-minute volume and
  price response, by N (10, 20, 50). A causal-looking jump at rank entry, without news, is herding.
- **Retail-signed flow, corrected signing.** On a sample with trade prints, signed retail imbalance in
  the first 5 minutes after the first 5% move, midpoint-signed, and its relation to the next 30-minute
  return and to the close-to-high give-back.
- **Lag of the second move.** After the first volume ignition, time to the second ignition (a re-ranking
  pop), as in the Hawkes/self-exciting views already in the evidence tools.
- **Repeat-gapper herding** is in `methods-short.md` §3 (day 2); do not duplicate.

---

## 5. Detectable footprints, by mechanism, with the data we have or could get

### 5.1 Data inventory

| data | have / could get | what it gives | limit |
|---|---|---|---|
| 1-minute bars (OHLCV) | have | volume ramps, price/volume ratio, premarket start minute | no trade-level signing |
| news publish times | have, with vendor-timestamp caveat | headline versus move timing | N1 in `methods-catalysts.md` |
| EDGAR acceptance times (`acceptanceDateTime`) | could get (free) | 8-K, 6-K, S-1/424B, Form 4, SC 13D/G, F-1, Form 25 timestamps to the second | public posting can trail acceptance; PDS lead is gone since 2014-15 |
| halts, LULD | have | halt codes, T1/T2, SEC suspension flag | |
| share counts, float, listing date | partial | float, IPO age, ratio | float sources are noisy |
| corporate actions | have | split ratios, ex-dates | declaration date may be missing |
| FINRA daily short sale volume | could get (free; published by 18:00 ET same day for that trade date; files since 2018-08) | prior-day short-volume share per symbol | not short interest; lags; excludes exchange trades unless combined |
| Reg SHO threshold list | could get (SRO-published daily) | persistent fails-to-deliver symbols | criteria not verified from the rule text this pass |
| odd lots, off-exchange share, sub-penny prints | trade prints needed | retail footprint; wash/match-trade checks | corrected signing needed (Barber 2024) |
| StockTwits / X message counts | possibly free, rate-limited | attention timing | coverage of micro-caps and API terms unverified |
| options flow | not available | the best leakage detector (ABS 2019) | micro-caps often have no listed options |

### 5.2 Footprint table, by mechanism

| mechanism | typical tell before the move | typical tell during | typical tell after |
|---|---|---|---|
| **Leakage (insider/tip)** | quiet drift or modest volume on a *real, material* catalyst in the hour or day before; a catalyst of the M&A/strategic type; Form 4 or 13D absent | gap on the release; volume confirmation; halts | no systematic reversal; price holds if the news was real |
| **Coordinated promotion** | recent foreign micro-IPO, tiny float, no news; a volume step at a *clock-synchronised instant* (IPO day open, 09:30, a round time); no Form 4/8-K; StockTwits/X spike with or after the first move | very high price change per share traded; large round-lot same-side prints; high off-exchange share; many round prices; halts | fast collapse; SEC suspension or exchange halt; high short-volume share the next day |
| **Mechanical squeeze (reverse split / ADS ratio)** | effective date public ≥2 business days earlier; an 8-K 5.03/3.03 or 6-K; low post-split float; sometimes no premarket trading on the first day (OMH-type) | scan-driven ignition from rank-list entry; gap at open; volume concentrated in minutes | reversal common; prospectus/ELOC filings within days; negative months-scale drift |
| **Retail herding on rank lists** | price change alone crosses a rank threshold on thin volume | self-exciting volume; heavy retail-signed flow; odd-lot spikes | reversal within hours to days (Barber 2022, Divernois & Filipovic); no catalyst required |
| **Dilution supply** | S-1/F-1/424B5, resale shelf effectiveness, 8-K 1.01 in the prior days; price near the offering price | selling pressure into the spike: flat high, big volume, little further price gain | gap-fill; further filings |

**What distinguishes them (testable with free data)**

1. **Is there a public pre-announced event?** Reverse split/ADS (8-K, exchange list) is a *calendar*
   feature. Nothing else needs to be inferred. This is the cleanest classification.
2. **Is there a real catalyst with a timestamp?** If yes and the move began before it, leakage or
   momentum is possible; if no, promotion or herding.
3. **Is the move concentrated at a synchronised instant, versus diffusing?** Coordination clusters;
   herding diffuses; leakage drifts.
4. **Foreign micro-IPO profile?** Strongly shifts probability toward promotion (regulator pattern).
5. **Prior filings** (offering/ELOC) shift toward dilution-supply; this is not exclusive of the others.
6. **Does the price hold the next hour/day?** Leakage with a true catalyst holds more than the rest
   (Barber, Renault, Leuz all find reversal in attention/promotion).

### 5.3 What a model can compute at 07:00-09:30 ET (candidate features; none tested)

- `rsEffectiveToday`, `rsRatio`, `adsRatioChange`, `cumRatio2y` (from 8-K 5.03/6-K and corporate actions;
  known the prior day).
- `daysSinceListing`, `foreignHQ`, `holdingCoDomicile` (Cayman/BVI), `ipoPriceBand`, `floatShares`.
- `filingsDilutionSupply90d` (counts of 424B/S-1/F-1/8-K 1.01) and `acceptedBeforeGapMinutes` for each.
- `newsFirstSeenMinute` versus `firstVolumeIgnitionMinute` (lead or lag, signed); `preNewsRunPct`.
- `volStepSynchronised` (largest 1-minute volume ratio and whether it sits within ±1 minute of a round
  clock time or the 09:30 open; computed separately for premarket start).
- `pricePerShareTraded` (|return| / volume-in-float-fractions) and `volOfFloatPct`.
- `rankListEntryMinute` and `rankAtEntry` where a movers feed is stored.
- `finraShortVolShareD1` (prior day), `thresholdListD1`, `halts D-1`.
- `socialCountSlope` (if feasible) and `socialLeadsPrice` boolean.
- `reRank` (second ignition within 30 minutes of the first).

Everything above is available *before* 09:30 except trade-print-based items, which need explore-only
data.

### What to test (train only, via an amendment)

- **Mechanism classifier, descriptive first.** Tag every train gapper with one of {calendared mechanical,
  catalyst with pre-run, catalyst without pre-run, no catalyst + foreign micro-IPO, no catalyst other,
  dilution-supply-primary} by rule, not by fit. Report outcomes per class at fixed horizons. Reject the
  idea of one model for all classes unless the class effect is null.
- **Pre-09:30 feature ablation.** Which of the §5.3 features add out-of-fold lift for 09:30-10:30
  continuation or reversal beyond the existing model, with a day-block bootstrap and the ledger's
  deflated-Sharpe accounting. Report nulls.
- **FINRA short volume as a feature.** D-1 short-volume share on the symbol, as a predictor of the
  fade, with a caution that it is not short interest and its availability for a given venue set is
  partial.
- **Cross-class stress.** Does the existing fade or long rule behave differently on promoted-profile
  names, with the halt tail added?

---

## 6. Legal and ethical note: what a retail trader can and cannot do

Not legal advice. Statute and rule wording below is from memory and was **not re-read this pass**
*(not verified)*; the regulator pages cited in the sources are the primary text.

**Fine, and what this study does.** Trading on public information and public price/volume patterns:
reading the exchange's reverse-split notices, 8-Ks, press releases, scanner lists, halts, and public
data like FINRA short volume; building a model from them; trading on the model. A footprint of other
people's buying is a public signal. Being faster than another retail trader is not a violation.

**Not fine.**
- **Material non-public information (MNPI).** Trading on a tip you know or should know came from a
  breach of duty (Section 10(b) and Rule 10b-5, misappropriation or classical theory) is illegal for
  tippee and tipper alike; Ahern's networks and the SEC cases above (Kindred, US Xpress, Blue Apron) are
  the pattern: family, friends, a meeting with an insider. "I saw it in a chat" is not a defence if the
  chat's source is an insider.
- **Joining a pump or ramp group.** Participating in a coordinated buy to inflate a price, or buying on
  instructions from a promoter who holds the stock, can fall under Rule 10b-5 and the manipulation
  provisions of Exchange Act §9(a). The SEC's SDM order treats "recommendations... by unknown persons via
  social media" designed to inflate price and volume as manipulation. Even as a victim, there is
  **no safe exit**: the stock can be suspended and the position frozen. A retail trader who knowingly
  joins is not protected by being small.
- **Spoofing, wash and match trades, account takeover** are plainly out. The FDIC OIG describes
  match trades and malware-based account use as part of the scheme.
- **Reg FD / selective disclosure** applies to issuers; the retail trader's risk is *knowing* the
  information is MNPI.
- **Do not engage with "investment group" outreach** (FINRA's alert); verify any adviser on BrokerCheck,
  and never move funds at a group's direction.

**Detection posture for our system.** Our detector may treat a promotion or leakage footprint as a
*reason not to trade* (or to size down) and as a *risk flag*. It must not (a) join or pay for a group,
(b) scrape private chats, (c) use any source that is itself MNPI. If a free social feed is used, it
must be a public, terms-permitted API.

**The user's question, answered plainly.** The runs in the five examples can be fully explained without
insider trading: a calendared mechanical event (WCT, NWGL, OMH's split), a dilution-supported price
(OMH), a thin float scanned by many people, and herding on a rank list. The regulator evidence says the
*other* explanation, coordinated promotion, is real and growing, and the academic evidence says
leakage is real around deals. For CKX (a $1B investment) and STAK (an LOI), a pre-publication run in the
tape is the observable that could separate leak from momentum; that is §1's test. We cannot see who
bought, and nothing here proves anyone traded on non-public information.

### What to test (train only, via an amendment)

- **Compliance guard in code review, not a statistical test.** A written rule that no feature depends on
  a private channel; list each feature's data source and licence in the amendment.
- **Cost of the promotion exclusion.** Report performance with and without `promoProfile` names so the
  trade-off between safety (halt tail, §2) and capture is explicit.

---

## Ranked shortlist (for the next amendment)

1. **Mechanism tag by rule** (§5, last test) and **explore-only reverse-split/ADS sample** (§3): cheap,
   uses data we have, and answers "what are these runs" before any model.
2. **Pre-run versus catalyst type** (§1): reuses the 5-year result; separates leakage-prone tags from
   scheduled/financing tags. Needs the wire-time repair (N1) first.
3. **`promoProfile` flag + halt/suspension tail** (§2): the regulator pattern is specific enough to
   encode, and it changes risk, not just return.
4. **Dilution-supply filings** (§3.3): EDGAR acceptance times are free and precise.
5. **Rank-list entry** and **retail-signed flow** (§4): explore-only; needs trade prints.
6. **Social counts and FINRA short volume** (§5): low expected value, cheap to check.

## Sources

Verified this pass (record and quoted finding; abstract-level unless noted):

- SEC trading suspension, Smart Digital Group (SDM), 2025-09-26, **regulator, page read**:
  https://www.sec.gov/enforcement-litigation/trading-suspensions/34-104112-ts (order PDF
  https://www.sec.gov/files/litigation/suspensions/2025/34-104112-o.pdf, not read).
- SEC trading suspension, QMMM Holdings, 34-104113, **regulator, title and date only**:
  https://www.sec.gov/enforcement-litigation/trading-suspensions/34-104113-ts and
  https://www.sec.gov/files/litigation/suspensions/2025/34-104113.pdf.
- SEC suspension order 34-105675 (HCHL, 2026-06-11), **regulator, listed not read**:
  https://www.sec.gov/files/litigation/suspensions/2026/34-105675.pdf.
- SEC press release 2025-113, Cross-Border Task Force (direct fetch 404; via secondary summaries):
  https://www.sec.gov/newsroom/press-releases/2025-113-sec-announces-formation-cross-border-task-force-combat-fraud;
  Cleary Gottlieb, https://www.clearygottlieb.com/news-and-insights/publication-listing/sec-announces-formation-of-cross-border-task-force-to-combat-fraud;
  Alston, https://www.alston.com/en/insights/publications/2025/09/sec-new-cross-border-task-force;
  Akin, https://www.akingump.com/en/insights/alerts/sec-forms-cross-border-task-force-with-implications-for-foreign-issuers-auditors-and-other-gatekeepers
  (law firm, secondary).
- Cooley, "What foreign issuers should know about SEC trading suspensions", 2026-04-28 (law firm):
  https://investigations.cooley.com/2026/04/28/what-foreign-issuers-should-know-about-sec-trading-suspensions/;
  Cooley, 2025-10-28, https://investigations.cooley.com/2025/10/28/sec-intensifies-oversight-of-foreign-companies-that-participate-in-u-s-capital-markets/.
- Bloomberg Government, "SEC foreign firm suspension blitz spurs monthslong trading halts" (press;
  snippet), https://news.bloomberglaw.com/securities-law/sec-foreign-firm-suspension-blitz-spurs-monthslong-trading-halts.
- Securities Lawyer 101 (law-firm blog, snippet):
  https://www.securitieslawyer101.com/2026/02/03/latest-sec-trading-suspensions-should-be-a-wake-up-call-for-foreign-issuers/,
  https://www.securitieslawyer101.com/2025/sec-trading-suspensions-of-qmmm-sdm-new-sec-cross-border-task-force/,
  https://www.securitieslawyer101.com/2026/06/18/nasdaq-reverse-stock-split-rules-timeline/.
- FINRA, Investor Alert, "Investment group imposter scams", 2025-12-09, **regulator, page read**:
  https://www.finra.org/investors/insights/investment-group-imposter-scams; earlier alert, January 2024
  (ThinkAdvisor summary): https://thinkadvisor.com/2024/01/12/finra-sees-spike-in-investment-group-frauds-on-social-media.
- FDIC OIG, "Ramp and dump" alert (undated), **regulator, page read**: https://fdicoig.gov/alerts/ramp-and-dump.
- Canadian Securities Administrators, ramp-and-dump alert, April 2026 (regulator, snippet):
  https://www.investright.org/news-and-insights/investor-alerts/csa-investor-alert-ramp-and-dump-scams-surge-as-fraudulent-investment-groups-target-unsuspecting-investors/;
  Washington DFI, https://dfi.wa.gov/consumer/alerts/whatsapp-groups-impersonating-registered-investment-advisor-firms-appears-be (regulator, snippet).
- DOJ, Guanhua Su indictment, November 2025 (regulator, snippet):
  https://www.justice.gov/opa/pr/hong-kong-businessman-indicted-role-filing-false-sec-investment-adviser-forms-behalf-sham;
  Investment Executive, https://www.investmentexecutive.com/from-the-regulators/fake-advisory-firms-in-ramp-and-dump-scam-sec/.
- Nasdaq, Sept 3, 2025 listing-standards proposal (exchange press release, snippet):
  https://www.nasdaq.com/press-release/nasdaq-proposes-changes-its-listing-standards-2025-09-03.
  Greenberg Traurig, "SEC approves Nasdaq's $25 million IPO requirement for China-based companies",
  May 2026 (law firm, secondary): https://www.gtlaw.com/en/insights/2026/5/sec-approves-nasdaqs-25-million-ipo-requirement-for-china-based-companies
  (the search returned localized copies of this URL).
- Nasdaq reverse-split rules 5250(b)(4), 5250(e)(7), 5810(c)(3)(A)(iv) (law firm, secondary; rule text not
  read): K&L Gates, https://www.klgates.com/The-State-of-Play-for-Reverse-Stock-Splits-by-Nasdaq-and-NYSE-Listed-Issuers-1-23-2025;
  Hogan Lovells, https://hlc.com/en/publications/sec-approves-nasdaq-and-nyse-revisions-to-reverse-stock-split-rules-what-public-companies-need-to-know;
  Norton Rose Fulbright, https://nortonrosefulbright.com/de-de/wissen/publications/5b2935b6/new-nasdaq-and-nyse-rules-on-use-of-reverse-stock-splits.
- SEC litigation releases (regulator, snippet): Kindred, https://www.sec.gov/enforcement-litigation/litigation-releases/lr-26629
  and /lr-26348; US Xpress, https://www.sec.gov/enforcement-litigation/litigation-releases/lr-26359;
  Blue Apron, https://www.sec.gov/enforcement-litigation/administrative-proceedings/34-103764-s;
  Chimerix and others via Willkie Compliance Concourse, https://complianceconcourse.willkie.com/?p=16908.
- Augustin, Brenner & Subrahmanyam (2019), *Management Science* 65(12), 5697-5720,
  https://econpapers.repec.org/RePEc:inm:ormnsc:v:65:y:2019:i:12:p:5697-5720 (peer-reviewed; abstract);
  NYU Stern summary, https://www.stern.nyu.edu/experience-stern/faculty-research/insider-trading-unmasked.md.
- Ahern (2017), *J. Financial Economics* 125, 26-47, https://econpapers.repec.org/RePEc:eee:jfinec:v:125:y:2017:i:1:p:26-47;
  PDF https://msbfile03.usc.edu/digitalmeasures/kahern/intellcont/Ahern%20JFE%202017-1.pdf (peer-reviewed; abstract/summary).
  Ahern (2020, RAPS; NBER w24297) is cited via `literature.md` (not re-read).
- Rogers, Skinner & Zechman (2017), "Run EDGAR run", *J. Accounting Research* 55(2), 459-505,
  https://econpapers.repec.org/RePEc:bla:joares:v:55:y:2017:i:2:p:459-505 (peer-reviewed; abstract).
- Prevoo & ter Weel (2010), *De Economist*, https://link.springer.com/content/pdf/10.1007/s10645-010-9146-1.pdf;
  CPB, https://www.cpb.nl/sites/default/files/publicaties/download/effects-change-market-abuse-regulation.pdf
  (peer-reviewed / working paper; snippet). Bank of Canada working paper on Canadian takeovers,
  https://www.oar-rao.bank-banque-canada.ca/record/1308 (snippet). Heggen, Gannon & Van Eekelen (2007),
  https://ideas.repec.org/p/dkn/acctwp/aef_2007_04.html (working paper, snippet).
- Kim, Klein & Rosenfeld (2008), *Financial Management* 37(2), 173-192,
  https://econpapers.repec.org/RePEc:bla:finmgt:v:37:y:2008:i:2:p:173-192 (peer-reviewed; abstract);
  EFMA 2006 working paper, https://www.efmaefm.org/0efmameetings/EFMA%20ANNUAL%20MEETINGS/2006-Madrid/papers/568563_full.pdf;
  Ogwu, IAES, https://iaes.confex.com/iaes/72am/webprogram/Paper6539.html (working paper);
  press on a Merrill Lynch study, https://www.investmentexecutive.com/news/benefits-of-reverse-stock-splits-fade-quickly (not verified).
- ADS ratio changes (company releases, secondary): Xiao-I, https://www.sec.gov/Archives/edgar/data/0001935172/000121390026053155/ea028828201ex99-1.htm;
  Lion, https://www.investing.com/news/company-news/lion-to-change-ads-ratio-in-reverse-split-equivalent-move-93CH-4368686;
  DBV, https://www.tipranks.com/news/the-fly/dbv-technologies-announces-ads-ratio-change;
  Nano Dimension, https://www.globenewswire.com/news-release/2019/10/07/1925575/0/en/Nano-Dimension-Announces-ADS-Ratio-Change.html.
- AMF (France), study on dilutive financing, 69 companies (regulator, snippet):
  https://www.amf-france.org/en/news-publications/news-releases/amf-publishes-study-share-price-performance-companies-using-dilutive-financing-form-ocabsas-or;
  AMF warning, https://www.amf-france.org/en/news-publications/news-releases/amf-news-releases/amf-warns-listed-companies-corporate-officers-and-investors-about-risks-associated-equity-line-and.
  Mayer Brown, equity-line event summary (law firm, thin),
  https://www.mayerbrown.com/zh-hans/insights/events/2022/09/equity-line-financings-practical-and-legal-considerations.
  Microcap commentary (vendor/anecdotal, not evidence): https://microcapclub.com/how-to-avoid-dilution-in-microcaps-a-dilution-risk-scorecard/.
- Barber, Huang, Odean & Schwarz (2022), *J. Finance* 77(6), 3141-3190,
  https://econpapers.repec.org/RePEc:bla:jfinan:v:77:y:2022:i:6:p:3141-3190 (peer-reviewed; abstract);
  SSRN draft, https://www.smallake.kr/wp-content/uploads/2020/12/SSRN-id3715077.pdf.
- Boehmer, Jones, Zhang & Zhang (2021), *J. Finance* 76(5), 2249-2305,
  https://econpapers.repec.org/RePEc:bla:jfinan:v:76:y:2021:i:5:p:2249-2305 (peer-reviewed; abstract).
- Barber, Huang, Jorion, Odean & Schwarz (2024), *J. Finance* 79(4), 2403-2427,
  https://econpapers.repec.org/RePEc:bla:jfinan:v:79:y:2024:i:4:p:2403-2427 (peer-reviewed; abstract);
  SSRN, https://papers.ssrn.com/abstract=4202874.
- Divernois & Filipovic, StockTwits message-volume event study, *Digital Finance* (2023),
  https://link.springer.com/article/10.1007/s42521-023-00102-z (peer-reviewed; snippet);
  SFI RP 21-33, https://www.sfi.ch/en/publications/n-21-33-event-studies-on-investor-sentiment (listed, not read).
- FINRA, Daily Short Sale Volume Files, https://www.finra.org/finra-data/browse-catalog/reg-sho-daily
  (regulator; snippet) and file layout, https://www.finra.org/sites/default/files/DailyShortSaleVolumeFileLayout.pdf.

Not read, cited via other study files or from memory (not verified; do not cite in results):
Ahern (2020); Keown & Pinkerton; Meulbroek; Leuz et al.; Hanke & Hauser; Frieder & Zittrain; Renault
(2017, 2018); Xu & Livshits; Dhawan & Putnins; Bradley et al.; Warkulat & Pelster; Ikenberry & Ramnath
(2002); Securities Exchange Act §9(a), §10(b), Rule 10b-5, Regulation FD text; Reg SHO threshold-list
criteria (third-party sites conflicted); Nasdaq's February 2026 delisting-on-suspension proposal status;
Bloomberg 2026 graphic and Inquirer Bancroft Capital article (listed in `methods-short.md`).

**Not found:** (1) any study of intraday timing between a micro-cap press release and the trading
before it; (2) any SEC action on pre-release trading in an LOI, PIPE, ELOC, offering or ADS ratio
change; (3) any study of first-session returns on reverse-split effective days (all found studies are
monthly or longer); (4) any study of low-float spikes after ADS ratio changes; (5) any study of
micro-cap price and volume around ELOC/ATM/offering-prospectus filings, or of whether counterparties sell
into one-day spikes; (6) any measurement of the top-gainers feedback loop in minutes, or of social-media
lead time at minute scale; (7) the OMH, WCT, NWGL, CKX and STAK facts, which are the user's and
unverified here.

My own reasoning, not from a source: the mechanical float arithmetic in §3.1, the scanner feedback loop
in §4.2, the mechanism tags, the feature names, the 5%/N thresholds, the 730-day listing cut, and the
rule that "no catalyst + foreign micro-IPO" signals promotion. They are starting points for train.
