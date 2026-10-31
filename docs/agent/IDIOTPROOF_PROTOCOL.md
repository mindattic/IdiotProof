# IdiotProof agent protocol

How an assistant (Claude Code, any MCP host, any LLM with a shell) drives IdiotProof on a
person's behalf. The person speaks plainly; you turn it into exact `idiotproof <noun> <verb>`
commands or `mcp__idiotproof__<noun>_<verb>` tool calls, all served by the IdiotProof Hub.
This file is the etiquette and the map; the full command reference is generated:
[`docs/CLI_COMMANDS.md`](../CLI_COMMANDS.md), [`docs/MCP_TOOLS.md`](../MCP_TOOLS.md) and
[`docs/agent/operation-catalog.json`](operation-catalog.json) (regenerate all three with
`idiotproof agent export`).

## 1. Bootstrap (every session, first)

```text
idiotproof agent bootstrap            # Hub health, owner, catalog size, docs, etiquette, exit codes
idiotproof agent bootstrap --json     # the same, machine-readable
```

- `agent bootstrap` runs in the CLI itself and works with the Hub down. If it says the Hub is
  not running, start it: `idiotproof hub start` (with `IDIOTPROOF_HUB_MONITOR=0` in the
  environment the trading loop stays off; without it the Hub also hosts the Monitor, which
  places real orders for active strategies). Never stop or restart a Hub you did not start
  unless the person asks.
- Then learn the surface: `idiotproof help`, `idiotproof <noun> --help`,
  `idiotproof agent catalog --json` (the MCP server exposes the same catalog as tools).
- Every command acts for one owner: `--user <guid>`, else the saved default
  (`idiotproof users use <guid>`), else the Hub's default when only one user exists.
  `idiotproof users whoami` says which and why.

## 2. The rules

1. **Everything goes through the Hub.** Never call Alpaca, the database or Vault yourself, and
   never edit `C:\Apps\IdiotProof` or the SQL tables to "make something happen". If an
   operation does not exist, say so and offer to file a ticket.
2. **Exit codes mean something.** `0` ok · `1` failed (or Hub not running) · `2` usage error
   (fix the command, use `--help`) · `3` blocked by a safety gate · `4` confirmation required.
3. **Exit 3 is final.** A gate (Risk Guardian, kill switch, market-hours guard, Mock-feed guard,
   Live elevation) said no. Report the reason verbatim. Do not retry with different numbers,
   another mode, or another command to get around it.
4. **Exit 4 means nothing happened yet.** The result carries a preview and a single-use code
   (6 characters, about 2 minutes, bound to the exact action). Show the person the preview
   verbatim and ask. Only on their explicit yes, re-run the identical command with
   `--confirm <CODE>` (MCP: the same tool with `confirmation_code`). Never confirm on your own,
   never reuse a code, never change the arguments between preview and confirm (any change
   voids the code).
5. **Modes.** Sandbox is IdiotProof's simulator (always safe), Paper is Alpaca's paper account,
   Live is real money. Sandbox and Paper run immediately; every Live order, cancel, replace,
   close, exercise and crypto order needs the confirm step; funding and transfers always do.
   Do not pick Live unless the person said Live.
6. **Times are Eastern.** Every time IdiotProof prints is ET (America/New_York). Quote it as
   ET; never convert to UTC or the person's local zone.
7. **Read before you write.** Prefer read operations (`list`, `show`, `status`, `preview`) to
   answer questions. Use `--json` when you will parse the output; show people the human text.
8. **No secrets in chat.** For `keys set`, pass `--secret -` so the value is read from stdin; never echo a key back.

## 3. Intent → operation

| The person says | Run |
|---|---|
| "How's my account / what's my P&L today?" | `account summary`, `account history --period 1M` |
| "Is the market open?" | `account clock`, `account calendar` |
| "What do I own?" / "close my NVDA" | `positions list`, `positions close NVDA` (gateway; Live → code) |
| "Buy 10 AAPL" / "short TSLA" | `trade buy AAPL 10 --stop 180 --mode paper`, `trade short TSLA 5 --stop 260` (a stop is required: IP-LAW-2; `trade preview` shows the gate checks without sending) |
| "Spend $500 on SPY" | `trade notional SPY 500` |
| "Bracket order with a stop" | `orders bracket ...`, `orders oco`, `orders oto` |
| "What orders are working? cancel them" | `orders list`, `orders cancel <id>`, `orders cancel-all` |
| "Quote / chart / news on X" | `market quote X`, `market chart X`, `market news X`, `market snapshot X` |
| "What's moving?" | `market movers`, `market most-actives`, `scan movers` |
| "Options on X" | `options chain X`, `options quote <contract>`, `options greeks`, `options buy/sell` |
| "Crypto" | `crypto latest BTC/USD`, `crypto buy BTC/USD 0.01`, `crypto positions` |
| "Make a strategy: buy NVDA if it reclaims VWAP, 2% stop" | `strategy from-text "..." --mode sandbox`, then `strategy show`, `strategy stamp` |
| "Turn it on / off" | `strategy activate <id>`, `strategy deactivate <id>` (Live → code) |
| "Why didn't it fire?" | `strategy progress <id>`, `strategy fires <id>`, `monitor status` |
| "Gappers this morning" | `gapper profiles`, `gapper arm ...`, `gapper list`, `gapper auto status` |
| "Replay / backtest it" | `replay run ...`, `backtest run ...` |
| "Is the Risk Guardian on? kill everything" | `risk status`, `risk kill-switch on` |
| "What does the LLM panel think?" | `llm panel`, `llm vote ...` |
| "Research on X" | `research ticker X`, `research claims` |
| "What did I trade?" | `diary list`, `diary pnl`, `audit tail` |

## 4. Knowing the project (docs operations)

The Hub indexes the BIBLE, AMENDMENTS, README, user stories, tickets, every library's XML
doc comments with `path:line`, and the static catalogs. Use these instead of guessing:

```text
idiotproof docs search <words>        # ranked across everything
idiotproof docs law 3                 # an IP-LAW in full (docs law = the list)
idiotproof docs section 4             # a BIBLE section (docs section = table of contents)
idiotproof docs story K1              # a user story with its status and proving tests
idiotproof docs explain TransactionGateway   # doc comment, declaration path:line, callers
idiotproof docs ticket IP-T259        # a ticket with its commits and dependencies
idiotproof docs dsl StopLoss          # an IdiotScript verb: phase, signatures, example
```

The laws in [docs/BIBLE.md §5](../BIBLE.md#IP-§5) are non-negotiable; if a request would break
one (for example "skip the stop loss" breaks IP-LAW-2), say so instead of looking for a way.

## 5. MCP specifics

- One tool per operation, named `<noun>_<verb>` (spaces and hyphens become `_`). Tool input is
  the operation's parameters in snake_case; money and destructive tools also take `confirmation_code`.
- Read tools may be allow-listed. Money tools (`trade_*`, `orders_*` that change orders,
  `positions_close*`, `options_buy|sell|close|exercise`, `crypto_buy|sell|withdraw`) are
  deliberately not, so the host asks the person before a Live action is confirmed.
- Resources: `idiotproof://docs/bible`, `idiotproof://docs/user-stories`,
  `idiotproof://docs/protocol` and `idiotproof://catalog` serve the BIBLE, the stories, this
  file and the live operation catalog.

## 6. When something fails

- `Hub not running` → `idiotproof hub start`, then retry once.
- `Usage error` → read `idiotproof <noun> <verb> --help`; fix the arguments.
- `Blocked` → report it (rule 3).
- Anything else → show the message; `idiotproof hub logs` has the Hub side.
