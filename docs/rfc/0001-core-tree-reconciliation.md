---
codex: 1
project: IdiotProof
code: IP
layer: rfc
status: planned
updated: 2026-06-07
---

# RFC 0001 — Reconcile the out-of-solution `IdiotProof.Core` tree

## Problem
A large `IdiotProof.Core/` tree (Calculators/, Services/, Strategy/, FutureState/,
Documentation/*.htm, Profiles/, Data/) plus `IdiotProof.Cli/`, `IdiotProof.Brokers.Ibkr/`,
`IdiotProof.Core.UnitTests/`, `tests/IdiotProof.NUnitTests/`, and `IdiotProof.Scripting.Tests/`
exist on disk but are **not** referenced by `IdiotProof.slnx`. They do not build or test as part
of the solution. The `README.md` and `.github/copilot-instructions.md` describe this older
`Core`/`Web`/IBKR/NUnit shape as if it were current, which makes "what is the build?" ambiguous.
See [IP-A1](../AMENDMENTS.md#IP-A1).

## Options compared
1. **Delete the dead trees.** Smallest repo, honest graph — but loses the FutureState/IBKR/Cli
   work and the rich `Documentation/*.htm` if they're still wanted.
2. **Adopt them back into `IdiotProof.slnx`.** Restores the Core/Cli/NUnit story — but they may
   not compile against the current DSL/Models and would inflate build/test scope.
3. **Move them to a clearly-labelled `legacy/` or a separate solution.** Keeps the code,
   removes the ambiguity, leaves `IdiotProof.slnx` as the single source of "what ships."

## Decision
Deferred — flagged for the maintainer. Default for now (per [IP-A1](../AMENDMENTS.md#IP-A1)):
treat the out-of-solution trees as legacy/dormant; canon and verified state track only
`IdiotProof.slnx`.

## What NOT to do
- Do **not** cite out-of-solution code as "done" in [USER_STORIES.md](../USER_STORIES.md).
- Do **not** silently delete the README's human onboarding content; it renders to the landing page.
- Do **not** re-add IBKR to the solution without the steps in `IdiotProof.Brokers.Ibkr/README.md`.

## Phased plan (with risk)
1. Maintainer picks option 1/2/3. *(Risk: none — a decision.)*
2. If adopt: add projects to `.slnx`, fix compile errors, fold their tests into §6 evidence.
   *(Risk: medium — drift between old Core and current Models/DSL.)*
3. If move/delete: relocate, then prune the README's `Core`/`Web`/NUnit sections to match.
   *(Risk: low.)*
4. Update [BIBLE §4](../BIBLE.md#IP-§4) and resolve [IP-A1](../AMENDMENTS.md#IP-A1).

## Graduates into
[BIBLE §4 Architecture canon](../BIBLE.md#IP-§4), [IP-US-F1](../USER_STORIES.md), and resolution of
[IP-A1](../AMENDMENTS.md#IP-A1).
