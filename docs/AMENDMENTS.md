---
codex: 1
project: IdiotProof
code: IP
layer: amendments
status: living
updated: 2026-06-08
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
