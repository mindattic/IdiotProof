using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Blazor.Migrations
{
    /// <inheritdoc />
    public partial class AddConditionProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConditionProgress",
                columns: table => new
                {
                    StrategyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PassedCount = table.Column<int>(type: "int", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    FirstFailingVerb = table.Column<string>(type: "nvarchar(280)", maxLength: 280, nullable: true),
                    EvaluatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConditionProgress", x => x.StrategyId);
                    table.ForeignKey(
                        name: "FK_ConditionProgress_Strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "Strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConditionProgress_EvaluatedUtc",
                table: "ConditionProgress",
                column: "EvaluatedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConditionProgress");
        }
    }
}
