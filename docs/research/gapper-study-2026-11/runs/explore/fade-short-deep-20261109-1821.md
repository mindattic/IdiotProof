# Explore: fade short deep dive at 09:45 (IP-T479: DSR, time of day, MAE-aware exits, tail sizing, squeeze predictors)

Train only. Weighted (amendment 01 population). Folds:
- fold 1 (fit 2021-11..2022-10, eval 2022-11..2023-04)
- fold 2 (fit 2021-11..2023-04, eval 2023-05..2023-10)
- fold 3 (fit 2021-11..2023-10, eval 2023-11..2024-04)
- fold 4 (fit 2021-11..2024-04, eval 2024-05..2024-10)

Signals: pmHigh>=+100% ext<10%; pmHigh>=+50% ext<10% (both still >= +20% at 09:45; from bars ending by 09:45). Entry: SSR uptick entry as in IP-T476 (the first bar after 09:45, or under Rule 201 the first uptick open within 30 minutes), Reg SHO D-1 names included. Net of the frozen cost model at $1,000 (premarket multiplier 9.393 from spread-multiplier.json, applied unchanged after the open) and 50 % APR borrow (one day). Every name assumed borrowable. Pooled = every train day; per fold = the four eval windows.

Candidates: 840 names pass the looser signal with an entry. The base exit reproduces IP-T476's headline: pmHigh>=+100% ext<10% 324 trades +8.16% [+5.71%, +10.61%]; pmHigh>=+50% ext<10% 839 trades +3.59% [+2.06%, +5.12%].

## 1. Deflated Sharpe ratio

`MultipleTesting.DeflatedSharpe` (Bailey & Lopez de Prado): the probabilistic Sharpe of the unweighted net series against the expected maximum Sharpe of N skill-less trials. N = the ledger total after this run = 1,157 (1,095 before + 62 logged here). The ledger stores no Sharpe, so the variance of trial Sharpes is estimated from the fade-short family: 480 IP-T476 trials (fade-short, fade-short-stress) with each Sharpe implied from its mean and day-clustered interval (per-trade SR ~ mean / (half-width / 1.96 x sqrt(trades)); per-day SR ~ mean / (half-width / 1.96 x sqrt(days))), plus the 48 exact Sharpes of this run's exits. Sensitivity: the variance over every return-metric trial with an interval (568 implied, exit routes included). Frozen minimum (params.json statistics.dsrMin): 0.95.

| signal | exit | series | T (observations) | SR per period | skew | kurtosis | V[SR] trials | E[max SR] at N | DSR | DSR (all-return V) | clears dsrMin |
|---|---|---|---|---|---|---|---|---|---|---|---|
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | per trade | 324 | 0.377 | -1.06 | 7.2 | 0.0105 | 0.338 | 0.709 | 0.000 | no |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | per trade-day (equal-weight day mean) | 249 | 0.420 | -0.74 | 6.2 | 0.0136 | 0.384 | 0.677 | 0.000 | no |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | per trade | 839 | 0.176 | -1.36 | 7.9 | 0.0105 | 0.338 | 0.000 | 0.000 | no |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | per trade-day (equal-weight day mean) | 491 | 0.190 | -1.21 | 8.9 | 0.0136 | 0.384 | 0.000 | 0.000 | no |

The DSR column of the exit table below is the per-trade DSR of each exit at the same N and variance.

## 2. Time-of-day profile of the fade

Gross short return from the entry fill marked at the open of the first bar at or after each time (before cost; 0 when the uptick entry had not filled yet). Mean with a day-clustered 95% interval; weighted median with a day-block bootstrap 95% interval (1,000 draws). Captured = mean at the time / mean at 15:55.

**pmHigh>=+100% ext<10%** (324 trades)

| time ET | mean [95% day-clustered] | median [95% day-block] | captured of the 15:55 mean | share of trades in profit |
|---|---|---|---|---|
| 10:00 | +1.96% [+0.91%, +3.01%] | +2.18% [+1.31%, +3.28%] | 19.1 % | 63.9 % |
| 10:30 | +4.88% [+3.45%, +6.30%] | +4.97% [+3.93%, +7.05%] | 47.5 % | 70.7 % |
| 11:00 | +5.38% [+3.45%, +7.30%] | +7.51% [+6.30%, +8.46%] | 52.3 % | 75.6 % |
| 12:00 | +7.24% [+5.11%, +9.38%] | +9.61% [+7.40%, +11.45%] | 70.5 % | 78.4 % |
| 13:00 | +7.68% [+5.20%, +10.15%] | +9.81% [+8.49%, +12.22%] | 74.7 % | 76.2 % |
| 14:00 | +9.03% [+6.64%, +11.42%] | +11.11% [+9.24%, +12.74%] | 87.9 % | 76.5 % |
| 15:00 | +9.93% [+7.36%, +12.50%] | +12.50% [+10.09%, +14.47%] | 96.7 % | 79.6 % |
| 15:55 | +10.27% [+7.83%, +12.72%] | +11.22% [+9.73%, +13.85%] | 100.0 % | 76.9 % |

When the highest high between entry and 15:55 printed (MAE timing):

| group | trades | median ET | median minutes after entry | entry-10:00 | 10:00-10:30 | 10:30-11:00 | 11:00-12:00 | 12:00-14:00 | 14:00-15:55 |
|---|---|---|---|---|---|---|---|---|---|
| all | 324 | 10:05 | 19.5 | 46.0 % | 14.2 % | 9.9 % | 8.3 % | 11.1 % | 10.5 % |
| squeeze decile (MAE >= +38.44%) | 33 | 12:15 | 149 | 3.0 % | 9.1 % | 21.2 % | 9.1 % | 33.3 % | 24.2 % |
| rest | 291 | 09:59 | 13 | 50.9 % | 14.8 % | 8.6 % | 8.2 % | 8.6 % | 8.9 % |

**pmHigh>=+50% ext<10%** (839 trades)

| time ET | mean [95% day-clustered] | median [95% day-block] | captured of the 15:55 mean | share of trades in profit |
|---|---|---|---|---|
| 10:00 | +1.52% [+0.94%, +2.09%] | +2.18% [+1.68%, +2.65%] | 27.2 % | 63.1 % |
| 10:30 | +3.30% [+2.48%, +4.12%] | +4.29% [+3.37%, +5.02%] | 59.3 % | 68.5 % |
| 11:00 | +3.55% [+2.52%, +4.59%] | +5.50% [+4.48%, +6.52%] | 63.8 % | 70.0 % |
| 12:00 | +4.42% [+3.18%, +5.65%] | +7.03% [+5.82%, +7.83%] | 79.2 % | 72.3 % |
| 13:00 | +4.90% [+3.51%, +6.30%] | +7.67% [+6.61%, +8.49%] | 88.0 % | 72.3 % |
| 14:00 | +5.75% [+4.28%, +7.22%] | +8.75% [+7.65%, +9.53%] | 103.1 % | 74.3 % |
| 15:00 | +5.94% [+4.36%, +7.52%] | +8.09% [+7.27%, +9.28%] | 106.5 % | 74.3 % |
| 15:55 | +5.57% [+4.04%, +7.11%] | +8.00% [+6.49%, +8.99%] | 100.0 % | 70.7 % |

When the highest high between entry and 15:55 printed (MAE timing):

| group | trades | median ET | median minutes after entry | entry-10:00 | 10:00-10:30 | 10:30-11:00 | 11:00-12:00 | 12:00-14:00 | 14:00-15:55 |
|---|---|---|---|---|---|---|---|---|---|
| all | 839 | 10:05 | 19 | 44.3 % | 16.1 % | 7.6 % | 8.6 % | 9.3 % | 14.1 % |
| squeeze decile (MAE >= +38.56%) | 84 | 12:12 | 146.5 | 1.2 % | 14.3 % | 10.7 % | 16.7 % | 28.6 % | 28.6 % |
| rest | 755 | 10:00 | 14 | 49.1 % | 16.3 % | 7.3 % | 7.7 % | 7.2 % | 12.5 % |

## 3. MAE-aware exits

All net of the same cost and borrow. Stops fill at the worse of the level and the bar open; cover limits at the better of the level and the open; the trailing stop sits that far over the lowest low of the bars before the current one, armed once a low reached the activation level. Volatility stops: k x the 09:30-09:45 range over the 09:30 open, or k x the mean daily (high - low) / close over the 14 sessions before D (a name without it trades unstopped). Max DD = largest peak-to-trough of the cumulative per-trade net sum in date order, in units of one position. DSR = per-trade deflated Sharpe at the N and variance above.

| signal | family | exit | trades | days | mean net [95% day-clustered] | per fold | folds > 0 | win | worst 1% | max DD | MAE p90 | stopped | SR | DSR |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| pmHigh>=+100% ext<10% | base | cover 15:55, no stop (IP-T476 headline) | 324 | 249 | +8.16% [+5.71%, +10.61%] | +6.06% / +12.98% / +10.01% / +8.63% | 4/4 | 74.4 % | -66.24% | -251.50% | +38.44% | 0.0 % | 0.377 | 0.709 |
| pmHigh>=+100% ext<10% | fixed stop | cover 15:55, stop +20% | 324 | 249 | +4.20% [+1.79%, +6.60%] | +6.40% / +4.50% / +5.18% / +3.46% | 4/4 | 62.0 % | -25.48% | -170.42% | +23.47% | 27.5 % | 0.202 | 0.006 |
| pmHigh>=+100% ext<10% | fixed stop | cover 15:55, stop +30% | 324 | 249 | +5.40% [+2.88%, +7.93%] | +6.17% / +8.55% / +8.05% / +3.89% | 4/4 | 68.8 % | -35.48% | -233.78% | +32.16% | 17.3 % | 0.242 | 0.048 |
| pmHigh>=+100% ext<10% | fixed stop | cover 15:55, stop +40% | 324 | 249 | +6.49% [+3.98%, +8.99%] | +5.98% / +12.98% / +8.14% / +4.93% | 4/4 | 72.2 % | -44.02% | -236.18% | +38.44% | 9.9 % | 0.289 | 0.213 |
| pmHigh>=+100% ext<10% | catastrophe stop | cover 15:55, stop +50% | 324 | 249 | +6.12% [+3.47%, +8.78%] | +5.54% / +12.98% / +7.58% / +4.57% | 4/4 | 72.8 % | -53.71% | -326.18% | +38.44% | 8.3 % | 0.257 | 0.099 |
| pmHigh>=+100% ext<10% | catastrophe stop | cover 15:55, stop +75% | 324 | 249 | +6.06% [+3.21%, +8.90%] | +5.81% / +12.98% / +9.02% / +4.16% | 4/4 | 73.5 % | -78.40% | -385.53% | +38.44% | 4.9 % | 0.237 | 0.064 |
| pmHigh>=+100% ext<10% | volatility stop | cover 15:55, stop 2 x 09:30-09:45 range | 324 | 249 | +5.97% [+3.41%, +8.54%] | +4.29% / +12.50% / +7.80% / +4.07% | 4/4 | 71.3 % | -54.27% | -295.98% | +35.30% | 13.0 % | 0.261 | 0.102 |
| pmHigh>=+100% ext<10% | volatility stop | cover 15:55, stop 3 x 09:30-09:45 range | 324 | 249 | +5.96% [+3.24%, +8.67%] | +4.80% / +12.98% / +7.45% / +4.40% | 4/4 | 73.1 % | -79.63% | -313.49% | +38.44% | 7.1 % | 0.240 | 0.064 |
| pmHigh>=+100% ext<10% | volatility stop | cover 15:55, stop 4 x 09:30-09:45 range | 324 | 249 | +7.33% [+4.68%, +9.98%] | +6.06% / +12.98% / +10.01% / +6.54% | 4/4 | 74.1 % | -77.96% | -377.14% | +38.44% | 3.4 % | 0.311 | 0.349 |
| pmHigh>=+100% ext<10% | volatility stop | cover 15:55, stop 2 x prior 14-day range% | 324 | 249 | +5.59% [+3.09%, +8.09%] | +6.65% / +9.28% / +7.61% / +4.17% | 4/4 | 67.0 % | -43.77% | -208.09% | +29.51% | 20.1 % | 0.263 | 0.096 |
| pmHigh>=+100% ext<10% | volatility stop | cover 15:55, stop 3 x prior 14-day range% | 324 | 249 | +6.21% [+3.62%, +8.80%] | +6.32% / +11.21% / +8.72% / +4.51% | 4/4 | 71.3 % | -60.25% | -326.99% | +35.17% | 12.7 % | 0.275 | 0.152 |
| pmHigh>=+100% ext<10% | volatility stop | cover 15:55, stop 4 x prior 14-day range% | 324 | 249 | +6.35% [+3.73%, +8.97%] | +5.47% / +12.98% / +8.04% / +4.47% | 4/4 | 72.8 % | -75.39% | -332.88% | +36.27% | 9.3 % | 0.265 | 0.132 |
| pmHigh>=+100% ext<10% | time stop | cover 11:00, no stop | 324 | 249 | +3.26% [+1.32%, +5.21%] | +2.46% / +6.99% / +5.56% / +0.85% | 4/4 | 68.8 % | -55.93% | -295.57% | +27.98% | 0.0 % | 0.191 | 0.015 |
| pmHigh>=+100% ext<10% | time stop | cover 12:00, no stop | 324 | 249 | +5.13% [+2.99%, +7.27%] | +4.34% / +9.08% / +7.50% / +5.32% | 4/4 | 71.9 % | -61.98% | -196.27% | +34.88% | 0.0 % | 0.274 | 0.187 |
| pmHigh>=+100% ext<10% | time stop | cover 13:00, no stop | 324 | 249 | +5.56% [+3.09%, +8.04%] | +3.29% / +11.05% / +8.03% / +5.37% | 4/4 | 70.7 % | -76.17% | -245.92% | +35.30% | 0.0 % | 0.272 | 0.170 |
| pmHigh>=+100% ext<10% | time stop | cover 14:00, no stop | 324 | 249 | +6.92% [+4.53%, +9.31%] | +4.21% / +10.52% / +9.78% / +7.55% | 4/4 | 73.1 % | -66.17% | -201.70% | +37.01% | 0.0 % | 0.345 | 0.541 |
| pmHigh>=+100% ext<10% | partial cover | half at 11:00, rest 15:55 | 324 | 249 | +5.71% [+3.77%, +7.65%] | +4.26% / +9.99% / +7.78% / +4.74% | 4/4 | 73.8 % | -52.26% | -222.53% | +38.44% | 0.0 % | 0.339 | 0.504 |
| pmHigh>=+100% ext<10% | partial cover | half on a -15% move (limit), rest 15:55 | 324 | 249 | +6.92% [+4.89%, +8.95%] | +5.48% / +10.81% / +8.69% / +7.57% | 4/4 | 78.1 % | -66.24% | -215.28% | +38.44% | 0.0 % | 0.387 | 0.717 |
| pmHigh>=+100% ext<10% | cover into weakness | cover on a -10% move (limit), else 15:55 | 324 | 249 | +3.53% [+2.04%, +5.02%] | +1.59% / +6.17% / +5.27% / +3.34% | 4/4 | 85.2 % | -62.33% | -194.69% | +35.07% | 0.0 % | 0.261 | 0.193 |
| pmHigh>=+100% ext<10% | cover into weakness | cover on a -20% move (limit), else 15:55 | 324 | 249 | +6.73% [+4.71%, +8.74%] | +4.81% / +10.13% / +8.97% / +7.46% | 4/4 | 75.9 % | -66.24% | -197.67% | +37.29% | 0.0 % | 0.372 | 0.657 |
| pmHigh>=+100% ext<10% | cover into weakness | cover on a -30% move (limit), else 15:55 | 324 | 249 | +7.35% [+5.11%, +9.60%] | +5.95% / +11.50% / +9.01% / +8.37% | 4/4 | 74.4 % | -66.24% | -223.30% | +38.44% | 0.0 % | 0.368 | 0.653 |
| pmHigh>=+100% ext<10% | cover into weakness | trail 5% over the low once -10%, else 15:55 | 324 | 249 | +3.48% [+1.96%, +5.00%] | +1.31% / +5.85% / +5.52% / +3.20% | 4/4 | 85.5 % | -62.33% | -195.84% | +35.07% | 82.1 % | 0.249 | 0.144 |
| pmHigh>=+100% ext<10% | cover into weakness | trail 10% over the low once -10%, else 15:55 | 324 | 249 | +3.94% [+2.28%, +5.61%] | +1.56% / +6.38% / +4.84% / +4.59% | 4/4 | 77.5 % | -62.33% | -168.46% | +35.07% | 77.8 % | 0.257 | 0.147 |
| pmHigh>=+100% ext<10% | cover into weakness | trail 5% over the low once -20%, else 15:55 | 324 | 249 | +6.60% [+4.57%, +8.62%] | +5.07% / +9.36% / +8.62% / +7.65% | 4/4 | 75.9 % | -66.24% | -194.44% | +37.29% | 52.8 % | 0.362 | 0.616 |
| pmHigh>=+100% ext<10% | cover into weakness | trail 10% over the low once -20%, else 15:55 | 324 | 249 | +6.49% [+4.40%, +8.57%] | +4.67% / +9.61% / +8.72% / +7.49% | 4/4 | 75.9 % | -66.24% | -208.15% | +37.29% | 46.0 % | 0.351 | 0.566 |
| pmHigh>=+50% ext<10% | base | cover 15:55, no stop (IP-T476 headline) | 839 | 491 | +3.59% [+2.06%, +5.12%] | +3.14% / +5.05% / +4.62% / +5.26% | 4/4 | 66.3 % | -69.10% | -431.38% | +38.56% | 0.0 % | 0.176 | 0.000 |
| pmHigh>=+50% ext<10% | fixed stop | cover 15:55, stop +20% | 839 | 491 | +1.35% [+0.11%, +2.59%] | +2.91% / +0.50% / +1.26% / +2.08% | 4/4 | 58.2 % | -25.80% | -251.27% | +22.63% | 26.6 % | 0.075 | 0.000 |
| pmHigh>=+50% ext<10% | fixed stop | cover 15:55, stop +30% | 839 | 491 | +1.89% [+0.49%, +3.28%] | +2.70% / +2.46% / +2.32% / +2.26% | 4/4 | 62.7 % | -35.44% | -282.84% | +31.29% | 16.7 % | 0.096 | 0.000 |
| pmHigh>=+50% ext<10% | fixed stop | cover 15:55, stop +40% | 839 | 491 | +2.61% [+1.18%, +4.05%] | +2.81% / +4.95% / +2.31% / +2.65% | 4/4 | 64.8 % | -44.02% | -265.44% | +38.56% | 9.5 % | 0.130 | 0.000 |
| pmHigh>=+50% ext<10% | catastrophe stop | cover 15:55, stop +50% | 839 | 491 | +2.74% [+1.24%, +4.24%] | +3.12% / +4.91% / +3.36% / +2.79% | 4/4 | 65.4 % | -53.40% | -335.44% | +38.56% | 6.6 % | 0.133 | 0.000 |
| pmHigh>=+50% ext<10% | catastrophe stop | cover 15:55, stop +75% | 839 | 491 | +2.57% [+0.90%, +4.25%] | +2.88% / +5.08% / +3.96% / +2.68% | 4/4 | 65.9 % | -77.73% | -512.74% | +38.56% | 3.8 % | 0.115 | 0.000 |
| pmHigh>=+50% ext<10% | volatility stop | cover 15:55, stop 2 x 09:30-09:45 range | 839 | 491 | +2.54% [+1.17%, +3.92%] | +2.31% / +4.97% / +3.30% / +2.90% | 4/4 | 63.9 % | -48.48% | -340.18% | +32.67% | 15.0 % | 0.130 | 0.000 |
| pmHigh>=+50% ext<10% | volatility stop | cover 15:55, stop 3 x 09:30-09:45 range | 839 | 491 | +2.76% [+1.26%, +4.26%] | +2.73% / +5.16% / +3.27% / +2.62% | 4/4 | 65.6 % | -66.00% | -351.31% | +36.27% | 7.7 % | 0.132 | 0.000 |
| pmHigh>=+50% ext<10% | volatility stop | cover 15:55, stop 4 x 09:30-09:45 range | 839 | 491 | +3.24% [+1.65%, +4.83%] | +3.31% / +4.83% / +4.58% / +3.75% | 4/4 | 66.0 % | -71.54% | -367.17% | +38.44% | 4.3 % | 0.155 | 0.000 |
| pmHigh>=+50% ext<10% | volatility stop | cover 15:55, stop 2 x prior 14-day range% | 839 | 491 | +2.30% [+0.93%, +3.67%] | +3.54% / +3.83% / +2.67% / +2.53% | 4/4 | 61.0 % | -45.03% | -385.36% | +28.76% | 20.5 % | 0.124 | 0.000 |
| pmHigh>=+50% ext<10% | volatility stop | cover 15:55, stop 3 x prior 14-day range% | 839 | 491 | +2.60% [+1.12%, +4.07%] | +3.05% / +4.63% / +2.97% / +3.09% | 4/4 | 63.9 % | -58.97% | -449.07% | +32.44% | 12.5 % | 0.131 | 0.000 |
| pmHigh>=+50% ext<10% | volatility stop | cover 15:55, stop 4 x prior 14-day range% | 839 | 491 | +2.72% [+1.25%, +4.19%] | +2.51% / +5.05% / +3.57% / +2.67% | 4/4 | 65.1 % | -69.10% | -379.22% | +35.37% | 8.3 % | 0.131 | 0.000 |
| pmHigh>=+50% ext<10% | time stop | cover 11:00, no stop | 839 | 491 | +1.57% [+0.53%, +2.61%] | +2.12% / +2.40% / +2.82% / +1.02% | 4/4 | 63.5 % | -50.12% | -293.75% | +27.98% | 0.0 % | 0.109 | 0.000 |
| pmHigh>=+50% ext<10% | time stop | cover 12:00, no stop | 839 | 491 | +2.43% [+1.20%, +3.66%] | +3.98% / +2.73% / +3.58% / +3.47% | 4/4 | 66.5 % | -53.36% | -377.83% | +32.44% | 0.0 % | 0.141 | 0.000 |
| pmHigh>=+50% ext<10% | time stop | cover 13:00, no stop | 839 | 491 | +2.92% [+1.53%, +4.31%] | +3.58% / +4.48% / +3.96% / +3.35% | 4/4 | 67.3 % | -74.87% | -278.77% | +34.89% | 0.0 % | 0.156 | 0.000 |
| pmHigh>=+50% ext<10% | time stop | cover 14:00, no stop | 839 | 491 | +3.76% [+2.30%, +5.22%] | +3.56% / +5.06% / +5.31% / +4.74% | 4/4 | 70.3 % | -78.95% | -283.16% | +35.37% | 0.0 % | 0.196 | 0.000 |
| pmHigh>=+50% ext<10% | partial cover | half at 11:00, rest 15:55 | 839 | 491 | +2.58% [+1.42%, +3.73%] | +2.63% / +3.72% / +3.72% / +3.14% | 4/4 | 65.4 % | -56.74% | -249.73% | +38.56% | 0.0 % | 0.166 | 0.000 |
| pmHigh>=+50% ext<10% | partial cover | half on a -15% move (limit), rest 15:55 | 839 | 491 | +3.29% [+1.93%, +4.64%] | +3.14% / +4.41% / +4.20% / +4.97% | 4/4 | 70.2 % | -69.10% | -402.98% | +38.56% | 0.0 % | 0.180 | 0.000 |
| pmHigh>=+50% ext<10% | cover into weakness | cover on a -10% move (limit), else 15:55 | 839 | 491 | +1.73% [+0.61%, +2.86%] | +1.29% / +2.26% / +2.95% / +2.41% | 4/4 | 78.4 % | -67.71% | -394.52% | +35.17% | 0.0 % | 0.114 | 0.000 |
| pmHigh>=+50% ext<10% | cover into weakness | cover on a -20% move (limit), else 15:55 | 839 | 491 | +3.05% [+1.66%, +4.43%] | +2.57% / +4.12% / +4.49% / +4.77% | 4/4 | 67.3 % | -69.10% | -439.96% | +38.04% | 0.0 % | 0.164 | 0.000 |
| pmHigh>=+50% ext<10% | cover into weakness | cover on a -30% move (limit), else 15:55 | 839 | 491 | +3.35% [+1.87%, +4.82%] | +3.11% / +4.53% / +4.26% / +5.29% | 4/4 | 66.3 % | -69.10% | -442.08% | +38.56% | 0.0 % | 0.170 | 0.000 |
| pmHigh>=+50% ext<10% | cover into weakness | trail 5% over the low once -10%, else 15:55 | 839 | 491 | +1.45% [+0.32%, +2.59%] | +1.10% / +1.84% / +2.61% / +2.09% | 4/4 | 78.2 % | -67.71% | -412.18% | +35.17% | 72.0 % | 0.094 | 0.000 |
| pmHigh>=+50% ext<10% | cover into weakness | trail 10% over the low once -10%, else 15:55 | 839 | 491 | +1.52% [+0.32%, +2.72%] | +1.18% / +1.70% / +1.80% / +2.82% | 4/4 | 71.5 % | -67.71% | -425.50% | +35.17% | 62.9 % | 0.094 | 0.000 |
| pmHigh>=+50% ext<10% | cover into weakness | trail 5% over the low once -20%, else 15:55 | 839 | 491 | +2.84% [+1.46%, +4.22%] | +2.52% / +3.50% / +4.07% / +4.69% | 4/4 | 67.3 % | -69.10% | -443.41% | +38.04% | 33.7 % | 0.153 | 0.000 |
| pmHigh>=+50% ext<10% | cover into weakness | trail 10% over the low once -20%, else 15:55 | 839 | 491 | +2.73% [+1.33%, +4.12%] | +2.24% / +3.22% / +3.98% / +4.55% | 4/4 | 67.3 % | -69.10% | -460.89% | +38.04% | 27.5 % | 0.146 | 0.000 |

Best three per signal (base exit excluded):

| signal | ranked by | exit | mean net [95%] | worst 1% | max DD | folds > 0 |
|---|---|---|---|---|---|---|
| pmHigh>=+100% ext<10% | mean net | cover on a -30% move (limit), else 15:55 | +7.35% [+5.11%, +9.60%] | -66.24% | -223.30% | 4/4 |
| pmHigh>=+100% ext<10% | mean net | cover 15:55, stop 4 x 09:30-09:45 range | +7.33% [+4.68%, +9.98%] | -77.96% | -377.14% | 4/4 |
| pmHigh>=+100% ext<10% | mean net | cover 14:00, no stop | +6.92% [+4.53%, +9.31%] | -66.17% | -201.70% | 4/4 |
| pmHigh>=+100% ext<10% | worst 1% | cover 15:55, stop +20% | +4.20% [+1.79%, +6.60%] | -25.48% | -170.42% | 4/4 |
| pmHigh>=+100% ext<10% | worst 1% | cover 15:55, stop +30% | +5.40% [+2.88%, +7.93%] | -35.48% | -233.78% | 4/4 |
| pmHigh>=+100% ext<10% | worst 1% | cover 15:55, stop 2 x prior 14-day range% | +5.59% [+3.09%, +8.09%] | -43.77% | -208.09% | 4/4 |
| pmHigh>=+50% ext<10% | mean net | cover 14:00, no stop | +3.76% [+2.30%, +5.22%] | -78.95% | -283.16% | 4/4 |
| pmHigh>=+50% ext<10% | mean net | cover on a -30% move (limit), else 15:55 | +3.35% [+1.87%, +4.82%] | -69.10% | -442.08% | 4/4 |
| pmHigh>=+50% ext<10% | mean net | half on a -15% move (limit), rest 15:55 | +3.29% [+1.93%, +4.64%] | -69.10% | -402.98% | 4/4 |
| pmHigh>=+50% ext<10% | worst 1% | cover 15:55, stop +20% | +1.35% [+0.11%, +2.59%] | -25.80% | -251.27% | 4/4 |
| pmHigh>=+50% ext<10% | worst 1% | cover 15:55, stop +30% | +1.89% [+0.49%, +3.28%] | -35.44% | -282.84% | 4/4 |
| pmHigh>=+50% ext<10% | worst 1% | cover 15:55, stop +40% | +2.61% [+1.18%, +4.05%] | -44.02% | -265.44% | 4/4 |

## 3b. Historical shortability proxy (methods-short-risk.md 4.2)

Thresholds set in advance, not fitted. Tier 3 (likely unborrowable): threshold-listed D-1 (`x.regShoListedD1`; the 10-session look-back is approximated by D-1), shares outstanding (latest filing before D) under 10M or unknown, prior close under $1, or a Nasdaq Trader halt in the 5 sessions before D. Tier 1: shares >= 30M and prior close >= $2 with no Tier 3 condition. Tier 2: the rest. The note's listing-age condition is not applied (no listing date in the panel). Seeded locate success 90% / 50% / 10% by tier (the IP-T476 per symbol-day draw), a 2.5 % locate fee on notional per trade, and the $1,000 position rounded down to 100-share lots (cost at the rounded size). One ledger trial per row.

| signal | exit | scope | signals by tier 1/2/3 | traded by tier 1/2/3 | trades | mean net [95% day-clustered] | per fold | worst 1% | mean by tier 1/2/3 |
|---|---|---|---|---|---|---|---|---|---|
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | all tiers | 26/55/243 | 13/21/19 | 53 | +0.87% [-4.42%, +6.16%] | +12.00% / +1.87% / +3.22% / +7.46% | -79.41% | -3.83% / +5.03% / -0.51% |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | tiers 1-2 only (tier 3 gated out) | 26/55/243 | 13/21/0 | 34 | +1.64% [-3.96%, +7.25%] | +11.10% / +1.58% / -2.61% / +11.12% | -42.90% | -3.83% / +5.03% / n/a |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | all tiers | 26/55/243 | 13/21/19 | 53 | -1.93% [-7.70%, +3.85%] | +12.38% / -0.92% / -2.14% / +4.62% | -33.33% | -5.54% / +0.28% / -1.89% |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | tiers 1-2 only (tier 3 gated out) | 26/55/243 | 13/21/0 | 34 | -1.95% [-9.25%, +5.35%] | +11.61% / -4.00% / -3.83% / +8.39% | -28.76% | -5.54% / +0.28% / n/a |
| pmHigh>=+100% ext<10% | cover 15:55, stop +30% | all tiers | 26/55/243 | 13/21/19 | 53 | -1.14% [-6.71%, +4.43%] | +12.00% / -2.58% / +3.22% / +5.20% | -38.76% | -6.72% / +1.26% / +0.03% |
| pmHigh>=+100% ext<10% | cover 15:55, stop +30% | tiers 1-2 only (tier 3 gated out) | 26/55/243 | 13/21/0 | 34 | -1.79% [-8.93%, +5.35%] | +11.10% / -7.33% / -2.61% / +9.01% | -38.76% | -6.72% / +1.26% / n/a |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | all tiers | 93/133/613 | 57/54/51 | 162 | -2.88% [-6.18%, +0.41%] | +1.42% / -4.69% / -3.26% / -1.07% | -100.49% | -10.60% / +4.32% / -1.89% |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | tiers 1-2 only (tier 3 gated out) | 93/133/613 | 57/54/0 | 111 | -3.34% [-7.37%, +0.69%] | +5.51% / -11.61% / -3.64% / -1.68% | -100.49% | -10.60% / +4.32% / n/a |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | all tiers | 93/133/613 | 57/54/51 | 162 | -2.49% [-5.05%, +0.06%] | +4.85% / -1.72% / -4.45% / -1.08% | -28.76% | -6.22% / +0.81% / -1.82% |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | tiers 1-2 only (tier 3 gated out) | 93/133/613 | 57/54/0 | 111 | -2.80% [-5.90%, +0.30%] | +6.44% / -5.36% / -3.61% / -0.56% | -26.75% | -6.22% / +0.81% / n/a |
| pmHigh>=+50% ext<10% | cover 15:55, stop +30% | all tiers | 93/133/613 | 57/54/51 | 162 | -2.74% [-5.44%, -0.04%] | +3.63% / -4.58% / -3.33% / -1.37% | -37.13% | -8.29% / +1.71% / -1.24% |
| pmHigh>=+50% ext<10% | cover 15:55, stop +30% | tiers 1-2 only (tier 3 gated out) | 93/133/613 | 57/54/0 | 111 | -3.43% [-6.77%, -0.08%] | +5.51% / -9.21% / -5.20% / -1.49% | -35.75% | -8.29% / +1.71% / n/a |

Base exit by tier with no locate stress (trades, mean net): pmHigh>=+100% ext<10%: tier 1 26 at +2.10% / tier 2 55 at +7.85% / tier 3 243 at +8.88%; pmHigh>=+50% ext<10%: tier 1 93 at -4.68% / tier 2 133 at +5.58% / tier 3 613 at +4.41%.

## 4. Tail sizing

10,000 bootstrapped years per row: whole trade days drawn with replacement until the year holds the train rate of trades (trades / 3.00 train years); each trade is short `fraction` of the day's opening equity, so equity x (1 + fraction x the day's summed net returns). Annual return = ending equity - 1. DD = max drawdown from the running peak; ruin = equity below 50% at any point. Seed 1337. Exits: the base, the best mean net and the best worst 1% with a lower bound above 0 (non-base).

Tail stress (methods-short-risk.md): the worst 1% of trades (at least one) replaced by -100% or by -150% before the bootstrap.

| signal | exit | tail | trades / year | fraction | median annual return | 5th pct annual return | P(DD > 25%) | P(DD > 50%) | P(ruin) |
|---|---|---|---|---|---|---|---|---|---|
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | as is | 108 | 0.5 % | +4.52% | +2.47% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | as is | 108 | 1.0 % | +9.22% | +4.98% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | as is | 108 | 2.0 % | +19.22% | +10.14% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | as is | 108 | 5.0 % | +54.48% | +26.70% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 4 -> -100% | 108 | 0.5 % | +4.40% | +2.23% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 4 -> -100% | 108 | 1.0 % | +8.97% | +4.50% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 4 -> -100% | 108 | 2.0 % | +18.66% | +9.11% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 4 -> -100% | 108 | 5.0 % | +52.59% | +23.69% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 4 -> -150% | 108 | 0.5 % | +4.06% | +1.53% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 4 -> -150% | 108 | 1.0 % | +8.27% | +3.05% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 4 -> -150% | 108 | 2.0 % | +17.12% | +6.08% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 4 -> -150% | 108 | 5.0 % | +47.57% | +14.89% | 0.15 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | as is | 108 | 0.5 % | +4.08% | +2.16% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | as is | 108 | 1.0 % | +8.30% | +4.36% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | as is | 108 | 2.0 % | +17.24% | +8.84% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | as is | 108 | 5.0 % | +48.23% | +23.08% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | worst 4 -> -100% | 108 | 0.5 % | +3.95% | +1.90% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | worst 4 -> -100% | 108 | 1.0 % | +8.05% | +3.81% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | worst 4 -> -100% | 108 | 2.0 % | +16.68% | +7.67% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | worst 4 -> -100% | 108 | 5.0 % | +46.44% | +19.71% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | worst 4 -> -150% | 108 | 0.5 % | +3.63% | +1.17% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | worst 4 -> -150% | 108 | 1.0 % | +7.37% | +2.33% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | worst 4 -> -150% | 108 | 2.0 % | +15.20% | +4.61% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover on a -30% move (limit), else 15:55 | worst 4 -> -150% | 108 | 5.0 % | +41.64% | +11.08% | 0.24 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | as is | 108 | 0.5 % | +2.28% | +0.39% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | as is | 108 | 1.0 % | +4.60% | +0.78% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | as is | 108 | 2.0 % | +9.36% | +1.51% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | as is | 108 | 5.0 % | +24.60% | +3.47% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | worst 4 -> -100% | 108 | 0.5 % | +1.81% | -0.39% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | worst 4 -> -100% | 108 | 1.0 % | +3.64% | -0.79% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | worst 4 -> -100% | 108 | 2.0 % | +7.34% | -1.66% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | worst 4 -> -100% | 108 | 5.0 % | +18.77% | -4.65% | 0.28 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | worst 4 -> -150% | 108 | 0.5 % | +1.49% | -1.05% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | worst 4 -> -150% | 108 | 1.0 % | +2.99% | -2.11% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | worst 4 -> -150% | 108 | 2.0 % | +5.97% | -4.29% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+100% ext<10% | cover 15:55, stop +20% | worst 4 -> -150% | 108 | 5.0 % | +14.92% | -11.21% | 2.43 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | as is | 280 | 0.5 % | +5.21% | +1.87% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | as is | 280 | 1.0 % | +10.65% | +3.73% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | as is | 280 | 2.0 % | +22.25% | +7.43% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | as is | 280 | 5.0 % | +63.37% | +17.85% | 0.95 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 9 -> -100% | 280 | 0.5 % | +4.96% | +1.55% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 9 -> -100% | 280 | 1.0 % | +10.13% | +3.08% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 9 -> -100% | 280 | 2.0 % | +21.11% | +6.07% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 9 -> -100% | 280 | 5.0 % | +59.63% | +14.16% | 1.48 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 9 -> -150% | 280 | 0.5 % | +4.19% | +0.34% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 9 -> -150% | 280 | 1.0 % | +8.51% | +0.62% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 9 -> -150% | 280 | 2.0 % | +17.52% | +1.00% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, no stop (IP-T476 headline) | worst 9 -> -150% | 280 | 5.0 % | +47.54% | +0.53% | 6.37 % | 0.01 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | as is | 280 | 0.5 % | +5.43% | +2.21% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | as is | 280 | 1.0 % | +11.11% | +4.43% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | as is | 280 | 2.0 % | +23.31% | +8.90% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | as is | 280 | 5.0 % | +67.05% | +22.18% | 0.44 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | worst 9 -> -100% | 280 | 0.5 % | +5.36% | +2.14% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | worst 9 -> -100% | 280 | 1.0 % | +10.97% | +4.28% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | worst 9 -> -100% | 280 | 2.0 % | +22.99% | +8.56% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | worst 9 -> -100% | 280 | 5.0 % | +65.97% | +21.27% | 0.54 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | worst 9 -> -150% | 280 | 0.5 % | +4.61% | +0.93% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | worst 9 -> -150% | 280 | 1.0 % | +9.39% | +1.81% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | worst 9 -> -150% | 280 | 2.0 % | +19.45% | +3.41% | 0.01 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 14:00, no stop | worst 9 -> -150% | 280 | 5.0 % | +53.87% | +6.92% | 4.04 % | 0.01 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | as is | 280 | 0.5 % | +1.89% | -0.66% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | as is | 280 | 1.0 % | +3.78% | -1.34% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | as is | 280 | 2.0 % | +7.61% | -2.75% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | as is | 280 | 5.0 % | +19.30% | -7.35% | 1.10 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | worst 9 -> -100% | 280 | 0.5 % | +0.80% | -2.09% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | worst 9 -> -100% | 280 | 1.0 % | +1.58% | -4.17% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | worst 9 -> -100% | 280 | 2.0 % | +3.06% | -8.30% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | worst 9 -> -100% | 280 | 5.0 % | +6.86% | -20.27% | 10.53 % | 0.01 % | 0.01 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | worst 9 -> -150% | 280 | 0.5 % | +0.07% | -3.24% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | worst 9 -> -150% | 280 | 1.0 % | +0.09% | -6.43% | 0.00 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | worst 9 -> -150% | 280 | 2.0 % | +0.03% | -12.61% | 0.12 % | 0.00 % | 0.00 % |
| pmHigh>=+50% ext<10% | cover 15:55, stop +20% | worst 9 -> -150% | 280 | 5.0 % | -1.11% | -29.68% | 27.78 % | 0.26 % | 0.11 % |

## 5. Squeeze predictors at 09:45

Base exit trades. Squeeze = the worst 10% by MAE (MAE at or above the weighted 90th percentile). AUC = weighted single-feature AUC for the squeeze label (> 0.5: higher values squeeze more), rows with the feature missing dropped; interval = day-block bootstrap 95% (1,000 draws). Nothing is fitted and re-scored on the same rows. A feature stands out when its interval excludes 0.5 and |AUC - 0.5| >= 0.05 (a near-constant flag such as x.haltNewsSinceClose can exclude 0.5 by a hair).

**pmHigh>=+100% ext<10%** (squeeze = MAE >= +38.44%)

| feature | rows | AUC [95% day-block] | median, squeezes | median, rest | stands out |
|---|---|---|---|---|---|
| VolRatioTod (volume vs time of day at 09:45) | 324 | 0.431 [0.332, 0.533] | 173.504 | 238.936 | no |
| LogPmCumVol | 324 | 0.549 [0.458, 0.651] | 17.025 | 16.829 | no |
| x.floatLogShares | 259 | 0.455 [0.351, 0.573] | 16.253 | 16.602 | no |
| x.shortVolShareD1 | 245 | 0.461 [0.360, 0.563] | 0.488 | 0.497 | no |
| x.floatRotation | 259 | 0.537 [0.411, 0.660] | 2.044 | 1.247 | no |
| x.haltMinutesSinceLast | 45 | 0.465 [0.043, 0.907] | 7.817 | 7.383 | no |
| x.haltPausesToday (by 09:45) | 324 | 0.466 [0.423, 0.516] | 0 | 0 | no |
| premarket high gap % (signal) | 324 | 0.471 [0.355, 0.587] | 158.166 | 141.263 | no |
| gap % at 09:45 (signal) | 324 | 0.473 [0.361, 0.584] | 64.095 | 68.649 | no |
| x.shortVolShareZ | 231 | 0.474 [0.364, 0.596] | 0.234 | 0.355 | no |
| x.siDaysToCover | 240 | 0.521 [0.414, 0.636] | 1 | 1 | no |
| 09:30-09:45 range / open | 324 | 0.520 [0.433, 0.611] | 0.207 | 0.199 | no |
| log entry price | 324 | 0.483 [0.399, 0.567] | 0.673 | 0.896 | no |
| x.haltPausesD1 | 324 | 0.489 [0.464, 0.528] | 0 | 0 | no |
| x.siChangePct | 240 | 0.510 [0.357, 0.648] | 20.25 | 5.66 | no |
| x.regShoListedD1 | 324 | 0.491 [0.466, 0.531] | 0 | 0 | no |
| x.haltNewsSinceClose | 324 | 0.491 [0.483, 0.498] | 0 | 0 | no |
| extension from 09:30 open % (signal) | 324 | 0.493 [0.380, 0.604] | -8.451 | -6.634 | no |
| x.ssrActive | 324 | 0.495 [0.423, 0.573] | 0 | 0 | no |
| prior 14-day range % | 324 | 0.496 [0.381, 0.610] | 0.132 | 0.139 | no |
| premarket volume share of 04:00-09:45 | 324 | 0.502 [0.395, 0.604] | 0.67 | 0.666 | no |

**pmHigh>=+50% ext<10%** (squeeze = MAE >= +38.56%)

| feature | rows | AUC [95% day-block] | median, squeezes | median, rest | stands out |
|---|---|---|---|---|---|
| x.floatLogShares | 679 | 0.432 [0.364, 0.499] | 16.593 | 16.971 | **yes** |
| x.floatRotation | 679 | 0.559 [0.491, 0.633] | 1.092 | 0.782 | no |
| VolRatioTod (volume vs time of day at 09:45) | 839 | 0.450 [0.383, 0.517] | 124.541 | 145.242 | no |
| prior 14-day range % | 839 | 0.540 [0.474, 0.608] | 0.131 | 0.13 | no |
| x.siChangePct | 639 | 0.534 [0.450, 0.621] | 18.06 | 3.71 | no |
| x.haltMinutesSinceLast | 68 | 0.467 [0.116, 0.753] | 10.75 | 8.333 | no |
| 09:30-09:45 range / open | 839 | 0.532 [0.468, 0.589] | 0.176 | 0.172 | no |
| x.siDaysToCover | 639 | 0.469 [0.408, 0.530] | 1 | 1 | no |
| gap % at 09:45 (signal) | 839 | 0.472 [0.401, 0.544] | 39.592 | 44.103 | no |
| x.shortVolShareZ | 623 | 0.516 [0.444, 0.593] | 0.463 | 0.349 | no |
| log entry price | 839 | 0.488 [0.432, 0.545] | 0.655 | 0.715 | no |
| x.shortVolShareD1 | 651 | 0.490 [0.422, 0.562] | 0.498 | 0.506 | no |
| x.haltNewsSinceClose | 839 | 0.490 [0.485, 0.495] | 0 | 0 | no |
| premarket high gap % (signal) | 839 | 0.490 [0.425, 0.555] | 85.955 | 87.073 | no |
| x.haltPausesD1 | 839 | 0.507 [0.486, 0.533] | 0 | 0 | no |
| extension from 09:30 open % (signal) | 839 | 0.494 [0.423, 0.563] | -4.789 | -4.147 | no |
| x.ssrActive | 839 | 0.505 [0.459, 0.555] | 0 | 0 | no |
| x.regShoListedD1 | 839 | 0.496 [0.471, 0.527] | 0 | 0 | no |
| premarket volume share of 04:00-09:45 | 839 | 0.503 [0.440, 0.566] | 0.616 | 0.627 | no |
| x.haltPausesToday (by 09:45) | 839 | 0.497 [0.473, 0.525] | 0 | 0 | no |
| LogPmCumVol | 839 | 0.502 [0.432, 0.567] | 16.802 | 16.714 | no |

Exclusion rule, scored by fold: for the top feature (standing out or not, flagged), each fold's fit window sets the squeeze direction (its fit-window AUC) and the quintile edge (weighted 80th, or 20th, percentile); the eval window drops names beyond the edge (missing values kept). One ledger trial per signal.

| signal | feature | stood out | fold | edge | side | eval trades | excluded | squeezes excluded / in eval | eval mean, all | eval mean, kept | worst 1%, all | worst 1%, kept |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| pmHigh>=+100% ext<10% | VolRatioTod (volume vs time of day at 09:45) | no | 1 | 35.983 | bottom | 42 | 1 | 1 / 4 | +6.06% | +6.88% | -66.24% | -66.24% |
| pmHigh>=+100% ext<10% | VolRatioTod (volume vs time of day at 09:45) | no | 2 | 62.155 | bottom | 46 | 11 | 0 / 0 | +12.98% | +13.08% | -26.20% | -26.20% |
| pmHigh>=+100% ext<10% | VolRatioTod (volume vs time of day at 09:45) | no | 3 | 58.85 | bottom | 71 | 3 | 0 / 5 | +10.01% | +9.93% | -36.53% | -36.53% |
| pmHigh>=+100% ext<10% | VolRatioTod (volume vs time of day at 09:45) | no | 4 | 70.833 | bottom | 105 | 23 | 5 / 14 | +8.63% | +10.73% | -62.33% | -44.57% |
| pmHigh>=+100% ext<10% | VolRatioTod (volume vs time of day at 09:45) | no | pooled eval | | | | | | +9.35% [+6.87%, +11.83%] | +10.16% [+7.78%, +12.53%] | | |
| pmHigh>=+50% ext<10% | x.floatLogShares | yes | 1 | 16.061 | bottom | 111 | 19 | 4 / 9 | +3.14% | +3.45% | -63.96% | -63.96% |
| pmHigh>=+50% ext<10% | x.floatLogShares | yes | 2 | 15.982 | bottom | 133 | 23 | 2 / 7 | +5.05% | +4.25% | -59.77% | -59.77% |
| pmHigh>=+50% ext<10% | x.floatLogShares | yes | 3 | 15.975 | bottom | 145 | 46 | 7 / 16 | +4.62% | +4.91% | -57.98% | -69.10% |
| pmHigh>=+50% ext<10% | x.floatLogShares | yes | 4 | 15.756 | bottom | 221 | 46 | 4 / 27 | +5.26% | +4.06% | -62.33% | -76.65% |
| pmHigh>=+50% ext<10% | x.floatLogShares | yes | pooled eval | | | | | | +4.68% [+3.07%, +6.29%] | +4.17% [+2.29%, +6.05%] | | |

Exploration only (train). Historical borrow availability is unknown, so every name is assumed borrowable; the frozen cost model was built for premarket longs. The top feature was picked on all of train, so the fold rule tests the quintile edge out of sample but not the choice of feature.

Trials logged: 62 (ids 1096..1157); ledger total 1157.
