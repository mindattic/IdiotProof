import { defineConfig } from "cypress";

/**
 * Cypress configuration for IdiotProof's Blazor Server app.
 *
 * Override the target URL with the CYPRESS_BASE_URL env var:
 *     CYPRESS_BASE_URL=http://localhost:5294 npm run run
 *
 * Test users: tests register a fresh account on first run via /register
 * (see commands.ts). The cleanup hook truncates the test database between
 * runs — never point CYPRESS_BASE_URL at a production install.
 */
export default defineConfig({
  e2e: {
    baseUrl: process.env.CYPRESS_BASE_URL ?? "https://localhost:5001",
    supportFile: "cypress/support/e2e.ts",
    specPattern: "cypress/e2e/**/*.cy.ts",
    viewportWidth: 1440,
    viewportHeight: 900,
    video: false,
    screenshotOnRunFailure: true,
    chromeWebSecurity: false,
    defaultCommandTimeout: 10000,
  },
});
