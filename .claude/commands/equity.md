Recreate the Alpaca web app's **Your Portfolio** equity chart (ticket IP-T439) — a Braille line
chart of equity over time. This view already exists verbatim in the CLI; this command just points
at it with the right window. Don't hand-build a chart — `idiotproof account history` already
renders this with `MindAttic.Helpers.Ascii` (`IdiotProof.Operations/Account/AccountHistoryOperation.cs`).

Default (no $ARGUMENTS) matches the screenshot's "1D" tab:

```
idiotproof account history --user a99504c3-0759-4256-8abb-a1f5c643808c --mode live --period 1D --timeframe 15Min --extended
```

$ARGUMENTS maps to the other tabs in the screenshot, same account/user otherwise:
- `1M` -> `--period 1M`
- `1Y` -> `--period 1A`
- `All` / `all` -> `--period all`
- anything else is passed straight through as `--period <value>` (e.g. `/equity 3M`)

Run the command and show its output exactly as printed — the title line already carries the
equity figure and signed % change (matching the screenshot's `$3,140.74  +6.07%` header), the
Braille plot already carries the time-of-day ticks, and the summary block below it (start/end
equity, P&L, high/low) is extra detail the screenshot's tile doesn't show but is worth keeping.
