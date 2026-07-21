---
name: replays
description: Regenerate the replay archive from SQL and publish it to mindattic.com/idiotproof/replays/ via FTP. Use /replays to publish all replays. Manual step — the Monitor never touches FTP.
---

When invoked, publish the full replay archive in two steps and report the result.

**1. Regenerate the archive HTML from SQL** (day-grouped root index + per-ticker indexes + every run page; writes to `D:\Projects\MindAttic\mindattic.com\idiotproof\replays`):

```
dotnet run --project D:\Projects\MindAttic\IdiotProof\IdiotProof.Monitor --no-build -c Release -- replay-regen
```

(If the Release binary isn't built, drop `--no-build` or build `IdiotProof.Monitor -c Release` first.)

**2. FTPS-upload the whole archive** (recursive → `/mindattic.com/idiotproof`, site slug `idiotproof-replays`) via the sibling **MindAttic.Deploy** repo:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; node --use-system-ca src/deploy.js --site idiotproof-replays"
```

Then report: runs/tickers regenerated, files uploaded (flag any failures), and the live URL `mindattic.com/idiotproof/replays/`.

Notes:
- `replay-regen` re-renders only what's already in SQL (`ReplayRun` rows) — it does NOT fetch new market data. To capture NEW replays first, run `scan` or `replay <ticker>` before publishing.
- The always-on Monitor logs trades to SQL + the trade diary; it does not generate replays or touch FTP. Publishing is always this manual `/replays` step.
- Credentials: MindAttic.Vault at `%APPDATA%\MindAttic\Deploy\ftp.json`.
