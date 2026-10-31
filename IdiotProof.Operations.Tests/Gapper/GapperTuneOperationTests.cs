using IdiotProof.Core.Trading;
using IdiotProof.Operations.Gapper;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper tune (IP-T164): profile + dial-ins through GapperScriptFactory; preview by default, --save stores it inactive.</summary>
public class GapperTuneOperationTests
{
    [Test]
    public async Task Preview_AppliesTheDials_AndWritesNothing()
    {
        using var kit = new GapperKit();
        var result = await new GapperTuneOperation().ExecuteAsync(new GapperTuneArgs
        {
            Symbol = "abcd", Profile = "penny-runner", Gap = 12, Stop = 6, Giveback = 18, SellBy = "09:25", Trail = 0,
        }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var script = result.Human!;
        Assert.That(script, Does.Contain("Ticker(\"ABCD\")").And.Contain("StopLoss").And.Contain("PeakGiveback(18").And.Contain("09:25"));
        Assert.That(script, Does.Not.Contain("TrailingStopLoss"), "--trail 0 turns the trailing stop off");
        Assert.That(await kit.StrategiesAsync(), Is.Empty);
    }

    [Test]
    public async Task Save_StoresAnInactivePaperGapper_WhoseCanonRoundTrips()
    {
        using var kit = new GapperKit();
        var result = await new GapperTuneOperation().ExecuteAsync(new GapperTuneArgs { Symbol = "NVDA", Save = true }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var row = (await kit.StrategiesAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Symbol, Is.EqualTo("NVDA"));
            Assert.That(row.IsActive, Is.False);
            Assert.That(row.BrokerMode, Is.EqualTo("Paper"));
            Assert.That(GapperRules.IsGapper(row), Is.True);
            Assert.That(StrategyLoader.Load(row.ScriptJson, row.ScriptText).Definition, Is.Not.Null);
            Assert.That(row.Title, Is.EqualTo("NVDA Gapper — Classic Gapper"));
        });
    }

    [Test]
    public void InvalidDials_AreAUsageError()
    {
        using var kit = new GapperKit();
        var ex = Assert.ThrowsAsync<OperationException>(() => new GapperTuneOperation().ExecuteAsync(
            new GapperTuneArgs { Symbol = "NVDA", ArmExit = "09:29", SellBy = "09:20" }, kit.Context()));
        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(ex.Message, Does.Contain("Arm-exit"));
    }

    [Test]
    public void LiveSave_IsRefused()
    {
        using var kit = new GapperKit();
        var ex = Assert.ThrowsAsync<OperationException>(() => new GapperTuneOperation().ExecuteAsync(
            new GapperTuneArgs { Symbol = "NVDA", Save = true, Mode = BrokerMode.Live }, kit.Context()));
        Assert.That(ex!.Message, Does.Contain("strategy mode"));
    }
}
