using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoGapperTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutoGapperScans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanEtDate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ScanStartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScanCompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MoversScreened = table.Column<int>(type: "int", nullable: false),
                    Qualified = table.Column<int>(type: "int", nullable: false),
                    Armed = table.Column<int>(type: "int", nullable: false),
                    Skipped = table.Column<int>(type: "int", nullable: false),
                    MinGapPercent = table.Column<double>(type: "float", nullable: false),
                    MaxCount = table.Column<int>(type: "int", nullable: false),
                    BrokerMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoGapperScans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutoGapperCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanEtDate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CapturedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Price = table.Column<double>(type: "float", nullable: false),
                    PreviousClose = table.Column<double>(type: "float", nullable: true),
                    GapPercent = table.Column<double>(type: "float", nullable: false),
                    PremarketVolume = table.Column<long>(type: "bigint", nullable: true),
                    AvgDailyVolume = table.Column<double>(type: "float", nullable: true),
                    VolumeRatio = table.Column<double>(type: "float", nullable: true),
                    AtrPercent = table.Column<double>(type: "float", nullable: true),
                    Score = table.Column<double>(type: "float", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    BehaviorClass = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    StopLossPercent = table.Column<double>(type: "float", nullable: false),
                    TrailingStopPercent = table.Column<double>(type: "float", nullable: true),
                    PeakGivebackPercent = table.Column<double>(type: "float", nullable: false),
                    ArmExitEt = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    SellByEt = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    MinVolumeRatio = table.Column<double>(type: "float", nullable: false),
                    PriceBandLow = table.Column<double>(type: "float", nullable: false),
                    PriceBandHigh = table.Column<double>(type: "float", nullable: false),
                    Notional = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Armed = table.Column<bool>(type: "bit", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SkipReason = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoGapperCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutoGapperCandidates_AutoGapperScans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "AutoGapperScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutoGapperCandidates_ScanId",
                table: "AutoGapperCandidates",
                column: "ScanId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoGapperCandidates_Symbol_ScanEtDate",
                table: "AutoGapperCandidates",
                columns: new[] { "Symbol", "ScanEtDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AutoGapperScans_ScanEtDate",
                table: "AutoGapperScans",
                column: "ScanEtDate",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoGapperCandidates");

            migrationBuilder.DropTable(
                name: "AutoGapperScans");
        }
    }
}
