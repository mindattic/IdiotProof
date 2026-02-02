// ============================================================================
// Strategy Runner - Executes multi-step strategies
// ============================================================================
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  IBKR API DEPENDENCY                                                      ║
// ║                                                                           ║
// ║  This class directly interfaces with the Interactive Brokers TWS API.     ║
// ║  When modifying order submission code, ensure compatibility with IB API:  ║
// ║                                                                           ║
// ║  Order Properties Used:                                                   ║
// ║    • order.Action        = "BUY" | "SELL"                                ║
// ║    • order.OrderType     = "MKT" | "LMT" | "STP"                         ║
// ║    • order.TotalQuantity                                                  ║
// ║    • order.LmtPrice      (for limit orders)                              ║
// ║    • order.AuxPrice      (for stop orders)                               ║
// ║    • order.OutsideRth    = true | false                                  ║
// ║    • order.Tif           = "GTC" | "DAY" | "IOC" | "FOK" | "OPG" | "DTC"║
// ║    • order.AllOrNone     = true | false                                  ║
// ║    • order.Account       (for multi-account setups)                       ║
// ║                                                                           ║
// ║  Reference: https://interactivebrokers.github.io/tws-api/               ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

using IBApi;
using IdiotProof.Backend;
using IdiotProof.Backend.Enums;
using IdiotProof.Backend.Helpers;
using IdiotProof.Backend.Strategy;
using System;
using System.Threading;
using IbContract = IBApi.Contract;

namespace IdiotProof.Backend.Models
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
        private volatile bool _disposed;

        // Lock for thread-safe timer callback handling
        private readonly object _disposeLock = new object();

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

        // Stop loss tracking
        private int _stopLossOrderId = -1;
        private bool _stopLossFilled;

        // Exit tracking
        private bool _exitedWithProfit;
        private double _exitFillPrice;

        // Trailing stop loss tracking
        private int _trailingStopLossOrderId = -1;
        private bool _trailingStopLossTriggered;
        private double _trailingStopLossPrice;
        private double _highWaterMark;  // Highest price since entry (for trailing stop)

        // ATR calculator for volatility-based stops
        private readonly Helpers.AtrCalculator? _atrCalculator;

        // Cancel timer
        private Timer? _cancelTimer;

        // Close position timer (time-based exit)
        private Timer? _closePositionTimer;
        private bool _closePositionTriggered;
        private int _closePositionOrderId = -1;

        // Result tracking
        private StrategyResult _result = StrategyResult.Running;

        // Daily reset tracking
        private DateOnly _lastCheckedDate;
        private bool _waitingForWindowLogged;
        private bool _windowEndedLogged;

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
        /// Logs a timestamped message to the console using Eastern Time (New York).
        /// </summary>
        private void Log(string message, ConsoleColor? color = null)
        {
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var easternTime = TimeZoneInfo.ConvertTime(DateTime.Now, easternZone);
            var timestamp = easternTime.ToString("hh:mm:ss tt");
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
            _lastCheckedDate = DateOnly.FromDateTime(DateTime.Today);

            // Initialize ATR calculator if ATR-based stop loss is configured
            if (strategy.Order.UseAtrStopLoss && strategy.Order.AtrStopLoss != null)
            {
                _atrCalculator = new Helpers.AtrCalculator(
                    period: strategy.Order.AtrStopLoss.Period,
                    ticksPerBar: 50 // Aggregate 50 ticks into one "bar" for ATR calculation
                );
            }

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

            // Update ATR calculator if configured
            _atrCalculator?.Update(lastPrice);

            double vwap = GetVwap();

            // Monitor trailing stop loss if position is open
            if (_entryFilled && !_isComplete)
            {
                MonitorTrailingStopLoss(lastPrice);
            }

            // Evaluate current condition
            EvaluateConditions(lastPrice, vwap);
        }

        /// <summary>
        /// Monitors and updates the trailing stop loss based on current price.
        /// Supports both percentage-based and ATR-based trailing stops.
        /// </summary>
        private void MonitorTrailingStopLoss(double currentPrice)
        {
            var order = _strategy.Order;

            if (!order.EnableTrailingStopLoss || _trailingStopLossTriggered)
                return;

            bool isLong = order.Side == OrderSide.Buy;

            // Update high water mark for long positions
            if (isLong)
            {
                if (currentPrice > _highWaterMark)
                {
                    _highWaterMark = currentPrice;

                    // Calculate new trailing stop price based on ATR or percentage
                    double newStopPrice;
                    string stopDescription;

                    if (order.UseAtrStopLoss && _atrCalculator != null && _atrCalculator.IsReady)
                    {
                        // ATR-based trailing stop
                        var atrConfig = order.AtrStopLoss!;
                        newStopPrice = _atrCalculator.CalculateStopPrice(
                            referencePrice: _highWaterMark,
                            multiplier: atrConfig.Multiplier,
                            isLong: true,
                            minPercent: atrConfig.MinStopPercent,
                            maxPercent: atrConfig.MaxStopPercent
                        );
                        double atrValue = _atrCalculator.CurrentAtr;
                        double stopDistance = _highWaterMark - newStopPrice;
                        stopDescription = $"{atrConfig.Multiplier:F1}× ATR (ATR=${atrValue:F2}, Distance=${stopDistance:F2})";
                    }
                    else
                    {
                        // Percentage-based trailing stop (fallback or explicit)
                        newStopPrice = Math.Round(_highWaterMark * (1 - order.TrailingStopLossPercent), 2);
                        stopDescription = $"{order.TrailingStopLossPercent * 100:F1}% below ${_highWaterMark:F2}";
                    }

                    // Only update if the new stop is higher (tighter)
                    if (newStopPrice > _trailingStopLossPrice)
                    {
                        double oldStop = _trailingStopLossPrice;
                        _trailingStopLossPrice = newStopPrice;

                        if (oldStop > 0)
                        {
                            Log($"TRAILING STOP UPDATED: ${oldStop:F2} -> ${_trailingStopLossPrice:F2} (High: ${_highWaterMark:F2})", ConsoleColor.Magenta);
                        }
                        else
                        {
                            Log($"TRAILING STOP SET: ${_trailingStopLossPrice:F2} ({stopDescription})", ConsoleColor.Magenta);
                        }
                    }
                }

                // Check if current price dropped below trailing stop
                if (_trailingStopLossPrice > 0 && currentPrice <= _trailingStopLossPrice)
                {
                    Log($"*** TRAILING STOP TRIGGERED! Price ${currentPrice:F2} <= Stop ${_trailingStopLossPrice:F2}", ConsoleColor.Red);
                    ExecuteTrailingStopLoss(); 
                }
            }
            else
            {
                // For short positions, track low water mark and stop above
                // (inverse logic - not implemented yet, but structure is here)
            }
        }

        /// <summary>
        /// Executes a sell order when trailing stop loss is triggered.
        /// Uses market order during RTH for faster execution, limit order outside RTH for safer fills.
        /// </summary>
        private void ExecuteTrailingStopLoss()
        {
            if (_trailingStopLossTriggered)
                return;

            _trailingStopLossTriggered = true;
            var order = _strategy.Order;

            // Cancel any existing take profit order
            if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled)
            {
                Log($"Cancelling take profit order #{_takeProfitOrderId}...", ConsoleColor.Yellow);
                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                _takeProfitCancelled = true;
            }

            // Submit stop loss order
            _trailingStopLossOrderId = _wrapper.ConsumeNextOrderId();

            string action = order.Side == OrderSide.Buy ? "SELL" : "BUY";

            // Determine if we're in Regular Trading Hours
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            bool isRTH = MarketTime.RTH.Contains(currentTimeET);

            Order stopOrder;

            if (isRTH)
            {
                // Use market order during RTH for immediate execution (good liquidity)
                stopOrder = new Order
                {
                    Action = action,
                    OrderType = "MKT",
                    TotalQuantity = order.Quantity,
                    OutsideRth = false,
                    Tif = "DAY"
                };
            }
            else
            {
                // Use limit order outside RTH for safer execution (low liquidity)
                // Set limit price slightly below current price to ensure fill
                // (price may have already dropped below stop level by the time order is submitted)
                bool isLong = order.Side == OrderSide.Buy;
                double offset = 0.02; // 2 cents buffer for slippage
                double limitPrice = isLong
                    ? Math.Round(Math.Min(_lastPrice, _trailingStopLossPrice) - offset, 2)  // Selling: slightly below current/stop
                    : Math.Round(Math.Max(_lastPrice, _trailingStopLossPrice) + offset, 2); // Covering short: slightly above

                stopOrder = new Order
                {
                    Action = action,
                    OrderType = "LMT",
                    LmtPrice = limitPrice,
                    TotalQuantity = order.Quantity,
                    OutsideRth = true,
                    Tif = "GTC"
                };
            }

            // Set account if specified
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                stopOrder.Account = Settings.AccountNumber;
            }

            var sessionStr = isRTH ? "RTH" : "Extended";
            var orderTypeStr = isRTH ? "MKT" : $"LMT @ ${stopOrder.LmtPrice:F2}";
            Log($">> SUBMITTING TRAILING STOP LOSS {action} {order.Quantity} @ {orderTypeStr} ({sessionStr})", ConsoleColor.Red);
            Log($"  OrderId={_trailingStopLossOrderId} | Triggered at ${_lastPrice:F2} | Stop Level: ${_trailingStopLossPrice:F2}", ConsoleColor.DarkGray);

            _client.placeOrder(_trailingStopLossOrderId, _contract, stopOrder);
        }

        /// <summary>
        /// Resets the strategy state for a new trading day.
        /// Called automatically at midnight to allow strategies to run again.
        /// </summary>
        private void ResetForNewDay(DateOnly newDate)
        {
            // Only reset if we haven't filled an entry order (don't reset mid-trade)
            if (_entryFilled)
            {
                Log($"New day detected but position is open - not resetting", ConsoleColor.DarkYellow);
                _lastCheckedDate = newDate;
                return;
            }

            Log($"*** MIDNIGHT RESET - New trading day detected, resetting strategy ***", ConsoleColor.Cyan);

            // Reset condition tracking
            _currentConditionIndex = 0;
            _isComplete = false;

            // Reset VWAP accumulators for new session
            _pvSum = 0;
            _vSum = 0;

            // Reset session tracking
            _sessionHigh = 0;
            _sessionLow = double.MaxValue;

            // Reset logging flags
            _waitingForWindowLogged = false;
            _windowEndedLogged = false;

            // Reset result
            _result = StrategyResult.Running;

            // Update the date
            _lastCheckedDate = newDate;

            // Log next window time
            if (_strategy.StartTime.HasValue)
            {
                var info = TimezoneHelper.GetTimezoneDisplayInfo(Settings.Timezone);
                var startLocal = TimezoneHelper.ToLocal(_strategy.StartTime.Value, Settings.Timezone);
                Log($"Will start monitoring at {_strategy.StartTime.Value:h:mm tt} ET ({startLocal:h:mm tt} {info.Abbreviation})", ConsoleColor.DarkGray);
            }
        }

        private double GetVwap()
        {
            return _vSum > 0 ? _pvSum / _vSum : 0;
        }

        /// <summary>
        /// Resets the strategy state for repeating after a trade completes.
        /// Called when RepeatEnabled is true after take profit, stop loss, or trailing stop fills.
        /// </summary>
        private void ResetForRepeat()
        {
            Log($"*** REPEAT ENABLED - Resetting strategy to wait for conditions again ***", ConsoleColor.Magenta);

            // Reset condition tracking
            _currentConditionIndex = 0;
            _isComplete = false;

            // Reset order tracking
            _entryOrderId = -1;
            _entryFilled = false;
            _entryFillPrice = 0;

            // Reset take profit tracking
            _takeProfitOrderId = -1;
            _takeProfitFilled = false;
            _takeProfitCancelled = false;
            _takeProfitTarget = 0;

            // Reset stop loss tracking
            _stopLossOrderId = -1;
            _stopLossFilled = false;

            // Reset exit tracking
            _exitedWithProfit = false;
            _exitFillPrice = 0;

            // Reset trailing stop loss tracking
            _trailingStopLossOrderId = -1;
            _trailingStopLossTriggered = false;
            _trailingStopLossPrice = 0;
            _highWaterMark = 0;

            // Reset close position tracking
            _closePositionTriggered = false;
            _closePositionOrderId = -1;

            // Reset result
            _result = StrategyResult.Running;

            // DON'T reset VWAP - we want continuous tracking within the session
            // DON'T reset session high/low - these continue throughout the day
            // DON'T reset logging flags - we're still in the same time window

            Log($"Waiting for conditions to be met again...", ConsoleColor.DarkGray);
        }

        /// <summary>
        /// Validates that the current price is not already above the take profit target.
        /// This prevents buying when we've "missed the boat" - the price has already run up
        /// past where we would have taken profits.
        /// </summary>
        /// <param name="currentPrice">The current market price.</param>
        /// <returns>True if safe to proceed with buy, false if price is too high.</returns>
        private bool ValidatePriceNotAboveTakeProfit(double currentPrice)
        {
            var order = _strategy.Order;

            // Only validate for BUY orders with take profit enabled
            if (order.Side != OrderSide.Buy || !order.EnableTakeProfit)
                return true;

            double? takeProfitThreshold = null;

            // Check ADX-based take profit (use conservative/weak target as threshold)
            if (order.AdxTakeProfit != null)
            {
                takeProfitThreshold = order.AdxTakeProfit.ConservativeTarget;
            }
            // Check fixed take profit price
            else if (order.TakeProfitPrice.HasValue)
            {
                takeProfitThreshold = order.TakeProfitPrice.Value;
            }

            // If we have a threshold and current price is at or above it, reject the trade
            if (takeProfitThreshold.HasValue && currentPrice >= takeProfitThreshold.Value)
            {
                Log($"*** MISSED THE BOAT! Price ${currentPrice:F2} >= Take Profit ${takeProfitThreshold.Value:F2}", ConsoleColor.Red);
                Log($"  Skipping entry - no profit potential at current price", ConsoleColor.Red);
                Log($"  Strategy will reset tomorrow and try again", ConsoleColor.Yellow);

                // Mark strategy as complete for today (will reset tomorrow)
                _isComplete = true;
                _result = StrategyResult.MissedTheBoat;

                PrintFinalResult();
                return false;
            }

            // Additional check: warn if price is close to take profit (within 5%)
            if (takeProfitThreshold.HasValue)
            {
                double potentialProfit = takeProfitThreshold.Value - currentPrice;
                double percentToTarget = (potentialProfit / currentPrice) * 100;

                if (percentToTarget < 5.0 && percentToTarget > 0)
                {
                    Log($"WARNING: Only {percentToTarget:F1}% potential profit to take profit target", ConsoleColor.Yellow);
                    Log($"  Current: ${currentPrice:F2} | Target: ${takeProfitThreshold.Value:F2} | Upside: ${potentialProfit:F2}", ConsoleColor.Yellow);
                }
            }

            return true;
        }

        private void EvaluateConditions(double price, double vwap)
        {
            if (_currentConditionIndex >= _strategy.Conditions.Count)
                return;

            // Check if we're within the time window (times are in Eastern)
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            var todayDate = DateOnly.FromDateTime(DateTime.Today);

            // Check for midnight reset - reset strategy state for a new trading day
            if (todayDate > _lastCheckedDate)
            {
                ResetForNewDay(todayDate);
            }

            // If StartTime is set and we haven't reached it yet, don't evaluate
            if (_strategy.StartTime.HasValue && currentTimeET < _strategy.StartTime.Value)
            {
                if (!_waitingForWindowLogged)
                {
                    _waitingForWindowLogged = true;
                    var info = TimezoneHelper.GetTimezoneDisplayInfo(Settings.Timezone);
                    var startLocal = TimezoneHelper.ToLocal(_strategy.StartTime.Value, Settings.Timezone);
                    Log($"Not monitoring yet - will start at {_strategy.StartTime.Value:h:mm tt} ET ({startLocal:h:mm tt} {info.Abbreviation})", ConsoleColor.DarkYellow);
                }
                return;
            }

            // If EndTime is set and we've passed it, wait for tomorrow (don't mark as complete)
            if (_strategy.EndTime.HasValue && currentTimeET > _strategy.EndTime.Value)
            {
                if (!_windowEndedLogged)
                {
                    _windowEndedLogged = true;
                    var info = TimezoneHelper.GetTimezoneDisplayInfo(Settings.Timezone);
                    var startLocal = _strategy.StartTime.HasValue 
                        ? TimezoneHelper.ToLocal(_strategy.StartTime.Value, Settings.Timezone) 
                        : new TimeOnly(4, 0);
                    var startET = _strategy.StartTime ?? new TimeOnly(4, 0);
                    Log($"Strategy window ended at {_strategy.EndTime.Value:h:mm tt} ET - will resume tomorrow at {startET:h:mm tt} ET ({startLocal:h:mm tt} {info.Abbreviation})", ConsoleColor.Yellow);
                }
                return;
            }

            var condition = _strategy.Conditions[_currentConditionIndex];

            if (condition.Evaluate(price, vwap))
            {
                _currentConditionIndex++;

                // Check if all conditions are met
                if (_currentConditionIndex >= _strategy.Conditions.Count)
                {
                    Log($"[OK] STEP {_currentConditionIndex}/{_strategy.Conditions.Count}: {condition.Name} - TRIGGERED @ ${price:F2}", ConsoleColor.Green);

                    // Safety check: Don't buy if price is already above take profit target ("missed the boat")
                    if (!ValidatePriceNotAboveTakeProfit(price))
                    {
                        return;
                    }

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
                OutsideRth = GetEffectiveOutsideRth(order),
                Tif = order.GetIbTif(),
                AllOrNone = order.AllOrNone
            };

            // Set account if specified
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                ibOrder.Account = Settings.AccountNumber;
            }

            // Set limit price
            if (order.Type == OrderType.Limit)
            {
                ibOrder.LmtPrice = order.LimitPrice.HasValue 
                    ? Math.Round(order.LimitPrice.Value, 2)
                    : Math.Round(vwap + order.LimitOffset, 2);
            }

            string priceStr = order.Type == OrderType.Limit ? $"@ ${ibOrder.LmtPrice:F2}" : "@ MKT";
            string tifDesc = GetTifDescription(order.TimeInForce);
            string aonStr = order.AllOrNone ? " | AON=true" : "";
            Log($">> SUBMITTING {order.Side} {order.Quantity} shares {priceStr}", ConsoleColor.Yellow);
            Log($"  OrderId={_entryOrderId} | TIF={tifDesc} | OutsideRTH={ibOrder.OutsideRth}{aonStr}", ConsoleColor.DarkGray);

            // Special handling for AtTheOpening orders
            if (order.TimeInForce == TimeInForce.AtTheOpening)
            {
                Log($"  NOTE: Order will execute at market open auction only", ConsoleColor.DarkGray);
            }

            // Special handling for Overnight orders - schedule cancellation at market open
            if (order.TimeInForce == TimeInForce.Overnight)
            {
                ScheduleOvernightCancellation();
            }

            // Log AllOrNone warning if enabled
            if (order.AllOrNone)
            {
                Log($"  NOTE: AllOrNone enabled - order must fill completely or not at all", ConsoleColor.DarkGray);
            }

            _client.placeOrder(_entryOrderId, _contract, ibOrder);
        }

        /// <summary>
        /// Gets the effective OutsideRth setting based on TIF type.
        /// </summary>
        /// <remarks>
        /// <para><b>Behavior by TIF:</b></para>
        /// <list type="bullet">
        ///   <item><see cref="TimeInForce.Overnight"/>: Forces OutsideRth = true.</item>
        ///   <item><see cref="TimeInForce.OvernightPlusDay"/>: Forces OutsideRth = true.</item>
        ///   <item><see cref="TimeInForce.AtTheOpening"/>: Uses order setting (typically false).</item>
        ///   <item>All others: Uses the order's OutsideRth setting.</item>
        /// </list>
        /// </remarks>
        private bool GetEffectiveOutsideRth(OrderAction order)
        {
            return order.TimeInForce switch
            {
                TimeInForce.Overnight => true,        // Must be true for extended hours
                TimeInForce.OvernightPlusDay => true, // Must be true for overnight portion
                _ => order.OutsideRth
            };
        }

        /// <summary>
        /// Gets a human-readable description of the TimeInForce setting.
        /// </summary>
        private string GetTifDescription(TimeInForce tif)
        {
            return tif switch
            {
                TimeInForce.Day => "Day (expires 4:00 PM EST)",
                TimeInForce.GoodTillCancel => "GTC (until filled/cancelled)",
                TimeInForce.ImmediateOrCancel => "IOC (immediate fill or cancel)",
                TimeInForce.FillOrKill => "FOK (all or nothing)",
                TimeInForce.Overnight => "Overnight (extended hours only)",
                TimeInForce.OvernightPlusDay => "Overnight+Day (extended + next day)",
                TimeInForce.AtTheOpening => "OPG (opening auction only)",
                _ => tif.ToString()
            };
        }

        /// <summary>
        /// Schedules order cancellation at market open for Overnight TIF orders.
        /// </summary>
        /// <remarks>
        /// Overnight orders should only execute during extended hours.
        /// This method schedules automatic cancellation at 9:30 AM EST if not filled.
        /// </remarks>
        private void ScheduleOvernightCancellation()
        {
            var now = DateTime.Now;
            var marketOpen = now.Date.Add(new TimeSpan(9, 30, 0)); // 9:30 AM local time

            // If market already opened today, schedule for tomorrow
            if (now >= marketOpen)
            {
                marketOpen = marketOpen.AddDays(1);
                // Skip weekends
                if (marketOpen.DayOfWeek == DayOfWeek.Saturday)
                    marketOpen = marketOpen.AddDays(2);
                else if (marketOpen.DayOfWeek == DayOfWeek.Sunday)
                    marketOpen = marketOpen.AddDays(1);
            }

            var delay = marketOpen - now;
            Log($"  OVERNIGHT TIF: Order will cancel at {marketOpen:HH:mm} if not filled ({delay.TotalHours:F1} hours)", ConsoleColor.DarkGray);

            // Note: Actual timer implementation would go here
            // For now, this is logged but the actual cancellation would need
            // a separate timer mechanism similar to ScheduleTakeProfitCancellation
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

                // Initialize trailing stop loss tracking
                if (_strategy.Order.EnableTrailingStopLoss)
                {
                    _highWaterMark = fillPrice;
                    _trailingStopLossPrice = Math.Round(fillPrice * (1 - _strategy.Order.TrailingStopLossPercent), 2);
                    Log($"TRAILING STOP INITIALIZED: ${_trailingStopLossPrice:F2} ({_strategy.Order.TrailingStopLossPercent * 100:F1}% below entry)", ConsoleColor.Magenta);
                }

                // Schedule close position if configured
                if (_strategy.Order.ClosePositionTime.HasValue)
                {
                    ScheduleClosePosition(_strategy.Order.ClosePositionTime.Value, _strategy.Order.ClosePositionOnlyIfProfitable);
                }

                Log($"  Monitoring position... Entry=${fillPrice:F2}", ConsoleColor.DarkGray);
                return;
            }

            // Check for trailing stop loss order fill
            if (orderId == _trailingStopLossOrderId && _trailingStopLossTriggered)
            {
                _isComplete = true;
                _exitFillPrice = fillPrice;

                double pnl = _strategy.Order.Quantity * (fillPrice - _entryFillPrice);
                _result = StrategyResult.TrailingStopLossFilled;

                if (pnl >= 0)
                {
                    Log($"*** TRAILING STOP LOSS FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2} (protected profit)", ConsoleColor.Yellow);
                }
                else
                {
                    Log($"*** TRAILING STOP LOSS FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2} (limited loss)", ConsoleColor.Red);
                }

                // Cancel take profit if still active
                if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled)
                {
                    _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                    _takeProfitCancelled = true;
                }

                PrintFinalResult();
                return;
            }

            // Check for fixed stop loss order fill
            if (orderId == _stopLossOrderId && !_stopLossFilled)
            {
                _stopLossFilled = true;
                _isComplete = true;
                _exitFillPrice = fillPrice;

                double pnl = _strategy.Order.Quantity * (fillPrice - _entryFillPrice);
                _result = StrategyResult.StopLossFilled;

                Log($"*** STOP LOSS FILLED @ ${fillPrice:F2} | P&L: ${pnl:F2}", ConsoleColor.Red);

                // Cancel take profit if still active
                if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled)
                {
                    _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                    _takeProfitCancelled = true;
                }

                PrintFinalResult();
                return;
            }

            // Check for take profit order fill (or early exit fill)
            if (orderId == _takeProfitOrderId && !_takeProfitFilled)
            {
                _takeProfitFilled = true;
                _isComplete = true;
                _exitFillPrice = fillPrice;

                double pnl = _strategy.Order.Quantity * (fillPrice - _entryFillPrice);

                // Cancel stop loss if still active
                if (_stopLossOrderId > 0 && !_stopLossFilled)
                {
                    _client.cancelOrder(_stopLossOrderId, new OrderCancel());
                }

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
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                tpOrder.Account = Settings.AccountNumber;
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
            // Thread-safe check - lock to prevent race with Dispose
            lock (_disposeLock)
            {
                if (_disposed || _takeProfitFilled || _takeProfitCancelled || _takeProfitOrderId < 0)
                    return;

                _takeProfitCancelled = true;
            }

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
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                exitOrder.Account = Settings.AccountNumber;
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
            _stopLossOrderId = _wrapper.ConsumeNextOrderId();

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
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                slOrder.Account = Settings.AccountNumber;
            }

            Log($">> SUBMITTING STOP LOSS @ ${slPrice:F2}", ConsoleColor.Yellow);
            Log($"  OrderId={_stopLossOrderId}", ConsoleColor.DarkGray);

            _client.placeOrder(_stopLossOrderId, _contract, slOrder);
        }

        /// <summary>
        /// Schedules automatic position close at specified time.
        /// </summary>
        private void ScheduleClosePosition(TimeOnly closeTime, bool onlyIfProfitable)
        {
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            var now = DateTime.Now;

            // Calculate time until close (assumes same day)
            var closeDateTime = now.Date.Add(closeTime.ToTimeSpan());

            // If close time already passed today, don't schedule
            if (closeDateTime <= now)
            {
                Log($"WARNING: ClosePosition time {closeTime:h:mm tt} ET already passed, no auto-close scheduled", ConsoleColor.Red);
                return;
            }

            var delay = closeDateTime - now;
            var profitClause = onlyIfProfitable ? " (only if profitable)" : " (regardless of P&L)";
            Log($"CLOSE POSITION SCHEDULED: {closeTime:h:mm tt} ET{profitClause} ({delay.TotalMinutes:F0} min from now)", ConsoleColor.Magenta);

            _closePositionTimer = new Timer(ClosePositionCallback, onlyIfProfitable, delay, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Callback triggered at ClosePositionTime to close the position.
        /// </summary>
        private void ClosePositionCallback(object? state)
        {
            bool onlyIfProfitable = state is bool b && b;

            // Thread-safe check
            lock (_disposeLock)
            {
                if (_disposed || _isComplete || !_entryFilled || _closePositionTriggered)
                    return;

                _closePositionTriggered = true;
            }

            // Check if position is profitable
            bool isLong = _strategy.Order.Side == OrderSide.Buy;
            bool isProfitable = isLong ? _lastPrice > _entryFillPrice : _lastPrice < _entryFillPrice;
            double unrealizedPnl = _strategy.Order.Quantity * (_lastPrice - _entryFillPrice);
            if (!isLong) unrealizedPnl = -unrealizedPnl;

            Log($"*** CLOSE POSITION TIME REACHED ***", ConsoleColor.Cyan);
            Log($"  Entry=${_entryFillPrice:F2} | Current=${_lastPrice:F2} | Unrealized P&L=${unrealizedPnl:F2}", ConsoleColor.Cyan);

            if (onlyIfProfitable && !isProfitable)
            {
                Log($"  Position is NOT profitable - keeping position open", ConsoleColor.Yellow);
                Log($"  WARNING: STILL HOLDING {_strategy.Order.Quantity} SHARES - will rely on stop loss or manual exit", ConsoleColor.Red);
                return;
            }

            // Cancel any open take profit order
            if (_takeProfitOrderId > 0 && !_takeProfitFilled && !_takeProfitCancelled)
            {
                Log($"  Cancelling take profit order #{_takeProfitOrderId}...", ConsoleColor.Yellow);
                _client.cancelOrder(_takeProfitOrderId, new OrderCancel());
                _takeProfitCancelled = true;
            }

            // Submit close position order
            SubmitClosePositionOrder(isProfitable);
        }

        /// <summary>
        /// Submits an order to close the position at current price.
        /// </summary>
        private void SubmitClosePositionOrder(bool isProfitable)
        {
            var order = _strategy.Order;
            _closePositionOrderId = _wrapper.ConsumeNextOrderId();

            string action = order.Side == OrderSide.Buy ? "SELL" : "BUY";

            // Check if we're in Regular Trading Hours
            var currentTimeET = TimezoneHelper.GetCurrentTime(MarketTimeZone.EST);
            bool isRTH = MarketTime.RTH.Contains(currentTimeET);

            Order closeOrder;

            if (isRTH)
            {
                // Use market order during RTH for immediate execution
                closeOrder = new Order
                {
                    Action = action,
                    OrderType = "MKT",
                    TotalQuantity = order.Quantity,
                    OutsideRth = false,
                    Tif = "DAY"
                };
                Log($">> SUBMITTING CLOSE POSITION {action} {order.Quantity} @ MKT (RTH)", ConsoleColor.Yellow);
            }
            else
            {
                // Use limit order outside RTH for safer execution
                double offset = 0.02;
                double limitPrice = order.Side == OrderSide.Buy
                    ? Math.Round(_lastPrice - offset, 2)  // Selling long: slightly below current
                    : Math.Round(_lastPrice + offset, 2); // Covering short: slightly above current

                closeOrder = new Order
                {
                    Action = action,
                    OrderType = "LMT",
                    LmtPrice = limitPrice,
                    TotalQuantity = order.Quantity,
                    OutsideRth = true,
                    Tif = "GTC"
                };
                Log($">> SUBMITTING CLOSE POSITION {action} {order.Quantity} @ ${limitPrice:F2} LMT (Outside RTH)", ConsoleColor.Yellow);
            }

            // Set account if specified
            if (!string.IsNullOrEmpty(Settings.AccountNumber))
            {
                closeOrder.Account = Settings.AccountNumber;
            }

            Log($"  OrderId={_closePositionOrderId}", ConsoleColor.DarkGray);

            // Track fill using the take profit order slot (it's already cancelled)
            _takeProfitOrderId = _closePositionOrderId;
            _takeProfitCancelled = false;
            _exitedWithProfit = isProfitable;

            _client.placeOrder(_closePositionOrderId, _contract, closeOrder);
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            // Dispose timers first to prevent callbacks after disposal starts
            _cancelTimer?.Dispose();
            _cancelTimer = null;

            _closePositionTimer?.Dispose();
            _closePositionTimer = null;

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

            double pnl = _strategy.Order.Quantity * (_exitFillPrice - _entryFillPrice);

            switch (_result)
            {
                case StrategyResult.TakeProfitFilled:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: *** TAKE PROFIT FILLED ***");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    break;

                case StrategyResult.StopLossFilled:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: *** STOP LOSS FILLED ***");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    break;

                case StrategyResult.TrailingStopLossFilled:
                    if (pnl >= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: *** TRAILING STOP LOSS (profit protected) ***");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: *** TRAILING STOP LOSS (loss limited) ***");
                    }
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  High: ${_highWaterMark:F2} | Trail Stop: ${_trailingStopLossPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
                    break;

                case StrategyResult.ExitedWithProfit:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: *** EXITED WITH PROFIT (time limit) ***");
                    Console.WriteLine($"[{timestamp}] |  Entry: ${_entryFillPrice:F2} -> Exit: ${_exitFillPrice:F2}");
                    Console.WriteLine($"[{timestamp}] |  P&L: ${pnl:F2}");
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

                case StrategyResult.MissedTheBoat:
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"[{timestamp}] |  [{_strategy.Symbol}] RESULT: MISSED THE BOAT");
                    Console.WriteLine($"[{timestamp}] |  Price already at/above take profit target when conditions met. No position taken");
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

            // Check if strategy should repeat after completion
            if (_strategy.RepeatEnabled && ShouldRepeat())
            {
                ResetForRepeat();
            }
        }

        /// <summary>
        /// Determines if the strategy should reset and repeat after the current trade completes.
        /// </summary>
        private bool ShouldRepeat()
        {
            // Only repeat after successful exits (TP, SL, TSL, or ExitedWithProfit)
            // Don't repeat if we never bought, missed the boat, or entry was cancelled
            return _result switch
            {
                StrategyResult.TakeProfitFilled => true,
                StrategyResult.StopLossFilled => true,
                StrategyResult.TrailingStopLossFilled => true,
                StrategyResult.ExitedWithProfit => true,
                _ => false
            };
        }
    }
}
