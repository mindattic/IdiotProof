/// <reference types="cypress" />

/**
 * /research tab (IP-US-T7): the primary view is a significance-ranked feed
 * with a last-scan banner and a "my watchlist only" toggle, not a search box.
 *
 * The feed's contents depend on whether IdiotProof.ResearchScanner has run
 * against this database, so the spec accepts either banner state and either
 * an empty or populated feed, but asserts the structure and that the filters
 * re-query without error. The manual one-ticker lookup stays tucked behind
 * the "Advanced" disclosure.
 */
describe("Research feed (authenticated)", () => {
    const testPass = "Cy-IdiotProof-2026-Kx9!";

    it("shows banner, filters and ranked feed; manual lookup is secondary", () => {
        cy.registerAndLogin(`research-${Date.now()}@idiotproof.local`, testPass);
        cy.visitInteractive("/research");

        cy.get("#research-heading").should("contain.text", "Intelligence Research");
        cy.contains(/No scan has run yet|Last scanned/).should("be.visible");
        cy.contains("button", "Refresh").should("be.visible");

        cy.get("#watchlistOnly").should("not.be.checked");
        cy.contains("label", /My watchlist only \(\d+ tickers\)/).should("be.visible");
        cy.contains("h2", "Today's High-Impact Events").should("be.visible");

        const feedSettled = () =>
            cy.contains("h2", "Today's High-Impact Events")
                .parent()
                .within(() => {
                    cy.contains(/Loading/).should("not.exist");
                });

        const assertFeedShape = () =>
            cy.contains("h2", "Today's High-Impact Events")
                .parent()
                .then(($card) => {
                    if ($card.find("table").length > 0) {
                        ["Score", "Event", "Type", "Impact", "Affects", "Historical", "Source", "Date"].forEach((h) =>
                            cy.wrap($card).find("th").contains(h)
                        );
                    } else {
                        cy.wrap($card).contains("No claims match these filters yet.");
                    }
                });

        feedSettled();
        assertFeedShape();

        // Watchlist-only re-queries; a new user has an empty watchlist so the feed must empty out.
        cy.get("#watchlistOnly").check();
        feedSettled();
        cy.contains("No claims match these filters yet.", { timeout: 10000 }).should("be.visible");
        cy.get("#watchlistOnly").uncheck();
        feedSettled();
        assertFeedShape();

        // A ticker filter with no claims empties the feed without error.
        cy.get('input[placeholder="Any ticker"]').type("ZZZZQ");
        cy.contains("No claims match these filters yet.", { timeout: 10000 }).should("be.visible");
        cy.get(".alert-danger").should("not.exist");

        // Manual lookup is behind the Advanced disclosure, not the primary view.
        cy.get('input[placeholder="LMT"]').should("not.exist");
        cy.contains("button", "Advanced: analyze one ticker or paste an article").click();
        cy.get('input[placeholder="LMT"]').should("be.visible");

        cy.checkPageA11y();
    });
});
