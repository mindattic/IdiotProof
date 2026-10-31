using IdiotProof.Core.Data;
using IdiotProof.Engine.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T6: research claim similarity. <see cref="ClaimVectorService"/> (LSH signatures, Hamming distance,
/// LLM feature scoring through a scripted Legion handler, idempotent persist) and
/// <see cref="ClaimCorrelationService"/> (neighbour search and outcome aggregation) on an in-memory database.
/// </summary>
[TestFixture]
public sealed class ClaimVectorServiceTests
{
    private IDbContextFactory<AppDbContext> factory = null!;

    [SetUp]
    public void SetUp() =>
        factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"claims-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();

    private ClaimVectorService Vectors(string reply = "[]", string? hostKey = "sk-host") =>
        Vectors(new ScriptedClaude(reply), hostKey);

    private ClaimVectorService Vectors(ScriptedClaude llm, string? hostKey = "sk-host") =>
        new(new LegionClient(new HttpClient(llm), options: null), new AppSettings { ClaudeApiKey = hostKey ?? "" },
            factory, NullLogger<ClaimVectorService>.Instance);

    private ClaimCorrelationService Correlation() => new(factory, NullLogger<ClaimCorrelationService>.Instance);

    private static float[] Features(float seed) =>
        Enumerable.Range(0, 20).Select(i => (float)((Math.Sin(seed * 7 + i * 1.3) + 1) / 2)).ToArray();

    private static string Json(IEnumerable<double> values) => "[" + string.Join(",", values.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

    // ── GetSignature / HammingDistance ─────────────────────────────────────────

    [Test]
    public void GetSignature_IsDeterministicAcrossInstances()
    {
        var f = Features(1);
        var a = Vectors().GetSignature(f);
        var b = Vectors().GetSignature(f);
        Assert.Multiple(() =>
        {
            Assert.That(a, Has.Length.EqualTo(8));
            Assert.That(b, Is.EqualTo(a));
            Assert.That(ClaimVectorService.HammingDistance(a, b), Is.Zero);
        });
    }

    [Test]
    public void GetSignature_ShortFeatureArray_ReturnsEightZeroBytes() =>
        Assert.That(Vectors().GetSignature(new float[19]), Is.EqualTo(new byte[8]));

    [Test]
    public void GetSignature_SimilarVectorsAreCloserThanOpposedOnes()
    {
        var svc = Vectors();
        var f = Features(3);
        var near = f.Select(v => v + 0.01f).ToArray();
        var opposed = f.Select(v => -v).ToArray();

        var dNear = ClaimVectorService.HammingDistance(svc.GetSignature(f), svc.GetSignature(near));
        var dFar = ClaimVectorService.HammingDistance(svc.GetSignature(f), svc.GetSignature(opposed));
        Assert.Multiple(() =>
        {
            Assert.That(dNear, Is.LessThanOrEqualTo(4));
            Assert.That(dFar, Is.GreaterThan(50), "a negated vector flips (nearly) every hyperplane bit");
        });
    }

    [Test]
    public void HammingDistance_Edges()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClaimVectorService.HammingDistance(null!, new byte[8]), Is.EqualTo(int.MaxValue));
            Assert.That(ClaimVectorService.HammingDistance(new byte[8], null!), Is.EqualTo(int.MaxValue));
            Assert.That(ClaimVectorService.HammingDistance(new byte[8], new byte[7]), Is.EqualTo(int.MaxValue));
            Assert.That(ClaimVectorService.HammingDistance([1, 2, 3], [1, 2, 3]), Is.Zero);
            Assert.That(ClaimVectorService.HammingDistance([0xFF, 0x00], [0x00, 0x00]), Is.EqualTo(8));
            Assert.That(ClaimVectorService.HammingDistance([0b0101], [0b0110]), Is.EqualTo(2));
        });
    }

    // ── ScoreAsync ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Score_ParsesAnArrayWrappedInProse_AndClampsTo01()
    {
        var values = Enumerable.Range(0, 20).Select(i => i / 20.0).ToArray();
        values[0] = -0.5;
        values[1] = 1.7;
        var svc = Vectors($"Sure, here are the scores: {Json(values)} hope that helps");

        var f = await svc.ScoreAsync("AAPL", "claim", "News", "Bullish", "High", isPortent: true);

        Assert.That(f, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(f!, Has.Length.EqualTo(20));
            Assert.That(f[0], Is.EqualTo(0f));
            Assert.That(f[1], Is.EqualTo(1f));
            Assert.That(f[10], Is.EqualTo(0.5f).Within(1e-6));
        });
    }

    [Test]
    public async Task Score_ExtraValuesAreIgnored()
    {
        var f = await Vectors(Json(Enumerable.Repeat(0.3, 25))).ScoreAsync("A", "c", "News", "Neutral", "Low", false);
        Assert.That(f, Has.Length.EqualTo(20));
    }

    [TestCase("[0.1, 0.2, 0.3]")]
    [TestCase("no array here")]
    [TestCase("] backwards [")]
    [TestCase("[\"a\",\"b\"]")]
    public async Task Score_FewerThan20OrMalformed_ReturnsNull(string reply)
        => Assert.That(await Vectors(reply).ScoreAsync("A", "c", "News", "Neutral", "Low", false), Is.Null);

    [Test]
    public async Task Score_Exactly19Floats_ReturnsNull() =>
        Assert.That(await Vectors(Json(Enumerable.Repeat(0.5, 19))).ScoreAsync("A", "c", "News", "Neutral", "Low", false), Is.Null);

    [Test]
    public async Task Score_NoKey_ReturnsNull_WithoutCallingTheLlm()
    {
        using var noVault = new EmptyLlmVault();
        var llm = new ScriptedClaude(Json(Enumerable.Repeat(0.5, 20)));
        var svc = Vectors(llm, hostKey: null);
        Assert.Multiple(async () =>
        {
            Assert.That(await svc.ScoreAsync("A", "c", "News", "Neutral", "Low", false), Is.Null);
            Assert.That(llm.Keys, Is.Empty);
        });
    }

    // ── ComputeAndSaveAsync ─────────────────────────────────────────────────────

    [Test]
    public async Task ComputeAndSave_PersistsVectorAndSignature_AndDoesNotDuplicate()
    {
        var claimId = Guid.NewGuid();
        var svc = Vectors(Json(Enumerable.Repeat(0.5, 20)));

        await svc.ComputeAndSaveAsync(claimId, "A", "c", "News", "Neutral", "Low", false);
        await svc.ComputeAndSaveAsync(claimId, "A", "c", "News", "Neutral", "Low", false);

        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.ResearchClaimVectors.Where(v => v.ClaimId == claimId).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].LshSignature, Is.EqualTo(svc.GetSignature(Enumerable.Repeat(0.5f, 20).ToArray())));
            Assert.That(rows[0].VectorJson, Does.StartWith("[0.5,"));
        });
    }

    [Test]
    public async Task ComputeAndSave_UnscoredClaim_WritesNothing()
    {
        await Vectors("garbage").ComputeAndSaveAsync(Guid.NewGuid(), "A", "c", "News", "Neutral", "Low", false);
        await using var db = await factory.CreateDbContextAsync();
        Assert.That(await db.ResearchClaimVectors.CountAsync(), Is.Zero);
    }

    // ── ClaimCorrelationService ─────────────────────────────────────────────────

    /// <summary>A signature whose first <paramref name="bits"/> bits are set: distance from zero = bits.</summary>
    private static byte[] Sig(int bits)
    {
        var s = new byte[8];
        for (var i = 0; i < bits; i++) s[i / 8] |= (byte)(1 << (i % 8));
        return s;
    }

    private async Task<Guid> AddClaimAsync(int bits, decimal? outcome, string summary, bool withVector = true)
    {
        var claim = new ResearchClaim { Ticker = "T", ClaimSummary = summary, OutcomePctChange = outcome };
        await using var db = await factory.CreateDbContextAsync();
        db.ResearchClaims.Add(claim);
        if (withVector) db.ResearchClaimVectors.Add(new ResearchClaimVector { ClaimId = claim.Id, LshSignature = Sig(bits) });
        await db.SaveChangesAsync();
        return claim.Id;
    }

    [Test]
    public async Task FindSimilar_MissingTargetVector_ReturnsEmpty()
    {
        await AddClaimAsync(1, 5m, "other");
        var target = await AddClaimAsync(0, null, "target", withVector: false);
        Assert.That(await Correlation().FindSimilarOutcomesAsync(target), Is.SameAs(ClaimCorrelationService.Empty));
    }

    [Test]
    public async Task FindSimilar_AppliesCutoff_ExcludesSelf_AggregatesOutcomes_NearestFirstExamplesCappedAt3()
    {
        var target = await AddClaimAsync(0, 99m, "target");
        await AddClaimAsync(5, 10m, "d5 up");
        await AddClaimAsync(1, -4m, "d1 down");
        await AddClaimAsync(3, null, "d3 no outcome");
        await AddClaimAsync(2, 6m, "d2 up");
        await AddClaimAsync(30, 50m, "d30 too far");

        var r = await Correlation().FindSimilarOutcomesAsync(target, maxHamming: 10);

        Assert.Multiple(() =>
        {
            Assert.That(r.SimilarCount, Is.EqualTo(4));
            Assert.That(r.WithOutcomes, Is.EqualTo(3));
            Assert.That(r.BullishCount, Is.EqualTo(2));
            Assert.That(r.BearishCount, Is.EqualTo(1));
            Assert.That(r.AvgPctChange, Is.EqualTo(4.0).Within(1e-9));
            Assert.That(r.BestHammingDistance, Is.EqualTo(1));
            Assert.That(r.ExampleSummaries, Is.EqualTo(new[] { "d1 down", "d2 up", "d3 no outcome" }));
        });
    }

    [Test]
    public async Task FindSimilar_NothingWithinCutoff_ReturnsEmpty()
    {
        var target = await AddClaimAsync(0, null, "target");
        await AddClaimAsync(40, 1m, "far");
        Assert.That(await Correlation().FindSimilarOutcomesAsync(target, maxHamming: 20), Is.SameAs(ClaimCorrelationService.Empty));
    }

    [Test]
    public async Task FindSimilar_NoOutcomes_AverageIsNull()
    {
        var target = await AddClaimAsync(0, null, "target");
        await AddClaimAsync(1, null, "n1");
        var r = await Correlation().FindSimilarOutcomesAsync(target);
        Assert.Multiple(() =>
        {
            Assert.That(r.SimilarCount, Is.EqualTo(1));
            Assert.That(r.AvgPctChange, Is.Null);
            Assert.That(r.WithOutcomes, Is.Zero);
        });
    }

    [Test]
    public async Task FindSimilarBatch_ExcludesOnlySelf_AndSkipsTargetsWithoutVectors()
    {
        var a = await AddClaimAsync(0, 2m, "a");
        var b = await AddClaimAsync(1, -2m, "b");
        await AddClaimAsync(2, 8m, "c");
        await AddClaimAsync(40, 100m, "far");
        var noVector = await AddClaimAsync(0, null, "nv", withVector: false);

        var r = await Correlation().FindSimilarOutcomesBatchAsync([a, b, noVector], maxHamming: 5);

        Assert.Multiple(() =>
        {
            Assert.That(r.Keys, Is.EquivalentTo(new[] { a, b }));
            Assert.That(r[a].SimilarCount, Is.EqualTo(2), "b is a valid neighbour of a even though both are targets");
            Assert.That(r[a].ExampleSummaries, Is.EqualTo(new[] { "b", "c" }));
            Assert.That(r[a].BullishCount, Is.EqualTo(1));
            Assert.That(r[a].BearishCount, Is.EqualTo(1));
            Assert.That(r[a].AvgPctChange, Is.EqualTo(3.0).Within(1e-9));
            Assert.That(r[a].BestHammingDistance, Is.EqualTo(1));
            Assert.That(r[b].ExampleSummaries, Is.EqualTo(new[] { "a", "c" }));
            Assert.That(r[b].AvgPctChange, Is.EqualTo(5.0).Within(1e-9));
        });
    }

    [Test]
    public async Task FindSimilarBatch_EmptyInputOrNoVectors_ReturnsEmpty()
    {
        Assert.That(await Correlation().FindSimilarOutcomesBatchAsync([]), Is.Empty);
        Assert.That(await Correlation().FindSimilarOutcomesBatchAsync([Guid.NewGuid()]), Is.Empty);
    }
}
