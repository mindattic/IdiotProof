/// <reference types="cypress" />

/**
 * Custom Cypress commands shared across specs. Each `Cypress.Commands.add`
 * also extends the global Chainable interface below so TypeScript hints work
 * in spec files.
 *
 * Blazor note: the app renders with a global InteractiveServer render mode,
 * so every page is first statically prerendered, then re-rendered when the
 * SignalR circuit attaches. Anything typed or clicked in that gap is wiped
 * by the second render. visitInteractive() waits for the circuit negotiate
 * before letting the spec interact, and typeStable() asserts the value stuck.
 */

declare global {
    // eslint-disable-next-line @typescript-eslint/no-namespace
    namespace Cypress {
        interface Chainable {
            /** cy.visit + wait for the Blazor Server circuit to attach. */
            visitInteractive(path: string): Chainable<void>;
            /** Type into an input and assert the value stuck (post-circuit). */
            typeStable(selector: string, value: string): Chainable<void>;
            /**
             * Sign up a brand-new user, then sign in (registration alone issues no cookie).
             * Password must satisfy the auth library: 12+ characters with a digit.
             */
            registerAndLogin(email: string, password: string): Chainable<void>;
            /** Submit the login form for an existing account. */
            login(email: string, password: string): Chainable<void>;
            /**
             * Inject axe-core and scan the current DOM. Logs violations via the
             * `a11yLog` task (report-only) rather than failing the test — flip
             * to a throwing assertion once a full baseline run is green.
             */
            checkPageA11y(context?: string): Chainable<void>;
        }
    }
}

Cypress.Commands.add("visitInteractive", (path: string) => {
    cy.intercept("POST", "**/_blazor/negotiate*").as("blazorNegotiate");
    cy.visit(path);
    cy.wait("@blazorNegotiate", { timeout: 20000 });
    // The circuit re-render replaces the prerendered DOM just after negotiate;
    // give it a beat so subsequent type/click hits the live DOM.
    cy.wait(500);
});

Cypress.Commands.add("typeStable", (selector: string, value: string) => {
    cy.get(selector).should("be.enabled").clear().type(value, { parseSpecialCharSequences: false });
    cy.get(selector).should("have.value", value);
});

Cypress.Commands.add("registerAndLogin", (email: string, password: string) => {
    cy.visitInteractive("/register");
    cy.typeStable('input[name="email"]', email);
    cy.typeStable('input[name="password"]', password);
    cy.typeStable('input[name="confirm"]', password);
    cy.get('button[type="submit"]').click();
    // /register-submit creates the account and redirects to /login?registered=1 —
    // it does NOT issue a cookie (sign-in is the auth library's own flow). A
    // redirect back to /register carries ?error=… (e.g. the library's 12-character
    // password minimum); surface that instead of a bare timeout.
    cy.location("pathname", { timeout: 10000 }).should("eq", "/login");
    cy.location("search").should("contain", "registered=1");
    cy.login(email, password);
});

Cypress.Commands.add("login", (email: string, password: string) => {
    // Programmatic sign-in through the auth library's own endpoint. The /login page
    // (prerendered HTML) carries the antiforgery token; cy.request shares the browser's
    // cookie jar, so the antiforgery cookie from the GET and the auth cookie from the
    // 302 both land in the browser. Driving the real form from the Electron runner
    // fails antiforgery validation (blank 400) even though the same POST succeeds from
    // a normal browser, so the UI path is deliberately not used here.
    cy.request("/login").then((page) => {
        const html = String(page.body);
        const match =
            /__RequestVerificationToken"[^>]*value="([^"]+)"/.exec(html) ??
            /value="([^"]+)"[^>]*name="__RequestVerificationToken"/.exec(html);
        expect(match, "antiforgery token on /login").to.not.equal(null);
        cy.request({
            method: "POST",
            url: "/_ma-auth/login",
            form: true,
            followRedirect: false,
            body: { userName: email, password, returnUrl: "/", __RequestVerificationToken: match![1] },
        }).then((res) => {
            expect(res.status, "login POST redirects on success").to.equal(302);
            expect(String(res.headers["location"]), "login redirect target").to.not.contain("error=1");
        });
    });
});

Cypress.Commands.add("checkPageA11y", (context?: string) => {
    cy.injectAxe();
    // skipFailures=true: report-only for now (see cypress.config.ts) — logs
    // violations via the a11yLog task instead of failing the test. Flip to
    // false once a full suite run is clean.
    cy.checkA11y(
        context,
        undefined,
        (violations) => {
            cy.task("a11yLog", violations, { log: false });
        },
        true
    );
});

export {};
