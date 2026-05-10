/// <reference types="cypress" />

/**
 * Happy-path E2E that proves the IConfiguration → MindAttic.Vault → provider
 * chain is live in IdiotProof.Blazor. Drives the /builder Describe-tab flow
 * with a REAL Claude call (no response stub), then asserts:
 *
 *   1. The outbound HTTP request to Anthropic carries an Authorization header
 *      — i.e. credential resolution succeeded somewhere in the chain
 *      (User Secrets via MindAttic:Vault:LLM:claude:apiKey, App Service
 *      Application Settings, Azure Key Vault, OR the legacy
 *      %APPDATA%/MindAttic/LLM/providers.json fallback).
 *   2. The response renders into the IdiotScript output panel as a
 *      Stock.Ticker(...) chain — the canonical generator output. A blank
 *      panel, a "No API key configured" toast, or a 401 banner all fail.
 *
 * Set the CYPRESS_LIVE_LLM=1 env var (e.g. `CYPRESS_LIVE_LLM=1 npm run run`)
 * to opt in. Default is skip, so CI runners without a configured Claude key
 * don't fail spuriously. Local dev boxes with %APPDATA%/MindAttic/LLM/
 * populated will pass without further setup.
 */
describe("Vault-backed AI happy-path (live LLM)", () => {
    const live = Cypress.env("LIVE_LLM") === true || Cypress.env("LIVE_LLM") === "1";
    const testUser = () => `vault-ai-${Date.now()}@idiotproof.local`;
    const testPass = "Test1234!";

    before(function () {
        if (!live) {
            // Skip the whole describe block — the alternate "spy-only" describe
            // below still runs to give CI useful coverage without a real key.
            this.skip();
        }
    });

    it("Describe → Generate with Claude returns an IdiotScript chain", () => {
        // Watch (don't stub) the outgoing Anthropic call. cy.intercept without
        // a response handler observes the request + reply pair so we can assert
        // on both sides afterwards. The regex matches every Anthropic-shaped
        // URL path the Legion HTTP client may use (proxied via /anthropic/...
        // or direct via api.anthropic.com).
        cy.intercept("POST", /anthropic/i).as("anthropicCall");

        cy.registerAndLogin(testUser(), testPass);
        cy.visit("/builder");
        cy.contains("button", /describe/i).click();

        cy.get("#describe-ticker").type("AAPL");
        cy.get("#describe-title").type("Vault happy-path");
        cy.get("#describe-prose").type(
            "When ADX is above 20 and the 9 EMA crosses above the 31 EMA on a volume-confirmed reclaim, go long."
        );

        cy.contains("button", /generate with claude/i).click();

        cy.wait("@anthropicCall", { timeout: 60_000 }).then((intercept) => {
            const req = intercept.request;
            const auth =
                req.headers["authorization"] ??
                req.headers["Authorization"] ??
                req.headers["x-api-key"] ??
                req.headers["X-Api-Key"];

            expect(
                auth,
                "outbound Anthropic request must carry an auth header — credential chain resolved a key"
            ).to.exist;

            // The response must NOT be 401/403. Anything else (200 happy
            // path, or even a 429 rate limit) at least proves the key was
            // accepted by Anthropic's auth layer.
            const status = intercept.response?.statusCode ?? 0;
            expect(status, "Anthropic accepted the key (no 401/403)").to.not.be.oneOf([401, 403]);
        });

        // The page renders the model output as IdiotScript. The grammar always
        // begins with Stock.Ticker(...) for stock-side describes. Allow up to
        // 60s for slower providers / cold starts.
        cy.contains("Stock.Ticker", { timeout: 60_000 }).should("be.visible");

        // Negative space: error toasts/banners that would appear if the chain
        // failed must not be on the page.
        cy.contains(/no api key configured/i).should("not.exist");
        cy.contains(/401|unauthorized/i).should("not.exist");
    });
});

/**
 * Always-on companion: spy on the outbound LLM request without sending it.
 * This runs in every CI invocation and proves the *page-level* wiring (UI
 * triggers an outbound call to a Vault-backed endpoint) without depending on
 * a real key. The live-call spec above is the strict happy-path; this one
 * catches regressions where the button stops calling Legion at all (e.g.
 * a refactor that nulls the IConfiguration before dispatch).
 */
describe("Vault-backed AI page wiring (no live call)", () => {
    const testUser = () => `vault-ai-spy-${Date.now()}@idiotproof.local`;
    const testPass = "Test1234!";

    it("clicking Generate dispatches a request to the AI endpoint", () => {
        cy.intercept("POST", /anthropic/i, {
            statusCode: 200,
            body: {
                content: [
                    {
                        type: "text",
                        text: 'Stock.Ticker("AAPL").RequireAdxAbove(20).RequireEmaStack(9, 31).OnReclaim(9).WithVolumeConfirm(1.2).Long().Build()',
                    },
                ],
            },
        }).as("legion");

        cy.registerAndLogin(testUser(), testPass);
        cy.visit("/builder");
        cy.contains("button", /describe/i).click();

        cy.get("#describe-ticker").type("AAPL");
        cy.get("#describe-title").type("Wiring smoke");
        cy.get("#describe-prose").type(
            "Long when ADX above 20 and 9 EMA above 31 EMA on a 9 EMA reclaim with volume above 1.2x average."
        );

        cy.contains("button", /generate with claude/i).click();

        // The interception fires only if the page actually calls into the
        // Legion-backed endpoint — which only happens when a credential was
        // resolved upstream. A page that bails out on missing creds (e.g.
        // showing "No API key configured" before dispatch) will never reach
        // the intercept and this assertion will time out.
        cy.wait("@legion", { timeout: 30_000 }).its("request.method").should("eq", "POST");

        cy.contains("Stock.Ticker", { timeout: 30_000 });
        cy.contains(/no api key configured/i).should("not.exist");
    });
});
