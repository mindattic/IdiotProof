/// <reference types="cypress" />

/**
 * /api-keys page — exercises the user-facing knobs that ultimately drive the
 * MindAttic.Vault credential pipeline:
 *
 *   • The paper/live Alpaca key pairs feed the alpaca-paper / alpaca-live
 *     provider entries that OverlayFromBrokerCredentials and
 *     OverlayFromConfiguration read at startup.
 *   • The Claude key input feeds ClaudeApiKey, which is the same field the
 *     Vault LLM file-store and IConfiguration overlays populate at startup.
 *
 * These specs DO NOT verify what's actually written to disk / SQL — they
 * verify the UI contract and the keys-are-masked-by-default contract
 * documented in the page's security notice. The persistence path is
 * unit-tested in AppSettingsCredentialOverlayTests / UserKeyService tests.
 * Per-strategy Live-mode elevation (password modal) is a Strategies-page
 * concern now, not this page — see 09_strategies_live_mode.cy.ts.
 */
describe("API Keys page (authenticated)", () => {
    const testUser = () => `apikeys-${Date.now()}@idiotproof.local`;
    const testPass = "Cy-IdiotProof-2026-Kx9!";

    beforeEach(() => {
        cy.registerAndLogin(testUser(), testPass);
        cy.visitInteractive("/api-keys");
        cy.contains(/api keys & connections/i, { timeout: 15000 });
        cy.checkPageA11y();
    });

    it("renders the credential sections", () => {
        cy.contains("h2", /alpaca/i);
        cy.contains("h2", /ai \(claude\)/i);
    });

    it("masks the Claude API key by default and reveals it on toggle", () => {
        cy.get("#claude-key").should("have.attr", "type", "password");

        cy.get('button[aria-label="Show Claude API key"]').click();
        cy.get("#claude-key").should("have.attr", "type", "text");

        cy.get('button[aria-label="Hide Claude API key"]').click();
        cy.get("#claude-key").should("have.attr", "type", "password");
    });

    it("masks the Alpaca paper secret by default and reveals it on toggle", () => {
        cy.get("#alpaca-paper-secret").should("have.attr", "type", "password");

        cy.get('button[aria-label="Show paper secret key"]').click();
        cy.get("#alpaca-paper-secret").should("have.attr", "type", "text");
    });

    it("Save All persists keys and shows a success confirmation", () => {
        cy.get("#claude-key").clear().type("sk-ant-cypress-test-key");
        cy.get("#alpaca-paper-key-id").clear().type("PK-CYPRESS");
        cy.get("#alpaca-paper-secret").clear().type("SECRET-CYPRESS");

        cy.contains("button", /save all/i).click();
        cy.contains(/keys saved successfully/i, { timeout: 10000 });

        // Reload — the saved values must come back. They reload masked, so we
        // can't assert the contents of <input type="password"> via .should("have.value")
        // reliably; instead we click "show" and read the value back.
        cy.visitInteractive("/api-keys");
        cy.contains(/api keys & connections/i, { timeout: 15000 });
        cy.get('button[aria-label="Show Claude API key"]').click();
        cy.get("#claude-key").should("have.value", "sk-ant-cypress-test-key");
    });
});
