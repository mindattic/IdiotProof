# Gapper study, amendment 13: buying the evening before (the close-to-open hold)

Registered 2026-11-21 (IP-T632), before any A13 number existed: no close-to-open return had been computed for
any list, on any split, and the script that computes one (`tools/gapper-study/a13_overnight.py`) had not been
written. The machine-readable form is `amendment-13.json`; where the two differ, the JSON governs. No earlier
file is edited, no frozen hypothesis, threshold or verdict changes, and the holdout stays locked. Research only:
nothing here arms, sizes, places or exits an order.

## Why

Every long rule this study has tested buys **after** the move is visible (premarket from 04:00, or after a
regular-hours ignition) and holds **intraday**: H12/H12p, H18, amendment 04's entries, amendment 07's sequences,
the watchlist ride (2,592 configurations), H29 and H30. All lose 2-8%/trade after costs. The literature the
study already cites says that is the wrong leg: attention and hard-to-arbitrage stocks earn their return
**overnight** and give it back **intraday** (Berkman, Koch, Tuttle & Zhang 2012; Lou, Polk & Skouras 2019; the
open is "systematically the worst price of the day" to buy). The owner's own framing is the same: "somebody
knows to buy before it goes up". The study can already rank tomorrow's gappers the evening before (D-1 16:00
boosted AUC 0.899, `runs/explore/visibility-20261109-1604.md`) but has never asked whether holding that list
from the close to the next open pays. This is a different mechanism, not another variant of the ride.

## Question

Does buying a small evening list at the D-1 close and selling at the D open net money after costs?

## Honest prior

Probably not, about one chance in four. The boring answer: the names an evening list picks are yesterday's
runners, and their overnight return is a mix of a few large gap-ups and many small gap-downs, plus overnight
offerings priced at a discount after a run (the dilution cycle in `methods-who-buys.md` §3.3). Vainio (2026,
working paper) found the overnight/intraday pattern's gross alpha is large and its net alpha negative after
Corwin-Schultz spreads in exactly the small, illiquid names this universe holds. A fail is the more likely
result and will be the headline if it happens.

## What was already known when this was written

Nothing about overnight returns of any list. Known and possibly related: the D-1 16:00 model's precision@10 of
0.163 for reaching +20% by 09:45 (a high touched at any time, not the open); the watchlist's recall of +100%
runners (25/38/55/74% at top 10/20/50/100); "7.1% of +100% runners run +100% again within 5 sessions"; buying
gappers already up 20% at the open loses 5.2% by the close; the open cross equals the 09:30 bar open at the
median, and differs by more than 0.5% on 6-16% of days (`runs/explore/auctions-20261119-2240.md`).

## Data, population and look-ahead (frozen with this file)

- **Source.** Only the frozen research tables: `GapperStudyDailyBars` (raw daily bars, every Alpaca us_equity
  asset, active and inactive) and `GapperStudySplits`. No minute bars, news or filings: this is deliberately a
  whole-universe test with no candidate-day selection, so amendment 01's weights are not needed.
- **Signal day and trade.** Signal day S = D-1. Buy at the S daily close, sell at the D daily open, where D is
  the symbol's next daily bar. Prices are split-adjusted across S to D with `GapperStudySplits`. If the symbol
  has no bar within 5 sessions after S the trade is closed at -100%. If the next bar is later than D but within
  5 sessions, it exits at that bar's open (a halt is held through, not skipped).
- **Eligibility at the S close (the frozen universe numbers, as of S, from daily bars only).** S close in
  [$0.10, $20.00); 20-session average dollar volume through S of at least $300,000; at least 5 sessions traded
  of the last 20; no split with an ex-date from S-25 calendar days through D (the frozen exclusion window); the
  frozen unadjusted-split guard. S in 2021-11-01..2024-10-30 so D is in train. Nothing dated after S is read
  for eligibility or signals.
- **Stated look-ahead, in the rule's favour.** Signals use the full S daily bar (close, high, low, volume),
  which is complete at 16:00, and the fill is priced at that same close. A market-on-close order must be in by
  about 15:50. So this test gives the rule information it would not quite have. That only helps the rule: a
  fail is therefore decisive, and a pass is not believed until the minute-level replication below.

## Signals (frozen; each ranks eligible symbols at the S close; the list is the top N, N in 5, 10, 20)

- **S1 top gainers.** S volume at least 3x the 20-session average share volume; rank by S close over the prior
  close, descending. The plain attention list.
- **S2 closed strong.** S1's volume rule, S close at least +20% over the prior close, and the S close in the
  top quarter of the S range (close >= low + 0.75 x (high - low)); rank by S close over the prior close.
- **S3 learned.** A gradient-boosted classifier (LightGBM, fixed: 300 trees, learning rate 0.05, 31 leaves,
  min 200 rows per leaf, feature fraction 0.8, seed 1337; no tuning) of "the D open is at least +10% over the
  S close", ranked by predicted probability. Fit rolling-origin on the explore plan's four folds (fit
  2021-11..2022-10 / ..2023-04 / ..2023-10 / ..2024-04; evaluate the next six months), a 5-session embargo
  between fit and evaluation. Features, all from daily bars and splits through S: returns over 1, 2, 5 and 20
  sessions; S gap (open over prior close), S intraday (close over open), S range, close location in the range;
  volume over the 20-session average on S and S-1; log price; log 20-session dollar volume; 20-session sigma;
  largest one-session return in the last 10 and last 60 sessions; sessions since the last close-to-close gain
  of +50% or more (capped 250); count of +20% sessions in the last 20; classic Corwin-Schultz spread over the
  last 20 sessions; day of week; calendar days since the last reverse split (capped 365, none = 366) and its
  ratio; sessions since the first bar (capped 500); the count of eligible symbols up +20% or more on S.

Nine configurations in all. S1 and S2 are reported on all of train and on the four evaluation windows; the
comparison across signals and every pass/fail uses the evaluation windows only (2022-11..2024-10).

## Cost (frozen with this file)

Amendment 12's per-session multipliers scale the study's minute-bar Corwin-Schultz feature, which exists only
on candidate and sample days, so A12 cannot price a whole-universe test. This amendment therefore states its
own, deliberately harsh, cost: round trip = max(classic Corwin-Schultz spread over the 20 sessions through S,
1.0%) + 2 x square-root impact ($1,000 notional, 20-session average share volume, 20-session sigma, the frozen
impact constant). The floor is twice the frozen 0.5%. Both fills are single-price auctions (market-on-close in,
market-on-open out), which pay no quoted spread at all, so charging a full spread is conservative. Reported
beside it, never used to choose: the gross mean (the break-even cost), and net at 2x cost and at a flat 3%.
Whether Alpaca accepts `cls` and `opg` orders on these names is unverified and is a gate before any paper use.

## Hypotheses

- **H31 (primary, family F13).** The configuration selected on train (highest day-clustered lower bound among
  those with at least 150 trades on at least 40 days) nets at least +1.0%/trade after the cost above: equal
  weight per trade, day-clustered bootstrap 95% lower bound above 0 (10,000 draws, seed 1337), positive in at
  least 3 of the 4 evaluation windows, a leave-one-month-out lower bound above 0, SPA p < 0.05 across the nine,
  and DSR >= 0.95 at the ledger's trial count after the run. Each configuration is a ledger trial.
- **H32 (secondary, descriptive).** Where the list's overnight return comes from: the share of trades opening
  at least +10% and +20% up, at least 10% and 20% down, the mean of each bucket, the base rates over all
  eligible symbol-days, and the top-1% and top-5% concentration of P&L. No verdict.

## Decision rules

- **Train fail -> stop.** Validation is not read. No other signal, list size, exit or cost is tried under this
  amendment. A further idea needs its own amendment.
- **Train pass -> one validation read**, written into this file as a dated note first: S3 fit on all of train,
  the selected configuration carried unchanged, the same minimums.
- **Train and validation pass -> the minute-level replication before anything else**: signals computed from
  bars through 15:50 ET, the entry at the closing-cross print and the exit at the opening-cross print, with
  quoted spreads pulled for the traded symbol-days. That needs a data pull and its own amendment.
- **Corrections.** A13 is its own family (F13); it never enters the frozen A01-A12 corrections or Holm set.
- **Promotion.** Unchanged from the study: the single holdout run only with the owner's explicit go-ahead, then
  20 sessions of paper shadow, before any paper-only template could be offered, and only the owner enables it.
- **Kill criterion.** Unaffected either way.

## Implementation note

`tools/gapper-study/a13_overnight.py` (Python, reads the research database read-only, writes
`runs/explore/a13-overnight-<stamp>.md/.json` and appends the nine trials to `runs/explore/ledger.jsonl`). It
refuses any signal day outside train. A pass would have to be re-implemented in the shared C# library
(`IdiotProof.Evidence/Gappers/`) before validation counts toward promotion: the playbook's one-code-path rule.

## Train result (2026-11-22, IP-T632)

**H31 fails on train** (`runs/explore/a13-overnight-20261122-0249.md`). The rule picked S3 top20 (the learned evening
list): 10,040 trades on 502 days, gross +6.75%, A13 cost 3.97%, net +2.77% [-0.48%, +6.78%]. It misses four of the
eight criteria: the lower bound, leave-one-month-out (-1.08%), SPA p 0.182 and DSR 0.000. The plain lists lose
outright: S1 top gainers -2.2% to -5.2% net, S2 closed-strong about -2.5%. Per the stop rule, validation is not read.

**The positive gross mean is a data hole, not an overnight edge.** 23 list trades "return" +500% to +9,990%
overnight (DVLT, BINI, EMPD, FFAI x2, ...). Every one checked is a reverse split with no row in `GapperStudySplits`,
probably recorded under the company's earlier ticker. With the 148 moves past the frozen split-guard bounds dropped
(a sensitivity, not the verdict), S3 top20 is -3.27% net [-3.62%, -2.92%] on +0.71% gross, and all nine
configurations are negative. The model's AUC of 0.87 is partly learning which sub-$0.20 stocks are about to reverse
split. The table gap is filed as IP-T633: it biases any daily-bar return across a split day in earlier runs too.

What it says: buying the evening list at the close and selling at the open does not pay. The literature's overnight
effect is there in the whole universe (+0.03% to +0.09% per night once the split artifacts are removed), but it is a
fraction of a 1.7% round trip. The script reads the CSV exports of the frozen tables, not the database, and writes
its nine trials to `runs/explore/a13-ledger-pending.jsonl`, which is merged into the ledger with amendment 14's.
