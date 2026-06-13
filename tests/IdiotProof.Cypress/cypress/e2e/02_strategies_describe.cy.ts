/// <reference types="cypress" />

/**
 * Authenticated user-flow tests covering the AI-assist → Strategies-page loop:
 *   1. Register a fresh test account.
 *   2. Open /builder, fill title/symbol, describe the strategy in prose.
 *   3. Generate (server-side fake LLM), confirm IdiotScript lands in the editor, save.
 *   4. Visit /strategies, confirm the saved row appears.
 *   5. Toggle Active, confirm persistence.
 *
 * Notes:
 *   • The LLM call happens SERVER-side (StrategyScriptGenerator → LegionClient),
 *     so cy.intercept can never see it. The server must run with
 *     IDIOTPROOF_FAKE_LLM=1: FakeLlmHandler answers the Legion call with the
 *     IdiotScript embedded in the prose's [[script: ...]] marker.
 *   • Each test re-registers a unique email so runs don't collide.
 */
describe("AI-assist loop (authenticated)", () => {
    const testUser = () => `test-${Date.now()}@idiotproof.local`;
    const testPass = "Test1234!";

    it("registers, describes a strategy, saves, and finds it on /strategies", () => {
        const email = testUser();
        cy.registerAndLogin(email, testPass);

        cy.visitInteractive("/builder");
        cy.typeStable("#b-title", "Smoke pullback");
        cy.typeStable("#b-symbol", "AAPL");

        // The [[script: ...]] marker tells FakeLlmHandler (IDIOTPROOF_FAKE_LLM=1)
        // exactly which IdiotScript to return — no live API key needed.
        cy.typeStable(
            "#b-prose",
            "When ADX above 20 and 9 EMA above 31 EMA, on a 9 EMA reclaim with volume above 1.2x average, go long. " +
                '[[script: Stock.Ticker("AAPL").RequireAdxAbove(20).RequireEmaStack(9, 31).OnReclaim(9).WithVolumeConfirm(1.2).Long().StopLoss(9.50).TakeProfit(12.00).Build()]]'
        );
        cy.get("#b-generate").click();

        // The generated chain lands in the script editor textarea.
        cy.get("#b-script", { timeout: 30000 }).should("contain.value", "Stock.Ticker");

        cy.contains("button", "Save").click();
        cy.contains(/saved\./i, { timeout: 10000 });

        cy.visit("/strategies");
        cy.contains("Smoke pullback");
        cy.contains("AAPL");
    });

    it("activate toggle persists across reloads", () => {
        const email = testUser();
        cy.registerAndLogin(email, testPass);

        // Seed a strategy through the builder so this user has one to toggle.
        cy.visitInteractive("/builder");
        cy.typeStable("#b-title", "Toggle persistence");
        cy.typeStable("#b-symbol", "MSFT");
        cy.typeStable(
            "#b-prose",
            'Reclaim long. [[script: Stock.Ticker("MSFT").OnReclaim(9).Long().StopLoss(9.50).TakeProfit(12.00).Build()]]'
        );
        cy.get("#b-generate").click();
        cy.get("#b-script", { timeout: 30000 }).should("contain.value", "Stock.Ticker");
        cy.contains("button", "Save").click();
        cy.contains(/saved\./i, { timeout: 10000 });

        cy.visitInteractive("/strategies");
        cy.get('input[type="checkbox"][id^="toggle-"]').first().check();
        // The toggle writes through to SQL; the reloaded page renders the
        // persisted state server-side.
        cy.reload();
        cy.get('input[type="checkbox"][id^="toggle-"]').first().should("be.checked");
    });
});
