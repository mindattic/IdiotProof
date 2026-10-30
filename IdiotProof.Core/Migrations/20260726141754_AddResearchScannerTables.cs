using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchScannerTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AffectedTickersJson",
                table: "ResearchClaims",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMacro",
                table: "ResearchClaims",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "SignificanceScore",
                table: "ResearchClaims",
                type: "float",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InsiderTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FilerRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TransactionCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SharesTransacted = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PricePerShare = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    DollarValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    SharesOwnedAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PctOfHoldingsChanged = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: true),
                    FilingUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TickersScanned = table.Column<int>(type: "int", nullable: false),
                    UniverseSize = table.Column<int>(type: "int", nullable: false),
                    ClaimsFound = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackedTickers",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Exchange = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsTradable = table.Column<bool>(type: "bit", nullable: false),
                    LastPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SharesOutstanding = table.Column<long>(type: "bigint", nullable: true),
                    LastRefreshedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedTickers", x => x.Symbol);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchClaims_IsMacro",
                table: "ResearchClaims",
                column: "IsMacro");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchClaims_SignificanceScore",
                table: "ResearchClaims",
                column: "SignificanceScore");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransactions_ClaimId",
                table: "InsiderTransactions",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanRuns_StartedUtc",
                table: "ScanRuns",
                column: "StartedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedTickers_Exchange",
                table: "TrackedTickers",
                column: "Exchange");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedTickers_LastRefreshedUtc",
                table: "TrackedTickers",
                column: "LastRefreshedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InsiderTransactions");

            migrationBuilder.DropTable(
                name: "ScanRuns");

            migrationBuilder.DropTable(
                name: "TrackedTickers");

            migrationBuilder.DropIndex(
                name: "IX_ResearchClaims_IsMacro",
                table: "ResearchClaims");

            migrationBuilder.DropIndex(
                name: "IX_ResearchClaims_SignificanceScore",
                table: "ResearchClaims");

            migrationBuilder.DropColumn(
                name: "AffectedTickersJson",
                table: "ResearchClaims");

            migrationBuilder.DropColumn(
                name: "IsMacro",
                table: "ResearchClaims");

            migrationBuilder.DropColumn(
                name: "SignificanceScore",
                table: "ResearchClaims");
        }
    }
}
