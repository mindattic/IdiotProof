# IdiotProof

A trading strategy platform that turns plain-English ideas into automated, monitored trading. Set conditions; the system watches the market, fires signals when they match, and (with your authorization) places the order. **Set and forget.**

> _"I think TSLA will gap up premarket and consolidate near the high. When that happens, go long with a stop at the premarket low and target 1.5 ATR above entry."_
>
> Set the strategy at 8 PM. Go to bed. The Monitor catches it at 4:07 AM ET when the conditions match.

---

## Table of contents

1. [What it is](#1-what-it-is)
2. [Architecture](#2-architecture)
3. [Storage layout](#3-storage-layout)
4. [The MindAttic family](#4-the-mindattic-family)
5. [IdiotScript — the DSL](#5-idiotscript--the-dsl)
6. [The Describe tab — Claude-driven generation](#6-the-describe-tab--claude-driven-generation)
7. [The Strategies page](#7-the-strategies-page)
8. [The Learning Center](#8-the-learning-center)
9. [Theme system](#9-theme-system)
10. [Account selector](#10-account-selector)
11. [The Monitor console app](#11-the-monitor-console-app)
12. [Running locally](#12-running-locally)
13. [Tests](#13-tests)
14. [Project layout](#14-project-layout)
15. [Roadmap](#15-roadmap)

---

## 1. What it is

IdiotProof is two things in one repo:

- **A Blazor Server web app** (`IdiotProof.Blazor`) where traders author strategies — visual flowcharts, raw IdiotScript fluent text, or plain-English descriptions handed to Claude.
- **A standalone console monitor** (`IdiotProof.Monitor`) that runs unattended, loads every active strategy from SQL, evaluates them per-condition each tick, and emits trade signals — so the trader doesn't have to be at the computer at 4 AM ET when their setup fires.

It is **Alpaca-only** in the active build (the IBKR adapter sits dormant in `IdiotProof.Brokers.Ibkr/`, ready to plug back in via the `IBrokerClient` abstraction).

---

## 2. Architecture

```
                                ┌──────────────────────────────┐
                                │        Trader (browser)      │
                                └──────────────┬───────────────┘
                                               │
                              ┌────────────────▼────────────────┐
                              │        IdiotProof.Blazor        │     ┌────────────────────┐
                              │  • Strategies page              │◄────┤  Cypress E2E       │
                              │  • Strategy Builder (3 tabs)    │     └────────────────────┘
                              │  • Account Pill (top-left)      │
                              │  • Learning Center              │
                              │  • Settings / API Keys          │
                              └─┬──────────┬──────────┬─────────┘
                                │          │          │
                       ┌────────▼──┐  ┌────▼────┐ ┌───▼─────────────┐
                       │ Strategy  │  │ Wikilink│ │ StrategyScript  │
                       │ Builder   │  │ Parser  │ │ Generator       │──► MindAttic.Legion
                       │ Renderer  │  └─────────┘ │ (Claude Opus,   │    ┌─────────────────┐
                       └─┬─────────┘              │  high-tier      │───►│ %APPDATA%       │
                         │                        │  voter panel)   │    │ MindAttic\LLM\  │
                         │                        └─────────────────┘    │ providers.json  │
                         │                                               └─────────────────┘
              ┌──────────▼─────────────────────────┐
              │       IdiotProof.Engine            │
              │  • AppSettings (overlay chain)     │
              │  • BrokerCredentialStore  ─────────┼──► %APPDATA%\MindAttic\Brokers\providers.json
              │  • BrokerRouter                    │
              │  • DataFeeds (Polygon / Mock)      │
              └─┬──────────┬──────────┬────────────┘
                │          │          │
            ┌───▼──┐   ┌───▼────┐ ┌───▼─────────┐
            │Models│   │Indicat-│ │ Strategies  │
            │      │   │ors     │ │ • IStrategy │
            └──────┘   └────────┘ │ • DslStrat  │
                                  │ • IndicatorSnapshotBuilder │
                                  └───┬─────────┘
                                      │
                              ┌───────▼──────────────────────┐
                              │      SQL Server (LocalDB)    │
                              │   IdiotProof database         │
                              │   • Strategies                │
                              │   • UserPreferences           │
                              │   • LearningArticles          │
                              │   • UserApiKeys               │
                              │   • AspNet* (Identity)        │
                              └───────▲──────────────────────┘
                                      │
                          ┌───────────┴───────────┐
                          │  IdiotProof.Monitor   │ ── 24/7 loop, evaluates active strategies,
                          │  (console app)         │    logs per-condition progress
                          └───────────────────────┘
```

### Components

| Project | Role |
|---|---|
| `IdiotProof.Blazor` | Web UI — Strategies page, Strategy Builder (Guided/Script/Describe), Learning Center, Settings, API Keys. ASP.NET Core Identity + Entity Framework Core for SQL Server. |
| `IdiotProof.Monitor` | Console host that loads active strategies and evaluates them on a fixed cadence. Logs `N/M conditions met — waiting on Foo` per pass. |
| `IdiotProof.Engine` | DI registration root. AppSettings load/overlay chain, BrokerCredentialStore, BrokerRouter, MarketDataFeed registration, AuditLogger, WorkspaceManager. |
| `IdiotProof.Brokers` | `IBrokerClient` abstraction + `AlpacaBrokerClient` + `SandboxBrokerClient` + `BrokerRouter`. Sandbox is always registered as the safe fallback. |
| `IdiotProof.Brokers.Ibkr` | **Dormant.** Not in `.sln`. Re-add to enable IBKR. |
| `IdiotProof.DataFeeds` | `IMarketDataFeed`: `PolygonDataFeed`, `MockDataFeed`, `SwitchableMarketDataFeed`. |
| `IdiotProof.Indicators` | Pure indicator math: ADX, ATR, Bollinger, EMA, MACD, RSI, SMA, Stochastic, VWAP, plus `CandlestickPatterns`. |
| `IdiotProof.Scripting` | `StrategyDefinition` + `StrategyBuilder` + the `Conditions` static catalog + condition algebra (`.And/.Or/.Not`). The IdiotScript DSL itself. |
| `IdiotProof.Strategies` | `IStrategy` interface, built-in strategies (ItiStrategy, etc.), `DslStrategy` adapter, `IndicatorSnapshotBuilder`. |
| `IdiotProof.Models` | Domain DTOs: `Candle`, `TradeSignal`, `Position`, etc. |
| `IdiotProof.Shared` | `IndicatorSnapshot` + cross-cutting helpers. |
| `IdiotProof.Cli` | Spectre.Console CLI (`idiotproof signal AAPL`, `positions`, `run`). |
| `tests/IdiotProof.NUnitTests` | NUnit tests for the new DSL surface, indicator pipeline, candlestick patterns, wikilink parser, DslStrategy adapter. |
| `tests/IdiotProof.Cypress` | End-to-end Cypress tests for the Blazor UI. |

---

## 3. Storage layout

IdiotProof follows the **MindAttic family conventions** (see § 4):

```
%LOCALAPPDATA%\MindAttic\IdiotProof\           ← per-app state (matches ThinkTank)
└── Settings\app-settings.json                 (legacy disk overlay; may move to SQL later)

%APPDATA%\MindAttic\                            ← shared keyrings (matches Legion)
├── LLM\providers.json                         (Claude/OpenAI/Gemini keys — Legion's home)
└── Brokers\providers.json                     (alpaca-paper, alpaca-live)

SQL Server (LocalDB by default)                ← canonical runtime state
└── IdiotProof database
    ├── AspNetUsers, AspNetRoles, ...           (ASP.NET Identity)
    ├── UserApiKeys                             (per-user encrypted broker/data keys)
    ├── Strategies                              (UUIDv7 id, OwnerUserId, Title, ScriptText, IsActive, ...)
    ├── UserPreferences                         (Theme, ActiveAccountId, OpenStrategyTabs, UiStateJson)
    ├── LearningArticles                        (Slug, Category, Title, BodyMarkdown, Order)
    ├── SettingsKv                               (generic KV store for runtime-editable settings)
    ├── Workspaces                               (per-user containers — Watchlist + Strategies + risk params, schema-tolerant BodyJson)
    ├── AuditLogs                                (append-only trail: signal fires, orders, broker switches, risk vetoes)
    └── ConditionProgress                        (one row per Strategy — Monitor's most recent N/M evaluation snapshot)
```

**Connection string priority chain** (matches StreetSamurai's pattern):

1. `ConnectionStrings__IdiotProof` env var
2. `ConnectionStrings:IdiotProof` from IConfiguration (`appsettings.json`)
3. LocalDB fallback: `Server=(localdb)\MSSQLLocalDB;Database=IdiotProof;Trusted_Connection=True;TrustServerCertificate=True;`

The same chain is used by both the Blazor host and the Monitor console — they always read the same database.

**`IDIOTPROOF_DATA_DIR` env var** overrides the per-app state root (useful for parallel test runs).

---

## 4. The MindAttic family

IdiotProof is one of several MindAttic projects (StreetSamurai, ThinkTank, etc.). Two conventions are shared:

- **Shared keyrings live in Roaming** (`%APPDATA%\MindAttic\<Subsystem>\providers.json`). Examples:
  - `LLM` — owned by `MindAttic.Legion`, holds Claude/OpenAI/Gemini/etc. keys.
  - `Brokers` — owned by IdiotProof, holds `alpaca-paper` + `alpaca-live`.
- **Per-app state lives in Local** (`%LOCALAPPDATA%\MindAttic\<AppName>\`). Examples:
  - `ThinkTank` — `Settings.json` + `Conversations\`
  - `IdiotProof` — same convention, app state subfolder.

`legion.json` at each project's root configures the LLM voter panel and judge. IdiotProof ships with the **high tier**:

```json
{
  "voters": ["claude", "openai", "gemini", "deepseek"],
  "judge": "claude",
  "tier": "high"
}
```

Strategy generation is high-stakes (a single LLM hallucinating a verb costs real money) so the panel cross-verifies before the script is shown to the user.

---

## 5. IdiotScript — the DSL

### Six lifecycle phases

Every strategy walks through fixed phases. The visual builder renders one card per phase; the parser rejects verbs used in the wrong phase.

| # | Phase | What it answers | Example verbs |
|---|---|---|---|
| 1 | Setup | Ticker / session / window | `Stock.Ticker`, `Session`, `Quantity` |
| 2 | Filters | Regime gates (always-on) | `RequireAdxAbove`, `RequireEmaStack` |
| 3 | Entry | Triggers (AND of conditions) | `IsAboveVwap`, `OnReclaim`, `IsBullishEngulfing` |
| 4 | Order | Direction + size | `Long`, `Short`, `Quantity` |
| 5 | Risk | Stop placement | `StopLoss`, `TrailingStopLoss` |
| 6 | Exit | Targets, time exit | `TakeProfit`, `ExitStrategy` |

### Verb catalog (this session's expansion)

#### Setup
- `Stock.Ticker(symbol)` — required first call
- `.Session(TradingSession)` — `Premarket / RTH / AfterHours / Extended`
- `.Quantity(int)` — share count

#### Filters (regime gates)
- `.RequireAdxAbove(threshold = 20)` — trending market only
- `.RequireEmaStack(fast, slow)` — fast EMA above slow = uptrend confirmation

#### Entry — VWAP
- `.IsAboveVwap()` / `.IsBelowVwap()` (alias: `AboveVwap`, `BelowVwap`)
- `.OnVwapReclaim()` / `.OnVwapLoss()`

#### Entry — EMA family
- `.IsAboveEma(period)` / `.IsBelowEma(period)`
- `.IsBetweenEma(fast, slow)` — pullback zone
- `.OnReclaim(period)` — prior bar at-or-below N-EMA, current bar back above

#### Entry — RSI / MACD / ADX / DI
- `.IsRsiOversold(threshold = 30)` / `.IsRsiOverbought(threshold = 70)`
- `.IsRsiBullishDivergence()` / `.IsRsiBearishDivergence()`
- `.IsMacdBullish()` / `.IsMacdBearish()`
- `.IsAdxAbove(threshold)` / `.IsDiPositive()` / `.IsDiNegative()`

#### Entry — Volume / Gap / Levels
- `.WithVolumeConfirm(multiplier = 1.2)` / `.IsVolumeAbove(multiplier)` / `.VolumeSpike(multiplier = 2.0)`
- `.IsGapUp(minPercent = 3)` / `.IsGapDown(minPercent = 3)`
- `.IsAtSupport(tolerancePercent = 0.5)` / `.IsAtResistance(tolerancePercent = 0.5)`
- `.HoldsAbove(price)` / `.HoldsBelow(price)` / `.IsNear(price, tolerance)` / `.BreaksAbove(price)` / `.BreaksBelow(price)`

#### Entry — Candlestick patterns
- `.IsBullishEngulfing()` / `.IsBearishEngulfing()`
- `.IsHammer()` / `.IsShootingStar()`
- `.IsDoji()`

#### Aliases
Every verb has natural-language aliases so Claude-generated scripts compile regardless of phrasing: `Oversold` / `Overbought` / `BullishMacd` / `BearishMacd` / `Trending` / `GapUp` / `GapDown` / `VolumeSpike`.

#### Order / Risk / Exit
- `.Long()` / `.Short()` / `.Order(TradeDirection)`
- `.Quantity(int shares)` / `.Quantity(decimal dollars)` — overloaded; share count or notional dollars (Alpaca's `notional` field). Mutually exclusive — setting one clears the other. Aliases: `.QuantityShares(N)` / `.QuantityNotional($)`.
- `.StopLoss(price)` / `.StopLossPercent(percent)` / `.TrailingStopLoss(percent)`
- `.TakeProfit(price)` / `.TakeProfit(t1, t2, t3?)` / `.TakeProfitPercent(percent)`
- `.ExitStrategy(timeOfDay)`

### Branching — expression syntax

```csharp
using static IdiotProof.Scripting.Conditions;

Stock.Ticker("SPY")
    .RequireAdxAbove(20)
    .If(IsAboveVwap.And(IsEmaAbove(9)))
        .Then(b => b.Long().StopLossPercent(1).TakeProfitPercent(2))
    .ElseIf(c => c.IsBelowVwap().IsEmaBelow(9),
            b => b.Short().StopLossPercent(1).TakeProfitPercent(2))
    .Else(b => b.Long().TakeProfitPercent(0.5))
    .Build();
```

`Conditions.IsAboveVwap`, `Conditions.IsEmaAbove(9)`, etc. are the static expression-form catalog. They compose with `.And()`, `.Or()`, `.Not()` from `ConditionExtensions`. Branching blocks evaluate top-down; the first match's overrides apply on top of the base strategy.

### Worked example — 9/30 pullback continuation

```csharp
Stock.Ticker("NVDA")
    .RequireAdxAbove(20)               // regime gate: trending market
    .RequireEmaStack(9, 31)            // confirm uptrend (9 above 31)
    .IsAboveVwap()                     // institutional bullish bias
    .IsBetweenEma(9, 31)               // price in pullback zone
    .OnReclaim(9)                      // trigger: closed back above 9
    .WithVolumeConfirm(1.2)            // 1.2× avg volume on trigger bar
    .Long()
    .StopLoss(450)                     // below the 31 EMA at signal time
    .TakeProfit(485)                   // ~2× risk
    .Build();
```

The Filters (RequireAdxAbove, RequireEmaStack) prevent the strategy from firing in chop or downtrend pullbacks. The Entry conditions are the actual trigger.

---

## 6. The Describe tab — Claude-driven generation

`/builder` has three tabs: **Guided** (multi-step wizard), **Script** (raw IdiotScript editor), and **Describe** (free-text → Claude → IdiotScript).

The Describe flow:

1. User types ticker + title + plain-English description.
2. Click "Generate with Claude" → `StrategyScriptGenerator` builds a system prompt by **reflecting** on `StrategyBuilder` + `Conditions` (so the verb catalog can never drift from the codebase) plus worked examples, sends to Claude through `LegionClient` via `legion.json`'s high-tier voter panel.
3. Generated IdiotScript appears in the left textarea; `WikilinkParser.ParseScript` parses it into a `StrategyDefinition`; the right pane renders a live visual via `<StrategyBuilderRenderer>`.
4. User edits if needed (re-parse on blur). Save → `StrategyRepository.CreateAsync` writes the row to SQL with a UUIDv7 id, paper-by-default.

```
[ Prose ] ──► StrategyScriptGenerator ──► LegionClient ──► Claude (high tier)
                  │                                            │
                  └─◄───── verb catalog (reflection) ◄─────────┘
                                                                ▼
                                                      IdiotScript text
                                                                │
                                       WikilinkParser.ParseScript
                                                                │
                                                   StrategyDefinition
                                                                │
                                                   <StrategyBuilderRenderer>
                                                                │
                                                       Save → SQL Strategies
```

---

## 7. The Strategies page

`/strategies` is the front door. Every saved strategy for the signed-in user appears as a row. Each row:

- **Title + ticker chip + last-fired-at + created date**
- **Active toggle** — flips `IsActive` in SQL. The Monitor picks active strategies on its next tick.
- **Edit** → `/builder?strategyId={guid}` — preloads the row into the Describe tab so you see the visual + script + prose ready to tweak. Save updates in place.
- **Delete** (with confirmation modal)
- **Live progress badge** on active rows: `3/5 · IsOnReclaim(9)` — the Monitor writes per-tick to `ConditionProgress`; the page polls every 5 s. Full-pass shows a green check.
- **Expand** (chevron) → renders `<StrategyBuilderRenderer>` inline, plus a `<details>` of the raw IdiotScript

The expand state is **persisted to localStorage** under `idiotproof.strategies.openRows` so re-opening the page restores the same expanded rows. Filter input narrows by title / ticker / description.

---

## 8. The Learning Center

`/learn` is the **encyclopedia and atlas** of IdiotScript. Every verb has a page. Articles live in the SQL `LearningArticles` table; they're seeded on app start by `LearningContentSeeder` from a fixed catalog.

### Wikilink syntax

Article bodies are Markdown-ish prose with **inline live-rendered strategy examples** via wikilinks:

```markdown
The textbook continuation pattern for a stock in an uptrend:

[[Stock.Ticker("NVDA").RequireAdxAbove(20).RequireEmaStack(9, 31).IsBetweenEma(9, 31).OnReclaim(9).WithVolumeConfirm(1.2).Long().StopLoss(450).TakeProfit(485).Build()]]

The regime filter is what keeps this from firing in chop.
```

`<WikiContent>` tokenizes the prose with `WikilinkParser`, parses each `[[...]]` script into a `StrategyDefinition`, and renders it with `<StrategyBuilderRenderer>` at exactly the position where the wikilink appeared. Unparseable scripts surface as a "unparseable" badge — they don't vanish silently.

### Categories

The seeded catalog covers:

- **1. Overview** — what is IdiotScript
- **2. Phases** — the six lifecycle stages
- **3. Setup Verbs** — `Ticker`, `Session`
- **4. Filter Verbs** — `RequireAdxAbove`, `RequireEmaStack`
- **5. Entry Verbs** — VWAP, EMA family, RSI divergence, candle patterns, volume confirm, support/resistance
- **6. Order Verbs** — `Long`, `Short`, `Quantity`
- **7. Risk Verbs** — `StopLoss`, `TrailingStopLoss`
- **8. Exit Verbs** — `TakeProfit` and multi-target scaling
- **9. Branching** — If / ElseIf / Else expression syntax
- **10. Worked Examples** — 9/30 pullback, gap-up fade, oversold bounce

To add a new article: append a new `Article(Slug, Category, Order, Title, Summary, BodyMarkdown)` entry in `LearningContentSeeder.Articles` and bump `CatalogVersion`. The seeder upserts on next startup.

---

## 9. Theme system

Alpaca palette only for now, themeable infrastructure for future themes.

```
wwwroot/css/_theme-alpaca.css      ← :root[data-theme="alpaca"] { --brand: #FFCD00; ... }
                                     (every variable + component override scoped to this selector)
Components/App.razor               ← <html data-theme="alpaca"> + pre-paint script
                                     reads localStorage.idiotproof.theme to avoid SSR flash
```

To add a new theme:

1. Copy `_theme-alpaca.css` to `_theme-{name}.css`.
2. Change the selector to `:root[data-theme="{name}"]`.
3. Edit the variable values.
4. Add a `<link>` to it in `App.razor`.
5. Set `UserPreferences.Theme = "{name}"` (UI for this is on the roadmap).

Razor components reference `var(--brand)`, `var(--bg-primary)`, etc. — never raw colors — so theme switching is a single attribute flip.

### Alpaca palette

| Token | Hex | Use |
|---|---|---|
| `--brand` | `#FFCD00` | Account pill, action buttons |
| `--brand-soft` | `#FFF9D6` | Selected row highlight |
| `--green` | `#00C853` | Buy / positive |
| `--red` | `#EF4444` | Sell / negative / danger |
| `--bg-primary` | `#FFFFFF` | Page background |
| `--text-primary` | `#0E1116` | Body text |

---

## 10. Account selector

The **AccountPill** in the top-left of every page mirrors Alpaca's UI: pill shows label + type ("Paper" / "Live") + masked account ID. Clicking opens a dropdown listing both Live and Paper accounts with cash + day-change %.

- Selecting an account writes `UserPreferences.ActiveAccountId` (SQL) and mirrors to `localStorage` for SSR pre-paint.
- **Live account** = pill renders with red outline.
- **Paper account** = pill renders with brand-yellow outline + soft-yellow background.

Account credentials come from the shared MindAttic broker keyring at `%APPDATA%\MindAttic\Brokers\providers.json` — the keys live there (synced across machines via Windows account roaming) and are overlaid onto `AppSettings` at startup via `BrokerCredentialStore.Get("alpaca-paper")` / `Get("alpaca-live")`.

---

## 11. The Monitor console app

`IdiotProof.Monitor` is the unattended evaluator. Run it once and forget about it; it'll loop forever, evaluating every active strategy in SQL.

### Behavior

Every `IDIOTPROOF_MONITOR_INTERVAL` (default 30s):

1. Load every `Strategy` row where `IsActive = true`.
2. Group by symbol; fetch a 120-bar window from the configured market data feed once per symbol.
3. For each strategy: parse `ScriptText`, build an `IndicatorSnapshot` via `IndicatorSnapshotBuilder` (with the EMA periods the strategy needs), walk each entry condition individually.
4. Log per-condition progress:

```
14:30:01 info: [9/30 Pullback] NVDA 4/5 — waiting on: IsOnReclaim(9)
14:30:31 info: [9/30 Pullback] NVDA 5/5 — waiting on: IsWithVolumeConfirm(1.2)
14:31:01 info: [9/30 Pullback] NVDA ✓ ALL 5/5 conditions met → SIGNAL (Long @ 472.18)
```

5. **Upsert `ConditionProgress`** with `(PassedCount, TotalCount, FirstFailingVerb)` — the Strategies page reads this every 5 s for live badges.
6. On full-pass, route the candidate signal through two gates before fire:
   - **Gate 1 — `LlmVotingService`** (Legion high-tier voter panel from `legion.json`). Approve / reject / abstain. Reject = no fire; AuditLog stamps the veto reasoning.
   - **Gate 2 — `RiskGuardian`** (the canonical pre-trade gatekeeper, **per-user instance** cached by `RiskGuardianService` and seeded from each user's `UserPreferences` risk fields). Validates: stop loss exists, stop is on the correct side of entry, total risk ≤ MaxLossPerTrade, daily loss ≤ MaxLossPerDay, stop distance within MinStop / MaxStop %, account risk ≤ MaxAccountRiskPercent. The cache preserves the in-memory daily-loss tracker across signals so the daily circuit breaker actually trips. Block = no fire; AuditLog stamps `signal-blocked` with `blockReasons` + `expectedLoss` + `worstCase` JSON.
   - When **both** gates pass, `RecordFiredAsync` bumps `LastFiredUtc` + `FireCount`. `LastFiredUtc` / `FireCount` only ever tick on signals that survived BOTH gates.
   - **LLM voting disabled or no Claude key** → skip Gate 1; Gate 2 still runs.

### Run

```bash
dotnet run --project IdiotProof.Monitor
```

### Configure

| Env var | Default | Notes |
|---|---|---|
| `ConnectionStrings__IdiotProof` | LocalDB | Shared with the Blazor host. |
| `IDIOTPROOF_MONITOR_INTERVAL` | `30s` | Accepts `30s`, `5m`, `2h`, or bare seconds. |

The Monitor uses the same `IBrokerClient` abstraction as the web app — order placement, Risk Guardian, and LLM voting still go through Alpaca via the Blazor host (the Monitor just emits signals; it doesn't place orders).

---

## 12. Running locally

### Prerequisites

- .NET 10 SDK
- SQL Server LocalDB (ships with Visual Studio; or install the SQL Server Express LocalDB package)
- Node 18+ (for Cypress only)

### First run

```bash
git clone <repo>
cd IdiotProof

# Restore + build
dotnet build IdiotProof.sln

# Apply migrations to LocalDB (creates the IdiotProof database)
dotnet ef database update --project IdiotProof.Blazor

# Run the Blazor host
dotnet run --project IdiotProof.Blazor
# → https://localhost:5001
```

In another terminal, optionally run the Monitor:

```bash
dotnet run --project IdiotProof.Monitor
```

### Configuration

- **Claude API key** — pre-fill at `%APPDATA%\MindAttic\LLM\providers.json` (the canonical MindAttic keyring), or paste into the API Keys page in-app.
- **Alpaca keys** — same pattern: `%APPDATA%\MindAttic\Brokers\providers.json` with `alpaca-paper` and `alpaca-live` entries (see § 10).
- **Polygon key** (market data) — paste into API Keys page in-app, or set `PolygonApiKey` env var.

---

## 13. Tests

### NUnit (backend)

```bash
dotnet test tests/IdiotProof.NUnitTests
```

52 tests covering:

- **DslConditionTests** — every new condition verb (VWAP/EMA/ADX/RSI/Volume/Support/Resistance) + the `.And/.Or/.Not` algebra + phase tagging
- **CandlestickPatternsTests** — engulfing/hammer/shooting-star/doji + zero-range edge cases
- **IndicatorSnapshotBuilderTests** — empty input, ramp data, EMA dictionary, prior fields, ADX/RSI/swing levels
- **WikilinkParserTests** — text passthrough, single/multi wikilinks, unparseable fallback, full chain parse
- **DslStrategyTests** — symbol mismatch, no candles, all-pass signal emission, stop/target propagation, type tagging
- **QuantityTests** — share/notional overload mutual exclusion, alias parity, default behavior

### Cypress (frontend)

```bash
cd tests/IdiotProof.Cypress
npm install
npm run open    # interactive
npm run ci      # headless
```

Specs cover the public Learning Center renders (including wikilink-embedded strategies actually appearing as phase cards) and the authenticated Describe-tab → Strategies-page round-trip with a stubbed Legion call.

### Existing xUnit (preserved)

The legacy `IdiotProof.Scripting.Tests`, `IdiotProof.Strategies.Tests`, `IdiotProof.Indicators.Tests`, `IdiotProof.Engine.Tests` xUnit projects still pass and run via `dotnet test IdiotProof.sln`. The new NUnit project is additive — no test was rewritten.

---

## 14. Project layout

```
IdiotProof/
├── IdiotProof.sln
├── legion.json                              ← Legion voter panel (high tier)
├── CLAUDE.md                                ← Project rules for AI tooling
├── README.md                                ← You are here
│
├── IdiotProof.Models/                       ← Domain DTOs
├── IdiotProof.Shared/                       ← IndicatorSnapshot
├── IdiotProof.Indicators/                   ← Pure indicator math + CandlestickPatterns
├── IdiotProof.Scripting/                    ← IdiotScript DSL
├── IdiotProof.Strategies/                   ← IStrategy, DslStrategy adapter, IndicatorSnapshotBuilder
├── IdiotProof.DataFeeds/                    ← IMarketDataFeed (Polygon, Mock)
├── IdiotProof.Brokers/                      ← Alpaca + Sandbox + IBrokerClient
├── IdiotProof.Brokers.Ibkr/                 ← Dormant — not in .sln
├── IdiotProof.Engine/                       ← DI root, AppSettings, BrokerCredentialStore
├── IdiotProof.Blazor/                       ← Web app (the front door)
│   ├── Data/                                ← AppDbContext, Strategy, UserPreferences, LearningArticle
│   ├── Migrations/                          ← EF migrations: InitialSqlServer, AddUserPreferences, AddLearningArticles, AddSettingsWorkspacesAuditLog, AddConditionProgress
│   ├── Services/                            ← StrategyScriptGenerator, WikilinkParser, repositories (Strategy / Workspace / Settings / AuditLog / UserPreferences / ConditionProgress)
│   ├── Components/Pages/                    ← Strategies.razor, StrategyBuilder.razor, Learn.razor, ...
│   ├── Components/Shared/                   ← AccountPill.razor, StrategyBuilderRenderer.razor, WikiContent.razor
│   └── wwwroot/css/_theme-alpaca.css
├── IdiotProof.Cli/                          ← Spectre CLI
├── IdiotProof.Monitor/                      ← 24/7 evaluator console
└── tests/
    ├── IdiotProof.NUnitTests/               ← New NUnit suite (47 tests)
    └── IdiotProof.Cypress/                  ← End-to-end UI tests
```

---

## 15. Roadmap

### Pending (deferred from prior sessions)

- **Engine adoption of SQL workspaces** — the schema (`Workspaces`, `SettingsKv`, `AuditLogs` tables + `WorkspaceRepository` / `SettingsRepository` / `AuditLogRepository`) landed in the migration `AddSettingsWorkspacesAuditLog`. The Engine's legacy JSON-on-disk `WorkspaceManager` still works; switching it to read/write through `WorkspaceRepository` is a follow-on refactor (plus a one-shot disk → SQL importer for any existing workspaces).
- **Multi-tab strategy editor** — the Strategies-page → /builder navigation is single-tab today. Multi-tab with localStorage tab restoration is on the roadmap.
- **Visual drag-and-drop** — the Strategy Builder's visual flow-chart is currently read-only. Drag-and-drop reorder + add/remove condition cards is a pending UX upgrade.
- **Roslyn-based parser** — the current `WikilinkParser.ParseScript` is regex-driven and tolerant. A proper Roslyn parser would surface syntax errors at exact line/col.

### Long-term

- Backtesting harness with the same DSL strategies.
- Strategy sharing / template marketplace.
- IBKR adapter re-enable (re-add `IdiotProof.Brokers.Ibkr` to `.sln`).
- Mobile app shell (MAUI or React Native) reading the same SQL Strategies.

---

## License

Internal MindAttic project.
