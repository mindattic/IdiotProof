# Gapper study — results (IP-T442)

Generated 2026-11-09 4:29 AM ET by `research gappers report` at `22a90dfb`. The protocol, hypotheses and parameters were frozen at `13f14521` before any data was pulled and are not restated here; this file is appended to, the protocol is never edited. Paper-only research: no orders, read-only market data.

## Protocol (fixed before running)

[`gapper-study-2026-11/protocol.md`](gapper-study-2026-11/protocol.md), [`hypotheses.json`](gapper-study-2026-11/hypotheses.json) (sha256 `645648b68f77`, frozen 2026-11-06 20:15 UTC) and [`params.json`](gapper-study-2026-11/params.json) (sha256 `872f3294a26b`, frozen 2026-11-06 20:15 UTC). Families: F1 prediction buckets (HoldoutBucketEvaluator, BH q < 0.05), F2 catalyst groups (same-day BMP with a 1000-draw within-day placebo, BH), F3 timing (descriptive intervals at 95%; never "significant"), F4 strategy grids (SPA p < 0.05 and DSR >= 0.95), F5 structural (day-clustered bootstrap, BH). Primaries with a p-value (H1, H2, H3, H6, H7, H8, H9, H10, H11, H12) are additionally Holm-corrected at α = 0.05. Seed 1337, 10000 bootstrap draws, minimum bucket 50.

| split | days | use |
|---|---|---|
| train | 2021-11-01 .. 2024-10-31 | profile fit, exploratory tests, grid selection |
| validation | 2024-11-03 .. 2025-10-31 | replication, H11, H12 SPA |
| holdout | 2025-11-03 .. 2026-10-30 | opened once, surviving set only |

**Verdict rule.** `supported` only when the effect meets the frozen minimum **and** the family correction (and Holm, for primaries) passes; `refuted` when the interval sits entirely where the claim cannot hold; `undecided` otherwise; `void` when the sample is below the minimum bucket, the population is missing, or the hypothesis was dropped or edited post-unlock. F3 verdicts come from intervals alone.

## Sample

Universe days / candidates / gapper tiers / controls below are live from `GapperStudyUniverseDays` and `GapperStudyEvents` (not the cached `sample.json`, which can lag a running pull); excluded counts, the premarket/open split and symbol counts fall back to `gapper-study-2026-11/sample.json` (written 2026-11-09T03:09:52.2165389Z, frozen commit `13f14521`) where the live status does not carry them.

| split | universe days | candidates | tier20 | tier50 | tier100 | premarket / open | controls | excluded | symbols | dates |
|---|---|---|---|---|---|---|---|---|---|---|
| holdout | withheld | withheld | withheld | withheld | withheld | withheld | withheld | withheld | withheld | locked |
| none | 2266 | 145 | 16 | 6 | 0 | 5 / 11 | 16 | 39 | 2305 | 2024-11-01 .. 2024-11-01 |
| train | 1669297 | 105964 | 6855 | 1922 | 557 | 3365 / 3490 | 6855 | 14682 | 4796 | 2021-11-01 .. 2024-10-31 |
| validation | 640612 | 51797 | 5679 | 1685 | 560 | 3237 / 2442 | 5679 | 10119 | 4442 | 2024-11-04 .. 2025-10-31 |

CIK coverage of candidate symbols: 5038/6018 (83.72%); H8 dropped: false.
SIP share of train+validation study minute bars (live): 100.00 %.

## Data tiers

| tier | content | rows |
|---|---|---|
| T1 | daily bars, every Alpaca us_equity asset | 12,040,511 rows, 13,663 symbols |
| T2 | 1-minute 04:00–20:00 ET bars (file store `D:\IdiotProofData\gapper-study\bars`) | none 100.0% of symbol-days; train 100.0% of symbol-days; validation 100.0% of symbol-days |
| T3 | Alpaca news (backfilled catalyst rows) | 226,742 |
| T4 | EDGAR form index | 723,836 rows, 383,741 with acceptance time |
| T5 | ticker ↔ CIK | 5,437/32,439 mapped |
| T6 | splits | 2,635 |
| panel | snapshots / labels | none 0 / 0; train 1,932,118 / 1,932,118; validation 921,677 / 921,677 |

Time-of-day profile: `data/tod-profile.json` present (fit on train control days). Premarket spread multiplier: 9.393 (spread-multiplier.json). Holdout: locked.

## Results by family

Every hypothesis on every split it was run on, including failures. Corrections are recomputed over the latest run of each hypothesis per split. Secondaries (H13–H17) are leads, never confirmations. A hypothesis marked **post-hoc** was edited after the holdout unlock and carries no verdict.

### F1 prediction buckets

| hypothesis | split | n (days) | effect vs minimum | corrected statistic | verdict |
|---|---|---|---|---|---|
| H1 | train | 4,907 (749) | precisionOverBaseRate 3.135 [3.007, 3.265] vs >= 3.000 | p 2.0e-4, BH q 2.0e-4, Holm p 0.002 | **supported** |
| H1 | validation | 4,387 (249) | precisionOverBaseRate 2.435 [2.333, 2.539] vs >= 3.000 | p 2.0e-4, BH q 2.0e-4, Holm p 0.002 | **refuted** |
| H6 | train | 45,844 (754) | lift 6.727 [6.362, 7.112] vs >= 2.000 | p 2.0e-4, BH q 2.0e-4, Holm p 0.002 | **supported** |
| H6 | validation | 19,290 (248) | lift 6.447 [6.077, 6.838] vs >= 2.000 | p 2.0e-4, BH q 2.0e-4, Holm p 0.002 | **supported** |
| H9 | train | 798 (463) | lift 3.579 [3.200, 3.983] vs >= 2.000 | p 2.0e-4, BH q 2.0e-4, Holm p 0.002 | **supported** |
| H9 | validation | 537 (218) | lift 2.252 [1.926, 2.613] vs >= 2.000 | p 2.0e-4, BH q 2.0e-4, Holm p 0.002 | **supported** |
| H11 | train | 0 (0) | void: hypotheses.json allows H11 on validation, holdout only | p n/a, BH q n/a | **void** |
| H11 | validation | 50,904 (249) | aucDifference 0.322 [0.313, 0.330] vs >= 0.050 | p 2.0e-4, BH q 2.0e-4, Holm p 0.002 | **supported** |
| H15 (secondary) | train | 14,491 (754) | lift 17.047 [16.063, 18.087] vs >= 1.500 | p 2.0e-4, BH q 2.0e-4 | **supported** |
| H15 (secondary) | validation | 4,706 (247) | lift 15.406 [14.332, 16.547] vs >= 1.500 | p 2.0e-4, BH q 2.0e-4 | **supported** |

### F2 catalyst groups

| hypothesis | split | n (days) | effect vs minimum | corrected statistic | verdict |
|---|---|---|---|---|---|
| H2 | train | 3,206 (728) | meanDifferencePct 0.455 [-1.121, 2.208] vs >= 2.000 | p 0.599, BH q 0.599, Holm p 1.000 | **undecided** |
| H2 | validation | 3,051 (249) | meanDifferencePct -2.252 [-4.204, -0.359] vs >= 2.000 | p 0.020, BH q 0.020, Holm p 0.052 | **refuted** |
| H3 | train | 6,657 (753) | proportionDifferencePp -4.661 [-6.108, -3.214] vs >= 10.000 | p 2.0e-4, BH q 4.0e-4, Holm p 0.002 | **refuted** |
| H3 | validation | 5,592 (249) | proportionDifferencePp -11.443 [-13.506, -9.434] vs >= 10.000 | p 2.0e-4, BH q 4.0e-4, Holm p 0.002 | **refuted** |
| H16 (secondary) | train | 4,013 (741) | void: 0 treated / 4013 control events, fewer than minBucketN 50 | p n/a, BH q n/a | **void** |
| H16 (secondary) | validation | 2,914 (249) | void: 2 treated / 2912 control events, fewer than minBucketN 50 | p n/a, BH q n/a | **void** |

### F3 timing (descriptive)

| hypothesis | split | n (days) | effect vs minimum | corrected statistic | verdict |
|---|---|---|---|---|---|
| H4 | train | 1,922 (0) | proportion 0.449 [0.427, 0.471] vs >= 0.600 | 95% interval only (descriptive) | **refuted** |
| H4 | validation | 1,685 (0) | proportion 0.401 [0.377, 0.424] vs >= 0.600 | 95% interval only (descriptive) | **refuted** |
| H5 · medianDeathToHodMin | train | 6,831 (753) | medianDeathToHodMin 42.000 [38.000, 44.000] vs >= -2.000 | 95% interval only (descriptive) | **refuted** |
| H5 · bucketShareBelow | train | 6,831 (0) | share of HOD in 09:30-09:31 0.020 [0.017, 0.024] vs <= 0.250 | 95% interval only (descriptive) | **supported** |
| H5 · medianDeathToHodMin | validation | 5,669 (249) | medianDeathToHodMin 35.000 [31.000, 38.000] vs >= -2.000 | 95% interval only (descriptive) | **refuted** |
| H5 · bucketShareBelow | validation | 5,669 (0) | share of HOD in 09:30-09:31 0.015 [0.012, 0.019] vs <= 0.250 | 95% interval only (descriptive) | **supported** |
| H13 (secondary) | train | 4,382 (0) | proportion 0.465 [0.451, 0.480] vs >= 0.850 | 95% interval only (descriptive) | **refuted** |
| H13 (secondary) | validation | 3,674 (0) | proportion 0.487 [0.471, 0.503] vs >= 0.850 | 95% interval only (descriptive) | **refuted** |
| H17 (secondary) | train | 6,855 (753) | ratioOverUnconditional 0.546 [0.520, 0.572] vs >= 1.500 | 95% interval only (descriptive) | **refuted** |
| H17 (secondary) | validation | 5,679 (249) | ratioOverUnconditional 0.559 [0.532, 0.588] vs >= 1.500 | 95% interval only (descriptive) | **refuted** |

### F4 strategy grids

| hypothesis | split | n (days) | effect vs minimum | corrected statistic | verdict |
|---|---|---|---|---|---|
| H12 | train | 1,611 (626) | meanNetPctPerTrade -3.610 [-3.897, -3.327] vs >= 1.000 | SPA p 1.000, DSR 1.9e-50, trades 1611 | **refuted** |
| H12 | validation | 1,254 (248) | meanNetPctPerTrade -3.453 [-3.750, -3.149] vs >= 1.000 | SPA p 1.000, DSR 8.5e-36, trades 1254 | **refuted** |
| H14 (secondary) | train | 1,893 (656) | meanPairedDifferencePct 0.196 [-0.144, 0.551] vs >= 0.500 | p 0.253, BH q 0.506 | **undecided** |
| H14 (secondary) | validation | 1,676 (249) | meanPairedDifferencePct 0.351 [0.023, 0.699] vs >= 0.500 | p 0.037, BH q 0.075 | **undecided** |

### F5 structural

| hypothesis | split | n (days) | effect vs minimum | corrected statistic | verdict |
|---|---|---|---|---|---|
| H7 · fwdCloseDifferencePct | train | 6,855 (753) | void: 35 rows in the tested group / 6820 in the control group, fewer than minBucketN 50 | p n/a, BH q n/a | **void** |
| H7 · fadeDifferencePct | train | 6,657 (753) | void: 34 rows in the tested group / 6623 in the control group, fewer than minBucketN 50 | p n/a, BH q n/a | **void** |
| H7 · fwdCloseDifferencePct | validation | 5,679 (249) | void: 33 rows in the tested group / 5646 in the control group, fewer than minBucketN 50 | p n/a, BH q n/a | **void** |
| H7 · fadeDifferencePct | validation | 5,592 (249) | void: 33 rows in the tested group / 5559 in the control group, fewer than minBucketN 50 | p n/a, BH q n/a | **void** |
| H8 | train | 1,549,033 (755) | rate ratio treated/control 0.731 [0.657, 0.808] vs >= 1.500 | p 2.0e-4, BH q 3.0e-4, Holm p 0.002 | **refuted** |
| H8 | validation | 590,326 (250) | rate ratio treated/control 0.679 [0.593, 0.765] vs >= 1.500 | p 2.0e-4, BH q 3.0e-4, Holm p 0.002 | **refuted** |
| H10 · lift | train | 105,964 (755) | rate ratio treated/control 1.448 [1.318, 1.586] vs >= 1.500 | p 2.0e-4, BH q 3.0e-4, Holm p 0.005 | **undecided** |
| H10 · fwdCloseMeanBelowPct | train | 460 (331) | mean fwdClose_16:00 (pct) on gapper days with the filing 3.456 [1.257, 5.621] vs <= 0.000 | p 0.002, BH q 0.002, Holm p 0.005 | **refuted** |
| H10 · lift | validation | 51,797 (249) | rate ratio treated/control 1.726 [1.569, 1.890] vs >= 1.500 | p 2.0e-4, BH q 3.0e-4, Holm p 0.052 | **undecided** |
| H10 · fwdCloseMeanBelowPct | validation | 498 (201) | mean fwdClose_16:00 (pct) on gapper days with the filing 2.405 [0.425, 4.415] vs <= 0.000 | p 0.017, BH q 0.017, Holm p 0.052 | **refuted** |

## Amendment A01: population-weighted detection and strategy (IP-T448)

Registered 2026-11-07 05:40 UTC before any test on study data ([`amendment-01.md`](gapper-study-2026-11/amendment-01.md), sha256 `557b34e2502b`). The frozen H1, H9, H11 and H12 are asked among candidate days, which are selected on the day's own outcome, so they bound from above what the same rule achieves on the live universe. Each amended hypothesis asks the same question with the same thresholds over every candidate day (weight 1) plus a seeded 10% sample of non-candidate universe days (weight 10). Amended runs are corrected as their own set, so no frozen q or Holm p moves. Promotion requires the frozen conditions **and** H11p and H12p.

| hypothesis | split | frozen (candidate days) | population estimate | corrected statistic | verdict |
|---|---|---|---|---|---|
| H1p (amends H1) | train | 3.135, precision 31.5% (supported) | precisionOverBaseRate 4.103 [3.951, 4.256] vs >= 3.000, precision 4.2%; n 15,451 (755 days), 26692 sample rows | p 2.0e-4, BH q 2.0e-4, Holm p 6.0e-4 | **supported** |
| H1p (amends H1) | validation | 2.435, precision 32.9% (refuted) | precisionOverBaseRate 2.870 [2.769, 2.979] vs >= 3.000, precision 4.6%; n 12,478 (249 days), 13928 sample rows | p 2.0e-4, BH q 2.0e-4, Holm p 8.0e-4 | **refuted** |
| H9p (amends H9) | train | 3.579, precision 28.6% (supported) | lift 20.311 [16.748, 24.472] vs >= 2.000, precision 13.4%; n 911 (497 days), 149892 sample rows | p 2.0e-4, BH q 2.0e-4, Holm p 6.0e-4 | **supported** |
| H9p (amends H9) | validation | 2.252, precision 23.5% (supported) | lift 11.619 [8.773, 15.083] vs >= 2.000, precision 12.8%; n 605 (226 days), 56698 sample rows | p 2.0e-4, BH q 2.0e-4, Holm p 8.0e-4 | **supported** |
| H11p (amends H11) | train | n/a (void) | void: amendment A01 allows H11p on validation, holdout only; n 0 (0 days),  sample rows | p n/a, BH q n/a | **void** |
| H11p (amends H11) | validation | 0.322 (supported) | aucDifference 0.354 [0.343, 0.365] vs >= 0.050; n 107,669 (249 days), 56765 sample rows | p 2.0e-4, BH q 2.0e-4, Holm p 8.0e-4 | **supported** |
| H12p (amends H12) | train | -3.610 (refuted) | meanNetPctPerTrade -4.385 [-4.727, -4.029] vs >= 1.000; n 1,878 (661 days), 155845 sample rows | SPA p 1.000, DSR 2.2e-65, trades 1878 | **refuted** |
| H12p (amends H12) | validation | -3.453 (refuted) | meanNetPctPerTrade -3.928 [-4.295, -3.581] vs >= 1.000; n 1,438 (249 days), 58752 sample rows | SPA p 1.000, DSR 1.8e-54, trades 1438 | **refuted** |

## Amendment A02: new questions (IP-T450)

Registered 2026-11-07 07:15 UTC before any test on study data ([`amendment-02.md`](gapper-study-2026-11/amendment-02.md), sha256 `c481f9272b9b`), corrected as its own set, on amendment 01's weighted population.

| hypothesis | split | n (days) | effect vs minimum | corrected statistic | verdict |
|---|---|---|---|---|---|
| H18 | train | 362 (268) | meanNetPctPerTrade -3.294 [-4.161, -2.434] vs >= 1.000 | SPA p 1.000, DSR 9.8e-14, trades 362 | **refuted** |
| H18 | validation | 212 (131) | meanNetPctPerTrade -2.355 [-3.354, -1.203] vs >= 1.000 | SPA p 1.000, DSR 1.1e-4, trades 212 | **refuted** |
| H19 | train | 1,440 (589) | median share of the hour's move before publication 0.311 [0.267, 0.353] vs <= 0.250 | 95% interval only (descriptive) | **refuted** |
| H19 | validation | 1,335 (244) | median share of the hour's move before publication 0.330 [0.288, 0.357] vs <= 0.250 | 95% interval only (descriptive) | **refuted** |
| H20 (secondary) | train | 883 (432) | mean gross return at 1m minus at 15m (pct) -0.275 [-0.654, 0.010] vs >= 1.000 | 95% interval only (descriptive) | **refuted** |
| H20 (secondary) | validation | 425 (193) | mean gross return at 1m minus at 15m (pct) -0.130 [-0.797, 0.620] vs >= 1.000 | 95% interval only (descriptive) | **refuted** |

- H18 on train: selected `latency5m news=bullish confirm=up5 sellBy09:15` (highest weighted day-clustered lower bound on this split among configs with enough unweighted trades); mean net -3.294%/trade [-4.161, -2.434], 362 trades on 268 days; 27949 news days (1017 bullish); mean round-trip cost 4.63 %; exits {
          "velocity-death": 177,
          "vwap-loss": 128,
          "sell-by": 36,
          "stop": 21
        }.
- H18 on validation: selected `latency5m news=bullish confirm=up5 sellBy09:15` (carried from the latest train run of H18); mean net -2.355%/trade [-3.354, -1.203], 212 trades on 131 days; 12769 news days (472 bullish); mean round-trip cost 4.32 %; exits {
          "sell-by": 20,
          "vwap-loss": 72,
          "velocity-death": 92,
          "stop": 28
        }.
- H19 on train: median share of the hour's move before the headline 0.311 [0.267, 0.353]; median 2.096% in the 30 minutes before vs 4.214% high in the 30 after; 36.67 % of events had already run 5% or more before publication.
- H19 on validation: median share of the hour's move before the headline 0.330 [0.288, 0.357]; median 3.111% in the 30 minutes before vs 5.556% high in the 30 after; 43.00 % of events had already run 5% or more before publication.
- H20 on train: mean gross return to 09:28 by entry latency: [
          {
            "latencyMinutes": 1,
            "meanGrossPct": -0.8743979031556754
          },
          {
            "latencyMinutes": 2,
            "meanGrossPct": -0.8876697624794799
          },
          {
            "latencyMinutes": 5,
            "meanGrossPct": -0.8480260874997219
          },
          {
            "latencyMinutes": 10,
            "meanGrossPct": -0.7205612096978595
          },
          {
            "latencyMinutes": 15,
            "meanGrossPct": -0.5991186500267012
          }
        ].
- H20 on validation: mean gross return to 09:28 by entry latency: [
          {
            "latencyMinutes": 1,
            "meanGrossPct": -0.7573388443441877
          },
          {
            "latencyMinutes": 2,
            "meanGrossPct": -0.7207117617412446
          },
          {
            "latencyMinutes": 5,
            "meanGrossPct": -0.6384432331449994
          },
          {
            "latencyMinutes": 10,
            "meanGrossPct": -0.5375317730552899
          },
          {
            "latencyMinutes": 15,
            "meanGrossPct": -0.6272669330186765
          }
        ].

## Amendment A03: new questions (IP-T451)

Registered 2026-11-07 15:58 UTC before any test on study data ([`amendment-03.md`](gapper-study-2026-11/amendment-03.md), sha256 `f09f5e28324e`), corrected as its own set, on amendment 01's weighted population.

| hypothesis | split | n (days) | effect vs minimum | corrected statistic | verdict |
|---|---|---|---|---|---|
| H21 | train | 0 (0) | void: amendment A03 allows H21 on validation, holdout only | p n/a, BH q n/a | **void** |
| H21 | validation | 107,669 (249) | aucDifference 0.377 [0.367, 0.386] vs >= 0.050 | p 2.0e-4, BH q 2.0e-4 | **supported** |
| H22 | train | 0 (0) | void: amendment A03 allows H22 on validation, holdout only | p n/a, BH q n/a | **void** |
| H22 | validation | 107,669 (249) | aucDifference 0.026 [0.021, 0.032] vs >= 0.010 | p 2.0e-4, BH q 2.0e-4 | **supported** |
| H23 (secondary) | train | 0 (0) | void: amendment A03 allows H23 on validation, holdout only | p n/a, BH q n/a | **void** |
| H23 (secondary) | validation | 107,669 (249) | aucDifference 0.023 [0.015, 0.031] vs >= 0.000 | p 2.0e-4, BH q 2.0e-4 | **supported** |
| H24 (secondary) | train | 46,248 (755) | precisionOverBaseRate 7.962 [7.746, 8.176] vs >= 2.000 | p 2.0e-4, BH q 2.0e-4 | **supported** |
| H24 (secondary) | validation | 30,951 (249) | precisionOverBaseRate 5.054 [4.898, 5.219] vs >= 2.000 | p 2.0e-4, BH q 2.0e-4 | **supported** |

- H21 on validation (corobs - benchmark): weighted AUC benchmark 0.558, logistic 0.912, Corobs alone 0.935, combined 0.938; difference 0.377 [0.367, 0.386]; combined-model weight on logit(Corobs) 0.172435; 253115 training mornings, 224 dimensions, k 100.
- H22 on validation (combined - logistic): weighted AUC benchmark 0.558, logistic 0.912, Corobs alone 0.935, combined 0.938; difference 0.026 [0.021, 0.032]; combined-model weight on logit(Corobs) 0.172435; 253115 training mornings, 224 dimensions, k 100.
- H23 on validation (corobs - logistic): weighted AUC benchmark 0.558, logistic 0.912, Corobs alone 0.935, combined 0.938; difference 0.023 [0.015, 0.031]; combined-model weight on logit(Corobs) 0.172435; 253115 training mornings, 224 dimensions, k 100.
- H24 on train: top novelty decile reaches +20% by 09:45 at 4.57 % against a base rate of 0.57 % (lift 7.962 [7.746, 8.176]).
- H24 on validation: top novelty decile reaches +20% by 09:45 at 5.66 % against a base rate of 1.12 % (lift 5.054 [4.898, 5.219]).

## Catalyst-latency benchmark (H11, the gatekeeper)

The honest prior: a premarket volume/velocity detector at 08:00 mostly re-encodes "a catalyst was published 10–90 minutes ago". H11 scores an L2 logistic detector (fit on train, standardized 08:00 price/volume/run-up/filing-age features, no catalyst inputs) against the benchmark indicator "a news item or 8-K with pubToT ≤ 60 min exists at 08:00" on `reach20By0945`, with a day-block bootstrap of the AUC difference.

- **train**: AUC detector n/a vs benchmark n/a; difference n/a [n/a, n/a] vs ≥ n/a; p n/a, Holm p n/a; n 0 on 0 days; verdict **void**.
- **validation**: AUC detector 0.870 vs benchmark 0.549; difference 0.322 [0.313, 0.330] vs ≥ 0.050; p 2.0e-4, Holm p 0.002; n 50,904 on 249 days; verdict **supported**.
- **train, population (H11p)**: AUC detector n/a vs benchmark n/a; difference n/a [n/a, n/a] vs ≥ n/a; p n/a, Holm p n/a; n 0 on 0 days; verdict **void**.
- **validation, population (H11p)**: AUC detector 0.912 vs benchmark 0.558; difference 0.354 [0.343, 0.365] vs ≥ 0.050; p 2.0e-4, Holm p 8.0e-4; n 107,669 on 249 days; verdict **supported**.

Gate status: **passed on validation** — the detector carries information beyond catalyst latency among candidates and over the population; strategy work may proceed to H12.

## Exit timing (H4, H5, H13, H14)

**H4.** At least 60% of Tier50 gapper days print the RTH high of day before 09:46.
- train · proportionAtLeast: proportion 0.449 [0.427, 0.471] vs >= 0.600; n 1,922 (0 days); **refuted**
- validation · proportionAtLeast: proportion 0.401 [0.377, 0.424] vs >= 0.600; n 1,685 (0 days); **refuted**

**H5.** Among Tier20 gappers with a velocity death, the median deathToHodMin lies in [-2, +5] minutes, and fewer than 25% of full-session highs fall in the 09:30-09:31 bucket.
- train · medianDeathToHodMin: medianDeathToHodMin 42.000 [38.000, 44.000] vs >= -2.000; n 6,831 (753 days); **refuted**
- train · bucketShareBelow: share of HOD in 09:30-09:31 0.020 [0.017, 0.024] vs <= 0.250; n 6,831 (0 days); **supported**
- validation · medianDeathToHodMin: medianDeathToHodMin 35.000 [31.000, 38.000] vs >= -2.000; n 5,669 (249 days); **refuted**
- validation · bucketShareBelow: share of HOD in 09:30-09:31 0.015 [0.012, 0.019] vs <= 0.250; n 5,669 (0 days); **supported**

**H13.** Tier20 gappers whose extension from the 09:30 open at 09:45 is below 10% close below the open at least 85% of the time.
- train · proportionAtLeast: proportion 0.465 [0.451, 0.480] vs >= 0.850; n 4,382 (0 days); **refuted**
- validation · proportionAtLeast: proportion 0.487 [0.471, 0.503] vs >= 0.850; n 3,674 (0 days); **refuted**

**H14.** On Tier50 gapper days, selling at the 09:30 open beats selling at 09:15 net of cost (paired per event).
- train · meanPairedDifferencePct: meanPairedDifferencePct 0.196 [-0.144, 0.551] vs >= 0.500; n 1,893 (656 days); **undecided**
- validation · meanPairedDifferencePct: meanPairedDifferencePct 0.351 [0.023, 0.699] vs >= 0.500; n 1,676 (249 days); **undecided**

### Post-registration variant (not pre-registered; descriptive only, no verdict)

Ticket note 10972 found the frozen velocity death trivially early on a trend day and asked for a tighter secondary. The feature registry marks `velocityDeathNearPmHigh` as **post-registration**: death = two consecutive negative v3 readings within 30 minutes after the premarket high, after a run of at least 20% from the prior close. It is reported here and excluded from every verdict table.

| split | feature | Tier20 gapper days | death inside the window | median minutes after PM high |
|---|---|---|---|---|
| train | `velocityDeathNearPmHigh` ⚠ post-registration | 6,855 | 3,358 (49.0%) | 4.000 |
| validation | `velocityDeathNearPmHigh` ⚠ post-registration | 5,679 | 3,223 (56.8%) | 4.000 |

## What this says

- **train**: 17 hypotheses run — 4 supported, 8 refuted, 2 undecided, 3 void. Supported primaries: H1, H6, H9.
- **validation**: 17 hypotheses run — 4 supported, 10 refuted, 1 undecided, 2 void. Supported primaries: H11, H6, H9.
- H12 on train (candidate days): selected `pmVolRatio>=10 v3>=0.5 death3m stop10% sellBy09:28 vwapoff` (highest day-clustered lower bound on this split among configs with enough trades); mean net -3.610%/trade [-3.897, -3.327], 1,611 trades on 626 days, SPA p 1.000, DSR 1.9e-50, mean round-trip cost 3.90 %: **refuted**.
- H12 on validation (candidate days): selected `pmVolRatio>=10 v3>=0.5 death3m stop10% sellBy09:28 vwapoff` (carried from the latest train run); mean net -3.453%/trade [-3.750, -3.149], 1,254 trades on 248 days, SPA p 1.000, DSR 8.5e-36, mean round-trip cost 3.41 %: **refuted**.
- H12p on train (population): selected `pmVolRatio>=10 v3>=0.5 death3m stop10% sellBy09:28 vwapoff` (highest weighted day-clustered lower bound on this split among configs with enough unweighted trades); mean net -4.385%/trade [-4.727, -4.029], 1,878 trades on 661 days, SPA p 1.000, DSR 2.2e-65, mean round-trip cost 4.17 %: **refuted**.
- H12p on validation (population): selected `pmVolRatio>=10 v3>=0.5 death3m stop10% sellBy09:28 vwapoff` (carried from the latest train run of H12p); mean net -3.928%/trade [-4.295, -3.581], 1,438 trades on 249 days, SPA p 1.000, DSR 1.8e-54, mean round-trip cost 3.62 %: **refuted**.
- Data limits stated in the protocol stand: no float, short interest, dark-pool or options flow; halts are a bar-gap proxy; fiscal.ai wire stamps bound publication time from above.

## Kill-criteria status

| criterion | threshold | observed | status |
|---|---|---|---|
| Tier20 gapper days in train | >= 3,000 | 6,855 | met |
| SIP provenance of study minute bars | >= 95% | 100.00 % | met |
| H11 on validation | supported | supported | met |
| H11p on validation (amendment A01) | supported | supported | met |
| Post-unlock edits to protocol/hypotheses/params | none | holdout not opened | not yet applicable |
| H8 CIK coverage (drop, not redefine) | >= 50% | 83.7% | kept |

## Promotion and the live gate

1. Feature claim survives train (q < 0.05): yes
2. Replicates on validation with the same sign: yes
3. H12 survives SPA on validation: refuted (train: refuted)
4. Single holdout run nets >= 0.5%/trade with the day-clustered lower bound > 0: holdout locked
5. Paper shadow: 20 trading days of live snapshots agreeing with the backtest before any Live BrokerMode is offered.
6. Amendment A01: H11p on validation supported; H12p on validation refuted; H12p at the holdout run >= 0.5%/trade with lower bound > 0: holdout locked. Every condition above must hold; the amendment only adds conditions.
7. News-ride rule (H18, its own path, not stopped by H11): validation refuted; holdout >= 0.5%/trade with lower bound > 0: holdout locked; then the paper shadow.

## Exports

Under `gapper-study-2026-11/data/` with `manifest.json` (row count and SHA-256 of the rows per file):

| file | rows | sha256 |
|---|---|---|
| `events.csv` | 16,734 | `87caeadba934b0fe71fc6a43dbaec2013bb29d45577a268a45447353f176aa64` |
| `snapshots.csv` | 2,853,795 | `b459f58fd8ced5a287c4736d8f5d50afa3cb96ef37614d5464606cc8261e9d9a` |
| `labels.csv` | 2,853,795 | `308410b713a21cf77535690e9b3d5b2fba2bf5bfc4183f8f1ca6b38220ef454e` |

Holdout rows are excluded from every export until holdout-run.json exists.

## Run files

| hypothesis | split | computed (ET) | harness commit | file |
|---|---|---|---|---|
| H1 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H1-train-20261109-0835.json` |
| H1p | train | 2026-11-09 03:35 | `22a90dfb` | `test-H1p-train-20261109-0835.json` |
| H2 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H2-train-20261109-0835.json` |
| H3 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H3-train-20261109-0835.json` |
| H4 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H4-train-20261109-0835.json` |
| H5 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H5-train-20261109-0835.json` |
| H6 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H6-train-20261109-0835.json` |
| H7 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H7-train-20261109-0835.json` |
| H8 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H8-train-20261109-0835.json` |
| H9 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H9-train-20261109-0835.json` |
| H9p | train | 2026-11-09 03:35 | `22a90dfb` | `test-H9p-train-20261109-0835.json` |
| H10 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H10-train-20261109-0835.json` |
| H11 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H11-train-20261109-0835.json` |
| H11p | train | 2026-11-09 03:35 | `22a90dfb` | `test-H11p-train-20261109-0835.json` |
| H12 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H12-train-20261109-0835.json` |
| H12p | train | 2026-11-09 03:35 | `22a90dfb` | `test-H12p-train-20261109-0835.json` |
| H13 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H13-train-20261109-0835.json` |
| H14 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H14-train-20261109-0835.json` |
| H15 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H15-train-20261109-0835.json` |
| H16 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H16-train-20261109-0835.json` |
| H17 | train | 2026-11-09 03:35 | `22a90dfb` | `test-H17-train-20261109-0835.json` |
| H18 | train | 2026-11-09 03:59 | `22a90dfb` | `test-H18-train-20261109-0859.json` |
| H19 | train | 2026-11-09 03:59 | `22a90dfb` | `test-H19-train-20261109-0859.json` |
| H20 | train | 2026-11-09 03:59 | `22a90dfb` | `test-H20-train-20261109-0859.json` |
| H21 | train | 2026-11-09 04:09 | `22a90dfb` | `test-H21-train-20261109-0909.json` |
| H22 | train | 2026-11-09 04:09 | `22a90dfb` | `test-H22-train-20261109-0909.json` |
| H23 | train | 2026-11-09 04:09 | `22a90dfb` | `test-H23-train-20261109-0909.json` |
| H24 | train | 2026-11-09 04:09 | `22a90dfb` | `test-H24-train-20261109-0909.json` |
| H1 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H1-validation-20261109-0857.json` |
| H1p | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H1p-validation-20261109-0857.json` |
| H2 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H2-validation-20261109-0857.json` |
| H3 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H3-validation-20261109-0857.json` |
| H4 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H4-validation-20261109-0857.json` |
| H5 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H5-validation-20261109-0857.json` |
| H6 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H6-validation-20261109-0857.json` |
| H7 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H7-validation-20261109-0857.json` |
| H8 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H8-validation-20261109-0857.json` |
| H9 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H9-validation-20261109-0857.json` |
| H9p | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H9p-validation-20261109-0857.json` |
| H10 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H10-validation-20261109-0857.json` |
| H11 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H11-validation-20261109-0857.json` |
| H11p | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H11p-validation-20261109-0857.json` |
| H12 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H12-validation-20261109-0857.json` |
| H12p | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H12p-validation-20261109-0857.json` |
| H13 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H13-validation-20261109-0857.json` |
| H14 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H14-validation-20261109-0857.json` |
| H15 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H15-validation-20261109-0857.json` |
| H16 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H16-validation-20261109-0857.json` |
| H17 | validation | 2026-11-09 03:57 | `22a90dfb` | `test-H17-validation-20261109-0857.json` |
| H18 | validation | 2026-11-09 04:00 | `22a90dfb` | `test-H18-validation-20261109-0900.json` |
| H19 | validation | 2026-11-09 04:00 | `22a90dfb` | `test-H19-validation-20261109-0900.json` |
| H20 | validation | 2026-11-09 04:00 | `22a90dfb` | `test-H20-validation-20261109-0900.json` |
| H21 | validation | 2026-11-09 04:22 | `22a90dfb` | `test-H21-validation-20261109-0922.json` |
| H22 | validation | 2026-11-09 04:22 | `22a90dfb` | `test-H22-validation-20261109-0922.json` |
| H23 | validation | 2026-11-09 04:22 | `22a90dfb` | `test-H23-validation-20261109-0922.json` |
| H24 | validation | 2026-11-09 04:22 | `22a90dfb` | `test-H24-validation-20261109-0922.json` |

