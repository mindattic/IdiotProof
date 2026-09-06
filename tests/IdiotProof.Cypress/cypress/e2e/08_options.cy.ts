/// <reference types="cypress" />

/**
 * /options — the manual Options section (IP-US-U6, U8 lock text, U11 jargon, RFC 0004).
 *
 * Runs entirely on the Sandbox broker: a freshly registered user has no Alpaca keys and no
 * routing consent, so the page resolves to Sandbox deterministically. The ticker IPTEST is
 * not a real symbol (6-char OCC root, so it still builds valid contracts): any live price
 * lookup fails and the page falls back to the Sandbox reference price, which is a hash of
 * the symbol — the chain is identical on every run, on every machine.
 *
 * Sandbox positions live in the circuit-scoped OptionsTradingService, so each test stays on
 * the page (no cy.visit between steps) — a reload would start a fresh, flat sandbox.
 */
describe("Options section (authenticated, Sandbox)", () => {
    const testPass = "Cy-IdiotProof-2026-Kx9!";
    const ticker = "IPTEST";

    const money = (text: string) => Number(text.replace(/[^0-9.-]/g, ""));

    beforeEach(() => {
        cy.registerAndLogin(`options-${Date.now()}@idiotproof.local`, testPass);
        cy.visitInteractive("/options");
        cy.contains("h1", /options/i, { timeout: 15000 });
        // The page renders this marker only from the interactive (circuit) render — typing
        // before it exists would hit the prerendered DOM that is about to be replaced.
        cy.get("[data-cy=opt-interactive]", { timeout: 20000 }).should("exist");
        cy.checkPageA11y();
    });

    function loadChain() {
        cy.get("#opt-ticker").should("be.enabled").clear().type(`${ticker}{enter}`);
        cy.get("[data-cy=opt-chain-table]", { timeout: 20000 }).should("exist");
        cy.get("[data-cy=opt-underlying-price]").should("contain.text", ticker);
    }

    function placeFromTicket(expectedSentence: RegExp) {
        cy.get("#opt-submit").should("be.enabled").click();
        cy.get("[data-cy=opt-confirm]").should("be.visible");
        cy.get("[data-cy=opt-confirm-sentence]").invoke("text").should("match", expectedSentence);
        cy.get("#opt-confirm-place").click();
        cy.get(".toast-item.toast-success", { timeout: 15000 }).should("contain.text", "Sandbox");
    }

    it("starts empty with the how-to open, and loads a chain on Enter", () => {
        cy.get("[data-cy=opt-howto]").should("have.attr", "open");
        cy.get("[data-cy=opt-howto]").should("contain.text", "three steps");
        cy.get("[data-cy=opt-chain-empty]").should("contain.text", "Type a ticker");
        cy.get("[data-cy=opt-ticket-empty]").should("exist");
        cy.get("[data-cy=opt-positions-empty]").should("exist");
        // Fresh user: no Alpaca consent → Sandbox is the resolved account.
        cy.get("[data-cy=opt-mode-sandbox]").should("have.class", "active-sandbox");

        loadChain();

        cy.get("[data-cy=opt-chain-table]").within(() => {
            cy.contains("th", "CALLS").should("be.visible");
            cy.contains("th", "PUTS").should("be.visible");
            cy.get("[data-cy=opt-cell-call]").its("length").should("be.gte", 5);
            cy.get("[data-cy=opt-cell-put]").its("length").should("be.gte", 5);
            cy.get("tr.opt-atm").should("have.length", 1);
            // Hype meter: at least one cell per colour extreme in a chain that spans ±8 strikes.
            cy.get(".opt-hype.hype-4").should("exist");
        });
        cy.get("[data-cy=opt-exp]").its("length").should("be.gte", 3);
        cy.get("[data-cy=opt-exp].btn-warning").should("have.length", 1);
        // Loading a chain collapses the how-to (it stays available).
        cy.get("[data-cy=opt-howto]").should("not.have.attr", "open");
    });

    it("fills the ticket in plain English when a call is picked", () => {
        loadChain();
        cy.get("tr.opt-atm [data-cy=opt-cell-call]").first().click();

        cy.get("[data-cy=opt-ticket-title]").should("contain.text", ticker).and("contain.text", "CALL");
        cy.get("[data-cy=opt-ticket-broker]").should("have.text", "Sandbox");
        cy.get("[data-cy=opt-side-buy]").should("have.class", "btn-success");
        cy.get("[data-cy=opt-intent]").should("contain.text", "Opens a new position");
        cy.get("[data-cy=opt-summary-headline]").invoke("text").should("match", /You're buying\s+1\s+IPTEST \$[\d.]+ call\s+for about \$[\d,]+\.\d\d/);
        cy.get("[data-cy=opt-real-hype]").should("contain.text", "real").and("contain.text", "hype");
        cy.get("[data-cy=opt-breakeven]").should("contain.text", "Breakeven").and("contain.text", "You don't have to get there");
        cy.get("[data-cy=opt-ticket-locked]").should("not.exist");
        cy.get("[data-cy=opt-limit]").should("be.enabled");

        // Two contracts = exactly double the money.
        cy.get("[data-cy=opt-total]").invoke("text").then((one) => {
            cy.get("[data-cy=opt-qty]").clear().type("2").blur();
            cy.get("[data-cy=opt-summary-headline]").should("contain.text", "2");
            cy.get("[data-cy=opt-total]").invoke("text").should((two) => {
                expect(money(two)).to.be.closeTo(money(one) * 2, 0.011);
            });
        });

        // Market: the price box becomes a read-only "est. fill" showing the live mid.
        cy.get("[data-cy=opt-type-market]").click();
        cy.get("[data-cy=opt-limit]").should("be.disabled");
        cy.contains("label", /est\. fill/i).should("exist");
        cy.get("[data-cy=opt-type-limit]").click();
        cy.get("[data-cy=opt-limit]").should("be.enabled");

        // Clearing the ticket returns the empty prompt.
        cy.get("[data-cy=opt-ticket-clear]").click();
        cy.get("[data-cy=opt-ticket-empty]").should("exist");
    });

    it("buys two calls, shows the position, then closes it from the tracker", () => {
        loadChain();
        cy.get("tr.opt-atm [data-cy=opt-cell-call]").first().click();
        cy.get("[data-cy=opt-qty]").clear().type("2").blur();

        placeFromTicket(/^Buy 2 IPTEST \$[\d.]+ calls expiring .+ at about \$[\d.]+\/share \(\$[\d,]+\.\d\d total\)$/);

        cy.get("[data-cy=opt-position-row]", { timeout: 15000 }).should("have.length", 1);
        cy.get("[data-cy=opt-position-qty]").should("have.text", "+2");
        cy.get("[data-cy=opt-position-row] .opt-split-bar").should("exist");
        // The sell-the-hype nudge can't fire on the first sample — the tracker says so.
        cy.get("[data-cy=opt-watching]").should("contain.text", "samples");
        cy.get("[data-cy=opt-signal-row]").should("not.exist");
        // The ticket now knows we hold 2 of this contract.
        cy.get("[data-cy=opt-existing]").should("contain.text", "+2");

        // Close pre-fills the ticket with the opposite side and the full size.
        cy.get("[data-cy=opt-close]").click();
        cy.get("[data-cy=opt-side-sell]", { timeout: 20000 }).should("have.class", "btn-danger");
        cy.get("[data-cy=opt-qty]").should("have.value", "2");
        cy.get("[data-cy=opt-intent]").should("contain.text", "Closes 2");
        cy.get("[data-cy=opt-summary-headline]").should("contain.text", "to CLOSE your position");
        cy.get("[data-cy=opt-writing-warning]").should("not.exist");

        placeFromTicket(/^Sell 2 IPTEST \$[\d.]+ calls expiring/);
        cy.get("[data-cy=opt-positions-empty]", { timeout: 15000 }).should("exist");
    });

    it("warns plainly when selling a contract you don't own (writing)", () => {
        loadChain();
        cy.get("tr.opt-atm [data-cy=opt-cell-put]").first().click();
        cy.get("[data-cy=opt-side-sell]").click();
        cy.get("[data-cy=opt-intent]").should("contain.text", "SHORT");
        cy.get("[data-cy=opt-summary-headline]").should("contain.text", "to OPEN a short");
        cy.get("[data-cy=opt-writing-warning]").should("contain.text", "assignment risk");
        // Sandbox is level 3, so writing is allowed — no lock banner.
        cy.get("[data-cy=opt-ticket-locked]").should("not.exist");
    });

    it("falls back to Sandbox with a reason when Paper or Live is picked without Alpaca consent", () => {
        cy.get("[data-cy=opt-mode-paper]").click();
        cy.get("[data-cy=opt-mode-fallback]", { timeout: 10000 }).should("contain.text", "Alpaca routing isn't enabled");
        cy.get("[data-cy=opt-mode-sandbox]").should("have.class", "active-sandbox");
        cy.get("[data-cy=opt-mode-paper]").should("not.have.class", "active-paper");

        cy.get("[data-cy=opt-mode-live]").click();
        cy.get("[data-cy=opt-mode-fallback]", { timeout: 10000 }).should("contain.text", "Alpaca routing isn't enabled");
        cy.get("[data-cy=opt-mode-live]").should("not.have.class", "active-live");
        // No red live banner without a live Alpaca account.
        cy.contains(/LIVE account — real money/i).should("not.exist");
    });

    it("explains its jargon: hover hints and click-to-open cards", () => {
        loadChain();
        // Every dotted term carries a hover hint.
        cy.get("[data-cy=jargon]").its("length").should("be.gte", 10);
        cy.get("[data-cy=jargon]").each(($el) => {
            expect($el.attr("title"), `title on ${$el.attr("data-term")}`).to.be.a("string").and.not.be.empty;
        });

        // Click the chain's Hype header → full card; Escape closes it.
        cy.get("[data-cy=opt-chain-table] [data-cy=jargon][data-term=hype]").first().click();
        cy.get("[data-cy=jargon-pop][data-term=hype]").should("be.visible").and("contain.text", "time value");
        cy.get("[data-cy=jargon-pop]").should("have.length", 1);
        cy.focused().type("{esc}");
        cy.get("[data-cy=jargon-pop]").should("not.exist");

        // Opening a second term closes the first (focus moved → only one card at a time).
        cy.get("[data-cy=jargon][data-term=breakeven]").first().click();
        cy.get("[data-cy=jargon-pop][data-term=breakeven]").should("be.visible");
        cy.get("[data-cy=jargon][data-term=iv]").first().click();
        cy.get("[data-cy=jargon-pop]").should("have.length", 1);
        cy.get("[data-cy=jargon-pop][data-term=iv]").should("be.visible");
        // Click-away (focus leaves the term) closes it.
        cy.get("#options-heading").click();
        cy.get("[data-cy=jargon-pop]").should("not.exist");
    });
});
