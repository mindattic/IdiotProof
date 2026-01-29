// ============================================================================
// Strategy Runner - Executes multi-step strategies
// ============================================================================

using IBApi;
using IdiotProof;
using System;
using System.Threading;
using IbContract = IBApi.Contract;

namespace IdiotProof.Models
{
    /// <summary>
    /// Possible outcomes for a strategy.
    /// </summary>
    public enum StrategyResult
    {
        /// <summary>Strategy is still running.</summary>
        Running,
        /// <summary>Conditions were never met - no position taken.</summary>
        NeverBought,
        /// <summary>Position taken and take profit was filled.</summary>
        TakeProfitFilled,
        /// <summary>Position taken, time expired, exited with profit.</summary>
        ExitedWithProfit,
        /// <summary>Position taken, time expired, cancelled TP (holding position).</summary>
        TakeProfitCancelled,
        /// <summary>Entry order was cancelled before fill.</summary>
        EntryCancelled
    }

    /// <summary>
    /// Executes a multi-step strategy for a single symbol.
    /// Monitors price and VWAP, evaluates conditions in sequence,
    /// and places orders when all conditions are met.
    /// </summary>
    public sealed class StrategyRunner : IDisposable
    {
        private readonly TradingStrategy _strategy;
        private readonly IbContract _contract;
        private readonly IbWrapper _wrapper;
        private readonly EClientSocket _client;

        private int _currentConditionIndex;
        private bool _isComplete;
        private bool _disposed;

        // VWAP accumulators
        private double _pvSum;
        private double _vSum;
        private double _lastPrice;

        // Session tracking
        private double _sessionHigh;
        private double _sessionLow = double.MaxValue;

        // Order tracking
        private int _entryOrderId = -1;
        private bool _entryFilled;
        private double _entryFillPrice;

        // Take profit tracking
        private int _takeProfitOrderId = -1;
        private bool _takeProfitFilled;
        private bool _takeProfitCancelled;
        private double _takeProfitTarget;

        // Exit tracking
        private bool _exitedWithProfit;
        private double _exitFillPrice;

        // Cancel timer
        private Timer? _cancelTimer;

        // Result tracking
        private StrategyResult _result = StrategyResult.Running;

        /// <summary>Gets the strategy being executed.</summary>
        public TradingStrategy Strategy => _strategy;

        /// <summary>Gets the symbol being traded.</summary>
        public string Symbol => _strategy.Symbol;

        /// <summary>Gets whether all conditions have been met and order executed.</summary>
        public bool IsComplete => _isComplete;

        /// <summary>Gets the current step index (0-based).</summary>
        public int CurrentStep => _currentConditionIndex;

        /// <summary>Gets the total number of conditions.</summary>
        public int TotalSteps => _strategy.Conditions.Count;

        /// <summary>Gets whether the entry order has been filled.</summary>
        public bool EntryFilled => _entryFilled;

        /// <summary>Gets the entry fill price.</summary>
        public double EntryFillPrice => _entryFillPrice;

        /// <summary>Gets whether the take profit order has been filled.</summary>
        public bool TakeProfitFilled => _takeProfitFilled;

        /// <summary>Gets the take profit target price.</summary>
        public double TakeProfitTarget => _takeProfitTarget;

        /// <summary>Gets the current VWAP value.</summary>
        public double CurrentVwap => GetVwap();

        /// <summary>Gets the last traded price.</summary>
        public double LastPrice => _lastPrice;

        /// <summary>Gets the final result of the strategy.</summary>
        public StrategyResult Result => _result;

        /// <summary>Gets the current condition name.</summary>
        public string CurrentConditionName => 
            _currentConditionIndex < _strategy.Conditions.Count 
                ? _strategy.Conditions[_currentConditionIndex].Name 
                : "Complete";

        /// <summary>
        /// Logs a timestamped message to the console.
        /// </summary>
        private void Log(string message, ConsoleColor? color = null)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            if (color.HasValue)
            {
                Console.ForegroundColor = color.Value;
            }
            Console.WriteLine($"[{timestamp}] [{_strategy.Symbol}] {message}");
            if (color.HasValue)
            {
                Console.ResetColor();
            }
        }

        public StrategyRunner(TradingStrategy strategy, IbContract contract, IbWrapper wrapper, EClientSocket client)
        {
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _contract = contract ?? throw new ArgumentNullException(nameof(contract));
            _wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
            _client = client ?? throw new ArgumentNullException(nameof(client));

            _currentConditionIndex = 0;

            // Subscribe to fill events
            _wrapper.OnOrderFill += OnOrderFill;

            Log("Strategy initialized - waiting for market data...", ConsoleColor.DarkGray);
        }

        /// <summary>
        /// Call this method when a new trade tick is received.
        /// </summary>
        public void OnLastTrade(double lastPrice, int lastSize)
        {
            if (_disposed)
                return;

            _lastPrice = lastPrice;

            // Ignore invalid data
            if (lastPrice <= 0 || lastSize <= 0)
                return;

            // Update session high/low
            if (lastPrice > _sessionHigh)
                _sessionHigh = lastPrice;
            if (lastPrice < _sessionLow)
                _sessionLow = lastPrice;

            // Update VWAP: sum(price * size) / sum(size)
            _pvSum += lastPrice * lastSize;
            _vSum += lastSize;

            double vwap = GetVwap();

            // Evaluate current condition
            EvaluateConditions(lastPrice, vwap);
        }

        private double GetVwap()
        {
            return _vSum > 0 ? _pvSum / _vSum : 0;
        }

        private void EvaluateConditions(double price, double vwap)
        {
            if (_currentConditionIndex >= _strategy.Conditions.Count)
                return;

            var condition = _strategy.Conditions[_currentConditionIndex];

            if (condition.Evaluate(price, vwap))
            {
                _currentConditionIndex++;

                // Check if all conditions are met
                if (_currentConditionIndex >= _strategy.Conditions.Count)
                {
                    Log($"[OK] STEP {_currentConditionIndex}/{_strategy.Conditions.Count}: {condition.Name} - TRIGGERED @ ${price:F2}", ConsoleColor.Green);
                    Log($"*** ALL CONDITIONS MET - Executing order... (VWAP=${vwap:F2})", ConsoleColor.Cyan);
                    ExecuteOrder(vwap);
                }
                else
                {
                    var nextCondition = _strategy.Conditions[_currentConditionIndex];
                    Log($"[OK] STEP {_currentConditionIndex}/{_strategy.Conditions.Count}: {condition.Name} - TRIGGERED @ ${price:F2}", ConsoleColor.Green);
                    Log($"  -> Next: {nextCondition.Name} (VWAP=${vwap:F2})", ConsoleColor.DarkGray);
                }
            }
        }

        private void ExecuteOrder(double vwap)
        {
            var order = _strategy.Order;
            _entryOrderId = _wrapper.ConsumeNextOrderId();

            var ibOrder = new Order
            {
                Action = order.GetIbAction(),
                OrderType = order.GetIbOrderType(),
                TotalQuantity = order.Quantity,
                OutsideRth = order.OutsideRth,
                Tif = order.GetIbTif()
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(Settings.IB_ACCOUNT))
            {
                ibOrder.Account = Settings.IB_ACCOUNT;
            }

            // Set limit price
            if (order.Type == OrderType.Limit)
            {
                ibOrder.LmtPrice = order.LimitPrice.HasValue 
                    ? Math.Round(order.LimitPrice.Value, 2)
                    : Math.Round(vwap + order.LimitOffset, 2);
            }

            string priceStr = order.Type == OrderType.Limit ? $"@ ${ibOrder.LmtPrice:F2}" : "@ MKT";
            Log($">> SUBMITTING {order.Side} {order.Quantity} shares {priceStr}", ConsoleColor.Yellow);
            Log($"  OrderId={_entryOrderId} | TIF={order.TimeInForce} | OutsideRTH={order.OutsideRth}", ConsoleColor.DarkGray);

            _client.placeOrder(_entryOrderId, _contract, ibOrder);
        }

        private void OnOrderFill(int orderId, double fillPrice, int fillSize)
        {
            // Check for entry order fill
            if (orderId == _entryOrderId && !_entryFilled)
            {
                _entryFilled = true;
                _entryFillPrice = fillPrice;

                Log($"[OK] ENTRY FILLED @ ${fillPrice:F2} ({fillSize} shares)", ConsoleColor.Green);

                // Handle take profit
                if (_strategy.Order.Side == OrderSide.Buy && _strategy.Order.EnableTakeProfit)
                {
                    SubmitTakeProfit(fillPrice);
                }

                // Handle stop loss
                if (_strategy.Order.EnableStopLoss)
                {
                    SubmitStopLoss(fillPrice);
                }

                Log($"  Monitoring position... Entry=${fillPrice:F2}", ConsoleColor.DarkGray);
                return;
            }

            // Check for take profit order fill (or early exit fill)
            if (orderId == _takeProfitOrderId && !_takeProfitFilled)
            {
                _takeProfitFilled = true;
                _isComplete = true;
                _exitFillPrice = fillPrice;

                double pnl = _strategy.Order.Quantity * (fillPrice - _entryFillPrice);

                // Determine if this was the original TP or an early exit
                if (_exitedWithProfit)
                {
                    _result = StrategyResult.ExitedWithProfit;
                    Log($"*** EXITED WITH PROFIT (time limit) @ ${fillPrice:F2} | P&L: ${pnl:F2}", ConsoleColor.Cyan);
                }
                else
                {
                    _result = StrategyResult.TakeProfitFilled;
                    Log($"*** TAKE PROFIT FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2}", ConsoleColor.Green);
                }

                // Show final result
                PrintFinalResult();
            }
        }

        private void SubmitTakeProfit(double entryPrice)
        {
            var order = _strategy.Order;
            _takeProfitOrderId = _wrapper.ConsumeNextOrderId();

            double tpPrice = order.TakeProfitPrice.HasValue
                ? Math.Round(order.TakeProfitPrice.Value, 2)
                : Math.Round(entryPrice + order.TakeProfitOffset, 2);

            _takeProfitTarget = tpPrice;

            var tpOrder = new Order
            {
                Action = "SELL",
                OrderType = "LMT",
                TotalQuantity = order.Quantity,
                LmtPrice = tpPrice,
                OutsideRth = order.TakeProfitOutsideRth,
                Tif = order.GetIbTif()
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(Settings.IB_ACCOUNT))
            {
                tpOrder.Account = Settings.IB_ACCOUNT;
            }

            Log($">> SUBMITTING TAKE PROFIT SELL {order.Quantity} @ ${tpPrice:F2}", ConsoleColor.Yellow);
            Log($"  OrderId={_takeProfitOrderId} | OutsideRTH={order.TakeProfitOutsideRth}", ConsoleColor.DarkGray);

            _client.placeOrder(_takeProfitOrderId, _contract, tpOrder);

            // Schedule cancellation if EndTime is set
            if (order.EndTime.HasValue)
            {
                ScheduleTakeProfitCancellation(order.EndTime.Value);
            }
        }

        private void ScheduleTakeProfitCancellation(TimeOnly cancelTime)
        {
            var now = DateTime.Now;
            var cancelDateTime = now.Date.Add(cancelTime.ToTimeSpan());

            // If cancel time already passed today, don't schedule
            if (cancelDateTime <= now)
            {
                Log($"WARNING: End time {cancelTime} already passed, no auto-exit scheduled", ConsoleColor.Red);
                return;
            }

            var delay = cancelDateTime - now;
            Log($"TIMER SET: Auto-exit at {cancelTime} ({delay.TotalMinutes:F0} min from now)", ConsoleColor.Magenta);

            _cancelTimer = new Timer(CancelTakeProfitCallback, null, delay, Timeout.InfiniteTimeSpan);
        }

        private void CancelTakeProfitCallback(object? state)
        {
            if (_disposed || _takeProfitFilled || _takeProfitCancelled || _takeProfitOrderId < 0)
                return;

            _takeProfitCancelled = true;

            // Check if position is profitable
            bool isLong = _strategy.Order.Side == OrderSide.Buy;
            bool isProfitable = isLong ? _lastPrice > _entryFillPrice : _lastPrice < _entryFillPrice;

            if (_entryFilled && isProfitable)
            {
                double unrealizedPnl = _strategy.Order.Quantity * (_lastPrice - _entryFillPrice);

                Log($"TIME LIMIT REACHED - Position is profitable!", ConsoleColor.Cyan);
                Log($"  Entry=${_entryFillPrice:F2} | Current=${_lastPrice:F2} | Unrealized P&L=${unrealizedPnl:F2}", ConsoleColor.Cyan);
                Log($"  Cancelling TP order #{_takeProfitOrderId} and selling at limit...", ConsoleColor.Yellow);

                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());

                // Mark that we're exiting early with profit
                _exitedWithProfit = true;

                // Submit limit sell order slightly below current price for quick fill in premarket
                SubmitLimitExit();
            }
            else
            {
                Log($"TIME LIMIT REACHED - Position NOT profitable", ConsoleColor.Yellow);
                Log($"  Entry=${_entryFillPrice:F2} | Current=${_lastPrice:F2} | Cancelling TP order", ConsoleColor.Yellow);
                Log($"  WARNING: STILL HOLDING {_strategy.Order.Quantity} SHARES - manage manually!", ConsoleColor.Red);

                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());

                // Set result - still holding position
                _result = StrategyResult.TakeProfitCancelled;
                _isComplete = true;
                PrintFinalResult();
            }
        }

        private void SubmitLimitExit()
        {
            var order = _strategy.Order;
            int exitOrderId = _wrapper.ConsumeNextOrderId();

            string action = order.Side == OrderSide.Buy ? "SELL" : "BUY";

            // Set limit price slightly below current price for sells (or above for buys) to ensure quick fill in premarket
            double offset = 0.02;
            double limitPrice = order.Side == OrderSide.Buy
                ? Math.Round(_lastPrice - offset, 2)  // Selling long position: slightly below current
                : Math.Round(_lastPrice + offset, 2); // Covering short position: slightly above current

            var exitOrder = new Order
            {
                Action = action,
                OrderType = "LMT",
                LmtPrice = limitPrice,
                TotalQuantity = order.Quantity,
                OutsideRth = order.TakeProfitOutsideRth,
                Tif = "GTC"
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(Settings.IB_ACCOUNT))
            {
                exitOrder.Account = Settings.IB_ACCOUNT;
            }

            Log($">> SUBMITTING LIMIT EXIT {action} {order.Quantity} @ ${limitPrice:F2}", ConsoleColor.Yellow);
            Log($"  OrderId={exitOrderId} | OutsideRTH={order.TakeProfitOutsideRth}", ConsoleColor.DarkGray);

            // Update take profit order ID to track the exit fill
            _takeProfitOrderId = exitOrderId;
            _takeProfitCancelled = false; // Reset so we can track the fill

            _client.placeOrder(exitOrderId, _contract, exitOrder);
        }

        private void SubmitStopLoss(double entryPrice)
        {
            var order = _strategy.Order;
            int slOrderId = _wrapper.ConsumeNextOrderId();

            double slPrice = order.StopLossPrice.HasValue
                ? Math.Round(order.StopLossPrice.Value, 2)
                : Math.Round(entryPrice - order.StopLossOffset, 2);

            var slOrder = new Order
            {
                Action = order.Side == OrderSide.Buy ? "SELL" : "BUY",
                OrderType = "STP",
                TotalQuantity = order.Quantity,
                AuxPrice = slPrice,
                OutsideRth = order.OutsideRth,
                Tif = order.GetIbTif()
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(Settings.IB_ACCOUNT))
            {
                slOrder.Account = Settings.IB_ACCOUNT;
            }

            Log($">> SUBMITTING STOP LOSS @ ${slPrice:F2}", ConsoleColor.Yellow);
            Log($"  OrderId={slOrderId}", ConsoleColor.DarkGray);

            _client.placeOrder(slOrderId, _contract, slOrder);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cancelTimer?.Dispose();
            _cancelTimer = null;

            _wrapper.OnOrderFill -= OnOrderFill;

            // If strategy never completed, determine final result
            if (_result == StrategyResult.Running)
            {
                if (!_entryFilled)
                {
                    _result = StrategyResult.NeverBought;
                }
                PrintFinalResult();
            }

            Log("Strategy disposed", ConsoleColor.DarkGray);
        }

        private void PrintFinalResult()
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.WriteLine();
            Console.WriteLine($"[{timestamp}] +===============================================================+");

            switch (_result)
            {
                case StrategyResult.TakeProfitFilled:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: *** TAKE PROFIT FILLED ***");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${(_strategy.Order.Quantity * (_exitFillPrice - _entryFillPrice)):F2}");
                    break;

                case StrategyResult.ExitedWithProfit:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: *** EXITED WITH PROFIT (time limit) ***");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${(_strategy.Order.Quantity * (_exitFillPrice - _entryFillPrice)):F2}");
                    break;

                case StrategyResult.TakeProfitCancelled:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: TAKE PROFIT CANCELLED (not profitable)");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} | Current: ${_lastPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  WARNING: STILL HOLDING {_strategy.Order.Quantity} SHARES - MANAGE MANUALLY!");
                    break;

                case StrategyResult.NeverBought:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: NEVER BOUGHT");
                    Console.WriteLine($"[{timestamp}] |  Conditions not met - no position taken");
                    break;

                case StrategyResult.EntryCancelled:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: ENTRY CANCELLED");
                    Console.WriteLine($"[{timestamp}] |  Entry order was cancelled before fill");
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: {_result}");
                    break;
            }

            Console.ResetColor();
            Console.WriteLine($"[{timestamp}] +===============================================================+");
        }
    }
}
