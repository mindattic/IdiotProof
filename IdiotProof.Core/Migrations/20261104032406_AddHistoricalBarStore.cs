using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricalBarStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoricalBarCoverage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Timeframe = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    DateEt = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BarCount = table.Column<int>(type: "int", nullable: false),
                    FetchedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalBarCoverage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HistoricalBars",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Timeframe = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    DateEt = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(20,2)", precision: 20, scale: 2, nullable: false),
                    Feed = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalBars", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalBarCoverage_Symbol_Timeframe_DateEt",
                table: "HistoricalBarCoverage",
                columns: new[] { "Symbol", "Timeframe", "DateEt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalBars_Symbol_Timeframe_DateEt",
                table: "HistoricalBars",
                columns: new[] { "Symbol", "Timeframe", "DateEt" });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalBars_Symbol_Timeframe_StartUtc",
                table: "HistoricalBars",
                columns: new[] { "Symbol", "Timeframe", "StartUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricalBarCoverage");

            migrationBuilder.DropTable(
                name: "HistoricalBars");
        }
    }
}
