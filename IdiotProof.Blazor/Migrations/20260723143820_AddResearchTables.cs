using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResearchClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ticker = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceTier = table.Column<int>(type: "int", nullable: false),
                    ArticleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClaimSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Sentiment = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Magnitude = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    HasHappenedAlready = table.Column<bool>(type: "bit", nullable: false),
                    PendingTrigger = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ExpectedTimeline = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TriggerConfidence = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LlmAnswer = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PriceAtClaim = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PriceAtOutcome = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    OutcomeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OutcomePctChange = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: true),
                    RawArticleSnippet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceTrustScores",
                columns: table => new
                {
                    SourceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceTier = table.Column<int>(type: "int", nullable: false),
                    TotalClaims = table.Column<int>(type: "int", nullable: false),
                    PortentsClaimed = table.Column<int>(type: "int", nullable: false),
                    PortentsRealized = table.Column<int>(type: "int", nullable: false),
                    ImmediateClaims = table.Column<int>(type: "int", nullable: false),
                    ImmediateCorrect = table.Column<int>(type: "int", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceTrustScores", x => x.SourceName);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchClaims_ArticleDate",
                table: "ResearchClaims",
                column: "ArticleDate");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchClaims_Ticker_CreatedUtc",
                table: "ResearchClaims",
                columns: new[] { "Ticker", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchClaims_Ticker_Status",
                table: "ResearchClaims",
                columns: new[] { "Ticker", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResearchClaims");

            migrationBuilder.DropTable(
                name: "SourceTrustScores");
        }
    }
}
