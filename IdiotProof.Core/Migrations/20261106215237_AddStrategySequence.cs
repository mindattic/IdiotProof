using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategySequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AwaitingTransition",
                table: "Strategies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "StepArmedUtc",
                table: "Strategies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StepIndex",
                table: "Strategies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StepCount",
                table: "ConditionProgress",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StepIndex",
                table: "ConditionProgress",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwaitingTransition",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "StepArmedUtc",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "StepIndex",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "StepCount",
                table: "ConditionProgress");

            migrationBuilder.DropColumn(
                name: "StepIndex",
                table: "ConditionProgress");
        }
    }
}
