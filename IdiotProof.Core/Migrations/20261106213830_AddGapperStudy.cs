using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGapperStudy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                table: "CatalystEvents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StampKind",
                table: "CatalystEvents",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "EdgarFilingIndex",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Cik = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    FormType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AccessionNumber = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AcceptedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceQuarter = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ItemsJson = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HeaderFetchedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgarFilingIndex", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GapperFormationSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DateEt = table.Column<DateOnly>(type: "date", nullable: false),
                    DecisionLabel = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DecisionTimeEt = table.Column<TimeOnly>(type: "time", nullable: false),
                    DecisionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Split = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FeatureVersion = table.Column<int>(type: "int", nullable: false),
                    IsCandidate = table.Column<bool>(type: "bit", nullable: false),
                    IsControl = table.Column<bool>(type: "bit", nullable: false),
                    GapPct = table.Column<double>(type: "float", nullable: true),
                    PmCumVol = table.Column<double>(type: "float", nullable: false),
                    VolRatioTod = table.Column<double>(type: "float", nullable: true),
                    OwnProfileRatio = table.Column<double>(type: "float", nullable: true),
                    Velocity1m = table.Column<double>(type: "float", nullable: true),
                    Velocity3m = table.Column<double>(type: "float", nullable: true),
                    Velocity5m = table.Column<double>(type: "float", nullable: true),
                    Velocity15m = table.Column<double>(type: "float", nullable: true),
                    Acceleration = table.Column<double>(type: "float", nullable: true),
                    VolumeRoc = table.Column<double>(type: "float", nullable: true),
                    TradesProxy = table.Column<int>(type: "int", nullable: false),
                    SpreadProxy = table.Column<double>(type: "float", nullable: true),
                    DistToPmHighPct = table.Column<double>(type: "float", nullable: true),
                    MinsSinceFirstAbnormal = table.Column<double>(type: "float", nullable: true),
                    SessionVwap = table.Column<double>(type: "float", nullable: true),
                    AbnormalReturnD1 = table.Column<double>(type: "float", nullable: true),
                    AbnormalReturnD2 = table.Column<double>(type: "float", nullable: true),
                    AbnormalReturnD3 = table.Column<double>(type: "float", nullable: true),
                    AbnormalReturnD4 = table.Column<double>(type: "float", nullable: true),
                    AbnormalReturnD5 = table.Column<double>(type: "float", nullable: true),
                    VolumeRatioD1 = table.Column<double>(type: "float", nullable: true),
                    VolumeRatioD2 = table.Column<double>(type: "float", nullable: true),
                    VolumeRatioD3 = table.Column<double>(type: "float", nullable: true),
                    VolumeRatioD4 = table.Column<double>(type: "float", nullable: true),
                    VolumeRatioD5 = table.Column<double>(type: "float", nullable: true),
                    DaysSinceReverseSplit = table.Column<int>(type: "int", nullable: true),
                    DaysSince424B = table.Column<int>(type: "int", nullable: true),
                    DaysSinceS1 = table.Column<int>(type: "int", nullable: true),
                    CatalystScore = table.Column<double>(type: "float", nullable: true),
                    CatalystAgeMinutes = table.Column<double>(type: "float", nullable: true),
                    CatalystDirection = table.Column<int>(type: "int", nullable: true),
                    CatalystPreClass = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    CatalystStampIsUpperBound = table.Column<bool>(type: "bit", nullable: true),
                    Halted = table.Column<bool>(type: "bit", nullable: true),
                    ExtraJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ComputedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GapperFormationSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GapperStudyCikMap",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    IsAdr = table.Column<bool>(type: "bit", nullable: false),
                    Cik = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CikSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    MappedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GapperStudyCikMap", x => x.Symbol);
                });

            migrationBuilder.CreateTable(
                name: "GapperStudyCoverage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tier = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GapperStudyCoverage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GapperStudyDailyBars",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DateEt = table.Column<DateOnly>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TradeCount = table.Column<long>(type: "bigint", nullable: false),
                    Vwap = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Feed = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GapperStudyDailyBars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GapperStudyEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DateEt = table.Column<DateOnly>(type: "date", nullable: false),
                    Split = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AdjPrevClose = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    GapMax = table.Column<double>(type: "float", nullable: false),
                    Tier20Touched = table.Column<bool>(type: "bit", nullable: false),
                    Tier50Touched = table.Column<bool>(type: "bit", nullable: false),
                    Tier100Touched = table.Column<bool>(type: "bit", nullable: false),
                    Tier20 = table.Column<bool>(type: "bit", nullable: false),
                    Tier50 = table.Column<bool>(type: "bit", nullable: false),
                    Tier100 = table.Column<bool>(type: "bit", nullable: false),
                    Tier20TouchTimeEt = table.Column<TimeOnly>(type: "time", nullable: true),
                    Tier50TouchTimeEt = table.Column<TimeOnly>(type: "time", nullable: true),
                    Tier100TouchTimeEt = table.Column<TimeOnly>(type: "time", nullable: true),
                    PmDollarVolumeAtTier20 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    PmDollarVolumeAtTier50 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    PmDollarVolumeAtTier100 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    SubType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    PmHigh = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    PmHighTimeEt = table.Column<TimeOnly>(type: "time", nullable: true),
                    RthHigh = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    HodTimeEt = table.Column<TimeOnly>(type: "time", nullable: true),
                    PmDollarVolume = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MinuteBars = table.Column<int>(type: "int", nullable: false),
                    SipShare = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GapperStudyEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GapperStudyLabels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DateEt = table.Column<DateOnly>(type: "date", nullable: false),
                    Split = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DecisionLabel = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DecisionTimeEt = table.Column<TimeOnly>(type: "time", nullable: false),
                    DecisionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EntryRef = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    EntryUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FwdMax0930 = table.Column<double>(type: "float", nullable: true),
                    FwdMax0945 = table.Column<double>(type: "float", nullable: true),
                    FwdMax1030 = table.Column<double>(type: "float", nullable: true),
                    FwdMax1600 = table.Column<double>(type: "float", nullable: true),
                    FwdClose0930 = table.Column<double>(type: "float", nullable: true),
                    FwdClose0945 = table.Column<double>(type: "float", nullable: true),
                    FwdClose1030 = table.Column<double>(type: "float", nullable: true),
                    FwdClose1600 = table.Column<double>(type: "float", nullable: true),
                    Reach20By0945 = table.Column<bool>(type: "bit", nullable: true),
                    PmHigh = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    HodTimePmEt = table.Column<TimeOnly>(type: "time", nullable: true),
                    HodTimeRthEt = table.Column<TimeOnly>(type: "time", nullable: true),
                    HodTimeEt = table.Column<TimeOnly>(type: "time", nullable: true),
                    HodBucket = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    Fade = table.Column<double>(type: "float", nullable: true),
                    FadeFlag30 = table.Column<bool>(type: "bit", nullable: true),
                    CloseBelowOpen = table.Column<bool>(type: "bit", nullable: true),
                    ExtensionFromOpen0945 = table.Column<double>(type: "float", nullable: true),
                    VelocityDeathTimeEt = table.Column<TimeOnly>(type: "time", nullable: true),
                    DeathToHodMin = table.Column<int>(type: "int", nullable: true),
                    HaltFlag = table.Column<bool>(type: "bit", nullable: true),
                    VwapLossTimeEt = table.Column<TimeOnly>(type: "time", nullable: true),
                    ComputedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GapperStudyLabels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GapperStudySplits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ExDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OldRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    NewRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GapperStudySplits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GapperStudyUniverseDays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DateEt = table.Column<DateOnly>(type: "date", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Split = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AdjPrevClose = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Adv20Dollars = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Adv20Shares = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Sessions = table.Column<int>(type: "int", nullable: false),
                    AdvDecile = table.Column<int>(type: "int", nullable: false),
                    PriceTier = table.Column<int>(type: "int", nullable: false),
                    IsAdr = table.Column<bool>(type: "bit", nullable: false),
                    IsCandidate = table.Column<bool>(type: "bit", nullable: false),
                    DayHigh = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    DayVolume = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    IsGapper = table.Column<bool>(type: "bit", nullable: false),
                    GapTier = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    GapperSubType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    GapMax = table.Column<double>(type: "float", nullable: true),
                    PremarketDollarVolumeAtTier = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ControlForSymbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ExcludedReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GapperStudyUniverseDays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EdgarFilingIndex_Cik_AccessionNumber",
                table: "EdgarFilingIndex",
                columns: new[] { "Cik", "AccessionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EdgarFilingIndex_Cik_FiledDate",
                table: "EdgarFilingIndex",
                columns: new[] { "Cik", "FiledDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EdgarFilingIndex_Symbol_FiledDate",
                table: "EdgarFilingIndex",
                columns: new[] { "Symbol", "FiledDate" });

            migrationBuilder.CreateIndex(
                name: "IX_GapperFormationSnapshots_Source_Symbol_DateEt_DecisionLabel",
                table: "GapperFormationSnapshots",
                columns: new[] { "Source", "Symbol", "DateEt", "DecisionLabel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GapperFormationSnapshots_Split_DecisionTimeEt",
                table: "GapperFormationSnapshots",
                columns: new[] { "Split", "DecisionTimeEt" });

            migrationBuilder.CreateIndex(
                name: "IX_GapperFormationSnapshots_Symbol_DateEt",
                table: "GapperFormationSnapshots",
                columns: new[] { "Symbol", "DateEt" });

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyCikMap_Cik",
                table: "GapperStudyCikMap",
                column: "Cik");

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyCoverage_Tier_Key",
                table: "GapperStudyCoverage",
                columns: new[] { "Tier", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyDailyBars_DateEt",
                table: "GapperStudyDailyBars",
                column: "DateEt");

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyDailyBars_Symbol_DateEt",
                table: "GapperStudyDailyBars",
                columns: new[] { "Symbol", "DateEt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyEvents_Split_Tier20",
                table: "GapperStudyEvents",
                columns: new[] { "Split", "Tier20" });

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyEvents_Symbol_DateEt",
                table: "GapperStudyEvents",
                columns: new[] { "Symbol", "DateEt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyLabels_Split_DecisionTimeEt",
                table: "GapperStudyLabels",
                columns: new[] { "Split", "DecisionTimeEt" });

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyLabels_Symbol_DateEt_DecisionLabel",
                table: "GapperStudyLabels",
                columns: new[] { "Symbol", "DateEt", "DecisionLabel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudySplits_Symbol_ExDate",
                table: "GapperStudySplits",
                columns: new[] { "Symbol", "ExDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyUniverseDays_DateEt_IsCandidate",
                table: "GapperStudyUniverseDays",
                columns: new[] { "DateEt", "IsCandidate" });

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyUniverseDays_Split_IsGapper",
                table: "GapperStudyUniverseDays",
                columns: new[] { "Split", "IsGapper" });

            migrationBuilder.CreateIndex(
                name: "IX_GapperStudyUniverseDays_Symbol_DateEt",
                table: "GapperStudyUniverseDays",
                columns: new[] { "Symbol", "DateEt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EdgarFilingIndex");

            migrationBuilder.DropTable(
                name: "GapperFormationSnapshots");

            migrationBuilder.DropTable(
                name: "GapperStudyCikMap");

            migrationBuilder.DropTable(
                name: "GapperStudyCoverage");

            migrationBuilder.DropTable(
                name: "GapperStudyDailyBars");

            migrationBuilder.DropTable(
                name: "GapperStudyEvents");

            migrationBuilder.DropTable(
                name: "GapperStudyLabels");

            migrationBuilder.DropTable(
                name: "GapperStudySplits");

            migrationBuilder.DropTable(
                name: "GapperStudyUniverseDays");

            migrationBuilder.DropColumn(
                name: "SourceName",
                table: "CatalystEvents");

            migrationBuilder.DropColumn(
                name: "StampKind",
                table: "CatalystEvents");
        }
    }
}
