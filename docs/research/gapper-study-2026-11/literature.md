# Gapper study — literature review (IP-T442)

Written for the record before any study data was pulled. Sources are grouped by the question they
answer; each entry gives author, year, venue, its standing (peer-reviewed / working paper /
practitioner or vendor), the one quantified finding this study leans on, and the hypothesis or
feature it motivates. Figures marked *(from memory)* or *(secondary)* were not re-read from the
paper for this review and should be treated as approximate; see *Uncertainty notes*. No reference
outside the two survey reports that fed this file has been added.

## 1. Gap formation and pre-move predictability (overnight vs intraday)

| reference | standing | finding | motivates |
|---|---|---|---|
| Lou, Polk & Skouras, JFE 2019, "A tug of war" | peer-reviewed | Anomaly profits earned overnight vs intraday carry opposite signs; small-cap overnight momentum alpha 0.54%/mo (t = 4.49) vs intraday 0.39% (t = 1.59) *(secondary)* | `overnight_j` / `intraday_j` decomposition feature; H4/H5 framing (the move is the gap) |
| Berkman, Koch, Tuttle & Zhang, JFQA 2012, "Paying attention" | peer-reviewed | 3,000 largest US stocks 1996–2008: +0.10% overnight / −0.07% intraday on average; high-retail-attention names +0.2–0.3% overnight and −0.2 to −0.4% intraday; hard-to-arbitrage +0.4–0.6% / −0.4 to −0.7%; more than 2× in high-sentiment months; the open is systematically the worst price of the day in attention stocks | H4, H13, H14 (sell into the open); `closeBelowOpen` label |
| Aboody, Even-Tov, Lehavy & Trueman, JFQA 2018 | peer-reviewed | Overnight return is a firm-level sentiment proxy: short-run persistence, 12-month reversal (~7.4pp long–short) | `overnight_j` as a run-up feature |
| Akbas, Boehmer, Jiang & Koch, JFE 2022 | peer-reviewed | The frequency of overnight-up / daytime-reversal days predicts returns | H15 (repeat behaviour), run-up block |
| Bogousslavsky, JFE 2021 | peer-reviewed | Small/illiquid stocks earn returns in the last 30 minutes and lose them overnight | caveat on the `D−1 16:00` snapshot: the last 30 minutes of D−1 are part of the signal |

## 2. Retail attention and reversal

| reference | standing | finding | motivates |
|---|---|---|---|
| Barber & Odean, RFS 2008, "All that glitters" | peer-reviewed | Individuals are net buyers after news, abnormal volume and extreme returns; attention-bought stocks underperform | H3, H16; `abnVol_j`, `catalyst` block |
| Barber, Huang, Odean & Schwarz, JF 2022 (Robinhood herding) | peer-reviewed | 5-day abnormal return −3% after top-bought days, −6% after extreme herding; only for sub-$1B caps; a herded stock is ~10% more likely to be herded again next day; herding-day +14% of which the opening return is +11%; −3.5% over 5 days, −5% over 20, losses traced to intraday; yesterday's top-mover rank raises next-day herding probability 6.7pp; Robintrack lagged 30–45 min | H15 (`priorDayTopMoverRank`), H4 (the gap is the move), short-side design |
| Kumar, JF 2009 | peer-reviewed | Lottery-like stocks underperform 2–3%/yr | universe price band rationale |
| Bali, Cakici & Whitelaw, JFE 2011 (MAX) | peer-reviewed | MAX effect ~1%/month raw *(from memory)* | `fwdMax_h` as a label the market already prices |
| Da, Engelberg & Gao, JF 2011 | peer-reviewed | Abnormal Google SVI predicts ~+0.3% over 2 weeks then reversal | not available on this stack; listed for completeness |
| Boehmer, Jones, Zhang & Zhang, JF 2021 | peer-reviewed | Marketable retail order imbalance (sub-penny prints in the SIP) predicts next-week returns, strongest in small stocks, order of 10–20 bps/week *(from memory)* | a weekly-horizon signal; not a formation feature here, noted as a possible T7-style add-on |

## 3. Pre-announcement run-ups and informed trading

| reference | standing | finding | motivates |
|---|---|---|---|
| Keown & Pinkerton, JF 1981 | peer-reviewed | ~40–50% of the takeover premium is impounded before announcement; abnormal returns start ~12 days out | `runUp5`, `ret_j` features; H6 |
| Meulbroek, JF 1992 | peer-reviewed | ~3% abnormal return on insider-trading days; about half of the pre-takeover run-up occurs on insider days | H8 (`form4BuysD30`) |
| Augustin, Brenner & Subrahmanyam, Mgmt Sci 2019 | peer-reviewed | ~25% of 1,859 takeovers show abnormal pre-announcement volume in short-dated OTM calls | the strongest pre-move footprint; **not available** (no options flow) |
| FDA advisory-meeting study (J Corp Fin 2023 / CLS Blue Sky 2022) | peer-reviewed (JCF) | ~32% show abnormal call volume beforehand and **no abnormal stock volume** | H6's honest prior: stock volume may carry nothing before the catalyst |
| Ahern, RAPS 2020 (NBER w24297) | peer-reviewed | Stock-volume proxies detect insider trading only for short-lived information; patient insiders evade spread/Amihud/Kyle-λ detection | H6/H11 prior: expect weak stock-volume lead |

## 4. Pump-and-dump and promotion

| reference | standing | finding | motivates |
|---|---|---|---|
| Leuz, Meyer, Muhn, Soltes & Hackethal, NBER 24083 / Mgmt Sci | peer-reviewed (Mgmt Sci) | 470 schemes; ~6–8% of active retail participate; mean return −28 to −30%; >35% are penny-stock day traders | H16, `promoSourceShare`, `PromoPr` type |
| Hanke & Hauser, JFM 2008 | peer-reviewed | Spam touting moves same-day return, volatility, spread and volume with no lasting price effect | H16, H3 |
| Frieder & Zittrain, 2008, "Spam Works" | working paper / law review | Buy the day before heavy touting, sell next morning: +4.3–5.8%; buyers on the heaviest tout day lose ~5–7% over two days; touting raises the probability of a heavy-volume day from 4% to 70% *(secondary)* | H15/H6 (volume the day before), H16 |
| Renault, arXiv 1804.04406, 2018 | working paper | Coordinated bot clusters promote low-value small caps; 635 Twitter-spike events in 315 OTC microcaps 2014–15: AR +4.10% day −1, +6.88% day 0, −3.11% CAR[+1,+5]; promoter-driven −5.34%, no-promoter +0.87% (no reversal); only 18% had a press release; tweets often follow the move | H16 (promoter vs not), H3, short-side design |
| SEC DERA, White 2016 | regulator working paper | OTC outcomes severely negative, worse for promoted stocks; ~25% of OTC trades fall in promotion windows | universe is listed `us_equity` only; promotion still expected in the sample |
| Xu & Livshits, USENIX 2019; Dhawan & Putnins, RoF 2023 (crypto) | peer-reviewed | Pumps are predictable before the announcement from pre-pump market data (AUC > 0.9) because coordinators accumulate first | transferable *idea* for H6/H11 (accumulation volume before the first tout); numbers not transferable |
| Price-path summary (surveys) | synthesis | 1-day spike, peak same/next day, decline over 2–5 days; multi-week campaigns show accumulation volume before the first tout | H6, `runUp5` |

## 5. Volume as a leading indicator

| reference | standing | finding | motivates |
|---|---|---|---|
| Gervais, Kaniel & Mingelgrin, JF 2001 | peer-reviewed | High-volume return premium over the next month | `abnVol_j`; H6 (shorter horizon than the paper tests) |
| Barclay & Hendershott, RFS 2003 | peer-reviewed | Pre-open trades carry more private information per trade | `pmVolRatio`, `trades_k`; H1, H9 |
| Warrior Trading criteria | practitioner, no methodology | Price $2–20, float < 10–20M, RVOL > 5×, gap > 5–10%, news; no published win rate | H9's `pmVolRatio > 5` threshold is the practitioner number, tested rather than assumed |
| Mometic 30-day backtest; TradingView Ross-style script; ChartMath | vendor / practitioner, unaudited | 64% win (30 days); ~40% win 2021–25; 64% win / PF 1.9 on 247 earnings gaps | none; listed so their disagreement is on record |
| A-share study (dev.to, 1.9M stock-days, 2026) | practitioner blog | 5–9% gaps on < 1× volume had a 62% 5-day win rate vs 34% on > 3× volume | **caution only**: high relative volume may mark the exhausted gap, not the continuing one |

No peer-reviewed test of premarket-volume / ADV or float rotation as an open-to-close predictor was
found by either survey. H1 and H9 are therefore first-of-kind on this sample, not replications.

## 6. Short squeezes and low float

| reference | standing | finding | motivates |
|---|---|---|---|
| Jiang, Liu, Schrowang & Xu, FAJ 2024 | peer-reviewed | Squeezes subside within ~7 days, followed by reversal | short-side timing (deferred) |
| Svoboda, Kapounek & Albrecht, 2025 | working paper | Short interest and attention spikes raise squeeze odds; institutional ownership lowers them; float not tested | no short interest on this stack; recorded as a limit |
| Filippou, Garcia-Ares & Zapatero | working paper | Betting on squeezes via calls has negative average returns | none (no options) |
| Short-interest cadence | fact | Bi-monthly publication | too slow for a formation feature |

## 7. Jump detection

| reference | standing | finding | motivates |
|---|---|---|---|
| Lee & Mykland, RFS 2008 | peer-reviewed | Reliable jump confirmation; over-flags in the first 30 minutes | `jumpFlag` (add-on only; the U-shape caveat is why it is not a primary) |
| Lee, RFS 2012 | peer-reviewed | Jumps are predictable from scheduled events and prior jumps | `catalyst` block, H2 |
| Boudt & Petitjean, JFM 2014 (DJIA) | peer-reviewed | Jumps preceded by liquidity shocks in spread and trade count; order imbalance most informative | `csSpread`, `trades_k`, H17 (large-cap evidence, transfer unproven) |
| Joulin et al., 2008 | working paper | Most jumps are liquidity-driven, not news | H3 (no-news gappers), H11 benchmark |
| Kong, Zhu & Azencott, J Forecasting 2021 | peer-reviewed | Random forest predicts a jump in the next 5 minutes with F1 ~63% (level-2 data) | not reproducible without L2; noted |
| Hawkes: Filimonov–Sornette; Hardiman–Bercot–Bouchaud | peer-reviewed | Jumps cluster; markets sit near-critical, limiting the branching ratio as a discriminator | consistent with the sweep's branching ≈ 0.4 finding; no Hawkes feature here |

## 8. Base rates

| reference | standing | finding | motivates |
|---|---|---|---|
| SmallCapLab (vendor, ~3,000–3,500 gappers >= 45% since 2022, no p-values) | vendor, unaudited | Close below RTH open 67.7%; close below VWAP ~75%; only ~45% break the premarket high (44.7%); HOD in first 15 min ~47–50%, by 10:30 ~70%; fade rate flat 70–72% across 45–150% gaps; round-trip to prior close 3.9%; gaps >= 100% ~1.6/day; premarket HOD median 08:00 ET (84% by 09:00); 59.6% give back >= half the premarket gain by the open; fade conditional on extension above open at 09:45: < 10% -> 93%, 25–50% -> 57%, > 100% -> 11% | H4 (60% vs vendor ~50%), H5, H13 (85% vs vendor 93%), `hodBucket`, `ext0945`, `fade` |
| QuantSync (paywalled, 822 gap-ups 2025–26) | vendor | 18% still +20% after 60 days; catalyst type beat gap size and volume as a predictor | H2, catalyst scoring |
| Dilution Tracker | vendor | Small-cap offerings average −20 to −30% on announcement; many big gappers are followed by offerings | H10, `daysSince424B`, `daysSinceS1` |
| (absent) | — | No source gives the fraction of >= 20% gappers preceded by a catalyst | the study reports this number as a sample fact |

## 9. Exit timing and intraday reversal

| reference | standing | finding | motivates |
|---|---|---|---|
| Vainio, Turku MSc 2026 | working paper | US 2011–2024: overnight -> intraday reversal monotonic across deciles, strongest small/illiquid/high-VIX; gross alpha large, **net alpha negative after Corwin–Schultz spreads** | cost model choice (Corwin–Schultz); H14 |
| Gao, Han, Li & Zhou, JFE 2018 | peer-reviewed | First 30-min return predicts the last 30-min (index) | not a small-cap result; noted |
| Baltussen, Da & Soebhag | working paper | End-of-day reversal stronger in small, illiquid, overnight-volatile stocks | `fwdClose_16:00` horizon rationale |
| Zarattini, Barbon & Aziz, SFI 2024 | working paper | 5-min opening-range breakout on top-20 relative-volume "stocks in play" 2016–2023, Sharpe 2.81 net (continuation, broad universe) | the one continuation result; H12 grid includes a no-VWAP-exit arm so continuation is not assumed away |
| Anonymous Reddit backtest | practitioner | 70% open day 2 below the day-1 open | not used |
| Trailing-stop multi-exit test | practitioner | Trailing stops add value only where momentum exists | H12 exit ladder |

## 10. Velocity and exhaustion measures

| reference | standing | finding | motivates |
|---|---|---|---|
| (absent) | — | No peer-reviewed test of price velocity, acceleration, VROC, VWAP distance or RSI divergence as top-callers in small caps | H5 is first-of-kind; stated as descriptive |
| Xiao, Yin & Zhao, J Financial Research 2020 (index ETFs) | peer-reviewed | No-news jumps reverse for >= 60 min; unscheduled-news jumps show momentum | H2, H3 |
| Aït-Sahalia, Li & Li, NBER 2024 (5-second data) | working paper | Relevant news produces a jump within ~2 min; most jumps have no news; no-news jumps are preceded by persistently high trade-to-quote imbalance that reverts right after the jump (exhaustion signature) | H17 (`tradesRoc` collapse), H11 benchmark window |
| Bacry et al. 2015 (Hawkes review); BOCPD/CUSUM | peer-reviewed | Intraday U-shape must be detrended; no paper validates Hawkes intensity decay as a small-cap exit | time-of-day profile (`profile_decile`) rather than raw volume |

## 11. The short side and LULD halts

| reference | standing | finding | motivates |
|---|---|---|---|
| Diether, Lee & Werner, RFS 2009 | peer-reviewed | Short flow predicts next-day negative returns, strongest in small/illiquid names; costs may eliminate the profit | short leg (deferred); FINRA short volume as T7 |
| SEC DERA LULD white paper 2015 (2013–14 data) | regulator working paper | Tier 2 stocks average 2.0 limit states and 0.004 pauses per security-day (Tier 1: 0.002 / 0.0001); 11% of Tier 2 paused within four months; events cluster in the first 15 min and 09:45–09:50 (band halving); bands 10% (20% if <= $3), doubled 09:30–09:45 | `haltFlag` proxy, halt fill rule, 09:28 sell-by in H12, sub-$3 cost-model floor |
| Kargarzadeh et al., 2026 | working paper | Shorting every news-covered small cap 2023–26 returned 15.9%/yr, Sharpe 1.35, borrow not modeled | short leg prior; not tested here |
| Barber et al. 2022 (sell-after-herding) | peer-reviewed | Sell after herding, rebuy 5 days later pays in 63% of 4,884 events (BHAR +3.5%); short interest rises ~3× more in herded names | short leg (deferred) |
| Alpaca HTB locates / `easy_to_borrow` | vendor fact | Available via the assets endpoint | `ShortSafetyCheck` design (code plan, not this study) |

## 12. News and filings as a numeric catalyst

| reference | standing | finding | motivates |
|---|---|---|---|
| Lerman & Livnat, 2008 / RAST 2010, 120k 8-Ks | peer-reviewed | Every item raises volume/volatility; good news reacts at the press-release date, not the filing; items 3.03 + 1.01 +3.98% 3-day; 1.03 bankruptcy −12%; 2.05/2.06/3.01/4.02 negative; drifts 30–90 days for some items | `eightKItems`, type priors in the catalyst score, H2 |
| Kargarzadeh et al., arXiv 2608.14014, 2026, 4.57M articles | working paper | Cumulative move by close of publication day 2.8× its 20-day value; press wires show **no pre-publication drift** (−0.06%); wire positive tilt reverses hardest; launch/partnership coverage 96–97% positive and gives back (−0.34% / −0.18% over days +6..+20); earnings/guidance/capital-return coverage continues; two promotional outlets carry −0.4 to −0.8%/month; 55% of articles are follow-ups | H11 benchmark (the release *is* the event), H16, `novelty`, `sourceTier` |
| PLOS ONE 2024 biopharma (1,012 firms, 503k releases) | peer-reviewed | Biotech reacts more than pharma; acquisitions / product-development show pre-release abnormal returns | H2 (Fda, MA), `runUp5` |
| FDA approvals (GitHub study, 234 approvals 2015–24); Princeton 2026 thesis | practitioner; student thesis | Small caps +8.5% but 36% negative; little directional AR after FDA news at minute scale, mostly volatility | H2 effect size kept modest (2pp) |
| Tetlock, Saar-Tsechansky & Macskassy, JF 2008; Loughran & McDonald, JF 2011 | peer-reviewed | Word lists matter: 73.8% of Harvard "negative" words are not negative in finance; use the LM list | `catalystSentiment` via the LLM classifier, LM counts as the "hardness" input to the score |
| Chan, JFE 2003 | peer-reviewed | Drift after headline news, reversal after no-news extreme moves, concentrated in small illiquid stocks | H2, H3 |

## 13. Real-time feasibility

| reference | standing | finding | motivates |
|---|---|---|---|
| Rogers, Skinner & Zechman, JAR 2017 | peer-reviewed | EDGAR acceptance -> public posting mean 40 s, median 36 s; the SEC removed the PDS head start | `ACCEPTANCE-DATETIME` as the filing's publication time |
| arXiv 2601.08962, 2026 | working paper | Post-earnings price discovery far faster after 2016 | expect short `pubToTMinutes` for scheduled catalysts |
| Alpaca News API (docs + forum) | vendor; two unanswered forum threads | `created_at`/`updated_at` RFC-3339, source, symbols, headline, summary, content; without real-time entitlement the default window lags 15 min; stream reported to carry Benzinga-written items and miss wire press releases; no published latency measurement | `pubToTMinutes` is an upper bound; the study reports first-abnormal-minute vs first-headline ordering |

## 14. Social media

| reference | standing | finding | motivates |
|---|---|---|---|
| Renault, JBF 2017 | peer-reviewed | StockTwits first-half-hour sentiment predicts last-half-hour SPY | index-level; not used |
| Bradley, Hanousek, Jame & Xiao, RFS 2024 | peer-reviewed | Pre-GME WSB DD posts +0.88% day 0, +5.17% one month, no reversal; post-GME zero | "edge already gone" entry below |
| Warkulat & Pelster, IRFA 2024 | peer-reviewed | Positions taken at peak WSB attention earn −8.5% | short-side prior |
| Barber et al. 2022; Renault 2018 | peer-reviewed; working paper | Robintrack lagged 30–45 min; tweets often follow the move; no study documents minute-scale social lead time | no social feature in this study |

## What is pre-move-predictive from public data

| signal | evidence strength | lead time | data required | available on this stack? |
|---|---|---|---|---|
| OTM short-dated call volume before M&A / FDA | strong (peer-reviewed, several papers) | days–weeks | options flow | **no** |
| Prior-day herding / top-mover rank -> repeat next day | moderate (Barber et al. 2022) | 1 day | movers lists, daily bars | yes (`priorDayTopMoverRank`, H15) |
| Marketable retail order imbalance (sub-penny prints) | moderate (Boehmer et al. 2021) | ~1 week | SIP trades | yes in principle; not a formation feature here |
| Liquidity shocks before jumps (spread, trade count, imbalance) | moderate, large-cap only (Boudt & Petitjean; Aït-Sahalia) | minutes | quotes + trades | partly (`csSpread`, `trades_k`; no quotes in the study) |
| Promotion accumulation volume before the first tout | moderate in crypto, weak in equities | hours–days | social + volume | partly (`abnVol_j`, `runUp5`; no social) |
| Jump clustering | moderate | minutes–hours | trades | yes (`jumpFlag`, add-on) |
| Scheduled catalysts (earnings, FDA dates) | strong for *when*, not direction | calendar | event calendar | partly (8-K items; no FDA calendar) |
| Extreme volume -> next-month premium | moderate (Gervais et al.) | weeks | daily bars | yes, wrong horizon |
| Google SVI | weak | 2 weeks | external | no |
| Premarket gap / float / RVOL / price screen | anecdotal (practitioner) | 0 (coincident) | minute bars | yes — this is what H1/H9/H11 test |

## What is not predictable, or where the edge is already gone

- Which micro-cap gaps 100% tomorrow with no scheduled catalyst: no public-data signal in either
  survey.
- Buying the open of an attention stock: negative on average (Berkman et al. 2012; Barber et al.
  2022). The open is the retail-overpay price.
- WSB recommendations after GME: zero (Bradley et al. RFS 2024).
- Betting on squeezes via calls: negative (Filippou et al.).
- Lee–Mykland as an *early* detector: it confirms jumps, over-flags the first 30 minutes, and does
  not lead them.
- Stock-volume footprints of informed trading before FDA/M&A: absent where the options footprint is
  present (FDA study; Ahern 2020). This is the prior behind H6 and H11.

## Best-supported exit rules

1. **Sell into the open, not 15 minutes before it.** Berkman et al. 2012 and Barber et al. 2022 put
   the retail overpay at the opening print; the vendor's premarket-HOD median of 08:00 is the only
   source for "earlier", and it is unaudited. H5 and H14 test this directly.
2. **Time stop ~15 minutes after the open.** Vendor HOD distribution (~50% by 09:45, ~70% by 10:30);
   LULD band halving at 09:45 (DERA). H4 tests the 60% version.
3. **Extension-from-open at 09:45 as the sell-vs-trail switch.** Vendor giveback table (< 10% ->
   93% fade; > 100% -> 11%). H13 tests the lower rung.
4. **Exhaustion = trade-to-quote imbalance collapse + VWAP loss.** Aït-Sahalia et al. 2024 for the
   imbalance; VWAP loss is a vendor base rate (~75% close below VWAP). H17 and `vwapLossTime`.
5. **Lower highs, RSI divergence, price velocity: no evidence either way.** H5 is the first test of
   velocity death on this sample and is descriptive by construction.
6. **Short only after no-news or promoter-driven spikes** (Renault 2018; Xiao et al. 2020; Chan
   2003). Halts are the dominant short-side risk (DERA). Deferred.

## Catalyst-to-number scoring rationale

The live `CatalystScorer` (code plan §B) is `clamp((TypePrior × Magnitude × SourceWeight +
NoveltyBonus) × Freshness(minutes), 0, 100)` with direction stored separately. The literature behind
each factor:

- **Type prior** per Lerman & Livnat (8-K items) and Kargarzadeh et al. (coverage categories):
  earnings / guidance / capital returns continue; launch / partnership gives back; 1.03 and
  dilution items are negative; FDA is high-variance with weak direction.
- **Hardness (magnitude)**: whether the item carries a number ("quantified" attribute) and its
  Loughran–McDonald word counts, not Harvard lists (Loughran & McDonald 2011).
- **Source weight**: a wire release *is* the event (no pre-publication drift, Kargarzadeh); EDGAR
  acceptance is public within ~40 s (Rogers et al. 2017); promotional outlets are penalised
  (Kargarzadeh's two outlets at −0.4 to −0.8%/month; Renault's promoter-driven reversal).
- **Novelty**: first report vs follow-up; 55% of articles are follow-ups (Kargarzadeh).
- **Timing**: a wire catalyst after 16:00 or before 09:30 means the gap *is* the move (Barber et al.
  2022; Berkman et al. 2012), so freshness decays fast after the open.

On this stack the scorer sees Benzinga-authored items with `created_at`, likely not raw wire
releases, and no EDGAR in the news stream; the study expects, and will report, how often the first
abnormal-volume minute precedes the first Alpaca headline. The weights themselves are placeholders
until the study sets them from the training split.

## Uncertainty notes

- The HOD-timing percentages are one vendor (SmallCapLab) with internal inconsistencies between
  pages and no methodology; H4, H5 and H13 are written against them as *targets to test*, not facts.
- Xiao, Yin & Zhao 2020 is index ETFs; Boudt & Petitjean 2014 is the DJIA; Gao et al. 2018 is an
  index. Transfer to sub-$20 small caps is assumed, not shown.
- Berkman et al. 2012 and Lou/Polk/Skouras 2019 are all-cap and largely pre-2010 samples.
- The DERA LULD figures are 2013–14 data under the pilot bands.
- Kargarzadeh et al. 2026 is a single-regime arXiv working paper (2023–26).
- The Alpaca press-release-coverage claim rests on two unanswered forum posts.
- Figures marked *(from memory)* or *(secondary)*: Lou et al. t-statistics, Bali et al. MAX size,
  Boehmer et al. magnitude, Frieder & Zittrain's 4% -> 70% volume figure. Several paywalled papers
  were read via abstracts by the surveying agents, not in full.
- Vendor and practitioner win rates (Mometic, TradingView script, ChartMath, QuantSync, Dilution
  Tracker) are unaudited and are cited only to show the spread of claims.
