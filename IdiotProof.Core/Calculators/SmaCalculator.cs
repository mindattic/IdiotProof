// ============================================================================
// SMA Calculator - Simple Moving Average Calculation
// ============================================================================
//
// SMA (Simple Moving Average) calculates the unweighted mean of the last N
// prices. Unlike EMA, all prices in the window carry equal weight.
//
// FORMULA:
//   SMA = (P1 + P2 + ... + PN) / N
//
// USAGE:
//   var sma20 = new SmaCalculator(20);
//   sma20.Update(price);  // Call on each candle close
//   if (sma20.IsReady)
//       Console.WriteLine($"SMA(20) = {sma20.CurrentValue}");
//
// USE CASES:
//   - Support/resistance levels (SMA 50, SMA 200 = "Golden Cross" / "Death Cross")
//   - Trend direction confirmation (price above SMA = bullish)
//   - Bollinger Bands middle band (SMA 20 is the center line)
//   - Crossover signals (SMA 50 crossing SMA 200)
//
// ============================================================================

using System;
using System.Collections.Generic;

namespace IdiotProof.Helpers
{
    /// <summary>
    /// Calculates Simple Moving Average (SMA) from price values.
    /// </summary>
    /// <remarks>
    /// <para><b>How It Works:</b></para>
    /// <list type="number">
    ///   <item>Maintains a sliding window of the last N prices.</item>
    ///   <item>Returns the arithmetic mean of all prices in the window.</item>
    ///   <item>Becomes ready after N prices have been received.</item>
    /// </list>
    /// </remarks>
    public sealed class SmaCalculator
    {
        private readonly int _period;
        private readonly Queue<double> _prices;
        private double _sum;

        /// <summary>
        /// Gets the SMA period.
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// Gets the current SMA value.
        /// </summary>
        public double CurrentValue { get; private set; }

        /// <summary>
        /// Gets the previous SMA value (1 bar ago).
        /// Used for slope/direction detection and crossover signals.
        /// </summary>
        public double PreviousValue { get; private set; }

        /// <summary>
        /// Gets whether the SMA calculator has enough data to provide a reliable value.
        /// Requires at least 'period' price points.
        /// </summary>
        public bool IsReady => _prices.Count >= _period;

        /// <summary>
        /// Gets the slope direction: positive = rising, negative = falling.
        /// </summary>
        public double Slope => IsReady ? CurrentValue - PreviousValue : 0;

        /// <summary>
        /// Gets whether the SMA is rising (current > previous).
        /// </summary>
        public bool IsRising => CurrentValue > PreviousValue;

        /// <summary>
        /// Gets whether the SMA is falling (current &lt; previous).
        /// </summary>
        public bool IsFalling => CurrentValue < PreviousValue;

        /// <summary>
        /// Creates a new SMA calculator.
        /// </summary>
        /// <param name="period">Number of periods for SMA calculation (e.g., 20, 50, 200).</param>
        public SmaCalculator(int period)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

            _period = period;
            _prices = new Queue<double>(period + 1);
        }

        /// <summary>
        /// Updates the SMA with a new price.
        /// </summary>
        /// <param name="price">The current price (typically candle close).</param>
        /// <returns>The updated SMA value, or 0 if not enough data yet.</returns>
        public double Update(double price)
        {
            if (price <= 0)
                return CurrentValue;

            _sum += price;
            _prices.Enqueue(price);

            // Remove oldest price if we exceed the period
            if (_prices.Count > _period)
            {
                _sum -= _prices.Dequeue();
            }

            PreviousValue = CurrentValue;
            CurrentValue = _sum / _prices.Count;
            return CurrentValue;
        }

        /// <summary>
        /// Checks if a given price is above this SMA.
        /// </summary>
        public bool IsPriceAbove(double price) => IsReady && price > CurrentValue;

        /// <summary>
        /// Checks if a given price is below this SMA.
        /// </summary>
        public bool IsPriceBelow(double price) => IsReady && price < CurrentValue;

        /// <summary>
        /// Gets the distance of a price from the SMA as a percentage.
        /// Positive = price above SMA, negative = below.
        /// </summary>
        public double GetDistancePercent(double price)
        {
            if (!IsReady || CurrentValue <= 0)
                return 0;
            return (price - CurrentValue) / CurrentValue * 100;
        }

        /// <summary>
        /// Calculates a score from -100 to +100 based on price position relative to SMA.
        /// Used for market score calculation.
        /// </summary>
        /// <param name="price">Current price.</param>
        /// <returns>Score: positive if price above SMA (bullish), negative if below (bearish).</returns>
        public int GetScore(double price)
        {
            if (!IsReady || CurrentValue <= 0)
                return 0;

            double distancePercent = GetDistancePercent(price);

            // Scale: 1% distance = ~20 score points, capped at ±100
            int score = (int)Math.Clamp(distancePercent * 20, -100, 100);

            // Bonus for slope confirmation
            if (IsRising && price > CurrentValue)
                score = Math.Min(score + 10, 100);
            else if (IsFalling && price < CurrentValue)
                score = Math.Max(score - 10, -100);

            return score;
        }

        /// <summary>
        /// Resets the calculator to initial state.
        /// </summary>
        public void Reset()
        {
            _prices.Clear();
            _sum = 0;
            CurrentValue = 0;
            PreviousValue = 0;
        }
    }
}
