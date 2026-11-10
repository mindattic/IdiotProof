using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddMomentumClaimTracker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MomentumVideos",
                columns: table => new
                {
                    VideoId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PublishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TitleDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    DurationSec = table.Column<int>(type: "int", nullable: true),
                    ChannelIndex = table.Column<int>(type: "int", nullable: true),
                    FetchedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MomentumVideos", x => x.VideoId);
                });

            migrationBuilder.CreateTable(
                name: "MomentumClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VideoId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    ClaimType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SpokenSymbol = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SymbolConfidence = table.Column<double>(type: "float", nullable: true),
                    SymbolNote = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IdentityHint = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    StatedMovePct = table.Column<double>(type: "float", nullable: true),
                    SetupTags = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Scenarios = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferencePrice = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ClaimedPct = table.Column<double>(type: "float", nullable: true),
                    ClaimWindow = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RawQuote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtractedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExtractedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MomentumClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MomentumClaims_MomentumVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "MomentumVideos",
                        principalColumn: "VideoId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MomentumTranscripts",
                columns: table => new
                {
                    VideoId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SegmentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    TextHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExtractedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtractedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExtractError = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ImportedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MomentumTranscripts", x => x.VideoId);
                    table.ForeignKey(
                        name: "FK_MomentumTranscripts_MomentumVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "MomentumVideos",
                        principalColumn: "VideoId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MomentumClaimOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioIndex = table.Column<int>(type: "int", nullable: false),
                    VerifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataSource = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PriorClose = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Open0930 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    GapPct0930 = table.Column<double>(type: "float", nullable: true),
                    PremarketHigh = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    DayHigh = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    DayClose = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    TriggerKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    TriggerLevel = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    TriggerHit = table.Column<bool>(type: "bit", nullable: false),
                    TriggerUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TriggerPrice = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ConfirmHit = table.Column<bool>(type: "bit", nullable: true),
                    ConfirmLevel = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ConfirmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EntryPrice = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    EntryUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TargetsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TargetsHit = table.Column<int>(type: "int", nullable: false),
                    FirstTargetHit = table.Column<bool>(type: "bit", nullable: false),
                    FirstTargetUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnyTargetHit = table.Column<bool>(type: "bit", nullable: false),
                    InvalidationLevel = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    InvalidationSource = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    InvalidationHitBeforeTarget = table.Column<bool>(type: "bit", nullable: false),
                    MaxRunAfterTriggerPct = table.Column<double>(type: "float", nullable: true),
                    MaxDrawdownAfterTriggerPct = table.Column<double>(type: "float", nullable: true),
                    CloseVsTriggerPct = table.Column<double>(type: "float", nullable: true),
                    ExitPrice = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ExitUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExitReason = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    PlanPnlPct = table.Column<double>(type: "float", nullable: true),
                    Verdict = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    GainerRank = table.Column<int>(type: "int", nullable: true),
                    GainerCount = table.Column<int>(type: "int", nullable: true),
                    GapRank = table.Column<int>(type: "int", nullable: true),
                    DayHighPct = table.Column<double>(type: "float", nullable: true),
                    DayClosePct = table.Column<double>(type: "float", nullable: true),
                    PrevDayGainerRank = table.Column<int>(type: "int", nullable: true),
                    PrevDayClosePct = table.Column<double>(type: "float", nullable: true),
                    TopGapper = table.Column<bool>(type: "bit", nullable: true),
                    D1MaxRunPct = table.Column<double>(type: "float", nullable: true),
                    D1AnyTargetHit = table.Column<bool>(type: "bit", nullable: true),
                    BragActualHighPct = table.Column<double>(type: "float", nullable: true),
                    BragActualClosePct = table.Column<double>(type: "float", nullable: true),
                    BragActualLowToHighPct = table.Column<double>(type: "float", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MomentumClaimOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MomentumClaimOutcomes_MomentumClaims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "MomentumClaims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MomentumClaimOutcomes_ClaimId_ScenarioIndex",
                table: "MomentumClaimOutcomes",
                columns: new[] { "ClaimId", "ScenarioIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MomentumClaims_TradeDate_Symbol",
                table: "MomentumClaims",
                columns: new[] { "TradeDate", "Symbol" });

            migrationBuilder.CreateIndex(
                name: "IX_MomentumClaims_VideoId",
                table: "MomentumClaims",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_MomentumTranscripts_TradeDate",
                table: "MomentumTranscripts",
                column: "TradeDate");

            migrationBuilder.CreateIndex(
                name: "IX_MomentumVideos_PublishedUtc",
                table: "MomentumVideos",
                column: "PublishedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MomentumVideos_TradeDate",
                table: "MomentumVideos",
                column: "TradeDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MomentumClaimOutcomes");

            migrationBuilder.DropTable(
                name: "MomentumTranscripts");

            migrationBuilder.DropTable(
                name: "MomentumClaims");

            migrationBuilder.DropTable(
                name: "MomentumVideos");
        }
    }
}
