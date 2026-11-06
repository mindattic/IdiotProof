Recreate the Alpaca web app's **Positions — All** tab (ticket IP-T439) from live account data.

Run:

```
idiotproof positions list --user a99504c3-0759-4256-8abb-a1f5c643808c --mode live --json
```

($ARGUMENTS, if given, replaces `--mode live` — e.g. `/positions paper`.)

Take every position in `.data[].positions[]` (every `account`, unless $ARGUMENTS restricts mode),
sort alphabetically by `symbol`, and render one row per position with these columns, in this order:

`Asset | Price | Qty | Side | Market Value | Outcome | Avg Entry | Cost Basis | Today's P/L (%) | Today's P/L ($) | Total P/L (%) | Total P/L ($)`

Per-row values, from each position's JSON fields:
- **Asset** = `symbol`
- **Price** = `current_price`
- **Qty** = `quantity` as-is (keeps its sign; a short shows e.g. `-1`)
- **Side** = `Long` if `quantity > 0`, else `Short`
- **Market Value** = `market_value`
- **Outcome** = `-` (Alpaca doesn't populate this for equities either — don't invent a value)
- **Avg Entry** = `average_price`
- **Cost Basis** = `average_price * quantity`
- **Today's P/L (%)** = `(current_price - last_day_price) / last_day_price`
- **Today's P/L ($)** = `unrealized_intraday_pnl`
- **Total P/L (%)** = `unrealized_pnl / abs(average_price * quantity)`
- **Total P/L ($)** = `unrealized_pnl`

Formatting:
- Money columns: `$1,234.56`, signed as `+$12.34` / `-$12.34` (always show the sign on P/L columns).
- Percent columns: `+8.11%` / `-3.11%`, 2 decimals, always signed.
- Color P/L cells (Today's and Total, both % and $) green (`\x1b[32m`) when positive, red
  (`\x1b[31m`) when negative, plain when exactly zero — reset with `\x1b[0m`. Only when this is
  an interactive terminal; skip color if output is being piped/captured.
- Qty: 0 decimal places unless fractional (e.g. `2.5`), matching Alpaca's own display.

After the table, one line: `<N> positions, <Live/Paper/both> account(s), read live via positions
list.`
