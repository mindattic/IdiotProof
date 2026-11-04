/// <reference types="cypress" />

/**
 * Gapper "From a transcript" box (IP-US-K10), against a server started with
 * IDIOTPROOF_FAKE_LLM=1.
 *
 * FakeLlmHandler answers the Legion call with whatever sits inside the
 * [[script: ...]] marker, so the transcript carries the model's JSON answer
 * itself. Its lazy match stops at the first "]]", so the array's closing
 * bracket is followed by a space before the marker's "]]".
 *
 * Proves the review-first contract in the UI: valid candidates render as cards
 * with their overlaid dial-ins, an invalid symbol is skipped with a warning,
 * nothing is queued until "Queue" is clicked on a card, a queued card leaves
 * the review list, and "Clear results" drops the cards left unqueued.
 */
describe("Gapper transcript interpretation (authenticated, fake LLM)", () => {
    const testPass = "Cy-IdiotProof-2026-Kx9!";

    const answer =
        '[ {"symbol":"CYACM","rationale":"FDA catalyst, tight stop.","profile":{"minGapPercent":9,"stopLossPercent":3}},' +
        ' {"symbol":"CYACN","rationale":"Second play, left unqueued."},' +
        ' {"symbol":"not a ticker!","rationale":"bad"} ]';
    const transcript =
        "Watch CYACM tomorrow, gapping 10% on FDA news, keep a tight stop. " + `[[script: ${answer} ]]`;

    it("renders candidates as review cards, warns on the bad one, queues only on click", () => {
        cy.registerAndLogin(`transcript-${Date.now()}@idiotproof.local`, testPass);
        cy.visitInteractive("/gapper");

        cy.contains("button", "Interpret").should("be.disabled");
        cy.get('textarea[aria-label="Transcript or natural-language description"]').type(transcript, {
            parseSpecialCharSequences: false,
            delay: 0,
        });
        cy.contains("button", "Interpret").should("be.enabled").click();

        cy.get('[data-candidate="CYACM"]', { timeout: 30000 }).within(() => {
            cy.contains("FDA catalyst, tight stop.").should("be.visible");
            cy.contains("gap ≥9%").should("be.visible");
            cy.contains("SL 3%").should("be.visible");
            cy.contains("summary", "Show generated IdiotScript").click();
            cy.get("pre").should("contain.text", "CYACM");
        });
        cy.get("[data-candidate]").should("have.length", 2);
        cy.contains(/Skipped candidate with invalid symbol/i).should("be.visible");

        // Review-first: nothing queued by the interpretation itself.
        cy.contains("No gappers queued").should("be.visible");

        cy.get('[data-candidate="CYACM"]').contains("button", "Queue").click();
        cy.get("li[data-strategy-id]", { timeout: 10000 }).should("have.length", 1).and("contain.text", "CYACM");
        // A queued candidate leaves the review list; the other stays for review.
        cy.get('[data-candidate="CYACM"]').should("not.exist");
        cy.get('[data-candidate="CYACN"]').should("be.visible");

        // Leave nothing armed.
        cy.get('li[data-strategy-id] input[type="checkbox"][id^="gtoggle-"]').first().uncheck();
        cy.get("li[data-strategy-id]").first().contains("label", "Paused", { timeout: 10000 });
        cy.get('button[aria-label^="Remove CYACM"]').click();
        cy.contains("No gappers queued", { timeout: 10000 }).should("be.visible");

        cy.contains("button", "Clear results").click();
        cy.get("[data-candidate]").should("not.exist");
    });
});
