using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyResolvedEntryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InitialPositionQty",
                table: "Strategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedEntryScriptJson",
                table: "Strategies",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialPositionQty",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "ResolvedEntryScriptJson",
                table: "Strategies");
        }
    }
}
