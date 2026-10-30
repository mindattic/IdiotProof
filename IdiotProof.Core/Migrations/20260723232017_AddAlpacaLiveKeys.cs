using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddAlpacaLiveKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlpacaLiveApiKeyId",
                table: "UserApiKeys",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlpacaLiveApiSecretKey",
                table: "UserApiKeys",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlpacaLiveApiKeyId",
                table: "UserApiKeys");

            migrationBuilder.DropColumn(
                name: "AlpacaLiveApiSecretKey",
                table: "UserApiKeys");
        }
    }
}
