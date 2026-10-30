using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class GapperPositionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EntryFilledUtc",
                table: "Strategies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LastEntryPrice",
                table: "Strategies",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LastExitPrice",
                table: "Strategies",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastExitReason",
                table: "Strategies",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastExitedUtc",
                table: "Strategies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PositionQty",
                table: "Strategies",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EntryFilledUtc",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "LastEntryPrice",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "LastExitPrice",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "LastExitReason",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "LastExitedUtc",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "PositionQty",
                table: "Strategies");
        }
    }
}
