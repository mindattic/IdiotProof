# Explore: run size, run duration and fade by catalyst type (IP-T487, amendment 08)

Train only. Weighted (amendment 01 population). Folds:
- fold 1 (fit 2021-11..2022-10, eval 2022-11..2023-04)
- fold 2 (fit 2021-11..2023-04, eval 2023-05..2023-10)
- fold 3 (fit 2021-11..2023-10, eval 2023-11..2024-04)
- fold 4 (fit 2021-11..2024-04, eval 2024-05..2024-10)

Rows are not weighted in this report: Table A counts every stored item once, Table B is a census of train gapper days. Descriptive only, long side only (amendment 08): a type that looks tradable still has to go through costs, the ledger, a registered amendment and validation.

## A. Per item: what the stock did after each kind of news item or filing

Unit: one row of the `explore catalyst-events` table (251,541 rows; 4,118 on excluded universe days dropped, 247,423 used: 202,535 news, 44,888 filings other than forms 3/4/5), event sessions 2021-11-01..2024-10-31. Type: the stored catalyst type (`CatalystPreClassifier.TypeOf`: headline + version 1 class for news, form + 8-K items for filings; one label by the amendment's priority). Run size = highest high from the first full minute after publication to 16:00 over the last trade before publication, minus 1. Duration = minutes from that first minute to the minute of the high. Fade = the 16:00 close over that high, minus 1. Kept = the 16:00 close over the reference, minus 1, before costs; its mean has a day-clustered 95% interval. The sessions are the ones the study holds minute bars for (gapper candidate days, their neighbours, controls and the population sample), so the levels run higher than the market's: compare the types with each other. Sorted by median run size.

| catalyst type | items | news / filings | symbol-days | median run | p75 run | p90 run | run >= +20% | run >= +50% | median minutes to high | median fade | median kept | mean kept [95%] |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| all items | 247,423 | 202,535 / 44,888 | 132,241 | +3.03% | +7.68% | +15.48% | 6.4 % | 1.1 % | 150 | -3.39% | -0.21% | -0.25% [-0.42%, -0.09%] |
| buyback | 544 | 544 / 0 | 485 | +6.83% | +13.36% | +19.65% | 9.4 % | 0.7 % | 248 | -3.36% | +2.54% | +3.29% [+2.44%, +4.14%] |
| guidance change | 3,960 | 3,960 / 0 | 3,423 | +6.74% | +14.06% | +22.41% | 13.3 % | 0.8 % | 278 | -5.82% | +0.10% | +0.37% [-0.22%, +0.97%] |
| earnings beat | 6,371 | 6,371 / 0 | 5,730 | +6.15% | +13.18% | +21.21% | 11.4 % | 0.6 % | 315 | -4.66% | +0.77% | +1.61% [+1.15%, +2.06%] |
| uplisting | 14 | 14 / 0 | 14 | +5.35% | +7.31% | +13.63% | 7.1 % | 0.0 % | 173 | -2.75% | +0.53% | -1.01% [-5.50%, +3.48%] |
| FDA / clinical | 4,593 | 4,593 / 0 | 3,760 | +4.70% | +11.10% | +23.36% | 12.2 % | 3.1 % | 99 | -5.88% | -0.90% | -1.77% [-2.38%, -1.16%] |
| outside investment / PIPE | 2,962 | 268 / 2,694 | 2,680 | +4.19% | +9.51% | +19.58% | 9.7 % | 2.7 % | 322 | -4.64% | -+0.00% | +0.22% [-0.30%, +0.74%] |
| earnings report (no beat/miss word) | 19,246 | 7,721 / 11,525 | 15,809 | +3.93% | +9.72% | +17.86% | 7.9 % | 0.7 % | 264 | -4.18% | -0.11% | -0.04% [-0.40%, +0.31%] |
| acquisition (acquirer) | 1,317 | 920 / 397 | 1,185 | +3.82% | +9.01% | +17.13% | 7.4 % | 2.1 % | 193 | -3.63% | -+0.00% | +0.14% [-0.47%, +0.75%] |
| listing compliance / deficiency | 1,224 | 271 / 953 | 1,105 | +3.81% | +9.92% | +19.21% | 9.3 % | 1.9 % | 254 | -5.66% | -1.00% | -2.50% [-3.18%, -1.83%] |
| earnings miss | 3,299 | 3,299 / 0 | 3,040 | +3.55% | +9.45% | +17.31% | 7.2 % | 0.5 % | 297 | -6.06% | -2.61% | -2.84% [-3.47%, -2.22%] |
| analyst upgrade | 7,424 | 7,424 / 0 | 6,128 | +3.47% | +7.94% | +13.98% | 4.4 % | 0.5 % | 153 | -2.58% | +0.51% | +1.49% [+1.27%, +1.72%] |
| offering / dilution | 8,649 | 1,631 / 7,018 | 6,633 | +3.33% | +8.02% | +15.58% | 6.7 % | 1.0 % | 289 | -4.91% | -1.03% | -1.69% [-2.03%, -1.35%] |
| contract / partnership | 7,331 | 3,935 / 3,396 | 6,760 | +3.26% | +7.91% | +16.34% | 7.3 % | 1.7 % | 221 | -3.52% | -0.19% | -0.14% [-0.42%, +0.15%] |
| reverse split / ADS ratio change | 1,497 | 119 / 1,378 | 1,466 | +3.06% | +7.72% | +15.16% | 6.9 % | 1.5 % | 330 | -3.58% | -0.28% | -0.93% [-1.53%, -0.34%] |
| no catalyst | 116,910 | 116,910 / 0 | 75,662 | +2.90% | +7.31% | +15.13% | 6.3 % | 1.1 % | 110 | -3.32% | -0.28% | -0.37% [-0.54%, -0.20%] |
| theme pivot | 2,924 | 2,924 / 0 | 2,484 | +2.75% | +6.38% | +13.29% | 4.7 % | 0.8 % | 98 | -3.07% | -0.25% | -0.09% [-0.54%, +0.36%] |
| acquisition (target) | 199 | 199 / 0 | 179 | +2.67% | +5.47% | +12.63% | 5.5 % | 2.0 % | 116 | -2.25% | +0.11% | +2.02% [-0.79%, +4.84%] |
| other company news | 58,221 | 40,694 / 17,527 | 45,906 | +2.49% | +6.16% | +12.63% | 4.6 % | 0.9 % | 175 | -2.71% | -0.13% | -0.11% [-0.27%, +0.06%] |
| merger | 738 | 738 / 0 | 667 | +2.12% | +5.82% | +15.24% | 7.6 % | 1.9 % | 101 | -2.56% | -+0.00% | -0.42% [-1.31%, +0.47%] |
| flag: non-binding LOI / MOU (any type) | 228 | 228 / 0 | 211 | +3.83% | +13.62% | +25.59% | 14.9 % | 2.6 % | 66 | -6.88% | -1.80% | -3.66% [-5.58%, -1.74%] |

## B. Per gapper day: what each kind of gapper did

Unit: one train gapper day of the frozen study (`GapperStudyEvents`, split train; 9,537 days, 2,099 symbols, 2021-11-01..2024-10-31; a census). Catalyst: the highest-priority type among the stored news (headline + version 1 class) and filings (8-K, 8-K/A, S-1, S-3, 424B3/4/5, SC 13D; a filing without a fetched acceptance time counts from 17:30 ET of its filing day) for the symbol published after D-1 16:00 ET and at or before the first +20% touch, so an item that only reports the move is not its driver. Coverage items are ignored when the day has any other item. Run size = the session high (premarket or regular hours, 04:00-16:00) over the split-adjusted prior close, minus 1. Duration = minutes from the first +20% touch to the high of day. Fade = the daily-bar close over the session high, minus 1. Kept = the daily-bar close over the prior close, minus 1. Conditional on the day having become a gapper: this says what each kind of gapper did, not how often a catalyst makes one. "No catalyst" = no item stored in Alpaca / Benzinga news or the EDGAR index in that window. Sorted by median run size.

| catalyst | gapper days | share of days | median run | p75 run | p90 run | run >= +50% | run >= +100% | median minutes, +20% touch to high | median high-of-day ET | median fade | median kept |
|---|---|---|---|---|---|---|---|---|---|---|---|
| all gapper days | 9,537 | 100.0 % | +34.34% | +52.63% | +89.24% | 27.4 % | 7.9 % | 19 | 09:38 | -16.24% | +13.79% |
| reverse split / ADS ratio change | 57 | 0.6 % | +38.91% | +57.59% | +88.60% | 31.6 % | 7.0 % | 49 | 09:47 | -18.11% | +17.17% |
| outside investment / PIPE | 112 | 1.2 % | +38.35% | +65.82% | +104.62% | 36.6 % | 11.6 % | 52 | 09:32 | -16.64% | +15.95% |
| contract / partnership | 213 | 2.2 % | +38.15% | +57.60% | +93.31% | 33.8 % | 8.9 % | 24 | 09:06 | -17.96% | +15.79% |
| FDA / clinical | 290 | 3.0 % | +37.71% | +59.65% | +93.33% | 31.4 % | 8.6 % | 21 | 08:14 | -19.41% | +10.82% |
| acquisition (target) | 8 | 0.1 % | +37.47% | +59.35% | +106.57% | 25.0 % | 25.0 % | 19 | 10:12 | -2.94% | +31.50% |
| listing compliance / deficiency | 36 | 0.4 % | +36.71% | +50.38% | +81.96% | 25.0 % | 5.6 % | 13 | 08:04 | -20.99% | +6.62% |
| acquisition (acquirer) | 57 | 0.6 % | +36.49% | +47.37% | +90.04% | 21.1 % | 8.8 % | 22 | 09:18 | -18.76% | +13.08% |
| uplisting | 1 | 0.0 % | +36.21% | +36.21% | +36.21% | 0.0 % | 0.0 % | 244 | 08:04 | -16.77% | +13.37% |
| no catalyst (no stored item) | 5,631 | 59.0 % | +35.54% | +55.25% | +92.26% | 29.7 % | 8.8 % | 13 | 09:32 | -17.83% | +12.00% |
| merger | 29 | 0.3 % | +35.08% | +64.89% | +104.69% | 37.9 % | 10.3 % | 6 | 09:34 | -18.68% | +14.22% |
| coverage only (no event item) | 1,119 | 11.7 % | +35.04% | +54.62% | +93.65% | 29.8 % | 9.0 % | 27 | 09:48 | -17.22% | +14.00% |
| theme pivot | 49 | 0.5 % | +34.94% | +50.09% | +99.96% | 28.6 % | 10.2 % | 41 | 10:09 | -14.04% | +18.12% |
| offering / dilution | 176 | 1.8 % | +33.80% | +50.76% | +84.65% | 26.1 % | 6.8 % | 24 | 09:39 | -13.38% | +16.52% |
| other company news | 518 | 5.4 % | +33.60% | +53.08% | +91.35% | 28.2 % | 8.9 % | 19 | 09:39 | -15.52% | +14.75% |
| guidance change | 72 | 0.8 % | +33.08% | +46.04% | +66.35% | 19.4 % | 4.2 % | 96 | 09:53 | -9.72% | +19.14% |
| buyback | 15 | 0.2 % | +32.28% | +49.78% | +58.49% | 26.7 % | 0.0 % | 91 | 09:51 | -11.86% | +22.47% |
| earnings report (no beat/miss word) | 240 | 2.5 % | +30.64% | +41.31% | +68.68% | 16.7 % | 3.7 % | 28 | 09:43 | -9.64% | +17.65% |
| earnings miss | 111 | 1.2 % | +29.05% | +39.50% | +53.26% | 10.8 % | 3.6 % | 63 | 10:01 | -6.97% | +20.48% |
| earnings beat | 684 | 7.2 % | +28.80% | +36.94% | +45.80% | 8.5 % | 0.7 % | 56 | 09:57 | -6.48% | +19.94% |
| analyst upgrade | 119 | 1.2 % | +28.30% | +40.70% | +62.32% | 14.3 % | 1.7 % | 25 | 10:07 | -9.44% | +16.76% |
| flag: non-binding LOI / MOU (any type) | 17 | 0.2 % | +37.23% | +52.59% | +106.56% | 35.3 % | 11.8 % | 9 | 09:32 | -25.00% | +11.01% |

Exploration only (train). Nothing after 2024-10-31 is read: the event table refuses a later session, and the gapper days, daily closes, news and filings are read for train dates only.

Trials logged: 41 (ids 5614..5654); ledger total 5654.
