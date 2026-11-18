# Live paper run — 2026-11-18 premarket (IP-T548)

Account: Paper 25K (PA39UP8N9), $25,000 start. 284 multi-stage strategies (radar top 300 minus 16
exclusions), 1 share each, premarket entry window 04:00-09:00 ET, flatten 09:28 ET. v1 one-shot from
04:00; v2 `Repeat(3)` from ~04:30.

**Account result: −$17.37 (−0.07%).** 27 round trips on 20 symbols; 264 of 284 strategies never entered.

## Trades (diary, entry time ET)

| Entry | Symbol | Tier | Entry | Exit | P&L | Exit reason |
|---|---|---|---|---|---|---|
| 04:03 | AVL | large | 32.96 | ~32.89 | −0.07 | SellBy flatten failed (IP-T553); closed by operator 09:34 |
| 04:04 | SHAZ | large | 23.62 | 22.50 | −1.12 | StopLoss |
| 04:24 | CRIS | micro | 0.66 | 0.60 | −0.06 | TrailingStop |
| 04:34 | ZCSH | large | 25.26 | 24.04 | −1.22 | StopLoss |
| 04:34 | MVLL | classic | 10.21 | 9.65 | −0.56 | StopLoss |
| 04:40 | CBRS | mega | 108.00 | 104.60 | −3.40 | StopLoss |
| 04:49 | DLLL | classic | 11.09 | 10.60 | −0.49 | SellByTime |
| 04:59 | MRVL | mega | 155.98 | 152.86 | −3.12 | StopLoss |
| 05:04 | BRFH | micro | 0.75 | 0.65 | −0.10 | StopLoss (−13% vs 10% stop) |
| 05:07 | SOXL | large | 53.49 | 51.25 | −2.24 | StopLoss |
| 05:24 | GP | penny | 3.05 | 2.72 | −0.33 | StopLoss (−10.8% vs 8%) |
| 06:01 | HIHO | penny | 1.40 | 1.48 | **+0.08** | TrailingStop |
| 06:08 | AZI | micro | 0.99 | 0.89 | −0.10 | StopLoss |
| 06:10 | RYDE | penny | 1.30 | 1.16 | −0.14 | StopLoss (−10.8% vs 8%) |
| 06:13 | VWAV | micro | 0.58 | 0.46 | −0.12 | StopLoss (−21% vs 10%) |
| 06:31 | CRTO | classic | 19.54 | 18.50 | −1.04 | StopLoss |
| 06:42 | AAOZ | large | 41.88 | 40.85 | −1.03 | SellByTime |
| 07:07 | HIHO | penny | 1.38 | 1.40 | **+0.02** | TrailingStop (peak +13.7%) |
| 07:09 | AZI | micro | 0.96 | 0.86 | −0.10 | StopLoss |
| 07:13 | GP | penny | 3.29 | 3.03 | −0.26 | StopLoss |
| 07:19 | PENG | large | 45.78 | 45.00 | −0.78 | SellByTime |
| 07:53 | NCPL | penny | 2.73 | 2.62 | −0.11 | TrailingStop (peak +6.2%) |
| 08:30 | SOXL | large | 51.63 | 50.97 | −0.66 | SellByTime |
| 08:31 | IONX | classic | 7.59 | 7.53 | −0.06 | SellByTime |
| 08:31 | AMDL | large | 47.40 | 46.62 | −0.78 | SellByTime |
| 08:34 | AXTX | classic | 5.73 | 5.66 | −0.07 | SellByTime |
| 08:59 | MPU | penny | 2.34 | 2.43 | **+0.09** | SellByTime |

Not filled (entry canceled / never filled): ALAB 04:04, IONX 04:04, HIHO 08:47.

## Findings

1. **3 winners in 27 (11%), all sub-$3.** Every large/mega name lost; the largest losses (CBRS, MRVL,
   SOXL, ZCSH, SHAZ) were modest gaps that faded. Consistent with the study's IP-T494 / IP-T500 H3
   result: no long entry on radar names survives costs.
2. **Entries are late.** The gap/volume/price gate passes after the premarket move has happened;
   the typical path after entry is a slow fade to the stop or to the 09:28 flatten.
3. **Stops slipped in thin books.** Exit limits were priced off the last trade −0.5%; in wide-spread
   names they rested above the bid and re-priced only after further drops (VWAV −21% on a 10% stop).
   Fixed in IP-T553 (2% extended-hours buffer, widening per unfilled attempt).
4. **Trailing stop too loose for small runners.** HIHO's second trade peaked +13.7% and kept +1.4%;
   NCPL peaked +6.2% and closed −4%. A 10% trail exceeds the size of most premarket runs.
5. **Volume gate held ~18% of strategies at 2/4 conditions all morning** (52-54 of 284), without
   draining as premarket volume built. Candidate for a lower multiplier, but given finding 1, loosening
   it likely buys more of the same fades.
6. **Repeat(3) worked**: HIHO, AZI, GP and SOXL re-entered after exiting.

## Defects found during the run

IP-T549, IP-T550, IP-T551, IP-T552 (fixed, live); IP-T553 (fixed, live 09:40); IP-T554 (open:
`positions close` leaves the strategy's booked quantity; AVL phantom clears at next 09:28).

## Next experiment (proposed, backtest first)

Earlier entry on fresh-catalyst sub-$3 names (the only winners), tighter trail (≈3-5%) once in
profit, and no large/mega tier. Run as a `gapper backtest` / study stage before another live day.
