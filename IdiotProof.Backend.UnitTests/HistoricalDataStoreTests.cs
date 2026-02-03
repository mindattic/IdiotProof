// ============================================================================
// Historical Data Store Unit Tests
// ============================================================================

using IdiotProof.Backend.Models;
using IdiotProof.Backend.Services;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests
{
    [TestFixture]
    public class HistoricalDataStoreTests
    {
        [Test]
        public void SetHistoricalData_StoresBarsCorrectly()
        {
            // Arrange
            var store = new HistoricalDataStore();
            var bars = CreateTestBars(10);

            // Act
            store.SetHistoricalData("AAPL", bars);

            // Assert
            Assert.That(store.HasData("AAPL"), Is.True);
            Assert.That(store.GetBars("AAPL").Count, Is.EqualTo(10));
        }

        [Test]
        public void GetBars_ReturnsChronologicalOrder()
        {
            // Arrange
            var store = new HistoricalDataStore();
            var bars = new List<HistoricalBar>
            {
                CreateBar(DateTime.Now.AddMinutes(-2), 100),
                CreateBar(DateTime.Now, 102),
                CreateBar(DateTime.Now.AddMinutes(-1), 101),
            };

            // Act
            store.SetHistoricalData("AAPL", bars);
            var result = store.GetBars("AAPL");

            // Assert
            Assert.That(result[0].Close, Is.EqualTo(100));
            Assert.That(result[1].Close, Is.EqualTo(101));
            Assert.That(result[2].Close, Is.EqualTo(102));
        }

        [Test]
        public void GetRecentBars_ReturnsLastNBars()
        {
            // Arrange
            var store = new HistoricalDataStore();
            var bars = CreateTestBars(10);
            store.SetHistoricalData("AAPL", bars);

            // Act
            var result = store.GetRecentBars("AAPL", 3);

            // Assert
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0].Close, Is.EqualTo(bars[7].Close));
            Assert.That(result[1].Close, Is.EqualTo(bars[8].Close));
            Assert.That(result[2].Close, Is.EqualTo(bars[9].Close));
        }

        [Test]
        public void GetBarsInRange_ReturnsCorrectSubset()
        {
            // Arrange
            var store = new HistoricalDataStore();
            var baseTime = DateTime.Now.AddHours(-5);
            var bars = Enumerable.Range(0, 10)
                .Select(i => CreateBar(baseTime.AddMinutes(i * 30), 100 + i))
                .ToList();
            store.SetHistoricalData("AAPL", bars);

            // Act
            var startDate = baseTime.AddMinutes(60);
            var endDate = baseTime.AddMinutes(150);
            var result = store.GetBarsInRange("AAPL", startDate, endDate);

            // Assert
            Assert.That(result.Count, Is.EqualTo(4)); // bars at 60, 90, 120, 150 minutes
        }

        [Test]
        public void AppendBar_AddsNewBarToExistingData()
        {
            // Arrange
            var store = new HistoricalDataStore();
            var bars = CreateTestBars(5);
            store.SetHistoricalData("AAPL", bars);

            var newBar = CreateBar(DateTime.Now.AddMinutes(10), 200);

            // Act
            var result = store.AppendBar("AAPL", newBar);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(store.GetBars("AAPL").Count, Is.EqualTo(6));
            Assert.That(store.GetLastBar("AAPL")!.Close, Is.EqualTo(200));
        }

        [Test]
        public void AppendBar_RejectsDuplicateBar()
        {
            // Arrange
            var store = new HistoricalDataStore();
            var bars = CreateTestBars(5);
            store.SetHistoricalData("AAPL", bars);

            var duplicateBar = CreateBar(bars[4].Time, 200); // Same time as last bar

            // Act
            var result = store.AppendBar("AAPL", duplicateBar);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(store.GetBars("AAPL").Count, Is.EqualTo(5));
        }

        [Test]
        public void AppendBar_TrimsOldBarsWhenMaxReached()
        {
            // Arrange
            var store = new HistoricalDataStore();
            var bars = CreateTestBars(5);
            store.SetHistoricalData("AAPL", bars);

            // Append bars until we exceed max
            for (int i = 0; i < 10; i++)
            {
                var newBar = CreateBar(DateTime.Now.AddMinutes(10 + i), 200 + i);
                store.AppendBar("AAPL", newBar, maxBars: 8);
            }

            // Assert
            Assert.That(store.GetBars("AAPL").Count, Is.EqualTo(8));
        }

        [Test]
        public void IsWarmedUp_ReturnsTrueWhenEnoughBars()
        {
            // Arrange
            var store = new HistoricalDataStore();
            store.SetHistoricalData("AAPL", CreateTestBars(21));
            store.SetHistoricalData("MSFT", CreateTestBars(20));

            // Assert
            Assert.That(store.IsWarmedUp("AAPL"), Is.True);
            Assert.That(store.IsWarmedUp("MSFT"), Is.False);
        }

        [Test]
        public void IsFullyWarmedUp_ReturnsTrueWhen200Bars()
        {
            // Arrange
            var store = new HistoricalDataStore();
            store.SetHistoricalData("AAPL", CreateTestBars(200));
            store.SetHistoricalData("MSFT", CreateTestBars(199));

            // Assert
            Assert.That(store.IsFullyWarmedUp("AAPL"), Is.True);
            Assert.That(store.IsFullyWarmedUp("MSFT"), Is.False);
        }

        [Test]
        public void GetClosePrices_ReturnsAllClosePrices()
        {
            // Arrange
            var store = new HistoricalDataStore();
            var bars = new List<HistoricalBar>
            {
                CreateBar(DateTime.Now.AddMinutes(-2), 100),
                CreateBar(DateTime.Now.AddMinutes(-1), 101),
                CreateBar(DateTime.Now, 102),
            };
            store.SetHistoricalData("AAPL", bars);

            // Act
            var closePrices = store.GetClosePrices("AAPL");

            // Assert
            Assert.That(closePrices.Count, Is.EqualTo(3));
            Assert.That(closePrices[0], Is.EqualTo(100));
            Assert.That(closePrices[1], Is.EqualTo(101));
            Assert.That(closePrices[2], Is.EqualTo(102));
        }

        [Test]
        public void GetSummary_ReturnsCorrectInfo()
        {
            // Arrange
            var store = new HistoricalDataStore();
            store.SetHistoricalData("AAPL", CreateTestBars(10));
            store.SetHistoricalData("MSFT", CreateTestBars(20));

            // Act
            var summary = store.GetSummary();

            // Assert
            Assert.That(summary.Count, Is.EqualTo(2));
            Assert.That(summary["AAPL"].BarCount, Is.EqualTo(10));
            Assert.That(summary["MSFT"].BarCount, Is.EqualTo(20));
        }

        [Test]
        public void CaseInsensitiveSymbolLookup()
        {
            // Arrange
            var store = new HistoricalDataStore();
            store.SetHistoricalData("AAPL", CreateTestBars(10));

            // Assert
            Assert.That(store.HasData("aapl"), Is.True);
            Assert.That(store.HasData("Aapl"), Is.True);
            Assert.That(store.HasData("AAPL"), Is.True);
        }

        private static List<HistoricalBar> CreateTestBars(int count)
        {
            var baseTime = DateTime.Now.AddMinutes(-count);
            return Enumerable.Range(0, count)
                .Select(i => CreateBar(baseTime.AddMinutes(i), 100 + i))
                .ToList();
        }

        private static HistoricalBar CreateBar(DateTime time, double close)
        {
            return new HistoricalBar
            {
                Time = time,
                Open = close - 0.5,
                High = close + 1,
                Low = close - 1,
                Close = close,
                Volume = 1000
            };
        }
    }
}
