# Momentum. edge study: results (IP-T500)

Run 2026-11-10 on clean trade dates (amendment 01). Runs: `runs/fit-20261110-1101.md`, `runs/validation-20261110-1135.md`,
`runs/holdout-h1-20261110-1136.md`, `runs/narratives-20261110-1139.md`, `radar-out-of-sample.md`.

## Answer

**His picking is real; trading his picks is not.**

- **Selection (H1): confirmed out of sample.** On the untouched holdout (2026-07-01..11-09, 62 watchlists) his three
  evening picks reached a next-session high of +65% on average, against +43% for the top-3 D-1 close gainers and +52%
  for the top-3 D-1 high gainers. That is +22.8 [+11.8, +33.5] and +13.7 [+2.3, +25.6] points, and closes beat by
  similar margins. 38% of his 608 fit + validation picks ran +50% next session, 167x the base rate of liquid names
  (0.23%). Every pick was published the evening before, so this is not premarket tape-reading. On fit alone the
  high difference was not significant, so the pre-registered kill fired there; amendment 03 settled the
  disagreement on the holdout, once.
- **Execution (H3/H4): nothing survives costs.** None of 240 entry/exit rules on his pick-days is net positive at a
  2% round trip on fit. The five best lost again on validation, the best by -1.38% [-2.54, -0.23] per trade. His own
  trigger/target/stop plans average -1.4%.
- **What helped inside his calls (H2): not confirmed.** The stock opening above his trigger (target first 35% vs 28%)
  and a wedge-breakout tag lifted on fit and pointed the same way on validation, but missed after Holm.
- **His reasons (descriptive, fit).** The tag that stands out is "moving after hours": 80% of those picks reached
  +20% (lift 1.33), median high +52%. Theme, uplist and day-2 picks did worse than his average.

## What changed in the product

- `gapper radar` scores a Momentum. pick at lift 167 (7.4 points), the strongest single sign it has. His video
  comes out around 19:00-21:00 ET, so the radar has to be run again after that.
- Detection works: a top-gainer screen is 30-40x the base rate, the radar's top 50 catches 20% of +50% runners,
  and his picks are 167x. Buying them loses money at every tested entry once a flat 2% cost is charged.
- **Strategy, as a paper forward test (amendment 04, IP-T502).** The best rule on his picks (premarket-high break,
  +10% target, out on VWAP loss, flat 15:55) was +0.49% per trade before costs on fit and +0.62% on validation. Its
  fail rests on the 2% cost, which the study assumed because it had no quotes. `idiotproof gapper momentum-arm`, run at
  09:29 ET, saves one 1-share Paper strategy per pick, with the premarket high filled in. Paper fills measure the
  spread part of the cost; the test ends at 60 fills or 2027-03-31. A pass earns only a small live test; a fail kills
  the rule.

## Next (each needs its own pre-registration and a forward window after 2026-11-10)

1. **After-hours sign.** A D-1 16:00-20:00 ET move from SIP extended-hours minute bars, scored as a new radar group.
   His after-hours picks are the lead. Pre-registered in amendment 06 (IP-T501): the radar now reads after-hours
   prints and lists the sign at 0 points; one evaluation after 2027-01-29 (`afterhours_forward.py --final`).
2. **Momentum. pick weight forward check.** The lift of 167 adds to the other groups as if independent; a forward
   window shows whether his picks still sit on top once the other signs are counted.
