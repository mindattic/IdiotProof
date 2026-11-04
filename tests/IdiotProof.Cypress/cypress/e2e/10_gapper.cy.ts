/// <reference types="cypress" />

/**
 * /gapper tab (IP-US-K7): queue a gapper, see it listed with its live-state
 * chip, pause it, then remove it.
 *
 * The queued row is a real active Strategy for a brand-new user with no broker
 * keys (so it routes to the Sandbox broker). The spec pauses and removes it
 * straight away so nothing is left armed for a Monitor sharing the database.
 *
 * Live state: with the gapper active and no ConditionProgress row the chip
 * reads "awaiting first evaluation"; seeding a row (standing in for a Monitor
 * tick) flips it to "N/M · verb" on the page's 5s poll.
 */
describe("Gapper tab (authenticated)", () => {
    const testPass = "Cy-IdiotProof-2026-Kx9!";
    const symbol = "CYGAP";

    it("queues a gapper, shows live state, pauses and removes it", () => {
        cy.registerAndLogin(`gapper-${Date.now()}@idiotproof.local`, testPass);
        cy.visitInteractive("/gapper");

        cy.get("#gapper-heading").should("contain.text", "Gapper");
        cy.contains("No gappers queued").should("be.visible");
        cy.contains("h2", "Queued gappers (0/3 active)").should("be.visible");

        // Queue button stays disabled until a ticker is typed.
        cy.contains("button", "Queue Gapper").should("be.disabled");
        cy.typeStable('input[aria-label="Ticker symbol"]', symbol);
        cy.get('select[aria-label="Gapper profile"] option').should("have.length.greaterThan", 0);

        // Dial-in panel toggles open and shows the three dial groups.
        cy.contains("button", "Dial in").click();
        cy.contains("Gap screen").should("be.visible");
        cy.contains("Windows (ET)").should("be.visible");
        cy.contains("Risk & exit").should("be.visible");

        cy.contains("button", "Queue Gapper").should("be.enabled").click();

        cy.get("li[data-strategy-id]", { timeout: 10000 })
            .should("have.length", 1)
            .first()
            .within(() => {
                cy.get(".ticker-chip").should("contain.text", symbol);
                cy.contains(/awaiting first evaluation/i).should("be.visible");
                cy.contains("label", "Active").should("be.visible");
            });
        cy.contains("h2", "Queued gappers (1/3 active)").should("be.visible");

        // Stand in for a Monitor tick: the chip picks up the progress row on its 5s poll.
        cy.get("li[data-strategy-id]")
            .first()
            .invoke("attr", "data-strategy-id")
            .then((strategyId) => {
                cy.task("seedConditionProgress", {
                    strategyId,
                    passed: 2,
                    total: 4,
                    verb: "IsGapUp(5)",
                });
            });
        cy.get("li[data-strategy-id]").first().within(() => {
            cy.contains("2/4", { timeout: 15000 }).should("be.visible");
            cy.contains("IsGapUp(5)").should("be.visible");
        });

        // Pause it first, then remove it.
        cy.get('li[data-strategy-id] input[type="checkbox"][id^="gtoggle-"]').first().uncheck();
        cy.get("li[data-strategy-id]").first().contains("label", "Paused", { timeout: 10000 }).should("be.visible");
        cy.contains("h2", "Queued gappers (0/3 active)").should("be.visible");

        cy.get(`button[aria-label^="Remove ${symbol}"]`).click();
        cy.contains("No gappers queued", { timeout: 10000 }).should("be.visible");

        cy.checkPageA11y();
    });
});
