// ============================================================================
// SegmentFactoryTests - Tests for strategy segment template creation
// ============================================================================

using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for the SegmentFactory class that creates strategy segment templates.
/// </summary>
[TestFixture]
public class SegmentFactoryTests
{
    #region GetAllTemplates Tests

    [Test]
    public void GetAllTemplates_ReturnsAllCategories()
    {
        // Act
        var templates = SegmentFactory.GetAllTemplates();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(templates, Contains.Key(SegmentCategory.Start));
            Assert.That(templates, Contains.Key(SegmentCategory.Session));
            Assert.That(templates, Contains.Key(SegmentCategory.PriceCondition));
            Assert.That(templates, Contains.Key(SegmentCategory.VwapCondition));
            Assert.That(templates, Contains.Key(SegmentCategory.IndicatorCondition));
            Assert.That(templates, Contains.Key(SegmentCategory.Order));
            Assert.That(templates, Contains.Key(SegmentCategory.RiskManagement));
            Assert.That(templates, Contains.Key(SegmentCategory.PositionManagement));
            Assert.That(templates, Contains.Key(SegmentCategory.OrderConfig));
        });
    }

    [Test]
    public void GetAllTemplates_StartCategory_ContainsTicker()
    {
        // Act
        var templates = SegmentFactory.GetAllTemplates();
        var startSegments = templates[SegmentCategory.Start];

        // Assert
        Assert.That(startSegments.Any(s => s.Type == SegmentType.Ticker), Is.True);
    }

    [Test]
    public void GetAllTemplates_OrderCategory_ContainsBuySellClose()
    {
        // Act
        var templates = SegmentFactory.GetAllTemplates();
        var orderSegments = templates[SegmentCategory.Order];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(orderSegments.Any(s => s.Type == SegmentType.Buy), Is.True);
            Assert.That(orderSegments.Any(s => s.Type == SegmentType.Sell), Is.True);
            Assert.That(orderSegments.Any(s => s.Type == SegmentType.Close), Is.True);
        });
    }

    [Test]
    public void GetAllTemplates_PriceConditions_ContainsExpectedTypes()
    {
        // Act
        var templates = SegmentFactory.GetAllTemplates();
        var priceConditions = templates[SegmentCategory.PriceCondition];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(priceConditions.Any(s => s.Type == SegmentType.Breakout), Is.True);
            Assert.That(priceConditions.Any(s => s.Type == SegmentType.Pullback), Is.True);
            Assert.That(priceConditions.Any(s => s.Type == SegmentType.IsPriceAbove), Is.True);
            Assert.That(priceConditions.Any(s => s.Type == SegmentType.IsPriceBelow), Is.True);
        });
    }

    [Test]
    public void GetAllTemplates_RiskManagement_ContainsStopAndTakeProfit()
    {
        // Act
        var templates = SegmentFactory.GetAllTemplates();
        var riskManagement = templates[SegmentCategory.RiskManagement];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(riskManagement.Any(s => s.Type == SegmentType.TakeProfit), Is.True);
            Assert.That(riskManagement.Any(s => s.Type == SegmentType.StopLoss), Is.True);
            Assert.That(riskManagement.Any(s => s.Type == SegmentType.TrailingStopLoss), Is.True);
        });
    }

    #endregion

    #region GetCategoryDisplayName Tests

    [Test]
    [TestCase(SegmentCategory.Start, "📍 Start")]
    [TestCase(SegmentCategory.Session, "⏰ Session")]
    [TestCase(SegmentCategory.PriceCondition, "💰 Price Conditions")]
    [TestCase(SegmentCategory.VwapCondition, "📊 VWAP Conditions")]
    [TestCase(SegmentCategory.IndicatorCondition, "📈 Indicators")]
    [TestCase(SegmentCategory.Order, "🛒 Orders")]
    [TestCase(SegmentCategory.RiskManagement, "🛡️ Risk Management")]
    [TestCase(SegmentCategory.PositionManagement, "📤 Position Management")]
    [TestCase(SegmentCategory.OrderConfig, "⚙️ Order Config")]
    public void GetCategoryDisplayName_ReturnsCorrectDisplayName(SegmentCategory category, string expected)
    {
        // Act
        var displayName = SegmentFactory.GetCategoryDisplayName(category);

        // Assert
        Assert.That(displayName, Is.EqualTo(expected));
    }

    #endregion

    #region GetCategoryColor Tests

    [Test]
    public void GetCategoryColor_ReturnsValidHexColor()
    {
        // Act & Assert
        foreach (SegmentCategory category in Enum.GetValues<SegmentCategory>())
        {
            var color = SegmentFactory.GetCategoryColor(category);
            Assert.That(color, Does.StartWith("#"), $"Color for {category} should be a hex color");
            Assert.That(color.Length, Is.EqualTo(7), $"Color for {category} should be 7 characters (#RRGGBB)");
        }
    }

    [Test]
    [TestCase(SegmentCategory.Start, "#4CAF50")]      // Green
    [TestCase(SegmentCategory.Order, "#F44336")]      // Red
    [TestCase(SegmentCategory.RiskManagement, "#FFC107")]  // Amber
    public void GetCategoryColor_ReturnsExpectedColors(SegmentCategory category, string expectedColor)
    {
        // Act
        var color = SegmentFactory.GetCategoryColor(category);

        // Assert
        Assert.That(color, Is.EqualTo(expectedColor));
    }

    #endregion

    #region Segment Template Parameter Tests

    [Test]
    public void TickerSegment_HasSymbolParameter()
    {
        // Act
        var templates = SegmentFactory.GetAllTemplates();
        var ticker = templates[SegmentCategory.Start].First(s => s.Type == SegmentType.Ticker);

        // Assert
        Assert.That(ticker.Parameters.Any(p => p.Name == "symbol"), Is.True);
    }

    [Test]
    public void BuySegment_HasRequiredParameters()
    {
        // Act
        var templates = SegmentFactory.GetAllTemplates();
        var buy = templates[SegmentCategory.Order].First(s => s.Type == SegmentType.Buy);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(buy.Parameters.Any(p => p.Name == "quantity"), Is.True);
            Assert.That(buy.Parameters.Any(p => p.Name == "priceType"), Is.True);
        });
    }

    [Test]
    public void BreakoutSegment_HasLevelParameter()
    {
        // Act
        var templates = SegmentFactory.GetAllTemplates();
        var breakout = templates[SegmentCategory.PriceCondition].First(s => s.Type == SegmentType.Breakout);

        // Assert
        var levelParam = breakout.Parameters.FirstOrDefault(p => p.Name == "level");
        Assert.Multiple(() =>
        {
            Assert.That(levelParam, Is.Not.Null);
            Assert.That(levelParam!.Type, Is.EqualTo(ParameterType.Price));
        });
    }

    [Test]
    public void TakeProfitSegment_HasPriceParameter()
    {
        // Act
        var templates = SegmentFactory.GetAllTemplates();
        var takeProfit = templates[SegmentCategory.RiskManagement].First(s => s.Type == SegmentType.TakeProfit);

        // Assert
        Assert.That(takeProfit.Parameters.Any(p => p.Name == "price" && p.Type == ParameterType.Price), Is.True);
    }

    [Test]
    public void TrailingStopLossSegment_HasPercentParameter()
    {
        // Act
        var templates = SegmentFactory.GetAllTemplates();
        var trailingStop = templates[SegmentCategory.RiskManagement].First(s => s.Type == SegmentType.TrailingStopLoss);

        // Assert
        Assert.That(trailingStop.Parameters.Any(p => p.Type == ParameterType.Percentage), Is.True);
    }

    #endregion

    #region Segment Cloning Tests

    [Test]
    public void SegmentClone_CreatesNewGuid()
    {
        // Arrange
        var templates = SegmentFactory.GetAllTemplates();
        var original = templates[SegmentCategory.Order].First(s => s.Type == SegmentType.Buy);
        var originalId = original.Id;

        // Act
        var clone = original.Clone();

        // Assert
        Assert.That(clone.Id, Is.Not.EqualTo(originalId));
    }

    [Test]
    public void SegmentClone_PreservesTypeAndCategory()
    {
        // Arrange
        var templates = SegmentFactory.GetAllTemplates();
        var original = templates[SegmentCategory.Order].First(s => s.Type == SegmentType.Buy);

        // Act
        var clone = original.Clone();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(clone.Type, Is.EqualTo(original.Type));
            Assert.That(clone.Category, Is.EqualTo(original.Category));
            Assert.That(clone.DisplayName, Is.EqualTo(original.DisplayName));
        });
    }

    #endregion
}
