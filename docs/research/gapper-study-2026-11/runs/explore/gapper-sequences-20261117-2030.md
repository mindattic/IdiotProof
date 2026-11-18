# Explore: long-only multi-step gapper templates against the single-step gapper (IP-T527, amendment 07)

Train only. Weighted (amendment 01 population). Folds:
- fold 1 (fit 2021-11..2022-10, eval 2022-11..2023-04)
- fold 2 (fit 2021-11..2023-04, eval 2023-05..2023-10)
- fold 3 (fit 2021-11..2023-10, eval 2023-11..2024-04)
- fold 4 (fit 2021-11..2024-04, eval 2024-05..2024-10)

Population: 261,809 train symbol-days (105,964 candidates, 155,845 amendment-01 sample days at weight 10). 2,102 had no minute bars; 237,203 never closed the minimum gap over the prior close inside the price band before the entry window closed, so step 0 could not enter and they were not replayed; 22,504 were replayed (0 failed).

Replay: `StrategyBacktester.Run` (IP-LAW-10: the evaluator the Monitor uses) on the day's own minute bars from 04:00 ET through the first bar at or after the strategy's last sell-by, prior close = the study's split-adjusted prior close. Entries and signal exits fill at the bar's close, a stop at the stop (or the open of a bar that gaps through it), as that evaluator does. One sequence run = one symbol-day on which step 0 entered; its return is the sum of its legs' returns, each leg on the same notional.

Cost per leg: the frozen model at $1,000 at the leg's own entry (prior-days Corwin-Schultz spread x the premarket multiplier 9.393 from spread-multiplier.json, square-root impact on the volume since 04:00, 20-session sigma, floor 0.5 %); 33 of 60,601 legs had no spread estimate (floor and impact only). The 0.5x and 0.25x columns scale the spread term only; the regular-hours column charges a later leg the prior-days spread with no multiplier plus $0.0035/share each side (the ORB report's cost) and leaves the ride at the frozen cost. None of the three decides anything. Intervals: day-clustered 95%, weighted.

## Strategies

**baseline: classic-gapper**

```
Ticker("SYM")
    .Name("SYM Gapper — Classic Gapper")
    .Session(IS.PREMARKET)
    .QuantityNotional(1000)
    .RequireEntryWindow("04:00", "09:00")
    .IsGapUp(5)
    .IsVolumeAbove(2)
    .IsPriceBetween(1, 50)
    .Long()
    .StopLossPercent(5)
    .SellBy("09:28")
    .PeakGiveback(25, "09:15")
```

**gapper-ride-vwap-pullback**

```
Ticker("SYM")
    .Step("ride")
    .Name("SYM Gapper sequence - ride, then VWAP pullback")
    .Session(IS.EXTENDED)
    .Quantity(1)
    .RequireEntryWindow("04:00", "09:00")
    .IsGapUp(5)
    .IsVolumeAbove(2)
    .IsPriceBetween(1, 50)
    .Long()
    .StopLossPercent(5)
    .SellBy("09:28")
    .PeakGiveback(25, "09:15")
    .ThenWhen(IsAboveVwap())
    .Within(1)
    .Step("pullback")
    .Session(IS.EXTENDED)
    .Quantity(1)
    .RequireEntryWindow("09:35", "10:30")
    .IsAboveVwap()
    .IsHigherLow()
    .IsGapUp(5)
    .Long()
    .StopLossPercent(8)
    .SellBy("11:00")
    .PeakGiveback(25)
```

**gapper-ride-retest**

```
Ticker("SYM")
    .Step("ride")
    .Name("SYM Gapper sequence - ride, then pre-bell high retest")
    .Session(IS.EXTENDED)
    .Quantity(1)
    .RequireEntryWindow("04:00", "09:00")
    .IsGapUp(5)
    .IsVolumeAbove(2)
    .IsPriceBetween(1, 50)
    .Long()
    .StopLossPercent(5)
    .SellBy("09:28")
    .PeakGiveback(25, "09:15")
    .ThenWhen(IsAboveVwap())
    .Within(1)
    .Step("retest")
    .Session(IS.EXTENDED)
    .Quantity(1)
    .RequireEntryWindow("09:31", "09:40")
    .IsAboveVwap()
    .IsAtResistance(1)
    .VelocityAbove(0.2, 3)
    .IsGapUp(5)
    .Long()
    .StopLossPercent(8)
    .SellBy("09:44")
    .PeakGiveback(25)
```

## Per sequence run (the unit amendment 07 decides on)

| strategy | runs | days | weighted runs | gapper days (Tier20+) | legs: step 0 / step 1 | gross mean | mean frozen cost | **net mean, frozen [95%]** | net median | win rate | net 0.5x spread [95%] | net 0.25x spread [95%] | net, later legs at regular-hours cost [95%] | fold 1 | fold 2 | fold 3 | fold 4 | folds > 0 | DSR |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| baseline: classic-gapper | 15,216 | 755 | 28,176 | 3,710 | 15,216 / 0 | -2.72% | +5.53% | **-8.26% [-8.56%, -7.95%]** | -7.45% | 8.2 % | -7.85% [-8.14%, -7.55%] | -7.65% [-7.94%, -7.35%] | -8.26% [-8.56%, -7.95%] | -8.41% | -8.72% | -8.83% | -8.10% | 0/4 | 0.000 |
| gapper-ride-vwap-pullback | 15,216 | 755 | 28,176 | 3,710 | 15,216 / 9,461 | -2.77% | +6.12% | **-8.89% [-9.20%, -8.59%]** | -7.87% | 7.2 % | -8.33% [-8.63%, -8.03%] | -8.06% [-8.35%, -7.76%] | -8.44% [-8.74%, -8.13%] | -9.00% | -9.35% | -9.52% | -8.81% | 0/4 | 0.000 |
| gapper-ride-retest | 15,216 | 755 | 28,176 | 3,710 | 15,216 / 5,492 | -2.71% | +5.87% | **-8.58% [-8.88%, -8.28%]** | -7.71% | 7.7 % | -8.09% [-8.38%, -7.79%] | -7.85% [-8.14%, -7.56%] | -8.32% [-8.62%, -8.02%] | -8.72% | -9.04% | -9.21% | -8.43% | 0/4 | 0.000 |

## The later leg, and the sequence against the baseline on the same symbol-days

| strategy | runs with the same ride as the baseline | hand-offs (ThenWhen held) | later-leg trades | days | later-leg gross mean [95%] | **later-leg net mean, frozen [95%]** | later-leg net median | later-leg win rate | later-leg net at regular-hours cost [95%] | later-leg exits | **sequence minus baseline, paired [95%]** |
|---|---|---|---|---|---|---|---|---|---|---|---|
| gapper-ride-vwap-pullback | 15,216 of 15,216 (baseline 15,216) | 14,247 | 9,461 | 755 | -0.12% [-0.17%, -0.07%] | **-1.47% [-1.55%, -1.40%]** | -0.98% | 13.3 % | -0.42% [-0.48%, -0.36%] | Peak giveback 9,007, Time exit 259, Stop loss 195 | **-0.64% [-0.67%, -0.60%]** |
| gapper-ride-retest | 15,216 of 15,216 (baseline 15,216) | 13,252 | 5,492 | 743 | +0.04% [-0.04%, +0.12%] | **-1.35% [-1.45%, -1.25%]** | -1.04% | 20.4 % | -0.25% [-0.33%, -0.16%] | Peak giveback 5,037, Time exit 365, Stop loss 90 | **-0.33% [-0.36%, -0.30%]** |

Ride exits (step 0): baseline: classic-gapper: Stop loss 6,940, Peak giveback 6,471, Time exit 1,805; gapper-ride-vwap-pullback: Stop loss 6,940, Peak giveback 6,471, Time exit 1,805; gapper-ride-retest: Stop loss 6,940, Peak giveback 6,471, Time exit 1,805.

## Amendment 07 criterion (train, frozen cost)

A template passes only if all hold: net mean per run >= +1.00% with the lower bound above 0; >= 150 runs on >= 40 days; >= 150 later-leg trades; net mean above 0 in >= 3 of the 4 fold windows; sequence minus baseline (paired, same symbol-days) above 0 with the lower bound above 0; deflated Sharpe >= 0.95 at N = 5,657 trials (the ledger after this run; variance of this run's trial Sharpes 0.0006).

| template | mean >= bar, lower bound > 0 | runs and days | later legs | folds | beats the baseline | DSR | verdict |
|---|---|---|---|---|---|---|---|
| gapper-ride-vwap-pullback | no (-8.89% [-9.20%, -8.59%]) | yes (15,216 runs, 755 days) | yes (9,461) | no (0/4) | no (-0.64% [-0.67%, -0.60%]) | no (0.000) | **FAIL** |
| gapper-ride-retest | no (-8.58% [-8.88%, -8.28%]) | yes (15,216 runs, 755 days) | yes (5,492) | no (0/4) | no (-0.33% [-0.36%, -0.30%]) | no (0.000) | **FAIL** |

Train only. A pass here would make a template selectable, never armed, and it would still owe validation, the single holdout run and the paper shadow (protocol, promotion path). A fail leaves the templates as samples.

Trials logged: 3 (ids 5655..5657); ledger total 5657.
