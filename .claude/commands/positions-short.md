Recreate the Alpaca web app's **Positions — Short** tab (ticket IP-T439) from live account data.

Run:

```
idiotproof positions list --user a99504c3-0759-4256-8abb-a1f5c643808c --mode live --json
```

($ARGUMENTS, if given, replaces `--mode live` — e.g. `/positions-short paper`.)

This is exactly `/positions` filtered to positions where `quantity < 0` (shorts only) — follow all
the same column list, per-row formulas, and formatting rules defined in
`.claude/commands/positions.md`, just drop every row with `quantity >= 0` before rendering, still
sorted alphabetically by `symbol`. The `Side` column will read `Short` on every row, `Qty` and
`Market Value` stay negative.

After the table, one line: `<N> short positions, <Live/Paper/both> account(s), read live via
positions list.` If there are no short positions, say so plainly instead of printing an empty
table.
