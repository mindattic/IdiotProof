using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddMomentumClaimCatalyst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IP-T493: a Momentum. pick records the catalyst he gives for the move (as said) and its type.
            // Additive and nullable: existing claims keep their values and show no catalyst until re-extracted.
            migrationBuilder.AddColumn<string>(
                name: "Catalyst",
                table: "MomentumClaims",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CatalystType",
                table: "MomentumClaims",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Catalyst",
                table: "MomentumClaims");

            migrationBuilder.DropColumn(
                name: "CatalystType",
                table: "MomentumClaims");
        }
    }
}
