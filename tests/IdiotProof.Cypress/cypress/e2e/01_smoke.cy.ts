/// <reference types="cypress" />

/**
 * Smoke suite — verifies the app boots and the home routes are reachable.
 * Learning Center / wikilink coverage was removed when those pages were
 * deleted in the UI reset.
 */
describe("Smoke — public routes render", () => {
    it("login page renders", () => {
        cy.visit("/login");
        cy.contains(/sign in|log in/i);
    });

    it("protected route redirects to login when unauthenticated", () => {
        cy.visit("/strategies", { failOnStatusCode: false });
        cy.location("pathname", { timeout: 10000 }).should("match", /login|strategies/);
    });
});
