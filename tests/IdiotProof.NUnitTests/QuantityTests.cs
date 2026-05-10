using IdiotProof.Scripting;

namespace IdiotProof.NUnitTests;

/// <summary>
/// Validates the share/notional overloads on StrategyBuilder.Quantity. The DSL
/// must guarantee mutual exclusion: setting one clears the other so the order
/// layer never sees both populated. The broker layer is defensive but the
/// invariant lives at the DSL boundary.
/// </summary>
[TestFixture]
public class QuantityTests
{
    [Test]
    public void Quantity_Int_SetsSharesAndClearsNotional()
    {
        var def = Stock.Ticker("TEST")
            .Quantity(1500m)        // start with notional
            .Quantity(100)          // switch to shares
            .Long()
            .Build();

        Assert.That(def.Quantity, Is.EqualTo(100));
        Assert.That(def.NotionalAmount, Is.Null);
        Assert.That(def.IsNotional, Is.False);
    }

    [Test]
    public void Quantity_Decimal_SetsNotionalAndClearsShares()
    {
        var def = Stock.Ticker("TEST")
            .Quantity(50)           // start with shares
            .Quantity(2500m)        // switch to notional
            .Long()
            .Build();

        Assert.That(def.Quantity, Is.EqualTo(0));
        Assert.That(def.NotionalAmount, Is.EqualTo(2500m));
        Assert.That(def.IsNotional, Is.True);
    }

    [Test]
    public void QuantityShares_AliasMatchesIntOverload()
    {
        var def = Stock.Ticker("TEST").QuantityShares(250).Long().Build();
        Assert.That(def.Quantity, Is.EqualTo(250));
        Assert.That(def.NotionalAmount, Is.Null);
    }

    [Test]
    public void QuantityNotional_AliasMatchesDecimalOverload()
    {
        var def = Stock.Ticker("TEST").QuantityNotional(750m).Long().Build();
        Assert.That(def.Quantity, Is.EqualTo(0));
        Assert.That(def.NotionalAmount, Is.EqualTo(750m));
        Assert.That(def.IsNotional, Is.True);
    }

    [Test]
    public void NoQuantity_DefaultsToZeroSharesNoNotional()
    {
        var def = Stock.Ticker("TEST").Long().Build();
        Assert.That(def.Quantity, Is.EqualTo(0));
        Assert.That(def.NotionalAmount, Is.Null);
        Assert.That(def.IsNotional, Is.False);
    }
}
