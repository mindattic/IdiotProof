// ============================================================================
// LowerHighsConditionTests - Tests for LowerHighsCondition
// ============================================================================

using IdiotProof.Backend.Strategy;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for LowerHighsCondition - descending resistance pattern detection.
/// </summary>
[TestFixture]
public class LowerHighsConditionTests
{
    #region Basic Evaluation Tests

    [Test]
    public void LowerHighsCondition_DescendingHighs_ReturnsTrue()
    {
        // Arrange - Each high is lower than the previous (bearish pattern)
        var condition = new LowerHighsCondition(3);
        // Array is [most recent, ..., oldest] - so 150, 155, 160 means 
        // most recent high is 150, previous was 155, oldest was 160
        condition.GetRecentHighs = () => new double[] { 150, 155, 160 };

        // Act
        var result = condition.Evaluate(currentPrice: 148, vwap: 155);

        // Assert - Pattern should be detected (150 < 155 < 160)
        Assert.That(result, Is.True);
    }

    [Test]
    public void LowerHighsCondition_AscendingHighs_ReturnsFalse()
    {
        // Arrange - Each high is higher than the previous (bullish)
        var condition = new LowerHighsCondition(3);
        condition.GetRecentHighs = () => new double[] { 160, 155, 150 };

        // Act
        var result = condition.Evaluate(currentPrice: 158, vwap: 155);

        // Assert - Pattern should NOT be detected
        Assert.That(result, Is.False);
    }

    [Test]
    public void LowerHighsCondition_EqualHighs_ReturnsFalse()
    {
        // Arrange - Highs are equal (no clear pattern)
        var condition = new LowerHighsCondition(3);
        condition.GetRecentHighs = () => new double[] { 155, 155, 155 };

        // Act
        var result = condition.Evaluate(currentPrice: 153, vwap: 155);

        // Assert - Equal values should return false
        Assert.That(result, Is.False);
    }

    [Test]
    public void LowerHighsCondition_WithoutCallback_ReturnsFalse()
    {
        // Arrange - No callback set
        var condition = new LowerHighsCondition(3);

        // Act
        var result = condition.Evaluate(currentPrice: 148, vwap: 155);

        // Assert - Should return false when callback is null
        Assert.That(result, Is.False);
    }

    [Test]
    public void LowerHighsCondition_InsufficientData_ReturnsFalse()
    {
        // Arrange - Not enough data points
        var condition = new LowerHighsCondition(3);
        condition.GetRecentHighs = () => new double[] { 150 }; // Only 1 value

        // Act
        var result = condition.Evaluate(currentPrice: 148, vwap: 155);

        // Assert - Should return false with insufficient data
        Assert.That(result, Is.False);
    }

    [Test]
    public void LowerHighsCondition_NullHighsArray_ReturnsFalse()
    {
        // Arrange
        var condition = new LowerHighsCondition(3);
        condition.GetRecentHighs = () => null!;

        // Act
        var result = condition.Evaluate(currentPrice: 148, vwap: 155);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region Configuration Tests

    [Test]
    public void LowerHighsCondition_Name_IsDescriptive()
    {
        // Arrange
        var condition = new LowerHighsCondition(3);

        // Assert
        Assert.That(condition.Name, Does.Contain("Lower"));
        Assert.That(condition.Name, Does.Contain("High").IgnoreCase);
    }

    [Test]
    public void LowerHighsCondition_MinimumLookback_ThrowsForLessThan2()
    {
        // Act & Assert - Lookback must be at least 2 to compare values
        Assert.Throws<ArgumentOutOfRangeException>(() => new LowerHighsCondition(1));
    }

    [Test]
    public void LowerHighsCondition_LookbackOf2_IsValid()
    {
        // Arrange & Act
        var condition = new LowerHighsCondition(2);

        // Assert
        Assert.That(condition.LookbackBars, Is.EqualTo(2));
    }

    [Test]
    public void LowerHighsCondition_LookbackBars_StoredCorrectly()
    {
        // Arrange
        var condition = new LowerHighsCondition(5);

        // Assert
        Assert.That(condition.LookbackBars, Is.EqualTo(5));
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public void LowerHighsCondition_MixedPattern_ReturnsFalse()
    {
        // Arrange - Mixed pattern: 150 < 160 but 160 > 155
        var condition = new LowerHighsCondition(3);
        condition.GetRecentHighs = () => new double[] { 150, 160, 155 };

        // Act
        var result = condition.Evaluate(currentPrice: 148, vwap: 155);

        // Assert - Pattern broken at 160 > 155
        Assert.That(result, Is.False);
    }

    [Test]
    public void LowerHighsCondition_SmallDifferences_ReturnsTrue()
    {
        // Arrange - Very small but consistent descending pattern
        var condition = new LowerHighsCondition(3);
        condition.GetRecentHighs = () => new double[] { 150.00, 150.01, 150.02 };

        // Act
        var result = condition.Evaluate(currentPrice: 149, vwap: 150);

        // Assert - Should detect even small descending patterns
        Assert.That(result, Is.True);
    }

    [Test]
    public void LowerHighsCondition_TwoHighsDescending_ReturnsTrue()
    {
        // Arrange - Minimum valid pattern with 2 bars
        var condition = new LowerHighsCondition(2);
        condition.GetRecentHighs = () => new double[] { 150, 155 };

        // Act
        var result = condition.Evaluate(currentPrice: 148, vwap: 152);

        // Assert - 150 < 155 is a valid descending pattern
        Assert.That(result, Is.True);
    }

    [Test]
    public void LowerHighsCondition_FourBarsDescending_ReturnsTrue()
    {
        // Arrange - Longer descending pattern
        var condition = new LowerHighsCondition(4);
        condition.GetRecentHighs = () => new double[] { 140, 145, 150, 155 };

        // Act
        var result = condition.Evaluate(currentPrice: 138, vwap: 145);

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    #region Comparison with HigherLows Tests

    [Test]
    public void LowerHighsCondition_IsBearishCounterpartOfHigherLows()
    {
        // Arrange - Same data but interpreted differently
        var lowerHighs = new LowerHighsCondition(3);
        var higherLows = new HigherLowsCondition(3);

        // Lower highs expects: most recent high < previous highs
        lowerHighs.GetRecentHighs = () => new double[] { 150, 155, 160 };
        
        // Higher lows expects: most recent low > previous lows
        higherLows.GetRecentLows = () => new double[] { 160, 155, 150 };

        // Act
        var lowerHighsResult = lowerHighs.Evaluate(currentPrice: 148, vwap: 155);
        var higherLowsResult = higherLows.Evaluate(currentPrice: 162, vwap: 155);

        // Assert - Both patterns should be detected
        Assert.That(lowerHighsResult, Is.True);
        Assert.That(higherLowsResult, Is.True);
    }

    #endregion
}
