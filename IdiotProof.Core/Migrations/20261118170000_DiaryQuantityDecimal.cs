using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class DiaryQuantityDecimal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IP-T566: the diary books the exact filled quantity (fractional shares, crypto) instead of
            // a rounded-up whole share. Widening int -> decimal(18,9) keeps every existing value.
            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "TradeDiary",
                type: "decimal(18,9)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "TradeDiary",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,9)");
        }
    }
}
