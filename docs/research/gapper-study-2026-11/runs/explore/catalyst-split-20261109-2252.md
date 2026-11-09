# Explore: catalyst split (IP-T492): the long rides by catalyst class, and the hard-catalyst open entry

Train only. Weighted (amendment 01 population). Folds:
- fold 1 (fit 2021-11..2022-10, eval 2022-11..2023-04)
- fold 2 (fit 2021-11..2023-04, eval 2023-05..2023-10)
- fold 3 (fit 2021-11..2023-10, eval 2023-11..2024-04)
- fold 4 (fit 2021-11..2024-04, eval 2024-05..2024-10)

## 1. The long rides by catalyst class

Each command was re-run in capture mode with its own simulator and data (no ledger line, no report), and the per-trade outcomes of its 3 best configurations by net at 0.25x spread (>= 30 trades) were kept. A trade's class = the `CatalystTypes` types of the stored news (headline + pre-class) and EDGAR filings (8-K items, S-1/S-3/424B, SC 13D; a filing without a fetched acceptance time counts from 17:30 ET of its filing day) for the symbol published after D-1 16:00 ET and at or before the entry minute (known before entry). A trade can be in several classes; "other item" = an item matching none of them; "no catalyst" = no stored item at all (the news store covers the candidate windows only, so this is "none stored", not "none"). Weighted as each command weighs (the 10% population sample counts 10); mean with a day-clustered 95% interval. Costs: each command's frozen model and its 0.25x-spread variant (the split-day and watchlist rides' 0.25x column; the alarm ride's too).

**alarm-ride, config 1: long R4 + news <= 60 min entry=passive limit -1% (5 min) exit=tp +10% stop -8% time 15 min, sell-by 09:28 opg (09:30 open)** (574 trades)

| class | trades | days | gross [95%] | net frozen [95%] | net 0.25x [95%] | win rate (net 0.25x) |
|---|---|---|---|---|---|---|
| all trades | 574 | 333 | +0.29% [-0.35%, +0.92%] | -1.65% [-2.29%, -1.02%] | -0.99% [-1.62%, -0.35%] | 39.6 % |
| merger / acquisition target | 4 | 4 | -0.11% [-7.69%, +7.47%] | -2.57% [-10.95%, +5.80%] | -1.49% [-9.44%, +6.46%] | 50.0 % |
| PIPE / outside investment | 23 | 22 | +0.88% [-1.44%, +3.19%] | -1.25% [-3.87%, +1.36%] | -0.44% [-2.92%, +2.03%] | 43.5 % |
| earnings beat | 79 | 70 | +0.18% [-0.61%, +0.98%] | -1.03% [-1.90%, -0.16%] | -0.65% [-1.49%, +0.18%] | 35.1 % |
| earnings miss | 17 | 17 | +0.16% [-1.50%, +1.81%] | -1.13% [-2.76%, +0.51%] | -0.54% [-2.21%, +1.13%] | 15.4 % |
| FDA / clinical | 132 | 109 | -1.17% [-2.45%, +0.12%] | -2.95% [-4.22%, -1.68%] | -2.40% [-3.64%, -1.16%] | 29.8 % |
| contract / partnership | 106 | 97 | +0.06% [-1.61%, +1.74%] | -1.95% [-3.61%, -0.28%] | -1.26% [-2.96%, +0.44%] | 34.3 % |
| non-binding LOI / MOU | 6 | 6 | -1.95% [-7.27%, +3.37%] | -5.28% [-12.13%, +1.57%] | -3.51% [-9.20%, +2.19%] | 16.7 % |
| reverse split | 3 | 3 | -3.25% [-7.91%, +1.40%] | -6.74% [-13.34%, -0.14%] | -5.45% [-11.59%, +0.69%] | 0.0 % |
| offering / dilution | 33 | 33 | -0.75% [-4.61%, +3.12%] | -2.68% [-6.18%, +0.83%] | -1.91% [-5.65%, +1.83%] | 40.5 % |
| other item (none of the classes above) | 247 | 194 | +0.97% [+0.14%, +1.81%] | -1.17% [-2.04%, -0.30%] | -0.40% [-1.25%, +0.44%] | 47.2 % |
| no catalyst | 0 | 0 | n/a | n/a | n/a | n/a |

**alarm-ride, config 2: long R4 + news <= 60 min entry=passive limit -1% (5 min) exit=tp +10% stop -8% time 15 min, sell-by 09:31** (574 trades)

| class | trades | days | gross [95%] | net frozen [95%] | net 0.25x [95%] | win rate (net 0.25x) |
|---|---|---|---|---|---|---|
| all trades | 574 | 333 | +0.29% [-0.35%, +0.92%] | -1.65% [-2.29%, -1.02%] | -0.99% [-1.62%, -0.35%] | 39.6 % |
| merger / acquisition target | 4 | 4 | -0.11% [-7.69%, +7.47%] | -2.57% [-10.95%, +5.80%] | -1.49% [-9.44%, +6.46%] | 50.0 % |
| PIPE / outside investment | 23 | 22 | +0.88% [-1.44%, +3.19%] | -1.25% [-3.87%, +1.36%] | -0.44% [-2.92%, +2.03%] | 43.5 % |
| earnings beat | 79 | 70 | +0.18% [-0.61%, +0.98%] | -1.03% [-1.90%, -0.16%] | -0.65% [-1.49%, +0.18%] | 35.1 % |
| earnings miss | 17 | 17 | +0.16% [-1.50%, +1.81%] | -1.13% [-2.76%, +0.51%] | -0.54% [-2.21%, +1.13%] | 15.4 % |
| FDA / clinical | 132 | 109 | -1.17% [-2.45%, +0.12%] | -2.95% [-4.22%, -1.68%] | -2.40% [-3.64%, -1.16%] | 29.8 % |
| contract / partnership | 106 | 97 | +0.06% [-1.61%, +1.74%] | -1.95% [-3.61%, -0.28%] | -1.26% [-2.96%, +0.44%] | 34.3 % |
| non-binding LOI / MOU | 6 | 6 | -1.95% [-7.27%, +3.37%] | -5.28% [-12.13%, +1.57%] | -3.51% [-9.20%, +2.19%] | 16.7 % |
| reverse split | 3 | 3 | -3.25% [-7.91%, +1.40%] | -6.74% [-13.34%, -0.14%] | -5.45% [-11.59%, +0.69%] | 0.0 % |
| offering / dilution | 33 | 33 | -0.75% [-4.61%, +3.12%] | -2.68% [-6.18%, +0.83%] | -1.91% [-5.65%, +1.83%] | 40.5 % |
| other item (none of the classes above) | 247 | 194 | +0.97% [+0.14%, +1.81%] | -1.17% [-2.04%, -0.30%] | -0.40% [-1.25%, +0.44%] | 47.2 % |
| no catalyst | 0 | 0 | n/a | n/a | n/a | n/a |

**alarm-ride, config 3: long R4 + news <= 60 min entry=passive limit -1% (5 min) exit=tp +8% stop -8% time 15 min, sell-by 09:28 opg (09:30 open)** (574 trades)

| class | trades | days | gross [95%] | net frozen [95%] | net 0.25x [95%] | win rate (net 0.25x) |
|---|---|---|---|---|---|---|
| all trades | 574 | 333 | +0.21% [-0.40%, +0.81%] | -1.73% [-2.34%, -1.12%] | -1.06% [-1.67%, -0.46%] | 40.7 % |
| merger / acquisition target | 4 | 4 | -0.61% [-7.36%, +6.13%] | -3.07% [-10.72%, +4.57%] | -1.99% [-9.15%, +5.17%] | 50.0 % |
| PIPE / outside investment | 23 | 22 | +1.14% [-0.86%, +3.13%] | -0.99% [-3.14%, +1.16%] | -0.18% [-2.22%, +1.85%] | 47.8 % |
| earnings beat | 79 | 70 | +0.12% [-0.65%, +0.89%] | -1.10% [-1.95%, -0.25%] | -0.72% [-1.53%, +0.09%] | 35.1 % |
| earnings miss | 17 | 17 | +0.00% [-1.55%, +1.55%] | -1.28% [-2.76%, +0.20%] | -0.70% [-2.27%, +0.87%] | 15.4 % |
| FDA / clinical | 132 | 109 | -1.27% [-2.52%, -0.02%] | -3.05% [-4.30%, -1.81%] | -2.50% [-3.72%, -1.29%] | 31.0 % |
| contract / partnership | 106 | 97 | +0.09% [-1.44%, +1.62%] | -1.92% [-3.44%, -0.41%] | -1.24% [-2.79%, +0.32%] | 36.5 % |
| non-binding LOI / MOU | 6 | 6 | -2.28% [-7.03%, +2.48%] | -5.61% [-11.93%, +0.71%] | -3.84% [-8.97%, +1.28%] | 16.7 % |
| reverse split | 3 | 3 | +2.08% [-3.72%, +7.88%] | -1.41% [-5.56%, +2.75%] | -0.12% [-4.60%, +4.37%] | 33.3 % |
| offering / dilution | 33 | 33 | -1.02% [-4.71%, +2.68%] | -2.95% [-6.27%, +0.38%] | -2.18% [-5.74%, +1.38%] | 40.5 % |
| other item (none of the classes above) | 247 | 194 | +0.87% [+0.06%, +1.68%] | -1.27% [-2.11%, -0.42%] | -0.50% [-1.32%, +0.32%] | 47.8 % |
| no catalyst | 0 | 0 | n/a | n/a | n/a | n/a |

**watchlist, config 1: all names (no watchlist) | k=20 cap=<+100% | limit -1% (5 min) | stop -8%, trail 35% once +10%, half at +20%, velocity death, out 15:55** (6,967 trades)

| class | trades | days | gross [95%] | net frozen [95%] | net 0.25x [95%] | win rate (net 0.25x) |
|---|---|---|---|---|---|---|
| all trades | 6,967 | 503 | -0.37% [-0.60%, -0.14%] | -2.73% [-2.97%, -2.48%] | -2.20% [-2.44%, -1.95%] | 33.2 % |
| merger / acquisition target | 16 | 14 | +1.41% [-3.86%, +6.67%] | -0.84% [-6.21%, +4.53%] | +0.11% [-5.24%, +5.47%] | 50.0 % |
| PIPE / outside investment | 83 | 77 | +1.06% [-0.78%, +2.89%] | -1.49% [-3.50%, +0.51%] | -0.84% [-2.87%, +1.19%] | 36.6 % |
| earnings beat | 351 | 146 | +0.67% [-0.28%, +1.62%] | -0.18% [-1.14%, +0.79%] | -0.04% [-1.00%, +0.92%] | 48.0 % |
| earnings miss | 153 | 92 | +1.61% [-0.12%, +3.35%] | +0.58% [-1.13%, +2.29%] | +0.78% [-0.90%, +2.46%] | 54.0 % |
| FDA / clinical | 116 | 104 | -3.27% [-4.75%, -1.79%] | -5.13% [-6.68%, -3.57%] | -4.65% [-6.18%, -3.13%] | 23.1 % |
| contract / partnership | 176 | 137 | -0.99% [-2.50%, +0.52%] | -2.93% [-4.40%, -1.46%] | -2.45% [-3.90%, -1.00%] | 34.6 % |
| non-binding LOI / MOU | 8 | 8 | -2.59% [-6.75%, +1.56%] | -4.51% [-8.44%, -0.57%] | -4.12% [-8.13%, -0.12%] | 37.5 % |
| reverse split | 11 | 10 | -2.14% [-6.79%, +2.51%] | -5.21% [-10.16%, -0.26%] | -4.09% [-8.84%, +0.66%] | 36.4 % |
| offering / dilution | 110 | 101 | -0.05% [-2.19%, +2.08%] | -2.42% [-4.54%, -0.29%] | -1.84% [-3.97%, +0.29%] | 34.8 % |
| other item (none of the classes above) | 985 | 420 | -0.69% [-1.28%, -0.10%] | -3.14% [-3.80%, -2.48%] | -2.44% [-3.10%, -1.79%] | 33.7 % |
| no catalyst | 5,178 | 503 | -0.36% [-0.62%, -0.10%] | -2.78% [-3.06%, -2.50%] | -2.26% [-2.54%, -1.98%] | 32.3 % |

**watchlist, config 2: all names (no watchlist) | k=20 cap=none | limit -1% (5 min) | stop -8%, trail 35% once +10%, half at +20%, velocity death, out 15:55** (6,976 trades)

| class | trades | days | gross [95%] | net frozen [95%] | net 0.25x [95%] | win rate (net 0.25x) |
|---|---|---|---|---|---|---|
| all trades | 6,976 | 503 | -0.37% [-0.60%, -0.14%] | -2.73% [-2.97%, -2.48%] | -2.20% [-2.44%, -1.95%] | 33.2 % |
| merger / acquisition target | 16 | 14 | +1.41% [-3.86%, +6.67%] | -0.84% [-6.21%, +4.53%] | +0.11% [-5.24%, +5.47%] | 50.0 % |
| PIPE / outside investment | 83 | 77 | +1.06% [-0.78%, +2.89%] | -1.49% [-3.50%, +0.51%] | -0.84% [-2.87%, +1.19%] | 36.6 % |
| earnings beat | 351 | 146 | +0.67% [-0.28%, +1.62%] | -0.18% [-1.14%, +0.79%] | -0.04% [-1.00%, +0.92%] | 48.0 % |
| earnings miss | 153 | 92 | +1.61% [-0.12%, +3.35%] | +0.58% [-1.13%, +2.29%] | +0.78% [-0.90%, +2.46%] | 54.0 % |
| FDA / clinical | 116 | 104 | -3.27% [-4.75%, -1.79%] | -5.13% [-6.68%, -3.57%] | -4.65% [-6.18%, -3.13%] | 23.1 % |
| contract / partnership | 176 | 137 | -0.99% [-2.50%, +0.52%] | -2.93% [-4.40%, -1.46%] | -2.45% [-3.90%, -1.00%] | 34.6 % |
| non-binding LOI / MOU | 8 | 8 | -2.59% [-6.75%, +1.56%] | -4.51% [-8.44%, -0.57%] | -4.12% [-8.13%, -0.12%] | 37.5 % |
| reverse split | 11 | 10 | -2.14% [-6.79%, +2.51%] | -5.21% [-10.16%, -0.26%] | -4.09% [-8.84%, +0.66%] | 36.4 % |
| offering / dilution | 111 | 102 | +0.06% [-2.08%, +2.19%] | -2.31% [-4.44%, -0.18%] | -1.72% [-3.86%, +0.42%] | 35.3 % |
| other item (none of the classes above) | 988 | 421 | -0.70% [-1.29%, -0.12%] | -3.16% [-3.82%, -2.50%] | -2.46% [-3.11%, -1.81%] | 33.6 % |
| no catalyst | 5,183 | 503 | -0.36% [-0.62%, -0.10%] | -2.79% [-3.07%, -2.51%] | -2.26% [-2.54%, -1.98%] | 32.3 % |

**watchlist, config 3: all names (no watchlist) | k=20 cap=none | market (next bar open) | stop -8%, trail 35% once +10%, half at +20%, velocity death, out 15:55** (10,303 trades)

| class | trades | days | gross [95%] | net frozen [95%] | net 0.25x [95%] | win rate (net 0.25x) |
|---|---|---|---|---|---|---|
| all trades | 10,303 | 503 | -0.44% [-0.62%, -0.25%] | -2.63% [-2.83%, -2.43%] | -2.20% [-2.40%, -2.00%] | 31.9 % |
| merger / acquisition target | 20 | 17 | +0.21% [-3.67%, +4.10%] | -1.94% [-5.95%, +2.07%] | -1.04% [-5.01%, +2.92%] | 50.0 % |
| PIPE / outside investment | 113 | 102 | +1.48% [-0.18%, +3.14%] | -1.27% [-3.04%, +0.50%] | -0.67% [-2.46%, +1.12%] | 36.4 % |
| earnings beat | 540 | 177 | +0.96% [+0.18%, +1.73%] | +0.10% [-0.69%, +0.88%] | +0.23% [-0.56%, +1.01%] | 51.7 % |
| earnings miss | 230 | 115 | +1.38% [-0.03%, +2.79%] | +0.39% [-1.00%, +1.79%] | +0.56% [-0.80%, +1.93%] | 54.6 % |
| FDA / clinical | 130 | 118 | -3.37% [-4.70%, -2.03%] | -5.30% [-6.77%, -3.83%] | -4.82% [-6.26%, -3.37%] | 21.6 % |
| contract / partnership | 239 | 178 | -1.33% [-2.41%, -0.25%] | -3.49% [-4.74%, -2.25%] | -3.02% [-4.21%, -1.83%] | 23.3 % |
| non-binding LOI / MOU | 12 | 12 | -0.76% [-6.52%, +4.99%] | -3.20% [-8.31%, +1.91%] | -2.62% [-7.92%, +2.68%] | 25.0 % |
| reverse split | 16 | 15 | +0.85% [-4.03%, +5.73%] | -4.03% [-8.37%, +0.31%] | -2.92% [-7.20%, +1.36%] | 43.8 % |
| offering / dilution | 143 | 123 | -1.74% [-3.04%, -0.44%] | -4.28% [-5.87%, -2.69%] | -3.71% [-5.25%, -2.18%] | 21.9 % |
| other item (none of the classes above) | 1,382 | 460 | -0.53% [-1.01%, -0.04%] | -2.82% [-3.36%, -2.28%] | -2.23% [-2.76%, -1.70%] | 34.8 % |
| no catalyst | 7,796 | 503 | -0.46% [-0.66%, -0.25%] | -2.69% [-2.92%, -2.47%] | -2.27% [-2.50%, -2.05%] | 30.8 % |

**split-days, config 1: calendar D 08:00 (premarket, marketable) | stop -15%, trail 25% once +10%, velocity death, out 15:55 same day** (529 trades)

| class | trades | days | gross [95%] | net frozen [95%] | net 0.25x [95%] | win rate (net 0.25x) |
|---|---|---|---|---|---|---|
| all trades | 529 | 354 | +0.96% [-0.37%, +2.30%] | -2.92% [-4.34%, -1.51%] | -1.89% [-3.29%, -0.50%] | 47.4 % |
| merger / acquisition target | 2 | 2 | +18.32% [-1.00%, +37.65%] | +8.65% [-24.05%, +41.35%] | +9.74% [-22.55%, +42.03%] | 50.0 % |
| PIPE / outside investment | 4 | 4 | -4.94% [-20.12%, +10.23%] | -7.75% [-22.24%, +6.74%] | -6.05% [-21.19%, +9.10%] | 50.0 % |
| earnings beat | 0 | 0 | n/a | n/a | n/a | n/a |
| earnings miss | 0 | 0 | n/a | n/a | n/a | n/a |
| FDA / clinical | 4 | 4 | -0.79% [-14.12%, +12.55%] | -3.93% [-17.57%, +9.71%] | -2.47% [-16.29%, +11.35%] | 50.0 % |
| contract / partnership | 3 | 3 | -3.09% [-20.93%, +14.74%] | -7.29% [-22.19%, +7.62%] | -4.69% [-21.49%, +12.10%] | 66.7 % |
| non-binding LOI / MOU | 0 | 0 | n/a | n/a | n/a | n/a |
| reverse split | 32 | 32 | -2.82% [-7.63%, +1.99%] | -6.90% [-11.89%, -1.92%] | -5.50% [-10.54%, -0.47%] | 31.2 % |
| offering / dilution | 15 | 15 | +6.69% [-1.59%, +14.96%] | +3.11% [-5.47%, +11.68%] | +4.25% [-4.26%, +12.76%] | 66.7 % |
| other item (none of the classes above) | 139 | 118 | +1.30% [-1.52%, +4.12%] | -3.63% [-6.58%, -0.68%] | -2.47% [-5.39%, +0.45%] | 48.2 % |
| no catalyst | 342 | 255 | +0.88% [-0.71%, +2.47%] | -2.57% [-4.28%, -0.87%] | -1.63% [-3.30%, +0.05%] | 47.7 % |

**split-days, config 2: calendar D 08:00 (premarket, marketable) | stop -8%, trail 25% once +10%, velocity death, out 15:55 same day** (529 trades)

| class | trades | days | gross [95%] | net frozen [95%] | net 0.25x [95%] | win rate (net 0.25x) |
|---|---|---|---|---|---|---|
| all trades | 529 | 354 | +0.87% [-0.29%, +2.04%] | -3.01% [-4.27%, -1.75%] | -1.98% [-3.23%, -0.73%] | 41.0 % |
| merger / acquisition target | 2 | 2 | +18.32% [-1.00%, +37.65%] | +8.65% [-24.05%, +41.35%] | +9.74% [-22.55%, +42.03%] | 50.0 % |
| PIPE / outside investment | 4 | 4 | -3.19% [-17.23%, +10.84%] | -6.00% [-19.29%, +7.30%] | -4.30% [-18.20%, +9.61%] | 50.0 % |
| earnings beat | 0 | 0 | n/a | n/a | n/a | n/a |
| earnings miss | 0 | 0 | n/a | n/a | n/a | n/a |
| FDA / clinical | 4 | 4 | +1.14% [-9.56%, +11.84%] | -2.00% [-13.31%, +9.31%] | -0.54% [-12.04%, +10.96%] | 50.0 % |
| contract / partnership | 3 | 3 | -3.09% [-20.93%, +14.74%] | -7.29% [-22.19%, +7.62%] | -4.69% [-21.49%, +12.10%] | 66.7 % |
| non-binding LOI / MOU | 0 | 0 | n/a | n/a | n/a | n/a |
| reverse split | 32 | 32 | -1.45% [-5.51%, +2.61%] | -5.53% [-9.83%, -1.24%] | -4.13% [-8.52%, +0.25%] | 28.1 % |
| offering / dilution | 15 | 15 | +5.08% [-3.28%, +13.45%] | +1.50% [-7.01%, +10.01%] | +2.65% [-5.85%, +11.14%] | 53.3 % |
| other item (none of the classes above) | 139 | 118 | +2.03% [-0.51%, +4.57%] | -2.89% [-5.57%, -0.22%] | -1.74% [-4.38%, +0.91%] | 43.9 % |
| no catalyst | 342 | 255 | +0.36% [-1.01%, +1.74%] | -3.09% [-4.61%, -1.57%] | -2.14% [-3.64%, -0.65%] | 40.4 % |

**split-days, config 3: calendar D 08:00 (premarket, marketable) | stop -15%, trail 25% once +10%, VWAP loss, out 15:55 same day** (529 trades)

| class | trades | days | gross [95%] | net frozen [95%] | net 0.25x [95%] | win rate (net 0.25x) |
|---|---|---|---|---|---|---|
| all trades | 529 | 354 | +0.67% [-0.64%, +1.98%] | -3.21% [-4.60%, -1.82%] | -2.18% [-3.55%, -0.81%] | 45.9 % |
| merger / acquisition target | 2 | 2 | +10.52% [-4.51%, +25.54%] | +0.84% [-27.56%, +29.24%] | +1.93% [-26.06%, +29.92%] | 50.0 % |
| PIPE / outside investment | 4 | 4 | -7.09% [-20.52%, +6.35%] | -9.89% [-23.21%, +3.43%] | -8.19% [-21.70%, +5.32%] | 25.0 % |
| earnings beat | 0 | 0 | n/a | n/a | n/a | n/a |
| earnings miss | 0 | 0 | n/a | n/a | n/a | n/a |
| FDA / clinical | 4 | 4 | -6.74% [-13.59%, +0.11%] | -9.88% [-16.57%, -3.20%] | -8.42% [-15.39%, -1.46%] | 0.0 % |
| contract / partnership | 3 | 3 | -6.94% [-21.09%, +7.21%] | -11.13% [-22.51%, +0.25%] | -8.53% [-21.56%, +4.50%] | 0.0 % |
| non-binding LOI / MOU | 0 | 0 | n/a | n/a | n/a | n/a |
| reverse split | 32 | 32 | -3.64% [-7.88%, +0.60%] | -7.72% [-12.29%, -3.16%] | -6.32% [-10.90%, -1.74%] | 31.2 % |
| offering / dilution | 15 | 15 | +5.86% [-2.53%, +14.26%] | +2.28% [-6.59%, +11.14%] | +3.42% [-5.41%, +12.26%] | 53.3 % |
| other item (none of the classes above) | 139 | 118 | +0.13% [-2.42%, +2.67%] | -4.80% [-7.51%, -2.09%] | -3.64% [-6.32%, -0.96%] | 44.6 % |
| no catalyst | 342 | 255 | +1.15% [-0.48%, +2.77%] | -2.31% [-4.04%, -0.58%] | -1.36% [-3.06%, +0.34%] | 47.7 % |

## 2. Hard-catalyst open entry ("news drifts")

Events: train universe days (prior close $0.10-$20, not excluded) with a stored news item (`CatalystTypes` on headline + pre-class) or filing (8-K item 3.02 or SC 13D = PIPE / investment) of the class published from D-1 16:00 to D 09:25 ET (weekend and holiday items roll to the next session; an item during the session is not counted). 14,820 events (37 without minute bars, 190 without a 09:30-09:34 bar). Buy the open of the first real bar at 09:30-09:34; sell at the open of the first bar from 09:45 / 10:30 / 15:55 (else the last close), at the next session's open, or at the D+2 close (daily bars, raw; dropped when a split falls in between or the session is past train). No stop. Cost: regular hours = Corwin-Schultz over D's 09:30-15:55 minute bars plus $0.0035/share each side; the frozen model (premarket multiplier 9.393 from spread-multiplier.json, impact on the 04:00-to-entry volume, floor 0.5 %) for comparison. The news store holds the candidate windows (gapper days +/- a few sessions), so these are mostly small-cap names with a move around them. Per fold = the four eval windows (net, regular hours).

| class | exit | trades | days | gross [95%] | net RTH [95%] | net frozen [95%] | per fold (net RTH) | median gross | win rate (net RTH) |
|---|---|---|---|---|---|---|---|---|---|
| merger / acquisition target | 09:45 | 983 | 322 | -0.33% [-0.72%, +0.06%] | -0.83% [-1.22%, -0.44%] | -3.22% [-3.67%, -2.78%] | -0.95% / -0.49% / -0.70% / -1.51% | -0.15% | 39.6 % |
| merger / acquisition target | 10:30 | 983 | 322 | -0.16% [-0.82%, +0.51%] | -0.65% [-1.32%, +0.01%] | -3.04% [-3.69%, -2.39%] | -0.29% / -0.32% / -1.76% / -1.85% | -0.21% | 41.2 % |
| merger / acquisition target | 15:55 | 983 | 322 | +0.02% [-0.86%, +0.89%] | -0.48% [-1.36%, +0.40%] | -2.87% [-3.72%, -2.02%] | -1.09% / -0.22% / -1.90% / -2.54% | -0.20% | 43.8 % |
| merger / acquisition target | next open | 978 | 320 | +0.10% [-1.51%, +1.72%] | -0.39% [-2.02%, +1.23%] | -2.79% [-4.36%, -1.21%] | -1.60% / -0.25% / -0.96% / -3.42% | -0.23% | 43.6 % |
| merger / acquisition target | D+2 close | 975 | 319 | -0.51% [-1.99%, +0.98%] | -1.00% [-2.50%, +0.49%] | -3.39% [-4.81%, -1.98%] | -5.27% / +0.19% / -2.99% / -3.21% | -0.10% | 45.7 % |
| PIPE / outside investment | 09:45 | 3,061 | 734 | -0.17% [-0.33%, +0.00%] | -0.77% [-0.95%, -0.60%] | -4.79% [-5.12%, -4.46%] | -0.95% / -1.08% / -0.67% / -0.82% | -0.11% | 39.9 % |
| PIPE / outside investment | 10:30 | 3,061 | 734 | -0.40% [-0.64%, -0.16%] | -1.00% [-1.25%, -0.76%] | -5.02% [-5.40%, -4.64%] | -1.10% / -1.26% / -0.78% / -1.36% | -0.30% | 40.3 % |
| PIPE / outside investment | 15:55 | 3,061 | 734 | -0.39% [-0.75%, -0.04%] | -1.00% [-1.35%, -0.64%] | -5.01% [-5.47%, -4.55%] | -1.85% / -1.42% / -0.22% / -1.47% | -0.44% | 41.5 % |
| PIPE / outside investment | next open | 3,058 | 733 | +2.64% [-3.30%, +8.59%] | +2.04% [-3.90%, +7.98%] | -1.98% [-7.94%, +3.99%] | -2.11% / -1.19% / -0.11% / +14.01% | -0.30% | 43.0 % |
| PIPE / outside investment | D+2 close | 3,054 | 732 | +0.66% [-3.38%, +4.71%] | +0.06% [-3.98%, +4.10%] | -3.96% [-8.03%, +0.11%] | -3.68% / -2.34% / -0.52% / +7.83% | -1.41% | 39.5 % |
| earnings beat | 09:45 | 6,661 | 641 | +0.03% [-0.14%, +0.20%] | -0.21% [-0.38%, -0.04%] | -2.06% [-2.26%, -1.85%] | -0.54% / -0.14% / +0.01% / -0.19% | -0.05% | 46.9 % |
| earnings beat | 10:30 | 6,661 | 641 | +0.25% [-+0.00%, +0.50%] | +0.01% [-0.24%, +0.26%] | -1.84% [-2.11%, -1.57%] | +0.07% / +0.10% / -0.21% / -0.27% | +0.00% | 48.8 % |
| earnings beat | 15:55 | 6,661 | 641 | +0.30% [-0.06%, +0.66%] | +0.06% [-0.29%, +0.42%] | -1.79% [-2.16%, -1.41%] | +0.12% / +0.20% / -0.14% / -0.54% | +0.00% | 48.9 % |
| earnings beat | next open | 6,608 | 640 | +0.55% [+0.15%, +0.96%] | +0.32% [-0.09%, +0.72%] | -1.54% [-1.95%, -1.12%] | +0.33% / +0.57% / +0.39% / -0.29% | +0.35% | 50.9 % |
| earnings beat | D+2 close | 6,592 | 639 | +1.03% [+0.43%, +1.63%] | +0.79% [+0.20%, +1.39%] | -1.06% [-1.66%, -0.46%] | +1.63% / +0.32% / +0.79% / +0.13% | +0.11% | 49.7 % |
| FDA / clinical | 09:45 | 4,186 | 726 | -0.43% [-0.61%, -0.24%] | -0.91% [-1.10%, -0.72%] | -3.71% [-3.95%, -3.48%] | -0.95% / -1.24% / -1.03% / -1.31% | -0.28% | 41.3 % |
| FDA / clinical | 10:30 | 4,186 | 726 | -0.65% [-0.92%, -0.38%] | -1.14% [-1.41%, -0.86%] | -3.94% [-4.24%, -3.63%] | -1.31% / -1.54% / -1.32% / -1.38% | -0.63% | 40.8 % |
| FDA / clinical | 15:55 | 4,186 | 726 | -0.42% [-0.83%, -0.01%] | -0.91% [-1.32%, -0.49%] | -3.71% [-4.14%, -3.28%] | -1.30% / -0.85% / -0.61% / -1.68% | -0.63% | 43.5 % |
| FDA / clinical | next open | 4,176 | 725 | -0.58% [-1.03%, -0.13%] | -1.06% [-1.52%, -0.61%] | -3.87% [-4.34%, -3.40%] | -1.42% / -0.83% / -0.93% / -1.82% | -0.58% | 44.1 % |
| FDA / clinical | D+2 close | 4,170 | 724 | -0.32% [-1.15%, +0.51%] | -0.80% [-1.64%, +0.03%] | -3.61% [-4.46%, -2.76%] | -1.21% / -1.73% / +1.22% / -1.38% | -1.28% | 43.8 % |

Example events (first 15 by date with an outcome at 15:55):

| symbol | day | classes | prior close | first item (ET) | to 15:55 | to D+2 close |
|---|---|---|---|---|---|---|
| ABVC | 2021-11-01 | FDA / clinical | 2.54 | 11-01 05:17: Shares of ABVC Biopharma Soar 400% Premarket; Co Filed Mixed Shelf on Friday After the Mar... | -26.30% | -50.06% |
| ACXP | 2021-11-01 | FDA / clinical | 5.00 | 11-01 08:03: Acurx Pharma to Present New Data from its Phase 2a Trial of Ibezapostat on November 5th | +9.18% | +9.58% |
| ALDX | 2021-11-01 | FDA / clinical | 9.10 | 11-01 09:24: Aldeyra Therapeutics Sponsored Study Titled 'An Exploratory Clinical Trial to Assess Safet... | +1.31% | +5.13% |
| BTTX | 2021-11-01 | merger / acquisition target | 9.30 | 11-01 05:20: Shares of Better Therapeutics Up 68% Premarket; Completed Merger with Mountain Crest Acqui... | +30.16% | -11.62% |
| CALA | 2021-11-01 | FDA / clinical | 1.93 | 11-01 07:04: Calithera Highlights Presentation Of Interim Data From Phase 1b Trial Pf Arginase Inhibito... | +1.62% | -2.54% |
| CHRS | 2021-11-01 | FDA / clinical | 16.73 | 11-01 07:31: Junshi Biosciences And Coherus Announce FDA Acceptance Of BLA Filing For Toripalimab For T... | -0.24% | +4.18% |
| CRNT | 2021-11-01 | earnings beat | 3.51 | 11-01 07:11: Ceragon Networks Q3 Adj. EPS $0.02 Down From $0.03 YoY, Sales $76.10M Beat $73.30M Estimat... | +0.31% | +0.62% |
| CTMX | 2021-11-01 | FDA / clinical | 5.93 | 11-01 08:02: CytomX Therapeutics to Present Preclinical Data for Conditionally Activated Cytokine Platf... | -4.46% | +2.69% |
| ENBL | 2021-11-01 | earnings beat | 8.14 | 11-01 08:08: Enable Midstream Partners Q3 EPS $0.24 Beats $0.21 Estimate, Sales $956.00M Beat $764.94M ... | +2.48% | +0.67% |
| HUDI | 2021-11-01 | PIPE / outside investment | 17.29 | 11-01 08:06: Huadi International Group Co., Ltd. Awarded $3.26M Stainless Steel Seamless Pipe Order Con... | +19.99% | +47.81% |
| IGC | 2021-11-01 | FDA / clinical | 1.40 | 11-01 08:51: IGC Posts Q2 Results, Reveals Promising Results From Clinical Trial Of THC-Drug For Alzhei... | +0.71% | +4.29% |
| KNSA | 2021-11-01 | earnings beat | 10.39 | 11-01 08:05: Kiniksa Pharmaceuticals Q3 EPS $(0.44) Beats $(0.65) Estimate, Sales $12.10M Beat $10.18M ... | +15.56% | +37.03% |
| KVSB | 2021-11-01 | merger / acquisition target | 10.23 | 11-01 08:18: Khosla Ventures Acquisition Co. II Expected To Close Business Combination With Nextdoor, I... | -1.07% | +2.48% |
| LDI | 2021-11-01 | earnings beat | 6.56 | 11-01 07:32: loanDepot Q3 EPS $0.46 Beats $0.39 Estimate, Sales $923.75M Beat $881.66M Estimate | +12.50% | -5.14% |
| PCG | 2021-11-01 | earnings beat | 11.60 | 11-01 08:13: PG&E Q3 Adj. EPS $0.24 Misses $0.26 Estimate, Sales $5.46B Beat $5.37B Estimate | -1.50% | +0.17% |

Exploration only (train). Nothing after 2024-10-31 is read: news to the last train day's 09:25, daily bars to its close, and a hold that would end after it is dropped.

Trials logged: 49 (ids 5520..5568); ledger total 5568.
