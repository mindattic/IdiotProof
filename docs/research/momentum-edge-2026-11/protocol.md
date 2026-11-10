# What worked in Momentum.'s calls, and can we do better? Protocol (IP-T500)

Pre-registered 2026-11-10 before any result below was computed. Fixed numbers live in `params.json`; the
scripts in `tools/momentum/edge/` read nothing else. This file and `params.json` are not edited after the
commit that adds them; changes go in numbered amendments.

## The ask

> use what worked for Momentum and improve upon that with deduction and empirical data
> (goal: research what events create gappers, build tools that flag possible gappers and strategies around them)

## Honest prior (the boring answer)

The claim tracker (IP-T493) already shows his picks are real movers (median pick high +27% over the prior close,
38% top-20 gainers) while his stated plans lose (-0.1% mean, -3.9% median before costs, 59% stopped first). The
gapper study (IP-T494) found detection easy and every long ride negative after costs. The likely answer is:

1. his selection is mostly "yesterday's top gainers plus after-hours news", which a screen reproduces;
2. no entry/exit rule on his picks survives a realistic round-trip cost;
3. if anything survives, it is a selection filter (which of his picks to skip), not a better entry.

Each is a hypothesis below with a kill criterion, so the boring answer gets reported plainly if it holds.

## Data

- His calls: `MomentumClaims` + `MomentumClaimOutcomes` (watchlist-scenario and morning-update-scenario rows with
  a resolved symbol), exported by `tools/momentum/artifact/build-artifact.ps1 -JsonOut`.
- Daily cross-section: `D:/IdiotProofData/momentum/movers/<date>.json`, every Alpaca symbol's prior close, open,
  high, close, volume and gain ranks for each date the tracker covered.
- Minute bars: `D:/IdiotProofData/momentum/bars` (SIP, 04:00-20:00 ET) for his pick-days.

## Splits (by trade date, ET)

| split | dates | use |
|---|---|---|
| fit | before 2026-01-01 (includes 2022-2024 videos once the tracker finishes) | search, lift tables, rule ranking |
| validation | 2026-01-01 .. 2026-06-30 | confirm the pre-chosen few, counted |
| holdout | 2026-07-01 .. 2026-11-10 | one run of the one survivor, ever |

Holdout rows are not read by any exploratory script: the scripts drop them unless run with `--holdout` and the
unlock file `holdout.unlock` exists. The first holdout run is recorded in `runs/holdout-run.json` and a second is
refused.

## Hypotheses

**H1 selection (does his pick beat a screen?).** For each evening watchlist (trade date D), compare his 3 picks
with screen S1 = the top 3 by D-1 close-over-prior-close among names with D-1 close $0.50-$20 and D-1 volume >=
1M shares, and S2 = the top 3 by D-1 high-over-prior-close under the same filter. Outcome on D: high over prior
close and close over prior close, each winsorized at +300%. Paired per-day difference (his mean minus screen
mean), day-clustered bootstrap 90% CI. Also report how often his pick is in the screen's top 3 / 10 / 20.
Kill: if the CI on high-over-prior-close includes 0 on fit, his selection adds nothing a screen does not, and
the radar uses the screen.

**H2 what worked inside his calls (descriptive, fit only).** For each evaluated scenario, target-first rate (of
triggered) and mean plan P&L by: video kind, rank, setup tag, price band, D-1 gainer rank band, gap at the 09:30
open band, trigger distance from prior close band, trigger premarket vs regular hours, gap-through. A level with
n >= 30 triggered and target-first lift >= 1.5x the fit base rate is carried to validation; at most 5 carry, by
lift. Validation pass: target-first rate above the validation base rate with a Wilson 90% interval lower bound
above it, Holm across the carried levels.

**H3 execution (can a different entry/exit on his picks make money?).** One trade per pick-day (symbol resolved,
minute bars present). Entries: E1 his trigger as stated, E2 buy the 09:30 open, E3 5-minute opening-range high
break after 09:35, E4 premarket-high break after 09:30, E5 VWAP reclaim (first close back above VWAP after a close
below it, 09:35-11:00). Exits: take-profit {+5, +10, +20 %, none} x stop {-3, -5, -10 %, close below VWAP} x time
{10:30, 12:00, 15:55}. 5 x 4 x 4 x 3 = 240 trials, all logged. Fills: next minute's open after the signal minute;
take-profit and stop fill at the level (stop at the bar's open if it gaps through). Primary cost: 2.0% round trip;
sensitivity 1.0% and 3.4% (the gapper study's frozen cost). Ranking on fit: mean net per trade with >= 50 trades.
The top 5 carry to validation. Validation pass: mean net > 0 with a day-clustered bootstrap 90% CI lower bound > 0,
Holm across the 5. Kill: none passes.

**H4 combined (deduction).** The best H2 filter applied to the best H3 rule, both chosen on fit, scored once on
validation by the H3 test. Only if H3 or H4 passes validation does a single rule go to the holdout.

## What carries to the product

- Whatever H1 says builds the radar's ranking: his selection features if they beat the screen, else the screen and
  the gapper study's D-1 prerequisites (recent +100% day, D-1 top-10 move, D-1 dollar volume >= 5x, halt in the last
  5 sessions, reverse split 11-90 days ago, calendared split on D or D+1).
- A rule reaches a strategy template only after passing validation, and then only as paper, 1 share.
- Ignore flags come from the gapper study's when-to-ignore list and any H2 level with lift <= 0.67x.

## Not available

After-hours prints for D-1 (his "after hours gapper" picks), quotes and spreads (cost is a flat assumption), float,
and Alpaca tradability on past days.
