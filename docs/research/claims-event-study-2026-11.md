# Research claims event study — 2026-11-04 (IP-T407)

`idiotproof evidence events --from 2024-02-01 --to 2026-07-26 --windows -1:1,0:1,0:5` (defaults: market model vs SPY,
120-day estimation ending 10 trading days before the window, 1,000-draw placebo bootstrap, seed 1337).
Run in-process against a research LocalDB (`IdiotProof_Research_T407`) holding a read-only copy of the
live claims; the live database was only read.

## Sample
- 10,684 single-ticker claims on 166 tickers (2024-02 .. 2026-07); 9,818 had a full estimation + event window.
- Skipped: 658 possible unadjusted splits (a >+200% or <-66.7% day in the windows — the bar store is
  split-unadjusted, see IP-T410), 204 too little history, 4 other.
- `InsiderTransactions` is empty, so no insider transaction-code groups.

## What moved prices (BMP p, robust to event-induced variance)

| group | window | n | mean CAR | median | BMP p |
|---|---|---|---|---|---|
| Earnings/Bearish | [0,1] | 254 | -3.95% | -3.47% | <0.001 |
| Earnings/Bullish | [0,1] | 367 | +1.50% | +1.06% | 0.002 |
| News/Bearish | [0,1] | 503 | -2.09% | -1.57% | <0.001 |
| News/Bullish | [0,1] | 936 | +0.70% | +0.19% | 0.030 |
| Regulatory/Bearish | [0,1] | 143 | -2.01% | -0.36% | 0.009 |
| Guidance/Bullish | [0,1] | 126 | +2.07% | +1.41% | 0.019 |
| Contract/Bullish | [0,5] | 145 | +2.42% | +1.58% | 0.004 |
| Insider/Bullish | [0,5] | 21 | +11.01% | +3.12% | 0.013 |

- The scanner's sentiment is right for Earnings, News and Regulatory claims; **bearish claims carry more
  signal than bullish ones**.
- Bullish contracts drift for a week (62% positive over [0,5]).
- Bullish insider claims are large but rest on 21 events with a mean far above the median: unproven.
- No reliable effect: Guidance beyond day 1, M&A, Neutral insider filings (n≈4,960). "Neutral" Earnings/News
  claims lean negative (≈-1% over [0,1]), so the Neutral label is not neutral.

## Caveats
- 84 group-window tests: about 4 would pass p<0.05 by chance. Trust the p<0.001 rows; treat 0.01–0.05 as leads.
- `ArticleDate` has no time of day, so day 0 can include price action before the claim existed; [-1,1] is not
  tradable, and even [0,1] may overstate what an entry after the article could capture.
- 166 tickers, mostly not penny stocks; the split guard is a heuristic until IP-T410 lands.
