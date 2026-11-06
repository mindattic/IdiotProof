using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalystEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalystEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PublishedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrimarySymbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SymbolsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Headline = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FormType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ItemsJson = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceTier = table.Column<int>(type: "int", nullable: false),
                    PreClass = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    PreDirection = table.Column<int>(type: "int", nullable: false),
                    PreConfidence = table.Column<double>(type: "float", nullable: false),
                    LlmType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    LlmSentiment = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LlmMagnitude = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LlmHasHappened = table.Column<bool>(type: "bit", nullable: true),
                    LlmMechanism = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LlmClassifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NoveltyCount = table.Column<int>(type: "int", nullable: false),
                    CatalystScore = table.Column<double>(type: "float", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    ScoreComputedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RawSnippet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Backfilled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalystEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalystEventSymbols",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalystEventSymbols", x => new { x.EventId, x.Symbol });
                    table.ForeignKey(
                        name: "FK_CatalystEventSymbols_CatalystEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "CatalystEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalystEvents_PrimarySymbol_PublishedUtc",
                table: "CatalystEvents",
                columns: new[] { "PrimarySymbol", "PublishedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalystEvents_ReceivedUtc",
                table: "CatalystEvents",
                column: "ReceivedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CatalystEvents_Source_ExternalId",
                table: "CatalystEvents",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalystEventSymbols_Symbol",
                table: "CatalystEventSymbols",
                column: "Symbol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalystEventSymbols");

            migrationBuilder.DropTable(
                name: "CatalystEvents");
        }
    }
}
