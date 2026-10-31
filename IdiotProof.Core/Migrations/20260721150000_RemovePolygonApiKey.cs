using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    // Hand-written without a Designer file; these attributes are what make EF discover it.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260721150000_RemovePolygonApiKey")]
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
