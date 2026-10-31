using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Gapper;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper auto enable / disable (IP-T170): paper-only, owner-scoped.</summary>
public class GapperAutoEnableOperationTests
{
    [Test]
    public async Task Enable_RefusesUnlessTheAutoGapperIsPaperOnly()
    {
        using var kit = new GapperKit();
        kit.Settings.AutoGapperBrokerMode = "live";

        var result = await new GapperAutoEnableOperation().ExecuteAsync(new GapperAutoEnableArgs(), kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Message, Does.Contain("paper"));
        Assert.That(await kit.StrategiesAsync(), Is.Empty);
    }

    [Test]
    public async Task Disable_DisarmsYourFlatAutoGappers_KeepsHoldingOnesAndManualOnes()
    {
        using var kit = new GapperKit();
        var flat = await kit.AddGapperAsync("AAA", active: true, author: GapperAutoRules.AuthorTag);
        var holding = await kit.AddGapperAsync("BBB", active: true, positionQty: 4, author: GapperAutoRules.AuthorTag);
        var manual = await kit.AddGapperAsync("CCC", active: true);

        var result = await new GapperAutoDisableOperation().ExecuteAsync(new NoArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var rows = (await kit.StrategiesAsync()).ToDictionary(s => s.Id);
        Assert.Multiple(() =>
        {
            Assert.That(rows[flat.Id].IsActive, Is.False);
            Assert.That(rows[holding.Id].IsActive, Is.True);
            Assert.That(rows[manual.Id].IsActive, Is.True);
            Assert.That(result.Human, Does.Contain("kept armed — holding 4 sh").And.Contain("1 of 2 disarmed"));
        });
    }
}
