import { defineConfig } from "cypress";
import { execFileSync } from "child_process";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";

/**
 * Cypress configuration for IdiotProof's Blazor Server app.
 *
 * Override the target URL with the CYPRESS_BASE_URL env var:
 *     CYPRESS_BASE_URL=http://localhost:5294 npm run run
 *
 * Test users: tests register a fresh account on first run via /register
 * (see commands.ts). The cleanup hook truncates the test database between
 * runs — never point CYPRESS_BASE_URL at a production install.
 *
 * The seedConditionProgress task writes a ConditionProgress row straight
 * into the same database the app reads (LocalDB by default; override with
 * IDIOTPROOF_SQL_SERVER / IDIOTPROOF_SQL_DB). It stands in for a Monitor
 * tick so the Strategies-page live badge can be asserted without running
 * the Monitor during the UI suite.
 *
 * The a11yLog task backs cy.checkPageA11y() (see support/commands.ts) — it
 * logs cypress-axe violations to the terminal without failing the run. This
 * is the report-only baseline period; once a full suite run is clean, swap
 * checkA11y's violation callback for the default throwing behavior.
 */
const SQL_SERVER = process.env.IDIOTPROOF_SQL_SERVER ?? "(localdb)\\MSSQLLocalDB";
const SQL_DB = process.env.IDIOTPROOF_SQL_DB ?? "IdiotProof";

export default defineConfig({
  e2e: {
    baseUrl: process.env.CYPRESS_BASE_URL ?? "https://localhost:5001",
    supportFile: "cypress/support/e2e.ts",
    specPattern: "cypress/e2e/**/*.cy.ts",
    viewportWidth: 1440,
    viewportHeight: 900,
    video: false,
    screenshotOnRunFailure: true,
    chromeWebSecurity: false,
    defaultCommandTimeout: 10000,
    setupNodeEvents(on) {
      on("task", {
        /**
         * Upsert a ConditionProgress row for a strategy, exactly as the
         * Monitor's ConditionProgressRepository.UpsertAsync does per tick.
         */
        seedConditionProgress({
          strategyId,
          passed,
          total,
          verb,
        }: {
          strategyId: string;
          passed: number;
          total: number;
          verb: string | null;
        }) {
          if (!/^[0-9a-fA-F-]{36}$/.test(strategyId)) {
            throw new Error(`seedConditionProgress: '${strategyId}' is not a GUID`);
          }
          const verbSql =
            verb === null ? "NULL" : `N'${String(verb).replace(/'/g, "''")}'`;
          const sql =
            `IF EXISTS (SELECT 1 FROM ConditionProgress WHERE StrategyId='${strategyId}') ` +
            `UPDATE ConditionProgress SET PassedCount=${Number(passed)}, TotalCount=${Number(total)}, ` +
            `FirstFailingVerb=${verbSql}, EvaluatedUtc=SYSUTCDATETIME() WHERE StrategyId='${strategyId}' ` +
            `ELSE INSERT INTO ConditionProgress (StrategyId, PassedCount, TotalCount, FirstFailingVerb, EvaluatedUtc) ` +
            `VALUES ('${strategyId}', ${Number(passed)}, ${Number(total)}, ${verbSql}, SYSUTCDATETIME());`;
          execFileSync("sqlcmd", ["-S", SQL_SERVER, "-d", SQL_DB, "-E", "-b", "-Q", sql], {
            stdio: "pipe",
          });
          return null;
        },

        /**
         * Replace a strategy's canonical JSON by its (unique) title. The text parser has no
         * If/Else syntax, so a branched strategy can only be saved through its JSON.
         */
        setStrategyScriptJson({ title, json }: { title: string; json: string }) {
          JSON.parse(json);
          const esc = (s: string) => s.replace(/'/g, "''");
          const sql =
            `UPDATE Strategies SET ScriptJson=N'${esc(json)}', UpdatedUtc=SYSUTCDATETIME() ` +
            `WHERE Title=N'${esc(title)}'; IF @@ROWCOUNT <> 1 THROW 50000, 'expected exactly one strategy', 1;`;
          // Via a file: JSON's double quotes don't survive Windows command-line quoting for -Q.
          const file = path.join(os.tmpdir(), `ip-cy-${Date.now()}.sql`);
          fs.writeFileSync(file, sql, "utf8");
          try {
            execFileSync("sqlcmd", ["-S", SQL_SERVER, "-d", SQL_DB, "-E", "-b", "-f", "65001", "-i", file], {
              stdio: "pipe",
            });
          } finally {
            fs.unlinkSync(file);
          }
          return null;
        },

        a11yLog(violations: Array<{ id: string; impact?: string; help: string; nodes: Array<{ target: string[] }> }>) {
          violations.forEach((v) => {
            // eslint-disable-next-line no-console
            console.log(
              `[a11y] ${v.impact ?? "?"} ${v.id}: ${v.help} (${v.nodes.length} node(s): ${v.nodes
                .map((n) => n.target.join(" "))
                .join(", ")})`
            );
          });
          return null;
        },
      });
    },
  },
});
