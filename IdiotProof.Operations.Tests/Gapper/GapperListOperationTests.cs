using IdiotProof.Operations.Gapper;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper list (IP-T166): only gapper rows, armed first, with position and live condition progress.</summary>
public class GapperListOperationTests
{
    [Test]
    public async Task ListsGappersWithLiveState_AndSkipsOtherStrategies()
    {
        using var kit = new GapperKit();
        var armed = await kit.AddGapperAsync("NVDA", active: true);
        await kit.AddGapperAsync("AAA", active: true, positionQty: 7);
        await kit.AddGapperAsync("ZZZ");
        await kit.AddPlainAsync("MSFT", active: true);
        await kit.Services.GetRequiredService<ConditionProgressRepository>().UpsertAsync(armed.Id, 3, 5, "IsVolumeAbove(2)");

        var result = await new GapperListOperation().ExecuteAsync(new GapperListArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("NVDA").And.Contain("AAA").And.Contain("ZZZ").And.Not.Contain("MSFT"));
            Assert.That(result.Human, Does.Contain("HOLDING").And.Contain("3/5").And.Contain("IsVolumeAbove(2)").And.Contain("09:28"));
            Assert.That(result.Human, Does.Contain("3 gappers, 2/3 armed."));
        });

        var activeOnly = await new GapperListOperation().ExecuteAsync(new GapperListArgs { Active = true }, kit.Context());
        Assert.That(activeOnly.Human, Does.Not.Contain("ZZZ"));
    }
}
