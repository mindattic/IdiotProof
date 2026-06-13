# IdiotProof.Cypress

End-to-end UI tests for IdiotProof's Blazor Server app.

## Run

```bash
# 1. Start the dev server in another terminal — with the fake-LLM seam on so
#    the AI-assist specs are deterministic and never call a vendor:
#    (PowerShell)
#    $env:IDIOTPROOF_FAKE_LLM='1'; $env:ClaudeApiKey='fake-llm-e2e-key'
#    dotnet run --project ../../IdiotProof.Blazor

# 2. Install Cypress (first time only)
npm install

# 3. Run headlessly (CI mode)
npm run ci

# Or open the Cypress runner for interactive debugging
npm run open
```

## Configuration

`cypress.config.ts` defaults `baseUrl` to `https://localhost:5001`. Override
via env var (the dev launch profile listens on 65025):

```bash
CYPRESS_BASE_URL=https://localhost:65025 npm run run
```

The condition-progress spec seeds rows straight into SQL via `sqlcmd`
(LocalDB by default; override `IDIOTPROOF_SQL_SERVER` / `IDIOTPROOF_SQL_DB`).

## The fake-LLM seam

The Describe/AI-assist flow calls the LLM **server-side**
(`StrategyScriptGenerator` → `LegionClient`), so `cy.intercept` can never see
or stub it. Instead, start the server with `IDIOTPROOF_FAKE_LLM=1`:
`FakeLlmHandler` answers the Legion call with the IdiotScript embedded in the
prose's `[[script: ...]]` marker. Opt in to a real round-trip with
`CYPRESS_LIVE_LLM=1` against a server *without* the seam.

## Specs

- `01_smoke.cy.ts` — public-route renders, auth redirect
- `02_strategies_describe.cy.ts` — full authenticated flow: register → AI
  assist → generate → save → activate → persist
- `03_api_keys.cy.ts` — key masking, Paper default, live-mode danger modal
- `04_vault_backed_ai.cy.ts` — Vault-backed generation wiring (+ opt-in live)
- `05_build_samples.cy.ts` — NCI/ERNA/SUNE sample round-trips
- `06_backtest.cy.ts` — backtest replay renders results
- `07_condition_progress.cy.ts` — live `N/M · verb` badge on /strategies

## Adding tests

Drop new `*.cy.ts` files into `cypress/e2e/`. Custom commands live in
`cypress/support/commands.ts`. The `Cypress.Commands.add(...)` calls extend
the global `Chainable` interface so TypeScript hints work in your spec.
