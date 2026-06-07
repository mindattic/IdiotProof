---
codex: 1
project: IdiotProof
code: IP
layer: amendments
status: living
updated: 2026-06-07
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

**Resolution / migration.** Until [RFC 0001](rfc/0001-core-tree-reconciliation.md) is decided,
the out-of-solution trees are treated as **legacy/dormant** for canon purposes: not deleted,
not cited as "done", not part of the build/test evidence. The README is left intact (it is the
human onboarding doc and other tooling renders it to the landing page); this amendment is the
authoritative reconciliation. When RFC 0001 lands, fold its decision into [BIBLE §4](BIBLE.md#IP-§4)
and mark this amendment's open question resolved.
