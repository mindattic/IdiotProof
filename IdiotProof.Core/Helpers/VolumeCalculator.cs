// ============================================================================
// Volume Calculator - Average Volume Tracking
// ============================================================================
//
// Tracks volume data to calculate average volume and detect volume spikes.
//
// FORMULA:
//   Average Volume = SMA of volume over N periods
//   Volume Ratio = Current Volume / Average Volume
//
// USAGE:
//   VolumeAbove(1.5) = Current volume >= 1.5x average volume (50% spike)
//   VolumeAbove(2.0) = Current volume >= 2x average volume (100% spike)
//
// WARM-UP:
//   Requires N candles to calculate meaningful average (default: 20 candles)
//
// ASCII VISUALIZATION:
//
//     Volume Spike Detection
//     +--------------------------------------------+
//     |                ████                        |
//     |                ████   ← Volume spike (2x+) |
//     |  Average ──────────────────────────────────|
//     |  ████      ████████      ████              |
//     |  ████  ██  ████████  ██  ████              |
//     +--------------------------------------------+
//
// ============================================================================

using System.Collections.Generic;
using System.Linq;

namespace IdiotProof.Helpers {
    /// <summary>
    /// Calculates average volume and detects volume spikes.
    /// </summary>
    public sealed class VolumeCalculator
    {
        private readonly int _period;
        private readonly Queue<long> _volumeHistory;
        private long _currentVolume;
        private double _averageVolume;

        /// <summary>
        /// Gets the averaging period.
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// Gets the current candle's volume.
        /// </summary>
        public long CurrentVolume => _currentVolume;

        /// <summary>
        /// Gets the average volume over the period.
        /// </summary>
        public double AverageVolume => _averageVolume;

        /// <summary>
        /// Gets the volume ratio (current / average).
        /// </summary>
        public double VolumeRatio => _averageVolume > 0 ? _currentVolume / _averageVolume : 0;

        /// <summary>
        /// Gets whether the calculator has enough data.
        /// </summary>
        public bool IsReady => _volumeHistory.Count >= _period;

        /// <summary>
        /// Creates a new volume calculator.
        /// </summary>
        /// <param name="period">The averaging period (default: 20).</param>
        public VolumeCalculator(int period = 20)
        {
            if (period < 1)
                throw new System.ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

            _period = period;
            _volumeHistory = new Queue<long>(period + 1);
        }

        /// <summary>
        /// Updates with a new candle's volume.
        /// </summary>
        /// <param name="volume">The volume of the completed candle.</param>
        public void Update(long volume)
        {
            if (volume <= 0)
                return;

            _currentVolume = volume;
            _volumeHistory.Enqueue(volume);

            // Keep only the required history
            while (_volumeHistory.Count > _period)
            {
                _volumeHistory.Dequeue();
            }

            // Calculate average
            if (_volumeHistory.Count > 0)
            {
                _averageVolume = _volumeHistory.Average();
            }
        }

        /// <summary>
        /// Checks if current volume is above the specified multiplier of average.
        /// </summary>
        /// <param name="multiplier">The volume multiplier (e.g., 1.5 = 150% of average).</param>
        /// <returns>True if current volume >= average × multiplier.</returns>
        public bool IsAboveAverage(double multiplier)
        {
            if (!IsReady || _averageVolume <= 0)
                return false;

            return _currentVolume >= _averageVolume * multiplier;
        }

        /// <summary>
        /// Resets the calculator to initial state.
        /// </summary>
        public void Reset()
        {
            _volumeHistory.Clear();
            _currentVolume = 0;
            _averageVolume = 0;
        }
    }
}


