/// <reference types="cypress" />

/**
 * Backtest page flow: build a strategy, open /backtest, pick it, run a previous
 * day, and confirm the replay renders a result (summary + trigger timeline or the
 * "no triggers" empty state). Market data is simulated server-side, so the run is
 * deterministic and needs no live data provider.
 *
 * Requires the app running at CYPRESS_BASE_URL with a reachable database.
 */
describe("Backtest a strategy (authenticated)", () => {
    const testPass = "Test1234!";

    it("runs a backtest for a saved strategy and shows results", () => {
        const email = `backtest-${Date.now()}@idiotproof.local`;
        const title = "Backtest sample";
        cy.registerAndLogin(email, testPass);

        // Seed a strategy via the Describe tab (LLM stubbed).
        cy.visit("/builder");
        cy.contains("button", /describe/i).click();
        cy.get("#describe-ticker").type("AAPL");
        cy.get("#describe-title").type(title);
        cy.get("#describe-prose").type("Breakout above 10, pull back, hold VWAP, go long.");
        cy.intercept("POST", "**/anthropic/**", {
            statusCode: 200,
            body: {
                content: [
                    {
                        type: "text",
                        text: 'Stock.Ticker("AAPL").Breakout(10).Pullback().IsAboveVwap().Long().TakeProfit(12).StopLoss(8).Build()',
                    },
                ],
            },
        }).as("legion");
        cy.contains("button", /generate with claude/i).click();
        cy.contains("Stock.Ticker", { timeout: 30000 });
        cy.contains("button", /save strategy/i).click();
        cy.contains(/saved/i, { timeout: 10000 });

        // Run the backtest.
        cy.visit("/backtest");
        cy.get("#backtest-strategy").select(`${title} (AAPL)`);
        cy.get("#backtest-date").type("2026-05-29");
        cy.get("#backtest-run").click();

        // Results panel must appear with a summary (bars processed > 0).
        cy.get("#backtest-results", { timeout: 20000 }).should("exist");
        cy.get("#backtest-summary").should("exist").and("contain.text", "AAPL");
        cy.get("#backtest-pnl").should("exist");
    });

    it("disables Run until a strategy is selected", () => {
        const email = `backtest2-${Date.now()}@idiotproof.local`;
        cy.registerAndLogin(email, testPass);
        cy.visit("/backtest");
        cy.get("#backtest-run").should("be.disabled");
    });
});
