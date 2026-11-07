# Research playbook: how IdiotProof researches a feature before it trades

Written 2026-11-07 (IP-T455) from the gapper study (IP-T442 and its amendments). Use it whenever a
feature rests on a claim about markets ("X predicts Y", "selling at Z beats holding"), not on
plumbing. Every step names the gapper study's version of it so the next study can copy the shape.

The principle: **a result only counts if it could have failed and was not looked at before the rules
were fixed.** Everything below protects that, while leaving plenty of room to search.

## 0. Ticket, question, honest prior

- File the ticket first. Write the question the way the user asked it, then as a measurable claim.
- Write down the most likely boring answer before starting. Gapper study: "premarket volume at 08:00
  mostly re-encodes 'a catalyst was published 10-90 minutes ago'" became a hypothesis (H11) with a
  kill criterion, so the boring answer gets reported cleanly instead of explained away.

## 1. Literature and code survey (before any data)

- Two parallel sweeps: the academic/practitioner literature (peer-reviewed first, vendor statistics
  marked unaudited), and the codebase (what exists, what is missing, where a train/serve skew
  already lurks). Gapper study: ~70 sources; found that intraday microstructure had already been
  falsified here (`docs/research/evidence-sweep-2026-11.md`), that no velocity feature existed, and
  that two volume ratios disagreed between live and backtest.
- Turn each documented effect into a candidate hypothesis with the paper's number as the bar.

## 2. Pre-registration (first commit, before any pull)

Under `docs/research/<study>/`:

- `protocol.md`: question, universe, sample definitions, splits, cost model, labels, decision
  rules, kill criteria, promotion path, what is NOT available (say it).
- `hypotheses.json`: each hypothesis with id, family, primary or secondary, test primitive, minimum
  effect, splits allowed.
- `params.json`: every number the code reads (dates, thresholds, grids, seeds, bootstrap draws).
  The code reads nothing else (`GapperStudyParams.Load`).
- Commit and push. The commit time is the external proof the rules came first. These three files
  are never edited again.

## 3. Splits and the locked holdout

- Three time-ordered splits: **train** (search and fit), **validation** (confirm, limited and
  counted), **holdout** (one run, ever). Gapper study: 3 years / 1 year / 1 year.
- The holdout is locked in code: it refuses to run without an unlock file holding the frozen
  files' hash, records the run, and refuses a second one (`HoldoutLock`). Any hypothesis edited
  after the unlock is flagged post-hoc by commit dates.

## 4. Data: tiered, resumable, permanent

- Pull only what the questions need: cheap data for everyone (daily bars), expensive data for the
  days that matter (minute bars for candidate days, their D-1..D-5 and matched controls).
- Every pull is resumable through coverage marks and survives a crash; record provenance
  (SIP vs IEX share; realtime vs batch timestamps, where a batch stamp is only an upper bound).
- Everything fetched goes into the permanent history database (`IdiotProof_History`, IP-T454) so no
  later study pays for it again.
- Never let a stage silently undo another (gapper study: `build-universe` wipes gapper flags;
  documented and guarded).

## 5. Look-ahead audit (do it twice: at design, and before the first test)

- Every feature at decision time t uses only data with a timestamp at or before t; a unit test
  appends post-t data and asserts nothing changes.
- **Selection look-ahead is the subtle one.** Gapper study: candidate days were defined by the
  day's own outcome, so precision measured on them overstated what a live detector would see.
  Fixed by a seeded uniform sample of non-candidate days with inverse-probability weights
  (amendment 01) and weighted estimators. Report absolute precision beside lift (lift is
  unchanged when uninformative negatives are added in proportion).
- "Already happened" look-ahead: score detection only on cases still forming (a stock already up
  20% at t is not a detection).

## 6. One code path for backtest and live

- Features, labels and exits live in a pure shared library (`IdiotProof.Evidence/Gappers/`), called
  by the study, the backtester and the live recorder. Live recording starts on day one with arming
  disabled, so the paper-shadow period accumulates while research runs.

## 7. Statistics that count the tries

- Families: prediction (bucket lift, BH), event studies (BMP with placebo), timing (descriptive with
  bootstrap/Wilson intervals, never "significant"), strategy grids (SPA plus deflated Sharpe),
  structural (BH). Primaries are Holm-corrected across families.
- Day-clustered bootstraps everywhere (days are not independent). Costs are frozen before any
  result (spread proxy times a calibrated multiplier, square-root impact, halt fills).
- "Worth trading" is a stated bar: net per trade, lower bound above zero, minimum trades and days.

## 8. Amendments: changing your mind honestly

- A new question or a design fix after registration goes in `amendment-NN.json/.md`, committed and
  pushed **before** any of its tests run, saying what had and had not run at that moment.
- Amendments are corrected as their own set (frozen q and Holm values never move), can only make
  promotion harder, and are reported beside what they amend. Gapper study: A01 population sample,
  A02 news ride (the user's thesis that the edge is getting the news second-hand fast), A03 Corobs
  look-alike detection scored separately from every existing score.
- A new, unproven method gets a detection-only amendment first: alone, plugged in, head to head.

## 9. Exploration (train only, every trial counted)

- Exploration is allowed and encouraged, inside train only, enforced in code (the explore stage
  refuses other splits). Rolling-origin folds inside train with an embargo; every configuration
  goes to an append-only ledger (`runs/explore/ledger.jsonl`) whose count deflates the finalists.
- Finalists: same sign in at least 3 of 4 folds, clear the frozen bars, no single month carries
  them, an economic reason, survive deflation. At most five go to an amendment, then validation
  once, then the single holdout run.

## 10. Run it unattended, report it honestly

- Long work runs as chained background stages with logs and DONE/ABORT markers; a crash leaves a
  `.crash.txt`; follow-up chains wait on markers.
- The results document (`docs/research/<study>.md`) has a table per family with n, effect, interval,
  corrected statistic and verdict, including failures and voids, and a "what this says" section in
  plain language. A kill criterion that fires is the headline, not a footnote.

## 11. From result to product

- Promotion path, in order: survives train, replicates on validation, strategy survives SPA and DSR
  on validation, single holdout run nets the bar, 20+ trading days of paper shadow comparing live
  features against backtest features. Only then is a live mode offered, and only the user enables it.
- Thresholds ship as configuration the live code reads (`formation-trigger.json`), null by default
  = never arms.

## Checklist

- [ ] Ticket filed; question and honest prior written
- [ ] Literature and code survey done; candidate hypotheses carry the literature's numbers
- [ ] protocol.md / hypotheses.json / params.json committed and pushed before any data
- [ ] Splits fixed; holdout locked in code
- [ ] Pulls resumable, provenance recorded, data in the permanent history store
- [ ] Look-ahead audit (time and selection) done twice
- [ ] Shared live/backtest code; live recording started with arming off
- [ ] Corrections and cost model frozen
- [ ] Every amendment registered and pushed before its tests
- [ ] Exploration on train only, ledger counted
- [ ] Results reported in full, failures included
- [ ] Promotion only through validation, one holdout run and the paper shadow
