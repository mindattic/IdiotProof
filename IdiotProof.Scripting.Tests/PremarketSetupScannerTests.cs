// ============================================================================
// PremarketSetupScanner Unit Tests
// ============================================================================

using IdiotProof.Scripting;
using Xunit;

namespace IdiotProof.Scripting.Tests;

public class PremarketSetupScannerTests
{
    [Fact]
    public void ScanGappers_EmptyList_ReturnsEmptyResult()
    {
        var scanner = new PremarketSetupScanner();
        var result = scanner.ScanGappers([]);

        Assert.Equal(0, result.TotalScanned);
        Assert.Equal(0, result.QualifiedCount);
        Assert.Empty(result.Setups);
    }

    [Fact]
    public void ScanGappers_QualifiedGapper_CreatesSetup()
    {
        var scanner = new PremarketSetupScanner(new SetupScannerConfig
        {
            MinPrice = 0.30,
            MaxPrice = 25.00,
            MinGapPercent = 3.0,
            MinVolumeRatio = 1.5,
            MinConfidenceScore = 50
        });

        var gappers = new List<ScannerInput>
        {
            new()
            {
                Symbol = "TEST",
                CompanyName = "Test Corp",
                PremarketPrice = 5.00,
                PreviousClose = 4.50,
                PremarketVolume = 500_000,
                AverageVolume = 200_000,
                SourceCount = 2
            }
        };

        var result = scanner.ScanGappers(gappers);

        Assert.Equal(1, result.TotalScanned);
        Assert.Equal(1, result.QualifiedCount);
        Assert.Single(result.Setups);

        var setup = result.Setups[0];
        Assert.Equal("TEST", setup.Symbol);
        Assert.True(setup.GapPercent > 10); // 5.00 vs 4.50 = ~11%
        Assert.Equal(SetupState.Watching, setup.State);
    }

    [Fact]
    public void ScanGappers_PriceTooHigh_Excluded()
    {
        var scanner = new PremarketSetupScanner(new SetupScannerConfig
        {
            MaxPrice = 25.00
        });

        var gappers = new List<ScannerInput>
        {
            new()
            {
                Symbol = "EXPENSIVE",
                PremarketPrice = 100.00,
                PreviousClose = 90.00,
                PremarketVolume = 1_000_000,
                AverageVolume = 500_000
            }
        };

        var result = scanner.ScanGappers(gappers);

        Assert.Equal(0, result.QualifiedCount);
    }

    [Fact]
    public void ScanGappers_GapTooSmall_Excluded()
    {
        var scanner = new PremarketSetupScanner(new SetupScannerConfig
        {
            MinGapPercent = 5.0
        });

        var gappers = new List<ScannerInput>
        {
            new()
            {
                Symbol = "SMALLGAP",
                PremarketPrice = 10.20,
                PreviousClose = 10.00, // Only 2% gap
                PremarketVolume = 500_000,
                AverageVolume = 200_000
            }
        };

        var result = scanner.ScanGappers(gappers);

        Assert.Equal(0, result.QualifiedCount);
    }

    [Fact]
    public void ScanGappers_LowVolume_Excluded()
    {
        var scanner = new PremarketSetupScanner(new SetupScannerConfig
        {
            MinVolumeRatio = 2.0
        });

        var gappers = new List<ScannerInput>
        {
            new()
            {
                Symbol = "LOWVOL",
                PremarketPrice = 5.50,
                PreviousClose = 5.00,
                PremarketVolume = 100_000,
                AverageVolume = 200_000 // Only 0.5x ratio
            }
        };

        var result = scanner.ScanGappers(gappers);

        Assert.Equal(0, result.QualifiedCount);
    }

    [Fact]
    public void ScanGappers_MultipleGappers_SortedByConfidence()
    {
        var scanner = new PremarketSetupScanner(new SetupScannerConfig
        {
            MinConfidenceScore = 0, // Accept all
            MinGapPercent = 1.0,    // Low threshold
            MinVolumeRatio = 0.5    // Low threshold
        });

        var gappers = new List<ScannerInput>
        {
            new()
            {
                Symbol = "LOW",
                PremarketPrice = 2.20,         // 10% gap
                PreviousClose = 2.00,
                PremarketVolume = 200_000,
                AverageVolume = 100_000,       // 2x volume
                SourceCount = 1
            },
            new()
            {
                Symbol = "HIGH",
                PremarketPrice = 3.00,
                PreviousClose = 2.50,          // 20% gap
                PremarketVolume = 1_000_000,
                AverageVolume = 200_000,       // 5x volume
                SourceCount = 3
            }
        };

        var result = scanner.ScanGappers(gappers);

        Assert.Equal(2, result.QualifiedCount);
        Assert.Equal("HIGH", result.Setups[0].Symbol); // Higher confidence first
    }

    [Fact]
    public void Setup_ToIdiotScript_GeneratesValidScript()
    {
        var scanner = new PremarketSetupScanner(new SetupScannerConfig
        {
            MinConfidenceScore = 0
        });

        var gappers = new List<ScannerInput>
        {
            new()
            {
                Symbol = "AAPL",
                CompanyName = "Apple Inc",
                PremarketPrice = 5.00,
                PreviousClose = 4.50,
                PremarketVolume = 500_000,
                AverageVolume = 200_000
            }
        };

        var result = scanner.ScanGappers(gappers);
        var setup = result.Setups[0];
        var script = setup.ToIdiotScript();

        Assert.Contains("Ticker(AAPL)", script);
        Assert.Contains("Breakout(", script);
        Assert.Contains("Pullback()", script);
        Assert.Contains("Long()", script);
        Assert.Contains("TakeProfit(", script);
        Assert.Contains("StopLoss(", script);
    }

    [Fact]
    public void Setup_ToStrategyCard_GeneratesFormattedCard()
    {
        var scanner = new PremarketSetupScanner(new SetupScannerConfig
        {
            MinConfidenceScore = 0
        });

        var gappers = new List<ScannerInput>
        {
            new()
            {
                Symbol = "NVDA",
                CompanyName = "NVIDIA Corp",
                PremarketPrice = 10.00,
                PreviousClose = 9.00,
                PremarketVolume = 1_000_000,
                AverageVolume = 500_000
            }
        };

        var result = scanner.ScanGappers(gappers);
        var setup = result.Setups[0];
        var card = setup.ToStrategyCard();

        Assert.Contains("NVDA", card);
        Assert.Contains("Trigger:", card);
        Assert.Contains("Confirmation:", card);
        Assert.Contains("Targets:", card);
        Assert.Contains("NO BREAK, NO TRADE", card);
    }
}

public class BreakoutSetupTests
{
    [Fact]
    public void State_DefaultsToWatching()
    {
        var setup = new BreakoutSetup
        {
            Symbol = "TEST",
            TriggerPrice = 5.00
        };

        Assert.Equal(SetupState.Watching, setup.State);
    }

    [Fact]
    public void RiskRewardRatio_CalculatedCorrectly()
    {
        var setup = new BreakoutSetup
        {
            Symbol = "TEST",
            TriggerPrice = 5.00,
            InvalidationPrice = 4.75, // 5% risk
            Targets = [
                new TargetLevel { Price = 5.50, PercentToSell = 100 } // 10% reward
            ]
        };

        Assert.Equal(2.0, setup.RiskRewardRatio, 1); // 10% / 5% = 2:1
    }
}
