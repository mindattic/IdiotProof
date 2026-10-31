using System;
using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    // Hand-written without a Designer file; these attributes are what make EF discover it.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260721180000_AddConditionProgressLastPrice")]
    /// <inheritdoc />
    public partial class AddConditionProgressLastPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LastPrice",
                table: "ConditionProgress",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPriceUtc",
                table: "ConditionProgress",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LastPrice",    table: "ConditionProgress");
            migrationBuilder.DropColumn(name: "LastPriceUtc", table: "ConditionProgress");
        }
    }
}
