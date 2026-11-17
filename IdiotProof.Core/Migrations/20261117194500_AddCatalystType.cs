using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalystType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IP-T487: the catalyst type and the non-binding-LOI flag beside the frozen version 1 class (PreClass), which stays.
            // The type is nullable so rows written before this migration, or by an older build, stay readable.
            migrationBuilder.AddColumn<string>(
                name: "CatalystType",
                table: "CatalystEvents",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NonBindingLoi",
                table: "CatalystEvents",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CatalystType",
                table: "CatalystEvents");

            migrationBuilder.DropColumn(
                name: "NonBindingLoi",
                table: "CatalystEvents");
        }
    }
}
