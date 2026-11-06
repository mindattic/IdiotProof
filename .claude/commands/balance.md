Recreate the Alpaca web app's **Balance** panel (ticket IP-T439) from live account data — a
two-column "Last Close" vs "Current" table.

Run (Git Bash mangles a leading `/v2/...` into a Windows path, so `MSYS_NO_PATHCONV=1` is required):

```
MSYS_NO_PATHCONV=1 idiotproof alpaca raw GET /v2/account --user a99504c3-0759-4256-8abb-a1f5c643808c --mode live --json
```

($ARGUMENTS, if given, replaces `--mode live` — e.g. `/balance paper`.)

Parse the JSON body (`.data.body` or equivalent) and render this exact table, right-aligning the
two money columns. Header: `Balance | Last Close on <balance_asof, e.g. "Nov 5th"> | Current`.

| Section | Row | Current | Last Close |
|---|---|---|---|
| **Buying Power** | RegT Buying Power | `regt_buying_power` | `-` |
| | Effective Buying Power | `effective_buying_power` | `-` |
| | Options Buying Power | `options_buying_power` | `-` |
| | Non-Marginable Buying Power | `non_marginable_buying_power` | `-` |
| **Margin** | Initial Margin | `initial_margin` | `-` |
| | Maintenance Margin | `maintenance_margin` | `last_maintenance_margin` |
| **Cash** | Cash | `cash` | `-` |
| | Cash Withdrawable | `not exposed by Trading API` | `-` |
| | Pending Transfer Out | `not exposed by Trading API` | `-` |
| | Pending Transfer In | `not exposed by Trading API` | `-` |
| **Positions** | Equity | `equity` | `last_equity` |
| | Long Market Value | `long_market_value` | `-` |
| | Short Market Value | `short_market_value` | `-` |
| | Position Market Value | `position_market_value` | `-` |
| **Miscellaneous** | Accrued Fees | `accrued_fees` | `-` |
| | Held ACH Deposits | `not exposed by Trading API` | `-` |

Formatting rules:
- Money: `$1,234.56`; negative as `-$1,234.56` (never a leading `-` before `$`... i.e. `-$4,779.08`).
- Section labels (**Buying Power**, **Margin**, **Cash**, **Positions**, **Miscellaneous**) print
  as their own bold row with no values, exactly like the screenshot's section headers.
- Don't invent an Effective-Buying-Power Last Close figure — the real Alpaca dashboard appears to
  derive it from `last_maintenance_margin` with a sign flip, but that formula isn't confirmed
  against the Trading API, so leave it `-` rather than guess (see IP-T439 notes).
- The three "not exposed by Trading API" rows are a real gap, verified 2026-11-06: `GET
  /v2/account/transfers` 404s (that data lives on Alpaca's Broker API, which this account doesn't
  have). Say so plainly instead of printing `$0.00`.

After the table, one line: `Balance as of <balance_asof>, read live via /v2/account.`
