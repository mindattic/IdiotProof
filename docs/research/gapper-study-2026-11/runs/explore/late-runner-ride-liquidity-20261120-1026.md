# Explore: liquidity-filtered late-runner ride (amendment 11, H30, IP-T619)

Train only (2021-11-01..2024-10-31); validation and the holdout are not read. Every choice is amendment-11.json's, registered and pushed before this code was written. One configuration, not a grid. Research only: nothing here arms, sizes, places or exits an order.

## Entries

- Move starts (amendment 09's train rows with an m0): 180,303; not scored (a fold without both classes to fit): 0.
- H27 full-feature model, pooled out of fold: s >= 0.50 on 437 move starts; of those, 137 below the $2,000,000 20-day-ADV floor. After both: 300 signals.
- No trade: 0 with no bar with volume after m0 before 15:55; 29 whose fill was already >= +20% over the prior close. Entries: 271.
- Cost model (amendment 10's, frozen): prior-days Corwin-Schultz x 9.393 (spread-multiplier.json), square-root impact at $1,000, floor 0.5 %; mean round trip 2.79%, median 2.60%; no 08:00 snapshot row 0, no Corwin-Schultz estimate 0 (spread 0, floor applies).
- Exit ladder (GapperRuleSimulator unchanged): stop 10%, VWAP loss on, velocity death 3m, sell-by 15:55 ET, target label20 = resting sell at 1.20 x the prior close.

## H30 (train, single configuration)

| trades | days | mean net | 95% interval | Sharpe | DSR | folds (mean net) | exits |
|---|---|---|---|---|---|---|---|
| 271 | 202 | -3.13% | [-4.10%, -2.26%] | -0.682 | 0.000 | -2.26% / -1.49% / -2.22% / -3.44% / -4.97% | velocity-death 128, target +20% 86, vwap-loss 48, stop 8, sell-by 1 |

## Verdict on train

**H30 fails on train**: mean -3.13% < +1.0%; lower bound -4.10% not > 0; DSR 0.000 < 0.95; 0 of 5 folds positive < 3. Per amendment 11: stop. Validation is not read, and no other liquidity floor, score threshold, exit or cost model is tried under this amendment. The move-start signal stays real (amendment 09); this specific liquidity-filtered entry/exit does not make it tradable after costs.


Trials logged: 1 (ids 5667..5667); ledger total 5667.
