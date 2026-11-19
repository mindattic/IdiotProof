# Daily runner journal (IP-T592)

The owner's goal (2026-11-19): keep studying the day's biggest runners until the real cause-and-effect rules
of 20-500% gappers surface: news and filing latency, promotion lists, reverse splits, sympathy moves, anything.
This folder is the running record: one post-mortem per session, and a pattern review every 10 sessions.

Everything here is a **case study (opened window, not a test)** under [amendment 05](../amendment-05.md): it
reads only sessions from 2026-09-10 on, tests no hypothesis and changes no strategy. A pattern found here is
only a candidate. It becomes a rule only through [docs/RESEARCH_PLAYBOOK.md](../../../RESEARCH_PLAYBOOK.md):
registered as an amendment first, tested on train (explore stage, counted in the ledger), confirmed on
validation, and proven in the paper shadow. Never apply a journal pattern to a strategy directly. Catalysts
are named by the [amendment 08 type list](../catalyst-types.md). Long only. All times ET.

## Every session, after 16:00 ET

Run it the same evening: the paper order list it reads holds the latest 500 orders, so a later run can miss
the morning's fills.

1. Note the owner's top-gainers screenshot names, if there is one (they go in as named extras).
2. From the repo root:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\gapper-journal.ps1 -Date 2026-11-19 -Extra "MPU,MDRN,SOWG"
   ```

   It runs, through the Hub (all read-only, nothing is placed or armed):
   - `research gappers casestudy --from D --to D --top 20 --extra D:<names>`: per runner the catalyst (Alpaca
     news D-2..D with publish minute, EDGAR filings with acceptance time), prior profile (shares out, reverse
     splits, dilution filings, prior top-mover days, foreign filer), the 1-minute path (premarket high,
     ignition, high of day, fade) and the R4 alarm check. Its output moves to `data/D.casestudy.json` and
     `data/D.casestudy.md` (the full dossiers).
   - `gapper radar --date D --top 300`: the radar as known at 20:00 ET the evening before, which is what the
     morning arming used. A runner with no rank was not on our radar.
   - `strategy list --deleted --mode paper`: a gapper strategy for the symbol created in the 48 h before
     D 09:30 ET counts as armed.
   - `orders list --status all --mode paper --limit 500`: D's fills per symbol give the entry, exit and
     paper P/L (percent per trade, not dollars: one share each).

   It writes `D.json` (the merged rows, the review's input) and `D.md` (scoreboard and runner table plus empty
   post-mortem sections). An existing `D.md` is kept; `-Force` rewrites it and loses the hand-written part. `-ReuseCaseStudy`
   skips the case study when `data/D.casestudy.json` is already there (it takes several minutes).
   If the Hub is restarting or Alpaca rate-limits (429), each command is retried up to 4 times a minute apart.
3. Write the post-mortem in `D.md`, one bullet per runner:
   - **Catalyst:** type, source, publish time vs the first move (did the move start before the news reached
     the tape: a leak, a promotion, sympathy?). "No catalyst" is a finding: say what else might explain it
     (sector sympathy, a promotion list, a recent reverse split, a halt cycle).
   - **Timing:** ignition and high of day against the catalyst; premarket or regular hours.
   - **Prior profile:** what stood out (tiny float, fresh reverse split, repeat runner, recent offering).
   - **Radar:** ranked (and where) or missed, and which evening sign it had or lacked.
   - **Us:** armed, entered, when against the ignition, the exit and why; the divergences already logged in
     the day's `runs/live-paper-D.md`.
4. Add anything that looks like a repeat to "Patterns to carry to the 10-session review".
5. Commit `D.md`, `D.json` and `data/D.casestudy.*`.

## Every 10 sessions: the pattern review

1. ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\gapper-journal.ps1 -Review
   ```
   aggregates the last 10 `D.json` files into `review-<first>_<last>.generated.md`: per session (runners, on
   radar, R4 hits, armed, entered, paper P/L), by catalyst type (run, catalyst-to-ignition, fade, our result),
   prior-profile and timing shares, and every entry we made against its runner's ignition. Named extras are
   left out of the counts.
2. Write `review-NN.md` (01, 02, ...) by hand from that table and the ten "Patterns" sections:
   - each pattern seen in 3 or more sessions, with the sessions and names it rests on;
   - for each, a testable claim (feature, decision time, label, minimum effect) and the cheapest train-split
     test (`research gappers explore`, ledger counted);
   - whether it needs data the study does not hold yet (a promotion-list feed, sympathy groups, filing
     latency) and the ticket to get it.
3. Register the chosen claims as the next `amendment-NN.md` (pushed before any test runs), then test on train.
   Nothing reaches a strategy before validation and the paper shadow.

## Index

| Session | Entry | Notes |
|---|---|---|
| 2026-11-19 | [2026-11-19.md](2026-11-19.md) | First entry; 20 owner-screenshot names (10:30 ET top gainers) as extras |
