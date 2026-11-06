Recreate the Alpaca web app's **Orders** tab (ticket IP-T439) from live account data. Default
(no $ARGUMENTS) matches the "Status: Open" filter from the screenshot; `/orders all` matches the
unfiltered view.

Run:

```
idiotproof orders list --user a99504c3-0759-4256-8abb-a1f5c643808c --mode live --status open --json
```

If $ARGUMENTS is `all` or `closed`, use that as `--status` instead of `open` (pass `--limit 50` if
you want more than the default 50). Any other $ARGUMENTS text is a `--mode` override instead
(e.g. `/orders paper`).

Flatten every order AND every bracket/OCO `legs[]` child into one flat row list (no special
indent — the real UI lists each leg as its own plain row), newest `submitted_utc` first. Render
these columns, in this order:

`Asset | Order Type | Side | Outcome | Qty | Filled Qty | Avg. Fill Price | Status | Source | Submitted At`

Per-row values:
- **Asset** = `symbol`
- **Order Type** = `Market` for `type: market`; `Limit @ $<limit_price>` for `limit`; `Stop @
  $<stop_price>` for `stop`; `Stop Limit @ $<limit_price>` for `stop_limit`.
- **Side** = `buy` or `sell` (`side`, lowercase, as Alpaca shows it)
- **Outcome** = `-` (not populated for equities, don't invent a value)
- **Qty** = `qty`
- **Filled Qty** = `filled_qty`
- **Avg. Fill Price** = `filled_avg_price` formatted as `$1,234.56`, else `-` when absent
- **Status** = `status` as-is (`new`, `held`, `filled`, `canceled`, `expired`, `rejected`, ...)
- **Source** = `client_order_id`, else `-` when absent
- **Submitted At** = `submitted_utc` converted to ET, formatted `Nov 04, 2026, 10:07 AM`

Formatting:
- Right-align Qty and Filled Qty; left-align everything else.
- No color coding needed (the screenshot doesn't use any here).

After the table, one line: `<N> orders (<status> filter), <Live/Paper/both> account(s), read live
via orders list.`
