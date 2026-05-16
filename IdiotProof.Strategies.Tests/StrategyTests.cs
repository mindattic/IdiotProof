using IdiotProof.Models;
using IdiotProof.Strategies;

namespace IdiotProof.Strategies.Tests;

// Named-strategy tests (ITI / LowHigh / PremarketBreakout / MomentumDecay) were
// removed when the matching strategy classes were deleted as part of the UI
// reset. The registry is now empty by default; everything runs as user-authored
// IdiotScript via DslStrategy. New tests should target DslStrategy + the parser.
public class StrategyTests
{
    [Test]
    public void StrategyRegistry_DefaultsToEmpty()
    {
        var registry = new StrategyRegistry();
        Assert.That(registry.GetAll(), Is.Empty);
    }

    [Test]
    public void StrategyRegistry_Get_UnknownName_ReturnsNull()
    {
        var registry = new StrategyRegistry();
        Assert.That(registry.Get("DoesNotExist"), Is.Null);
    }
}
