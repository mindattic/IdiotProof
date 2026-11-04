namespace IdiotProof.Evidence.Tests;

public class QuantileBinnerTieTests
{
    [Test]
    public void QuantileBinner_CutInsideARunOfTies_StillSeparatesTheTwoValues()
    {
        // 300 of one value and 299 of another: the median lands on the lower value's run.
        var values = Enumerable.Repeat(2000.0, 300).Concat(Enumerable.Repeat(5000.0, 299));
        var binner = new QuantileBinner(values, 2);
        Assert.That(binner.Bin(2000), Is.Not.EqualTo(binner.Bin(5000)));
    }

    [Test]
    public void QuantileBinner_AllValuesEqual_IsOneBucket()
    {
        var binner = new QuantileBinner(Enumerable.Repeat(1.0, 50), 3);
        Assert.That(binner.Bins, Is.EqualTo(1));
    }
}
