---
name: run
description: Build and run the IdiotProof Hub (it hosts the Monitor and serves the CLI and MCP; there is no web UI). No arguments needed.
---

When invoked:

1. Run: `dotnet run --project IdiotProof.Hub` with `IDIOTPROOF_HUB_MONITOR=0` unless the user asks for the trading loop (the Monitor places real orders), and `IDIOTPROOF_HUB_PORT` set to a free port if the deployed Hub already holds 5910.
2. Stream the output so the user can see build progress and any errors; the Hub prints its port and operation count when ready.
3. If the build fails, summarize the error and suggest a fix.
