# Gapper radar, out of sample (IP-T500, amendment 02 result)

Run 2026-11-10 on the radar frozen at c006032. Full table: `runs/radar-backtest-20261110-0959.md`; data
`D:/IdiotProofData/radar/backtest-2026-09-10-2026-11-09.json`.

**Verdict: FAIL on the pre-registered rule.** At the top 20, 7.3% of the radar's names reached a high of +50% or more
the next session, against 6.6% for the plain "top D-1 gainers by high" screen. The difference is +0.7 points
[90%: -0.5, +1.9], so the radar is not shown to beat the screen where the rule looked.

| next-session high >= +50% (base rate 0.20%, 12.7 runners per session) | precision@10 | precision@20 | precision@50 | recall@50 |
|---|---|---|---|---|
| radar | 8.1% | 7.3% | 5.1% | 19.9% |
| top D-1 gainers by high | 7.9% | 6.6% | 3.9% | 15.3% |
| top D-1 gainers by close | 6.3% | 4.7% | 3.0% | 11.3% |
| top dollar-volume surge | 7.9% | 6.3% | 3.6% | 14.4% |

What it means:

- **The top of the list is the top-gainers screen.** The strongest radar sign is "top-10 mover today", so its first 10-20
  names largely are yesterday's top gainers. Everyone's scanners already show these.
- **The radar adds most deeper down** (top 50: 5.1% precision and 20% recall against 3.9% and 15%). That comparison was
  not the pre-registered one, so it is descriptive and needs its own forward check before anyone leans on it.
- **Both lists are 30-40x the base rate.** Detection is easy; the gapper study already found no long entry on these
  names that survives costs. The radar is a list of names to watch, not a list of trades.
- At +20% the radar and the top-gainers screen are tied (19.1% vs 18.4% precision@20).

What ships: `gapper radar` stays, labeled with these numbers in its own output. Its value is the reasons and warnings
per name (split timing, catalyst type, halts, squeeze profile, Alpaca tradability), not a better ranking. The window
is now spent for radar tuning; any change to the weights needs a new forward window (sessions after 2026-11-10).
