using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class RemovePolygonApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PolygonApiKey",
                table: "UserApiKeys");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PolygonApiKey",
                table: "UserApiKeys",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
