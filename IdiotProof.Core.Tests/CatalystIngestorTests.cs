using System.Text.Json;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace IdiotProof.Blazor.Tests;

/// <summary>IP-T443: one intake item becomes one CatalystEvent row plus its symbol rows; the same (Source, ExternalId) never lands twice.</summary>
[TestFixture]
public sealed class CatalystIngestorTests
{
    private static readonly DateTime Published = new(2026, 11, 6, 13, 0, 0, DateTimeKind.Utc);

    private sealed class Kit : IAsyncDisposable
    {
        public ServiceProvider Services { get; }
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 6, 13, 0, 30, TimeSpan.Zero));

        public Kit()
        {
            Services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<TimeProvider>(Clock)
                .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"catalyst-{Guid.NewGuid():N}"))
                .AddSingleton<SettingsRepository>()
                .AddSingleton<CatalystIntakeSettings>()
                .AddSingleton<CatalystClassifier>()
                .AddScoped<CatalystIngestor>()
                .AddScoped<CatalystLookup>()
                .BuildServiceProvider();
        }

        public CatalystIngestor Ingestor => Services.CreateScope().ServiceProvider.GetRequiredService<CatalystIngestor>();

        public CatalystLookup Lookup => Services.CreateScope().ServiceProvider.GetRequiredService<CatalystLookup>();

        public IDbContextFactory<AppDbContext> Db => Services.GetRequiredService<IDbContextFactory<AppDbContext>>();

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }

    private static CatalystIntake News(string id, string headline, DateTime? published = null, params string[] symbols) =>
        new(CatalystIntake.AlpacaNews, id, published ?? Published, (published ?? Published).AddSeconds(30),
            symbols.Length == 0 ? ["ABCD"] : symbols, headline, "summary text", "https://example.test/" + id, "benzinga");

    [Test]
    public async Task Ingest_WritesTheRow_ItsSymbols_PreClass_AndScore()
    {
        await using var kit = new Kit();
        var r = await kit.Ingestor.IngestAsync(News("1", "Acme Receives FDA Approval for ZYX-1", null, "ABCD", "EFGH"));

        Assert.That(r.Outcome, Is.EqualTo(CatalystIngestOutcome.Inserted));
        await using var db = await kit.Db.CreateDbContextAsync();
        var row = await db.CatalystEvents.SingleAsync();
        var symbols = await db.CatalystEventSymbols.Where(s => s.EventId == row.Id).Select(s => s.Symbol).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(row.Id, Is.EqualTo(r.Id));
            Assert.That(row.PrimarySymbol, Is.EqualTo("ABCD"));
            Assert.That(JsonSerializer.Deserialize<string[]>(row.SymbolsJson), Is.EqualTo(new[] { "ABCD", "EFGH" }));
            Assert.That(symbols, Is.EquivalentTo(new[] { "ABCD", "EFGH" }));
            Assert.That(row.PreClass, Is.EqualTo("Fda"));
            Assert.That(row.PreDirection, Is.EqualTo(1));
            Assert.That(row.Direction, Is.EqualTo(1));
            Assert.That(row.SourceTier, Is.EqualTo(2));
            Assert.That(row.NoveltyCount, Is.EqualTo(0));
            Assert.That(row.CatalystScore, Is.GreaterThan(0).And.LessThanOrEqualTo(100));
            Assert.That(row.ReceivedUtc - row.PublishedUtc, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(row.ScoreComputedUtc, Is.EqualTo(row.ReceivedUtc));
            Assert.That(row.Backfilled, Is.False);
            Assert.That(row.LlmClassifiedUtc, Is.Null);
        });
    }

    [Test]
    public async Task SameSourceAndExternalId_IsADuplicate_EvenWithADifferentHeadline()
    {
        await using var kit = new Kit();
        var first = await kit.Ingestor.IngestAsync(News("42", "First headline"));
        var second = await kit.Ingestor.IngestAsync(News("42", "Updated headline"));

        Assert.That(first.Outcome, Is.EqualTo(CatalystIngestOutcome.Inserted));
        Assert.That(second.Outcome, Is.EqualTo(CatalystIngestOutcome.Duplicate));
        Assert.That(second.Id, Is.Null);
        await using var db = await kit.Db.CreateDbContextAsync();
        Assert.That(await db.CatalystEvents.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.CatalystEventSymbols.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task SameExternalId_FromAnotherSource_IsNotADuplicate()
    {
        await using var kit = new Kit();
        await kit.Ingestor.IngestAsync(News("7", "news"));
        var edgar = await kit.Ingestor.IngestAsync(News("7", "8-K: ACME") with { Source = CatalystIntake.Edgar, FormType = "8-K", SourceName = "edgar" });
        Assert.That(edgar.Outcome, Is.EqualTo(CatalystIngestOutcome.Inserted));
    }

    [Test]
    public async Task Novelty_CountsEarlierEventsOfTheSymbol_InsideTheWindow()
    {
        await using var kit = new Kit();
        var weights = CatalystCatalogs.ScoringWeights;
        await kit.Ingestor.IngestAsync(News("a", "old news", Published.AddDays(-weights.NoveltyWindowDays - 1)));
        await kit.Ingestor.IngestAsync(News("b", "recent news", Published.AddDays(-3)));
        await kit.Ingestor.IngestAsync(News("c", "other symbol", Published.AddDays(-1), "ZZZZ"));
        await kit.Ingestor.IngestAsync(News("d", "today"));

        await using var db = await kit.Db.CreateDbContextAsync();
        var today = await db.CatalystEvents.SingleAsync(e => e.ExternalId == "d");
        Assert.That(today.NoveltyCount, Is.EqualTo(1), "only 'b' is the same symbol inside the window and before it");
    }

    [Test]
    public async Task NoSymbols_IsRecordedAsNoSymbol_AndWritesNothing()
    {
        await using var kit = new Kit();
        var r = await kit.Ingestor.IngestAsync(News("x", "Market wrap") with { Symbols = [] });
        Assert.That(r.Outcome, Is.EqualTo(CatalystIngestOutcome.NoSymbol));
        await using var db = await kit.Db.CreateDbContextAsync();
        Assert.That(await db.CatalystEvents.AnyAsync(), Is.False);
    }

    [Test]
    public async Task LlmClassifySetting_QueuesTheNewRow()
    {
        await using var kit = new Kit();
        var classifier = kit.Services.GetRequiredService<CatalystClassifier>();
        await kit.Ingestor.IngestAsync(News("1", "before the switch"));
        Assert.That(classifier.Pending, Is.EqualTo(0));

        await kit.Services.GetRequiredService<SettingsRepository>().SetAsync(CatalystIntakeSettings.LlmClassifyKey, "true");
        kit.Services.GetRequiredService<CatalystIntakeSettings>().Invalidate();
        await kit.Ingestor.IngestAsync(News("2", "after the switch"));
        Assert.That(classifier.Pending, Is.EqualTo(1));
    }

    [Test]
    public async Task Lookup_IsPointInTime_AndSeesSecondarySymbols()
    {
        await using var kit = new Kit();
        await kit.Ingestor.IngestAsync(News("1", "early", Published.AddHours(-2), "ABCD"));
        await kit.Ingestor.IngestAsync(News("2", "late", Published, "QQQQ", "ABCD"));

        var atEarly = await kit.Lookup.LatestForSymbolAsync("abcd", Published.AddHours(-1));
        var atLate = await kit.Lookup.LatestForSymbolAsync("ABCD", Published);
        var recent = await kit.Lookup.RecentAsync(["ABCD"], Published.AddHours(-3));
        Assert.Multiple(() =>
        {
            Assert.That(atEarly?.Headline, Is.EqualTo("early"));
            Assert.That(atLate?.Headline, Is.EqualTo("late"), "the secondary symbol row links the late event to ABCD");
            Assert.That(recent.Select(e => e.Headline), Is.EqualTo(new[] { "late", "early" }));
        });
    }
}
