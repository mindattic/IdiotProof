# Gapper study, amendment 02: riding the news second-hand

Registered 2026-11-07 02:15 ET (IP-T450), before any hypothesis was tested on study data. The
machine-readable form is `amendment-02.json`; where the two differ, the JSON governs. No earlier
file is edited.

## The question

Gappers appear every day, and the frozen study asks whether premarket volume and velocity can
recognize one while it is forming (H1, H9, H11, H12). H11 also allows that the real edge is simply
being early to the news: a detector built from price and volume may only re-encode "a catalyst was
published 10 to 90 minutes ago". The frozen protocol says that if so, strategy work stops and the
product is a catalyst-latency pipeline. It never tested whether that pipeline makes money.

This amendment asks the practical form of the question: a trader who cannot see the news first but
reads it a minute or five after publication, buys, and sells before or into the opening bell. Does
that net money after costs, and how fast does the edge decay with delay? It also asks the question
behind "someone knows which gappers will gap": if informed traders act ahead of the headline, price
should move before publication.

## Events

The first realtime-stamped Alpaca news item tagged with the symbol, published between the prior
close (16:00 ET) and 09:20 ET on the day. Overnight items are traded from 04:00. Entry is the open of
the first traded bar at or after publication plus the latency. Batch-stamped items (whose time is an
upper bound) and items without a known stamp kind are excluded. The population and weights are
amendment 01's, so a rule is judged on every day it would have fired, not only on days that ran.

## Hypotheses

- **H18 (primary, strategy grid).** Buy the first news item second-hand, exit on the frozen ladder
  (stop 10%, VWAP loss, velocity death, sell-by). 24 configurations: latency 1 or 5 minutes; any news
  or bullish pre-class only; no confirmation or "already up 5% at the trigger"; sell by 09:15, 09:28
  or 09:45. Worth trading under the same bar as H12: >= +1.0%/trade net, lower bound > 0, >= 150
  trades on >= 40 days, SPA and DSR on validation.
- **H19 (primary, descriptive).** On Tier20 gapper days with news between 04:30 and 09:15, the median
  share of the hour's move that comes before publication is at most 0.25. If the interval sits above
  0.25, price leads the news, and the write-up says so.
- **H20 (secondary, descriptive).** On bullish news, entering 1 minute after publication beats
  entering 15 minutes after by at least one percentage point to a 09:28 exit; the full decay curve
  over 1, 2, 5, 10 and 15 minutes is reported.

## Decision rules

A02 runs are corrected as their own set. H18 is not stopped by the H11 kill criterion, because it is
how a catalyst-latency edge would be traded. A news-ride rule reaches paper trading only through the
same gate as everything else: validation, a single holdout run netting >= +0.5%/trade with a lower
bound above zero, then 20 days of paper shadow.
