# Explore: trading the late-runner move start (amendment 10, H29, IP-T617)

Train only (2021-11-01..2024-10-31); validation and the holdout are not read. Every choice is amendment-10.json's, registered and pushed before this code was written. Research only: nothing here arms, sizes, places or exits an order.

## Entries

- Move starts (amendment 09's train rows with an m0): 180,303 (weighted 1,052,817); not scored (a fold without both classes to fit): 0.
- H27 full-feature model, pooled out of fold over amendment 09's five month blocks: s >= 0.50 on 437 move starts (0.24 %; weighted 482); y = 1 on 238 of them.
- No trade: 0 with no bar with volume after m0 before 15:55; 48 whose fill was already >= +20% over the prior close. Entries: 389.
- Cost model (H18's, frozen): prior-days Corwin-Schultz x 9.393 (spread-multiplier.json), square-root impact at $1,000, floor 0.5 %; mean round trip 2.83%, median 2.58%; no 08:00 snapshot row 0, no Corwin-Schultz estimate 8 (spread 0, floor applies).
- Exit ladder (GapperRuleSimulator unchanged): stop 10%, VWAP loss on, velocity death 3m, sell-by per configuration; target label20 = resting sell at 1.20 x the prior close.

## H29 grid (train)

Weighted mean net per trade (amendment 01 weights) with a day-clustered bootstrap 95% interval; trades and days unweighted; DSR over the 4 configurations at the unweighted trade count; per-fold weighted means over amendment 09's five month blocks.

| configuration | trades | days | mean net | 95% interval | Sharpe | DSR | BH q | folds (mean net) | exits |
|---|---|---|---|---|---|---|---|---|---|
| s>=threshold target none timeStop 15:55 | 389 | 257 | -3.41% | [-4.18%, -2.70%] | -0.663 | 0.000 | 1.000 | -1.93% / -2.81% / -3.02% / -3.51% / -5.13% | velocity-death 252, vwap-loss 113, stop 15, sell-by 9 |
| s>=threshold target none timeStop entry+60m | 389 | 257 | -3.44% | [-4.22%, -2.74%] | -0.674 | 0.000 | 1.000 | -2.03% / -2.95% / -3.02% / -3.51% / -5.18% | velocity-death 245, vwap-loss 108, sell-by 21, stop 15 |
| s>=threshold target label20 timeStop 15:55 **(selected)** | 389 | 257 | -3.12% | [-3.90%, -2.44%] | -0.672 | 0.000 | 1.000 | -1.66% / -2.44% / -2.55% / -3.26% / -5.19% | velocity-death 170, target +20% 112, vwap-loss 85, stop 13, sell-by 9 |
| s>=threshold target label20 timeStop entry+60m | 389 | 257 | -3.16% | [-3.94%, -2.48%] | -0.685 | 0.000 | 1.000 | -1.76% / -2.58% / -2.57% / -3.26% / -5.24% | velocity-death 165, target +20% 110, vwap-loss 80, sell-by 21, stop 13 |

Reality check over the 4 configurations (per-day weighted mean net per population cell, zero where a configuration did not trade; 1000 resamples): White p 1.000, SPA p 1.000, best s>=threshold target label20 timeStop 15:55.

## Verdict on train

Selected (highest weighted lower bound among configurations with >= 150 trades): **s>=threshold target label20 timeStop 15:55**.

**H29 fails on train**: mean -3.12% < +1.0%; lower bound -3.90% not > 0; SPA p 1.000 not < 0.05; DSR 0.000 < 0.95; 0 of 5 folds positive < 3. Per amendment 10: stop. Validation is not read, and no other threshold, target, time stop, stop size or cost model is tried under this amendment. The move-start signal stays real (amendment 09); this entry and exit do not make it tradable after costs.


Trials logged: 4 (ids 5663..5666); ledger total 5666.
