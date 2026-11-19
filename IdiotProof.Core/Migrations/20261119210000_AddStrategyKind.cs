using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IP-T593: a strategy carries the kind the code path that created it gave it, so tools (the daily
            // gapper retirement, IP-T590) select gapper strategies by kind instead of by "Gapper" in the title.
            // Additive and nullable: null = hand-made or unspecified.
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Strategies",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            // Backfill the rows written before kinds existed by the titles their creators gave them:
            // radar/profile gappers ("SYM Gapper — profile") and auto-armed ones ("SYM Auto-Gapper +N% (class)"),
            // Momentum. picks from `gapper momentum-arm` ("Momentum pick SYM yyyy-MM-dd PM-high break"), and the
            // exhaustion scanner's longs ("SYM Exhaustion Long").
            migrationBuilder.Sql("UPDATE Strategies SET Kind = 'gapper' WHERE Kind IS NULL AND Title LIKE '%Gapper%';");
            migrationBuilder.Sql("UPDATE Strategies SET Kind = 'momentum' WHERE Kind IS NULL AND Title LIKE 'Momentum pick %';");
            migrationBuilder.Sql("UPDATE Strategies SET Kind = 'exhaustion' WHERE Kind IS NULL AND Title LIKE '% Exhaustion Long%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Strategies");
        }
    }
}
