using System.Text.Json;
using IdiotProof.Operations.Replay;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>dataset export (IP-T187): trades.csv / bars.csv / manifest.json straight from the feature store.</summary>
public class DatasetExportOperationTests
{
    [Test]
    public async Task WritesTheCsvsAndManifest_FromTheFeatureStore()
    {
        using var w = new ReplayWorld();
        await w.AddGapperAsync();
        var replay = await new ReplayRunOperation().ExecuteAsync(new ReplayRunArgs { Symbol = "GAPT", Date = ReplayWorld.Day, Pages = false }, w.Context());
        Assert.That(replay.IsOk, Is.True, replay.Message);
        var dir = Path.Combine(w.Temp, "dataset");

        var result = await new DatasetExportOperation().ExecuteAsync(new DatasetExportArgs { Out = dir }, w.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var trades = File.ReadAllLines(Path.Combine(dir, "trades.csv"));
        var bars = File.ReadAllLines(Path.Combine(dir, "bars.csv"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")));
        Assert.Multiple(() =>
        {
            Assert.That(trades[0], Does.StartWith("runId,symbol,dateEt,strategy"));
            Assert.That(trades.Length, Is.GreaterThan(1));
            Assert.That(bars.Length, Is.GreaterThan(100));
            Assert.That(manifest.RootElement.GetProperty("runs").GetInt32(), Is.EqualTo(1));
            Assert.That(manifest.RootElement.GetProperty("generatedUtc").GetString(), Does.StartWith("2026-07-18T16:00:00"));
            Assert.That(result.Human, Does.Contain("trades.csv").And.Contain(dir));
        });
    }
}
