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

## Results, part 2 (appended 2026-11-04; protocol above unchanged)

**Run status.** The seven steps the host stopped (topology, pools, conformal ×2, search, flow ×5,
hawkes ×5) were run from a clean export of `origin/main` at cf5e694, **one tool per process**,
sequentially (`dotnet run --project tools/EvidenceSweep -- main <step>`). Every step exited cleanly;
topology took 17 minutes with 2–4 GB free and needed **no** memory-driven parameter change, so
every sample and parameter is exactly as declared. IP-T435 (tick-aware HMM/BOCPD, a62076b) landed
before this pass, so both regime steps were re-run on the same sample and dates; the first-pass
outputs are kept as `regime-*-v1.*`. The export also carried IP-T433 (backtester honors
TakeProfitPercent), which the pre-declared search grid does not use.

| tool | tested | n | effect | corrected significance | verdict |
|---|---|---|---|---|---|
| topology | H0/H1 persistence features, most extreme training bucket re-tested on holdout | 900 train / 380 holdout sessions, 22,027 holdout reads | holdout up-rates 49.1%–50.8% vs base 49.0%–51.0% | day-clustered p 0.37–0.77 vs α 0.0125 per feature | **not significant** |
| pools | raid-and-reverse / break / reject rates per pool kind × side × tier | S20, e.g. 13,307 round-number approaches from above at $1–5 | round numbers fail to hold (raided or broken) 72% at $1–5, 60% at $5–20, 53% at $20+ | n/a (descriptive rates with Wilson CIs) | **description only**: tighter tiers are raided more; no timing claim |
| conformal — analog | split and adaptive (ACI) 90% intervals; abstain-unless-interval-excludes-zero rule | 20,608 forecasts; 222 calls on 57 days | coverage split 90.7%, ACI 90.0%; calls 40.1% hit (CI 33.9–46.7%), edge −0.136%/call before cost | day-clustered bootstrap p 0.998, t-test p 0.991 | intervals **calibrated as promised**; directional rule **not significant** |
| conformal — kalman | as above | 27,608 forecasts; 299 calls on 54 days | coverage split 90.2%, ACI 89.8%; calls 49.8% hit (CI 44.2–55.5%), +0.035%/call before cost | bootstrap p 0.502, t-test p 0.981 | intervals **calibrated**; rule **not significant** |
| search | 6 configs of `OnVwapReclaim().StopLossPercent({sl}).TrailingStopLoss({trail})` on S10 | 450 selection + 190 holdout symbol-days, 441 trades per config | every config negative net of modeled 0.108% cost; best sl=4 trail=2 −0.142%/cell; holdout −0.047%/cell, 75/190 winning | reality check p 1.000, SPA p 1.000, q 1.000, DSR 2.6e-7; holdout p 0.612 | **not significant** — no surviving config |
| flow (S5) | imbalance, VPIN, Kyle's λ, absorption buckets vs 5-min forward return (next-open entry) | 90 holdout buckets, 84 tests; 400k–1.44M trades per symbol | lifts 0.49–1.09, nothing beyond base | 0 of 90 with BH q < 0.05 (min q 0.56) | **not significant** |
| hawkes (S5) | self-excitation per session; momentum-ignition bars vs 5-min forward return | 110 session fits, 493 ignition bars, 60 buckets | branching ratio 0.28–0.70 (median 0.43): trading is strongly self-exciting | fits: LR vs Poisson ≫ 0 every session; buckets: 0 of 60 with q < 0.05 (min q 0.14) | self-excitation **real (descriptive)**; ignition as a signal **not significant** |
| regime bocpd — re-run after IP-T435 | change points, 5 sessions | ~1,900 bars × 20 | 312 flagged changes vs 318 before the fix (6–25 per symbol per 5 days) | n/a (descriptive) | **still over-flags**: the tick-noise floor barely moved the count |
| regime hmm — re-run after IP-T435 | 2-state Gaussian HMM | ~1,550 training bars × 20 | the degenerate "calm" states got a realistic floor (e.g. OPEN 0.003%→0.013%/bar, GRAB 0.002%→0.019%) but 7 of 20 still have a calm state with stay ≈ 0.4–0.5 | n/a (descriptive) | **still misbehaves** on those 7: the calm state still alternates with every tick |

**What part 2 adds.**
- Out of sample, after the corrections each tool declared, **no new timing signal survived**:
  topology, order flow, Hawkes ignition, the conformal call rule and the VWAP-reclaim grid all sit
  at or below a coin flip net of cost. That is consistent with the penny walk-forward (IP-T375).
- What did hold up is **descriptive, not predictive**: conformal intervals deliver their promised
  coverage (useful for sizing and for deciding when *not* to trade), sub-$5 trading is strongly
  self-exciting (branching ≈ 0.4), low-priced names co-crash (part 1), and cheap names' round-number
  levels get raided or broken ~72% of the time.
- IP-T435 helps the HMM's variance floor but does not fix either regime model on sub-$5 minute bars;
  a follow-up (IP-T437) is needed before regime output drives anything.

## Part 3 — regime models re-run after IP-T437 (2026-11-04)

Same sample (S20) and dates (2026-10-26 .. 2026-10-30), same thresholds; the IP-T435 outputs are kept
as `regime-*-v2.*`. IP-T437 changes two things before either model is fit:

1. **Bar size is chosen from the data.** Each symbol is fit on the smallest of 1, 2, 3, 5, 10, 15, 30
   minutes at which at most 25% of bars print no change and tick rounding is at most 10% of the return
   variance (aggregation stays inside each session; `--aggregate 1` forces raw minutes).
2. **The rounding floor uses the quoted cent.** The tick estimate from closes was pulled below a cent by
   sub-penny midpoint prints (OPEN at $3.77 had a "calm" state of 0.013%/bar, under the 0.108% a cent
   implies); the floor now never uses less than the Reg NMS quoting increment.

The HMM also refuses to fit fewer than 150 training bars after aggregation.

| tool | before (IP-T435) | after (IP-T437) | verdict |
|---|---|---|---|
| regime bocpd | 312 flagged changes (6–25 per symbol, 5 days) | 145 (≈1.5 per symbol-session; 7 names moved to 2–30 min bars) | **plausible**: about one intraday phase change per session |
| regime hmm | 7 of 20 names with a degenerate calm state (stay ≈ 0.4–0.5) | 0 of 16 fitted names degenerate (calm stay 0.85–0.99); OPEN, GRAB, NIO, PLUG report "too few bars at 15–30m" instead of a fit | **fixed**: the model now separates volatility or says it can't |

Regime output is descriptive only; nothing was promoted to the DSL.
