# Amendment 02: out-of-sample check of the gapper radar (IP-T500)

Written 2026-11-10 before the backtest below was run. The radar's weights are frozen in the commit that adds this file
(`IdiotProof.Evidence/Gappers/GapperRadar.cs`): every lift comes from the gapper study's train split (IP-T484) or the
split-day study (IP-T490), nothing from the window tested here.

## Window

Sessions 2026-09-10 .. 2026-11-09 (42 sessions). This is the window the gapper study's amendment 05 opened; it lies
after that study's locked holdout (2025-11-03 .. 2026-09-09), which is not touched. The radar has never been fitted
on it.

## What is scored

For each session D, the radar ranks its candidates from what was known by 20:00 ET on the basis session D-1
(`GapperRadarService.BacktestAsync`: Alpaca daily bars through D-1, splits recorded with ex-dates up to D+1, Alpaca news
to D-1 20:00, the Nasdaq halt feed; Alpaca tradability and Momentum. picks are left out). Outcome: the session's high
over the prior close from the daily cross-section of liquid names (prior close >= $0.10, >= $1M traded on D).

- Runner levels: high >= +50% (primary) and >= +20%.
- Recall@N: share of the session's runners in the top N. Precision@N: runner share of the top N. N = 10, 20, 50.
  Means over sessions, with a session bootstrap 90% interval (5,000 draws, seed 500).

## Baselines (same candidates, same sessions)

- B1: top N by D-1 high over its prior close (the plain "today's top gainers" screen).
- B2: top N by D-1 close over its prior close.
- B3: top N by D-1 dollar volume against its 20-day mean.

## Pass rule

The radar passes if its precision@20 for >= +50% beats the best of B1-B3 with the 90% interval of the per-session
difference above 0. Recall and the other N are reported, not tested. A fail ships the radar labeled as a watchlist
no better than a top-gainers screen.
