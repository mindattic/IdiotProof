---
name: run
description: Build and run the IdiotProof Blazor web app. No arguments needed.
---

When invoked:

1. Run: `dotnet run --project IdiotProof.Blazor` (the trading console is a separate process: `dotnet run --project IdiotProof.Monitor`)
2. Stream the output so the user can see build progress and any errors
3. If the build fails, summarize the error and suggest a fix
