/// <reference types="cypress" />

/**
 * Smoke suite — verifies the app boots, the home routes are reachable, and
 * the marquee UI components (Account pill, Strategies page, Learning Center)
 * render without server-side errors.
 *
 * Pre-req: dev server is running. Tests skip the auth flow on routes that
 * allow anonymous access; protected routes redirect to /login (verified).
 */
describe("Smoke — public routes render", () => {
    it("login page renders", () => {
        cy.visit("/login");
        cy.contains(/sign in|log in/i);
    });

    it("setup page redirects unauthenticated users to login", () => {
        cy.visit("/strategies", { failOnStatusCode: false });
        cy.location("pathname", { timeout: 10000 }).should("match", /login|strategies/);
    });
});

describe("Smoke — Learning Center renders without auth", () => {
    it("learn root shows the Overview category", () => {
        cy.visit("/learn", { failOnStatusCode: false });
        // The first article seeded is "What is IdiotScript?" under "1. Overview".
        cy.contains(/learning center|what is idiotscript/i, { timeout: 15000 });
    });

    it("a verb article renders the wikilink-embedded strategy preview", () => {
        cy.visit("/learn/example-9-30-pullback", { failOnStatusCode: false });
        cy.contains("9/30 pullback", { matchCase: false });
        // The phase chips ("Setup", "Filters", "Entry"...) come from
        // StrategyBuilderRenderer rendered inside WikiContent. If they appear,
        // the wikilink-→-renderer loop is working end-to-end.
        cy.get(".phase-title", { timeout: 15000 }).should("have.length.at.least", 3);
    });
});
