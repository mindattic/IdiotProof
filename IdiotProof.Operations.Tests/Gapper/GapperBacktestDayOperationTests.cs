using IdiotProof.Operations.Gapper;
using IdiotProof.Operations.Replay;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>
/// gapper backtest-day (IP-T168): MockDataFeed's deterministic gap day (GAP* symbols always gap)
/// through GapperDayBacktester — fills, MFE/MAE, the giveback grid, hindsight — saved under an id.
/// </summary>
public class GapperBacktestDayOperationTests
{
    internal static GapperBacktestDayArgs Args() => new()
    {
        Symbol = "GAPT", Date = new DateOnly(2026, 7, 17), MaxPrice = 5000, Stop = 8, ArmExit = "09:12",
    };

    [Test]
    public async Task ReplaysTheDay_ReportsExcursionsAndTheGrid_AndSavesTheRun()
    {
        using var kit = new GapperKit();
        var result = await new GapperBacktestDayOperation().ExecuteAsync(Args(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("GAPT Fri 2026-07-17").And.Contain("Mock (synthetic"));
            Assert.That(result.Human, Does.Contain("entry").And.Contain("MFE").And.Contain("MAE").And.Contain("Giveback grid"));
            Assert.That(result.Human, Does.Match(@"\d{1,2}:\d{2} [AP]M"), "fill times are shown in ET");
            Assert.That(result.Human, Does.Contain("idiotproof gapper apply-tuned gb-"));
        });

        var id = (string)result.Data!.GetType().GetProperty("Id")!.GetValue(result.Data)!;
        var saved = await kit.Services.GetRequiredService<SavedRunStore>().LoadAsync<GapperBacktestRun>(id, "gb", kit.Owner);
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Payload.Report.GivebackGrid, Has.Count.EqualTo(8));
        Assert.That(await new SavedRunStore(kit.RunDir).LoadAsync<GapperBacktestRun>(id, "gb", Guid.NewGuid()), Is.Null, "another owner can't read it");
    }

    [Test]
    public async Task AFutureDay_IsAUsageError()
    {
        using var kit = new GapperKit();
        var result = await new GapperBacktestDayOperation().ExecuteAsync(Args() with { Date = new DateOnly(2027, 1, 4) }, kit.Context());
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
