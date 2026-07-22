using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    public partial class AddAuditLogCategoryTimeIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Category_TimestampUtc",
                table: "AuditLogs",
                columns: ["Category", "TimestampUtc"]);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Category_TimestampUtc",
                table: "AuditLogs");
        }
    }
}
