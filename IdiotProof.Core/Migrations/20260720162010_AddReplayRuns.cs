using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddReplayRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplayRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Strategy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DateEt = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Feed = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Stamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    GeneratedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedEt = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Fired = table.Column<bool>(type: "bit", nullable: false),
                    PayoffCount = table.Column<int>(type: "int", nullable: false),
                    TotalPnl = table.Column<double>(type: "float", nullable: false),
                    FirstFireEt = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    EntryPrice = table.Column<double>(type: "float", nullable: true),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StrategyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplayRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplayRuns_GeneratedUtc",
                table: "ReplayRuns",
                column: "GeneratedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReplayRuns_Symbol_GeneratedUtc",
                table: "ReplayRuns",
                columns: new[] { "Symbol", "GeneratedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplayRuns");
        }
    }
}
