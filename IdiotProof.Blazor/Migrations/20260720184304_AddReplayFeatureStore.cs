using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddReplayFeatureStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplayBars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReplayRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DateEt = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Strategy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Et = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Min = table.Column<int>(type: "int", nullable: false),
                    Open = table.Column<double>(type: "float", nullable: false),
                    High = table.Column<double>(type: "float", nullable: false),
                    Low = table.Column<double>(type: "float", nullable: false),
                    Close = table.Column<double>(type: "float", nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false),
                    Vwap = table.Column<double>(type: "float", nullable: false),
                    WindowHigh = table.Column<double>(type: "float", nullable: false),
                    Volx = table.Column<double>(type: "float", nullable: false),
                    InSession = table.Column<bool>(type: "bit", nullable: false),
                    CondPassed = table.Column<int>(type: "int", nullable: false),
                    CondTotal = table.Column<int>(type: "int", nullable: false),
                    Fire = table.Column<bool>(type: "bit", nullable: false),
                    Exit = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplayBars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplayBars_ReplayRuns_ReplayRunId",
                        column: x => x.ReplayRunId,
                        principalTable: "ReplayRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReplayTrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReplayRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DateEt = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Strategy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Feed = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    EntryEt = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    EntryMin = table.Column<int>(type: "int", nullable: false),
                    EntryPx = table.Column<double>(type: "float", nullable: false),
                    ExitEt = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    ExitMin = table.Column<int>(type: "int", nullable: false),
                    HoldMin = table.Column<int>(type: "int", nullable: false),
                    ExitPx = table.Column<double>(type: "float", nullable: false),
                    PnlPct = table.Column<double>(type: "float", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Won = table.Column<bool>(type: "bit", nullable: false),
                    EntryVwap = table.Column<double>(type: "float", nullable: false),
                    EntryWindowHigh = table.Column<double>(type: "float", nullable: false),
                    EntryVolx = table.Column<double>(type: "float", nullable: false),
                    DistVwapPct = table.Column<double>(type: "float", nullable: false),
                    DistWinHighPct = table.Column<double>(type: "float", nullable: false),
                    GeneratedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplayTrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplayTrades_ReplayRuns_ReplayRunId",
                        column: x => x.ReplayRunId,
                        principalTable: "ReplayRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplayBars_ReplayRunId",
                table: "ReplayBars",
                column: "ReplayRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplayTrades_ReplayRunId",
                table: "ReplayTrades",
                column: "ReplayRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplayTrades_Symbol_Won",
                table: "ReplayTrades",
                columns: new[] { "Symbol", "Won" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplayBars");

            migrationBuilder.DropTable(
                name: "ReplayTrades");
        }
    }
}
