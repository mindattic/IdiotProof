/// <reference types="cypress" />

/**
 * Strategies-page per-row broker-mode toggle (Paper ⇄ Live).
 *
 * Replaces the old API Keys page live-mode-confirmation tests (deleted from
 * 03_api_keys.cy.ts) — that Paper/Live toggle-with-modal UI moved off the
 * API Keys page entirely. Broker mode is now switched per strategy row on
 * /strategies: clicking the row's mode button (Live → Paper is immediate;
 * Paper/Sandbox → Live opens a password-confirmation modal that verifies the
 * real account password via LiveModeElevationService and grants a 5-minute
 * elevation window (StrategyListPageBase.ToggleBrokerModeAsync /
 * ConfirmPasswordModalAsync) — so the confirm step below reuses the same
 * password the test registered the account with.
 */
describe("Strategies broker-mode toggle (authenticated)", () => {
    const testPass = "Cy-IdiotProof-2026-Kx9!";

    it("Cancel keeps the row in its prior mode; Confirm flips it to Live", () => {
        const email = `livemode-${Date.now()}@idiotproof.local`;
        cy.registerAndLogin(email, testPass);

        // Seed a strategy through the AI-assist pane — same entry path as
        // 07_condition_progress.cy.ts.
        cy.visitInteractive("/builder");
        cy.get("#b-title").type("Live mode toggle");
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

        cy.visitInteractive("/strategies");
        cy.get('[data-cy="broker-mode-btn"]').first().should("contain.text", "Paper").as("modeBtn");

        // New strategies default to Paper mode — clicking opens the password modal.
        cy.get("@modeBtn").click();
        cy.get('[data-cy="pw-modal"]').should("be.visible");
        cy.contains(/enable live trading/i).should("be.visible");
        cy.contains(/real money/i).should("be.visible");
        cy.checkPageA11y();

        // Cancel — modal closes, mode button unchanged.
        cy.get('[data-cy="pw-cancel"]').click();
        cy.get('[data-cy="pw-modal"]').should("not.exist");
        cy.get("@modeBtn").should("contain.text", "Paper").and("not.have.class", "btn-danger");

        // Re-open, enter a password, confirm — mode flips to Live.
        cy.get("@modeBtn").click();
        cy.get('[data-cy="pw-modal"] input[type="password"]').type(testPass);
        cy.get('[data-cy="pw-confirm"]').click();
        cy.get('[data-cy="pw-modal"]', { timeout: 10000 }).should("not.exist");
        cy.get("@modeBtn").should("contain.text", "Live").and("have.class", "btn-danger");
    });
});
