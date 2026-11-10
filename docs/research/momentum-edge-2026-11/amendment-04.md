# Amendment 04: paper forward test of the Momentum. pick strategy (IP-T502)

Written 2026-11-10, before any trade below exists.

## Why a forward test of a rule that failed validation

H3's best rule on fit, a premarket-high break on his picks with a +10% target, an exit on a close below VWAP, and out
at 15:55, made money before costs in both windows: +0.49% gross per trade on fit (242 trades) and +0.62% on
validation (152). It failed because the study charged a flat 2.0% round trip. That number was an assumption: the
study had no quotes (protocol, "Not available"). Whether the rule pays depends on the one input nobody measured.
Paper trading measures part of it at no risk: Alpaca's paper fills cross the quoted spread on market orders.

This is not a claim that the rule works. Paper fills leave out queue position and market impact, so a paper result
is an optimistic bound on cost. A paper fail kills the rule. A paper pass earns a small live test, which would need
its own amendment.

## Rule (frozen, `gapper momentum-arm`)

For each of his picks for session D (evening "TOP 3" and morning update scenarios, video published before the run),
at 09:29 ET or later on D, with the premarket high (04:00-09:29 ET minute-bar high) known:

`Stock.Ticker(S).Session(RTH).RequireEntryWindow("09:30", "15:30").Breakout(pmh).While().IsAboveVwap().EndWhile()
.ExitWhenFalse().Quantity(1).Long().StopLossPercent(10).TakeProfitPercent(10).SellBy("15:55")`, Paper, 1 share.

Differences from the backtested rule, stated up front: the VWAP exit acts on the Monitor's tick, not on a 1-minute
close; the VWAP condition also gates entry (a breakout under VWAP is skipped); a -10% backstop is added because the
validator requires a stop. A pick with no premarket bars is skipped.

## Measurement

- Unit: one filled trade. Realized return = paper exit fill / paper entry fill - 1 (from the trade diary).
- Alongside each, the bar-model return for the same symbol and day (edge.py `simulate`, pm-high-break, tp +10,
  stop vwap, 15:55), so the cost per trade = model minus realized is measured, not assumed.
- Window: sessions 2026-11-11 onward, until 60 filled trades or 2027-03-31, whichever comes first.
- **Pass:** mean realized return > 0 with a day-clustered bootstrap 90% lower bound > 0 (seed and draws from
  params.json). Reported: mean measured cost, fill rate, share of picks skipped.
- One evaluation at the end of the window; interim looks are counts only.
