using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveBars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiveBars",
                columns: table => new
                {
                    Id            = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StrategyId    = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateEt        = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Et            = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Min           = table.Column<int>(type: "int", nullable: false),
                    Open          = table.Column<double>(type: "float", nullable: false),
                    High          = table.Column<double>(type: "float", nullable: false),
                    Low           = table.Column<double>(type: "float", nullable: false),
                    Close         = table.Column<double>(type: "float", nullable: false),
                    Volume        = table.Column<long>(type: "bigint", nullable: false),
                    Vwap          = table.Column<double>(type: "float", nullable: false),
                    WindowHigh    = table.Column<double>(type: "float", nullable: false),
                    Volx          = table.Column<double>(type: "float", nullable: false),
                    InSession     = table.Column<bool>(type: "bit", nullable: false),
                    CondBitsJson  = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Fire          = table.Column<bool>(type: "bit", nullable: false),
                    Exit          = table.Column<bool>(type: "bit", nullable: false),
                    WrittenUtc    = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveBars", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiveBars_StrategyId_DateEt_Min",
                table: "LiveBars",
                columns: new[] { "StrategyId", "DateEt", "Min" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveBars_WrittenUtc",
                table: "LiveBars",
                column: "WrittenUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LiveBars");
        }
    }
}
