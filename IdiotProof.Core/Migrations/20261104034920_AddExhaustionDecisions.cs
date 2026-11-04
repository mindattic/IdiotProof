using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddExhaustionDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExhaustionDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EtDate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Armed = table.Column<bool>(type: "bit", nullable: false),
                    AnalysisOnly = table.Column<bool>(type: "bit", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ClosedBars = table.Column<int>(type: "int", nullable: false),
                    MoveAtr = table.Column<double>(type: "float", nullable: false),
                    ExtremePrice = table.Column<double>(type: "float", nullable: false),
                    TriggerPrice = table.Column<double>(type: "float", nullable: false),
                    ReversalSuccesses = table.Column<int>(type: "int", nullable: true),
                    ReversalSamples = table.Column<int>(type: "int", nullable: true),
                    ReversalProbability = table.Column<double>(type: "float", nullable: true),
                    ReversalLow = table.Column<double>(type: "float", nullable: true),
                    ReversalHigh = table.Column<double>(type: "float", nullable: true),
                    TrapDetector = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TrapProbability = table.Column<double>(type: "float", nullable: true),
                    TrapSamples = table.Column<int>(type: "int", nullable: true),
                    StopLossPercent = table.Column<double>(type: "float", nullable: true),
                    TrailingStopPercent = table.Column<double>(type: "float", nullable: true),
                    TakeProfitPercent = table.Column<double>(type: "float", nullable: true),
                    AtrPercent = table.Column<double>(type: "float", nullable: true),
                    Regime = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    FeaturesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExhaustionDecisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExhaustionDecisions_StrategyId",
                table: "ExhaustionDecisions",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExhaustionDecisions_Symbol_EtDate",
                table: "ExhaustionDecisions",
                columns: new[] { "Symbol", "EtDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExhaustionDecisions");
        }
    }
}
