# Split-table audit: which runs used `GapperStudySplits`, and what a repair changes (IP-T644)

Part of IP-T633 (the table misses reverse splits). Read-only report: no table, result or frozen file is changed here.

## What this is based on

- The runs' own reports and amendments in this folder (what each one reads from `GapperStudySplits` and how).
- `tools/gapper-study/split_exposure.py` over the frozen train snapshot (the CSV exports in `D:\IdiotProofData\gapper-study\a13\`,
  the local copy of the 2026-11-09 tables). It is a heuristic, not the IP-T642 detector: raw open / prior close >= 3, no recorded split
  within 5 days, the level holds and the volume steps. Local data only; no Azure resource was read.
- The repaired table is not read: IP-T642 (detect from bars) and IP-T643 (Alpaca corporate actions under former symbols) have not
  landed, so every number below is the heuristic's, to be redone against the repaired table.

## How big the hole is (train, 2021-11-01..2024-10-31)

- 2,635 split rows in the table. 1,529 days have a raw open of >= 3x or <= 1/3 of the prior close; 721 have a recorded split within 5 days.
- 504 more are upward jumps with no recorded split. 384 of those hold their level over the next 5 sessions and 318 also show a
  volume step: 318 probable unrecorded reverse splits in 262 symbols (2021: 4, 2022: 96, 2023: 123, 2024: 95), median ratio 9.2x.
- 304 downward jumps with no row are not counted (forward splits and real crashes are not separated by this heuristic).

## Effect on returns (the 318 days removed, prior close $0.10-$20)

| measure | with them | without | dropped |
|---|---|---|---|
| mean close-to-open, all symbol-days | +0.609% | +0.456% | 206 of 2,714,880 |
| mean close-to-close, all symbol-days | +0.446% | +0.293% | same 206 |
| signal days (high >= 1.2x prior close, 40,329), close to next open | +3.909% | +3.227% | 27 |
| same signal days, close to next close | +2.606% | +1.913% | 27 |

27 days out of 40,329 move the mean by about 0.7 points, so a few split days dominate any mean across a split day. Intraday returns are not affected; medians were not measured. (206 of the 318 fall in the price band.) The A13 figure from its own run is larger
because its lists select the sub-$0.20 names that split: S3 top20 +2.77% net with the artifacts, -3.27% net with the 148 guard-limit
moves dropped.

## Which runs used the table

Exposure classes: **A** = a return or label measured across a daily bar boundary (a split day inside the window biases it);
**B** = the table only sets the prior close or the split-window exclusion, so an unrecorded split shows up as a +200..+10,000% "gap"
that the frozen unadjusted-split guard (up > +200%, down < -66.7%, 25-day lookback) drops from the universe, and the loss is a
missing symbol-day, not a wrong return; **C** = a feature (`daysSinceReverseSplit`, `reverseSplit90`, ratio) is built from the table.

| run | uses the table for | class | status |
|---|---|---|---|
| universe, labels, snapshots (`build-universe`, `label-train`, `label-validation`, `snapshot-train`) | split-adjusted prior close and ADV, 25-day exclusion | B | unrecorded splits inside the exclusion window are not excluded; guard catches the large ones. Re-run after repair |
| amendments 01, 03, 04, 06, 07, 09, 10, 11 (detector, alarm, steps, late-runner ride) | prior close, `daysSinceReverseSplit` feature, split-window exclusion; intraday entries and exits on minute bars | B, C | intraday returns unaffected; the day population and the reverse-split feature are. Low expected effect, not measured |
| amendment 08 and `catalyst-types` | the ReverseSplit catalyst label reads news and 8-Ks, not the table | none | not affected |
| `explore split-days` (IP-T490) | events are the recorded splits plus inferred ratio changes (IP-T484 rule) and unexplained > +200% moves; prices put on one basis | A | already infers unrecorded changes; the 735 recorded events are the table, the inferred ones partly cover the gap. Re-run to move inferred ones to recorded |
| `explore catalyst-split`, `catalyst-events`, `catalyst-ml`, `late-runners`, `watchlist` | split factor in multi-day series, split-window buckets, split features | A, C | D-1..D+N returns across a missed split are wrong; the watchlist and late-runner feature `splits.Any(...<= 60)` misses them |
| amendment 13 (`a13-overnight`) | split-adjusted close-to-open across the table | A | H31 verdict already withdrawn as a split artifact; the sensitivity is recorded (-3.27% net). Re-run needed |
| amendment 14 (`a14-cause-census`) and 15 | D-high over prior close, `reverseSplit90` | A, C | 16 of the 20 largest D-high ratios are unrecorded splits; its sensitivity dropped 70 days (all G100) and moved no headline by more than a few tenths |

## What to do once IP-T642 / IP-T643 land

1. Re-run `tools/gapper-study/split_exposure.py` against the repaired table: its "unrecorded" list should fall to near zero (the
   residual is the false-positive rate of the heuristic and of the detector).
2. Re-run, in this order, the class A runs: A13, A14, `split-days`, `late-runners`, `watchlist`, `catalyst-split`. Each is
   train only and cheap; class B/C runs only if a headline sits within the sensitivity above.
3. Any result that quotes a mean return across a day boundary and was not on this list needs the same check.
