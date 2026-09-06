---
codex: 1
project: IdiotProof
code: IP
layer: rfc
status: active
updated: 2026-09-05
---

# RFC 0004 — Manual options section: buy the idea, sell the hype

## Problem
Session directive 2026-09-05 (verbatim intent, condensed): "Options are really confusing, but
they don't need to be; if they were just presented in a clear way so that it would be easy to
put CALLS and PUTS without having to infer the actual premium cost and break even and all that."
The motivating trade from a desk chat: two Bloom Energy (BE) December calls bought in August and
sold a few weeks later for +30% when the stock spiked — never anywhere near the $248 breakeven.
The lesson the user drew: **the breakeven only matters if you hold to the last second of
expiration**; the real trade is "make a bet about the future and when things look brightest you
sell off — cash in when the HYPE is highest, don't wait for REALITY to come crashing down."
Extrinsic value (the idea) vs intrinsic value (the actual) is the whole mental model. A second
thread in the same chat: S&P 500 / S&P 100 joiners as "safe bets" (Bloom, Illumina; Simon
Property, Sandisk) — index inclusion as a mechanical, pre-announced catalyst.

Current reality (audit, 2026-09-05): IdiotProof was equity-only end to end. `IBrokerClient`,
`OrderRequest`/`Position`, `AlpacaBrokerClient`, the DSL (`StrategyDefinition.Symbol` is a plain
ticker), `RiskGuardian` (linear stop-distance × shares) and every doc had no notion of a
contract, a strike, an expiration, or a right. `IdiotProof.UI` (the shared RCL from
[IP-A28](../AMENDMENTS.md#IP-A28)) was an empty template with no `ProjectReference` from the
web host. `ResearchClaim.ClaimType` had no value for an index-membership event.

## Scope decisions (locked with the user)
1. **Phase 1 = manual trading only.** No Strategy DSL changes, no Monitor auto-firing, no
   `RiskGuardian` changes. `IdiotProof.Scripting`, `StrategyJson.cs`, `RiskGuardian.cs`, and the
   `Conditions` catalog are untouched. The three gates ([IP-LAW-1](../BIBLE.md#IP-LAW-1)) govern
   *automated* fires; a user-initiated options order is outside the Monitor and is governed by
   the Paper/Live consent + elevation gates instead.
2. **Not a new Blazor app** — a new top-level **Options** nav section in the existing app,
   deliberately separate from the Stock Strategy pages. Components live in `IdiotProof.UI`
   (dual-host rule); MAUI stays frozen, only the Blazor Server host is wired.
3. **Black-Scholes in scope** as a self-contained calculator (theoretical value + implied-vol
   solver), used as a cross-check next to — and a fallback for — Alpaca's own server-side Greeks.
4. **"Should I sell now?" is informational.** Extrinsic value near its recent high + recent
   bullish `ResearchClaim` rows for the underlying → a calm callout, never an order.
5. **Index add/delete signal ships alongside**, as a hand-maintained JSON log →
   `IndexEventScanner` → `ResearchClaim(ClaimType = "IndexEvent")`. No fake live feed.
6. **Duplicate** the Live password-elevation modal for options; extracting a shared component
   out of the live `Strategies.razor` is a follow-up.
7. **Alpaca options entitlement is NOT yet confirmed** on the account. Everything below builds
   and tests against Sandbox; a real paper round-trip waits on `option_trading_level > 0`.

## Design

### D1. Models (`IdiotProof.Models`) — additive, equity paths untouched
`AssetClass { Equity, Option }`, `OptionRight { Call, Put }`. `OptionContract` (OCC symbol,
underlying, expiration, strike, right, multiplier = 100, tradable, OI) with `ParseOcc`/`BuildOcc`
— OCC symbols are self-describing (`ROOT` + `YYMMDD` + `C|P` + strike×1000 padded to 8), so
positions never need Alpaca to echo the structured fields. `OptionQuote` (bid/ask/last, IV,
`OptionGreeks`; IV/Greeks **null when the broker omits them** — 0DTE, missing inputs — no
guessing). `OrderRequest` and `Position` gain `AssetClass` (default Equity) and `Option?`.
**`Position.Quantity` for options is a contract count** (Alpaca's native unit); the UI multiplies
by `Multiplier` for dollars.

### D2. Broker layer (`IdiotProof.Brokers`)
`IBrokerClient` gains **default-implemented** members — `SupportsOptions`,
`GetOptionTradingLevelAsync`, `GetOptionChainAsync`, `GetOptionQuotesAsync` — so dormant
adapters compile untouched and `BrokerRouter` needs no restructuring. `AlpacaBrokerClient`:
- Contract catalog: `GET /v2/options/contracts?underlying_symbols=…&expiration_date=…` on the
  trading host, following `next_page_token`.
- Quotes + server-side Greeks/IV: `GET https://data.alpaca.markets/v1beta1/options/snapshots/{underlying}`
  — a **different host**, so a second `HttpClient` with the same credentials. Feed defaults to
  `indicative` (free); `OptionsDataFeed = "opra"` once the paid data plan exists.
- Orders: the same `POST /v2/orders`, with the OCC symbol, whole contracts only (notional is
  rejected locally with a plain message), DAY time-in-force, no extended hours, Market/Limit only.
- Positions: `asset_class == "us_option"` → `AssetClass.Option` + decoded `OptionContract`.
- Entitlement: `option_trading_level` from `/v2/account` (0 = not approved).
- A test seam (`internal` ctor taking an `HttpMessageHandler`) so payload shape and response
  parsing are asserted without the network.
`SandboxBrokerClient` serves a **synthetic chain** (strikes ±20% around a reference price in
sensible increments, four weekly expirations) and synthetic quotes (intrinsic + a time-value
hump; IV deliberately omitted so the UI's local-model fallback is exercised), and fills contracts
into the position book with the ×100 multiplier. This is what lets the whole page be built and
demoed with zero entitlement.

### D3. Pricing math (`IdiotProof.Shared/Options`) — pure, no I/O
- `IntrinsicValueCalculator`: intrinsic, extrinsic (floored at 0), extrinsic % of premium,
  breakeven, days-to-expiration, moneyness, and a one-call `Breakdown` bundle.
- `BlackScholesCalculator`: `C = S·N(d1) − K·e^(−rT)·N(d2)` (put via parity), vega, delta, and
  `ImpliedVolatility` — Newton-Raphson seeded by Brenner–Subrahmanyam with a bisection fallback
  when vega collapses; returns null when no solution exists (price below intrinsic, or above the
  underlying). N(·) via Abramowitz–Stegun 7.1.26. **Documented simplifications (v1):** European
  exercise assumed for American equity options; no dividend yield; constant vol/rate.
- `SellSignalEvaluator`: fires when current extrinsic is within 5% of its observed high **and**
  ≥1 Bullish claim for the underlying in the last 7 days. Pure function over a
  `BullishClaimSummary` projection so `IdiotProof.Shared` stays free of EF.
- Risk-free rate: `SettingsKv["Options.RiskFreeRate"]` (default 0.04), editable inline on the page.

### D4. UI (`IdiotProof.UI` RCL + thin host page)
`IdiotProof.Blazor` now references `IdiotProof.UI`; the RCL references only
Models/Brokers/Shared. Components are presentational — data in via parameters, actions out via
`EventCallback`s; the host owns data access, confirmation, and the Live gate.
- `OptionsChainView` — CALLS | strike | PUTS, one strike per row. Per cell: IV (badged
  `Alpaca`/`Model`), **hype $ / %** on a five-step colour ramp (real → all hype), **breakeven**,
  bid × ask, mid. ITM shaded, ATM marked. Expiration pills show DTE.
- `OptionOrderTicket` — buy/sell, contracts, market/limit; a plain-English summary ("you're
  buying 2 BE $38 calls for about $1,900; $500 of each contract's premium is hype…; breakeven
  $47.50 if held to expiration — you don't have to get there"). Alpaca `position_intent` is
  derived from the existing holding (`buy_to_open` / `sell_to_close` …). Locked with a banner
  when Alpaca reports level 0; red styling + REAL MONEY label on Live.
- `OptionPositionTracker` — qty (contracts), avg/now per share, P&L, a real/hype split bar, DTE,
  IV, Close (pre-fills the ticket with the opposite side), and the sell-signal callout row.
- `OptionsLiveElevationModal` — copy of the Strategies password gate; the host verifies via
  `LiveModeElevationService` and reuses its 5-minute window.
- `Options.razor` (`/options`) composes the above: account mode switch (Sandbox / Paper / Live —
  defaults to Paper only when Alpaca routing is opted in and keyed, otherwise Sandbox per
  [IP-LAW-3](../BIBLE.md#IP-LAW-3)), `AccountSummaryBar`, entitlement banner, 20-second
  position refresh with an in-session extrinsic history buffer for the signal.
- `OptionsTradingService` (scoped, host) resolves the broker under the **same consent rule** as
  `AccountSummaryService`/`UserBrokerResolver` (opted into Alpaca + full key pair; Vault first),
  gets the underlying price from `AlpacaDataFeed` (user keys → host keys → Sandbox reference,
  labelled), reads recent bullish claims, and holds the risk-free rate.

### D5. Index events (`IndexEventScanner`)
`wwwroot/data/sp-index-events.json` — announced add/remove entries (ticker, SP500|SP100,
Add|Remove, announcedDate, effectiveDate?, sourceUrl?, note), hand-edited when S&P announces a
change. The scanner (every ResearchScanner pass; also registered in the web host) writes one
`ResearchClaim` per entry: `ClaimType = "IndexEvent"`, `IsMacro = false` (it *is* about one
company), Bullish/High for an S&P 500 add, Bearish for a removal, Medium for S&P 100, `SourceTier`
1 with a press-release URL else 3, `HasHappenedAlready` false until `effectiveDate` passes, then
flipped to Realized. Idempotent per (ticker, summary). It reaches the position tracker's sell
signal through the ordinary bullish-claim join — no special casing.

## Verification
- `IdiotProof.Engine.Tests/OptionsPricingTests.cs` — OCC round-trips; intrinsic/extrinsic/
  breakeven for calls and puts; Black-Scholes vs textbook (S=K=100, T=1, r=5%, σ=20% → C 10.4506,
  P 5.5735), put-call parity, N(1.96); IV round-trips across moneyness/DTE; sell-signal cases.
- `IdiotProof.Brokers.Tests/OptionsBrokerTests.cs` — Sandbox chain/quotes/fills; Alpaca order
  payload (OCC, qty, day, no notional/extended hours), local rejections, `us_option` position
  decode, paged contract catalog, data-host snapshot parse with/without greeks, trading level.
- `IdiotProof.Blazor.Tests/IndexEventScannerTests.cs` — persistence semantics, idempotency,
  Pending→Realized flip, malformed entries, missing/broken file.
- Blocked until the account is approved for options: a real paper chain/quote load and a
  $-small order round-trip. Until then the ticket shows the level-0 banner on Alpaca modes.

## Out of scope (Phase 2+)
Multi-leg spreads (`order_class: "mleg"`), options in the DSL / Monitor, non-linear
`RiskGuardian` math for options, persisted extrinsic history / charting, dividend yield in
Black-Scholes, MAUI host wiring, extracting the shared elevation modal.

*(The Cypress spec for `/options` and the entitlement prerequisite were closed by
[IP-A34](../AMENDMENTS.md#IP-A34): `08_options.cy.ts`; both accounts at options level 3.)*
