/// <reference types="cypress" />

/**
 * Custom Cypress commands shared across specs. Each `Cypress.Commands.add`
 * also extends the global Chainable interface below so TypeScript hints work
 * in spec files.
 *
 * Blazor note: the app renders with a global InteractiveServer render mode,
 * so every page is first statically prerendered, then re-rendered when the
 * SignalR circuit attaches. Anything typed or clicked in that gap is wiped
 * by the second render. visitInteractive() waits for the circuit negotiate
 * before letting the spec interact, and typeStable() asserts the value stuck.
 */

declare global {
    // eslint-disable-next-line @typescript-eslint/no-namespace
    namespace Cypress {
        interface Chainable {
            /** cy.visit + wait for the Blazor Server circuit to attach. */
            visitInteractive(path: string): Chainable<void>;
            /** Type into an input and assert the value stuck (post-circuit). */
            typeStable(selector: string, value: string): Chainable<void>;
            /** Sign up a brand-new user (lands on /api-keys, the first-run page). */
            registerAndLogin(email: string, password: string): Chainable<void>;
            /** Submit the login form for an existing account. */
            login(email: string, password: string): Chainable<void>;
        }
    }
}

Cypress.Commands.add("visitInteractive", (path: string) => {
    cy.intercept("POST", "**/_blazor/negotiate*").as("blazorNegotiate");
    cy.visit(path);
    cy.wait("@blazorNegotiate", { timeout: 20000 });
    // The circuit re-render replaces the prerendered DOM just after negotiate;
    // give it a beat so subsequent type/click hits the live DOM.
    cy.wait(500);
});

Cypress.Commands.add("typeStable", (selector: string, value: string) => {
    cy.get(selector).should("be.enabled").clear().type(value, { parseSpecialCharSequences: false });
    cy.get(selector).should("have.value", value);
});

Cypress.Commands.add("registerAndLogin", (email: string, password: string) => {
    cy.visitInteractive("/register");
    cy.typeStable('input[name="email"]', email);
    cy.typeStable('input[name="password"]', password);
    cy.typeStable('input[name="confirm"]', password);
    cy.get('button[type="submit"]').click();
    // /register-submit signs the new user in and lands on /api-keys (the
    // first-run flow). Anywhere outside the anonymous pages means success.
    cy.location("pathname", { timeout: 10000 }).should("not.match", /register|login/);
});

Cypress.Commands.add("login", (email: string, password: string) => {
    cy.visitInteractive("/login");
    cy.typeStable('input[name="email"]', email);
    cy.typeStable('input[name="password"]', password);
    cy.get('button[type="submit"]').click();
    cy.location("pathname", { timeout: 10000 }).should("eq", "/");
});

export {};
