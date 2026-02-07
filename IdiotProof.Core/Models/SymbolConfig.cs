// ============================================================================
// Symbol Configuration - Per-symbol strategy settings
// ============================================================================
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  IBKR API VALUES                                                          ║
// ║                                                                           ║
// ║  Some properties map directly to IB Contract/Order fields:                ║
// ║                                                                           ║
// ║  Contract Fields:                                                         ║
// ║    • Exchange  → contract.Exchange ("SMART", "NASDAQ", "NYSE", etc.)     ║
// ║    • Currency  → contract.Currency ("USD", "EUR", "GBP", etc.)           ║
// ║    • SecType   → contract.SecType ("STK", "OPT", "FUT", etc.)            ║
// ║                                                                           ║
// ║  Order Fields:                                                            ║
// ║    • TimeInForce → order.Tif ("GTC", "DAY", "IOC", "FOK")                ║
// ║                                                                           ║
// ║  Reference: https://interactivebrokers.github.io/tws-api/               ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

namespace IdiotProof.Backend.Models
{
    /// <summary>
    /// Configuration for a single symbol's pullback strategy.
    /// Create one instance per symbol you want to trade.
    /// </summary>
    /// <remarks>
    /// <para><b>IBKR API Mapping:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="Exchange"/> → <c>contract.Exchange</c> (default: "SMART" for automatic routing)</item>
    ///   <item><see cref="Currency"/> → <c>contract.Currency</c> (default: "USD")</item>
    ///   <item><see cref="SecType"/> → <c>contract.SecType</c> (default: "STK" for stocks)</item>
    ///   <item><see cref="TimeInForce"/> → <c>order.Tif</c> (default: "GTC")</item>
    /// </list>
    /// </remarks>
    public sealed class SymbolConfig
    {
        // ----- Symbol Settings (IB Contract fields) -----

        /// <summary>Stock ticker symbol (e.g., "AAPL", "MSFT").</summary>
        public required string Symbol { get; init; }

        /// <summary>Exchange for routing. IB API: contract.Exchange. Default: "SMART" (auto-route).</summary>
        public string Exchange { get; init; } = "SMART";

        /// <summary>Currency code. IB API: contract.Currency. Default: "USD".</summary>
        public string Currency { get; init; } = "USD";

        /// <summary>Security type. IB API: contract.SecType. Default: "STK" (stock).</summary>
        public string SecType { get; init; } = "STK";

        // ----- Strategy Levels -----
        public required double BreakoutLevel { get; init; }     // Stage 1: Price must reach this level
        public required double PullbackLevel { get; init; }     // Stage 2: Price must pull back to this level
        public double VwapBuffer { get; init; } = 0.01;         // Stage 3: Price must be >= VWAP + this buffer

        // ----- Order Settings -----

        /// <summary>Number of shares to buy.</summary>
        public int Quantity { get; init; } = 1000;

        /// <summary>True = LIMIT order (IB: "LMT"), False = MARKET order (IB: "MKT").</summary>
        public bool UseLimitEntry { get; init; } = true;

        /// <summary>For LIMIT orders: buy at VWAP + this offset.</summary>
        public double LimitOffset { get; init; } = 0.02;

        /// <summary>Time in force. IB API: order.Tif. Default: "GTC" (Good Till Cancelled).</summary>
        public string TimeInForce { get; init; } = "GTC";

        // ----- Take Profit Settings -----
        public bool EnableTakeProfit { get; init; } = true;     // Enable automatic take profit order
        public double TakeProfitOffset { get; init; } = 0.30;   // Sell at entry price + this offset

        // ----- RTH (Regular Trading Hours) Settings -----

        /// <summary>Allow orders outside regular trading hours. IB API: order.OutsideRth.</summary>
        public bool AllowOutsideRth { get; init; } = true;

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


