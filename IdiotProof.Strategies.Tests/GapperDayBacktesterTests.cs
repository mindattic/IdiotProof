using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// The design→replay→examine→re-dial loop (IP-US-K12): replay a gapper
/// profile over a past day, get honest fills from the SAME exit brain the
/// live console runs, hindsight analysis, and a tuned profile for reuse.
/// Driven by MockDataFeed's deterministic gap day so results are exact.
/// </summary>
public class GapperDayBacktesterTests
{
    private static readonly DateOnly Day = new(2026, 7, 17); // Friday; GAP* symbols always gap
    private static readonly TimeZoneInfo Eastern = MarketTime.Eastern;

    private static DateTime EtToUtc(int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(Day.ToDateTime(new TimeOnly(hour, minute)), DateTimeKind.Unspecified), Eastern);

    private static GapperProfile Profile() => new()
    {
        Id = "classic-gapper", Name = "Classic",
        MinGapPercent = 5, MinVolumeRatio = 2, MinPrice = 1, MaxPrice = 5000,
        EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
        StopLossPercent = 8, PeakGivebackPercent = 25,
        ArmExitAtEt = "09:12", SellByEt = "09:28", DefaultNotional = 1000m,
    };

    private static async Task<List<Candle>> DayBarsAsync(string symbol)
    {
        var feed = new MockDataFeed();
        var list = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, EtToUtc(4, 0), EtToUtc(16, 0), TimeSpan.FromMinutes(1)))
            list.Add(c);
        return list;
    }

    private static async Task<decimal?> PreviousCloseAsync(string symbol) =>
        await ((IMarketDataFeed)new MockDataFeed()).GetPreviousCloseAsync(symbol, EtToUtc(9, 30));

    [Test]
    public async Task Replay_MockGapDay_EntersHoldsAndExitsBeforeTheBell()
    {
        var report = GapperDayBacktester.Run("GAPT", Profile(), await DayBarsAsync("GAPT"), await PreviousCloseAsync("GAPT"), Day);

        Assert.That(report.Entered, Is.True, report.NoEntryReason);
        Assert.Multiple(() =>
        {
            Assert.That(report.Entry, Is.Not.Null);
            Assert.That(report.Exit, Is.Not.Null);
            Assert.That(report.GapAtEntryPercent, Is.GreaterThanOrEqualTo(5), "entered on a real gap");
            Assert.That(MarketTime.ToEasternTimeOfDay(report.Entry!.Utc),
                Is.InRange(new TimeSpan(4, 0, 0), new TimeSpan(9, 0, 0)), "entry inside the premarket window");
            Assert.That(MarketTime.ToEasternTimeOfDay(report.Exit!.Utc),
                Is.LessThan(new TimeSpan(9, 30, 0)), "flat BEFORE the bell — same rule as live");
            Assert.That(report.PeakPrice, Is.GreaterThanOrEqualTo(report.Entry.Price));
            Assert.That(report.Quantity, Is.GreaterThan(0));
            Assert.That(report.Suggestions, Is.Not.Empty);
        });
    }

    [Test]
    public async Task Replay_GivebackGrid_CoversTheDial_AndBestIsAtLeastActual()
    {
        var report = GapperDayBacktester.Run("GAPT", Profile(), await DayBarsAsync("GAPT"), await PreviousCloseAsync("GAPT"), Day);

        Assert.That(report.GivebackGrid, Has.Count.EqualTo(8));
        var best = report.GivebackGrid.MaxBy(g => g.PnL)!;
        Assert.Multiple(() =>
        {
            // The actual setting (25) is in the grid, so the best grid row can
            // never be worse than what actually happened.
            Assert.That(report.GivebackGrid.Select(g => g.GivebackPercent), Does.Contain(25.0));
            Assert.That(best.PnL, Is.GreaterThanOrEqualTo(report.PnL - 0.01));
        });
    }

    [Test]
    public async Task Replay_TunedProfile_IsValid_AndCarriesTheHindsightDials()
    {
        var report = GapperDayBacktester.Run("GAPT", Profile(), await DayBarsAsync("GAPT"), await PreviousCloseAsync("GAPT"), Day);
        var tuned = report.TunedProfile;

        Assert.That(tuned, Is.Not.Null);
        var best = report.GivebackGrid.MaxBy(g => g.PnL)!;
        Assert.Multiple(() =>
        {
            Assert.That(tuned!.Validate(), Is.Empty, "the tuned profile must be immediately queueable");
            Assert.That(tuned.PeakGivebackPercent, Is.EqualTo(best.GivebackPercent), "tuned giveback = hindsight best");
            Assert.That(tuned.StopLossPercent, Is.LessThanOrEqualTo(Profile().StopLossPercent), "tuning never loosens the stop");
            Assert.That(tuned.Name, Does.Contain("tuned"));
        });
    }

    [Test]
    public async Task Replay_TuningAnAlreadyTunedProfile_DoesNotStackTheSuffix()
    {
        // Regression: backtest → Apply tuned → backtest again used to yield
        // "Classic (tuned) (tuned)".
        var p = Profile();
        p.Name = "Classic (tuned)";
        var report = GapperDayBacktester.Run("GAPT", p, await DayBarsAsync("GAPT"), await PreviousCloseAsync("GAPT"), Day);

        Assert.That(report.TunedProfile, Is.Not.Null);
        Assert.That(report.TunedProfile!.Name, Is.EqualTo("Classic (tuned)"));
    }

    [Test]
    public async Task Replay_ImpossibleGapScreen_ReportsNoEntryWithTheBlocker()
    {
        var p = Profile();
        p.MinGapPercent = 90; // nothing gaps 90%
        var report = GapperDayBacktester.Run("GAPT", p, await DayBarsAsync("GAPT"), await PreviousCloseAsync("GAPT"), Day);

        Assert.Multiple(() =>
        {
            Assert.That(report.Entered, Is.False);
            Assert.That(report.NoEntryReason, Does.Contain("Last blocker"));
        });
    }

    [Test]
    public async Task Replay_NoPreviousClose_FailsClosedLikeLive()
    {
        var report = GapperDayBacktester.Run("GAPT", Profile(), await DayBarsAsync("GAPT"), previousClose: null, Day);
        Assert.Multiple(() =>
        {
            Assert.That(report.Entered, Is.False);
            Assert.That(report.NoEntryReason, Does.Contain("previous close"));
        });
    }

    [Test]
    public void Replay_NoBars_ReportsCleanly()
    {
        var report = GapperDayBacktester.Run("GAPT", Profile(), [], 10m, Day);
        Assert.That(report.Entered, Is.False);
        Assert.That(report.NoEntryReason, Does.Contain("No bars"));
    }

    [Test]
    public void Replay_PeakAfterExit_IsVisibleToHindsight()
    {
        // Regression: the peak/MFE window used to stop at the EXIT bar, so the
        // "peak came AFTER your exit" suggestion was unreachable dead code and
        // the day's real max-favorable move was understated. The peak must run
        // to the hard sell-by; the trough (MAE) stays entry→exit.
        static Candle Bar(int h, int m, double o, double hi, double lo, double c) => new()
        {
            Symbol = "HAND",
            StartUtc = EtToUtc(h, m),
            EndUtc = EtToUtc(h, m).AddMinutes(1),
            Open = (decimal)o, High = (decimal)hi, Low = (decimal)lo, Close = (decimal)c,
            Volume = 100_000m,
        };

        // Gap day vs previous close 10: enter 11, run to 12, roll over hard
        // enough to trip the 25% giveback at 10.90 — then rip to 13 before the
        // sell-by. Old code reported peak 12; the honest peak is 13, after exit.
        List<Candle> bars =
        [
            Bar(4, 0, 11.0, 11.2, 10.9, 11.0),   // entry bar — all conditions pass
            Bar(4, 1, 11.0, 11.5, 11.0, 11.4),
            Bar(4, 2, 11.4, 12.0, 11.3, 11.9),   // pre-exit peak 12
            Bar(4, 3, 11.9, 11.9, 10.85, 10.9),  // giveback floor 11.75 → exit
            Bar(4, 4, 10.9, 12.5, 10.9, 12.4),
            Bar(4, 5, 12.4, 13.0, 12.3, 12.8),   // post-exit peak 13, before sell-by
            Bar(9, 29, 12.8, 12.8, 12.7, 12.75), // past the 09:28 sell-by — excluded from the peak
        ];

        var p = Profile();
        p.MinGapPercent = 1;
        p.MinVolumeRatio = 0.5;
        p.PeakGivebackPercent = 25;
        p.ArmExitAtEt = "04:00"; // armed immediately

        var report = GapperDayBacktester.Run("HAND", p, bars, previousClose: 10m, Day);

        Assert.That(report.Entered, Is.True, report.NoEntryReason);
        Assert.Multiple(() =>
        {
            Assert.That(report.Exit!.Reason, Is.EqualTo(nameof(GapperExitReason.PeakGiveback)));
            Assert.That(report.PeakPrice, Is.EqualTo(13.0), "peak runs to the sell-by, not the exit");
            Assert.That(report.PeakUtc, Is.GreaterThan(report.Exit.Utc), "the peak is after the exit");
            Assert.That(report.Suggestions, Has.Some.Contains("AFTER your exit"));
            Assert.That(report.TroughPrice, Is.EqualTo(10.85), "MAE still bounded at the exit");
        });
    }
}
