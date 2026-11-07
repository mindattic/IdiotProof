using System.Text.Json;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Evidence.Gappers;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>
/// IP-T450 amendment 02 on planted data: news at 07:00 ET followed by a steady climb that H18 rides
/// second-hand and H20 shows decaying with latency; a flat hour before the headline that H19 calls
/// "the move follows the news", and a ramp that starts 30 minutes early that H19 calls "price led the
/// headline". Also the trading-day rule for news and the registered 24-configuration grid.
/// </summary>
[TestFixture]
public class GapperNewsRideTests
{
    private const string Split = "train";
    private const int NewsMinute = 180;
    private static readonly DateOnly Start = new(2023, 3, 1);

    private static readonly Lazy<GapperStudyPaths> Committed = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, GapperStudyPaths.RelativeDir.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(Path.Combine(candidate, "amendment-02.json"))) return new GapperStudyPaths(candidate);
        }
        throw new FileNotFoundException("amendment-02.json was not found above the test binary.");
    });

    private static GapperStudyHost Host()
    {
        var a1 = Committed.Value.AmendmentFiles[0];
        var a2 = Committed.Value.AmendmentFiles[1];
        var host = new GapperStudyHost();
        File.Copy(a1, Path.Combine(host.StudyDir, Path.GetFileName(a1)));
        File.Copy(a2, Path.Combine(host.StudyDir, Path.GetFileName(a2)));
        Directory.CreateDirectory(host.Paths.DataDir);
        File.WriteAllText(GapperSpreadMultiplier.File(host.Paths), "{\"multiplier\": 1.5, \"n\": 500}");
        return host;
    }

    /// <summary>60 trading days x 4 candidate symbols, each with one realtime news item at 07:00 ET (even symbols bullish) and the given session.</summary>
    private static async Task SeedAsync(GapperStudyHost host, Func<int, double> close, bool gappers = false)
    {
        var store = host.Store;
        await using var db = await host.DbAsync();
        var day = Start;
        for (int d = 0; d < 60; d++)
        {
            for (int i = 0; i < 4; i++)
            {
                var sym = $"S{i}";
                db.GapperStudyUniverseDays.Add(new GapperStudyUniverseDay { Symbol = sym, DateEt = day, Split = Split, AdjPrevClose = 2m, Adv20Dollars = 400_000m, Adv20Shares = 200_000m, Sessions = 20, AdvDecile = 3, PriceTier = 1, IsCandidate = true, IsGapper = gappers, GapTier = gappers ? "Tier20" : null, DayVolume = 300_000m });
                var t = MinuteBarMath.ToUtc(day, new TimeSpan(8, 0, 0));
                db.GapperFormationSnapshots.Add(new GapperFormationSnapshotRow { Source = GapperSnapshotStage.Source, Symbol = sym, DateEt = day, Split = Split, DecisionLabel = "08:00", DecisionTimeEt = new TimeOnly(8, 0), DecisionUtc = t, IsCandidate = true, FeatureVersion = 1, PmCumVol = 100_000, ExtraJson = "{\"csSpreadPrior\": 0.004}" });
                db.GapperStudyLabels.Add(new GapperStudyLabel { Symbol = sym, DateEt = day, Split = Split, DecisionLabel = "08:00", DecisionTimeEt = new TimeOnly(8, 0), DecisionUtc = t, EntryRef = 2m, EntryUtc = t });
                var ev = new CatalystEvent
                {
                    Id = Guid.NewGuid(), Source = CatalystIntake.AlpacaNews, ExternalId = $"{sym}-{day:yyyyMMdd}", PublishedUtc = MinuteBarMath.ToUtc(day, new TimeSpan(7, 0, 0)),
                    ReceivedUtc = MinuteBarMath.ToUtc(day, new TimeSpan(7, 0, 0)), PrimarySymbol = sym, Headline = "news", StampKind = CatalystStampKind.Realtime,
                    PreClass = i % 2 == 0 ? "ContractAward" : "Other", PreDirection = i % 2 == 0 ? 1 : 0, Backfilled = true,
                };
                db.CatalystEvents.Add(ev);
                db.CatalystEventSymbols.Add(new CatalystEventSymbol { EventId = ev.Id, Symbol = sym });
                store.AppendDay(sym, day, Session(day, close, _ => 1000));
            }
            day = MarketCalendar.NextTradingDay(day);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Flat at 2.00 until the news, +0.4 cents a minute for two hours, flat into the bell, then falling.</summary>
    private static double ClimbAfterNews(int i) => i <= NewsMinute ? 2.00 : i <= 300 ? 2.00 + 0.004 * (i - NewsMinute) : i < 330 ? 2.48 : 2.48 - 0.002 * (i - 330);

    private static Task<TestStageResult> RunAsync(GapperStudyHost host, params string[] ids) =>
        host.Get<GapperTestStage>().RunAsync(new TestStageRequest(ids, Split, Smoke: false), CancellationToken.None);

    private static JsonElement Statistic(HypothesisRun run) => JsonDocument.Parse(File.ReadAllText(run.File!)).RootElement.GetProperty("parts")[0].GetProperty("statistic");

    [Test]
    public void NewsDay_IsTheFirstSessionAfterThePriorClose_AndMidSessionNewsHasNone()
    {
        DateTime Et(int y, int mo, int d, int h, int mi) => MinuteBarMath.ToUtc(new DateOnly(y, mo, d), new TimeSpan(h, mi, 0));
        Assert.Multiple(() =>
        {
            Assert.That(GapperTestStage.NewsDay(Et(2023, 3, 3, 17, 0)), Is.EqualTo(new DateOnly(2023, 3, 6)), "Friday evening news belongs to Monday");
            Assert.That(GapperTestStage.NewsDay(Et(2023, 3, 5, 20, 0)), Is.EqualTo(new DateOnly(2023, 3, 6)), "Sunday news belongs to Monday");
            Assert.That(GapperTestStage.NewsDay(Et(2023, 3, 6, 9, 20)), Is.EqualTo(new DateOnly(2023, 3, 6)));
            Assert.That(GapperTestStage.NewsDay(Et(2023, 3, 6, 9, 21)), Is.Null, "published during the session: no premarket trade");
            Assert.That(GapperTestStage.NewsDay(Et(2023, 3, 6, 16, 0)), Is.Null, "the close itself belongs to no window");
        });
    }

    [Test]
    public void RegisteredGrid_Has24DistinctConfigurations()
    {
        var a = GapperAmendment.LoadAll(Committed.Value).Single(x => x.Id == "A02");
        var configs = GapperTestStage.NewsRideGrid(a.Find("H18")!.Grid!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(configs, Has.Count.EqualTo(24));
            Assert.That(configs.Select(c => c.Label).Distinct().Count(), Is.EqualTo(24));
            Assert.That(configs.Select(c => c.LatencyMinutes).Distinct(), Is.EquivalentTo(new[] { 1, 5 }));
            Assert.That(a.DefinesSample, Is.False, "amendment 02 reuses amendment 01's population");
        });
    }

    [Test]
    public async Task H18_RidesTheNewsSecondHand_AndRespectsTheFilterAndConfirmation()
    {
        using var host = Host();
        await SeedAsync(host, ClimbAfterNews);
        var run = (await RunAsync(host, "H18")).Runs.Single();
        var stat = Statistic(run);
        var configs = stat.GetProperty("configs").EnumerateArray().ToDictionary(c => c.GetProperty("label").GetString()!, c => c);
        var part = run.Parts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.Inputs.AmendmentId, Is.EqualTo("A02"));
            Assert.That(run.Inputs.AmendsHypothesis, Is.Null, "a new question, not an amended frozen one");
            Assert.That(configs, Has.Count.EqualTo(24));
            Assert.That(configs["latency1m news=any confirm=none sellBy09:15"].GetProperty("trades").GetInt32(), Is.EqualTo(240));
            Assert.That(configs["latency1m news=bullish confirm=none sellBy09:15"].GetProperty("trades").GetInt32(), Is.EqualTo(120), "only the even, bullish symbols");
            Assert.That(configs["latency1m news=any confirm=up5 sellBy09:15"].GetProperty("trades").GetInt32(), Is.EqualTo(0), "price has not moved 5% one minute after the news");
            Assert.That(stat.GetProperty("newsDays").GetInt32(), Is.EqualTo(240));
            Assert.That(stat.GetProperty("newsDaysBullish").GetInt32(), Is.EqualTo(120));
            Assert.That(part.Effect, Is.GreaterThan(10), "the planted 24% climb survives the frozen cost floor");
            Assert.That(part.MeetsMinimum, Is.True);
            Assert.That(stat.GetProperty("selectedConfig").GetString(), Does.Contain("latency1m"), "the earlier entry is the better ride");
        });
    }

    [Test]
    public async Task H20_TheEdgeDecaysWithLatency()
    {
        using var host = Host();
        await SeedAsync(host, ClimbAfterNews);
        var run = (await RunAsync(host, "H20")).Runs.Single();
        var part = run.Parts.Single();
        var curve = Statistic(run).GetProperty("curve").EnumerateArray().Select(c => c.GetProperty("meanGrossPct").GetDouble()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(part.N, Is.EqualTo(120), "bullish events only");
            Assert.That(part.Effect, Is.EqualTo((2.48 / 2.00 - 2.48 / 2.056) * 100).Within(0.05), "entry at 07:01 (2.00) vs 07:15 (2.056), out at the 09:28 open (2.48)");
            Assert.That(curve, Is.Ordered.Descending);
            Assert.That(run.Verdict, Is.EqualTo(HypothesisVerdict.Supported));
        });
    }

    [Test]
    public async Task H19_AMoveThatFollowsTheHeadline_IsSupported()
    {
        using var host = Host();
        await SeedAsync(host, ClimbAfterNews, gappers: true);
        var run = (await RunAsync(host, "H19")).Runs.Single();
        var part = run.Parts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(part.N, Is.EqualTo(240));
            Assert.That(part.Effect, Is.EqualTo(0).Within(1e-9), "flat for the half hour before publication");
            Assert.That(run.Verdict, Is.EqualTo(HypothesisVerdict.Supported));
        });
    }

    [Test]
    public async Task H19_APriceThatRunsBeforeTheHeadline_IsRefuted()
    {
        using var host = Host();
        await SeedAsync(host, i => i <= 150 ? 2.00 : i <= 300 ? 2.00 + 0.004 * (i - 150) : 2.60, gappers: true);
        var run = (await RunAsync(host, "H19")).Runs.Single();
        var part = run.Parts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(part.Effect, Is.GreaterThan(0.4), "the ramp starts 30 minutes before the news, so about half the hour's move precedes it");
            Assert.That(run.Verdict, Is.EqualTo(HypothesisVerdict.Refuted), "price led the headline: someone traded ahead of it");
        });
    }
}
