# Amendment 01: the tracker scored evening watchlists on the day they react to (IP-T500)

Written 2026-11-10 before any validation or holdout read. It changes the data, not the hypotheses or the numbers in
`params.json`.

## What was wrong

The first fit pass (`runs/fit-20261110-0911.*`) looked too good: buying his picks at the 09:30 open and holding to
the close averaged +7.7% gross, while the gapper study found that buying gappers at the open loses 5.2%. A check of
upload times against trade dates found why.

- Titles like "TOP 3 STOCKS FOR TUESDAY" carry no date. The collector then used the **upload date** as the trade
  date. An evening watchlist posted Monday 19:17 ET was scored on Monday's session, which had already closed.
- In 2025, 92 of 199 evening watchlists (46%) were dated this way. Dated titles had their own errors: typos
  ("FOR FRIDAY 1/29/26" posted Thursday 1/29; "WEDNESDAY 2/10/26" posted 2026-06-09) and lists for market holidays
  (7/3/26, Juneteenth, Labor Day, Memorial Day, MLK and Presidents' Day).
- So part of every 2025 outcome in the tracker, in `docs/research/momentum-claims-report.md` and in the published
  "Momentum Calls vs Reality" page, measured the session he was describing, not the one he was calling. The
  2026 numbers were mostly right (212 of 220 dated watchlists were posted the evening before).

## Fix

- `MomentumTracker.ResolveTradeDate` (IdiotProof.Core) picks the session from the title and the upload time: a
  written date only when it is a session opening after the upload, within a week, on the weekday the title names;
  otherwise the named weekday's next session (the next session when that day is a holiday); otherwise the session in
  progress, or the next one when posted after 16:00 ET. Unit cases cover every pattern above (not run here; tests
  run on request).
- Import re-dated 136 videos and re-queued their transcripts, so their claims are re-extracted against the right
  day's movers and prior close and re-verified.
- The study keeps a call only when its video was public before that session's 09:30 ET open (`known_before_open`
  in `edge.py`). This drops morning updates posted during regular hours and any row without an upload time.

## What it spends

The contaminated fit pass was read. It is search-split data and the protocol allows repeated fit passes, so
nothing is spent. Its numbers are void and are not quoted anywhere as results. Validation and holdout were not read.
