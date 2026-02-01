// ============================================================================
// PercentTests - Tests for Percent helper class
// ============================================================================

using IdiotProof.Shared.Helpers;

namespace IdiotProof.Shared.UnitTests.Helpers;

/// <summary>
/// Tests for Percent helper class.
/// </summary>
[TestFixture]
public class PercentTests
{
    #region Predefined Constants

    [Test]
    public void One_Returns001()
    {
        Assert.That(Percent.One, Is.EqualTo(0.01).Within(0.0001));
    }

    [Test]
    public void Two_Returns002()
    {
        Assert.That(Percent.Two, Is.EqualTo(0.02).Within(0.0001));
    }

    [Test]
    public void Three_Returns003()
    {
        Assert.That(Percent.Three, Is.EqualTo(0.03).Within(0.0001));
    }

    [Test]
    public void Four_Returns004()
    {
        Assert.That(Percent.Four, Is.EqualTo(0.04).Within(0.0001));
    }

    [Test]
    public void Five_Returns005()
    {
        Assert.That(Percent.Five, Is.EqualTo(0.05).Within(0.0001));
    }

    [Test]
    public void Six_Returns006()
    {
        Assert.That(Percent.Six, Is.EqualTo(0.06).Within(0.0001));
    }

    [Test]
    public void Seven_Returns007()
    {
        Assert.That(Percent.Seven, Is.EqualTo(0.07).Within(0.0001));
    }

    [Test]
    public void Eight_Returns008()
    {
        Assert.That(Percent.Eight, Is.EqualTo(0.08).Within(0.0001));
    }

    [Test]
    public void Nine_Returns009()
    {
        Assert.That(Percent.Nine, Is.EqualTo(0.09).Within(0.0001));
    }

    [Test]
    public void Ten_Returns010()
    {
        Assert.That(Percent.Ten, Is.EqualTo(0.10).Within(0.0001));
    }

    [Test]
    public void Fifteen_Returns015()
    {
        Assert.That(Percent.Fifteen, Is.EqualTo(0.15).Within(0.0001));
    }

    [Test]
    public void Twenty_Returns020()
    {
        Assert.That(Percent.Twenty, Is.EqualTo(0.20).Within(0.0001));
    }

    [Test]
    public void TwentyFive_Returns025()
    {
        Assert.That(Percent.TwentyFive, Is.EqualTo(0.25).Within(0.0001));
    }

    [Test]
    public void Thirty_Returns030()
    {
        Assert.That(Percent.Thirty, Is.EqualTo(0.30).Within(0.0001));
    }

    [Test]
    public void Fifty_Returns050()
    {
        Assert.That(Percent.Fifty, Is.EqualTo(0.50).Within(0.0001));
    }

    #endregion

    #region Custom Method

    [TestCase(0, 0.00)]
    [TestCase(1, 0.01)]
    [TestCase(10, 0.10)]
    [TestCase(25, 0.25)]
    [TestCase(50, 0.50)]
    [TestCase(75, 0.75)]
    [TestCase(100, 1.00)]
    public void Custom_ValidValues_ReturnsDecimal(double input, double expected)
    {
        var result = Percent.Custom(input);

        Assert.That(result, Is.EqualTo(expected).Within(0.0001));
    }

    [TestCase(12.5, 0.125)]
    [TestCase(33.33, 0.3333)]
    [TestCase(66.67, 0.6667)]
    public void Custom_FractionalValues_ReturnsDecimal(double input, double expected)
    {
        var result = Percent.Custom(input);

        Assert.That(result, Is.EqualTo(expected).Within(0.0001));
    }

    [Test]
    public void Custom_NegativeValue_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Percent.Custom(-1));
    }

    [Test]
    public void Custom_ValueOver100_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Percent.Custom(101));
    }

    [Test]
    public void Custom_ExactlyZero_ReturnsZero()
    {
        var result = Percent.Custom(0);

        Assert.That(result, Is.EqualTo(0).Within(0.0001));
    }

    [Test]
    public void Custom_Exactly100_ReturnsOne()
    {
        var result = Percent.Custom(100);

        Assert.That(result, Is.EqualTo(1.0).Within(0.0001));
    }

    #endregion
}
