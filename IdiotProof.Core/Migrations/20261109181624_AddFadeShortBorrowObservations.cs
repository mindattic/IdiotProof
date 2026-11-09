using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdiotProof.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddFadeShortBorrowObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FadeShortBorrowObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateEt = table.Column<DateOnly>(type: "date", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CandidateSource = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Checked0940Utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Checked0946Utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignalEvaluatedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrevClose = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    PmHigh = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    PmHighPct = table.Column<double>(type: "float", nullable: true),
                    PmHigh100 = table.Column<bool>(type: "bit", nullable: false),
                    Open0930 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Close0945 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    GapAt0945 = table.Column<double>(type: "float", nullable: true),
                    ExtensionAt0945 = table.Column<double>(type: "float", nullable: true),
                    SignalFired = table.Column<bool>(type: "bit", nullable: false),
                    Shortable0940 = table.Column<bool>(type: "bit", nullable: true),
                    EasyToBorrow0940 = table.Column<bool>(type: "bit", nullable: true),
                    Marginable0940 = table.Column<bool>(type: "bit", nullable: true),
                    Status0940 = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Shortable = table.Column<bool>(type: "bit", nullable: true),
                    EasyToBorrow = table.Column<bool>(type: "bit", nullable: true),
                    Marginable = table.Column<bool>(type: "bit", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    BorrowStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    LocateQueried = table.Column<bool>(type: "bit", nullable: false),
                    LocateAvailable0940 = table.Column<bool>(type: "bit", nullable: true),
                    LocateSize0940 = table.Column<double>(type: "float", nullable: true),
                    LocateFee0940 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    LocateAvailable = table.Column<bool>(type: "bit", nullable: true),
                    LocateSize = table.Column<double>(type: "float", nullable: true),
                    LocateFee = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    LocateNote = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SsrD1LowVsD2Close = table.Column<double>(type: "float", nullable: true),
                    SsrActive = table.Column<bool>(type: "bit", nullable: true),
                    RegSho = table.Column<bool>(type: "bit", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FadeShortBorrowObservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FadeShortBorrowObservations_DateEt_Symbol",
                table: "FadeShortBorrowObservations",
                columns: new[] { "DateEt", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FadeShortBorrowObservations");
        }
    }
}
