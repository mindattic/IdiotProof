using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class DropUserApiKeysAlpacaIsPaper : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlpacaIsPaper",
                table: "UserApiKeys");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AlpacaIsPaper",
                table: "UserApiKeys",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
