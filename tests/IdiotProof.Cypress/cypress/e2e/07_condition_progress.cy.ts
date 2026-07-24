/// <reference types="cypress" />

/**
 * Strategies-page live condition-progress badge (IP-US-E2).
 *
 * The Monitor upserts one ConditionProgress row per strategy per tick; the
 * Strategies page polls those rows every 5 seconds and renders a
 * "3/5 · IsOnReclaim(9)" chip on active rows. This spec stands in for the
 * Monitor with the seedConditionProgress task (direct SQL upsert via sqlcmd
 * into the same row the repository writes), then asserts the page picks it
 * up on its poll without a reload:
 *
 *   1. Active strategy with no progress row → "awaiting first evaluation".
 *   2. Seed 3/5 + first failing verb → badge shows "3/5 · IsOnReclaim(9)".
 *   3. Seed a full pass (5/5, verb cleared) → badge flips to "5/5", no verb.
 */
describe("Condition-progress live badge (authenticated)", () => {
    const testPass = "Test1234!";

    it("shows awaiting state, then live N/M badge, then full pass", () => {
        const email = `progress-${Date.now()}@idiotproof.local`;
        cy.registerAndLogin(email, testPass);

        // Seed a strategy through the AI-assist pane — the same entry path the
        // other authenticated specs use. Server runs with IDIOTPROOF_FAKE_LLM=1;
        // the [[script: ...]] marker picks the script.
        cy.visit("/builder");
        cy.get("#b-title").type("Progress badge");
        cy.get("#b-symbol").type("AAPL");
        cy.get("#b-prose").type(
            "When ADX above 20 and 9 EMA above 31 EMA, on a 9 EMA reclaim with volume above 1.2x average, go long. " +
                '[[script: Stock.Ticker("AAPL").RequireAdxAbove(20).RequireEmaStack(9, 31).OnReclaim(9).WithVolumeConfirm(1.2).Long().StopLoss(9.50).TakeProfit(12.00).Build()]]',
            { parseSpecialCharSequences: false }
        );
        cy.get("#b-generate").click();
        cy.get("#b-script", { timeout: 30000 }).should("contain.value", "Stock.Ticker");
        cy.contains("button", "Save").click();
        cy.contains(/saved\./i, { timeout: 10000 });

        // Activate it — the badge only renders on active rows.
        cy.visit("/strategies");
        cy.get('input[type="checkbox"][id^="toggle-"]').first().check();

        // No progress row yet → awaiting chip.
        cy.contains(/awaiting first evaluation/i, { timeout: 10000 }).should("be.visible");

        // Seed a Monitor-style partial pass and wait out one poll cycle (5s).
        cy.get("tr[data-strategy-id]")
            .first()
            .invoke("attr", "data-strategy-id")
            .then((strategyId) => {
                cy.task("seedConditionProgress", {
                    strategyId,
                    passed: 3,
                    total: 5,
                    verb: "IsOnReclaim(9)",
                });
                cy.contains("3/5", { timeout: 15000 }).should("be.visible");
                cy.contains("IsOnReclaim(9)").should("be.visible");

                // Full pass clears the verb and flips the chip style.
                cy.task("seedConditionProgress", {
                    strategyId,
                    passed: 5,
                    total: 5,
                    verb: null,
                });
                cy.contains("5/5", { timeout: 15000 }).should("be.visible");
                cy.contains("IsOnReclaim(9)").should("not.exist");
            });
    });
});
