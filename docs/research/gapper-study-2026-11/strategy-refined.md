# Gapper study: the refined strategy and what is still missing (IP-T468)

Written 2026-11-08. It builds on review rounds 1-4: `literature.md`, `avenues.md`, `methods-onset.md`,
`methods-exit.md`, `methods-catalysts.md`, `methods-prediction.md` and `methods-execution.md`. It
also builds on the frozen `protocol.md`, `hypotheses.json` (H1-H17), amendments 01-03 (H1p-H12p,
H18-H24) and `explore-plan.md`. For this note I did web research and read the repo. No code was
run, nothing was built, and no Alpaca, SEC or idiotproof tool was called, because the study pull is
still running.

This is a research note, not an amendment. Every threshold below is a **candidate to test** on train
through the explore round. None of them is a finding. A rule can reach validation only as one of the
at most five finalists in amendment 04.

The goal, in the user's words: find small-cap premarket gappers ($0.10-$20) while they are forming,
buy in, and sell when velocity runs out, either about 15 minutes before 09:30 or within about a minute
of the bell ("take the money and run").

Evidence labels used below: **peer-reviewed**, **working paper**, **exchange/regulator**, and
**vendor/anecdotal**. *(abstract)* means only the abstract or a search summary was read. *(not
verified)* means I could not read the source itself and the claim must not be cited in results.

---

## 1. What the new sources add or contradict

Rounds 1-4 already cover the overnight/intraday literature, retail herding, pumps, change-point
detection, optimal stopping, LULD, the Nasdaq cross and Alpaca's order rules. The sources below are
new to this study. They are grouped by what they change.

### 1.1 Gap direction: continuation is not ruled out

- **Plastun, Sibande, Gupta & Wohar 2020, *North American J. of Economics and Finance*,
  peer-reviewed** *(abstract)*. Dow, S&P 500 and Nasdaq indices, 1928-2018. On gap days, prices
  tend to move in the gap's direction (momentum). The effect is temporary, and the authors reject the
  folk rule that "gaps get filled". Caporale & Plastun 2017 (*Investment Analysts Journal*,
  peer-reviewed, abstract) report the same across asset classes.
  - **Contradicts** the round-1 "fade prior" only at the index level. Index gaps are a different
    animal from +20% small-cap gaps, so this does not move H13. What it does do is weaken any
    argument that a gap must fill. That justifies keeping a continuation arm (the hold-past-bell
    switch, §2.5) alongside the fade-flavoured exits.
- **Medhat & Schmeling 2022, *RFS* 35(3), peer-reviewed** *(abstract and accepted manuscript
  summary)*. Last month's return reverses in low-turnover stocks but continues ("short-term
  momentum") in high-turnover stocks. They read turnover as disagreement.
  - **Adds** a moderator the study does not test: turnover. The intraday analogue is float rotation
    (`x.floatRotation`, already built). The horizon is monthly and the effect is strongest in large
    liquid names, so transfer to a premarket morning is an assumption. It is a cheap ablation
    interaction, `floatRotation × pmRet`, and that is all.
- **Glasserman, Krstovski, Laliberte & Mamaysky 2025, arXiv 2507.04481, working paper**
  *(abstract)*. Topic models over 2.4M articles explain much of the overnight-up / intraday-down
  pattern from what news arrives overnight versus intraday. Out of sample, they predict which stocks
  outperform overnight and underperform intraday.
  - **Adds** support for treating catalyst *type* as a moderator of the open fade, as H2, H3 and H16
    already do, rather than "news vs no news" alone.
- **Boyarchenko, Larsen & Whelan, NY Fed Staff Report 917 / *RFS* 36(9) 2023,
  exchange/regulator + peer-reviewed** *(abstract)*. US index-futures returns concentrate in the
  early European hours, and they are linked to dealer inventory and to order imbalance at the prior
  US close. A later NY Fed post titled "The disappearing overnight drift" revisits the effect; I did
  not read it *(not verified)*.
  - Relevance is indirect. Overnight returns can be mechanical inventory effects that decay once
    published. Expect regime drift in any overnight-gap edge.

### 1.2 Who trades the premarket now (exchange research)

- **Cboe, "Retail trading in subdollar securities", 2024-11-25, exchange research.** Sub-$1 names
  went from 10.4% of US volume (Q2 2020) to 17.4% (Q2 2024). Premarket reached a record 4.05% of total
  volume in Q2 2024, and **sub-$1 names were 57% of premarket volume**. Retail was about 41-47% of
  subdollar volume.
- **Cboe, "Early birds and night owls", 2025-03-28, exchange research.** Retail is more than half of
  EDGX premarket share. Sub-$1 went from 5% to about 20% of EDGX premarket volume over 2023-2024. The
  top 10 securities are at least half of premarket notional.
- **MEMX insights, 2025, exchange research.** In May 2025, MEMX premarket volume was **60% retail and
  75% sub-$1**. Premarket rose from 5.1% to 7.5% of industry volume, year over year.
- **NYSE (Bazinas 2025)** is already in round 4: premarket volume is growing and spikes at 08:00:00.
  - **Contradiction on trend.** One Cboe post (search summary, *not verified*) says premarket ADV
    fell 65% from its January 2021 peak. NYSE and MEMX report strong growth. The measures differ
    (shares vs share of volume vs one venue), so all of these can be true at once.
  - **What it changes.** The premarket the study trades is a retail, sub-$1 market whose make-up
    shifted across the train window (2021-11..2024-10). That fits round 4's finding that the Tier20
    base rate doubles after train. It argues for **rank-based triggers** (§2.2) and for reporting
    results per price tier: sub-$1, $1-3, $3-20.
- **Eaton, Shkilko & Werner 2025, "Nocturnal trading", working paper** *(secondary summary, not
  verified)*. Trading 20:00-04:00 on Blue Ocean ATS and IBEOS contributes about 9% of 24-hour price
  discovery, mostly in non-US names and ETFs. These sessions are not on the standard consolidated
  feed (a summary says tape inclusion is planned for December 2026; *not verified*).
  - **Adds a blind spot.** Part of a gap can form before 04:00 on venues the study's SIP minute bars
    do not show. For single US small caps the share is probably small, but it is unmeasured.

### 1.3 Retail herding and liquidity

- **Eaton, Green, Roseman & Wu 2022, *JFE* 146, peer-reviewed** *(abstract)*. During Robinhood
  outages, high-retail-interest stocks show smaller order imbalances, better liquidity and lower
  volatility. Outages at traditional brokers show the opposite.
  - **Adds** that inexperienced-retail herding *worsens* liquidity in exactly the names that gap. So
    premarket spreads are likely widest when the signal fires. That supports round 4's call to check
    the frozen cost model against real quotes at entry time (gap list, P1).
- **Ozik, Sadka & Shen 2021, *JFQA* 56(7), peer-reviewed** *(abstract)*. Retail trading offset about
  40% of the 2020 rise in illiquidity, but less so in high-media-attention stocks. Same reading as
  Eaton et al.
- **Schwarz, Barber, Huang, Jorion & Odean 2025, *JF* 80(5), peer-reviewed** *(abstract)*. The
  authors placed identical simultaneous trades through different brokers. Mean round-trip execution
  cost ranged from 0.07% to 0.46% depending on the broker, because wholesalers price the same order
  differently.
  - **Adds** that the cost of a fill depends on the broker. Alpaca routes to wholesalers (round 4),
    so the only valid cost evidence for this system is this account's own fills. Paper trading cannot
    supply it.

### 1.4 Pumps, promotion and dilution

- **SEC press release 2022-221, exchange/regulator.** Eight influencers were charged in a roughly
  $100M scheme. They bought exchange-listed stocks, touted them on Twitter and Discord (Atlas
  Trading, more than 150k members), then sold into the demand.
  - **Adds** that promotion of *listed* small caps through chat rooms is documented. Round 1 cited
    mostly OTC spam. The stack has no social data, so H16's promo flag can catch only press-release
    promotion.
- **Massoud, Ullah & Scholnick 2016, *J. Financial Stability* 26, peer-reviewed** *(abstract)*.
  Secretly paid promotion lifts price and volume on the start date, and the gain reverses once
  regulators act. A blog quotes +11.9% then −32.9% CAAR *(not verified)*.
- **FINRA Regulatory Notice 22-25 (2022) and the SEC Cross-Border Task Force (September 2025),
  regulator.** These target "ramp-and-dump" schemes in small-cap IPOs, mostly foreign issuers with
  tiny public floats. **Nasdaq Rule 5210(l)**, approved by the SEC on 2026-05-14 (law-firm summaries,
  regulator-level), requires a $25M minimum IPO raise for China-based issuers. A trade-press claim
  that foreign micro-IPOs fell from 140 to 13 is *not verified*.
  - **Contradicts the stationarity assumption.** A known gapper type, the low-float foreign
    micro-IPO, was common in train and is being regulated away during the holdout year
    (2025-11..2026-10). Holdout counts per type should be reported, and a `foreignSmallIpoAge`
    feature is worth a look (gap list).
- **Hillion & Vermaelen 2004, *JFE* 71(2), peer-reviewed** *(abstract)*. Floating-price ("death
  spiral") convertibles are followed by significant negative abnormal returns, consistent with short
  selling by the holders.
  - **Adds** a dilution mechanism that the ATM/424B5 detector does not catch: conversions at a
    discount to recent VWAP. These are announced in 8-K items 1.01 and 3.02 and in S-1 resale
    registrations. They supply shares *into* a spike.
- **Gustafson & Iliev 2017, *JFE* 124(3), peer-reviewed** *(abstract)*. The 2008 change that let
  sub-$75M-float firms use shelf registration doubled their reliance on public equity and moved them
  away from PIPEs.
  - **Adds** the empirical base for round 2's baby-shelf mechanism: these firms *do* use the shelf.
- **SEC proposal of 2026-05-19, "Registered offering reform", regulator** *(law-firm summaries: BDO,
  Davis Polk, Goodwin)*. It would remove Form S-3 instruction I.B.6, which contains the baby-shelf
  cap, and the $75M float test. It was **not adopted** as of the latest summaries I found.
  - **Contradicts** the durability of `x.dilBabyShelf` and `x.dilShelfHeadroomFrac`. If the rule is
    adopted, headroom stops binding, and any finalist that leans on it must be re-checked live.
- **Nasdaq reverse-split rules approved 2025-01-17, exchange/regulator** *(law-firm summaries)*.
  There is no automatic compliance period if the company did a reverse split in the prior year, and
  a hearing no longer stays suspension after the second 180-day period. The $0.10-for-10-days fast
  delisting dates from 2020.
  - **Adds** that names near $0.10 carry delisting risk. The universe floor of $0.10 is right at
    that edge.

### 1.5 The prior on day trading itself

- **Barber, Lee, Liu, Odean & Zhang 2020, *Review of Asset Pricing Studies* 10(1),
  peer-reviewed** *(abstract)*. In Taiwan 1992-2006, day traders as a group lose, and 97% are likely
  to lose in future day trading.
- **Chague, De-Losso & Giovannetti 2019, working paper (FGV/SSRN)** *(abstract)*. Of Brazilian
  mini-index futures day traders who persisted more than 300 days, about 97% lost money.
  - **Adds** the honest base rate for the whole enterprise. A strategy that "works" in backtest
    should be presumed wrong until validation, holdout and shadow say otherwise. That is the gate
    the protocol already has.

### 1.6 Searched for and not found

- Peer-reviewed tests of premarket gap-and-go on sub-$20 names, of float rotation as a predictor,
  or of premarket opening-range rules. The literature still has nothing beyond the
  vendor/practitioner items in round 1. Concretum Research has a 2026 post on fading single stocks,
  but it is paywalled *(vendor, not verified)*.
- Market-maker or wholesaler white papers on premarket small-cap liquidity (Citadel Securities,
  Virtu). None are public. The nearest is a Bundesbank paper on German retail market makers
  (working paper, not relevant enough to use).
- Academic work on premarket return predicting the same day's intraday return for single stocks.
  Only index (Gao et al.) and half-hour (Heston et al.) results exist, and both are already cited.

---

## 2. The refined strategy

### 2.0 The shape in one paragraph

Each morning, scan the whole universe every minute from 04:00 to 09:10. Score every name on volume
building against its time-of-day baseline (CUSUM), its cross-sectional rank that morning, a
volume-confirmed gap, its catalyst and its D-1 setup. Buy only names that are **still below about
+10-15%**, rank in the morning's top few, and pass dilution, split and promo filters. Buy with a
marketable extended-hours limit order, capped at 5% participation. Exit mostly **by the clock**:
- an `opg` limit sell submitted by 09:27, with a 09:30:05 regular-hours fallback, or a 09:15
  continuous sale, whichever train favours;
- a drift-change (Shiryaev-Roberts) overlay and a volatility-scaled giveback can sell earlier, and
  both tighten once the trade-count/imbalance collapse appears;
- never hold into the 09:45 LULD band contraction.

### 2.1 Universe and pre-filter at 04:00

**Universe U(D)** is the protocol's definition, with one live change:
- prior close in [$0.10, $20);
- ADV20$ >= $300,000;
- at least 5 of 20 sessions with a bar;
- the live change: exclude a split ex-date in [D−25, **D**] (the protocol uses D+1, which a live
  system cannot know).

**Pre-filter (D−1 setup score, used for ordering and the watch list, never as a hard gate).**
At 04:00, compute the following for every U(D) member:
- `abnVol_1` (H6);
- `priorDayTopMoverRank` (H15);
- `x.haltPausesD1` (A1);
- news since the 16:00 close (`x.haltNewsSinceClose` and the catalyst block);
- scheduled-catalyst flags: earnings expected today from the 8-K 2.02 cadence (C4), PDUFA text (C1);
- SSR in force today (`explore ssr`).

`setup(D) = logistic(abnVol_1, topMoverRank ≤ 20, haltsD1 > 0, newsSinceClose, earningsToday)`,
fit on train folds. Its only job is to order the scan and to serve as a feature in §2.2. Candidate
test: does the top 200 by `setup` hold at least 60% of forming gappers? This is track 1, as a
descriptive check.

**Hard exclusions at 04:00** (each is a no-trade filter, §2.4): split in [D−25, D], active T1/H10
halt, OTC or not `tradable`, warrants/units/rights (name or symbol suffix), and prior close < $0.20
with a quoted spread above 2% at the decision minute.

### 2.2 The forming-gapper score

Computed every minute t from 04:00 to 09:10, on bars with `EndUtc <= t`.

**(a) Onset accumulator (methods-onset P1).** For minute m:
```
e_m  = ADV20 × (profile_decile(m) − profile_decile(m−1))
z_m  = min( ln((v_m + c) / (e_m + c)), ln 20 ),   c = ADV20 × 1e-5
S_m  = max(0, S_{m−1} + z_m − ln 2)                # cusumVol
```
The same chart runs on trade count `n_m`, using a trade-count time-of-day profile (`cusumTrades`).
- Threshold `h` is set on **train control mornings only**. Candidates: 1 false alarm per 20 or per
  50 control mornings (04:00-09:30).
- Feature `minsSinceCusumAlarm(t)`.

**(b) Market-day normalisation and rank (P2).**
- `pmVolRatioMkt(t) = pmVolRatio(t) / median over U(D) of pmVolRatio(t)`.
- Percentile ranks of `pmVolRatio(t)`, `pmRet(t)` and `cusumVol(t)` within U(D) at t.
- In the explore panel, U(D) is the candidates plus the weighted 10% sample, so the rank is a
  weighted estimate. Live, it is exact.

**(c) Volume-confirmed gap (P4).**
- `pmVwapRet(t)`;
- `conf(t) = pmVwapRet(t) / pmRet(t)`;
- the share of `pmDollarVol(t)` traded above `prevClose × 1.10`.

**(d) Velocity:** `v_3` and `v_15` on log prices minus `σ²/2` (methods-exit 1.4), and `accel`.

**(e) Float:** `x.floatRotation`, `x.floatLogShares`, plus the turnover interaction from §1.1.

**(f) Catalyst:**
- fresh item `pubToTMinutes ≤ 60`, with repaired wire times once N1 exists;
- `catalystType`, `sourceTier`, `novelty`;
- `newsHaltPm` and `has6kPm`.

**(g) Sympathy:** `x.sympathyFormed20` and `x.sympathyForming10`.

**(h) Supply/constraint block (as features, beside the hard filters):**
- the `x.dil*` set;
- `x.si*`;
- SSR today;
- Reg SHO threshold flag and FINRA daily short-volume ratio (IP-T469).

**Model.** Weighted logistic and the in-repo `GradientBoostedTrees` (shallow, strongly regularised),
fit per fold on train (amendment 01 weights), giving the score `s(t)`.

**Trigger by rank, not probability.** The base rate doubles after train (methods-prediction), so a
fixed probability threshold would fire about twice as often live. Use instead:
`r(t)` = rank of `s(t)` among U(D) members that have not yet alarmed and are still forming.

**Alarm** at the first t where all of these hold:
```
06:00 ≤ t ≤ 09:10                                       window, candidates {04:00, 06:00, 07:00} start
AND cusumVol(t) ≥ h  OR  cusumTrades(t) ≥ h'
AND r(t) ≤ k                                            k ∈ {3, 5, 10}
AND gLo ≤ pmRet(t) < gHi                                gLo ∈ {2%, 5%}, gHi ∈ {10%, 15%}
AND pmDollarVol(t) ≥ $50,000                            the protocol's tier floor
AND conf(t) ≥ 0.5                                       most of the gap actually traded through
AND no §2.4 filter fires
```

**Scoring the detector.** This is P0 plus the round-4 benchmarks.
- Lead to +10% and to +20%.
- Price already moved at the alarm, `pmRet(τ)`.
- False alarms per 100 mornings.
- Within-morning precision@k against B1 (news), B2 (top gainers) and **B3 (gainers + news)**.
- A detector that cannot beat B3 is not a finding. In that case the honest product is "screen plus
  news", and amendment 02's H18 news ride is the strategy.

### 2.3 Entry

- **When.** At the open of the next bar after the alarm minute (protocol entry rule). Track 4 entry
  variants stay in play: buy now, first pullback to VWAP, premarket-high breakout. The 08:00:00-08:00:05
  liquidity spike (NYSE) is a candidate *avoid* window, {none, skip 07:59:30-08:00:30}, because a
  flood of queued retail orders sets that price.
- **Order.** Alpaca `limit`, `time_in_force=day`, `extended_hours=true`. Before 09:30, Alpaca allows
  no market orders and no stops.
  - Limit `L_buy = ask + max(1 tick, 0.25 × (ask − bid))`.
  - Cancel if unfilled after 30 s.
  - One re-price is allowed, only if the ask is still within +2% of the alarm price. Otherwise
    abandon; a gapper that ran away is not chased.
- **Backtest fill rule** (round 4, here as the *primary* rule, not a sensitivity): the buy fills at
  `L_buy` only if a bar within 60 s of the decision has a low ≤ `L_buy`; otherwise no trade. Report
  the frozen next-bar-open rule beside it.
- **Size cap by participation.**
  ```
  shares = floor( min( N / price,
                       p × V5(t),
                       p × Vexit(t) ) ),        p = 5% (candidate {2%, 5%, 10%})
  V5(t)    = shares traded in (t−5, t]
  Vexit(t) = pmCumVol(t) × (profile(09:27) − profile(09:20)) / profile(t)   # forecast exit-window volume
  N        = $1,000 notional during study and shadow
  skip if shares × price < $200
  ```
  `Vexit` is the binding side (round 4 §4.3). Selling the whole morning's position in the last
  minutes is where capacity runs out.

### 2.4 No-trade filters

Each filter is a candidate. It is tested as a *removal* from the base strategy's trades: does removing
these trades raise mean net per trade with a day-clustered interval? A filter that removes nothing
measurable is dropped. Filters do not stack untested.

| filter | rule (candidate) | basis | maps to |
|---|---|---|---|
| ATM live | `x.dilAtmActive` and `x.dilShelfHeadroomFrac > 0` | round 2 A2; Gustafson & Iliev 2017; Billett et al. 2019 | dilution block (explore); **A04** |
| fresh offering | 424B* in prior 10 days, or S-1/A, F-1/A or EFFECT in prior 5 days, or a 6-K since the close with offering words | H10; A2 | H10 (frozen); **A04** for the filter form |
| toxic convertible | 8-K 1.01/3.02 or S-1 resale in prior 90 days with "conversion price … lower of / discount to VWAP" | Hillion & Vermaelen 2004 | **new feature + A04** |
| reverse split | live: ex-date in [D−25, D] → no trade; study evidence covers only days 26-30 (methods-prediction G2) | H7; C6 | H7 (frozen, thin); the live rule is **precautionary, untested** |
| promo | `catalystPreClass = PromoPr` or source on `promotionalSources`, or `promoSourceShare` top decile | H16; Massoud et al. 2016; SEC 2022-221 | H16 (frozen); **A04** for the filter form |
| foreign micro-IPO | listed < 180 days, IPO < $25M, foreign issuer | FINRA 22-25; Nasdaq 5210(l) | **new feature, explore only** (regime flag) |
| SSR | **not** a long filter by default. Barardehi et al. 2025 find higher returns under SSR. Test both signs as a feature; a hard block only for any future short leg | avenues A5a | explore "short-sale constraints"; short leg deferred |
| Reg SHO threshold | on today's list (the file dated D−1) | A5c; IP-T469 | **explore, after IP-T469** |
| delisting edge | prior close < $0.20 and a bid-price deficiency (8-K 3.01) or a reverse split in the prior 365 days | Nasdaq 2020/2025 rules | **new feature, explore only** |
| halt state | T1/T12/H10 halt active, or a halt resumed < 5 min ago | A1; Christie et al. 2002 | halts block (built) |

### 2.5 Exit: clock first, overlays second

Round 3's analytic result stands. At 1-minute volatility of about 2%, the price path cannot locate a
drift reversal within 15 minutes, so the clock carries most of the decision. The overlays only sell
*earlier* than the clock. They never extend a hold, except through the one explicit hold-past-bell
switch.

**Guards (always on, computed by the Monitor; no broker stop exists before 09:30):**
- hard stop: `c_t ≤ entry × (1 − s)`, with `s ∈ {8%, 12%}`;
- VWAP loss (the frozen ladder);
- every guard exit is sent as a marketable limit at `bid − 1 tick`, re-priced every 10 s.

**Overlay A: drift-change exit (methods-exit 1.3, rank 1).**
- `R_t = (1 + R_{t−1}) × L_t` on 1-minute log returns.
- `μ2` = `v_15` at arming; `μ1` = the train post-high drift; `σ` = the rolling 15-minute standard
  deviation.
- `λ(t)` = the train hazard of the high-of-day time.
- Sell when the posterior `p_t ≥ p*`, with `p* ∈ {0.5, 0.7, 0.9}`.
- Run the `snr = μ2/σ` split diagnostic first. If velocity rules beat the clock only in high-snr
  names, enable the overlay only when `snr` is above the train median.

**Overlay B: volatility-scaled giveback (rank 4).** Sell when `c_t ≤ H_t − k × σ1m × √5`, where
`H_t` is the running high and `k ∈ {3, 5}`.

**Imbalance-collapse state (H17; trade-flow explore).**
- Trigger: `tradesRoc ≤ −0.5`, or, where trade flow exists, the 5-minute retail imbalance flipping
  negative.
- Effect: the state is not an exit. From that minute, `λ(t)` is multiplied by 3 and `k` drops from 5
  to 3.
- Reason: the explore run found the collapse about 24.5 minutes before the high, at 86.6% of it.
  As a sell signal it fires too early; as a warning it is useful.

**Clock exit (one of two routes; train picks, paired per event, H14 and methods-exit rank 2):**

*Route (a), continuous premarket:*
- Sell by `T ∈ {09:15, 09:20}` with a marketable extended-hours limit.

*Route (b), opening cross:*
- **09:26:30**: cancel any resting premarket sell. Alpaca rejects sells beyond the available
  quantity, so a working order and an `opg` order cannot both hold the same shares.
- **by 09:27:00**: submit `opg` limit sell at `L = last × (1 − x)`, with `x ∈ {3%, 5%}`. Alpaca
  rejects `opg` orders after 09:28. Never use a market-on-open: it has no protection if the cross
  prints far below the premarket.
- **09:30:05**: if the `opg` order came back cancelled or unfilled (no cross, failed Nasdaq price
  validation B/C, or limit not reached), send a regular-hours marketable limit at `bid`, re-priced
  every 10 s. At 09:31:00, send a market order.

**Hold-past-bell switch (rank 5, low prior; the only continuation arm):**
- Allowed only if route (b) was skipped by rule.
- Keep the position past 09:30 only if the 09:30-09:34 bar closes above both the open and the
  premarket high. Otherwise sell at 09:35.
- While held, Overlays A and B apply, with the RTH spread.

**LULD awareness (from 09:30 only; bands are doubled until 09:45):**
- **09:43-09:44 band-contraction exit (rank 3).** Compute `ref` = the mean close of the last 5
  minutes. If the last 3 minutes' high-low range exceeds `0.8 ×` the post-09:45 band (10%; 20% for
  $0.75-3; lesser of $0.15 or 75% below $0.75), sell before 09:45.
- **Absolute last exit 09:44.** Nothing in this strategy is held into 09:45-09:50, which holds 36.4%
  of Tier 2 pauses (DERA 2017).
- **Caught in an up-pause:** never add. Cancel and replace the resting order at the reopen, sell in
  the first minute after it, and fill at the first bar open after the gap (the frozen halt rule).

### 2.6 Risk and capacity

- **Per trade:** $1,000 notional, `p = 5%` participation (§2.3). The thinnest qualifying name, at the
  $50k premarket dollar floor, allows about $2,500 in total (round 4), so $1,000 is already near the
  cap.
- **Per day:** at most 5 entries and 3 open positions. A daily loss stop of 3 × $1,000 × `s` halts
  new entries for the day.
- **Process risk:** with no broker stops before 09:30, everything depends on the Monitor.
  - If the Monitor heartbeat is lost for more than 60 s while a position is open, a watchdog
    flattens with marketable limits.
  - On restart, open gapper positions are reconciled and closed before any new entry.
- **Kelly:** empirical fractional Kelly on train trades is reported as an upper bound only (round 4
  §4.2). Capacity binds first.
- **Mode:** paper only through validation, holdout and shadow, as in the protocol. Live needs the
  protocol's live gate *and* the user's explicit go-ahead.

### 2.7 Rule-to-hypothesis map

| rule | maps to | status |
|---|---|---|
| D−1 setup ordering | H6, H15, A1 halts, C4 | frozen (H6, H15); setup score is explore track 1/2 |
| `pmVolRatio`, velocity in the score | H1/H1p, H9/H9p, H11/H11p | frozen |
| CUSUM onset, P0 alarm-time scoring | explore track 1/2 | **needs A04** |
| cross-sectional rank, `pmVolRatioMkt` | explore track 2 | **needs A04** |
| volume-confirmed gap `conf(t)` | explore track 2 | **needs A04** |
| beat B3 (gainers + news), within-morning precision@k | explore scoring (IP-T465, built) | **A04 finalist bar** |
| Corobs as an extra score input | H21-H24 | frozen (A03) |
| news-only alternative | H18-H20 | frozen (A02) |
| entry variants, limit-touch fill, participation cap | explore track 4; methods-execution | **needs A04** |
| clock exit 09:15 vs 09:28 vs cross vs 09:31 | H12/H12p sell-by grid, H14 | frozen in part; the `opg` + 09:30:05 route **needs A04** |
| drift-change overlay, giveback, imbalance state | H5, H17, explore track 5 | descriptive frozen; the rules **need A04** |
| hold-past-bell switch, band-contraction exit | H4, H13, explore track 5 | descriptive frozen; the rules **need A04** |
| dilution / ATM / toxic-convert filters | H10; dilution block | H10 frozen; filter forms **need A04** |
| reverse-split filter | H7 | frozen but thin (G2); the live rule is precautionary |
| promo filter | H16 | frozen; filter form **needs A04** |
| SSR, Reg SHO, short interest | short-sale block | explore only |
| short leg | track 6 | deferred by protocol |

Most of this cannot be one finalist. §4 says how to pick five.

---

## 3. Gap list

Paths follow the repo layout:
- `IdiotProof.Evidence/Gappers` holds pure features and rules shared with live;
- `IdiotProof.Core/Research/Gappers` holds stages and explore;
- `tools/GapperStudy` is the CLI;
- `IdiotProof.Core/Monitoring/Formation` holds the live recorder (`GapperFormationWorker`,
  `FormationTriggerRule`).

"Needs SEC/Alpaca" means the work must wait until the running study pull finishes.

**Already present (not repeated):** halts (A1), FINRA short interest, SEC companyfacts float proxy,
dilution filings and ATM text, trade flow on gapper mornings, news, EDGAR headers, Form 4, sympathy,
SSR, Corobs, screen benchmarks and within-morning metrics (IP-T465).
**Being built under IP-T469:** Reg SHO threshold lists and FINRA daily short volume.

### 3.1 Code to write

| pri | item | likely file / class | needs SEC/Alpaca |
|---|---|---|---|
| P1 | Count CUSUM on volume and trade count, plus a trade-count time-of-day profile fit on train controls | `IdiotProof.Evidence/Gappers/CountCusum.cs`; extend `TimeOfDayProfile.cs`; register `x.cusumVol`, `x.cusumTrades`, `x.minsSinceCusumAlarm` in `GapperFeatureRegistry.cs` | no (bars in store) |
| P1 | Cross-sectional rank and market-day normalisation at t | `IdiotProof.Core/Research/Gappers/Explore/ExploreDerived.cs` (weighted rank over the panel); live twin in `GapperFormationFeatures.cs` | no |
| P1 | Volume-confirmed gap features `pmVwapRet`, `conf`, dollar share above +10% | `GapperFormationFeatures.cs` / `MinuteBarMath.cs` | no |
| P1 | P0 alarm-time evaluation (lead to +10/+20%, `pmRet` at alarm, false alarms per 100 mornings, operating curve per fold) | `IdiotProof.Evidence/Gappers/AlarmTimeEvaluator.cs`; `Explore/GapperExploreStage.Alarms.cs`; `explore alarms` verb in `tools/GapperStudy/Program.cs` | no |
| P1 | Drift-change exit with a time-of-day hazard, giveback, and imbalance state | `IdiotProof.Evidence/Gappers/DriftChangeExitRule.cs`, `HodHazard.cs`, `GivebackExitRule.cs` beside `VelocityExitRule.cs`; wired into `GapperRuleSimulator.cs` | no |
| P1 | Exit-route simulator: continuous 09:15/09:20, `opg` by 09:27 with 09:30:05 fallback, 09:31; limit-touch fills both sides; halt-aware | `IdiotProof.Evidence/Gappers/GapperExitRouteSimulator.cs`; `GapperEntrySimulator.cs` fill rule | no (uses minute bar 09:30 open until auction prints exist) |
| P1 | Participation cap and capacity curve ($1k/$5k/$25k) | `IdiotProof.Evidence/Gappers/ParticipationCap.cs`; used by both simulators | no |
| P1 | Look-ahead sensitivity universe (unexplained-move guard to D−1, G1) and the live split window [D−25, D] | in-memory variant in `GapperUniverseBuilder.cs`, explore-only | no |
| P1 | Ledger: per-month metrics, lockbox (last two months of fold 4), effective trial count (if not already done under IP-T465) | `Explore/ExploreLedger.cs`, `ExploreFolds.cs` | no |
| P2 | No-trade filter evaluator (removal test per filter, day-clustered) | `Explore/GapperExploreStage.Filters.cs` | no |
| P2 | Toxic-convertible detector (regex over 8-K 1.01/3.02 and S-1 text, with a hand-labelled check set) | `IdiotProof.Evidence/Gappers/ToxicConvertDetector.cs` beside `DilutionFilings.cs`; pull tier in `GapperPullStage.Dilution.cs` | yes (SEC text fetch) |
| P2 | ATM termination 8-K detection (known gap in round 2's detector) | `DilutionFilings.cs` | yes (SEC text) |
| P2 | Earnings-expected flag from 8-K 2.02 cadence (C4) | `IdiotProof.Evidence/Gappers/EarningsCadence.cs` | no if the EDGAR index already holds 2.02 rows; yes for missing headers |
| P2 | Wire-time repair (N1) and scanner-lead report (N2/N3) | `IdiotProof.Core/Research/Gappers/WireTimeRepair.cs`; `pull --tier wire-times` | no (wire sites, ~1 req/s, respect terms) |
| P2 | Live shadow recorder: NBBO bid/ask/sizes at each decision minute, at alarm, at order send/fill/cancel, and the `opg` outcome; heartbeat watchdog | `IdiotProof.Core/Monitoring/Formation/ShadowQuoteRecorder.cs`; `FormationTriggerRule.cs` | yes (live Alpaca stream; start only after the pull and with the user's go-ahead) |
| P2 | Hold-past-bell switch and 09:43-09:44 band-contraction exit | `GapperExitRouteSimulator.cs` | no |
| P3 | Foreign micro-IPO and delisting-edge regime flags | `GapperSnapshotExtras.cs` | no (EDGAR index held) for F-1/IPO dates |
| P3 | PDUFA-date text flag (C1) | `IdiotProof.Evidence/Gappers/PdufaText.cs` over held news/8-K text | no |
| P3 | Clock-time onset hazard (P3) | `HodHazard.cs` reuse | no |

### 3.2 Data to collect

| pri | data | source / URL | cost | date range | needs SEC/Alpaca |
|---|---|---|---|---|---|
| P1 | Opening (and closing) auction prints for train Tier20/Tier50 days, plus the matched controls | Alpaca `GET https://data.alpaca.markets/v2/stocks/auctions` | free (SIP entitlement held) | 2021-11-01..2024-10-31 (train); validation/holdout days only when amendment 04 runs | **yes (Alpaca)** |
| P1 | NBBO quotes ±60 s around every simulated entry and exit of the explore finalists (cost model check, depth at touch, hour-of-day spread) | Alpaca `GET /v2/stocks/{s}/quotes` | free | train only, finalists' trades | **yes (Alpaca)** |
| P1 | Trade-count minute profile | minute bars already in the store (`n` field) | free | train controls | no |
| P1 | Reg SHO threshold lists and FINRA daily short volume | IP-T469 (`nasdaqtrader.com/dynamic/symdir/regsho/…`, `cdn.finra.org/equity/regsho/daily/…`) | free | 2021-10..2026-10 | no (in progress) |
| P2 | SIP trades for **control and sample** mornings 07:00-09:30 (rush-order / odd-lot / TRF features usable as detection features) | Alpaca `GET /v2/stocks/trades` | free; large storage | train, then validation/holdout only for finalists | **yes (Alpaca)** |
| P2 | Wire release pages for batch-stamped items | prnewswire.com, globenewswire.com, businesswire.com, accesswire.com (URLs already stored on the items) | free; site terms | 2021-11..2024-10 first | no |
| P2 | 8-K 1.01/3.02 and S-1 text for toxic-convert detection; ATM termination 8-Ks | EDGAR Archives document URLs | free | 365 days before each target day | **yes (SEC)** |
| P2 | Forward daily snapshot of Alpaca asset flags (`easy_to_borrow`, `shortable`, `borrow_status`) for U(D) at 08:00 | Alpaca `GET /v2/assets` | free | from now, forward | **yes (Alpaca)** |
| P2 | SEC trading suspensions list | https://www.sec.gov/litigation/suspensions.htm | free | 2021-11..2026-10 | **yes (SEC site)** |
| P3 | FDA advisory-committee dates | Federal Register API (`federalregister.gov/api/v1`) | free | 2021-11..2026-10 | no |
| P3 | Reddit mentions (first-mention vs first-abnormal-minute, research only) | Arctic Shift dumps (Academic Torrents) | free | 2021-11..2024-10 | no |
| P3 | Overnight-session (20:00-04:00) prints | Blue Ocean ATS via ICE; not on the SIP feed Alpaca resells today | paid | n/a | no (paid; out of scope) |
| P3 | Nasdaq NOII / TotalView imbalance | Nasdaq TotalView-ITCH | paid | n/a | no (paid; out of scope) |

---

## 4. Validation path

The frozen pre-registration governs. Nothing here edits `protocol.md`, `hypotheses.json` or
`params.json`.

1. **Finish the current chain.** That means the pull, snapshots, labels, and the confirmatory
   train/validation tests of H1-H24, all reported as registered, failures included. The H11 kill
   criterion and H11p apply as written. If the detector does not beat the catalyst benchmark,
   strategy work on the detector stops, and H18 (news ride) is the remaining route.
2. **Explore on train only** (code-guarded split, every trial in the ledger):
   - folds 1-4 rolling, with the last two months of fold 4 kept as a lockbox;
   - P0 alarm-time curves first, then the §2.2 ablations, then entry and exit grids on the
     surviving detector;
   - every strategy result is reported under both fill rules and at $1k/$5k/$25k;
   - per month and leave-one-month-out;
   - the power check before registering a finalist (round 4).
3. **Amendment 04: at most five finalists**, registered and pushed before any of them touches
   validation. A suggested slate, so the five slots are not spent on near-duplicates:
   - F1 **detector**: CUSUM + rank model vs **B3**. Bars: ΔAUC ≥ 0.05 (mean of folds), and
     within-morning precision@10 above B3.
   - F2 **core strategy**: F1's alarm with the §2.3 entry, and the clock exit route chosen on train.
     Bar: ≥ +1.0%/trade net under the limit-touch fill rule, lower bound > 0, ≥ 150 trades on ≥ 40
     days.
   - F3 **exit overlay**: drift-change plus giveback vs the pure clock on F2's entries, paired.
     Minimum effect registered at about half the train effect.
   - F4 **exit route**: `opg` by 09:27 with the 09:30:05 fallback vs continuous 09:15/09:20, paired.
     Needs the auction prints.
   - F5 **one filter**: the single no-trade filter with the largest train removal effect, most likely
     ATM/dilution.
4. **One validation test** of amendment 04. SPA and DSR use the ledger's effective trial count.
   There is no re-fit and no second try.
5. **One holdout run**, frozen, with amendments 01-04 together, under `HoldoutLock`. Report the
   holdout separately per price tier and per gapper type, given the regime changes in §1.2 and §1.4.
6. **Promotion.** Survivor thresholds go into `formation-trigger.json`. Arming stays off until the
   user says so.
7. **20 paper-shadow trading days.**
   - `GapperFormationWorker` runs the same code.
   - `ShadowQuoteRecorder` stores NBBO bid/ask and sizes at each decision minute, at alarm, at every
     order event, and the `opg` result.
   - Live features must match the backtest's on the same minutes. A mismatch is a data bug to
     explain, never a parameter to tune.
   - Alpaca offers top-of-book only, so "depth" here means NBBO size. Paper fills are quote touches
     (round 4), so the shadow can confirm signals and quoted costs but **cannot** confirm fills.
     Measuring real fill quality needs this account's own live fills (Schwarz et al. 2025). Whether
     and how to take that step is the user's decision after the live gate. It is not part of this
     plan.

Kill and stop rules stay as frozen. Any edit to frozen definitions after the unlock voids the
affected hypotheses.

---

## Sources

New in this note. Labels: peer-reviewed / working paper / exchange-regulator / vendor-anecdotal;
*(abstract)* = abstract or search summary only.

- Plastun, Sibande, Gupta & Wohar 2020, "Price gap anomaly in the US stock market: the whole story", *NAJEF* — peer-reviewed *(abstract)* — https://repository.up.ac.za/handle/2263/78336
- Caporale & Plastun 2017, "Price gaps: another market anomaly?", *Investment Analysts Journal* — peer-reviewed *(abstract, via search)* — https://bura.brunel.ac.uk/bitstream/2438/25828/3/FullText.pdf
- Medhat & Schmeling 2022, "Short-term momentum", *RFS* 35(3):1480-1526 — peer-reviewed *(abstract)* — https://econpapers.repec.org/RePEc:oup:rfinst:v:35:y:2022:i:3:p:1480-1526. ; https://openaccess.city.ac.uk/id/eprint/31278/
- Glasserman, Krstovski, Laliberte & Mamaysky 2025, "Does overnight news explain overnight returns?", arXiv 2507.04481 — working paper *(abstract read)* — https://arxiv.org/abs/2507.04481
- Boyarchenko, Larsen & Whelan, "The overnight drift", NY Fed Staff Report 917; *RFS* 36(9) 2023 — regulator + peer-reviewed *(abstract)* — https://www.newyorkfed.org/medialibrary/media/research/staff_reports/sr917.pdf ; https://ideas.repec.org/a/oup/rfinst/v36y2023i9p3502-3547..html
- Cboe, "U.S. equities volume drivers: retail trading in subdollar securities", 2024-11-25 — exchange research (read) — https://www.cboe.com/insights/posts/u-s-equities-volume-drivers-retail-trading-in-subdollar-securities
- Cboe, "Early birds and night owls", 2025-03-28 — exchange research (read) — https://www.cboe.com/insights/posts/early-birds-and-night-owls-how-extended-trading-hours-are-reshaping-u-s-equities-markets-/
- MEMX, "Pre-market share gains…", 2025 — exchange research (read) — https://memx.com/insights/pre-market-share-gains-and-new-options-active-risk-feature
- NYSE, "The early bird gets the worm" (already in round 4) — exchange research — https://www.nyse.com/research/insights/the-early-bird-gets-the-worm-a-new-normal-in-off-hours-us-equities-trading
- Eaton, Shkilko & Werner 2025, "Nocturnal trading" — working paper *(secondary summary, not verified)* — https://haslam.utk.edu/wp-content/uploads/2024/11/Eaton-Paper.pdf ; https://www.neudata.co/literature-reviews/trading-in-the-dark-retail-flow-in-the-overnight-session
- Eaton, Green, Roseman & Wu 2022, "Retail trader sophistication and stock market quality", *JFE* 146:502-528 — peer-reviewed *(abstract)* — https://ideas.repec.org/a/eee/jfinec/v146y2022i2p502-528.html
- Ozik, Sadka & Shen 2021, "Flattening the illiquidity curve", *JFQA* 56(7):2356-2388 — peer-reviewed *(abstract)* — https://econpapers.repec.org/RePEc:cup:jfinqa:v:56:y:2021:i:7:p:2356-2388_3
- Schwarz, Barber, Huang, Jorion & Odean 2025, "The 'actual retail price' of equity trades", *JF* 80(5):2507-2541 — peer-reviewed *(abstract)* — https://econpapers.repec.org/RePEc:bla:jfinan:v:80:y:2025:i:5:p:2507-2541 ; https://papers.ssrn.com/abstract=4189239
- SEC press release 2022-221 (influencer Twitter/Discord scheme) — regulator — https://www.sec.gov/news/press-release/2022-221
- Massoud, Ullah & Scholnick 2016, "Does it help firms to secretly pay for stock promoters?", *J. Financial Stability* 26:45-61 — peer-reviewed (abstract read) — https://econpapers.repec.org/RePEc:eee:finsta:v:26:y:2016:i:c:p:45-61
- FINRA Regulatory Notice 22-25 (small-cap IPO price spikes) — regulator — https://www.finra.org/rules-guidance/notices/22-25
- SEC/FINRA cross-border small-cap oversight, 2025 (law-firm summary) — regulator-level *(secondary)* — https://www.foley.com/zh/insights/publications/2025/12/sec-and-finra-increase-oversight-of-cross-border-small-cap-offerings/
- Nasdaq Rule 5210(l) $25M IPO minimum for China-based issuers, SEC approval 2026-05-14 (law-firm summary) — exchange/regulator *(secondary)* — https://www.gtlaw.com/es/insights/2026/5/sec-approves-nasdaqs-25-million-ipo-requirement-for-china-based-companies
- Trade-press claim of foreign micro-IPO collapse — vendor/anecdotal *(not verified)* — https://cryptobriefing.com/foreign-company-us-ipos-sec-crackdown/
- Hillion & Vermaelen 2004, "Death spiral convertibles", *JFE* 71(2):381-415 — peer-reviewed *(abstract)* — https://ideas.repec.org/a/eee/jfinec/v71y2004i2p381-415.html
- Gustafson & Iliev 2017, "The effects of removing barriers to equity issuance", *JFE* 124(3):580-598 — peer-reviewed *(abstract)* — https://econpapers.repec.org/RePEc:eee:jfinec:v:124:y:2017:i:3:p:580-598
- SEC "Registered offering reform" proposal, 2026-05-19 (law-firm summaries) — regulator-level *(secondary)* — https://arch.bdo.com/sec-proposes-registered-offering-reform ; https://www.davispolk.com/insights/client-update/sec-proposes-reforms-registered-offerings-and-public-company-reporting ; https://www.goodwinlaw.com/en/insights/publications/2026/05/alerts-practices-pca-sec-proposes-significant-changes-to-registered-offering-framework
- Nasdaq reverse-split / bid-price rule changes (SR-NASDAQ-2024-045; K&L Gates summary) — exchange/regulator — https://listingcenter.nasdaqomx.com/assets/rulebook/nasdaq/filings/SR-NASDAQ-2024-045.pdf ; https://www.klgates.com/The-State-of-Play-for-Reverse-Stock-Splits-by-Nasdaq-and-NYSE-Listed-Issuers-1-23-2025
- Nasdaq $0.10 accelerated delisting (2020; Orrick summary) — exchange/regulator *(secondary)* — https://www.orrick.com/Insights/2020/04/Accelerated-Delisting-Process-for-Nasdaq-Issuers-with-Bid-Price-Compliance-Issues
- Barber, Lee, Liu, Odean & Zhang 2020, "Learning, fast or slow", *RAPS* 10(1):61-93 — peer-reviewed *(abstract)* — https://ideas.repec.org/a/oup/rasset/v10y2020i1p61-93..html
- Chague, De-Losso & Giovannetti 2019, "Day trading for a living?" — working paper *(abstract)* — https://econpapers.repec.org/RePEc:spa:wpaper:2019wpecon47
- SEC staff report on equity and options market structure in early 2021 — regulator *(press release and summaries; not used for numbers)* — https://www.sec.gov/news/press-release/2021-212
- Zarattini, Barbon & Aziz 2024 (already cited; universe filters still unread) — working paper — https://ideas.repec.org/p/chf/rpseri/rp2498.html
- Concretum Research, "Identifying stocks to fade", 2026-05-23 — vendor, paywalled *(not verified)* — https://concretumgroup.substack.com/p/identifying-stocks-to-fade

Carried from rounds 1-4 and used above (see those files for URLs and verification status): Barardehi
et al. 2025 (SSR); Moise & Flaherty, SEC DERA 2017 (LULD); LULD Plan 2025 annual report; Nasdaq
Opening Cross FAQ; Alpaca "Orders at Alpaca" and paper-trading docs; Ekström & Lindberg 2013;
Moustakides 1986; Barclay & Hendershott 2003/2004/2008; Billett, Floros & Garfinkel 2019; Hong,
Scheinkman & Xiong 2006; Barber et al. 2022; Berkman et al. 2012; Aït-Sahalia, Li & Li 2024.

## Not verified in this pass

- Any figure taken from a search summary rather than the source: the Massoud et al. CAAR figures,
  the Eaton-Shkilko-Werner 9% and the December 2026 tape date, the Cboe "premarket ADV −65%" claim,
  the foreign IPO 140→13 count, and the NY Fed "disappearing overnight drift" conclusions.
- Whether the SEC adopted the 2026 offering reform. Whether Nasdaq's proposed $15M public-float
  minimum for small IPOs was approved.
- Zarattini et al.'s stocks-in-play filters.
- Plastun et al.'s strategy returns. Only the abstract-level direction was read.
- All thresholds in §2. They are candidates by construction.
