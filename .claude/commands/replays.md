Regenerate the full replay archive from SQL and publish it to the live site via FTP.

The replay pages under `mindattic.com/idiotproof/replays/` are generated FROM the `ReplayRun` rows in SQL (the day-grouped root index, each per-ticker index, and every run page), then FTPS-uploaded through **MindAttic.Deploy** (sibling repo at `D:\Projects\MindAttic\MindAttic.Deploy`, site slug `idiotproof-replays`). This is the manual publish step — the Monitor never touches FTP.

Run these two steps and report the result:

**1. Regenerate the archive HTML from SQL** (writes to `D:\Projects\MindAttic\mindattic.com\idiotproof\replays`):

```
dotnet run --project D:\Projects\MindAttic\IdiotProof\IdiotProof.Monitor --no-build -c Release -- replay-regen
```

**2. FTPS-upload the whole archive** (recursive, to `/mindattic.com/idiotproof`):

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; node --use-system-ca src/deploy.js --site idiotproof-replays"
```

Then report: how many runs/tickers were regenerated, how many files uploaded (and any failures), and the live URL `mindattic.com/idiotproof/replays/`.

Notes:
- If the Monitor Release binary isn't built yet, drop `--no-build` on step 1 (or build first).
- `replay-regen` only re-renders what's already in SQL — it does not fetch new market data. To capture NEW replays first, run `scan` or `replay <ticker>` before `/replays` (or say so and I'll run it).
- Catalog entry: `MindAttic.Deploy/projects.json` -> site slug `idiotproof-replays` -> `/mindattic.com/idiotproof`.
- Credentials: MindAttic.Vault at `%APPDATA%\MindAttic\Deploy\ftp.json`.
