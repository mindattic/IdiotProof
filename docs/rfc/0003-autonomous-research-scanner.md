---
codex: 1
project: IdiotProof
code: IP
layer: rfc
status: active
updated: 2026-07-26
---

# RFC 0003 — Autonomous market-event research scanner

## Problem
Session directive 2026-07-26 (verbatim intent, condensed): the `/research` tab is useless
because it asks the *user* to supply tickers and paste articles — the whole point was for the
system to go out and compile market-moving news itself: insider sales large enough to be
signal ("when a CEO sells a million shares, that means he knows it's going down"), reverse
stock splits, mergers, earnings surprises, and regulatory decisions (the concrete example
given: the SEC approved Nasdaq's new **$5M Market Value of Listed Securities** continued-listing
standard on 2026-07-22, replacing the old $1-minimum-bid-price-only rule that let failing
micro-caps reverse-split their way past delisting indefinitely — real, current, with broad
ramifications for Nasdaq-listed penny stocks). The tab should be a **results review** of
things with a high probability of moving a price, ranked, not a search engine. Follow-up
directives: the scan must be a **separate console app firing on a scheduled task**, silently,
so the Blazor UI only ever displays what's already been computed by the time the user opens it;
and the tone must read like sober equity research — what happened, which tickers it affects and
why (the mechanism), and when — never clickbait.

Current reality (audit, 2026-07-26): `ResearchService` + `EdgarService` + `AlpacaNewsService` +
`CatalystExtractor` + `ClaimVectorService` + `ClaimCorrelationService` already form a working
extraction/scoring pipeline — but every one of them only ever runs when a human types a ticker
into `Research.razor` and clicks a button. Two more gaps found during the audit, independent of
the "must ask a human" problem: (1) `EdgarService` discarded the EDGAR full-text-search API's
`items` field (8-K item codes) and used the wrong JSON property names (`form_type`/`entity_name`
instead of the real `form`/`display_names`) — Form 4 boilerplate text never actually said
anything about transaction size because nobody had confirmed the schema against a live filing;
(2) `ResearchService.AnalyzeArticleAsync` had no dedup — a scheduled scanner re-pulling the same
tickers hourly would have re-extracted (and re-billed the LLM for) the same articles every pass.

## Design

### D1. Ground truth first
Before writing any parser, the real EDGAR full-text-search response, a real Form 4 XML filing,
an real 8-K item-code payload, and the Federal Register's public documents API were fetched
live and inspected (`curl` with the required SEC User-Agent). This is why `EdgarFiling` now
carries `Items string[]`, `IssuerCik`, and `DocumentFileName` populated from the *actual*
`_source.items` / `_source.ciks` / `_id` fields, and why `EdgarService.GetFilingDocumentAsync`
fetches the real primary document instead of only ever seeing search metadata.

### D2. Ticker universe — `TickerUniverseService` + `TrackedTicker`
Caches Alpaca's tradable NASDAQ/NYSE asset list plus a batched latest-price snapshot in a new
`TrackedTicker` table, refreshed once per 24h. This is the scan's "everything to consider" set
and the (best-effort) market-value screen the regulatory scanner uses. `SharesOutstanding` is
left null in this pass — a documented future enhancement (EDGAR XBRL company-facts lookup), not
required to ship the rest.

### D3. Real filing content, not boilerplate
- `Form4Parser` parses the actual non-derivative transaction table (shares, transaction code,
  price, shares owned after) into a new `InsiderTransaction` table and composes a real sentence
  ("X disposed of N shares (P% of holdings) at $Y — now holds Z shares") instead of "a director
  changed their beneficial ownership."
- `CorporateActionDetector` reads 8-K item codes already present in the search hit (no extra
  fetch needed to classify) and fetches the real document text only for the codes that matter
  (1.01, 2.01, 3.02, 3.03, 5.03 — the split/M&A-adjacent set), falling back to boilerplate only
  when the fetch fails.

### D4. Regulatory/macro events — `RegulatoryScanner`
Polls the Federal Register's public API (`federalregister.gov/api/v1/documents.json`, no key
required) for SEC "Self-Regulatory Organizations" notices (`SR-NASDAQ-*`, `SR-NYSE-*`), asks the
LLM whether a given notice is substantive (most SRO filings are routine fee-schedule tweaks —
non-substantive ones are dropped, not surfaced), and if so extracts the rule's mechanism and
which listing tier/segment it affects. These persist as `ResearchClaim` rows with `IsMacro=true`
and `Ticker=""`; affected tickers live in `AffectedTickersJson` — a real list when the
`TrackedTicker` market-value screen has enough data, otherwise an honest descriptive string
rather than a fabricated ticker list. First real content: the Nasdaq $5M MVLS rule itself.

### D5. Sober tone by construction, not by prompt alone
`CatalystExtractor`'s schema gained a `Mechanism` field (why this affects the price, stated
plainly) alongside the existing `Summary`/`ExpectedTimeline`. The claim's display sentence is
now **deterministically composed** — `"{Summary}. Affects {Ticker} because {Mechanism}.
Expected impact: {ExpectedTimeline}."` — instead of trusting one LLM-authored paragraph. The
system prompt also states the tone constraint explicitly, but the composition is the actual
guarantee; prompt drift can't reintroduce clickbait into a field the code assembles itself.

### D6. Significance ranking — `SignificanceScorer`
Every claim gets a single 0–100 `SignificanceScore` combining LLM magnitude/confidence,
historical outcome strength from the existing `ClaimCorrelationService`, source empirical trust
(`SourceTrustScore`), recency decay, and a small watchlist-membership boost. The Research tab
sorts by this instead of requiring a ticker filter first.

### D7. `IdiotProof.ResearchScanner` — a separate, scheduled, one-shot console app
New project (sibling to `IdiotProof.Monitor`, same project-reference pattern into
`IdiotProof.Blazor` for `AppDbContext`/services). **Not a daemon and not part of Monitor's
real-time trading loop** — it runs one scan pass (watchlist tickers every pass + a rotating
batch of the rest of the tracked universe, regulatory scan on its own slower cadence,
significance scoring last) and exits. `tools/register-research-scan-task.ps1` publishes it and
registers a Windows Scheduled Task to fire it on an interval — a deliberate, by-hand,
admin-elevated step, not something that runs itself.

### D8. Research tab — a results review, not a search box
`Research.razor`'s primary view is now "Today's High-Impact Events": a feed ordered by
`SignificanceScore`, a last-scan banner ("last scanned Xm ago, covered N/M tickers"), and a
"my watchlist only" toggle (from the union of the signed-in user's `WorkspaceTab.Watchlist`
entries). The old "type a ticker, click Fetch" and "paste an article" flows still exist, moved
into a collapsed **Advanced** panel for one-off manual research and forcing an immediate
re-scan of a specific ticker — no longer the primary interaction.

## What NOT to do
- Do **not** make the scanner a Windows Service / long-running daemon — it is a scheduled-task,
  one-shot batch job by explicit directive; that keeps its cadence decoupled from Monitor's
  real-time trading loop and from Blazor's request lifecycle.
- Do **not** trust an LLM-authored paragraph for tone — compose the display sentence from
  structured fields (D5) so sober-analyst tone survives prompt changes.
- Do **not** fabricate an affected-ticker list for a macro/regulatory claim when the underlying
  market-value data is too sparse to support one — say so honestly (D4) instead of guessing.
- Do **not** re-extract (and re-bill the LLM for) an article/filing the scanner has already
  ingested — `ResearchService.AnalyzeArticleAsync` now dedupes by (ticker, source URL) before
  calling the extractor.
- Do **not** register the Scheduled Task automatically as part of any build/test tooling —
  `tools/register-research-scan-task.ps1` is a deliberate, admin-elevated, by-hand step.

## Graduates into
BIBLE new subsystem section (autonomous research scanner), USER_STORIES new epic (Research),
amendment IP-A (next number) recording the tab's shift from search-box to ranked results-review.
