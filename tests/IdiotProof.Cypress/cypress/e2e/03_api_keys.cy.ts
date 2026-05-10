/// <reference types="cypress" />

/**
 * /api-keys page — exercises the user-facing knobs that ultimately drive the
 * MindAttic.Vault credential pipeline:
 *
 *   • The "Paper" toggle flips AppSettings.AlpacaIsPaper, which is what
 *     OverlayFromBrokerCredentials and OverlayFromConfiguration use to pick
 *     between the alpaca-paper and alpaca-live provider entries.
 *   • The Claude key input feeds ClaudeApiKey, which is the same field the
 *     Vault LLM file-store and IConfiguration overlays populate at startup.
 *
 * These specs DO NOT verify what's actually written to disk / SQL — they
 * verify the UI contract, the live-mode confirmation guard, and the
 * keys-are-masked-by-default contract documented in the page's security
 * notice. The persistence path is unit-tested in
 * AppSettingsCredentialOverlayTests / UserKeyService tests.
 */
describe("API Keys page (authenticated)", () => {
    const testUser = () => `apikeys-${Date.now()}@idiotproof.local`;
    const testPass = "Test1234!";

    beforeEach(() => {
        cy.registerAndLogin(testUser(), testPass);
        cy.visit("/api-keys");
        cy.contains(/api keys & connections/i, { timeout: 15000 });
    });

    it("renders all three credential sections", () => {
        cy.contains("h2", /alpaca/i);
        cy.contains("h2", /polygon/i);
        cy.contains("h2", /ai \(claude\)/i);
    });

    it("masks the Claude API key by default and reveals it on toggle", () => {
        cy.get("#claude-key").should("have.attr", "type", "password");

        cy.get('button[aria-label="Show Claude API key"]').click();
        cy.get("#claude-key").should("have.attr", "type", "text");

        cy.get('button[aria-label="Hide Claude API key"]').click();
        cy.get("#claude-key").should("have.attr", "type", "password");
    });

    it("masks the Alpaca secret by default and reveals it on toggle", () => {
        cy.get("#alpaca-secret").should("have.attr", "type", "password");

        cy.get('button[aria-label="Show Alpaca secret key"]').click();
        cy.get("#alpaca-secret").should("have.attr", "type", "text");
    });

    it("opens the live-mode confirmation modal when Paper is unchecked", () => {
        // The Paper switch starts checked (AlpacaIsPaper defaults true). Unchecking
        // it must NOT immediately flip the underlying value — instead the danger
        // modal shows so the user has to confirm.
        cy.get("#alpacaPaper").should("be.checked");
        cy.get("#alpacaPaper").uncheck();

        cy.contains(/enable live trading\?/i).should("be.visible");
        cy.contains(/real money/i).should("be.visible");
    });

    it("Cancel on the live-mode modal keeps Paper checked", () => {
        cy.get("#alpacaPaper").uncheck();
        cy.contains(/enable live trading\?/i).should("be.visible");

        cy.contains("button", /cancel/i).click();

        // Modal dismissed and the underlying state is back to paper.
        cy.contains(/enable live trading\?/i).should("not.exist");
        cy.get("#alpacaPaper").should("be.checked");
        // The "LIVE Alpaca trading" red banner only renders when paper is OFF;
        // confirm it did NOT appear.
        cy.contains(/live alpaca trading/i).should("not.exist");
    });

    it("Confirming the live-mode modal flips to Live and shows the danger banner", () => {
        cy.get("#alpacaPaper").uncheck();
        cy.contains("button", /yes, use real money/i).click();

        cy.contains(/enable live trading\?/i).should("not.exist");
        cy.get("#alpacaPaper").should("not.be.checked");
        // The page renders the in-section red banner once paper is off — this
        // is the same UI contract the AccountPill relies on (red outline = LIVE).
        cy.contains(/live alpaca trading/i).should("be.visible");
    });

    it("Save All persists keys and shows a success confirmation", () => {
        cy.get("#claude-key").clear().type("sk-ant-cypress-test-key");
        cy.get("#alpaca-key-id").clear().type("PK-CYPRESS");
        cy.get("#alpaca-secret").clear().type("SECRET-CYPRESS");

        cy.contains("button", /save all/i).click();
        cy.contains(/keys saved successfully/i, { timeout: 10000 });

        // Reload — the saved values must come back. They reload masked, so we
        // can't assert the contents of <input type="password"> via .should("have.value")
        // reliably; instead we click "show" and read the value back.
        cy.reload();
        cy.contains(/api keys & connections/i, { timeout: 15000 });
        cy.get('button[aria-label="Show Claude API key"]').click();
        cy.get("#claude-key").should("have.value", "sk-ant-cypress-test-key");
    });
});
