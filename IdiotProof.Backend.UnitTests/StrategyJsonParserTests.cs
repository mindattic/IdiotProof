// ============================================================================
// StrategyJsonParserTests - Tests for JSON parsing and file operations
// ============================================================================

using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Models;
using IdiotProof.Shared.Services;
using NUnit.Framework;
using System.Text.Json;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for the StrategyJsonParser class that handles strategy JSON serialization/deserialization.
/// </summary>
[TestFixture]
public class StrategyJsonParserTests
{
    private string _testFolder = null!;

    [SetUp]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), $"IdiotProof_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testFolder);
    }

    [TearDown]
    public void Teardown()
    {
        if (Directory.Exists(_testFolder))
        {
            Directory.Delete(_testFolder, recursive: true);
        }
    }

    #region GetDefaultFolder Tests

    [Test]
    public void GetDefaultFolder_ReturnsValidPath()
    {
        // Act
        var folder = StrategyJsonParser.GetDefaultFolder();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(folder, Is.Not.Null.And.Not.Empty);
            Assert.That(folder, Does.Contain("IdiotProof"));
            Assert.That(folder, Does.Contain("Strategies"));
        });
    }

    #endregion

    #region GetDateFolder Tests

    [Test]
    public void GetDateFolder_ReturnsCorrectPath()
    {
        // Arrange
        var date = new DateOnly(2025, 1, 15);

        // Act
        var folder = StrategyJsonParser.GetDateFolder(date, _testFolder);

        // Assert
        Assert.That(folder, Does.Contain("2025-01-15"));
        Assert.That(folder, Does.StartWith(_testFolder));
    }

    [Test]
    public void GetDateFolder_WithNullBaseFolder_UsesDefault()
    {
        // Arrange
        var date = new DateOnly(2025, 1, 15);

        // Act
        var folder = StrategyJsonParser.GetDateFolder(date);

        // Assert
        Assert.That(folder, Does.Contain(StrategyJsonParser.GetDefaultFolder()));
    }

    #endregion

    #region LoadFromJson Tests

    [Test]
    public void LoadFromJson_ValidJson_ReturnsStrategy()
    {
        // Arrange
        var json = """
        {
            "id": "12345678-1234-1234-1234-123456789abc",
            "name": "Test Strategy",
            "symbol": "AAPL",
            "enabled": true,
            "segments": []
        }
        """;

        // Act
        var strategy = StrategyJsonParser.LoadFromJson(json);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy, Is.Not.Null);
            Assert.That(strategy!.Name, Is.EqualTo("Test Strategy"));
            Assert.That(strategy.Symbol, Is.EqualTo("AAPL"));
            Assert.That(strategy.Enabled, Is.True);
        });
    }

    [Test]
    public void LoadFromJson_InvalidJson_ThrowsJsonException()
    {
        // Arrange
        var invalidJson = "{ invalid json }";

        // Act & Assert
        Assert.Throws<JsonException>(() => StrategyJsonParser.LoadFromJson(invalidJson));
    }

    [Test]
    public void LoadFromJson_EmptyString_ReturnsNull()
    {
        // Arrange
        var emptyJson = "";

        // Act & Assert
        Assert.Throws<JsonException>(() => StrategyJsonParser.LoadFromJson(emptyJson));
    }

    [Test]
    public void LoadFromJson_WithSegments_ParsesSegments()
    {
        // Arrange
        var json = """
        {
            "name": "Test Strategy",
            "symbol": "AAPL",
            "enabled": true,
            "segments": [
                {
                    "type": "Ticker",
                    "category": "Start",
                    "displayName": "Ticker",
                    "order": 1,
                    "parameters": [
                        { "name": "symbol", "label": "Symbol", "type": "String", "value": "AAPL" }
                    ]
                }
            ]
        }
        """;

        // Act
        var strategy = StrategyJsonParser.LoadFromJson(json);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy!.Segments, Has.Count.EqualTo(1));
            Assert.That(strategy.Segments[0].Type, Is.EqualTo(SegmentType.Ticker));
        });
    }

    #endregion

    #region SaveToFile and LoadFromFile Tests

    [Test]
    public void SaveToFile_CreatesFile()
    {
        // Arrange
        var strategy = CreateTestStrategy();
        var filePath = Path.Combine(_testFolder, "test_strategy.json");

        // Act
        StrategyJsonParser.SaveToFile(strategy, filePath);

        // Assert
        Assert.That(File.Exists(filePath), Is.True);
    }

    [Test]
    public void SaveToFile_CreatesDirectory()
    {
        // Arrange
        var strategy = CreateTestStrategy();
        var newDir = Path.Combine(_testFolder, "subdir");
        var filePath = Path.Combine(newDir, "test_strategy.json");

        // Act
        StrategyJsonParser.SaveToFile(strategy, filePath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(newDir), Is.True);
            Assert.That(File.Exists(filePath), Is.True);
        });
    }

    [Test]
    public void LoadFromFile_ReturnsStrategy()
    {
        // Arrange
        var strategy = CreateTestStrategy();
        var filePath = Path.Combine(_testFolder, "test_strategy.json");
        StrategyJsonParser.SaveToFile(strategy, filePath);

        // Act
        var loaded = StrategyJsonParser.LoadFromFile(filePath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Name, Is.EqualTo(strategy.Name));
            Assert.That(loaded.Symbol, Is.EqualTo(strategy.Symbol));
        });
    }

    [Test]
    public void LoadFromFile_NonExistentFile_ReturnsNull()
    {
        // Arrange
        var filePath = Path.Combine(_testFolder, "does_not_exist.json");

        // Act
        var result = StrategyJsonParser.LoadFromFile(filePath);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void SaveAndLoad_PreservesAllProperties()
    {
        // Arrange
        var original = new StrategyDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Complete Strategy",
            Description = "A strategy with all properties set",
            Symbol = "MSFT",
            Enabled = true,
            Author = "Test Author",
            Tags = ["swing", "premarket"],
            Segments = new List<StrategySegment>
            {
                new()
                {
                    Type = SegmentType.Ticker,
                    Category = SegmentCategory.Start,
                    DisplayName = "Ticker",
                    Order = 1,
                    Parameters = new List<SegmentParameter>
                    {
                        new() { Name = "symbol", Label = "Symbol", Type = ParameterType.String, Value = "MSFT" }
                    }
                }
            }
        };
        var filePath = Path.Combine(_testFolder, "complete_strategy.json");

        // Act
        StrategyJsonParser.SaveToFile(original, filePath);
        var loaded = StrategyJsonParser.LoadFromFile(filePath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.Id, Is.EqualTo(original.Id));
            Assert.That(loaded.Name, Is.EqualTo(original.Name));
            Assert.That(loaded.Description, Is.EqualTo(original.Description));
            Assert.That(loaded.Symbol, Is.EqualTo(original.Symbol));
            Assert.That(loaded.Enabled, Is.EqualTo(original.Enabled));
            Assert.That(loaded.Author, Is.EqualTo(original.Author));
            Assert.That(loaded.Tags, Is.EquivalentTo(original.Tags));
            Assert.That(loaded.Segments, Has.Count.EqualTo(original.Segments.Count));
        });
    }

    #endregion

    #region LoadStrategiesForDate Tests

    [Test]
    public void LoadStrategiesForDate_NoFolder_ReturnsEmptyList()
    {
        // Arrange
        var date = new DateOnly(2099, 12, 31);  // Far future date

        // Act
        var strategies = StrategyJsonParser.LoadStrategiesForDate(date, _testFolder);

        // Assert
        Assert.That(strategies, Is.Empty);
    }

    [Test]
    public void LoadStrategiesForDate_EmptyFolder_ReturnsEmptyList()
    {
        // Arrange
        var date = new DateOnly(2025, 1, 15);
        var dateFolder = StrategyJsonParser.GetDateFolder(date, _testFolder);
        Directory.CreateDirectory(dateFolder);

        // Act
        var strategies = StrategyJsonParser.LoadStrategiesForDate(date, _testFolder);

        // Assert
        Assert.That(strategies, Is.Empty);
    }

    [Test]
    public void LoadStrategiesForDate_WithStrategies_ReturnsAllStrategies()
    {
        // Arrange
        var date = new DateOnly(2025, 1, 15);
        var dateFolder = StrategyJsonParser.GetDateFolder(date, _testFolder);
        Directory.CreateDirectory(dateFolder);

        // Create two strategy files
        var strategy1 = CreateTestStrategy("AAPL Strategy", "AAPL");
        var strategy2 = CreateTestStrategy("MSFT Strategy", "MSFT");

        StrategyJsonParser.SaveToFile(strategy1, Path.Combine(dateFolder, "strategy1.json"));
        StrategyJsonParser.SaveToFile(strategy2, Path.Combine(dateFolder, "strategy2.json"));

        // Act
        var strategies = StrategyJsonParser.LoadStrategiesForDate(date, _testFolder);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategies, Has.Count.EqualTo(2));
            Assert.That(strategies.Any(s => s.Symbol == "AAPL"), Is.True);
            Assert.That(strategies.Any(s => s.Symbol == "MSFT"), Is.True);
        });
    }

    [Test]
    public void LoadStrategiesForDate_SkipsInvalidFiles()
    {
        // Arrange
        var date = new DateOnly(2025, 1, 15);
        var dateFolder = StrategyJsonParser.GetDateFolder(date, _testFolder);
        Directory.CreateDirectory(dateFolder);

        // Create one valid and one invalid file
        var strategy = CreateTestStrategy();
        StrategyJsonParser.SaveToFile(strategy, Path.Combine(dateFolder, "valid.json"));
        File.WriteAllText(Path.Combine(dateFolder, "invalid.json"), "{ not valid json }");

        // Act
        var strategies = StrategyJsonParser.LoadStrategiesForDate(date, _testFolder);

        // Assert - should only contain the valid strategy
        Assert.That(strategies, Has.Count.EqualTo(1));
    }

    [Test]
    public void LoadStrategiesForDate_ReturnsOrderedByName()
    {
        // Arrange
        var date = new DateOnly(2025, 1, 15);
        var dateFolder = StrategyJsonParser.GetDateFolder(date, _testFolder);
        Directory.CreateDirectory(dateFolder);

        var strategyZ = CreateTestStrategy("Zebra Strategy", "ZZZ");
        var strategyA = CreateTestStrategy("Alpha Strategy", "AAA");
        var strategyM = CreateTestStrategy("Middle Strategy", "MMM");

        StrategyJsonParser.SaveToFile(strategyZ, Path.Combine(dateFolder, "z.json"));
        StrategyJsonParser.SaveToFile(strategyA, Path.Combine(dateFolder, "a.json"));
        StrategyJsonParser.SaveToFile(strategyM, Path.Combine(dateFolder, "m.json"));

        // Act
        var strategies = StrategyJsonParser.LoadStrategiesForDate(date, _testFolder);

        // Assert - should be ordered alphabetically by name
        Assert.Multiple(() =>
        {
            Assert.That(strategies[0].Name, Is.EqualTo("Alpha Strategy"));
            Assert.That(strategies[1].Name, Is.EqualTo("Middle Strategy"));
            Assert.That(strategies[2].Name, Is.EqualTo("Zebra Strategy"));
        });
    }

    #endregion

    #region LoadEnabledStrategies Tests

    [Test]
    public void LoadEnabledStrategies_FiltersDisabled()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.Today);
        var dateFolder = StrategyJsonParser.GetDateFolder(date, _testFolder);
        Directory.CreateDirectory(dateFolder);

        var enabled = CreateTestStrategy("Enabled", "AAPL");
        enabled.Enabled = true;

        var disabled = CreateTestStrategy("Disabled", "MSFT");
        disabled.Enabled = false;

        StrategyJsonParser.SaveToFile(enabled, Path.Combine(dateFolder, "enabled.json"));
        StrategyJsonParser.SaveToFile(disabled, Path.Combine(dateFolder, "disabled.json"));

        // Act
        var strategies = StrategyJsonParser.LoadEnabledStrategies(_testFolder);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategies, Has.Count.EqualTo(1));
            Assert.That(strategies[0].Name, Is.EqualTo("Enabled"));
        });
    }

    #endregion

    #region Helper Methods

    private static StrategyDefinition CreateTestStrategy(string name = "Test Strategy", string symbol = "AAPL")
    {
        return new StrategyDefinition
        {
            Id = Guid.NewGuid(),
            Name = name,
            Symbol = symbol,
            Enabled = true,
            Segments = new List<StrategySegment>
            {
                new()
                {
                    Type = SegmentType.Ticker,
                    Category = SegmentCategory.Start,
                    DisplayName = "Ticker",
                    Order = 1,
                    Parameters = new List<SegmentParameter>
                    {
                        new() { Name = "symbol", Label = "Symbol", Type = ParameterType.String, Value = symbol }
                    }
                }
            }
        };
    }

    #endregion
}


