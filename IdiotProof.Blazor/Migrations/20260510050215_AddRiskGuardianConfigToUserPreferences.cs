using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskGuardianConfigToUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RiskAccountBalance",
                table: "UserPreferences",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RiskMaxAccountRiskPercent",
                table: "UserPreferences",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RiskMaxLossPerDay",
                table: "UserPreferences",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RiskMaxLossPerTrade",
                table: "UserPreferences",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RiskMaxStopLossPercent",
                table: "UserPreferences",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RiskMinStopLossPercent",
                table: "UserPreferences",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RiskAccountBalance",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "RiskMaxAccountRiskPercent",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "RiskMaxLossPerDay",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "RiskMaxLossPerTrade",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "RiskMaxStopLossPercent",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "RiskMinStopLossPercent",
                table: "UserPreferences");
        }
    }
}
