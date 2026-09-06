// e2e support file — Cypress loads this before every spec.
// Register custom commands here (login, seedStrategy, etc.).

import "./commands";
import "cypress-axe";

// Bypass uncaught exceptions on the Blazor SignalR reconnect path so a flaky
// websocket doesn't fail the whole test. We still surface assertion failures
// and any exception thrown inside the spec body.
Cypress.on("uncaught:exception", (err) => {
    if (err.message.includes("blazor.server.js")) return false;
    return true;
});
