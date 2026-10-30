using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AutoGapperScanPhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AutoGapperScans_ScanEtDate",
                table: "AutoGapperScans");

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "AutoGapperScans",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_AutoGapperScans_ScanEtDate_Phase",
                table: "AutoGapperScans",
                columns: new[] { "ScanEtDate", "Phase" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AutoGapperScans_ScanEtDate_Phase",
                table: "AutoGapperScans");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "AutoGapperScans");

            migrationBuilder.CreateIndex(
                name: "IX_AutoGapperScans_ScanEtDate",
                table: "AutoGapperScans",
                column: "ScanEtDate",
                unique: true);
        }
    }
}
