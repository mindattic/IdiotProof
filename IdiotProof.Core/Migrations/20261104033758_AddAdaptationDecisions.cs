using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAdaptationDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdaptationDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EtDate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedBars = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    FeaturesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VariantsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChosenVariant = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AppliedOverrides = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RiskSummary = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    OriginalScriptJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RealizedTrades = table.Column<int>(type: "int", nullable: true),
                    RealizedPnL = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RealizedReturnPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    OutcomeRecordedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdaptationDecisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdaptationDecisions_Outcome_OutcomeRecordedUtc",
                table: "AdaptationDecisions",
                columns: new[] { "Outcome", "OutcomeRecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdaptationDecisions_StrategyId_EtDate",
                table: "AdaptationDecisions",
                columns: new[] { "StrategyId", "EtDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdaptationDecisions");
        }
    }
}
