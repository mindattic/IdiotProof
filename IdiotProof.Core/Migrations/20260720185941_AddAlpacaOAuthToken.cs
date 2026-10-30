using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddAlpacaOAuthToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlpacaOAuthAccessToken",
                table: "UserApiKeys",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlpacaOAuthRefreshToken",
                table: "UserApiKeys",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlpacaOAuthScope",
                table: "UserApiKeys",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlpacaOAuthAccessToken",
                table: "UserApiKeys");

            migrationBuilder.DropColumn(
                name: "AlpacaOAuthRefreshToken",
                table: "UserApiKeys");

            migrationBuilder.DropColumn(
                name: "AlpacaOAuthScope",
                table: "UserApiKeys");
        }
    }
}
