# Gapper radar out of sample (IP-T500 amendment 02)

43 sessions 2026-09-10 .. 2026-11-09; mean liquid names per session 6289; mean candidates 534.

## High >= +50% (base rate 0.20% of liquid names, 12.7 runners per session)

| list | precision@10 | precision@20 | precision@50 | recall@10 | recall@20 | recall@50 |
|---|---|---|---|---|---|---|
| radar | 8.1% | 7.3% | 5.1% | 6.7% | 11.7% | 19.9% |
| B1 top D-1 high | 7.9% | 6.6% | 3.9% | 6.5% | 10.5% | 15.3% |
| B2 top D-1 close | 6.3% | 4.7% | 3.0% | 5.0% | 7.2% | 11.3% |
| B3 top $vol surge | 7.9% | 6.3% | 3.6% | 6.3% | 10.0% | 14.4% |

## High >= +20% (base rate 0.86% of liquid names, 54.3 runners per session)

| list | precision@10 | precision@20 | precision@50 | recall@10 | recall@20 | recall@50 |
|---|---|---|---|---|---|---|
| radar | 20.0% | 19.1% | 14.4% | 4.1% | 7.5% | 14.5% |
| B1 top D-1 high | 21.4% | 18.4% | 13.6% | 4.4% | 7.6% | 13.5% |
| B2 top D-1 close | 20.9% | 16.0% | 11.7% | 4.3% | 6.4% | 11.4% |
| B3 top $vol surge | 17.7% | 15.6% | 10.4% | 3.6% | 6.3% | 10.4% |

## Pass rule

Precision@20 for >= +50%: radar 7.3% vs best baseline (B1 top D-1 high) 6.6%; difference +0.7% [90%: -0.5%, +1.9%] -> **FAIL**.

