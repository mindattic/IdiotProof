---
codex: 1
project: IdiotProof
code: IP
layer: amendments
status: living
updated: 2026-09-05
---

# IdiotProof — Amendments (append-only; amendment wins over the bible)

## IP-A34 — Options hardening: real Alpaca field names, level-aware lock, jargon glossary, Cypress {#IP-A34}
**What changed.** (2026-09-05, follow-up to [IP-A33](#IP-A33) / RFC 0004.) A bug-and-clarity pass
over the manual Options section, prompted by "look for ways to improve them, fix bugs, clearer
instructions, tooltips to explain the jargon, Cypress tests".

1. **Alpaca's options account fields are plural.** `/v2/account` carries `options_trading_level`,
   `options_approved_level` and `options_buying_power`. `AlpacaBrokerClient` read the singular
   `option_trading_level`, a key Alpaca never sends, so **every** real account reported level 0 and
   the ticket locked itself. `GetOptionsAccountAsync` now returns an `OptionsAccountInfo`
   (effective level, approved level, options buying power); the singular spelling survives only as
   a fixture fallback. Both accounts re-checked the same day: `options_trading_level = 3` on paper
   and live (the IP-A33 "not approved" note was an artefact of the wrong key).
2. **Level semantics are a rule, in one place.** `IdiotProof.Shared/Options/OptionsTradingLevel`:
   0 = disabled; 1 = covered calls / cash-secured puts only (**no** buying calls or puts outright);
   2 = long calls/puts; 3 = spreads. The ticket locks per *action* (`Blocker(level, positionIntent)`)
   with a plain-English reason before Alpaca can 422; the level banner shows the four-row table
   and flags an approved-vs-effective cap mismatch.
3. **Jargon has one source of truth.** `IdiotProof.UI/Components/Options/OptionsGlossary` (RCL;
   cannot depend on the host's `DslGlossary`) holds every term's title, hover hint and full
   explanation; the `<Jargon>` component renders a dotted word (hover = hint, click = fixed-position
   card, Escape/click-away closes, one at a time, no JS interop). Every `title="…"` in the chain,
   ticket and tracker pulls from it. Wording mirrors [BIBLE §9](BIBLE.md#IP-§9).
4. **Plain words, not codes.** Alpaca `position_intent` values never reach the screen — the ticket
   and confirm modal say "Opens a new position" / "Closes 2 you already hold" / "Opens a SHORT —
   you'd be writing the option". Put breakevens read "needs to fall X%". A three-step how-to
   (`<details>`, open until a chain or position exists) heads the page. Live confirm is red and
   says REAL MONEY; a buying-power warning appears when the ticket costs more than
   `options_buying_power`; off-tick limits ≥ $3 get a 5¢-step hint.
5. **Sell-the-hype nudge needs history.** `SellSignalEvaluator.MinObservations = 3`: with fewer
   earlier samples the current value is trivially "the high" and the nudge fired on the very first
   refresh. The tracker shows "Watching the hype level — n/3 samples" until then.
6. **Smaller fixes.** Put-side ITM shading no longer paints every put when the underlying price is
   unknown; the Market ticket shows the live mid it actually prices with (not the limit captured at
   selection); the Close prefill keys on `(OccSymbol, InitialSide)` instead of relying on an
   intermediate null render; the order toast pins the broker label before the network call and the
   mode buttons are disabled while placing (a Live fill can no longer be announced as "Paper");
   `SandboxBrokerClient.Book` blends the basis when adding to a short and starts a fresh basis when
   a fill flips the position through zero.
7. **Tests.** New `IdiotProof.UI.Tests` (NUnit) for `OptionsPresenter`, `OptionPositionView`,
   `OptionsGlossary`; Brokers/Engine tests for the plural fields, level rules, Sandbox basis and the
   minimum-sample guard; new Cypress spec `tests/IdiotProof.Cypress/cypress/e2e/08_options.cy.ts`
   (Sandbox-only, deterministic `IPTEST` ticker) covering empty state → chain → ticket → confirm →
   position → close, the Paper/Live fallback, the writing warning and the jargon cards.

**Why.** The section shipped as "built, not proven" (U6/U8 🟡) with a hidden defect that would
have kept the ticket locked on the day Alpaca approved the account for Level 3 — which happened
during this pass. The rest is the promise in RFC 0004's problem statement: options presented so
nobody has to infer anything.

**Effect on canon.**
- Same interpretation of [IP-LAW-1](BIBLE.md#IP-LAW-1) / [IP-LAW-3](BIBLE.md#IP-LAW-3) as IP-A33;
  still manual, still Sandbox-default, still single-leg. **New rule:** an Alpaca options order is
  offered only when `OptionsTradingLevel` permits its `position_intent` (level 1 cannot buy to open).
- [USER_STORIES](USER_STORIES.md#Epic-U): U6 → ✅ (`08_options.cy.ts` + `OptionsPresenterTests`);
  U8 lock text proven by `OptionsTradingLevelTests` (Live path still not Cypress-provable — 🟡);
  U10 unblocked (level 3 on both accounts) but still ⬜ until a deliberate small paper order; new
  U11 (jargon) and U12 (level-aware lock).
- [BIBLE §4.1](BIBLE.md#IP-§4) project table gains `IdiotProof.UI.Tests`; RFC 0004's
  "no Cypress spec" follow-up is closed.

## IP-A33 — Manual Options section (buy the idea, sell the hype) + IndexEvent claims {#IP-A33}
**What changed.** (2026-09-05, RFC 0004.) IdiotProof gains a manual **Options** section — a new
top-level nav tab (`/options`), deliberately separate from the Stock Strategy pages — for
single-leg calls and puts on the user's Alpaca account, presented so nobody has to infer premium
cost, breakeven, or the **intrinsic ("real") vs extrinsic ("hype")** split in their head.

1. **Models/brokers are options-aware, additively.** `AssetClass`, `OptionRight`,
   `OptionContract` (OCC parse/build), `OptionQuote`/`OptionGreeks`; `OrderRequest`/`Position`
   carry `AssetClass` + `Option?` (equity defaults, nothing else changed). `IBrokerClient` gets
   default-implemented options members; `AlpacaBrokerClient` speaks `/v2/options/contracts`,
   the data-host `/v1beta1/options/snapshots`, options orders on `/v2/orders` (OCC symbol, whole
   contracts, DAY, no extended hours), `us_option` positions, and `option_trading_level`.
   `SandboxBrokerClient` serves a synthetic chain so the page works with zero entitlement.
2. **Pure pricing math in `IdiotProof.Shared/Options`.** Intrinsic/extrinsic/breakeven/DTE, a
   Black-Scholes theoretical value + implied-vol solver (cross-check and fallback for Alpaca's
   server-side Greeks; European-exercise/no-dividend simplifications documented), and the
   informational `SellSignalEvaluator` (extrinsic near its high + recent bullish research → "consider taking profit").
3. **`IdiotProof.UI` is occupied.** The RCL from [IP-A28](#IP-A28) had no host reference; it now
   holds `OptionsChainView`, `OptionOrderTicket`, `OptionPositionTracker`,
   `OptionsLiveElevationModal`, and `IdiotProof.Blazor` references it. The host page
   `Options.razor` + `OptionsTradingService` own data access, confirmation, and the Live gate.
4. **`ClaimType = "IndexEvent"`.** `IndexEventScanner` turns the hand-maintained
   `wwwroot/data/sp-index-events.json` (announced S&P 500/100 adds/removes) into research
   claims — Pending until the effective date, then Realized. Runs each ResearchScanner pass.

**Why.** Session directive 2026-09-05: the BE-December-calls example — bought in August, sold
weeks later for +30% at peak hype, never near the $248 breakeven. "It's about cashing in when
the HYPE is highest… don't wait for REALITY to come crashing down." Plus the S&P joiners as
pre-announced mechanical catalysts.

**Effect on canon.**
- [IP-LAW-1](BIBLE.md#IP-LAW-1) (three gates) governs *automated* fires. A user-initiated options
  order is outside the Monitor and is governed instead by the existing Paper/Live consent rule
  (Alpaca opt-in + key pair, Vault first) and the 5-minute password elevation for Live. This is
  an interpretation, not a law change. [IP-LAW-3](BIBLE.md#IP-LAW-3) holds: Sandbox is the
  default account on the page unless Paper is actually configured.
- Still Alpaca-only ([BIBLE §3](BIBLE.md#IP-§3)). Still not an autotrader for options: the DSL,
  Monitor, `RiskGuardian`, and `Conditions` catalog are untouched (Phase 2 frontier).
- [BIBLE §4.1](BIBLE.md#IP-§4) project table gains `IdiotProof.UI`; §7 frontier and §9 glossary
  updated; new Epic U in [USER_STORIES.md](USER_STORIES.md#Epic-U).
- **Prerequisite, not yet met:** the Alpaca account's `option_trading_level` is unconfirmed
  (likely 0). The ticket locks itself on Alpaca modes until approval; Sandbox works today.
- Pre-existing test debt found and fixed during this work: two `RegulatoryScannerTests` hardcoded a
  2026-07-27 notice but filtered with `since = now − 1 day`, so they failed on any day after
  2026-07-28. Now use a fixed `Since` cutoff (2026-07-01).
- Alpaca entitlement checked 2026-09-05 against `/v2/account` on both the paper and live accounts:
  neither returns `option_approved_level` / `option_trading_level` at all — options are not yet
  enabled. The 2026-09-04 S&P rebalance batch in `sp-index-events.json` was verified against the
  S&P DJI press release the same day (effective prior to the open, Mon 2026-09-21).

## IP-A32 — Research tab: search box → autonomous ranked results-review {#IP-A32}
**What changed.** (2026-07-26.) The `/research` tab required a human to type a ticker or paste
an article before anything happened — "the whole point was for the system to go out and find
this stuff itself." New `IdiotProof.ResearchScanner` console project (RFC 0003) fixes that:

1. **New scanner project, not a daemon.** `IdiotProof.ResearchScanner` runs one scan pass and
   exits — meant for a Windows Scheduled Task (`tools/register-research-scan-task.ps1`, written
   but not registered), decoupled from `IdiotProof.Monitor`'s real-time trading loop and from
   Blazor's request lifecycle. Sweeps watchlist tickers every pass plus a rotating batch of the
   tracked universe (`TickerUniverseService` + new `TrackedTicker` table, cached from Alpaca's
   asset list, refreshed every 24h), runs the regulatory scanner on its own slower cadence, then
   scores everything new.

2. **Real filing content, not boilerplate.** `Form4Parser` parses the actual Form 4 XML
   (share counts, transaction code, price, resulting holdings) into a new `InsiderTransaction`
   table — "when a CEO sells a million shares" is now a real, sized fact instead of "ownership
   changed." `CorporateActionDetector` classifies 8-K item codes already present in EDGAR's
   search response (no extra fetch to classify) and fetches real document text only for the
   split/M&A-adjacent codes (1.01, 2.01, 3.02, 3.03, 5.03). Along the way, `EdgarService` was
   found reading JSON fields that don't exist in the real API (`form_type`/`entity_name` instead
   of the actual `form`/`display_names`) — entity names had silently been empty this whole time;
   fixed, and `BrowseUrl` now points at the real per-filing archive page instead of a shared
   per-ticker search URL.

3. **Regulatory/macro events.** `RegulatoryScanner` polls the Federal Register's public API for
   SEC "Self-Regulatory Organizations" notices (exchange rule changes — e.g. the real Nasdaq $5M
   Market Value of Listed Securities continued-listing rule, SEC-approved 2026-07-22), has the
   LLM triage out routine fee-schedule noise, and persists substantive ones as macro
   `ResearchClaim` rows (`IsMacro = true`, `Ticker = ""`, tickers affected in
   `AffectedTickersJson` when a market-value screen can resolve them — an honest gap message
   otherwise, never a fabricated ticker list).

4. **Sober tone by construction.** `CatalystExtractor` gained a `Mechanism` field; the claim's
   display sentence is now composed deterministically (`"{Summary}. Affects {Ticker} because
   {Mechanism}. Expected impact: {ExpectedTimeline}."`) instead of trusting one LLM-authored
   paragraph — the tone guarantee survives prompt drift.

5. **Significance ranking + feed redesign.** New `SignificanceScorer` combines LLM magnitude/
   confidence, historical correlation strength, source trust, recency, and watchlist membership
   into one 0-100 `ResearchClaim.SignificanceScore`. `Research.razor`'s primary view is now
   "Today's High-Impact Events" ordered by that score, with a last-scan banner and a
   "my watchlist only" toggle; the old manual ticker-fetch/paste-article flow moved into a
   collapsed Advanced panel.

6. **Dedup fix.** `ResearchService.AnalyzeArticleAsync` had no dedup — a scheduler re-pulling the
   same tickers on every pass would have re-extracted (and re-billed the LLM for) the same
   article repeatedly. Now dedupes by (ticker, source URL) before calling the extractor.

7. **Outcome backfill — proving the news actually correlates with price.** A gap found only
   after the fact: `ClaimCorrelationService`'s historical matching and `SignificanceScorer`'s
   history/source bonuses both read `ResearchClaim.OutcomePctChange` — but nothing ever wrote it,
   so those bonuses silently sat at zero forever, and `SourceTrustScore`'s sub-counters
   (`PortentsClaimed`/`ImmediateClaims`) were never incremented either (only `TotalClaims` was).
   New `OutcomeBackfillService`: for every non-macro claim old enough that its predicted impact
   has had time to play out (default 5+ days), fetches the real Alpaca daily close at the claim
   date and at the outcome date (default +7 days), computes `OutcomePctChange`, and — comparing
   the actual direction against the claim's Bullish/Bearish call — marks a pending portent
   Realized or Disproven and bumps the source's `PortentsRealized`/`ImmediateCorrect` count
   (Neutral calls are never scored either way). Runs every scan pass, before significance
   scoring, so the score always reads freshly-backfilled history. Verified against real news and
   real Alpaca price history (not synthetic fixtures): a 2023-09-30 MSFT government-contract
   claim backfilled to $321.82 → $329.91 (+2.51%, Realized); an 2025-08-29 MSFT claim to
   $506.74 → $494.96 (-2.32%); a 2026-07-17 AAPL portent ("early settlement talks with DOJ")
   backfilled to a -0.21% move and correctly marked Disproven against its Bullish call; claims
   inside the 5-day window were correctly left for a later pass rather than guessed at.

Verified end-to-end against the real dev database and live APIs (not just the 200 new/updated
unit and integration tests): one full default scan pass covered 300/8,445 tracked tickers in
~4 minutes with 0 errors, and the Nasdaq MVLS rule was captured, scored 99.998/100 (top of the
feed), and read as sober equity research — "Nasdaq adopts new continued listing requirement
affecting listed companies' ability to maintain exchange quotation. Affects Nasdaq-listed
companies subject to the new continued listing requirement because New continued [listing
requirements force delisting of non-compliant issuers]." **Status: shipped & verified.**

8. **Found and fixed after the fact: EDGAR's User-Agent header was silently invalid on every
   call.** `AddHttpClient("edgar", ...)` in both `IdiotProof.Blazor/Program.cs` and
   `IdiotProof.ResearchScanner/Program.cs` registered `"IdiotProof/1 research@idiotproof.app"` —
   a bare email as a second token, which RFC 7231's product-token grammar rejects (no `@`
   allowed outside a parenthesized comment). .NET's strict header parser threw
   `FormatException` on every call, and `EdgarService.GetRecentFilingsAsync`'s own fail-closed
   try/catch silently swallowed it (logged at Warning, returned an empty list) — so the 300/8,445
   run above never actually got a single real 8-K or Form 4 filing; every claim in it came from
   Alpaca News, USASpending.gov, and the Federal Register regulatory pipeline, none of which use
   this HttpClient. `EdgarServiceTests`/`Form4ParserTests`/`CorporateActionDetectorTests` didn't
   catch it either — they inject a stub `IHttpClientFactory` that never runs the real header
   registration. Fixed (`"IdiotProof/1 (research@idiotproof.app)"`, the contact email wrapped as
   a comment) and re-verified live: AAPL now returns real Form 4 filings (6 in a 90-day window)
   and real 8-K filings (17 in a 180-day window, item codes intact — `[2.02,9.01]` for an
   earnings 8-K), plus a real fetched Form 4 XML document (3,141 chars). Added
   `EdgarUserAgentString_MatchingProgramCsRegistration_ParsesWithoutThrowing` — a test that
   directly parses the literal string Program.cs registers, since a mocked HttpClientFactory
   can never catch this class of bug.

**Why.** Session directive 2026-07-26: the Research tab should be "a results review, not a
search engine" — the system should surface high-probability market-moving events (insider
sales, splits, M&A, earnings surprises, regulatory decisions) on its own, ranked, in sober
equity-research tone, via a silent scheduled process rather than a manual search box.

**Effect on canon.** New subsystem — see [RFC 0003](rfc/0003-autonomous-research-scanner.md).
[BIBLE §4](BIBLE.md#IP-§4) architecture table gains `IdiotProof.ResearchScanner`;
[BIBLE §9](BIBLE.md#IP-§9) glossary gains Significance score, Macro claim, Tracked ticker.
New USER_STORIES Epic (Research). No law changes.

## IP-A31 — Origin transcripts, two-path coverage, and volume-gate firing fixes {#IP-A31}
**What changed.** (2026-07-20.) Three things, all in service of "the watchlist strategies must
actually fire tomorrow if the setup occurs":

1. **Origin transcripts.** New nullable `Strategy.OriginTranscript` (nvarchar(max),
   `AddStrategyOriginTranscript` migration), threaded through `StrategyRepository.CreateAsync`
   and the Monitor watchlist schema (`"OriginTranscript"`). The verbatim source the recipe was
   distilled from (e.g. the trader's watchlist-video transcript) is stored on every strategy born
   from it — intentionally denormalized, several strategies share one text — so the origin/intent
   is recallable and never lost. Attribution only; not part of the canonical strategy JSON
   ([IP-LAW-8]).

2. **Two-path coverage.** The momentum watchlist plans give *two* independent entries per name
   ("either way, you've got two ways to watch this"): the over-the-highs **breakout-pullback** and
   the **higher-low reclaim** (higher lows back over VWAP + a floor, *without* first breaking the
   high). Only the breakout path had been encoded — requiring `Breakout AND Pullback` made the
   higher-low play unreachable. Added `IsHigherLow().IsAboveVwap().IsPriceBetween(floor,…)`
   companions for HIHO and GMM (SHPH is breakout-only per its plan).

3. **Volume-gate firing fixes (money-path).** Two defects on the shared entry path that silently
   suppressed legitimate fires:
   - `AverageVolume` folded the current (spike) bar into its own baseline → a true 10× bar read
     ~7×, making `WithVolumeConfirm`/`IsVolumeAbove` harder to satisfy than authored. Now averages
     the prior up-to-20 bars (matching the swing-window convention).
   - `VolumeRatio` returned **0** when the baseline was zero — the thin-premarket small-cap case —
     so a huge real spike over a dead overnight book *failed* the volume screen and blocked the
     fire. A live bar over a dead baseline is now treated as a confirmed spike.

   Gate audit for the active watchlist (paper, no UserPreferences row, LLM voting off + no Claude
   key): LLM gate **skipped** (can't block), RiskGuardian **approves** (~$36 risk/trade < $100
   cap, 8% stop within 0.5–10%, < $500 daily, < $50 confirm threshold). All eight actives verified
   `WILL FIRE: YES`, none quarantined. **Status: shipped & verified.**

## IP-A30 — Strategy authorship attribution {#IP-A30}
**What changed.** (2026-07-20.) Strategies now carry an **author** — credit for who invented the
*recipe*, distinct from `OwnerUserId` (whose account runs it). New nullable `Strategy.Author`
column (`AddStrategyAuthor` migration), threaded through `StrategyRepository.CreateAsync`, the
Monitor's `create-strategies` watchlist schema (`"Author"` field), and shown in `status`
(`▸ SYM "Title" — by <author>`).

- The gapper/breakout-pullback family carried over from the trader transcripts (ADVB, BXBL, FGMC,
  HIHO, SHPH, GMM) is credited **`momentum`**.
- Three **`IdiotProof`**-authored originals were added — deliberately *self-priced* (keyed to
  VWAP/EMA/structure + volume, not hard-coded price levels), to contrast with momentum's
  absolute-level breakouts: **VWAP Reclaim Continuation** (NVDA, long — reclaim + higher-low),
  **Failed-High Fade** (TSLA, short — lower-high + stretched RSI + VWAP loss), **Coiled EMA
  Squeeze Breakout** (AMD, long — EMA stack + 2× volume expansion).

Attribution is metadata, not part of the canonical strategy JSON ([IP-LAW-8]) — the script still
defines *what it does*; author records *who made it*. **Status: shipped.**

## IP-A29 — Real-time SIP is the default feed (Algo Trader Plus) {#IP-A29}
**What changed.** (2026-07-20.) The account's Alpaca market-data subscription was upgraded to
**Algo Trader Plus** — unlimited **real-time SIP** consolidated tape, including the 4:00 AM
premarket window. Verified live against the account key: `GET /v2/stocks/AAPL/quotes/latest?feed=sip`
returns `200` with a current, tight quote (the free tier returns `403` on that call), while the IEX
quote is stale at the 4 PM close with a junk spread. This removes the single biggest constraint on
the flagship Gapper flow: the live Monitor no longer trades on the thin partial IEX book.

1. **Live feed default flipped `iex` → `sip`** in both the Monitor's streaming client
   (`MonitorWorker`) and its historical `AlpacaDataFeed` registration (`Program`).
   `IDIOTPROOF_ALPACA_FEED=iex` still forces the free feed for anyone without the subscription.
2. **Replay 15-min SIP wall lifted.** `StrategyReplay` clamped requests to `now-16min` because
   free SIP historical is delayed ≥15 min; with real-time entitlement the clamp drops to a 1-min
   guard (never request the currently-forming bar), so intraday replays of *today* run right to
   the last closed minute.
3. **Replay disclaimer corrected.** The per-run feed note no longer warns that the live IEX system
   sees different bars — replay and live now share the same real-time SIP tape.

**No new API key required** — a market-data subscription attaches to the Alpaca *account*, not to
a key; the existing BYO key/secret gains the entitlement immediately. **Status: shipped & verified.**

## IP-A28 — Dual-host UI: one shared Razor Class Library for Blazor Server + MAUI desktop {#IP-A28}
**What changed.** (2026-07-20.) Reverses the earlier web-only / "No MAUI host" stance. IdiotProof's
UI consolidates into ONE shared Razor Class Library, `IdiotProof.UI`, rendered by two hosts that
cannot drift — **parity by construction**:

- `IdiotProof.Blazor` — Blazor Server (online / browser), real login.
- `IdiotProof.Maui` — MAUI Blazor Hybrid (Windows desktop, runs without localhost), **also with
  login** (full parity — desktop is not single-user).

Both reference the SAME RCL; a page/component is **never** forked per host. Host projects keep only
their root/shell + host-specific services (auth backend, endpoints/SignalR on Server; `MauiProgram`/
`MainPage` + a local auth backend on desktop). The `IdiotProof.Monitor` console evaluator is unchanged.

**Status: foundation laid** — `IdiotProof.UI` (RCL) + `IdiotProof.Maui` (Windows-only TFM; only the
`maui-windows` workload is installed) created and added to `IdiotProof.slnx`; both build; the existing
app is intact. The remaining migration — extracting the app services/data out of `IdiotProof.Blazor`
into a shared project (so the RCL *and* the Monitor reference it without a cycle), moving the Razor
components into the RCL, and wiring both hosts + the desktop auth backend — proceeds **incrementally
with a green build at each step** (it touches the whole project graph, incl. the Monitor's data usings).

## IP-A27 — Strategy-family expansion: shorts, RTH-drive, RSI/swing reversals {#IP-A27}
**What changed.** (2026-07-20.) The [IP-A25]/[IP-A26] replay surface grows from long-only
gappers to a full long+short family library, plus the pivot-based swing-structure primitive the
bottom-reversal charts (BE, AMD, ORCL, ISRG) required.

1. **Short-side support.** `GapperExitEvaluator.EvaluateShort` — the mirror of the long exit
   logic (trough low-water mark, stops ABOVE entry, take-profit below, giveback = bounce off the
   trough); `StrategyReplay` dispatches it by `def.Direction` and inverts P&L. New `shortfade`
   profile (short a failed high, below VWAP/EMA9). The live money path already handled shorts
   (`RiskGuardian` requires the stop above entry, [IP-LAW-2]); this closed the replay's long-only
   exit sim.
2. **More long families.** `rthdrive` (RTH open-drive: above VWAP + above EMA9 + EMA9-over-34, a
   trend-HOLDING entry — a crossing trigger can't catch a continuous ramp); `rsireversal`
   (RSI-oversold-at-support dip-buy).
3. **Swing-structure primitive.** `IndicatorSnapshotBuilder` now detects the last two pivot
   lows/highs → `HasHigherLow`/`HasLowerHigh`; DSL verbs `IsHigherLow()`/`IsLowerHigh()`; a
   prior-HOD take-profit (`StrategyDefinition.ExitAtPriorHigh`) that sells long into the pre-entry
   high; `swingreversal` profile (buy a confirmed higher low → target the earlier HOD).

Also: `reversal` gained a sell-by; replay pages got the Alpaca-yellow light accent, an SVG
CSS-variable fix, client-side Mermaid rebuild, and a legend fix. Nine strategy families now run
through one evaluator → scan → SQL feature store → dataset. (RSI bullish-divergence stays
declared-but-uncomputed — a further bottom-signal upgrade.)

## IP-A26 — Reversal/EMA-break strategy families, normalized ML feature store, OAuth foundation {#IP-A26}
**What changed.** (2026-07-20.) Three additions extending the [IP-A25] replay/scan/dataset surface.

1. **Two non-gapper strategy families** (`--profile reversal | emabreak`), composed with the real
   DSL and run through the same evaluator/scan/dataset. `reversal` = EMA9 reclaim off a recent low
   (`OnReclaim(9)` + `IsAtSupport`, a BE-style higher-low bounce); `emabreak` = EMA200 reclaim while
   above VWAP (SPCX-style trend break). `StrategyReplay.BuiltinStrategy()` dispatches
   momentum/reversal/emabreak, else falls through to the gapper-profile catalog. (A dedicated
   swing-structure verb — explicit higher-low/lower-high — remains the follow-up that would sharpen
   both `reversal` and the AMD-style range logic; today they approximate it with EMA reclaim.)
2. **Normalized ML feature store [IP-LAW-7].** `ReplayTrade` (one row per round-trip: entry-bar
   features → `pnlPct`/`won` label) and `ReplayBar` (one row per minute) tables (`AddReplayFeatureStore`
   migration), FK-linked to `ReplayRun` (cascade). Populated on every replay and back-filled by
   `replay-export`, which now emits its CSVs straight from these tables — one source of truth,
   directly queryable for analytics/training.
3. **OAuth/Connect foundation.** `IdiotProof.Brokers.AlpacaOAuthClient` (authorize-URL builder +
   code→token exchange) — the account-LINKING alternative to a raw key/secret: the user authorizes on
   Alpaca's own page and IdiotProof stores a scoped, revocable token instead of the keys. Deliberately
   OFF the money path; wiring the token into order placement (a Bearer mode on `AlpacaBrokerClient`) is
   gated on registering an Alpaca OAuth app + paper testing, never shipped blind (activation checklist
   in the class). [IP-LAW-1]/[IP-LAW-2] unaffected.

## IP-A25 — Offline strategy replay harness, SQL-persisted archive, gapper scanner, ML dataset {#IP-A25}
**What changed.** (2026-07-20.) A replay/analysis surface on the Monitor CLI that reuses the
*live* evaluator to show — and publish — what a strategy would have done on a past session.
Five additions:

1. **`replay` command.** Walks a day's Alpaca bars (delayed SIP is free ≥15 min old; the fetch
   window is clamped to `now−16min` so an intraday replay of *today* works) through the SAME
   code the Monitor runs — `IndicatorSnapshotBuilder`, the strategy's real `ICondition.Evaluate`,
   and the shared `MarketTime.IsInsideSession` gate (extracted from `MonitorWorker` so live and
   replay can never diverge). It reuses `GapperExitEvaluator` to simulate exits, so a repeating
   strategy yields several entry→exit round-trips ("payoffs"), each with reason and P&L. A ticker
   with no saved strategy is replayed by composing a gapper profile on the fly
   (`GapperScriptFactory`, [IP-A8]) or a built-in repeating momentum strategy. Folder ids are ET
   generation stamps `yyyy-MM-ddTHH.mm.ss` with a bijective `-a…-z,-aa` suffix on collision.
2. **SQL system of record [IP-LAW-7].** Each run is a `ReplayRun` row (metadata + the full page
   DATA payload + the strategy card/flow HTML), added by the `AddReplayRuns` migration. The pages
   under `/idiotproof/replays/<ticker>/<stamp>/` are a VIEW rendered from those rows; the
   per-ticker and root indexes are built by querying the table; `replay-regen` rebuilds the whole
   archive from SQL alone. The Monitor CLI migrates on first use so it is self-sufficient.
3. **`scan` command.** Pulls the day's movers straight from the Market Data API
   (`/v1beta1/screener/stocks/movers`) — no HTML scraping — filters to the gapper band, and drives
   a replay for each survivor, so one command populates the morning board.
4. **`replay-export` command.** Flattens the archive into ML-ready CSVs (`trades.csv` one row per
   round-trip with entry features → P&L label; `bars.csv` one row per minute) at
   `/idiotproof/dataset/`.
5. **Rendering.** `StrategyDefinition.ToHtml()` (phase cards), `.ToMermaid()` (flow as Mermaid),
   and `.ToSvg()` (flow as inline SVG) render the strategy; pages default to a dark theme with a
   moon/sun toggle persisted to localStorage. Published via `MindAttic.Deploy` recursive-upload
   site mode (`--site idiotproof-replays`).

Note: this harness read-only *analyses* — it never places orders (it has no broker call path);
[IP-LAW-1] and [IP-LAW-2] are unaffected. Tests ship after per the README sequence, so the
Epic-R stories are 🟡 until cited.

## IP-A24 — Bug-hunt round 11 on the IP-A23 surface + multi-strategy-per-ticker + order-execution proof {#IP-A24}
**What changed.** (2026-07-20.) Eleventh find-10-fix-10 sweep, focused on the fresh IP-A23
code (diary, operator CLI, blocklist, Monitor wiring), plus two capability additions the
paper run needed. Ten fixes:

1. **Monitor could crash on boot** — the new Security-bucket surfacing parsed
   `providers.json` unguarded; a malformed/locked file would take down the *trading* process.
   Now try/catch (create-account degrades; the eval loop is unaffected).
2. **CLI `create-account` disposable check was a no-op** — the blocklist only seeds on Blazor
   startup, so the Monitor standalone saw an empty table. It now seeds before checking, and
   distinguishes malformed vs disposable.
3. **`set-keys` stored keys with zero validation** — a typo'd/dead key silently broke routing.
   Now validates the PK/AK-vs-paper/live prefix AND does a live authenticated Alpaca probe
   before saving (`--force` overrides).
4. **`ResolveUserAsync` leaked a DbContext** (no `await using`).
5. **`ResolveUserAsync` returned null silently** with >1 user — now a clear "pass --user" error.
6. **Diary return % was wrong on partial fills** — cost used the optimistic entry quantity
   while realized P&L used the (smaller) reconciled sold quantity. `CloseAsync` now takes the
   sold quantity and records/returns on it.
7. **Blazor startup seed/backfill was unguarded** — a hiccup aborted web startup. Now
   log-and-continue.
8. **Registration showed "domain not accepted" for MALFORMED emails** — now a distinct
   "enter a valid email" message.
9. **A crash between buy and sell orphaned a diary Open row** — the next trade's close could
   match the wrong (stale) row. `OpenAsync` now marks any pre-existing Open for the strategy
   `Orphaned`, so there is never more than one Open.
10. **`create-strategies` didn't dedup within a single watchlist file** — same title+symbol
    twice created two rows. Now in-batch deduped.

**Capabilities added.**
- **`test-order` CLI** — places a deliberately-unfillable limit and cancels it through the REAL
  broker code path, proving the account can place AND cancel orders autonomously (no human
  step). Verified against Alpaca paper.
- **Multi-strategy-per-ticker** ([IP-A24]) — the broker reports ONE position per symbol, but a
  user may run competing strategies on the same ticker to compare. The exit reconciliation
  (IP-A20) compared each strategy to the broker AGGREGATE, which cross-contaminates when a
  symbol is shared. `CountHoldingForSymbolAsync` now detects the shared case and skips the
  aggregate reconciliation there (trusting per-strategy bookkeeping; each strategy's diary P&L
  is computed from its own fills, so the comparison stays valid). Sole-holder — every case
  today — reconciles exactly as before. True per-strategy attribution on a shared symbol still
  wants order-state tracking (deferred).

**Live-run tuning (same session).**
- **Evaluation interval 5s → 1s** (default; still `IDIOTPROOF_MONITOR_INTERVAL`-overridable) so a
  live market is reacted to within ~1s off the streamed last trade — no extra REST (candles are
  cached + stream-fed).
- **Throttled the "no previous close" warning** to once per 10 min per symbol (it retries every
  30s but was logging every retry), and corrected the wording — a missing previous close only
  gates GAP conditions; non-gap strategies are unaffected.
- **FGMC fallback strategy** — the speaker's #2 pick (FGMC→BXBL post-merger) has NO Alpaca data
  under `BXBL` yet (the stock still trades as `FGMC`), so an FGMC mirror of the BXBL setup was
  added active alongside it; whichever ticker the feed serves is covered.

**Why.** Session directive 2026-07-20: "find 10 fix 10, and redeploy"; plus the operator's need
to prove creds place valid API-honored orders and to run multiple strategies per ticker.

**Effect on canon.** Additive; no laws changed. The new build was published to the on-box
deploy path and the scheduled task restarted on it (heartbeat green). Build green; backend
tests pass (Blazor 61, Strategies 82, Brokers 13, Indicators 18).

## IP-A23 — Trade diary, operator CLI, disposable-email blocklist; first live paper account {#IP-A23}
**What changed.** (2026-07-20.) Feature work to stand up a real per-user paper-trading run:

1. **Trade diary** — new `TradeDiary` SQL table (one row per trade lifecycle) written by the
   Monitor on the money path: opened on the buy (side, size, entry price/time, the full risk
   plan — stop / trailing / take-profit / peak-giveback / sell-by — broker + `IsPaper`),
   closed on the sell (exit price/time/reason, realized P&L, return %), and marked `NotFilled`
   when reconciliation proves a phantom entry. Deliberately denormalized and FK-free (no
   cascade) so it's a permanent record that survives strategy/account deletion. All diary
   writes are log-and-continue — a diary failure can never break a trade. (`TradeDiaryEntry`,
   `TradeDiaryRepository`; migration `AddTradeDiary`.)
2. **`IBrokerClient.IsPaper`** — a broker-abstract paper/live flag (Sandbox always paper;
   Alpaca reflects its endpoint) so the diary and the ops CLI can never mislabel a live fill
   as paper. Interface-level on purpose — a future broker must answer it.
3. **Monitor operator CLI** — `dotnet run --project IdiotProof.Monitor -- <cmd>` subcommands
   that run against the REAL DI/config and exit without starting the worker:
   `status` (verifies key / paper / will-fire / will-sell for every active strategy, with a
   LIVE authenticated Alpaca ping), `set-keys` (per-user Alpaca keys, no UI), `create-strategies`
   (from a watchlist JSON, canon-first via the repo), and `create-account` (real Argon2id path).
   The auth stack + Security-vault-bucket surfacing were added to the Monitor host so
   `create-account` works headless.
4. **Disposable-email blocklist** — new `DomainNameBlacklist` table (seeded at Blazor startup
   with ~90 known temp-mail domains, admin-extendable) + `EmailDomainBlocklistService`;
   `/register-submit` and the CLI `create-account` both reject malformed and disposable domains.
   (Migration `AddDomainNameBlacklist`.)

**Why.** Session directive 2026-07-19/20: interpret a trader's watchlist transcript into live
paper strategies, with a diary recording every buy/sell, and a CLI to verify the setup — a
real-money-shaped run (paper) demanding the money path be verifiable end to end.

**Operational note (not canon).** First live paper account provisioned:
`ryandebraal@mindattic.com` routing to its own Alpaca **paper** account; two breakout-pullback
strategies (BXBL, ADVB — VEEE excluded per the transcript's explicit do-not-trade) active for
the Extended (all-day) session, exit on stop/trailing/target. On-box hosting is a run-as-user
scheduled task; Azure target (Container Apps / continuous WebJob) recorded for go-live.

**Effect on canon.** Additive — no laws changed. Reinforces [IP-LAW-3](BIBLE.md#IP-LAW-3)
(the `status` ping and `IsPaper` make paper-vs-live explicit and auditable) and
[IP-LAW-7](BIBLE.md#IP-LAW-7) (diary + blocklist are runtime state in SQL). Build green; 174
backend tests pass (Blazor 61, Strategies 82, Brokers 13, Indicators 18, plus Engine).

## IP-A22 — Bug-hunt round 10: real-money guards, BYO-key integrity, shipped-config coherence {#IP-A22}
**What changed.** (2026-07-19.) Tenth find-10-fix-10 sweep, run under the standing directive
that this is **real money and the money path must be bulletproof** — bring-your-own Alpaca key,
tight Alpaca integration behind the `IBrokerClient`/`IMarketDataFeed` abstractions so future
brokers inherit the same guards. Ten fixes, six with regression tests.

1. **Real orders on synthetic prices.** The market-data feed is a single global instance (keyed
   on the host's Alpaca settings; Mock when unkeyed), but order routing is per-user (IP-A9). A
   host missing global data keys while a user had their own would evaluate strategies against
   Mock prices and fire REAL orders on them. The Monitor now refuses any non-Sandbox **entry**
   when the feed is Mock (exits are risk-reducing and still allowed); Mock data pairs only with
   the Sandbox broker.
2. **Polygon key silently wiped.** `UserKeyService.Encrypt` AND `Decrypt` both omitted
   `PolygonApiKey`, so a saved Polygon key was dropped to null on every save and never read
   back — the API Keys field and the Backtest real-data feed (both advertised in the README)
   were dead. Now protected/unprotected like the other secrets (LocalDB round-trip + encrypted-
   at-rest tests).
3. **Phantom-fill reconciliation hardened.** IP-A20's exit reconciliation could clear a
   still-working premarket limit order (which can rest for minutes) as a "non-fill", letting the
   next tick re-enter and double the position; and it cleared via `RecordExitFillAsync`, leaving
   `EntryFilledUtc` set so a genuine non-fill was locked out for the day. Now: a 90-second grace
   window before declaring a non-fill, and a full `ClearUnfilledEntryAsync` reset (clears
   `EntryFilledUtc`) so a real non-fill re-arms within its window.
4. **Shipped gapper profile blocked by shipped risk default.** "Penny Runner" uses an 8% stop
   (pennies collapse fast) but the default `MaxStopLossPercent` was 5%, so RiskGuardian blocked
   every fire of that profile out of the box. Raised the default to 10% in both
   `RiskGuardianConfig` and `UserPreferences`; the DOLLAR cap (`MaxLossPerTrade`) remains the
   binding money constraint, the percent guard a secondary width bound (tests: 8% clears, 15%
   still blocked).
5. **AuditLog write could throw on long messages.** `AuditLog.Message` is `nvarchar(500)` but
   the Monitor builds messages from raw Alpaca error bodies / stacked block reasons that can
   exceed it → "string or binary data would be truncated" throws, losing the entry (and
   throwing right after a real order on the order-placed path). `LogAsync` now truncates to the
   column width and preserves the full text in `DataJson` (unbounded).
6. **AccountPill under-represented live trading.** The red-outline "Live" cue is driven by a
   UI preference, but routing is by key mode; a user with LIVE keys who never touched the pill
   saw the SAFE yellow "Paper" pill while real money traded. The pill now reconciles to the
   configured key mode (World Rules: the Live pill must show the danger outline).
7. **Register button lied.** "Create Account & Sign In" redirected to /login without signing
   in. Honest button text + a "sign in to continue" banner on the login page.
8. **GapperInterpreter dropped stringified numbers.** LLMs often emit numeric fields as strings
   ("7"); one threw a JsonException that dropped the whole profile overlay to defaults.
   `AllowReadingFromString` added (tested).
9. **Backtest deep-link stale state.** `/backtest/{id}` selected the strategy only in
   `OnInitializedAsync`; Blazor reuses the component across deep-link changes, so a new id left
   the old strategy chosen. Selection moved to `OnParametersSet` keyed on the route id (same
   fix pattern as IP-A19's StrategyBuilder).
10. **Learn Center taught the wrong phase.** The reflected verb catalog classified `Quantity*`
    under Setup; the DSL spec (CLAUDE.md) puts quantity in the Order phase. Reclassified.

**Why.** Session directive 2026-07-19: "find 10 fix 10" (round 10), plus the explicit standing
requirement that the real-money path be bulletproof and Alpaca-tight yet broker-abstractable.

**Effect on canon.** Adds a real-money invariant enforced at the Monitor: **synthetic
market data can never drive a real (non-Sandbox) order** — reinforcing
[IP-LAW-3](BIBLE.md#IP-LAW-3) (Sandbox is the safe default) and [IP-LAW-2](BIBLE.md#IP-LAW-2).
The guards live at the `IBrokerClient`/`IMarketDataFeed` seams, so a future broker inherits
them. 205 tests green. No new laws.

## IP-A21 — Bug-hunt round 9: editor lost-updates, under-validated auth inputs, deploy-blind config {#IP-A21}
**What changed.** (2026-07-19.) Ninth find-10-fix-10 sweep — persistence-layer concurrency, the
input-validation surface, and configuration that only worked from a dev checkout. Ten fixes,
four with regression tests (`StrategyRepositoryGuardTests`, `GapperInterpreterTests`):

1. **The editor's Save clobbered the Monitor's live position bookkeeping** — `UpdateAsync` did
   a full-row write from the caller's detached snapshot, so saving an editor opened minutes
   earlier stomped `PositionQty`/`LastEntryPrice`/`FireCount` back to stale values: a filled
   position silently read as flat (orphaned shares, exits never run) AND the one-shot-per-day
   guard re-armed for a duplicate fire. `UpdateAsync` now writes ONLY the editor-owned columns
   (title/symbol/description/script/IsActive) onto the FRESH row, returns a `StrategyMutation`,
   and enforces the same PositionOpen/NotOwner guards as `SetActiveAsync` (tested).
2. **`legion.json` never shipped with either host** — the voter-panel config lives at the repo
   root and only walk-up discovery from a dev checkout found it; a deployed Blazor host or
   Monitor silently reverted the "high tier" 4-voter panel to Legion's defaults. Both csproj
   files now link and publish it.
3. **Monitor hammered the daily-bars endpoint during outages** — IP-A18's "don't cache a null
   previous close" fix retried EVERY tick (12 req/min/symbol), the mirror image of the
   empty-candle-window problem it sat next to. Nulls now negative-cache for 30 s.
4. **Gapper dial fields parsed with server culture** — the string-bound Max-gap and Trailing
   dial-ins used bare `double.TryParse`, so on a comma-decimal host "2.5" read as 25: a
   silently 10×-loosened trailing stop. Invariant both ways now.
5. **ConditionProgress rows orphaned forever** — no FK links them to Strategies, and delete
   never cleaned them; a table the Monitor hammers every tick accumulated rows for strategies
   that no longer exist. Deleted alongside now (tested).
6. **The Strategies page froze at load** — only the progress badge polled; the active toggle,
   "last fired", and position/exit state never refreshed, so the Monitor could fire and exit
   a strategy while the page claimed it never fired. The existing 5-second poll now refreshes
   each row's volatile fields in place (matched by id — no re-sort, no scroll jump).
7. **A truncated interpreter response lost every candidate silently** — a transcript response
   that hit the token cap started the JSON array but never closed it, and the generic "no
   JSON array" warning hid the cause. Token headroom doubled (4000) and truncation now warns
   specifically ("cut off — try a shorter transcript") (tested).
8. **Login forwarded `?returnUrl` unvalidated** — a crafted login link could bounce a
   successful sign-in to an attacker's site (open redirect / phishing). Only same-site paths
   are forwarded now (absolute, protocol-relative, and backslash forms all fall back to "/").
9. **Password endpoints under-validated** — the register and dev-reset endpoints never
   enforced the "one digit" rule both pages advertise, and accepted unbounded passwords —
   an anonymous Argon2id CPU-exhaustion vector. Both rules enforced (8–128 chars, ≥1 digit)
   with proper per-page error messages.
10. **AI-generated scripts were never parse-checked** — IP-LAW-4 promises invented verbs
    "fail to parse", but nothing told the user: a hallucinated condition quietly vanished
    from the saved strategy. Generation now parse-checks the output and shows an inline
    heads-up when the script doesn't fully parse.

**Why.** Session directive 2026-07-19: "find 10 fix 10" (round 9).

**Effect on canon.** The repository layer now owns ALL strategy-mutation invariants (update
included — no page-level path can clobber Monitor state), and the LLM voter-panel config
survives deployment. 199 tests green. No new laws.

## IP-A20 — Bug-hunt round 8: phantom positions, account-takeover chain, window-scoped latches {#IP-A20}
**What changed.** (2026-07-19.) Eighth find-10-fix-10 sweep — the order/position boundary, the
anonymous auth surfaces, and the latch-verb gap left by IP-A18. Ten fixes, six with regression
tests (`WindowScopedConditionTests`):

1. **Phantom positions from unfilled entries** — the Monitor records the entry fill the moment
   Alpaca ACCEPTS the order, so an unfilled marketable limit (price ran, halt, expiry) left
   bookkeeping claiming shares that don't exist; the exit brain would then SELL them —
   rejected at best, a naked short on a margin account at worst. Exits now reconcile with the
   broker first: no broker position (from a successful call) → phantom bookkeeping cleared
   with an audit entry, no order; partial fill → sells the broker quantity.
   `AlpacaBrokerClient.GetPositionsAsync` now fails LOUD on HTTP/parse errors instead of
   returning `[]`, so "empty means flat" is actually true; on reconciliation failure the
   risk-reducing exit proceeds with the recorded quantity. (Full order-state tracking —
   pending orders as first-class rows — remains future work; this closes the dangerous half.)
2. **Unauthenticated account takeover via `/forgot-password-submit`** — the "dev-only" direct
   password reset (email + new password, no token/old password/session) was mapped
   unconditionally, production included. Now mapped only in Development; the ForgotPassword
   page tells production users to contact the administrator.
3. **Anonymous email enumeration via `/forgot-username`** — the page listed EVERY registered
   user's email to anonymous visitors (chained with #2: a two-click takeover map). The
   listing (and its query) now renders in Development only.
4. **`Breakout()`/`Pullback()` were dead on the live path while the Learning Center teaches
   them** — all three "production-ready" example strategies are built on the latch verbs
   IP-A18 made fail closed. New window-scoped semantics: `IndicatorSnapshot.WindowHigh/Low`
   (computed by the snapshot builder over the whole candle window) stand in for the
   backtester's cross-tick latch — Breakout(level) = the level traded in the window;
   Pullback = retest of the support (or any retracement off the window high). No window
   data still fails closed; the backtester's precise `TrackedTrigger` is unchanged.
5. **`HoldsAbove`/`HoldsBelow` degraded to point-in-time checks live** — fresh instances per
   tick meant "never violated" only ever saw the current price. The window extremes restore
   violation memory (an earlier dip through the level now fails the gate).
6. **A deferred SellBy lost its urgency overnight** — a position that survived past midnight
   ET (exit rejections, market closed) re-waited for the sell-by TIME next day (04:00 <
   09:28), holding unwanted overnight exposure for hours with only the stop active. A
   sell-by position that outlived its entry's ET day now flattens at the first evaluated
   instant.
7. **MockDataFeed synthesized weekend minute bars** — the daily branch skipped
   Saturday/Sunday but the intraday branch happily printed them, so a replay pointed at a
   weekend date reported phantom trades. No weekend minute bars now.
8. **Re-selecting the Backtest placeholder crashed the circuit** — the strategy `<select>`
   bound `""` to a non-nullable `Guid`, and the bind-conversion exception tore down the
   session. Nullable now.
9. **TradingHub accepted anonymous connections** — the SignalR hub had no `[Authorize]`, so
   an unauthenticated client could join the broadcast group and receive whatever
   signals/prices get published. Authorized now.
10. **API Keys Save crashed the circuit on failure** — an unhandled Data-Protection/SQL
    exception in `SaveAll` produced the generic error banner and lost the whole form.
    Guarded and surfaced inline.

**Why.** Session directive 2026-07-19: "find 10 fix 10" (round 8).

**Effect on canon.** The order boundary now tolerates its own optimism (positions reconcile
against the broker before any exit order — strengthens [IP-LAW-2](BIBLE.md#IP-LAW-2)'s "the
worst case cannot exceed the limit" by preventing phantom-driven shorts), and the latch verbs
the Learning Center teaches are evaluable on the live path (window-scoped, fail-closed without
data — consistent with [IP-LAW-1](BIBLE.md#IP-LAW-1)). 195 tests green. No new laws.

## IP-A19 — Bug-hunt round 7: replay exit fidelity, missing risk editor, stale-state editor {#IP-A19}
**What changed.** (2026-07-19.) Seventh find-10-fix-10 sweep — the replay engine's exit
fidelity, the UI surfaces the earlier rounds never opened, and locale-hostile serialization.
Ten fixes, six with regression tests (`StrategyBacktesterExitTests`, `ScriptTextRoundTripTests`,
`UserPreferencesServiceTests`):

1. **The generic replay ignored `TrailingStopLoss` and `PeakGiveback` entirely** —
   `/backtest` simulated only stop/targets/time exits, so any strategy using the Risk-phase
   trailing stop or the flagship momentum-rollover exit was reported holding to the time
   exit/end of session: the exact backtest ≠ live divergence class IP-A15/A18 hunted, on the
   two exits that define the gapper. Both now replay (trailing intrabar off the peak,
   giveback close-based like the live evaluator).
2. **The risk-limit editor did not exist** — `SetRiskConfigAsync` and
   `RiskGuardianService.Invalidate` had ZERO production callers and the Settings page was a
   placeholder, so every user was permanently stuck on the RiskGuardian class defaults
   ($100/trade, $500/day, $10k balance) with no way to change the IP-LAW-2 limits the whole
   product is named for. The Settings page now edits all six limits (clamped by the service,
   clamps read back), expiring the UI-process Guardian config in place.
3. **Editing strategy A could overwrite strategy B** — the builder loaded its fields in
   `OnInitializedAsync` only, but Blazor reuses the component across /builder/{A} →
   /builder/{B} → /builder navigations (browser back/forward, "Create New" after an edit),
   leaving A's fields on screen under B's route id — Save then wrote A's content into B's
   row. Loading moved to `OnParametersSetAsync` keyed on the route id; Learn-page seed links
   also re-apply correctly on repeat use.
4. **A quarantined strategy holding shares lost exit management silently** — the quarantine
   check ran before the open-position check, so a holding row whose canon went invalid got a
   quiet "(invalid strategy)" note while its stop/giveback/sell-by brain simply stopped
   running. Now escalated: error-level log + a progress badge that says the position is
   unmanaged until the strategy is fixed or flattened.
5. **Comma-decimal locales corrupted script round trips** — `TrailingStopLoss(2.5)`
   serialized as `TrailingStopLoss(2,5)` on a de-DE host, which the invariant parser reads
   as TWO args and applies as a silently tightened 2% trail; `StrategyOverrides.ToScript`
   had five more default-culture sites corrupting branch overrides the same way. All emit
   InvariantCulture now (tested under de-DE).
6. **API Keys pinned users to the retired `claude-sonnet-4-6`** — the page default, the
   empty-row fallback, and the only full-tier dropdown option all used the pre-rename id
   (the entity default is `claude-sonnet-5`), so saving keys pushed every user back onto the
   deprecated model. Dropdown now leads with `claude-sonnet-5`; the legacy id stays
   selectable so old rows still bind.
7. **The visual preview misrepresented every gapper** — `StrategyBuilderRenderer` showed no
   sizing for notional strategies (all gappers) and never rendered the PeakGiveback exit,
   so the WYSIWYG card omitted both the dollars at risk and the signature exit. Chips added
   (Setup/Order notional, Exit giveback + arm time).
8. **One corrupt workspace row broke workspace loading entirely** — `SqlWorkspaceStore.Load`
   let `JsonException` propagate (its JSON-file sibling explicitly skips corrupt files).
   Unreadable rows are now skipped.
9. **The gapper day replay used the host's keys, not the user's** — `/gapper`'s "Backtest a
   day" read only the global settings chain while `/backtest` prefers the signed-in user's
   own Alpaca keys, so a keyed user got synthetic Mock bars on one page and real bars on the
   other. Same priority now (user pair first, global fallback, Mock last).
10. **Else-only branch blocks crashed view generation** — `ConditionalBlock.ToScript`
    bang-dereferenced the first branch's condition, which canonical JSON legally allows to
    be null. Null-safe now (renders as `.Else(...)`).

**Why.** Session directive 2026-07-19: "find 10 fix 10" (round 7).

**Effect on canon.** Restores the backtest-mirrors-live invariant for the Risk/Exit phases
and makes the [IP-LAW-2](BIBLE.md#IP-LAW-2) limits actually user-settable end-to-end (the
Settings-page flow other docs already described now exists). 189 tests green. No new laws.

## IP-A18 — Bug-hunt round 6: fail-open entry conditions, dead cross triggers, blind replay surfaces {#IP-A18}
**What changed.** (2026-07-19.) Sixth find-10-fix-10 sweep — focused on the evaluation pipeline
(conditions, market data, replay surfaces) rather than the guards rounds 4–5 hardened. Ten
fixes, eight with regression tests (`ConditionFailClosedTests`):

1. **A transient 4 AM feed blip disabled gap strategies for the whole day** — the Monitor
   cached the previous close per ET day INCLUDING nulls, so one failed fetch pinned "no
   previous close" until midnight and every gap condition failed closed all day. Nulls now
   retry next tick; only real closes are cached.
2. **`BreaksAbove`/`BreaksBelow` could never fire in the Monitor** — the definition is
   re-materialized from canonical JSON every tick, so the stateful `PriceLevelCondition` was
   a fresh instance each evaluation and its prior-price memory was always null: the cross was
   structurally undetectable. Cross checks now fall back to the snapshot's prior-bar close.
3. **`IsMacdBearish()` failed OPEN without MACD data** — under ~26 bars (exactly the early
   premarket a gapper trades) `MacdLine > SignalLine` is null-false, so the bare negation
   passed the bearish gate on every data-starved bar. Now requires data (IP-LAW-1 doctrine).
4. **`IsDiNegative()` failed OPEN without ADX data** — same shape, under ~28 bars. Both DI
   verbs now require PlusDI/MinusDI to exist.
5. **`Breakout()`/`Pullback()` were always-true on the live path** — the latch state machine
   exists only in the backtester's `TrackedTrigger`; direct evaluation (Monitor, DslStrategy)
   returned `true` unconditionally, so a live strategy's core trigger was silently satisfied
   and it fired on the remaining conditions alone. Direct evaluation now fails closed (the
   blocked verb is visible in ConditionProgress until the tracker is ported); the unknown-type
   default arms of Pattern/Indicator/Price conditions fail closed too.
6. **`/backtest` could never trigger a premarket or gap strategy** — the page fetched
   09:30–16:00 RTH bars regardless of the strategy's session (a gapper's 4:00–9:00 entry
   window never had data), and no previous close was plumbed into `StrategyBacktester`, so
   gap conditions failed closed on every replay. The fetch window now follows `def.Session`
   and `BacktestOptions.PreviousClose` carries the reference close end-to-end.
7. **Branching strategies lost their sizing and rollover exit** — `DslStrategy.ResolveBranches`'
   clone omitted `NotionalAmount`, `PeakGivebackPercent`, and `PeakGivebackArmTime`; any
   strategy with a ConditionalBlock silently dropped its dollar sizing and momentum exit in
   the resolved definition (latent — no current consumer reads those fields off the clone).
8. **The Monitor hammered the bars endpoint all night** — an empty candle window was never
   cached, so overnight (no bars) every tick re-fetched via REST: 12 requests/min/symbol
   burning the Alpaca rate limit right before the 4 AM window gappers arm in. Empty windows
   now cache for 30 s.
9. **`Entry(price)` silently vanished from scripts** — the serializer emits it and the
   reflection-built catalog (IP-LAW-4) teaches it to Claude, but `ScriptParser` had no case,
   so the price gate was dropped on every text round trip and from AI-generated scripts.
   Parser case added; `PriceCondition`/`PriceLevelCondition`/`PatternCondition.ToScript` also
   emit InvariantCulture numbers so comma-decimal locales can't produce unparseable verbs.
10. **Replay pages defaulted to days with no data and lied about them** — both replay date
    pickers defaulted to a raw "yesterday" (server-local; lands on weekends, and on a UTC
    host rolls to the in-progress session at 8 PM ET), and a no-data day rendered as "no
    entry condition fired — the strategy would have stayed flat", a false claim about a day
    that was never replayed. New `MarketTime.PreviousEquityTradingDayEt` drives both
    defaults; `StrategyBacktester` now emits an explicit no-data note.

**Why.** Session directive 2026-07-19: "find 10 fix 10" (round 6).

**Effect on canon.** Extends [IP-LAW-1](BIBLE.md#IP-LAW-1)'s fail-closed doctrine from the
gates to the condition layer itself: every condition whose inputs are absent or whose type is
unrecognized now blocks instead of passing. 183 tests green. No new laws.

## IP-A17 — Bug-hunt round 5: guard-bypass routes, quarantine-net leaks, daily-loss reset {#IP-A17}
**What changed.** (2026-07-19.) Fifth find-10-fix-10 sweep — mostly holes in the guards rounds 3
and 4 installed; ten fixes, seven with regression tests:

1. **`Invalidate()` still reset the daily circuit breaker** — the Settings page's post-edit hook
   dropped the whole cache entry, discarding the Guardian instance and its in-memory daily-loss
   counter — the exact hazard IP-A16's `UpdateConfig` was built to avoid, on the most common
   path (a mid-day risk edit). Invalidate now expires the config TTL in place; the instance and
   its recorded losses survive (LocalDB test `RiskGuardianServiceTests`).
2. **StrategyBuilder Save bypassed the PositionOpen guard** — the editor's update branch writes
   `IsActive` directly through `UpdateAsync`, so unchecking Active on a holding strategy
   orphaned the position without ever consulting IP-A16's `SetActiveAsync` guard. The Save path
   now refuses to deactivate a row with `PositionQty > 0`.
3. **Strategies-page toggle bypassed the one-active-gapper-per-symbol rule** — gapper rows also
   render on `/strategies`, whose activate toggle had neither the duplicate-symbol check nor
   the post-write recheck: pause A on the Gapper tab, re-activate it from Strategies = doubled
   exposure on one gap. Same SQL pre-check + post-write revert as the Gapper page now.
4. **The exit deferral gate only checked the weekday, not the clock** — a SellBy decision at
   Friday 20:01 ET (or a weekday 02:00) still placed a regular-hours DAY sell that queues for
   the next open at a stale limit price — the precise hazard IP-A16's weekend gate described.
   Deferral now requires a weekday AND the 4:00–20:00 ET window Alpaca accepts orders in.
5. **The strict-JSON quarantine net leaked exceptions** — `GetDecimal` on an overflowing number
   (`1e30`) threw a raw `FormatException` that `StrategyLoader` doesn't catch, crashing the
   Monitor's evaluation loop instead of quarantining the row; `1e400` silently became
   `double.Infinity`; wrong-kind values (`"session": 5`, string quantities) silently coerced to
   defaults. All strict readers now fail closed with `StrategyJsonException` (present-but-wrong
   kind or unrepresentable value → quarantine; absent/null → null), per [IP-LAW-8](BIBLE.md#IP-LAW-8).
6. **The day replay built snapshots with zero EMA periods** — `GapperDayBacktester` claimed
   Monitor fidelity but passed an empty EMA set, so any EMA-conditioned strategy could never
   enter in replay (null EMAs fail every EMA condition). It now uses `EmaPeriodCollector`, the
   same walk the live paths use.
7. **The replay's peak window stopped at the exit** — making "the peak came AFTER your exit —
   this day rewarded more patience" mathematically unreachable dead code and understating MFE.
   The peak now runs to the hard sell-by (per its own doc); the trough (MAE) stays entry→exit.
8. **Inverted entry windows validated clean** — `GapperProfile.Validate` never checked window
   ordering; `TimeWindowCondition` evaluates start≥end as an overnight wrap, so an
   LLM-interpreted candidate with a swapped window becomes eligible outside the intended
   premarket slot. Cross-field ordering check added.
9. **A missing `equity` field rendered as a −100% day** — `AccountPill`'s new intraday P&L
   parsed absent/unparseable equity to 0 and happily computed (0 − last)/last. Both values must
   now parse positive or the pill shows 0%.
10. **`WorkspaceManager.Delete`/`LoadAll` mutated the cache outside the new gate** — the lock
    IP-A16-era work added covers Load/Save, but Delete removed from the same shared `List`
    unlocked and LoadAll assigned unlocked — the torn-state race the lock exists to prevent.
    Both now take the gate.

**Why.** Session directive 2026-07-19: "find 10 fix 10" (round 5).

**Effect on canon.** Hardens [IP-LAW-2](BIBLE.md#IP-LAW-2) (daily-loss counter survives config
edits end-to-end), [IP-LAW-8](BIBLE.md#IP-LAW-8) (the quarantine net no longer leaks), and the
IP-A16 mutation guards (no page-level bypass routes). 175 tests green. No new laws.

## IP-A16 — Bug-hunt round 4: fail-open LLM gate, weekend gate, orphaned positions {#IP-A16}
**What changed.** (2026-07-19.) Fourth find-10-fix-10 sweep; ten fixes with regression tests:

1. **LLM gate failed OPEN** — the Monitor only blocked on an explicit Reject consensus, so a
   dead voter panel (zero votes), unparseable votes (all Abstain), or a split below threshold
   all let the trade through, violating [IP-LAW-1](BIBLE.md#IP-LAW-1)'s "quorum approves".
   When voting is enabled the gate now requires an explicit **Approve** consensus; anything
   else blocks with an audited reason. `LlmVotingResult.Consensus` is also initialized to
   Abstain (Approve is enum zero — same fail-closed rule IP-A11 applied to `LlmVote`).
2. **No weekend gate anywhere** — the session windows are time-of-day only, so Saturday
   10:00 ET counted as "inside RTH": entries could fire against Friday's stale bars and queue
   orders for Monday's open, and a held position's SellBy decision tripped every tick all
   weekend (order spam / stale queued sells). New `MarketTime.IsEquityTradingDay` (ET
   weekday; tested incl. the UTC-rollover edge) gates both the entry session check and the
   exit order placement — a weekend exit decision defers, visibly, to the next weekday.
3. **Risk-config edits never reached the Monitor** — `RiskGuardianService` cached each user's
   Guardian for the process lifetime and the UI's `Invalidate()` only clears the *UI
   process's* cache; the Monitor (a separate process) kept trading on stale limits until
   restart. Config now refreshes on a 2-minute TTL via new `RiskGuardian.UpdateConfig`,
   which swaps limits WITHOUT resetting the in-memory daily-loss counter (tested).
4. **Pausing/deleting a holding strategy orphaned the position** — the Monitor only evaluates
   `IsActive` rows, so deactivating (Strategies page, Gapper toggle) or deleting a row with
   `PositionQty > 0` silently killed all exit management for shares the broker still holds.
   `SetActiveAsync`/`DeleteAsync` now return a `StrategyMutation` verdict and refuse
   `PositionOpen` at the repository; both pages surface the reason.
5. **Repository-level ownership** — the same mutators now require the caller's user id and
   refuse `NotOwner`, closing the write-IDOR class at the repo instead of relying on every
   page to pre-filter (defense-in-depth on top of IP-A15's StrategyBuilder fix). LocalDB
   integration suite `StrategyRepositoryGuardTests` pins all guards.
6. **Gapper duplicate-symbol check was poll-stale** — it consulted the in-memory page list
   (up to 5s old), so two tabs could double-queue the same ticker and double the exposure on
   one gap. The check now hits SQL (`CountActiveForSymbolAsync`).
7. **3-gapper cap TOCTOU closed** — queueing and re-activation re-verify the cap AND the
   duplicate rule AFTER flipping the row active, reverting their own activation on
   violation (no transaction spans create+activate; the post-write recheck enforces the
   invariant deterministically).
8. **Re-activation skipped the one-per-symbol rule** — pause A, queue B same ticker,
   re-toggle A = two active gappers on one gap. The toggle now applies the same SQL check.
9. **FireAsync dropped T2/T3 from the voter panel's view** — the Monitor's own signal
   construction had the same T1-only `Targets` defect fixed in `DslStrategy` (IP-A15); the
   LLM panel judged risk:reward on a truncated exit plan. Full ladder now.
10. **Silent key-decryption failures** — `UserKeyService.Unprotect` swallowed all
    DataProtection failures as `null`, so a key-ring mismatch between Blazor and the Monitor
    read as "never configured" while broker routing silently fell back to the global default.
    Failures now log the field, user, and probable cause.

**Why.** Session directive 2026-07-19: "find 10 fix 10" (round 4).

**Effect on canon.** Strengthens the enforcement of [IP-LAW-1](BIBLE.md#IP-LAW-1) (the LLM
gate is now fail-closed end-to-end) and [IP-LAW-2](BIBLE.md#IP-LAW-2) (live config refresh).
[BIBLE §6](BIBLE.md#IP-§6) build evidence updated (168 tests green). No new laws.

## IP-A15 — Bug-hunt round 3: Legion provider-id drift, IDOR, backtest/live divergences {#IP-A15}
**What changed.** (2026-07-19.) Third find-10-fix-10 sweep; ten fixes, each with a regression
test where one is expressible:

1. **Describe tab was dead** — `StrategyScriptGenerator` still called Legion with
   `providerId: "claude"`; Legion 20 renamed the Anthropic provider to `"claude-api"` and the
   old id is gone from `LlmProviderCatalog`. Fixed; `LegionProviderContractTests` now pins the
   id and the default model against the live catalog so the next rename fails a test instead
   of a runtime feature.
2. **legion.json voter panel silently lost Claude** — `voters`/`judge` still said `"claude"`,
   which the `AllowedProviderIds` filter drops as untrusted. Updated to `"claude-api"`.
3. **Stale model pins** — `AppSettings.LlmVoterModel` default, the interpreter/generator
   fallbacks, and the Technical Analyst voter persona moved `claude-sonnet-4-6` →
   `claude-sonnet-5` (Legion's current catalog default; same list price, current generation).
   The two haiku personas stay on the catalog-valid cheap tier.
4. **Write-IDOR on strategies** — `/builder/{id}` checked ownership only when *displaying*;
   `Save()` re-resolved the route id unchecked, so any logged-in user could overwrite another
   user's strategy row. Fixed twice over: foreign ids are dropped at page init AND `Save()`
   re-checks `OwnerUserId` before the update branch.
5. **Backtest ≠ live on scale-outs** — `DslStrategy` emitted only `TakeProfitPrice` (T1) into
   the live `TradeSignal.Targets` while the backtester honored the full `TakeProfitTargets`
   ladder; `.TakeProfit(t1, t2, t3)` strategies silently never scaled out live. The signal now
   carries the full ladder (`DslStrategySignalTests`).
6. **EMA collector triplication** — `DslStrategy`, `MonitorWorker`, and `StrategyBacktester`
   each hand-rolled "which EMA periods does this strategy reference"; the backtester's copy
   skipped `ConditionalBlocks`, so branch-only EMA references replayed against missing series.
   Single source of truth now: `IdiotProof.Scripting/EmaPeriodCollector.cs`
   (`EmaPeriodCollectorTests`).
7. **Tuned-profile name stacking** — re-backtesting an applied tuned profile produced
   "X (tuned) (tuned)". Suffix is now idempotent (`GapperDayBacktesterTests`).
8. **Learn → Builder deep links were dead** — the "Open in Builder" links passed
   `?seed=&ticker=` that `StrategyBuilder.razor` never read. The builder now honors both via
   `[SupplyParameterFromQuery]`.
9. **Account pill always showed "+0.00%"** — `DayChangePercent` was hardcoded `0m`. Now
   computed from Alpaca's `equity` vs `last_equity`.
10. **Workspace cache lied under SQL** — the eager `LoadAll()` at startup targeted the legacy
    `__global__` bucket, which `SqlWorkspaceStore`'s Guid-gate silently no-ops (an ephemeral
    Default tab was fabricated each boot and nothing persisted); `Save()` for a never-loaded
    user seeded the cache empty and hid that user's other tabs; concurrent first reads could
    double-seed. Startup is now lazy per user, `Save()` hydrates from the store first, and
    seeding is serialized (`WorkspaceManagerTests`).

**Why.** Session directive 2026-07-19: "find 10 fix 10" (round 3) + "document changes and
update any guides."

**Effect on canon.** No law changes. [BIBLE §6](BIBLE.md#IP-§6) build evidence updated
(156 tests green). Fix 1/2/3 are the recorded prerequisite for the deferred "Legion native
voter-panel API migration" debt in [BIBLE §7](BIBLE.md#IP-§7).

## IP-A14 — Gapper day replay: backtest a past day, examine, re-dial, reuse {#IP-A14}
**What changed.** (2026-07-19.) The Gapper tab gained **"Backtest a day"**: pick a ticker +
date, and `GapperDayBacktester` (`IdiotProof.Strategies/GapperDayBacktester.cs`) replays the
current dial-ins over that day's bars — premarket included — answering "what WOULD have
happened".

- **Fidelity rule:** entries walk the same condition list the Monitor walks (with real
  previous-close gap math), and exits run the SAME `GapperExitEvaluator` the live console
  runs, bar by bar. Fill price = decision bar's close (the honest analogue of the live
  marketable-limit orders). A backtest that runs different code than live is a lie; this one
  can't drift because it calls the live brain.
- **Examine:** the report carries entry/exit fills with reasons, P&L, gap-at-entry, max
  favorable/adverse excursion with timestamps, a **giveback grid** (what every giveback dial
  from 10–50% would have done on the same day), and plain-English hindsight suggestions
  ("peak came AFTER your exit — this day rewarded more patience"; "your stop was never
  close — tighten to ~4%").
- **Re-dial and reuse:** the report includes a **tuned profile** — hindsight-best giveback, a
  stop informed by the day's real adverse excursion (never loosened), gap screen set just
  under the day's actual gap — with an *Apply tuned dials* button that loads it into the
  manual form, ready to queue for a real trading day. Human applies; nothing auto-tunes.
- **Data:** Alpaca (global settings chain, premarket bars via time-range requests + daily
  previous close) when keyed; the deterministic Mock gap day otherwise, so the whole loop is
  rehearsable keyless. No-entry days report the exact blocking condition; missing previous
  close fails closed, same as live.

**Why.** Session directive 2026-07-19: "make sure the alpaca API can run backtesting … design
a strategy for a day in the past and see what WOULD have happened … determine how it could
have been even better and then take that 'strategy profile' and use it again on a real
trading day and dial it in for that specific ticker."

**Effect on canon.** New story IP-US-K12 with the replay/grid/tuning test suite. The generic
`/backtest` page (RTH strategies) is unchanged; gapper replay lives with the gapper.

## IP-A13 — Canonical strict-JSON strategy layer; IdiotScript demoted to a view (new law IP-LAW-8) {#IP-A13}
**What changed.** (2026-07-19.) The fluent DSL text is no longer the machine format.

- **Canon:** `StrategyJson` (`IdiotProof.Scripting/StrategyJson.cs`) round-trips the full
  `StrategyDefinition` — every condition type including `.And()/.Or()/.Not()` composition and
  `.Then()/.Else()` branching, which the text round trip structurally loses — as
  `schemaVersion: 1` JSON in a new `Strategy.ScriptJson` column (migration
  `AddStrategyCanonicalJson`). Deserialization is fail-closed must-understand: unknown
  version/condition-type/property → `StrategyJsonException`.
- **One materialization path:** `StrategyLoader.Load(scriptJson, scriptText)` — canon first;
  a present-but-rejected canon **quarantines** the row (Monitor logs it and writes
  `(invalid strategy: …)` to ConditionProgress; the Backtest page surfaces it) and expressly
  does NOT fall back to the tolerant text parse. Text parse remains only for legacy rows with
  no canon; a startup backfill derives canon for those once.
- **Zero-parse gapper path:** the Gapper tab serializes the factory's semantic model directly
  to canon; the script text is generated from the same model purely for humans. Editor/Describe
  flows still originate as text (canon derived via the tolerant parser — no worse than before);
  migrating the Describe tab's LLM output to model-JSON and the strict Roslyn parser
  (IP-US-H1) are the recorded tail.
- **Verified live:** startup backfilled the legacy row; a canon tampered with an unknown
  property (`hostileField`) was refused by the running console with
  "refusing to guess at its meaning" and never fell back to text.

**Why.** Session question 2026-07-19: "is the DSL language into a fluent api the best way …
or should there be a structured JSON layer … the last thing I want is for instructions to get
misinterpreted because the language is too 'helpful'." Expert consensus applied: Fowler's
semantic-model pattern (persist the model, not surface syntax), langsec's rejection of
tolerant machine-boundary parsers, "parse, don't validate", and vendor guidance that LLM
output be schema-constrained JSON.

**Effect on canon.** New law [IP-LAW-8](BIBLE.md#IP-LAW-8). New story IP-US-K11 with the
round-trip + fail-closed test suite. `ScriptText` stays as the human view (IP-LAW-4's
reflected verb catalog is unaffected).

## IP-A12 — Transcript → gapper interpreter {#IP-A12}
**What changed.** (2026-07-19.) The Gapper tab gained a **"From a transcript"** panel: paste
any natural language (typically a video transcript) and `GapperInterpreter`
(`IdiotProof.Blazor/Services/GapperInterpreter.cs`) asks Claude — through MindAttic.Legion,
HOUSE-LAW-4 — to extract premarket gap plays as a STRICT JSON array of
`{symbol, rationale, profile{...}}` against the live Classic-Gapper defaults (the system
prompt is written from the actual catalog profile so it can't drift). The response is
re-validated fail-closed in a pure, unit-tested parse layer: symbols must match
`^[A-Z]{1,6}$`, profile overlays change only fields actually present in the JSON,
`GapperProfile.Validate` rejects impossible dial-ins, and at most 5 candidates survive.
Candidates render as **review cards** — rationale, dial summary, generated IdiotScript —
with per-card *Queue* (respects the 3-active cap) and *Load into dials* (hand-tweak first).
**A transcript can never queue itself**; a human clicks every queue.

**Why.** Session directive 2026-07-19: "I get a transcript from a video and I want you to
interpret that transcript and build the gapper strategies … a dedicated open text spot where
I can just enter natural language which you interpret using claude into a strategy."

**Effect on canon.** New story IP-US-K10 (🟡 — parse contract ✅-tested; live round trip +
Cypress ⬜). No law changes; the three gates still guard every fire downstream.

## IP-A11 — Second find-10-fix-10 pass: sixteen more defects across the wider codebase {#IP-A11}
**What changed.** (2026-07-19, second self-review round — this one swept beyond the fresh
IP-A8/A9 code into the DSL round trip, backtester, UI pages, and long-run resource behavior.)

1. **Backtester time exits fired on the UTC clock.** `StrategyBacktester.ManageOpenBar`
   compared `ExitTime` against `bar.StartUtc.TimeOfDay`, but `SellBy("09:28")` is Eastern
   everywhere else (DSL, Monitor, `GapperExitEvaluator`) — a backtested gapper time-exited at
   09:28 UTC = 05:28 ET, hours early, silently corrupting every backtest using a time exit.
   Now compares via `MarketTime.ToEasternTimeOfDay`. *(test: `TimeExit_FiresOnEasternClock_NotUtc`.)*
2. **`ScriptParser` silently dropped `Name(...)`** — the serializer always emits it; there was
   no parser case, so a strategy's display name was lost on every round trip. *(asserted in
   `GapperScriptFactory_Script_SurvivesParserRoundTrip`.)*
3. **`UserBrokerResolver` still leaked the old client on the user→global transition** — the
   IP-A10 disposal fix only covered key rotation; a user who UN-configured their Alpaca keys
   left their old client's `HttpClient` undisposed. Disposal now centralized for both paths.
4. **A malformed websocket frame tore down the Alpaca stream.** `HandleMessage` let JSON/shape
   exceptions escape into the receive loop → full disconnect/reconnect cycle (seconds of lost
   coverage) over a frame that would have been ignored anyway. Frames are now individually
   fault-isolated.
5. **`GapperProfile.Validate` accepted arm-time ≥ sell-by** — a dial-in whose momentum-rollover
   exit could never fire (the hard flatten always preempts it). Cross-field check added.
   *(test: `GapperProfile_Validate_RejectsArmTimeAtOrAfterSellBy`.)*
6. **The Strategies page preview rendered an empty shell for every strategy** —
   `TryParse` built a bare `Ticker()` definition instead of parsing the script, so the
   expand-row "preview" showed no conditions/stops/targets. Now uses `ScriptParser.ParseScript`.
7. **Monitor caches grew forever** — `candleCache`/`previousCloseCache` never evicted symbols
   with no active strategy; weeks of queue/remove cycles accumulate 240 bars per stale ticker
   in a process designed never to restart. Stale entries evicted each tick.
8. **Write-only `OpenStrategyTabs` state removed** (audit H6 resolved): the
   `AddOpenTabAsync`/`RemoveOpenTabAsync`/`GetOpenTabsAsync` trio fed a CSV column for a
   `BuilderTabBar` that was never built — unbounded per-user growth, zero readers. Write path
   deleted; column removal rides the next schema migration.
9. **3× CS8629 in `IdiotScript.cs` eliminated** — the tuple-of-`HasValue` switch couldn't prove
   non-null; rewritten as direct nullable patterns. Solution now builds **warning-free**.
10. **CS8601 in `UserBrokerResolverTests` eliminated** — same warning-free goal.

A parallel review agent surfaced six more, all verified and fixed in the same pass:

11. **LLM votes failed OPEN to Approve.** `VoteDecision.Approve` is the enum's zero value, so a
    vote whose `"decision"` key was missing — or cased `"Decision"` (the property lookup was
    case-sensitive) — silently counted as an **approval** at the LLM gate. Now: `LlmVote.Decision`
    initializes to Abstain and property lookups are case-insensitive. *(tests:
    `ParseVoteJson_MissingDecisionKey_FailsClosedToAbstain`,
    `ParseVoteJson_CapitalizedPropertyNames_StillParse`.)*
12. **The API Keys page reset broker routing on every save AND the per-user Alpaca routing was
    unreachable.** `SaveAll` rebuilt the whole `UserApiKeys` row without `DefaultBroker`/
    `DefaultDataFeed` (stomping them to "Sandbox"/"Mock"), and no UI ever set
    `DefaultBroker = "alpaca"` — making IP-A9's per-user routing permanently inert. The page now
    has an explicit "Route my orders to this Alpaca account" toggle and preserves both fields.
13. **The Backtest page hardcoded the EDT offset** (13:30–20:00 UTC as "09:30–16:00 ET") — a
    winter backtest replayed 08:30–15:00 ET, pulling in an hour of premarket and dropping the
    final hour including the close. Session bounds now convert through the real Eastern zone.
14. **`PolygonDataFeed` swallowed HTTP errors and ignored pagination** — an auth failure or
    rate-limit rendered as "0 bars / no triggers", and any window over 50,000 rows silently
    truncated to the first page. Non-2xx now throws with status + body; `next_url` is followed.
15. **`SqlWorkspaceStore.Save` skipped the ownership check `Delete` enforces** — a bare
    primary-key lookup on an 8-hex-char TabId (32 bits; cross-user collisions plausible) could
    silently overwrite another user's workspace body. Lookup now scoped to the owner.
16. **First-visit race in `UserPreferencesService.GetOrCreateAsync`** — two concurrent first
    requests both inserted; the loser threw an unhandled `DbUpdateException`. The loser now
    detaches and returns the winner's row.

**Why.** Session directive: second "find 10 fix 10" round.

**Effect on canon.** No law changes. Indicator math (Stochastic, OBV, Bollinger, CCI,
Williams %R) was line-checked in this round and found correct — the ADX Wilder-seed defect
fixed in IP-A8 was the only real math bug. §6 verified-state counts updated.

## IP-A10 — Seven bugs found and fixed in the IP-A8/A9 pipeline {#IP-A10}
**What changed.** (2026-07-18/19, self-review pass.) Line-by-line re-audit of the Monitor
pipeline and per-user broker routing shipped in IP-A8/A9 surfaced seven confirmed defects,
all now fixed:

1. **`UserBrokerResolver` leaked `HttpClient`s on key rotation.** When a user's Alpaca keys
   changed, the old `AlpacaBrokerClient` was silently dropped from the cache without disposing
   its `HttpClient` — a slow socket leak over a console process's multi-week lifetime. Now
   disposed on replacement.
2. **`MonitorWorker.FireAsync` computed stop/target as always-long**, placing a short
   strategy's "stop" below entry regardless of direction. `RiskGuardian` correctly rejects a
   wrong-side stop — meaning every short-direction strategy was silently blocked before it
   ever reached the "signal recorded" path. Stop/target are now direction-aware.
3. **The short-signal path had no per-day dedupe.** Once conditions matched, a qualifying
   short candidate re-fired and re-wrote an audit-log row every tick (every 5s) indefinitely.
   Fixed by stamping the same `EntryFilledUtc` day-guard a real fill uses (with `PositionQty=0`,
   since no order is placed for shorts yet).
4. **The Gapper tab's "max 3 active gappers" cap was client-side only.** `ActiveGapperCount`
   was computed from a list that's only as fresh as the last 5s poll; two browser tabs (or two
   rapid clicks) could both pass the check and exceed the cap. `QueueGapper`/`ToggleActive`
   now re-query SQL for the true active count immediately before activating.
5. **`RiskGuardian.RecordTradePnL` never rolled the daily-loss counter over.** Only
   `ValidateTrade` did. An exit landing before any `ValidateTrade` call on a new trading day
   (a position held overnight, closed before a new entry is evaluated) added its loss onto a
   stale prior-day total — able to falsely trip today's circuit breaker with yesterday's
   numbers. `RecordTradePnL` now shares the same rollover check.
6. **The synthetic "live tick" candle zeroed volume.** `AppendLiveTick` (added in IP-A9 to make
   exits reactive between minute bars) stamped the synthetic candle's `Volume = 0`; when it was
   the newest bar, `IndicatorSnapshotBuilder`'s `VolumeRatio` computation went to zero,
   spuriously failing `IsVolumeAbove` — and therefore every gapper's volume screen — exactly
   when live data should sharpen evaluation, not break it. Volume is now carried forward from
   the last real bar.
7. **`IMarketDataFeed.GetPreviousCloseAsync`'s date comparison ran daily-bar timestamps
   through an Eastern-time conversion**, which shifts a UTC-midnight-stamped bar back to the
   *prior* ET calendar date (UTC midnight ≈ 7-8PM ET the day before) — a systematic day-shift
   in the "which day is this bar" comparison. Bar dates now compare on their raw UTC date (a
   "1Day" bar already denotes one whole session); only "now" still converts through ET, since
   that's a genuine instant-in-time question.

**Why.** Session directive: "find 10 fix 10" — a self-audit pass on the money-path code that
had just shipped, before any of it saw live capital.

**Effect on canon.** No law changes; these are defect fixes within the IP-A8/A9 design, not
architecture changes. New regression tests: `RecordTradePnL_AsFirstEverCall_...`,
`RecordTradePnL_ThenValidateTrade_SameDay_...` (`IdiotProof.Engine.Tests/RiskGuardianTests.cs`),
`GetPreviousCloseAsync_UtcMidnightStampedDailyBars_PicksYesterdayNotToday`
(`IdiotProof.Strategies.Tests/GapperLifecycleTests.cs`). Items 2/3/6 have no isolated unit test
(they live in `MonitorWorker`'s private methods, exercised only by live console runs today) —
recorded as a test-coverage gap, not silently left unverified.

## IP-A9 — Production hardening: per-user broker routing, service hosting, leader lease, DP key-ring readiness {#IP-A9}
**What changed.** (2026-07-18, follow-on to [IP-A8](#IP-A8).)

- **Per-user broker routing.** The Monitor no longer routes every user's orders through one
  global broker account. `UserBrokerResolver` (`IdiotProof.Blazor/Services/UserBrokerResolver.cs`)
  resolves each strategy owner's broker: users who opted into Alpaca on the API Keys page
  (both keys present, `DefaultBroker = "alpaca"`) trade **their own** account with their own
  paper/live flag; everyone else falls through to the global router whose default is Sandbox
  (IP-LAW-3) — so a missing/undecryptable key can never route one user's money into another's
  account. Clients are cached 5 min per user; key rotations in the UI take effect without a
  console restart. The pure routing rule is `UserBrokerResolver.Choose` (unit-tested).
- **Shared Data Protection key ring.** The Monitor configures Data Protection with the same
  app name ("IdiotProof") and key-ring location as the Blazor host (dev:
  `%APPDATA%\MindAttic\DataProtection\IdiotProof`, matching MindAttic.Authentication's
  `DevKeyRingPath` convention; prod: `DataProtection:KeyRingPath` config in BOTH hosts) so
  the console can decrypt the per-user API keys the UI writes.
- **Production boot unblocked.** `Program.cs` now supplies MindAttic.Authentication's required
  `ConfigureDataProtection` hook when `DataProtection:KeyRingPath` is configured (durable,
  instance-shared storage — on Azure App Service, `%HOME%\data\dp-keys`). Without the config
  key, production still fail-closes — the library's intended posture. Azure Blob + Key Vault
  key-ring protection remains the upgrade path once infra exists.
- **Single-active-instance lease.** `MonitorLeaderLease` (session-owned `sp_getapplock` on
  `IdiotProof.Monitor.Leader`) guarantees at most one Monitor evaluates/trades per database;
  standby instances wait and take over automatically if the leader dies. Two Monitors can no
  longer double-fire orders.
- **Service hosting.** The Monitor registers `AddWindowsService` (service name
  `IdiotProof.Monitor`) — installable via `sc.exe create`, no-op when run interactively.

**Why.** Session directive 2026-07-18: "console operates as a service … separate; support
multiple simultaneous users; … production grade even if it never has any users."

**Effect on canon.** [BIBLE §4](BIBLE.md#IP-§4) Monitor row + verbs updated; §7 frontier's
"per-user keys" debt narrowed to LLM keys only. New story IP-US-K8. No law changes —
IP-LAW-3's sandbox-default now holds per user, not just per host.

## IP-A8 — Gapper epic + single-pipeline unification adopted {#IP-A8}
**What changed.** [RFC 0002](rfc/0002-gapper-and-unification.md) adopted (2026-07-18). The
premarket **Gapper** flow becomes the product's flagship: pick up to 3 tickers on a dedicated
`/gapper` tab, screen each against an adjustable **gapper profile** (gap %, volume ratio, price
band, entry window, stop/trailing-stop, peak-giveback rollover exit, arm time, hard sell-by
before the 9:30 bell), queue them as ordinary Strategy rows, and let the console Monitor buy
the gap and sell it off before the bell. Supporting canon changes:

- **Single pipeline:** Strategy SQL rows are the only strategy runtime state; the console is
  the always-on evaluator; UI changes propagate via SQL each tick (no restart); order
  execution flows through `BrokerRouter`/`IBrokerClient` behind the three gates. The rival
  `WorkspaceTab`-binding evaluation path in `StrategyExecutionService` is deprecated as an
  evaluation input (workspace tabs remain UI layout state).
- **DSL:** new verbs `RequireEntryWindow`/`EntryWindow`, `SellBy`, `PeakGiveback`; gap
  conditions now evaluate against a real `PreviousClose` (fail closed without it); parser
  round-trip fixes (`Session` was silently dropped; `ExitTime`/`StopLossPercent` never
  serialized).
- **Profiles:** first static JSON catalog under IP-LAW-7:
  `IdiotProof.Blazor/wwwroot/data/gapper-profiles.json` (templates; every value dialable per
  queued ticker; tuned result denormalized into `ScriptText`).
- **Exits:** momentum-rollover exit = give back N% of the entry→peak run, armed in the last
  premarket minutes; hard `SellBy` fallback so gappers are always flat before the bell.
  Exit orders are risk-reducing: audit-logged, no LLM panel, RiskGuardian kill-switch still
  honored.
- **Data:** config-driven feed selection (Alpaca when keyed, Mock fallback), Alpaca websocket
  streaming for subscribed symbols, daily-bar previous-close fetch for gap math. Premarket
  orders are limit + `extended_hours` on Alpaca.

**Why.** Session directives 2026-07-18: "this application only needs to do one thing well …
take a stock ticker at 4AM and check to see if it's a gapper … sell off the gapper before the
bell"; "make sure the console and the UI run asynchronously and communicate"; "need to be able
to dial in gappers … all gappers are not the same"; "do a full system audit … build it into a
single unified vision."

**Effect on canon.** [BIBLE §7](BIBLE.md#IP-§7) frontier gains the Gapper epic. New Epic K
added to [USER_STORIES.md](USER_STORIES.md). Stories flip ✅ only with named green tests
(HOUSE-LAW-8).

## IP-A1 — The README/copilot narrative describes a graph that is not the built solution (supersedes —)
**What changed.** The Codex canon ([BIBLE §4](BIBLE.md#IP-§4)) is anchored to the projects
actually referenced by `IdiotProof.slnx` (Blazor, Monitor, Engine, Scripting, Strategies,
Indicators, DataFeeds, Brokers, Models, Shared + three test projects). The pre-existing
`README.md` and `.github/copilot-instructions.md` describe a different, partly-historical shape:
an `IdiotProof.Core` "headless engine", an `IdiotProof.Web`/`IdiotProof.Cli` split, IBKR as the
primary broker, and an `IdiotProof.NUnitTests` suite as the canonical backend tests.

**Why.** On disk there is a large `IdiotProof.Core/` tree (Calculators/, Services/, Strategy/,
FutureState/, Documentation/*.htm, Profiles/, Data/), an `IdiotProof.Cli/`, an
`IdiotProof.Brokers.Ibkr/`, an `IdiotProof.Core.UnitTests/`, a `tests/IdiotProof.NUnitTests/`,
and an `IdiotProof.Scripting.Tests/` — **none of which are referenced by `IdiotProof.slnx`** and
none of which build or run as part of the solution. The README's NUnit "canonical backend"
section and the copilot "Core/Web" architecture therefore do not match what `dotnet build/test
IdiotProof.slnx` actually exercises. The verified state ([BIBLE §6](BIBLE.md#IP-§6)) reflects only
the in-solution projects.

**Resolution / migration.** ~~Until RFC 0001 is decided, treat as legacy/dormant.~~ See
[IP-A2](#IP-A2) — RFC 0001 resolved 2026-06-07: all out-of-solution trees deleted.

## IP-A2 — Out-of-solution trees deleted; README pruned to match `IdiotProof.slnx` (supersedes IP-A1 open question) {#IP-A2}
**What changed.** [RFC 0001](rfc/0001-core-tree-reconciliation.md) was resolved 2026-06-07 by
deleting all out-of-solution trees: `IdiotProof.Core/`, `IdiotProof.Cli/`,
`IdiotProof.Brokers.Ibkr/`, `IdiotProof.Core.UnitTests/`, `tests/IdiotProof.NUnitTests/`,
`IdiotProof.Scripting.Tests/`, `src/`, and the loose `__rescue_*.cs` / `__rescue_*.idiot`
root-level artifacts. The `README.md` project-layout tree, component table, and Tests section
were pruned to match what `IdiotProof.slnx` actually builds and tests.

**Why.** The dead trees made "what is the build?" ambiguous; the README described an architecture
that had not been the active graph for some time. Deleted trees are recoverable from git history.

**Effect on canon.** [BIBLE §4](BIBLE.md#IP-§4) dormant-tree scope note is superseded — there
are no longer any out-of-solution sibling projects at the repo root. The five test projects in
the solution (Engine, Indicators, Strategies, Brokers, Blazor) are the complete test surface.
This amendment closes the open question in [IP-A1](#IP-A1) and marks
[IP-US-F1](USER_STORIES.md) as ✅.

## IP-A3 — SQL-backed workspace store adopted in Blazor host; JSON-on-disk demoted to fallback {#IP-A3}
**What changed.** `SqlWorkspaceStore` (`IdiotProof.Blazor/Services/SqlWorkspaceStore.cs`) now
implements `IWorkspaceStore` backed by the `Workspaces` SQL table. It is registered in
`Program.cs` before `AddIdiotProofEngine` so it wins over the JSON-on-disk `TryAddSingleton`
default in the engine. The `Workspaces` table was re-created by migration
`20260608042532_RestoreWorkspaces` (it had been dropped in `TrimUiCruftSchema`). The JSON store
remains as a one-shot import path: on first load for a user, if SQL has no rows but disk files
exist, `SqlWorkspaceStore` copies them into SQL so existing workspaces migrate transparently.

**Why.** Workspace tabs are runtime state, not static config — [IP-LAW-7](BIBLE.md#IP-LAW-7)
requires them in SQL. The JSON-on-disk path was a temporary default; the Blazor host was always
intended to override it once the SQL store existed.

**Effect on canon.** [BIBLE §7](BIBLE.md#IP-§7) "Engine adoption of SQL workspaces" frontier item
is resolved. [IP-LAW-7](BIBLE.md#IP-LAW-7) is now fully enforced for workspace state.

## IP-A4 — FakeLlmHandler test seam added; Cypress suite expanded to 7 specs (E1–E6) {#IP-A4}
**What changed.** `FakeLlmHandler` (`IdiotProof.Blazor/Services/FakeLlmHandler.cs`) is a
server-side `HttpMessageHandler` that intercepts Legion/Anthropic calls when the host starts
with `IDIOTPROOF_FAKE_LLM=1` (Development only). It is registered in `Program.cs` as a
`LegionClient` override. Because Cypress can only intercept browser-side HTTP, the seam must
live server-side. The handler returns Anthropic-Messages-shaped JSON whose `content[0].text`
is the IdiotScript extracted from a `[[script: ...]]` marker in the request body, or a
default reclaim chain on the ticker named in the message if no marker is present.

The Cypress suite grew from 2 specs to 7:
- `02_strategies_describe.cy.ts` — updated: uses `FakeLlmHandler`; adds activate-toggle persistence test (IP-US-E1).
- `03_api_keys.cy.ts` — updated: adds credential masking and Save-All persistence tests (IP-US-E3).
- `04_vault_backed_ai.cy.ts` — new: proves the page → Legion credential-chain wiring; always-on via fake seam; live variant opt-in with `CYPRESS_LIVE_LLM=1` (IP-US-E4).
- `05_build_samples.cy.ts` — new: builds NCI Breakout-Pullback, ERNA AH Momentum, SUNE Wedge Breakout via the UI (IP-US-E5).
- `06_backtest.cy.ts` — new: exercises the `/backtest` page — select strategy, pick date, run, assert results panel (IP-US-E6).
- `07_condition_progress.cy.ts` — new: exercises the Strategies-page live ConditionProgress badge using the `seedConditionProgress` Cypress task (direct SQL via `sqlcmd`) to stand in for a Monitor tick (IP-US-E2).

`cypress.config.ts` adds the `seedConditionProgress` task (upserts a `ConditionProgress` row
directly into the LocalDB via `sqlcmd`, mirroring what `ConditionProgressRepository.UpsertAsync`
writes per tick). `cypress/support/commands.ts` adds `visitInteractive` (waits for Blazor
Server's SignalR circuit negotiate before interacting) and `typeStable` (asserts the typed
value stuck after the circuit re-render).

**Why.** The 🟡 E-stories (E1–E3) needed a broader, deterministic harness. The `FakeLlmHandler`
removes the live-key dependency so CI runs are self-contained. New stories E4–E6 were added to
[USER_STORIES.md](USER_STORIES.md) to cite the new specs.

**Effect on canon.** [BIBLE §6](BIBLE.md#IP-§6) updated to reflect the expanded suite and
FakeLlmHandler seam. [BIBLE §7](BIBLE.md#IP-§7) Cypress CI run item updated. No law changes —
the seam is Development-only and never bypasses real credential resolution in Production.

## IP-A5 — Adopt MindAttic.Authentication; migrate UserId columns to Guid {#IP-A5}
**What changed.** Replaced `Microsoft.AspNetCore.Identity.EntityFrameworkCore` with
`MindAttic.Authentication v2.0.0` (org-standard, used by Prose, MindAttic.Ideas, Tutor).

- **Data model:** `AppUser.cs` deleted. `AppDbContext` changed from `IdentityDbContext<AppUser>`
  to `DbContext, IAuthDataContext`; 8 `auth`-schema tables configured via
  `b.ApplyMindAtticAuthConfiguration()`. `UserId`/`OwnerUserId` on all SQL entities migrated from
  `nvarchar(450)` to `uniqueidentifier` (Guid). EF migration:
  `Migrations/…AdoptMindAtticAuthentication.cs`.
- **Services:** `GetAllForUserAsync`, `GetOrCreateAsync`, `GetForUserAsync`, `LogAsync`, etc.
  now accept `Guid userId` (or `Guid?`). `SqlWorkspaceStore` keeps the `string userId`
  `IWorkspaceStore` contract and parses to Guid internally. Non-SQL paths (`TradingStateService`,
  `TradeSignal.UserId`) remain `string` and receive `userId.ToString()`.
- **Auth endpoints:** Login.razor posts to `/_ma-auth/login` (field renamed `userName`).
  `/logout` is handled by the library. `/register-submit` and `/forgot-password-submit` call
  `IUserAdminService.CreateAsync` / `ResetPasswordAsync` respectively. `Program.cs` calls
  `AddMindAtticAuthentication<AppDbContext>` + `app.UseMindAtticAuthentication()` +
  `app.MapMindAtticAuthEndpoints()`.
- **EF packages:** Upgraded from `9.0.*` to `10.0.*` in `IdiotProof.Blazor` and
  `IdiotProof.Monitor` to match the MA runtime dependency.

**Why.** "If this has users it needs MindAttic.Authentication" — session directive 2026-06-09.
Standardises auth across the MindAttic family; gains Argon2id+pepper hashing, per-session revoke,
MFA/TOTP scaffolding, account lockout, and `AuthAuditLog`.

**Effect on canon.** [BIBLE §4](BIBLE.md#IP-§4) architecture table updated to show
MindAttic.Authentication as the identity/auth layer. [BIBLE §6](BIBLE.md#IP-§6) build
evidence line updated.

## IP-A7 — Learning Center + Backtest UI enhancement implemented {#IP-A7}
**What changed.** The two planned epics from [IP-A6](#IP-A6) are now built:

- **Learning Center** (`/learn` page, IP-US-I1…I5 built):
  - `Learn.razor` at `IdiotProof.Blazor/Components/Pages/Learn.razor`.
  - Workflow overview diagram (five-step HTML/CSS flow), six-phase IdiotScript walkthrough
    with reflected verb catalog (`StrategyScriptGenerator.GetVerbsByPhase()` —
    IP-LAW-4), three-gates visual diagram, annotated example strategies (NCI/ERNA/SUNE)
    with "Open in Builder" links, "Try it" contextual links per section.
  - Nav tab added to `MainLayout.razor`.
  - `StrategyScriptGenerator.GetVerbsByPhase()` and `GetConditionCatalog()` (internal static)
    added — verbs reflect from live `StrategyBuilder` + `Conditions` types; phase assignment
    uses method-name-prefix grouping so new verbs auto-classify.

- **Backtest per-candle condition table** (IP-US-J1…J3 built):
  - `CandleConditionRow` record added to `BacktestReport.cs`; `BacktestReport.ConditionTable`
    populated by `StrategyBacktester.Run()` on every bar.
  - `Backtest.razor`: injects `UserKeyService`, uses `PolygonDataFeed` when a Polygon API key
    is set (falls back to `MockDataFeed`), shows data-source banner, renders a collapsible
    per-candle condition pass/fail table with ✅/❌ per entry condition and a "fire" chip on
    the bar that opened a trade.
  - `UserApiKeys.PolygonApiKey` column added; EF migration `AddPolygonApiKey` created.
  - `ApiKeys.razor`: Polygon.io section added for entering the key.

**Why.** Session directive 2026-06-10 (implementation follow-through for IP-A6).

**Effect on canon.** IP-A6 planned epics I and J are now partially implemented (stories
remain `⬜` until the Cypress suite proves them end-to-end). No law changes.

## IP-A6 — Learning Center + Backtest UI enhancement planned {#IP-A6}
**What changed.** Two new planned epics added to [USER_STORIES.md](USER_STORIES.md):

- **Epic I — Learning Center** (IP-US-I1…I5, all ⬜): In-app documentation hub teaching users
  how to create and monitor a strategy. Verb catalog and phase reference rendered from live
  reflection (IP-LAW-4), not hand-authored.
- **Epic J — Backtest UI enhancement** (IP-US-J1…J3, all ⬜): Full-depth backtest that fetches
  a day of historical candles, evaluates the strategy tick-by-tick, and renders a per-candle
  condition table plus hypothetical P&L. Enhances the `StrategyBacktester.Run()` / `BacktestReport`
  pipeline already in `IdiotProof.Strategies`.

**Why.** Session directive 2026-06-09: "build a learning center" and "create backtesting into
the UI which allows you to run a strategy past a day's worth of data."

**Effect on canon.** [BIBLE §7](BIBLE.md#IP-§7) active frontier updated. No law changes.
