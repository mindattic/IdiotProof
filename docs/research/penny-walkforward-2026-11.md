# Penny-stock walk-forward: does the LSH analog model call direction at 51%+? (IP-T375)

**Verdict: no. It did not beat 51%, and it did not beat a coin.** Over the single pre-registered
evaluation (2026-08-03 .. 2026-10-30) the frozen model hit **46.5%** (904 / 1,944 calls, 95% Wilson
CI 44.3%–48.7%). Day-clustered tests reject "better than 50%" in the wrong direction (bootstrap p =
0.9994, day t-test p = 0.9994; vs 51%: p ≈ 1). The mean edge after a 0.3% round-trip cost is
**−0.285% per call**. Nothing here supports trading this model.

Raw outputs: [`penny-walkforward-2026-11/`](penny-walkforward-2026-11/) (`tune.json`, `frozen.json`, `eval.json`).
Harness: `tools/EvidenceRun/` (in-process, research LocalDB `IdiotProof_Research_T375`, read-only market data, no orders).

## Protocol (fixed before any evaluation data was scored)

| | |
|---|---|
| Model | LSH pattern analogs (`PatternAnalogPredictor` logic): z-normalized close shape of the last *window* 1-min RTH bars, 16×8-bit SimHash bands, k = 25, min similarity 0.8, ≥ 8 neighbors, call when the smoothed neighbor up-share ≥ *agreement* |
| Training | per (symbol, day D): only that symbol's 20 prior RTH sessions; labels resolve inside their own session; nothing from D or later |
| Calls | on D every *horizon* bars, so scored outcomes never overlap |
| Universe | each weekday's **point-in-time** under-$5 set (prior-day close $0.10–$5, 20-session avg dollar volume ≥ $500k, only bars dated before D), from **32,442 Alpaca equities incl. 18,556 inactive/delisted** (480 placeholder tickers the data API rejects count as no data); the 15 most liquid members per day (deterministic, chosen before running) |
| Tuning | 2026-05-01 .. 2026-07-31 only |
| Grid | window {10, 20, 30} × horizon {5, 15} × agreement {0.6, 0.7} = **12 configurations** |
| Selection | highest day-clustered bootstrap 2.5% bound among configurations with ≥ 200 calls |
| Evaluation | 2026-08-03 .. 2026-10-30, **once**, frozen parameters; the harness refuses a second run |
| Significance | day-clustered: null-centered block bootstrap over trading days (10,000 draws, seed 1337) **and** a one-sided t-test on daily hit rates; < 5 called days proves nothing. Naive binomial p reported but never used for the verdict |
| Cost | 0.3% round trip (sub-$5 spread + slippage estimate) |

### Amendments (both made after tuning results were known under the old scoring, before any evaluation)

1. **Next-open entry** (IP-T405 found close-entry labels on $1–5 names are inflated by bid-ask bounce).
   Every training label and every call now enters at bar *i+1*'s **open** and exits at bar *i+h*'s
   **close**. Close-entry scoring of the same calls is reported below as a diagnostic only.
2. **Split exclusion** (IP-T407: the bar store is split-unadjusted, IP-T410 open). A symbol-day is
   excluded when a split falls anywhere in its 20-session training + test span: an Alpaca
   corporate-actions forward/reverse split ex-date (662 ex-dates over 555 symbols, Mar–Oct), or a
   daily close-to-close or overnight move above +200% / below −66.7%.

IP-T414 (an `--entry` option for `evidence walkforward`) had not landed, so both amendments are
implemented in the harness (`tools/EvidenceRun/NextOpenWalkForward.cs`), a line-for-line copy of the
analog path of `EvidenceWalkForward` at e8930f5 with those two changes. Built from a `git archive`
export of origin/main 4c1abc6 so concurrent uncommitted edits could not leak in.

3. **Ties diagnostic** (added after tuning, before evaluation; primary scoring unchanged). A call
   whose outcome is exactly 0 counts as a miss under the pre-registered rule; at 1-cent ticks that
   happens often, which biases strict hit rates below 50%. The ties-excluded rate is reported as a
   secondary diagnostic only.

### Attempts (all counted)

| attempt | scoring | configs scored | outcome |
|---|---|---|---|
| 1 | close entry | 0 | crashed during daily preload (a 400 "invalid symbol" aborted the batch) |
| 2 | close entry | 1 (never written or seen) | crashed serializing NaN |
| 3 | close entry | 10 | SQL timeout under LocalDB contention; built from a working tree with other agents' uncommitted edits, so discarded |
| 4 | close entry | 0 | clean-snapshot rerun, stopped for the amendments |
| 5 | **next-open + split exclusion** | **12** | the tuning used for selection |

Configurations evaluated on tuning data across all attempts: 23 (11 under superseded close-entry
scoring, 12 under the amended protocol). The evaluation period was scored exactly once.

## Tuning (attempt 5, 2026-05-01 .. 07-31)

66 universe days, avg 865 point-in-time members/day, 50 distinct symbols traded, 909 symbol-days
(36 excluded for splits: 7 by corporate action, 29 by gap guard).

| window | horizon | agree | calls | hit | clustered 2.5% bound | clustered boot p |
|---|---|---|---|---|---|---|
| 10 | 5 | 0.6 | 23,084 | 44.7% | 44.1% | 1.00 |
| 10 | 5 | 0.7 | 3,763 | 44.1% | 42.3% | 1.00 |
| 10 | 15 | 0.6 | 7,287 | 46.5% | 45.4% | 1.00 |
| 10 | 15 | 0.7 | 1,234 | 46.4% | 43.4% | 0.99 |
| 20 | 5 | 0.6 | 18,665 | 45.6% | 44.7% | 1.00 |
| 20 | 5 | 0.7 | 3,734 | 46.4% | 44.8% | 1.00 |
| 20 | 15 | 0.6 | 5,912 | 47.4% | 46.1% | 1.00 |
| 20 | 15 | 0.7 | 1,302 | 47.5% | 44.7% | 0.96 |
| 30 | 5 | 0.6 | 19,246 | 45.2% | 44.5% | 1.00 |
| 30 | 5 | 0.7 | 4,814 | 44.4% | 43.1% | 1.00 |
| 30 | 15 | 0.6 | 6,409 | 47.0% | 45.6% | 1.00 |
| **30** | **15** | **0.7** | **1,960** | **48.6%** | **46.3%** | **0.89** |

**Frozen: window 30, horizon 15, agreement 0.7.** No configuration was above 50% even in-sample.

## Evaluation (single run, 2026-08-03 .. 10-30)

65 universe days, avg 876 members/day, 50 distinct symbols, 945 symbol-days scored, 15 skipped
(no bars / too little history), 15 excluded for splits (4 corporate action, 11 gap guard).

| measure | value |
|---|---|
| calls / evaluation points | 1,944 / 23,110 (coverage 8.4%) |
| hit rate (strict) | **46.5%** (904), Wilson 95% 44.3%–48.7% |
| naive binomial p vs 50% | 0.999 |
| day-clustered vs 50% | 64 days, day-mean 46.3%; bootstrap p = 0.9994 (95% 44.4%–48.6%); t-test p = 0.9994 |
| day-clustered vs 51% | bootstrap p = 1.000; t-test p = 1.000 |
| always-up base rate (decided outcomes) | 48.4% up; best constant guess 51.6% |
| mean edge per call | +0.015% before cost, **−0.285% after 0.3%** |

Per month: Aug 47.8% (310/649), Sep 44.4% (284/640), Oct 47.3% (310/655). It was consistently below 50%.

**Diagnostics (never used for selection or the verdict):**
- *Ties excluded:* 185 calls had an exactly-flat next-open outcome. Without them: 51.4% (904/1,759),
  day-clustered bootstrap p = 0.117, t p = 0.127 vs 50%; vs 51% p ≈ 0.37. **Not significant.**
- *Close entry, same calls:* 47.8% (929/1,944), clustered p = 0.98. Here close entry was not inflated
  above 50% (IP-T405's bounce showed up in mean-reversion features, not these analog calls).

## Caveats

- 50 symbols × 65 days is a modest sample; the 15-per-day cap concentrates on the most liquid pennies.
- Bars are raw (split-unadjusted). Split spans are excluded, but dividends and other adjustments are not handled.
- `AlpacaDataFeed` silently downgrades to IEX on a 403 and the store records only "Alpaca" as the
  feed, so SIP-only data cannot be proven from the cache. The account is SIP-entitled.
- Survivorship: inactive assets were candidates, but a ticker reused by a newer company maps to the newer entity's history.
- Significance assumes days are independent of each other. It clusters within a day only, not across market-wide regimes.

## What this means

On this universe and protocol, minute-level shape analogs carry **no exploitable directional
information**. The apparent ~51% after removing ties is within noise, and it vanishes against costs. Do
not arm anything from this model. The negative result is the finding: the toolkit's job was to refuse
an edge that isn't there, and it did.
