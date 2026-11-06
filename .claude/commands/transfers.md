Recreate the Alpaca web app's **Transfer History** table (ticket IP-T441) from live account data.

Run:

```
idiotproof account activities --user a99504c3-0759-4256-8abb-a1f5c643808c --mode live --type CSD,CSW,JNLC --since 2026-01-01 --limit 200 --json
```

($ARGUMENTS, if given: a date like `2026-06-01` replaces `--since`; `paper` replaces `--mode live`.)

Render one row per activity, newest first (the CLI already returns them newest-first), with these
columns, in this order:

`Date | From | To | Type | Amount | Status`

Per-row values, from each activity's JSON fields (`type`, `time_et`, `amount`, `description`):
- Parse `description`, a `key: value, key: value, ...` string — pull out `type` (`instant_ach` or
  `ach`) and `direction` (`INCOMING` or `OUTGOING`).
- **Date** = `time_et` (`yyyy-MM-dd`). Note: this is the activity log's settlement/effective date,
  not the dashboard's initiation timestamp — they can differ by a day or two for the same
  transfer (confirmed on IP-T441: 2 of 5 real transfers were off by 1-2 days). Don't reformat it
  to look like a precise timestamp; it isn't one.
- **From** = `-` when `direction` is `INCOMING`; `Alpaca Account (<account_number>)` when
  `OUTGOING`. (`account_number` comes from `GET /v2/account` if you need it — `252673877` for
  this account, but read it live rather than hardcoding.)
- **To** = the reverse: `Alpaca Account (<account_number>)` when `INCOMING`, `-` when `OUTGOING`.
- **Type** = `INSTANT_ACH` if the parsed type is `instant_ach`, else `ACH`. (`JNLC` activities, if
  any show up, are internal journal transfers — render their own `type` field uppercased instead.)
- **Amount** = the activity's `amount`, always signed: `+$800.00` for incoming, `-$150.43` for
  outgoing (CSW amounts already come back negative from the API — don't double-negate).
- **Status** = `posted`. Don't invent `Deposit Complete` / `Deposit Pending` — the activity log has
  no status field, and IP-T441 confirmed a transfer can appear here while Alpaca's own dashboard
  still shows it pending (the $800 CSD on 2026-11-06 was "Deposit Pending" there). Say plainly
  that pending-vs-complete isn't available from this endpoint if asked.

Formatting: right-align Amount, color it green for incoming / red for outgoing when this is an
interactive terminal.

After the table, one line: `<N> transfers since <since date>, Live account, read live via account
activities (CSD/CSW/JNLC).`
