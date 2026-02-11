// ============================================================================
// Multi-Symbol Volume/OBV/SMA/Momentum/ROC Tests
// Validates volume-based and momentum indicators across all symbols
// ============================================================================

using IdiotProof.Core.UnitTests.Helpers;
using IdiotProof.Helpers;

namespace IdiotProof.Core.UnitTests.Calculators;

[TestFixture]
public class MultiSymbolVolumeMomentumTests
{
    private static readonly string[] Symbols = TestDataLoader.GetAvailableSymbols().ToArray();

    // ========================================================================
    // Volume Calculator
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void Volume_AverageIsPositive_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var vol = new VolumeCalculator(20);

        foreach (var bar in bars)
            vol.Update(bar.Volume);

        if (vol.IsReady)
        {
            Assert.That(vol.AverageVolume, Is.GreaterThan(0),
                $"{symbol}: Average volume should be positive");
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void Volume_RatioIsPositive_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var vol = new VolumeCalculator(20);

        foreach (var bar in bars)
        {
            vol.Update(bar.Volume);
            if (vol.IsReady && vol.AverageVolume > 0)
            {
                Assert.That(vol.VolumeRatio, Is.GreaterThanOrEqualTo(0),
                    $"{symbol}: Volume ratio should be >= 0");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void Volume_DetectsAboveAverage_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        var vol = new VolumeCalculator(20);
        bool sawAboveAvg = false;

        foreach (var bar in bars)
        {
            vol.Update(bar.Volume);
            if (vol.IsReady && vol.IsAboveAverage(1.5))
                sawAboveAvg = true;
        }

        Assert.That(sawAboveAvg, Is.True,
            $"{symbol}: Should see above-average volume (1.5x) at least once in 2000 bars");
    }

    // ========================================================================
    // OBV Calculator
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void OBV_ProducesNonZeroValues_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var obv = new ObvCalculator(20);
        double prevClose = 0;

        foreach (var bar in bars)
        {
            if (prevClose > 0)
                obv.Update(bar.Close, bar.Volume);
            prevClose = bar.Close;
        }

        Assert.That(obv.CurrentObv, Is.Not.EqualTo(0),
            $"{symbol}: OBV should be non-zero after 200 bars of real data");
    }

    [TestCaseSource(nameof(Symbols))]
    public void OBV_GetScore_InRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var obv = new ObvCalculator(20);
        double prevClose = 0;

        foreach (var bar in bars)
        {
            if (prevClose > 0)
                obv.Update(bar.Close, bar.Volume);
            prevClose = bar.Close;
        }

        var score = obv.GetScore();
        Assert.That(score, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100),
            $"{symbol}: OBV score out of range: {score}");
    }

    [TestCaseSource(nameof(Symbols))]
    public void OBV_DetectsRisingOrFalling_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var obv = new ObvCalculator(20);
        double prevClose = 0;
        bool sawRising = false;
        bool sawFalling = false;

        foreach (var bar in bars)
        {
            if (prevClose > 0)
            {
                obv.Update(bar.Close, bar.Volume);
                if (obv.IsRising) sawRising = true;
                if (obv.IsFalling) sawFalling = true;
            }
            prevClose = bar.Close;
        }

        // At least one direction should be detected
        Assert.That(sawRising || sawFalling, Is.True,
            $"{symbol}: OBV should detect rising or falling trend");
    }

    // ========================================================================
    // SMA Calculator
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void SMA_StaysWithinPriceRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 200);
        var sma = new SmaCalculator(20);
        var allCloses = bars.Select(b => b.Close).ToArray();
        double minPrice = allCloses.Min();
        double maxPrice = allCloses.Max();

        foreach (var bar in bars)
        {
            sma.Update(bar.Close);
            if (sma.IsReady)
            {
                Assert.That(sma.CurrentValue, Is.GreaterThanOrEqualTo(minPrice * 0.5)
                    .And.LessThanOrEqualTo(maxPrice * 1.5),
                    $"{symbol}: SMA outside price range");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void SMA_GetScore_InRange_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var sma = new SmaCalculator(20);

        foreach (var bar in bars)
        {
            sma.Update(bar.Close);
            if (sma.IsReady)
            {
                var score = sma.GetScore(bar.Close);
                Assert.That(score, Is.GreaterThanOrEqualTo(-100).And.LessThanOrEqualTo(100),
                    $"{symbol}: SMA score out of range: {score}");
            }
        }
    }

    [TestCaseSource(nameof(Symbols))]
    public void SMA_DistancePercent_ReasonableForRealData_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var sma = new SmaCalculator(20);

        foreach (var bar in bars)
            sma.Update(bar.Close);

        if (sma.IsReady)
        {
            var lastPrice = bars[^1].Close;
            var dist = sma.GetDistancePercent(lastPrice);
            // Distance should be reasonable for 1-min bars
            Assert.That(Math.Abs(dist), Is.LessThan(50),
                $"{symbol}: SMA distance of {dist:F2}% seems extreme");
        }
    }

    // ========================================================================
    // Momentum Calculator
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void Momentum_ProducesValues_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var mom = new MomentumCalculator(10);
        int readyCount = 0;

        foreach (var bar in bars)
        {
            mom.Update(bar.Close);
            if (mom.IsReady)
                readyCount++;
        }

        Assert.That(readyCount, Is.GreaterThan(80),
            $"{symbol}: Momentum should produce values for most bars");
    }

    [TestCaseSource(nameof(Symbols))]
    public void Momentum_ProducesBothPositiveAndNegative_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        var mom = new MomentumCalculator(10);
        bool sawPositive = false;
        bool sawNegative = false;

        foreach (var bar in bars)
        {
            mom.Update(bar.Close);
            if (mom.IsReady)
            {
                if (mom.CurrentValue > 0) sawPositive = true;
                if (mom.CurrentValue < 0) sawNegative = true;
            }
        }

        Assert.That(sawPositive, Is.True,
            $"{symbol}: Should see positive momentum in 2000 bars");
        Assert.That(sawNegative, Is.True,
            $"{symbol}: Should see negative momentum in 2000 bars");
    }

    [Test]
    public void Momentum_PennyStockValues_AreSmall()
    {
        var bars = TestDataLoader.LoadBars("CCHH", 100);
        var mom = new MomentumCalculator(10);

        foreach (var bar in bars)
            mom.Update(bar.Close);

        if (mom.IsReady)
        {
            // For a ~$0.50 stock, momentum values should be small
            Assert.That(Math.Abs(mom.CurrentValue), Is.LessThan(1),
                $"CCHH momentum ({mom.CurrentValue:F4}) should be < $1");
        }
    }

    // ========================================================================
    // ROC Calculator
    // ========================================================================

    [TestCaseSource(nameof(Symbols))]
    public void ROC_ProducesValues_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 100);
        var roc = new RocCalculator(10);
        int readyCount = 0;

        foreach (var bar in bars)
        {
            roc.Update(bar.Close);
            if (roc.IsReady)
                readyCount++;
        }

        Assert.That(readyCount, Is.GreaterThan(80),
            $"{symbol}: ROC should produce values for most bars");
    }

    [TestCaseSource(nameof(Symbols))]
    public void ROC_ProducesBothPositiveAndNegative_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 2000);
        var roc = new RocCalculator(10);
        bool sawPositive = false;
        bool sawNegative = false;

        foreach (var bar in bars)
        {
            roc.Update(bar.Close);
            if (roc.IsReady)
            {
                if (roc.CurrentValue > 0) sawPositive = true;
                if (roc.CurrentValue < 0) sawNegative = true;
            }
        }

        Assert.That(sawPositive, Is.True,
            $"{symbol}: Should see positive ROC in 2000 bars");
        Assert.That(sawNegative, Is.True,
            $"{symbol}: Should see negative ROC in 2000 bars");
    }

    [TestCaseSource(nameof(Symbols))]
    public void ROC_ValuesAreReasonable_AllSymbols(string symbol)
    {
        var bars = TestDataLoader.LoadBars(symbol, 300);
        var roc = new RocCalculator(10);

        foreach (var bar in bars)
        {
            roc.Update(bar.Close);
            if (roc.IsReady)
            {
                // ROC for 1-min bars should rarely exceed ±50%
                Assert.That(roc.CurrentValue, Is.GreaterThan(-100).And.LessThan(100),
                    $"{symbol}: ROC of {roc.CurrentValue:F2}% seems extreme for 1-min data");
            }
        }
    }
}
