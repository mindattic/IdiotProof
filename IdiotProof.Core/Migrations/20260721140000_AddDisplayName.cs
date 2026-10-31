using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    // Hand-written without a Designer file; these attributes are what make EF discover it.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260721140000_AddDisplayName")]
    /// <inheritdoc />
    public partial class AddDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "auth",
                table: "AuthUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedDisplayName",
                schema: "auth",
                table: "AuthUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_NormalizedDisplayName",
                schema: "auth",
                table: "AuthUsers",
                column: "NormalizedDisplayName",
                unique: true,
                filter: "[NormalizedDisplayName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthUsers_NormalizedDisplayName",
                schema: "auth",
                table: "AuthUsers");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "auth",
                table: "AuthUsers");

            migrationBuilder.DropColumn(
                name: "NormalizedDisplayName",
                schema: "auth",
                table: "AuthUsers");
        }
    }
}
