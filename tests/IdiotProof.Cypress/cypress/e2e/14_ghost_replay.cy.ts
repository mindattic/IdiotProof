/// <reference types="cypress" />

/**
 * Ghost replay on /backtest (IP-US-G2..G4): step/play/pause/speed over the day, fork markers with
 * a hover tooltip at each If() resolution, side-by-side runs, and the branch-tree downloads.
 *
 * A fresh test user has no Alpaca keys, so the page replays MockDataFeed candles (deterministic,
 * no market data). The strategy is saved through the builder (fake LLM) and its canonical JSON is
 * then swapped for an If/Else version, because the text parser has no branch syntax.
 *
 * Requires the app at CYPRESS_BASE_URL with IDIOTPROOF_FAKE_LLM=1 and IDIOTPROOF_SQL_DB pointing at
 * the same throwaway database.
 */
describe("Ghost replay (authenticated)", () => {
    const testPass = "Cy-IdiotProof-2026-Kx9!";
    const title = `Ghost branched ${Date.now()}`;
    const email = `ghost-${Date.now()}@idiotproof.local`;

    const position = () =>
        cy.get("#ghost-main .ghost-position").invoke("text").then((t) => {
            const m = /Bar (\d+)\/(\d+)/.exec(t);
            expect(m, `position text '${t}'`).to.not.equal(null);
            return { bar: Number(m![1]), of: Number(m![2]) };
        });

    const runBacktest = (date: string) => {
        cy.get("#backtest-date").clear().type(date);
        cy.get("#backtest-run").click();
        cy.get("#backtest-ghost", { timeout: 30000 }).should("exist");
        cy.get("#ghost-main .ghost-position").should("contain.text", "Bar 1/");
    };

    before(() => {
        cy.registerAndLogin(email, testPass);
        cy.visitInteractive("/builder");
        cy.typeStable("#b-title", title);
        cy.typeStable("#b-symbol", "AAPL");
        cy.typeStable(
            "#b-prose",
            "Long AAPL above VWAP. " +
                '[[script: Stock.Ticker("AAPL").IsAboveVwap().Long().TakeProfitPercent(2).StopLossPercent(1).Build()]]'
        );
        cy.get("#b-generate").click();
        cy.get("#b-script", { timeout: 30000 }).should("contain.value", "Stock.Ticker");
        cy.contains("button", "Save").click();
        cy.contains(/saved\./i, { timeout: 10000 });
        cy.fixture("ghost-branched-strategy.json").then((json) =>
            cy.task("setStrategyScriptJson", { title, json: JSON.stringify(json) })
        );
    });

    beforeEach(() => {
        // Cypress clears cookies between tests; the account from before() signs back in.
        cy.login(email, testPass);
        cy.visitInteractive("/backtest");
        cy.get("#backtest-strategy").select(`${title} (AAPL)`);
    });

    it("steps forward and back, plays at the chosen speed, and pauses", () => {
        runBacktest("2026-05-29");

        cy.get("#ghost-main .ghost-back").should("be.disabled");
        cy.get("#ghost-main .ghost-step").click();
        cy.get("#ghost-main .ghost-position").should("contain.text", "Bar 2/");
        cy.get("#ghost-main .ghost-back").click();
        cy.get("#ghost-main .ghost-position").should("contain.text", "Bar 1/");

        cy.get("#ghost-main .ghost-speed").select("30");
        cy.get("#ghost-main .ghost-play").click().should("have.attr", "aria-pressed", "true").and("contain.text", "Pause");
        cy.wait(1500);
        cy.get("#ghost-main .ghost-play").click().should("have.attr", "aria-pressed", "false").and("contain.text", "Play");
        position().then((paused) => {
            expect(paused.bar, "30x for 1.5s advances well past a few bars").to.be.greaterThan(10);
            cy.wait(700);
            position().its("bar").should("equal", paused.bar);
        });

        cy.get("#ghost-main .ghost-restart").click();
        cy.get("#ghost-main .ghost-position").should("contain.text", "Bar 1/");
        cy.get("#ghost-main .ghost-frame").should("contain.text", "If block 1:");
    });

    it("shows fork markers and a tooltip with every branch when hovering one", () => {
        runBacktest("2026-05-29");
        cy.get("#backtest-ghost").invoke("text").then((t) => {
            const forks = Number(/(\d+) fork\(s\)/.exec(t)![1]);
            expect(forks, "an IsAboveVwap If/Else flips during a mock day").to.be.greaterThan(0);
        });

        // Play to the end so every fork is revealed.
        cy.get("#ghost-main .ghost-speed").select("30");
        cy.get("#ghost-main .ghost-play").click();
        cy.get("#ghost-main .ghost-play", { timeout: 90000 }).should("have.attr", "aria-pressed", "false");
        position().then((p) => expect(p.bar).to.equal(p.of));

        // Sweep the pointer across the chart until the crosshair lands on a fork bar.
        cy.get("#ghost-main .ip-chart").then(($chart) => {
            const win = $chart[0].ownerDocument.defaultView!;
            const rect = $chart[0].getBoundingClientRect();
            const y = rect.top + rect.height * 0.4;
            const tip = $chart[0].querySelector(".ghost-tip") as HTMLElement;
            const frame = () => new Promise<void>((r) => win.requestAnimationFrame(() => r()));
            const sweep = async () => {
                const first = win.document.elementFromPoint(rect.left + 2, y);
                first?.dispatchEvent(new win.MouseEvent("mouseenter", { clientX: rect.left + 2, clientY: y, view: win }));
                for (let x = rect.left + 2; x < rect.right - 60; x += 6) {
                    const target = win.document.elementFromPoint(x, y);
                    if (!target) continue;
                    const init = { clientX: x, clientY: y, bubbles: true, cancelable: true, view: win, pointerType: "mouse", isPrimary: true };
                    target.dispatchEvent(new win.PointerEvent("pointermove", init));
                    target.dispatchEvent(new win.MouseEvent("mousemove", init));
                    await frame();
                    if (tip && tip.style.display === "block") return tip.textContent ?? "";
                }
                return "";
            };
            return cy.wrap(null).then({ timeout: 30000 }, () => sweep()).then((shown) => {
                expect(shown, "a fork tooltip appeared while sweeping the chart").to.not.equal("");
                expect(shown).to.match(/If/);
                expect(shown).to.match(/Else/);
            });
        });
    });

    it("compares two runs side by side and downloads the branch tree as JSON and Mermaid", () => {
        runBacktest("2026-05-29");
        cy.get("#ghost-compare-add").click();
        cy.get("#ghost-compare .ghost-compare-row").should("have.length", 1);

        runBacktest("2026-05-28");
        cy.get("#ghost-compare-add").click();
        cy.get("#ghost-compare .ghost-compare-row").should("have.length", 2);
        cy.get("#ghost-compare").should("contain.text", "2026-05-29").and("contain.text", "2026-05-28");
        cy.get("#ghost-compare-0 .ip-chart").should("exist");
        cy.get("#ghost-compare-1 .ip-chart").should("exist");
        cy.get("#ghost-compare-1 .ghost-step").click();
        cy.get("#ghost-compare-1 .ghost-position").should("contain.text", "Bar 2/");
        cy.get("#ghost-compare-0 .ghost-position").should("contain.text", "Bar 1/");

        // Assert on the real browser downloads (a cy.spy on ipChart.download would fail Blazor's
        // cross-realm `instanceof Function` check and break the circuit).
        cy.get("#ghost-export-json").click();
        cy.readFile("cypress/downloads/AAPL-2026-05-28-branch-tree.json", { timeout: 15000 }).then((tree) => {
            expect(JSON.stringify(tree)).to.match(/If/);
        });

        cy.get("#ghost-export-mermaid").click();
        cy.readFile("cypress/downloads/AAPL-2026-05-28-branch-tree.mmd", { timeout: 15000 })
            .should("match", /graph|flowchart/);
        cy.get("#backtest-ghost").should("exist");

        cy.get("#ghost-compare-clear").click();
        cy.get("#ghost-compare").should("not.exist");
    });
});
