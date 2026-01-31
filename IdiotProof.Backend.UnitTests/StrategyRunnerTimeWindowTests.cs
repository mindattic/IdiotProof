// ============================================================================
// StrategyRunnerTimeWindowTests - Tests for Start/End time constraints
// ============================================================================

using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Helpers;
using IdiotProof.Backend.Models;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Tests for StrategyRunner time window enforcement.
/// Validates that Start() and End() times properly constrain when conditions are evaluated.
/// </summary>
[TestFixture]
public class StrategyRunnerTimeWindowTests
{
    #region Test Helpers

    /// <summary>
    /// Creates a test strategy with the specified time window.
    /// </summary>
    private static TradingStrategy CreateTestStrategy(TimeOnly? startTime, TimeOnly? endTime)
    {
        var builder = Stock.Ticker("TEST")
            .PriceAbove(10.00);

        if (startTime.HasValue)
            builder = builder.Start(startTime.Value);

        var strategyBuilder = builder.Buy(100, Price.Current);

        if (endTime.HasValue)
            return strategyBuilder.End(endTime.Value);

        return strategyBuilder.Build();
    }

    /// <summary>
    /// Test helper to simulate time-based condition evaluation.
    /// Uses reflection or internal access to test EvaluateConditions behavior.
    /// </summary>
    private class TestableStrategyRunner
    {
        private readonly TradingStrategy _strategy;
        private int _currentConditionIndex;
        private bool _isComplete;
        private StrategyResult _result = StrategyResult.Running;
        private readonly List<string> _logs = new();

        public int CurrentConditionIndex => _currentConditionIndex;
        public bool IsComplete => _isComplete;
        public StrategyResult Result => _result;
        public IReadOnlyList<string> Logs => _logs;

        public TestableStrategyRunner(TradingStrategy strategy)
        {
            _strategy = strategy;
        }

        /// <summary>
        /// Simulates EvaluateConditions with a given Eastern Time for testing.
        /// </summary>
        public void EvaluateConditionsAt(TimeOnly currentTimeET, double price, double vwap)
        {
            if (_currentConditionIndex >= _strategy.Conditions.Count)
                return;

            // If StartTime is set and we haven't reached it yet, don't evaluate
            if (_strategy.StartTime.HasValue && currentTimeET < _strategy.StartTime.Value)
            {
                _logs.Add($"Skipped: {currentTimeET:HH:mm} is before StartTime {_strategy.StartTime.Value:HH:mm}");
                return;
            }

            // If EndTime is set and we've passed it, mark complete and don't evaluate
            if (_strategy.EndTime.HasValue && currentTimeET > _strategy.EndTime.Value)
            {
                if (!_isComplete)
                {
                    _isComplete = true;
                    _result = StrategyResult.TimedOut;
                    _logs.Add($"TimedOut: {currentTimeET:HH:mm} is after EndTime {_strategy.EndTime.Value:HH:mm}");
                }
                return;
            }

            var condition = _strategy.Conditions[_currentConditionIndex];

            if (condition.Evaluate(price, vwap))
            {
                _currentConditionIndex++;
                _logs.Add($"Condition triggered at {currentTimeET:HH:mm}: {condition.Name}");

                if (_currentConditionIndex >= _strategy.Conditions.Count)
                {
                    _logs.Add("All conditions met - would execute order");
                }
            }
        }
    }

    #endregion

    #region StartTime Tests

    [Test]
    public void EvaluateConditions_BeforeStartTime_DoesNotEvaluate()
    {
        // Arrange
        var startTime = new TimeOnly(9, 30); // 9:30 AM ET
        var strategy = CreateTestStrategy(startTime, null);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate tick at 9:00 AM (before start)
        var currentTime = new TimeOnly(9, 0);
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runner.CurrentConditionIndex, Is.EqualTo(0), "Condition should not advance before StartTime");
            Assert.That(runner.IsComplete, Is.False);
            Assert.That(runner.Logs, Has.Some.Contains("before StartTime"));
        });
    }

    [Test]
    public void EvaluateConditions_AtStartTime_Evaluates()
    {
        // Arrange
        var startTime = new TimeOnly(9, 30); // 9:30 AM ET
        var strategy = CreateTestStrategy(startTime, null);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate tick at exactly 9:30 AM (at start)
        var currentTime = new TimeOnly(9, 30);
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);

        // Assert - price 15.00 > 10.00 should trigger condition
        Assert.That(runner.CurrentConditionIndex, Is.EqualTo(1), "Condition should advance at StartTime");
    }

    [Test]
    public void EvaluateConditions_AfterStartTime_Evaluates()
    {
        // Arrange
        var startTime = new TimeOnly(9, 30); // 9:30 AM ET
        var strategy = CreateTestStrategy(startTime, null);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate tick at 10:00 AM (after start)
        var currentTime = new TimeOnly(10, 0);
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);

        // Assert - price 15.00 > 10.00 should trigger condition
        Assert.That(runner.CurrentConditionIndex, Is.EqualTo(1), "Condition should advance after StartTime");
    }

    [Test]
    public void EvaluateConditions_NoStartTime_EvaluatesImmediately()
    {
        // Arrange
        var strategy = CreateTestStrategy(startTime: null, endTime: null);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate tick at any time
        var currentTime = new TimeOnly(3, 0); // 3:00 AM
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);

        // Assert
        Assert.That(runner.CurrentConditionIndex, Is.EqualTo(1), "Condition should evaluate when no StartTime is set");
    }

    #endregion

    #region EndTime Tests

    [Test]
    public void EvaluateConditions_BeforeEndTime_Evaluates()
    {
        // Arrange
        var endTime = new TimeOnly(16, 0); // 4:00 PM ET
        var strategy = CreateTestStrategy(null, endTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate tick at 3:00 PM (before end)
        var currentTime = new TimeOnly(15, 0);
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runner.CurrentConditionIndex, Is.EqualTo(1), "Condition should advance before EndTime");
            Assert.That(runner.IsComplete, Is.False);
            Assert.That(runner.Result, Is.EqualTo(StrategyResult.Running));
        });
    }

    [Test]
    public void EvaluateConditions_AtEndTime_Evaluates()
    {
        // Arrange
        var endTime = new TimeOnly(16, 0); // 4:00 PM ET
        var strategy = CreateTestStrategy(null, endTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate tick at exactly 4:00 PM (at end)
        var currentTime = new TimeOnly(16, 0);
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);

        // Assert - at end time should still evaluate (not past it)
        Assert.Multiple(() =>
        {
            Assert.That(runner.CurrentConditionIndex, Is.EqualTo(1), "Condition should advance at EndTime");
            Assert.That(runner.IsComplete, Is.False);
        });
    }

    [Test]
    public void EvaluateConditions_AfterEndTime_DoesNotEvaluate()
    {
        // Arrange
        var endTime = new TimeOnly(16, 0); // 4:00 PM ET
        var strategy = CreateTestStrategy(null, endTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate tick at 4:01 PM (after end)
        var currentTime = new TimeOnly(16, 1);
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runner.CurrentConditionIndex, Is.EqualTo(0), "Condition should not advance after EndTime");
            Assert.That(runner.IsComplete, Is.True, "Strategy should be marked complete");
            Assert.That(runner.Result, Is.EqualTo(StrategyResult.TimedOut), "Result should be TimedOut");
            Assert.That(runner.Logs, Has.Some.Contains("TimedOut"));
        });
    }

    [Test]
    public void EvaluateConditions_AfterEndTime_OnlyLogsOnce()
    {
        // Arrange
        var endTime = new TimeOnly(16, 0); // 4:00 PM ET
        var strategy = CreateTestStrategy(null, endTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate multiple ticks after end
        var currentTime = new TimeOnly(16, 30);
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);
        runner.EvaluateConditionsAt(currentTime, price: 16.00, vwap: 15.00);
        runner.EvaluateConditionsAt(currentTime, price: 17.00, vwap: 16.00);

        // Assert - should only log TimedOut once
        var timedOutLogs = runner.Logs.Count(l => l.Contains("TimedOut"));
        Assert.That(timedOutLogs, Is.EqualTo(1), "TimedOut should only be logged once");
    }

    #endregion

    #region Combined Start/End Window Tests

    [Test]
    public void EvaluateConditions_WithinWindow_Evaluates()
    {
        // Arrange
        var startTime = new TimeOnly(4, 0);  // 4:00 AM ET (premarket start)
        var endTime = new TimeOnly(9, 30);   // 9:30 AM ET (market open)
        var strategy = CreateTestStrategy(startTime, endTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate tick at 7:00 AM (within window)
        var currentTime = new TimeOnly(7, 0);
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runner.CurrentConditionIndex, Is.EqualTo(1), "Condition should advance within window");
            Assert.That(runner.IsComplete, Is.False);
            Assert.That(runner.Result, Is.EqualTo(StrategyResult.Running));
        });
    }

    [Test]
    public void EvaluateConditions_BeforeWindow_DoesNotEvaluate()
    {
        // Arrange
        var startTime = new TimeOnly(4, 0);  // 4:00 AM ET
        var endTime = new TimeOnly(9, 30);   // 9:30 AM ET
        var strategy = CreateTestStrategy(startTime, endTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate tick at 3:00 AM (before window)
        var currentTime = new TimeOnly(3, 0);
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runner.CurrentConditionIndex, Is.EqualTo(0));
            Assert.That(runner.IsComplete, Is.False);
            Assert.That(runner.Logs, Has.Some.Contains("before StartTime"));
        });
    }

    [Test]
    public void EvaluateConditions_AfterWindow_TimesOut()
    {
        // Arrange
        var startTime = new TimeOnly(4, 0);  // 4:00 AM ET
        var endTime = new TimeOnly(9, 30);   // 9:30 AM ET
        var strategy = CreateTestStrategy(startTime, endTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act - simulate tick at 10:00 AM (after window)
        var currentTime = new TimeOnly(10, 0);
        runner.EvaluateConditionsAt(currentTime, price: 15.00, vwap: 14.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runner.CurrentConditionIndex, Is.EqualTo(0));
            Assert.That(runner.IsComplete, Is.True);
            Assert.That(runner.Result, Is.EqualTo(StrategyResult.TimedOut));
        });
    }

    [Test]
    public void PreMarketStrategy_OnlyEvaluatesDuringPreMarket()
    {
        // Arrange - typical premarket strategy
        var startTime = new TimeOnly(4, 0);   // 4:00 AM ET
        var endTime = new TimeOnly(9, 30);    // 9:30 AM ET
        var strategy = CreateTestStrategy(startTime, endTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act & Assert - before premarket
        runner.EvaluateConditionsAt(new TimeOnly(3, 59), price: 15.00, vwap: 14.00);
        Assert.That(runner.CurrentConditionIndex, Is.EqualTo(0), "Should not evaluate before 4:00 AM");

        // Act & Assert - during premarket
        runner.EvaluateConditionsAt(new TimeOnly(4, 0), price: 15.00, vwap: 14.00);
        Assert.That(runner.CurrentConditionIndex, Is.EqualTo(1), "Should evaluate at 4:00 AM");
    }

    [Test]
    public void Strategy_ConditionNotMet_ThenTimesOut()
    {
        // Arrange
        var startTime = new TimeOnly(9, 0);
        var endTime = new TimeOnly(9, 30);
        var strategy = CreateTestStrategy(startTime, endTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act - tick during window but condition not met (price below threshold)
        runner.EvaluateConditionsAt(new TimeOnly(9, 15), price: 5.00, vwap: 5.00); // 5.00 < 10.00
        Assert.That(runner.CurrentConditionIndex, Is.EqualTo(0), "Condition not met");

        // Act - window ends
        runner.EvaluateConditionsAt(new TimeOnly(9, 31), price: 5.00, vwap: 5.00);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(runner.IsComplete, Is.True);
            Assert.That(runner.Result, Is.EqualTo(StrategyResult.TimedOut));
            Assert.That(runner.CurrentConditionIndex, Is.EqualTo(0), "Condition never advanced");
        });
    }

    #endregion

    #region MarketTime Helper Integration Tests

    [Test]
    public void Strategy_UsingMarketTimePreMarket_HasCorrectWindow()
    {
        // Arrange - using MarketTime helper like in Program.cs
        var strategy = Stock.Ticker("TEST")
            .Start(MarketTime.PreMarket.Start)  // 4:00 AM ET
            .PriceAbove(10.00)
            .Buy(100, Price.Current)
            .End(MarketTime.PreMarket.End);     // 9:30 AM ET

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(4, 0)));
            Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(9, 30)));
        });
    }

    [Test]
    public void Strategy_UsingMarketTimeRTH_HasCorrectWindow()
    {
        // Arrange
        var strategy = Stock.Ticker("TEST")
            .Start(MarketTime.RTH.Start)  // 9:30 AM ET
            .PriceAbove(10.00)
            .Buy(100, Price.Current)
            .End(MarketTime.RTH.End);     // 4:00 PM ET

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(strategy.StartTime, Is.EqualTo(new TimeOnly(9, 30)));
            Assert.That(strategy.EndTime, Is.EqualTo(new TimeOnly(16, 0)));
        });
    }

    #endregion

    #region Edge Cases

    [Test]
    public void EvaluateConditions_MidnightCrossover_HandlesCorrectly()
    {
        // Arrange - strategy that spans midnight (theoretical)
        var startTime = new TimeOnly(23, 0);  // 11:00 PM
        var endTime = new TimeOnly(23, 59);   // 11:59 PM
        var strategy = CreateTestStrategy(startTime, endTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act - at 11:30 PM
        runner.EvaluateConditionsAt(new TimeOnly(23, 30), price: 15.00, vwap: 14.00);

        // Assert
        Assert.That(runner.CurrentConditionIndex, Is.EqualTo(1));
    }

    [Test]
    public void EvaluateConditions_StartTimeEqualsEndTime_EvaluatesAtExactTime()
    {
        // Arrange - edge case: start == end (single point in time)
        var exactTime = new TimeOnly(9, 30);
        var strategy = CreateTestStrategy(exactTime, exactTime);
        var runner = new TestableStrategyRunner(strategy);

        // Act - at exact time
        runner.EvaluateConditionsAt(exactTime, price: 15.00, vwap: 14.00);

        // Assert - should evaluate at the exact time
        Assert.That(runner.CurrentConditionIndex, Is.EqualTo(1));
    }

    [Test]
    public void EvaluateConditions_ConditionNotMet_DoesNotAdvance()
    {
        // Arrange
        var strategy = CreateTestStrategy(null, null);
        var runner = new TestableStrategyRunner(strategy);

        // Act - price below threshold (10.00)
        runner.EvaluateConditionsAt(new TimeOnly(10, 0), price: 5.00, vwap: 5.00);

        // Assert
        Assert.That(runner.CurrentConditionIndex, Is.EqualTo(0), "Condition should not advance when not met");
    }

    #endregion
}
