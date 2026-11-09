# Gapper study, amendment 05: opening the last 60 days for recent-runner case studies

Recorded 2026-11-09 (IP-T485) on the user's explicit decision ("use both, test both"), before any
2026-09-10..2026-11-09 data was examined for this purpose. No earlier file is edited.

## Change

- **Opened:** 2026-09-10 .. 2026-11-09 (the last 60 calendar days to the decision date). These days may
  be examined freely for qualitative case studies of the biggest runners: news, filings, earnings, bar
  paths, ignition and top footprints. 2026-11-02..2026-11-09 lies after the original holdout anyway.
- **Holdout, now:** 2025-11-03 .. 2026-09-09. Still locked, opened once, with the same unlock rules as
  protocol.md; no hypothesis or rule may be computed on it before that single run.
- **Validation and train:** unchanged.

## Why

The user wants the most recent runners studied directly, on the view that the latest market behaviour is
the best guide. Studying them inside the holdout would silently contaminate it; recording the trim keeps
the remaining holdout honest.

## Rules

- Any rule, threshold or feature suggested by the opened window is **not** tested on it. It is tested on
  train (exploration, counted in the ledger), then on validation or the remaining holdout through a
  registered amendment, and in every case proven forward in the 20-day paper shadow.
- Reports from the opened window are labelled "case study (opened window, not a test)".
- A05 tests no hypothesis and changes no frozen verdict.
