using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddExhaustionDecisionOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OutcomeRecordedUtc",
                table: "ExhaustionDecisions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RealizedPnL",
                table: "ExhaustionDecisions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RealizedReturnPercent",
                table: "ExhaustionDecisions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RealizedTrades",
                table: "ExhaustionDecisions",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutcomeRecordedUtc",
                table: "ExhaustionDecisions");

            migrationBuilder.DropColumn(
                name: "RealizedPnL",
                table: "ExhaustionDecisions");

            migrationBuilder.DropColumn(
                name: "RealizedReturnPercent",
                table: "ExhaustionDecisions");

            migrationBuilder.DropColumn(
                name: "RealizedTrades",
                table: "ExhaustionDecisions");
        }
    }
}
