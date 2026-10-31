using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy samples (IP-T158): every sample is valid and round-trips.</summary>
public class StrategySamplesOperationTests
{
    [TestCaseSource(nameof(Names))]
    public void EverySample_ValidatesWithoutErrors_AndItsCanonRoundTrips(string name)
    {
        var def = StrategySamples.Find(name)!.Build();
        var v = StrategyDrafts.Validate(def);

        Assert.That(v.Findings.Where(f => f.Level == FindingLevel.Error), Is.Empty, v.Render());
        Assert.That(v.CanonRoundTrips, Is.True);
        Assert.That(def.Quantity, Is.EqualTo(1));
    }

    [Test]
    public async Task Samples_ListsThemAll_AndShowsOne()
    {
        var kit = new StrategyKit();
        var op = new StrategySamplesOperation();

        var list = await op.ExecuteAsync(new StrategySamplesArgs(), kit.Context());
        Assert.That(list.Human, Does.Contain("vwap-reclaim").And.Contain($"{StrategySamples.All.Count} samples"));

        var json = await op.ExecuteAsync(new StrategySamplesArgs { Name = "VWAP-RECLAIM", Format = StrategyExportFormat.Json }, kit.Context());
        Assert.That(StrategyJson.Deserialize(json.Human!).Symbol, Is.EqualTo("SPY"), "the json format prints pure canon, ready to pipe into create");

        Assert.That(async () => await op.ExecuteAsync(new StrategySamplesArgs { Name = "nope" }, kit.Context()), Throws.TypeOf<OperationException>());
    }

    [Test]
    public async Task ASample_PipesIntoCreate()
    {
        var kit = new StrategyKit();
        var json = await new StrategySamplesOperation().ExecuteAsync(new StrategySamplesArgs { Name = "sune-wedge", Format = StrategyExportFormat.Json }, kit.Context());

        var created = await new StrategyCreateOperation().ExecuteAsync(new StrategyCreateArgs { Canon = json.Human }, kit.Context());

        Assert.That(created.IsOk, Is.True, created.Message);
    }

    private static IEnumerable<string> Names() => StrategySamples.All.Select(s => s.Name);
}
