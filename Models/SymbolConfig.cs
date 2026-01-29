// ============================================================================
// Symbol Configuration - Per-symbol strategy settings
// ============================================================================

namespace IdiotProof.Models
{
    /// <summary>
    /// Configuration for a single symbol's pullback strategy.
    /// Create one instance per symbol you want to trade.
    /// </summary>
    public sealed class SymbolConfig
    {
        // ----- Symbol Settings -----
        public required string Symbol { get; init; }
        public string Exchange { get; init; } = "SMART";
        public string Currency { get; init; } = "USD";
        public string SecType { get; init; } = "STK";

        // ----- Strategy Levels -----
        public required double BreakoutLevel { get; init; }     // Stage 1: Price must reach this level
        public required double PullbackLevel { get; init; }     // Stage 2: Price must pull back to this level
        public double VwapBuffer { get; init; } = 0.01;         // Stage 3: Price must be >= VWAP + this buffer

        // ----- Order Settings -----
        public int Quantity { get; init; } = 1000;              // Number of shares to buy
        public bool UseLimitEntry { get; init; } = true;        // true = LIMIT order, false = MARKET order
        public double LimitOffset { get; init; } = 0.02;        // For LIMIT orders: buy at VWAP + this offset
        public string TimeInForce { get; init; } = "GTC";       // GTC = Good Till Cancelled

        // ----- Take Profit Settings -----
        public bool EnableTakeProfit { get; init; } = true;     // Enable automatic take profit order
        public double TakeProfitOffset { get; init; } = 0.30;   // Sell at entry price + this offset

        // ----- RTH (Regular Trading Hours) Settings -----
        public bool AllowOutsideRth { get; init; } = true;      // Allow orders outside regular trading hours

        // ----- Job Control -----
        public bool Enabled { get; init; } = true;              // Enable/disable this job

        /// <summary>
        /// Creates a display-friendly summary of this configuration.
        /// </summary>
        public override string ToString()
        {
            return $"{Symbol}: Breakout={BreakoutLevel:F2}, Pullback={PullbackLevel:F2}, Qty={Quantity}, TP={TakeProfitOffset:F2}";
        }
    }
}
