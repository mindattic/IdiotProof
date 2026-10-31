using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommandLedger",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Caller = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ArgsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ExitCode = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandLedger", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommandLedger_Operation_TimestampUtc",
                table: "CommandLedger",
                columns: new[] { "Operation", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandLedger_OwnerUserId_TimestampUtc",
                table: "CommandLedger",
                columns: new[] { "OwnerUserId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandLedger_TimestampUtc",
                table: "CommandLedger",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandLedger");
        }
    }
}
