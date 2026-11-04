# Evidence sweep on real data — 2026-11 (IP-T432)

Every evidence tool added under IP-T367 has been run once on real market data. This file was
written in two passes: the **protocol** below was fixed before any tool was run, and **results**
were appended afterward without changing it. Nothing here is promoted to the DSL.

## Protocol (fixed before running)

**Where it ran.** In-process from a clean export of `origin/main`, against research LocalDB copies:
`IdiotProof_Research_T375` (daily + minute bar cache from the penny walk-forward) and
`IdiotProof_Research_T407` (read-only copy of the live research claims). The live database was
only read (to copy the 76 replay trades for survival). Read-only Alpaca market data, no orders.

**Sample rule (S20).** The point-in-time under-$5 universe on **2026-08-03**: last close on the
prior session in [$0.10, $5), at least 5 of the last 20 sessions, 20-session average dollar volume
≥ $500k, computed only from daily bars dated before 2026-08-03, over the 32,442 active and
delisted Alpaca equities cached for IP-T375. S20 = the 20 members with the highest average dollar
volume; ties broken alphabetically. **S10** = the top 10 of S20, **S5** = the top 5. Benchmarks:
SPY, IWM. No sampling randomness.

**Date range.** 2026-08-03 .. 2026-10-30 unless a row below says otherwise.

| tool | symbols | dates | parameters (everything else default) |
|---|---|---|---|
| flow | each of S5 | 2026-10-01 .. 2026-10-30 | horizon 5, bins 5, signing tick, train 0.67 |
| hawkes | each of S5 | 2026-10-01 .. 2026-10-30 | horizon 5, bins 5, train 0.67, window 10, spike 3, branching 0.7 |
| topology | S20 | range | window 40, dimension 3, delay 1, horizon 5, buckets 4, holdout 0.3 |
| conformal | S20 | range | model analog, alpha 0.1, gamma 0.005, horizon 15, window 30, entry next-open |
| conformal | S20 | range | model kalman, otherwise as above |
| tails | S20 + SPY + IWM, daily | 2025-11-03 .. 2026-10-30 | q 0.1, train 0.67, permutations 999, alpha 0.05 |
| survival | all replay trades (copied from live) | all | defaults |
| pools | S20 | range | horizon 10, proximity 0.25, min-samples 20 |
| regime bocpd | S20, minute | 2026-10-26 .. 2026-10-30 | threshold 0.5 |
| regime hmm | S20, minute | 2026-10-26 .. 2026-10-30 | states 2 |
| events | research claims | 2024-02-01 .. 2026-07-26 | windows -1:1,0:1,0:5 (identical to IP-T407, now split-adjusted) |
| search | S10 | range | `Ticker(X).OnVwapReclaim().StopLossPercent({sl}).TrailingStopLoss({trail})`, grid sl=2,4 trail=1,2,3 (6 configs), holdout 0.3, seed 1337 |

Flow and Hawkes read every trade, so they get the five most liquid names and one month.
The search grid avoids `TakeProfit`/`TakeProfitPercent`: the first is a fixed price (meaningless
across symbols, IP-T434) and the backtester ignores the second (IP-T433).

**Verdict rule.** A result counts as **significant after correction** only if the tool's own
corrected statistic says so (BH q < 0.05, day-clustered p < 0.05, Bonferroni-split α for
topology, reality-check p < 0.05 for search). Everything else is reported as **not significant**,
however large the raw effect.

## Results (appended after the runs; protocol above unchanged)

**Run status.** The `events` run and the first four steps of the main run (survival, tails,
regime bocpd, regime hmm) completed. The main run was then stopped by the host for low system
memory while `topology` was executing, so **topology, pools, conformal (both models), search, flow
and hawkes did not run**. They are not reported, not inferred, and remain open on IP-T432.
Raw outputs: `docs/research/evidence-sweep-2026-11/` (`*.txt` human, `*.json` data, `sample.json`).

**Sample.** The 2026-08-03 point-in-time universe had 812 members. S20 = NVD OPEN TE MSTU TSLG GRAB
ACHR SNAP KEEL AMC EOSE BBD NIO PLUG DAMD CONL BTE BBAI ABEV IOVA (by 20-session dollar volume).
Several are leveraged single-stock ETFs (NVD, MSTU, TSLG, CONL, DAMD) that the rule admits because
they trade under $5; the rule was fixed in advance, so they stay in.

**Data tier (IP-T423).** In the research cache, 4,565,456 minute and 2,049,387 daily bars were
fetched before tiers were recorded (`Alpaca`, tier unknown); 33,147 minute and 1,751 daily bars
fetched during this sweep are `Alpaca:sip`. No IEX-stamped bars. The results therefore rest mostly
on bars whose SIP provenance is unproven, though no fallback to IEX was observed in this run.

| tool | tested | n | effect | corrected significance | verdict |
|---|---|---|---|---|---|
| events (split-adjusted) | 84 group-window CAR tests | 10,205 events (was 9,818); 54 splits back-adjusted on 40 symbols | Bearish Earnings [0,1] −3.92%, Bearish News [0,1] −2.14%, Bullish Earnings [0,5] +2.23%, Bearish Regulatory [-1,1] −4.18% | 24 of 84 have BH q < 0.05 (BMP p); top rows q < 0.001 | **significant after correction** for the sentiment-direction rows; Bullish Contract [0,5] (+1.53%, q > 0.05) and Bullish Guidance [0,1] (p 0.048) **no longer survive** correction |
| tails — training screen | lower-tail dependence λL(0.1), 231 pairs of S20+SPY+IWM | 166 training days per pair | 76 pairs co-crash beyond chance; 15 of them with \|corr\| < 0.3 (e.g. OPEN/EOSE λL 0.50 at corr 0.23) | BH q < 0.05 (permutation floor q ≈ 0.007) | **significant after correction** as description only |
| tails — held-out contagion | does a bottom-decile day in A predict B down next session (next-open entry)? | 152 directed links, 4–11 events each | none above base rate | 0 of 152 with BH q < 0.05 | **not significant** — no contagion guard is justified |
| survival | target vs stop hazard on replay exits | 76 trades over 28 strategies | 8 target and 28 stop exits; no strategy has 20+ trades | Cox skipped (needs 40 events) | **not significant** — too few trades |
| regime bocpd | online change points, 5 sessions | ~1,900 bars × 20 | 6–25 flagged changes per symbol in 5 days | n/a (descriptive) | **misbehaves**: over-flags on 1-cent price grids (IP-T435) |
| regime hmm | 2-state Gaussian HMM | ~1,550 training bars × 20 | on 7 names the "calm" state is sd ≈ 0.002%/bar, stay ≈ 0.5 | n/a (descriptive) | **misbehaves**: the calm state captures zero-return ticks, not volatility (IP-T435) |
| topology, pools, conformal ×2, search, flow ×5, hawkes ×5 | — | — | — | — | **not run** (host memory stop) |

**What this says.**
- The one finding that is both large and corrected-significant is old news made more reliable:
  the Research scanner's **sentiment direction** on Earnings, News and Regulatory claims moves prices
  over days 0–1, and bearish claims carry more signal than bullish ones. ArticleDate has no time of
  day, so day 0 may include price action before the claim existed; a tradable entry is later.
- Low-priced names **do crash together** more than their correlation suggests, but over 82 held-out
  sessions that co-crash **did not predict the next session**. Tail dependence is a risk-concentration
  fact (don't hold many of these at once), not a timing signal.
- Regime tools built on Gaussian returns need a tick-aware model before they are used on penny stocks.

**Filed while running.** IP-T433 (backtester ignores TakeProfitPercent), IP-T434 (evidence search
example uses absolute-price verbs), IP-T435 (HMM/BOCPD degenerate on tick-quantized returns).
