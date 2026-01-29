// ============================================================================
// Strategy Runner - Executes multi-step strategies
// ============================================================================

using IBApi;
using System;
using System.Threading;
using IbContract = IBApi.Contract;

namespace IdiotProof.Models
{
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

        // Cancel timer
        private Timer? _cancelTimer;

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

        /// <summary>Gets the current condition name.</summary>
        public string CurrentConditionName => 
            _currentConditionIndex < _strategy.Conditions.Count 
                ? _strategy.Conditions[_currentConditionIndex].Name 
                : "Complete";

        public StrategyRunner(TradingStrategy strategy, IbContract contract, IbWrapper wrapper, EClientSocket client)
        {
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _contract = contract ?? throw new ArgumentNullException(nameof(contract));
            _wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
            _client = client ?? throw new ArgumentNullException(nameof(client));

            _currentConditionIndex = 0;

            // Subscribe to fill events
            _wrapper.OnOrderFill += OnOrderFill;
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
                    Console.WriteLine();
                    Console.WriteLine($"[{_strategy.Symbol}] *** ALL CONDITIONS MET - Executing order...");
                    Console.WriteLine($"    Price={price:F2} | VWAP={vwap:F2}");
                    ExecuteOrder(vwap);
                }
                else
                {
                    // Print updated progress
                    Console.WriteLine();
                    _strategy.WriteProgress(_currentConditionIndex, _entryFilled, _takeProfitFilled, _lastPrice, _entryFillPrice, _takeProfitTarget);
                    Console.WriteLine($"    VWAP={vwap:F2}");
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

            // Set limit price
            if (order.Type == OrderType.Limit)
            {
                ibOrder.LmtPrice = order.LimitPrice.HasValue 
                    ? Math.Round(order.LimitPrice.Value, 2)
                    : Math.Round(vwap + order.LimitOffset, 2);
            }

            string priceStr = order.Type == OrderType.Limit ? $"@ {ibOrder.LmtPrice:F2}" : "@ MKT";
            Console.WriteLine($"[{_strategy.Symbol}] → Submitting {order.Side} {order.Quantity} {priceStr}");
            Console.WriteLine($"    TIF={order.TimeInForce} | OutsideRTH={order.OutsideRth} | OrderId={_entryOrderId}");

            _client.placeOrder(_entryOrderId, _contract, ibOrder);
        }

        private void OnOrderFill(int orderId, double fillPrice, int fillSize)
        {
            // Check for entry order fill
            if (orderId == _entryOrderId && !_entryFilled)
            {
                _entryFilled = true;
                _entryFillPrice = fillPrice;

                Console.WriteLine($"[{_strategy.Symbol}] ORDER FILLED!");
                Console.WriteLine($"    OrderId={orderId} | FillPrice={fillPrice:F2} | FillSize={fillSize}");

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

                // Show monitoring step progress
                Console.WriteLine();
                _strategy.WriteProgress(_currentConditionIndex, _entryFilled, _takeProfitFilled, _lastPrice, _entryFillPrice, _takeProfitTarget);
                return;
            }

            // Check for take profit order fill
            if (orderId == _takeProfitOrderId && !_takeProfitFilled)
            {
                _takeProfitFilled = true;
                _isComplete = true;

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[{_strategy.Symbol}] *** TAKE PROFIT FILLED! ***");
                Console.ResetColor();
                Console.WriteLine($"    OrderId={orderId} | FillPrice={fillPrice:F2} | FillSize={fillSize}");
                Console.WriteLine($"    Entry={_entryFillPrice:F2} -> Exit={fillPrice:F2} | P&L=${(_strategy.Order.Quantity * (fillPrice - _entryFillPrice)):F2}");

                // Show final progress with green take profit step
                Console.WriteLine();
                _strategy.WriteProgress(_currentConditionIndex, _entryFilled, _takeProfitFilled, _lastPrice, _entryFillPrice, _takeProfitTarget);
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

            Console.WriteLine($"[{_strategy.Symbol}] → Submitting TAKE PROFIT SELL {order.Quantity} @ {tpPrice:F2}");
            Console.WriteLine($"    OutsideRTH={order.TakeProfitOutsideRth} | OrderId={_takeProfitOrderId}");

            _client.placeOrder(_takeProfitOrderId, _contract, tpOrder);

            // Schedule cancellation if CancelTakeProfitAt is set
            if (order.CancelTakeProfitAt.HasValue)
            {
                ScheduleTakeProfitCancellation(order.CancelTakeProfitAt.Value);
            }
        }

        private void ScheduleTakeProfitCancellation(TimeOnly cancelTime)
        {
            var now = DateTime.Now;
            var cancelDateTime = now.Date.Add(cancelTime.ToTimeSpan());

            // If cancel time already passed today, don't schedule
            if (cancelDateTime <= now)
            {
                Console.WriteLine($"[{_strategy.Symbol}] WARNING: Cancel time {cancelTime} already passed, no auto-cancel scheduled");
                return;
            }

            var delay = cancelDateTime - now;
            Console.WriteLine($"[{_strategy.Symbol}] TIMER: Take profit will auto-cancel at {cancelTime} ({delay.TotalMinutes:F0} min from now)");

            _cancelTimer = new Timer(CancelTakeProfitCallback, null, delay, Timeout.InfiniteTimeSpan);
        }

        private void CancelTakeProfitCallback(object? state)
        {
            if (_disposed || _takeProfitFilled || _takeProfitCancelled || _takeProfitOrderId < 0)
                return;

            _takeProfitCancelled = true;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[{_strategy.Symbol}] TIMER: CANCELLING TAKE PROFIT - Time limit reached");
            Console.ResetColor();
            Console.WriteLine($"    OrderId={_takeProfitOrderId} | Price was ${_lastPrice:F2}");

            _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
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

            Console.WriteLine($"[{_strategy.Symbol}] → Submitting STOP LOSS @ {slPrice:F2}");
            Console.WriteLine($"    OrderId={slOrderId}");

            _client.placeOrder(slOrderId, _contract, slOrder);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cancelTimer?.Dispose();
            _cancelTimer = null;

            _wrapper.OnOrderFill -= OnOrderFill;
        }
    }
}
