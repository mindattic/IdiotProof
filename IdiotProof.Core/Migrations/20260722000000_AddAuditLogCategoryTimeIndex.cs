using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    // Hand-written without a Designer file; these attributes are what make EF discover it.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260722000000_AddAuditLogCategoryTimeIndex")]
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
