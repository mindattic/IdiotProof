using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeDiary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TradeDiary",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StrategyTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Broker = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsPaper = table.Column<bool>(type: "bit", nullable: false),
                    EntryUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Notional = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    EntryOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StopLossPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    StopLossPercent = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    TrailingStopPercent = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    TakeProfitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    PeakGivebackPercent = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    PeakGivebackArmEt = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    SellByEt = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    ExitUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ExitReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ExitOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RealizedPnL = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ReturnPercent = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeDiary", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradeDiary_OwnerUserId_EntryUtc",
                table: "TradeDiary",
                columns: new[] { "OwnerUserId", "EntryUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeDiary_StrategyId_Status",
                table: "TradeDiary",
                columns: new[] { "StrategyId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeDiary");
        }
    }
}
