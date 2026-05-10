# IdiotProof Project Rules

## Conversation
- A bare "do" / "do it" / "yes" from the user means "continue", "keep going", "proceed". Resume the current task without asking for clarification.

## Rate Limit & Context Protection

### Rate Limit (billing — HARD STOP at 96%)
See global rules in ~/.claude/CLAUDE.md. The rate-limit-monitor skill enforces:
- Warn every 5% starting at 80%, every 1% starting at 91%
- **Hard stop at 96%** — queue pending tasks to ~/.claude/rl-queue.json, write handoff to ~/.claude/rl-handoff.md
- Every exceeded limit = ~$30 charge on the credit card

### Context Window (conversation — HARD STOP at 96%)
- When approaching 96% context usage, STOP immediately
- Create a task list of all pending/in-progress work
- Write a handoff summary to memory so the next session can resume seamlessly
- Tell the user to take a break and come back after cooldown

## Code Style
- Do NOT use underscore-prefixed variables (e.g. `_myField`). Use `camelCase` for private fields without the underscore prefix.
- JSON for static data, SQL Server for runtime state. No Python scripts, no YAML.
- Web-only project (Blazor Server) + Console app for the Monitor. No MAUI host.

## Architecture Rules

### Storage system of record
- **SQL Server** (`IdiotProof` database on LocalDB by default; override via `ConnectionStrings__IdiotProof`) holds: Identity tables, `UserApiKeys`, `Strategies`, `Workspaces`, `AppSettings`, `AuditLog`, `UserPreferences`.
- **Per-user UI state** (open tabs, expand/collapse, last selected account, theme choice) is mirrored to `localStorage` for SSR pre-paint, with `UserPreferences` row in SQL as the canonical source.
- **MindAttic broker keyring** at `%APPDATA%\MindAttic\Brokers\providers.json` (mirroring `%APPDATA%\MindAttic\LLM\providers.json`) — alpaca-paper / alpaca-live entries override DB-stored keys.
- **Storage root** for the rest: `%LOCALAPPDATA%\MindAttic\IdiotProof\` (matches ThinkTank). `IDIOTPROOF_DATA_DIR` env-var overrides.

### Brokers
- **Alpaca-only** for the active build. IBKR adapter lives dormant in `IdiotProof.Brokers.Ibkr/` (not in `.sln`); re-enable steps in that project's README.
- `IBrokerClient` is the abstraction. New brokers implement it and register via `BrokerRouter`.

### LLM gateway — Legion (transport) + Vault (credentials)
- All LLM **communication** goes through `MindAttic.Legion` (`LegionClient` / `LLMVotingService`). **No direct Anthropic SDK or OpenAI SDK calls in feature code.**
- All LLM **credential reads** go through `MindAttic.Vault` (`LlmCredentialStore` for the file keyring, `LlmCredentialResolver` for the IConfiguration-aware resolver). The legacy `MindAttic.Legion.MindAtticCredentialStore` is no longer used in IdiotProof — do not reintroduce calls to it.
- Let Legion decide model selection / voter panel / quorum unless a specific task requires a fixed model. Configure voter panels via `legion.json` at the project root when overriding defaults.
- Claude API key resolution chain: explicit DI > env var > `%APPDATA%\MindAttic\LLM\providers.json` via Vault (canonical for the family) > IConfiguration overlay (`MindAttic:Vault:LLM:claude:apiKey` — User Secrets / App Service / Key Vault).

### Strategy DSL phases
Every authored strategy walks through fixed phases. The visual builder renders one card per phase; the parser rejects verbs used in the wrong phase.
1. **Setup** — ticker, session, account, window
2. **Filters** — regime preconditions (always-on gates: ADX, EMA stack, volume regime)
3. **Entry** — trigger conditions ("the fire")
4. **Order** — direction, quantity, type, price (`Quantity.Shares(N)` or `Quantity.Notional($)`)
5. **Risk** — stop, trailing
6. **Exit** — targets, time exits, condition exits

Branching uses **expression syntax**: `If(IsAboveVwap.And(IsEmaAbove(9))).Then(Long.Quantity(...)).ElseIf(...).Else(...)`. Conditions compose with `.And() / .Or() / .Not()`.

## Theme
- **Alpaca palette only** for now. Themeable via CSS custom properties under `:root[data-theme="alpaca"]`. New themes drop in as additional `_theme-{name}.css` files; Razor components reference variables, not raw colors.
- Theme stored in `UserPreferences.Theme` (SQL); mirrored to `localStorage` for pre-paint flash protection.

## Tests
- **Backend**: NUnit (one Tests project per source project).
- **Frontend**: Cypress in `tests/IdiotProof.Cypress/`.
- Tests are written **after** features ship — see the README for the canonical sequence.

## World Rules (trading-domain invariants)
- Sandbox broker is always registered as the safe fallback in `BrokerRouter`. Live trades require explicit user opt-in (`AlpacaIsPaper = false`) plus a confirmation modal.
- LIVE trading mode requires the user to acknowledge the danger banner. The Live account pill in the top-left renders with a red outline; Paper renders with the Alpaca brand-yellow outline.
- Risk Guardian is the final gate before any order placement. It can veto regardless of strategy/LLM-voting consensus.
