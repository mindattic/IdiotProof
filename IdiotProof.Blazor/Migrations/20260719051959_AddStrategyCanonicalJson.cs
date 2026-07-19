using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyCanonicalJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScriptJson",
                table: "Strategies",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScriptJson",
                table: "Strategies");
        }
    }
}
