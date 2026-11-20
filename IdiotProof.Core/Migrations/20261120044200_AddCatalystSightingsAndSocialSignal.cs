using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalystSightingsAndSocialSignal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstSeenSource",
                table: "CatalystEvents",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSeenUtc",
                table: "CatalystEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StoryId",
                table: "CatalystEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CatalystSightings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PublishedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Headline = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Similarity = table.Column<double>(type: "float", nullable: false),
                    Backfilled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalystSightings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalystSightings_CatalystEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "CatalystEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SocialSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ObservedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WindowMinutes = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    Capped = table.Column<bool>(type: "bit", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    Score = table.Column<double>(type: "float", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialSignals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalystEvents_StoryId",
                table: "CatalystEvents",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalystSightings_EventId",
                table: "CatalystSightings",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalystSightings_SeenUtc",
                table: "CatalystSightings",
                column: "SeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CatalystSightings_Source_ExternalId",
                table: "CatalystSightings",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialSignals_Source_ObservedUtc",
                table: "SocialSignals",
                columns: new[] { "Source", "ObservedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialSignals_Symbol_ObservedUtc",
                table: "SocialSignals",
                columns: new[] { "Symbol", "ObservedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalystSightings");

            migrationBuilder.DropTable(
                name: "SocialSignals");

            migrationBuilder.DropIndex(
                name: "IX_CatalystEvents_StoryId",
                table: "CatalystEvents");

            migrationBuilder.DropColumn(
                name: "FirstSeenSource",
                table: "CatalystEvents");

            migrationBuilder.DropColumn(
                name: "FirstSeenUtc",
                table: "CatalystEvents");

            migrationBuilder.DropColumn(
                name: "StoryId",
                table: "CatalystEvents");
        }
    }
}
