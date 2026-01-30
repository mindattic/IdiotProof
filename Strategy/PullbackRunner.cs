// ============================================================================
// Pullback Runner - Strategy State Machine
// ============================================================================
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  IBKR API DEPENDENCY                                                      ║
// ║                                                                           ║
// ║  This class directly interfaces with the Interactive Brokers TWS API.     ║
// ║  When modifying order submission code, ensure compatibility with IB API:  ║
// ║                                                                           ║
// ║  Order Properties Used:                                                   ║
// ║    • order.Action      = "BUY" | "SELL"                                  ║
// ║    • order.OrderType   = "MKT" | "LMT"                                   ║
// ║    • order.TotalQuantity                                                  ║
// ║    • order.LmtPrice    (for limit orders)                                ║
// ║    • order.OutsideRth  = true | false                                    ║
// ║    • order.Tif         = "GTC" | "DAY" | etc.                            ║
// ║                                                                           ║
// ║  Reference: https://interactivebrokers.github.io/tws-api/               ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// Stage 1: Wait for price >= BreakoutLevel
// Stage 2: Wait for price <= PullbackLevel  
// Stage 3: Wait for price >= VWAP
// Stage 4: Enter Long
//
// ============================================================================

using IBApi;
using IdiotProof.Enums;
using IdiotProof.Models;
using System;
using IbContract = IBApi.Contract;

namespace IdiotProof.Strategy
{
    /// <summary>
    /// Executes the pullback strategy for a single symbol.
    /// Create one instance per symbol you want to trade.
    /// </summary>
    public sealed class PullbackRunner
    {
        private readonly SymbolConfig _config;
        private readonly IbContract _contract;
        private readonly IbWrapper _wrapper;
        private readonly EClientSocket _client;

        private SetupState _state;

        // VWAP accumulators
        private double _pvSum;
        private double _vSum;
        private double _lastPrice;
        private int _lastSize;

        // Track the highest/lowest prices (useful for debugging)
        private double _sessionHigh;
        private double _sessionLow = double.MaxValue;

        // Order tracking
        private int _entryOrderId;
        private bool _entryFilled;
        private double _entryFillPrice;

        /// <summary>
        /// Gets the current state of the strategy.
        /// </summary>
        public SetupState State => _state;

        /// <summary>
        /// Gets the symbol this runner is tracking.
        /// </summary>
        public string Symbol => _config.Symbol;

        /// <summary>
        /// Gets whether the strategy has completed (filled or done).
        /// </summary>
        public bool IsComplete => _state == SetupState.Done;

        /// <summary>
        /// Gets the current VWAP value.
        /// </summary>
        public double CurrentVwap => GetVwap();

        /// <summary>
        /// Gets the last traded price.
        /// </summary>
        public double LastPrice => _lastPrice;

        public PullbackRunner(SymbolConfig config, IbContract contract, IbWrapper wrapper, EClientSocket client)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _contract = contract ?? throw new ArgumentNullException(nameof(contract));
            _wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
            _client = client ?? throw new ArgumentNullException(nameof(client));

            _state = SetupState.WaitingForBreakout;

            // Listen for fills
            _wrapper.OnOrderFill += OnOrderFill;
        }

        /// <summary>
        /// Call this method when a new trade tick is received.
        /// </summary>
        public void OnLastTrade(double lastPrice, int lastSize)
        {
            // Update local last
            _lastPrice = lastPrice;
            _lastSize = lastSize;

            // Ignore bogus data
            if (lastPrice <= 0 || lastSize <= 0)
                return;

            // Update session high/low
            if (lastPrice > _sessionHigh)
                _sessionHigh = lastPrice;
            if (lastPrice < _sessionLow)
                _sessionLow = lastPrice;

            // Update VWAP accumulators: VWAP = sum(price * size) / sum(size)
            _pvSum += lastPrice * lastSize;
            _vSum += lastSize;

            double vwap = GetVwap();

            // State machine
            switch (_state)
            {
                case SetupState.WaitingForBreakout:
                    HandleWaitingForBreakout(vwap);
                    break;

                case SetupState.WaitingForPullback:
                    HandleWaitingForPullback(vwap);
                    break;

                case SetupState.WaitingForVwapReclaim:
                    HandleWaitingForVwapReclaim(vwap);
                    break;

                case SetupState.OrderSubmitted:
                case SetupState.Done:
                    // Wait for fills or do nothing
                    break;
            }
        }

        private double GetVwap()
        {
            if (_vSum <= 0) return 0;
            return _pvSum / _vSum;
        }

        /// <summary>
        /// Stage 1: Wait for price to break out above BreakoutLevel
        /// </summary>
        private void HandleWaitingForBreakout(double vwap)
        {
            if (_lastPrice >= _config.BreakoutLevel)
            {
                Console.WriteLine($"[{_config.Symbol}] ✓ STAGE 1 COMPLETE: Breakout detected!");
                Console.WriteLine($"    Last={_lastPrice:F2} >= Breakout={_config.BreakoutLevel:F2} | VWAP={vwap:F2}");
                _state = SetupState.WaitingForPullback;
            }
        }

        /// <summary>
        /// Stage 2: Wait for price to pull back to PullbackLevel
        /// </summary>
        private void HandleWaitingForPullback(double vwap)
        {
            if (_lastPrice <= _config.PullbackLevel)
            {
                Console.WriteLine($"[{_config.Symbol}] ✓ STAGE 2 COMPLETE: Pullback detected!");
                Console.WriteLine($"    Last={_lastPrice:F2} <= Pullback={_config.PullbackLevel:F2} | VWAP={vwap:F2}");
                _state = SetupState.WaitingForVwapReclaim;
            }
        }

        /// <summary>
        /// Stage 3: Wait for price to reclaim VWAP (price >= VWAP + buffer)
        /// </summary>
        private void HandleWaitingForVwapReclaim(double vwap)
        {
            if (vwap <= 0)
                return;

            double vwapTrigger = vwap + _config.VwapBuffer;

            if (_lastPrice >= vwapTrigger)
            {
                Console.WriteLine($"[{_config.Symbol}] ✓ STAGE 3 COMPLETE: VWAP reclaimed!");
                Console.WriteLine($"    Last={_lastPrice:F2} >= VWAP+Buffer={vwapTrigger:F2} | VWAP={vwap:F2}");
                SubmitEntry(vwap);
                _state = SetupState.OrderSubmitted;
            }
        }

        private void SubmitEntry(double vwap)
        {
            _entryOrderId = _wrapper.ConsumeNextOrderId();

            var entry = new Order
            {
                Action = "BUY",
                OrderType = _config.UseLimitEntry ? "LMT" : "MKT",
                TotalQuantity = _config.Quantity,
                OutsideRth = _config.AllowOutsideRth,
                Tif = _config.TimeInForce
            };

            if (_config.UseLimitEntry)
            {
                entry.LmtPrice = Math.Round(vwap + _config.LimitOffset, 2);
                Console.WriteLine($"[{_config.Symbol}] → Submitting LIMIT BUY {_config.Quantity} @ {entry.LmtPrice:F2}");
            }
            else
            {
                Console.WriteLine($"[{_config.Symbol}] → Submitting MARKET BUY {_config.Quantity}");
            }

            Console.WriteLine($"    TIF={_config.TimeInForce} | OutsideRTH={_config.AllowOutsideRth} | OrderId={_entryOrderId}");

            _client.placeOrder(_entryOrderId, _contract, entry);
        }

        private void OnOrderFill(int orderId, double fillPrice, int fillSize)
        {
            if (orderId != _entryOrderId)
                return;

            _entryFilled = true;
            _entryFillPrice = fillPrice;

            Console.WriteLine($"[{_config.Symbol}] ★ ENTRY FILLED!");
            Console.WriteLine($"    OrderId={orderId} | FillPrice={fillPrice:F2} | FillSize={fillSize}");

            if (_config.EnableTakeProfit)
            {
                SubmitTakeProfit(fillPrice);
            }

            _state = SetupState.Done;
        }

        private void SubmitTakeProfit(double entryFillPrice)
        {
            int tpOrderId = _wrapper.ConsumeNextOrderId();
            double tpPrice = Math.Round(entryFillPrice + _config.TakeProfitOffset, 2);

            var tp = new Order
            {
                Action = "SELL",
                OrderType = "LMT",
                TotalQuantity = _config.Quantity,
                LmtPrice = tpPrice,
                OutsideRth = _config.AllowOutsideRth,
                Tif = _config.TimeInForce
            };

            Console.WriteLine($"[{_config.Symbol}] → Submitting TAKE PROFIT SELL {_config.Quantity} @ {tpPrice:F2}");
            Console.WriteLine($"    TIF={_config.TimeInForce} | OutsideRTH={_config.AllowOutsideRth} | OrderId={tpOrderId}");

            _client.placeOrder(tpOrderId, _contract, tp);
        }

        /// <summary>
        /// Unsubscribes from events. Call when disposing.
        /// </summary>
        public void Dispose()
        {
            _wrapper.OnOrderFill -= OnOrderFill;
        }
    }
}
