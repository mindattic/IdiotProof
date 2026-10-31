using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingConfirmations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingConfirmations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Preview = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedBy = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingConfirmations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingConfirmations_Code_ExpiresUtc",
                table: "PendingConfirmations",
                columns: new[] { "Code", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingConfirmations_OwnerUserId_CreatedUtc",
                table: "PendingConfirmations",
                columns: new[] { "OwnerUserId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingConfirmations");
        }
    }
}
