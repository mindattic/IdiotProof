---
codex: 1
project: IdiotProof
code: IP
layer: amendments
status: living
updated: 2026-06-09
---

# IdiotProof — Amendments (append-only; amendment wins over the bible)

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
`MindAttic.Authentication v2.0.0` (org-standard, used by StreetSamurai, MindAttic.Ideas, Tutor).

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
