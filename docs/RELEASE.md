---
layer: release
status: living
---
# IdiotProof — Release charter

> The one finish line this repo is working toward. Every open ticket in this repo sits under one of the epics
> below, and every epic is part of the milestone. Work that does not fit is out of scope until this milestone is
> reached (owner, 2026-11-27, MAT-T319).

## 1. What this is
IdiotProof turns a plain-English trade idea into a DSL strategy that the Monitor, running 24/7 inside the
IdiotProof Hub, evaluates against live Alpaca data and fires only through three gates (conditions, LLM voter panel,
Risk Guardian); the owner drives it through the `idiotproof` CLI and MCP server. Its flagship flow is the premarket
Gapper ([BIBLE §1](BIBLE.md)).

## 2. The milestone: IdiotProof Gapper paper arming {#milestone}
- **Milestone:** MS-25, "IdiotProof Gapper paper arming"
- **Published means:** an internal tool on this machine. The Hub, Cli and Mcp exes are deployed to
  `R:\MindAttic\Apps\IdiotProof\{Debug,Release}` by `run.ps1`, run against local SQL Server / LocalDB, and are
  not hosted anywhere else.
- **The natural stopping point:** the gapper study's surviving formation rule arms Gapper strategies on Paper by
  itself, catalysts are classified by the LLM queue within a budget, and a 20-trading-day paper shadow report
  compares the live formation snapshots against the backtest features, ending in a written go / no-go. That is
  the gate the BIBLE already sets before any Live BrokerMode is offered, so it closes the gapper program's loop
  (study → rule → paper arming → evidence) without opening Live trading, options automation or new research arcs.
  The bounded research that feeds it (the missed-month backfill and one cause-and-effect report) finishes with it.

## 3. Done when (acceptance checks)
Each line is also a `tickets milestone check` (build | exists | harness | signoff).
1. build — `IdiotProof.slnx` builds clean on main.
2. exists — `R:\MindAttic\Apps\IdiotProof\Release\Hub\idiotproof-hub.exe` (deployed by `run.ps1 -Redeploy`), and
   nothing in the repo names `C:\Apps\IdiotProof` or the deleted Azure SQL server.
3. exists — the paper-shadow report and its go / no-go verdict under `docs/research/`.
4. signoff — the owner watches a trading day where the formation rule arms Gapper strategies on Paper from the
   R: Hub and the journal records them.

## 4. Epics, in priority order
| Priority | Epic (ticket) | What it delivers | Features |
|---|---|---|---|
| 900 | IP-T656 Formation arming on paper | The study's rule arms paper Gappers, catalysts classified, 20-day shadow verdict | IP-T659 Formation trigger arming (990), IP-T660 Catalyst classification (980), IP-T661 Paper shadow verdict (970) |
| 800 | IP-T657 Local runtime and deploy | Hub on local SQL, deployed from R:, reliable | IP-T662 Local SQL defaults (890), IP-T663 R: deploy and run menu (880), IP-T664 Hub reliability (870) |
| 700 | IP-T658 Gapper cause research | The missed month backfilled and one bounded cause-and-effect report | IP-T665 History backfill (790), IP-T666 Cause-and-effect report (780) |

## 5. Out of scope (not before this milestone)
- **Live BrokerMode for gappers** — only after the paper shadow verdict says go; a new milestone.
- **Hosting on Hetzner or any server** — the Hub is a local, 127.0.0.1, per-user-Data-Protection process on local
  SQL; nothing here serves the web (Azure was decommissioned 2026-11-26).
- **Paid fundamentals vendor (float / short interest)** — the IP-T603 connectors stay off; fundamentals are a
  candidate feature, not in the trading path.
- **Options phase 2, shorts on a real account, multi-broker, Roslyn IdiotScript parser, adaptive auto-strategy
  generation (Epic S)** — BIBLE §7 frontier items that do not serve the gapper paper loop.
- **Open-ended research loops** — new gapper amendments, at-scale paper sweeps of the whole radar universe, the
  after-hours forward test's final read (window ends 2027-01-29). Each is filed `proposed` if wanted later.
- **Repo-wide exhaustive bug hunts** — real bugs are filed one at a time as fix tickets.

## 6. Current verified state
- Hub, CLI and MCP over one reflected operation catalog, TransactionGateway and Live confirm codes (Epic W,
  BIBLE §6).
- Gapper arm/tune/replay, radar, momentum-arm, daily retire job (45800636), strategy kinds (d4ebd087).
- Exit and entry hardening from the 2026-11-18/19 paper runs: outlier-print stops (df21cf87), unfilled entry
  cancel (f6eca989), exit rest minimum (7b157450), pre-open SellBy exits (008e28c3, b4f8d7bf).
- Formation recorder and TOD deciles (04392f84); move-start (m0) watch-only shadow, alert and journal hit rates
  (a60c22a8, IP-T628); after-hours radar sign with its pre-registered forward test (75215fc7, IP-T501).
- Gapper study amendments 09–15 registered and run; splits pull fixes (5b8785b0, 769eebf5).
- Data is on local SQL (user env `ConnectionStrings__IdiotProof*`), but the code default in
  `IdiotProof.Core/Hosting/CloudDatabases.cs` and the README/BIBLE still name the deleted Azure server.

## 7. Decision record
- 2026-11-27 — charter drafted from README/BIBLE/docs/tickets; Legion vote: **Approve**, plurality quorum, 7 of 9 (strength 0.78). Dissent: one voter wanted IP-T647 (fundamentals vendor) kept as core; another flagged the Azure SQL code default, which IP-T667 already fixes. No revision applied.
- Triage: 22 open → done 5 (IP-T628 + children IP-T637/638/639 in a60c22a8, IP-T501 in 75215fc7), duplicate 0, wont-do 8 (IP-T548, IP-T647, IP-T648, Hetzner IP-T649/650/651, containers IP-T447/632), kept 9; new tasks 3 (IP-T667 local SQL defaults, IP-T668 docs, IP-T669 shadow verdict). IP-T652/653/654 also stay under the cross-repo MAT-T293 / MAT-T261 trees.
