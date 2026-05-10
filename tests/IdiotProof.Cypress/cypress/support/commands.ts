/// <reference types="cypress" />

/**
 * Custom Cypress commands shared across specs. Each `Cypress.Commands.add`
 * also extends the global Chainable interface below so TypeScript hints work
 * in spec files.
 */

declare global {
    // eslint-disable-next-line @typescript-eslint/no-namespace
    namespace Cypress {
        interface Chainable {
            /** Sign up a brand-new user and end up on the dashboard. */
            registerAndLogin(email: string, password: string): Chainable<void>;
            /** Submit the login form for an existing account. */
            login(email: string, password: string): Chainable<void>;
        }
    }
}

Cypress.Commands.add("registerAndLogin", (email: string, password: string) => {
    cy.visit("/register");
    cy.get('input[name="email"]').type(email);
    cy.get('input[name="password"]').type(password);
    cy.get('button[type="submit"]').click();
    cy.location("pathname", { timeout: 10000 }).should("eq", "/");
});

Cypress.Commands.add("login", (email: string, password: string) => {
    cy.visit("/login");
    cy.get('input[name="email"]').type(email);
    cy.get('input[name="password"]').type(password);
    cy.get('button[type="submit"]').click();
    cy.location("pathname", { timeout: 10000 }).should("eq", "/");
});

export {};
