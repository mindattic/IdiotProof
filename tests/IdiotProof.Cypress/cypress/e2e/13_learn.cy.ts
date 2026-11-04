/// <reference types="cypress" />

/**
 * /learn Learning Center (IP-US-I1..I5).
 *
 *   I1 workflow overview diagram (Describe → Generate → Activate → Monitor → Three Gates)
 *   I2 six phases with a verb catalog reflected from the live DSL
 *   I3 three-gates diagram (Condition Match → LLM Voter Quorum → Risk Guardian)
 *   I4 three annotated examples, each with an "Open in Builder" seed link
 *   I5 "try it" links to Builder, Strategies, Backtest and API Keys
 */
describe("Learning Center (authenticated)", () => {
    const testPass = "Cy-IdiotProof-2026-Kx9!";

    beforeEach(() => {
        cy.registerAndLogin(`learn-${Date.now()}@idiotproof.local`, testPass);
        cy.visitInteractive("/learn");
    });

    it("I1: shows the end-to-end workflow diagram", () => {
        cy.get('#learn-workflow [aria-label="Workflow overview"] .wf-step').should("have.length", 5);
        ["Describe", "Generate", "Activate", "Monitor", "Three Gates"].forEach((step, i) =>
            cy.get("#learn-workflow .wf-step").eq(i).should("contain.text", step)
        );
    });

    it("I2: lists the six phases with a non-empty reflected verb catalog", () => {
        cy.get("#learn-phases [data-phase]", { timeout: 10000 }).should("have.length", 6);
        ["setup", "filters", "entry", "order", "risk", "exit"].forEach((phase) =>
            cy.get(`#learn-phases [data-phase="${phase}"]`).should("exist")
        );
        cy.get("#learn-phases [data-phase]").each(($card) => {
            cy.wrap($card).find(".verb-chip").should("have.length.greaterThan", 0);
        });
        // Verbs come from the live DSL, so known verbs must be present.
        cy.get("#learn-phases .verb-chip").then(($chips) => {
            const verbs = [...$chips].map((c) => c.textContent?.trim() ?? "");
            expect(verbs.some((v) => v.startsWith("StopLoss")), "StopLoss reflected").to.equal(true);
            expect(verbs.some((v) => v.startsWith("TakeProfit")), "TakeProfit reflected").to.equal(true);
        });
    });

    it("I3: explains the three gates in order", () => {
        cy.get('#learn-gates [aria-label="Three gates diagram"] .gate-card').should("have.length", 3);
        ["Condition Match", "LLM Voter Quorum", "Risk Guardian"].forEach((gate, i) =>
            cy.get("#learn-gates .gate-card").eq(i).should("contain.text", gate)
        );
        cy.get("#learn-gates .gate-card").eq(2).should("contain.text", "Cannot be bypassed");
    });

    it("I4: shows three annotated examples with Open-in-Builder seed links", () => {
        cy.get("#learn-examples .example-card").should("have.length", 3);
        ["NCI Breakout-Pullback", "ERNA AH Momentum", "SUNE Wedge Breakout"].forEach((title, i) => {
            cy.get("#learn-examples .example-card").eq(i).within(() => {
                cy.contains(title).should("be.visible");
                cy.get("pre code").should("contain.text", "Stock.Ticker(").and("contain.text", ".StopLoss(");
                cy.contains("a", "Open in Builder")
                    .should("have.attr", "href")
                    .and("match", /^\/builder\?seed=.+&ticker=[A-Z]+$/);
            });
        });

        cy.get("#learn-examples .example-card").first().contains("a", "Open in Builder").click();
        cy.location("pathname", { timeout: 10000 }).should("eq", "/builder");
        cy.location("search").should("contain", "ticker=NCI");
    });

    it("I5: every try-it link points at a real page", () => {
        const expected = ["/builder", "/", "/backtest", "/api-keys"];
        cy.get("#learn-try-it a.try-it-card").should("have.length", expected.length);
        expected.forEach((href, i) => cy.get("#learn-try-it a.try-it-card").eq(i).should("have.attr", "href", href));
        cy.get("#learn-workflow").contains("a", "Try the Builder").should("have.attr", "href", "/builder");

        expected.forEach((href) => cy.request({ url: href, failOnStatusCode: false }).its("status").should("eq", 200));

        cy.checkPageA11y();
    });
});
