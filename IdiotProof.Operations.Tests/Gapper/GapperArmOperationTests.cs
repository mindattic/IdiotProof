using IdiotProof.Core.Trading;
using IdiotProof.Operations.Gapper;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper arm / disarm (IP-T165): one active strategy per symbol, at most three active gappers, Live needs a code.</summary>
public class GapperArmOperationTests
{
    [Test]
    public async Task ArmingANewSymbol_QueuesAPaperGapperAndActivatesIt()
    {
        using var kit = new GapperKit();
        var result = await new GapperArmOperation().ExecuteAsync(new GapperArmArgs { Gapper = "abcd", Profile = "penny-runner" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var row = (await kit.StrategiesAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.IsActive, Is.True);
            Assert.That(row.BrokerMode, Is.EqualTo("Paper"));
            Assert.That(row.Title, Is.EqualTo("ABCD Gapper — Penny Runner"));
            Assert.That(result.Human, Does.Contain("1/3 gappers active"));
        });
    }

    [Test]
    public async Task ArmingAnExistingGapper_ActivatesThatRow()
    {
        using var kit = new GapperKit();
        var g = await kit.AddGapperAsync("NVDA");
        var result = await new GapperArmOperation().ExecuteAsync(new GapperArmArgs { Gapper = "NVDA" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That((await kit.StrategiesAsync()).Single(s => s.Id == g.Id).IsActive, Is.True);
    }

    [Test]
    public async Task TheFourthGapper_IsBlockedByTheCap_AndNothingIsWritten()
    {
        using var kit = new GapperKit();
        await kit.AddGapperAsync("AAA", active: true);
        await kit.AddGapperAsync("BBB", active: true);
        await kit.AddGapperAsync("CCC", active: true);

        var result = await new GapperArmOperation().ExecuteAsync(new GapperArmArgs { Gapper = "DDD" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Message, Does.Contain("3 gappers are already active"));
        Assert.That(await kit.StrategiesAsync(), Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ASymbolWithAnActiveStrategy_IsBlocked_OnePerSymbol()
    {
        using var kit = new GapperKit();
        await kit.AddPlainAsync("NVDA", active: true);
        var g = await kit.AddGapperAsync("NVDA");

        var result = await new GapperArmOperation().ExecuteAsync(new GapperArmArgs { Gapper = g.Id.ToString() }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Message, Does.Contain("one per symbol"));
        Assert.That((await kit.StrategiesAsync()).Single(s => s.Id == g.Id).IsActive, Is.False);
    }

    [Test]
    public async Task ALiveGapper_NeedsAConfirmationCode_ThenArms()
    {
        using var kit = new GapperKit();
        var g = await kit.AddGapperAsync("NVDA", mode: BrokerMode.Live);

        var first = await new GapperArmOperation().ExecuteAsync(new GapperArmArgs { Gapper = "NVDA" }, kit.Context());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired));
        Assert.That((await kit.StrategiesAsync()).Single().IsActive, Is.False);

        var second = await new GapperArmOperation().ExecuteAsync(new GapperArmArgs { Gapper = "NVDA" }, kit.Context(first.Confirmation!.Code));
        Assert.That(second.IsOk, Is.True, second.Message);
        Assert.That((await kit.StrategiesAsync()).Single(s => s.Id == g.Id).IsActive, Is.True);
    }

    [Test]
    public async Task ANonGapperReference_IsAUsageError()
    {
        using var kit = new GapperKit();
        var plain = await kit.AddPlainAsync("NVDA", active: false);
        var ex = Assert.ThrowsAsync<OperationException>(() => new GapperArmOperation().ExecuteAsync(new GapperArmArgs { Gapper = plain.Id.ToString() }, kit.Context()));
        Assert.That(ex!.Message, Does.Contain("not a gapper"));
    }

    [Test]
    public async Task Disarm_DeactivatesAndRemoves_ButNeverWhileHolding()
    {
        using var kit = new GapperKit();
        var flat = await kit.AddGapperAsync("AAA", active: true);
        var holding = await kit.AddGapperAsync("BBB", active: true, positionQty: 5);

        var off = await new GapperDisarmOperation().ExecuteAsync(new GapperDisarmArgs { Gapper = "AAA", Remove = true }, kit.Context());
        var held = await new GapperDisarmOperation().ExecuteAsync(new GapperDisarmArgs { Gapper = "BBB" }, kit.Context());

        Assert.That(off.IsOk, Is.True, off.Message);
        Assert.That(held.Status, Is.EqualTo(OperationStatus.Blocked));
        var rows = await kit.StrategiesAsync();
        Assert.That(rows.Any(s => s.Id == flat.Id), Is.False, "removed");
        Assert.That(rows.Single(s => s.Id == holding.Id).IsActive, Is.True);
    }
}
