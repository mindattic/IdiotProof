/// <reference types="cypress" />

/**
 * Builds each bundled sample strategy (NCI / ERNA / SUNE) through the UI and
 * confirms it saves and shows up on /strategies. Uses the Describe tab with the
 * Claude call stubbed to return the sample's IdiotScript — the same path a user
 * takes, minus the live LLM dependency.
 *
 * These exercise the round-trip the C# fixes targeted: Breakout/Pullback,
 * HoldsAbove, multi-target TakeProfit and the quoted Ticker all have to parse for
 * the saved script to render on /strategies.
 *
 * Requires the app running at CYPRESS_BASE_URL with a reachable database.
 */
describe("Build sample strategies (authenticated)", () => {
    const testPass = "Test1234!";

    const samples = [
        {
            symbol: "NCI",
            title: "NCI Breakout-Pullback",
            script:
                'Stock.Ticker("NCI").Breakout(3.68).Pullback().IsAboveVwap().Long().TakeProfit(5.00, 6.50).StopLoss(3.50).Repeat().Build()',
        },
        {
            symbol: "ERNA",
            title: "ERNA AH Momentum",
            script:
                'Stock.Ticker("ERNA").Breakout(0.52).Pullback().IsAboveVwap().HoldsAbove(0.48).Long().TakeProfit(0.66, 0.88).StopLoss(0.46).Repeat().Build()',
        },
        {
            symbol: "SUNE",
            title: "SUNE Wedge Breakout",
            script:
                'Stock.Ticker("SUNE").Breakout(2.42).HoldsAbove(2.30).Long().TakeProfit(2.85, 3.20, 4.20).StopLoss(2.25).Repeat().Build()',
        },
    ];

    it("builds NCI, ERNA, and SUNE and lists them", () => {
        const email = `samples-${Date.now()}@idiotproof.local`;
        cy.registerAndLogin(email, testPass);

        for (const s of samples) {
            cy.visit("/builder");
            cy.contains("button", /describe/i).click();

            cy.get("#describe-ticker").clear().type(s.symbol);
            cy.get("#describe-title").clear().type(s.title);
            cy.get("#describe-prose").clear().type(`No break, no trade: ${s.title}.`);

            cy.intercept("POST", "**/anthropic/**", {
                statusCode: 200,
                body: { content: [{ type: "text", text: s.script }] },
            }).as("legion");

            cy.contains("button", /generate with claude/i).click();
            cy.contains("Stock.Ticker", { timeout: 30000 });

            cy.contains("button", /save strategy/i).click();
            cy.contains(/saved/i, { timeout: 10000 });
        }

        cy.visit("/strategies");
        for (const s of samples) {
            cy.contains(s.title);
            cy.contains(s.symbol);
        }
    });
});
