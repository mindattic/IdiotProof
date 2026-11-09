# Backup gap diagnosis (IP-T449)

Diagnosis only. Nothing was backed up, copied, restarted or edited outside this file. The fix belongs in
`D:\Projects\MindAttic\MindAttic.Launcher` (this CLI may not edit that repo). Diagnosed 2026-11-09.

## 1. Summary

The ticket premise ("no Databases folder, IdiotProof and IdiotProof_Research_GAP unprotected") is only
partly true. The database backup code works and is currently producing good output.

- `R:\Backup\MindAttic\2026-11-07`, `2026-11-08` and `2026-11-09` all have `Databases\(localdb)_MSSQLLocalDB\`
  with 20 `.bak` files each, including `IdiotProof.bak` (203 MB) and `IdiotProof_Research_GAP.bak` (9.08 GB).
  The 11-09 files are stamped 04:10-04:25.
- Every dated folder from 2026-10-16 through 2026-11-04 also has `Databases` (12-18 `.bak` files).
- The only folders in that range without `Databases` are **2026-11-05 and 2026-11-06**, plus 10-13 and 10-15,
  which predate the LocalDB auto-discovery commit (89b2e8c).
- So the ticket was probably written while 11-05 and 11-06 were the newest folders. There is no standing
  "Databases never produced" bug. There was a **two-night gap in which the run did not finish**.

## 2. Root cause of the 11-05 / 11-06 gap

Short version: those two nightly runs **stopped during the robocopy "files" step, before the SQL step started**.
`BackupCommand.Run` is strictly serial: files, then AppData zip, then SQL, then SQLite. Whatever ended the
process during step 1 meant no later step ran.

Evidence, from read-only listings of R: only:

- `2026-11-05` has about 194 top-level entries and `2026-11-06` about the same. `2026-11-04` and `2026-11-07`
  have about 224. The 11-05 folder lacks everything alphabetically late: `Unity.*`, `UnityRonin`,
  `Unreal.*`, `Unstuck`, `Tutor`, `TownWalker`, `_dropbox`, `_recovery`, `IdiotProof_t432` and `IdiotProof_t436`.
  11-06 lacks `Unity.*`, `Unreal.*`, `Unstuck` and the same `IdiotProof_t4xx` folders. Both are truncated copies.
  A robocopy that finishes (exit code under 8) would have copied them.
- Neither folder has `Databases`, and no AppData zip step output, consistent with the process never reaching step 2.
- `Launcher.NightlyBackup` runs `conhost.exe --headless C:\Apps\MindAttic.Launcher\MindAttic.Launcher.exe backup --keep 30`.
  It is a daily trigger from 2026-11-02 00:00, `LogonType Interactive`, user `ryand`, `StartWhenAvailable=True`,
  `ExecutionTimeLimit=PT72H`.
  - An Interactive-logon task is killed when the session ends (logoff, reboot, Orchestrator restart, sleep/wake).
  - 11-08's folder was last written at 09:45, which shows a late `StartWhenAvailable` catch-up run.
  - 11-05 was started about 00:00 and its folder was last touched 00:19. 11-06 was last touched 03:36.
- Task Scheduler operational history is off here and the Launcher's log DB
  (`%APPDATA%\MindAttic\Logging\MindAttic.Launcher\MindAttic.Log.db`, 49 KB) holds no backup summary line for
  those dates. That is consistent with an abnormal exit, because the summary
  (`BackupCommand.cs:146-150`) is only written when `Run` returns normally.

**Not proven:** *what* killed the 11-05 and 11-06 processes (logoff, reboot, a kill, or a robocopy hang). That
cannot be recovered without logs, which is itself the defect.

**Ruled out:**

- LocalDB not started or discovery returning nothing: discovery returned 20 databases tonight with no changes.
- SQL service account cannot write to R:. The SQL Server engine writes the `.bak`, and it did on 11-07 to 11-09.
- A temp path that is never copied: `.bak` files are written straight into the dated folder
  (`SqlBackupService.cs:108-109`).
- A swallowed exception: `BackupOne` and `DiscoverDatabases` do convert exceptions to failed results
  (`SqlBackupService.cs:226-232`, `277-280`). `BackupCommand` prints per-database FAILED and sets the exit
  code, so those are not silent. But a *killed process* is silent.

### Structural weaknesses that made this invisible (file:line)

All paths are under `MindAttic.Launcher\MindAttic.Launcher\`.

1. `Commands\BackupCommand.cs:78` runs the multi-hour robocopy of `D:\Projects\MindAttic` **before** the databases
   (`:102`). Anything that kills the process during the longest step silently skips every database. The
   irreplaceable data is scheduled last.
2. `Commands\BackupCommand.cs:146-150`: the only durable log line is written once at the end. There is no
   "started" line and no per-step line, so a killed run leaves **zero** trace.
3. `Commands\BackupCommand.cs:122-127`: prune uses `ok`, so a run that dies never prunes. That is right, but
   nothing records that it died.
4. `Commands\BackupCommand.cs:92-101`: a failed LocalDB discovery is a yellow "skipped" message but is **not**
   folded into `ok` (the exit code stays 0). A broken instance would mean no databases and a green run.
5. `Services\BackupService.cs:70-148`: only `Source = D:\Projects\MindAttic` is backed up. `D:\IdiotProofData`
   is out of scope by construction (see section 4).

## 3. Exact code change needed in MindAttic.Launcher

Goal: the databases are captured before the long file copy, every step leaves a log line, a partial run is
marked, and a discovery failure turns the run red.

1. `Commands\BackupCommand.cs`: reorder `Run` so that SQL discovery and `d.BackupSql(...)` run **first**
   (right after `ResolveTargetFolder`, before `d.RunFiles`). Move `SqliteBackupService` up with it. Keep AppData
   second and files last.
2. Add `Serilog.Log.Information("backup step {Step} {Phase}", ...)` before and after each step, plus a
   "backup started: target=..." line at the top. Wrap the body in `try/finally` so a final line with
   `completed=false` is written on any exception.
3. Treat discovery failure as a failure: after line 98, `else { ok = false; ... }`. A machine that
   legitimately has no LocalDB can be gated behind a setting.
4. Write `{target}\BACKUP_COMPLETE.json` (steps, counts, bytes, UTC start/end) **only** at the very end, and
   have prune and a future restore ignore folders without it. This makes a truncated folder like 11-05/06
   self-evident instead of looking like a backup.
5. `Services\BackupService.cs`: add a second source (section 4), either as a `ExtraSources` list or a second
   `Run(source, target)` call from `BackupCommand`.
6. Scheduled task: change `LogonType` to "Run whether user is logged on or not" or otherwise make it survive
   logoff. Note LocalDB is a **user-scoped** instance (per MEMORY: LocalDB and DataProtection are user-scoped),
   so the task must still run as `ryand`.
7. Tests: add cases to `MindAttic.Launcher.Tests\BackupRestoreCommandTests.cs` for "SQL runs before files" and
   "discovery failure gives a non-zero exit code".

## 4. Adding D:\IdiotProofData to the backup scope

Sizes (read-only `du -sm`, 2026-11-09; about 33.3 GB total):

| Path under `D:\IdiotProofData` | Size | Re-downloadable? | Back up? |
|---|---:|---|---|
| `gapper-study\bars` | 27,719 MB | Yes (5-yr Alpaca pull; slow, but sourced) | **Exclude** |
| `gapper-study\tradeflow` | 47 MB | Yes (`tradeflow-pull.sh`) | **Exclude** |
| `gapper-study\explore` | 1,073 MB | Partly (runs are deterministic, expensive; ledger is also in git) | Include |
| `gapper-study\harness-bin` .. `harness-bin10` | about 2,200 MB (10 x 220) | Yes (build output copies) | **Exclude** |
| `gapper-study\logs`, `*.sh` | about 3 MB | No (small) | Include |
| `sec-companyfacts` | 1,385 MB | Yes (SEC API) | Optional; exclude to save space |
| `finra-short-volume` | 449 MB | Yes, but per-day files are resumable and slow | Include |
| `finra-short-interest` | 232 MB | Yes | Include |
| `cli-bin` | 221 MB | Yes (build output) | **Exclude** |
| `halts` | 78 MB | Nasdaq halt XML; older history may not be re-fetchable | Include |
| `regsho-threshold` | 10 MB | Threshold lists are point-in-time; older ones may not be re-fetchable | Include |

Recommended scope: everything **except** `gapper-study\bars`, `gapper-study\tradeflow`,
`gapper-study\harness-bin*`, `cli-bin` and `sec-companyfacts`. That is about 1.8 GB (explore 1.07 GB,
finra 0.68 GB, halts, regsho, logs), against about 33 GB for the whole tree. The market history that matters
is also inside the SQL backups (`IdiotProof_Research_GAP.bak` is 9 GB), so `bars` is a re-pull convenience, not
the only copy. If you later decide the 5-year bar pull is too costly to repeat, back it up separately and
infrequently (weekly), not nightly.

Implementation in the Launcher: after the main robocopy in `BackupService.Run`, run a second robocopy:

```
robocopy D:\IdiotProofData "<dated>\_IdiotProofData" /E /R:1 /W:1 /MT:2 /NFL /NDL /NJH /NJS /NP ^
  /XD bars tradeflow harness-bin harness-bin2 harness-bin3 harness-bin4 harness-bin5 ^
      harness-bin6 harness-bin7 harness-bin8 harness-bin9 harness-bin10 cli-bin sec-companyfacts
```

(or `/XD harness-bin*`, which robocopy accepts as a wildcard). Add `D:\IdiotProofData` as a second default source
constant next to `DefaultSource` (`BackupService.cs:15`), with its own exclude list rather than reusing
`ExcludeDirs`. Treat exit code under 8 as OK (`BackupService.ComputeOk`, `:198`) and fold it into `ok`.
Because it is an incremental-friendly mirror, consider a single rolling mirror at
`R:\Backup\IdiotProofData` instead of copying it into each dated folder, so 30 retained dated folders do not
each carry another copy.

## 5. Manual stop-gap (do NOT run from this CLI; for the user)

The databases are already covered nightly right now, so the only genuinely uncovered data is
`D:\IdiotProofData`. Run this from a normal PowerShell. `/MT:2` and `/IPG` keep RAM and I/O pressure low
while the gapper study is reading the tree. It writes about 1.8 GB.

```powershell
robocopy D:\IdiotProofData R:\Backup\IdiotProofData /E /R:1 /W:1 /MT:2 /IPG:50 /NP `
  /XD bars tradeflow harness-bin harness-bin2 harness-bin3 harness-bin4 harness-bin5 `
      harness-bin6 harness-bin7 harness-bin8 harness-bin9 harness-bin10 cli-bin sec-companyfacts
```

Notes:

- `R:\Backup\IdiotProofData` is outside the dated folders, so the Launcher's `--keep 30` prune will not touch it.
- Exit codes 0-7 are success; 8 or higher is a failure.
- To also protect the heavy `bars` tree once (27.7 GB, R: has 7.5 TB free), add
  `R:\Backup\IdiotProofData-bars` as a separate run when the study is idle.
- To take an extra database backup right now, run the Launcher's own command
  (`C:\Apps\MindAttic.Launcher\MindAttic.Launcher.exe backup`). It is **not** recommended during the study: it
  robocopies the whole `D:\Projects\MindAttic` tree and writes about 32 GB of `.bak` files.

## 6. Capacity note

Each night now writes about 32 GB of `.bak` (`Prose` 18.4 GB, `IdiotProof_Research_GAP` 9.1 GB,
`IdiotProof_Research_T375` 3.2 GB, `StreetSamurai` 2.7 GB). `--keep 30` implies about 1 TB of `.bak` on R:
(7.5 TB free, 42% used). That is fine today, but `Prose` and the research DBs dominate; consider keeping fewer
dated copies for those or backing them up weekly.

## 7. Status

IP-T449 stays **open**: the Launcher change (section 3) and the scope addition (section 4) are not made.
