/// <reference types="cypress" />

/**
 * Vault-backed AI generation path.
 *
 * The LLM call happens SERVER-side (StrategyScriptGenerator → LegionClient →
 * api.anthropic.com), so cy.intercept can never observe it from the browser.
 * What the browser CAN prove:
 *
 *   • The AI-assist pane dispatches generation and renders the returned
 *     IdiotScript chain into the script editor.
 *   • The page does NOT short-circuit with "No Claude API key configured" —
 *     i.e. the credential chain (env var → Vault providers.json →
 *     IConfiguration overlay) resolved a key before dispatch.
 *
 * Two variants:
 *   1. live   — opt-in via CYPRESS_LIVE_LLM=1 against a server WITHOUT
 *               IDIOTPROOF_FAKE_LLM: the response really came from Anthropic,
 *               so a rendered Stock.Ticker chain proves the key was accepted.
 *   2. always — runs in every invocation; with IDIOTPROOF_FAKE_LLM=1 the
 *               FakeLlmHandler answers, proving the page→Legion wiring
 *               (a page that bails on missing creds never renders a chain).
 */
describe("Vault-backed AI happy-path (live LLM)", () => {
    const live = Cypress.env("LIVE_LLM") === true || Cypress.env("LIVE_LLM") === "1";
    const testUser = () => `vault-ai-${Date.now()}@idiotproof.local`;
    const testPass = "Cy-IdiotProof-2026-Kx9!";

    before(function () {
        if (!live) {
            // Skip the whole describe block — the always-on variant below
            // still runs to give CI useful coverage without a real key.
            this.skip();
        }
    });

    it("Generate returns an IdiotScript chain from the real provider", () => {
        cy.registerAndLogin(testUser(), testPass);
        cy.visitInteractive("/builder");

        cy.typeStable("#b-title", "Vault happy-path");
        cy.typeStable("#b-symbol", "AAPL");
        cy.typeStable(
            "#b-prose",
            "When ADX is above 20 and the 9 EMA crosses above the 31 EMA on a volume-confirmed reclaim, go long."
        );

        cy.get("#b-generate").click();

        // The model output lands in the script editor. The grammar always
        // begins with Stock.Ticker(...) for stock-side describes. Allow up to
        // 60s for slower providers / cold starts.
        cy.get("#b-script", { timeout: 60_000 }).should("contain.value", "Stock.Ticker");

        // Negative space: the error label that renders when the chain failed
        // must not be on the page.
        cy.contains(/no claude api key configured/i).should("not.exist");
        cy.contains(/401|unauthorized/i).should("not.exist");
    });
});

/**
 * Always-on companion: proves the page-level wiring (AI-assist pane → server →
 * Legion → script editor) without a real key. Under IDIOTPROOF_FAKE_LLM=1 the
 * FakeLlmHandler returns the [[script: ...]] marker payload; a refactor that
 * stops dispatching to Legion (or that nulls the credential before dispatch)
 * renders an error instead and fails the assertions.
 */
describe("Vault-backed AI page wiring (no live call)", () => {
    const testUser = () => `vault-ai-spy-${Date.now()}@idiotproof.local`;
    const testPass = "Cy-IdiotProof-2026-Kx9!";

    it("clicking Generate renders the generated IdiotScript chain", () => {
        cy.registerAndLogin(testUser(), testPass);
        cy.visitInteractive("/builder");

        cy.typeStable("#b-title", "Wiring smoke");
        cy.typeStable("#b-symbol", "AAPL");
        cy.typeStable(
            "#b-prose",
            "Long when ADX above 20 and 9 EMA above 31 EMA on a 9 EMA reclaim with volume above 1.2x average. " +
                '[[script: Stock.Ticker("AAPL").RequireAdxAbove(20).RequireEmaStack(9, 31).OnReclaim(9).WithVolumeConfirm(1.2).Long().StopLoss(9.50).TakeProfit(12.00).Build()]]'
        );

        cy.get("#b-generate").click();

        cy.get("#b-script", { timeout: 30_000 }).should("contain.value", "Stock.Ticker");
        cy.contains(/no claude api key configured/i).should("not.exist");
        cy.contains(/generation error/i).should("not.exist");
    });
});
