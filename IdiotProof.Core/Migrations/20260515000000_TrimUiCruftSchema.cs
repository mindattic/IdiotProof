using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    // Hand-written: drops every table + column that became orphan after the UI
    // reset. Re-creating them is intentionally a no-op in Down() — the historical
    // migrations that originally added them remain in the migration history and
    // can be revived by reverting this migration only after restoring the
    // matching CLR entity classes (LearningArticle, Workspace, PolygonApiKey).
    [DbContext(typeof(AppDbContext))]
    [Migration("20260515000000_TrimUiCruftSchema")]
    public class TrimUiCruftSchema : Migration
    {
        protected override void Up(MigrationBuilder mb)
        {
            mb.DropTable(name: "LearningArticles");
            mb.DropTable(name: "Workspaces");
            mb.DropColumn(name: "PolygonApiKey", table: "UserApiKeys");
        }

        protected override void Down(MigrationBuilder mb)
        {
            // Restoring PolygonApiKey is cheap; recreate the column so a revert
            // doesn't lose access to anyone's stored Polygon key (it sat
            // encrypted before; we no longer re-encrypt anything here, the
            // column just exists again).
            mb.AddColumn<string>(
                name: "PolygonApiKey",
                table: "UserApiKeys",
                type: "nvarchar(max)",
                nullable: true);

            // LearningArticles and Workspaces re-creation is left intentionally
            // empty. If you need them back, revive the entity classes and let
            // EF tooling scaffold a fresh additive migration.
        }
    }
}
