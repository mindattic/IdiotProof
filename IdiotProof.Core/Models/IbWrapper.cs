// ============================================================================
// IB Wrapper - EWrapper implementation for IBKR API
// ============================================================================
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  ⚠️  WARNING: DO NOT MODIFY THIS FILE  ⚠️                                  ║
// ║                                                                           ║
// ║  This class implements the EWrapper interface from the Interactive        ║
// ║  Brokers TWS API DLL (IBApi). The interface is defined by IB and cannot   ║
// ║  be changed.                                                              ║
// ║                                                                           ║
// ║  LOCKED:                                                                  ║
// ║    • Method signatures (names, parameters, return types)                  ║
// ║    • Class inheritance (must implement EWrapper)                          ║
// ║    • Interface member implementations                                     ║
// ║                                                                           ║
// ║  SAFE TO MODIFY:                                                          ║
// ║    • Method body implementations (logic inside methods)                   ║
// ║    • Private fields and helper methods                                    ║
// ║    • Custom events and properties (non-interface members)                 ║
// ║    • XML documentation comments                                           ║
// ║                                                                           ║
// ║  Reference: https://interactivebrokers.github.io/tws-api/               ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// FUNCTIONALITY:
// - Captures last trade prints for VWAP calculation
// - Captures nextValidId for order IDs
// - Captures execution details for fills
// - Routes market data to registered handlers per ticker
//
// ============================================================================

using IBApi;
using IdiotProof.Backend.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IdiotProof.Core.Helpers;
using IbContract = IBApi.Contract;

namespace IdiotProof.Backend.Models
{
    /// <summary>
    /// EWrapper implementation for the IBKR TWS API.
    /// Handles market data, order IDs, and execution reports.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ EXTERNAL DEPENDENCY - DO NOT MODIFY INTERFACE MEMBERS ⚠️</b></para>
    /// <para>
    /// This class implements <see cref="EWrapper"/> from the Interactive Brokers TWS API DLL.
    /// All interface method signatures are defined by IB and cannot be changed.
    /// </para>
    /// 
    /// <para><b>What is LOCKED (do not change):</b></para>
    /// <list type="bullet">
    ///   <item>Method signatures (names, parameters, return types)</item>
    ///   <item>Class inheritance (<c>: EWrapper</c>)</item>
    ///   <item>Interface member implementations</item>
    /// </list>
    /// 
    /// <para><b>What is SAFE to modify:</b></para>
    /// <list type="bullet">
    ///   <item>Method body implementations (logic inside methods)</item>
    ///   <item>Private fields and helper methods</item>
    ///   <item>Custom events like <see cref="OnLastTrade"/> and <see cref="OnOrderFill"/></item>
    ///   <item>XML documentation comments</item>
    /// </list>
    /// 
    /// <para><b>Key Events:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="OnLastTrade"/>: Fired when last trade data is received.</item>
    ///   <item><see cref="OnOrderFill"/>: Fired when an order execution is reported.</item>
    /// </list>
    /// 
    /// <para><b>Reference:</b> https://interactivebrokers.github.io/tws-api/</para>
    /// </remarks>

    /// <summary>
    /// Represents the result of a ping operation to the IBKR API.
    /// </summary>
    /// <remarks>
    /// This struct is returned by <see cref="IbWrapper.Ping"/> and <see cref="IbWrapper.PingAsync"/>.
    /// It contains the success status, server timestamp, and round-trip latency.
    /// </remarks>
    public readonly struct PingResult
    {
        /// <summary>Gets whether the ping was successful (received response within timeout).</summary>
        public bool Success { get; init; }

        /// <summary>Gets the server time returned by the API (Unix timestamp in seconds).</summary>
        public long ServerTime { get; init; }

        /// <summary>Gets the round-trip latency in milliseconds.</summary>
        public long LatencyMs { get; init; }

        /// <summary>Gets the server time as a DateTime (UTC).</summary>
        public DateTime ServerTimeUtc => Success ? DateTimeOffset.FromUnixTimeSeconds(ServerTime).UtcDateTime : DateTime.MinValue;
    }

    public sealed class IbWrapper : EWrapper, IDisposable
    {
        /// <summary>
        /// Shared session logger instance (set from Program.cs).
        /// </summary>
        public static SessionLogger? SessionLogger { get; set; }

        /// <summary>
        /// Signal used by the EReader to notify when messages are available.
        /// </summary>
        /// <remarks>
        /// This is passed to <see cref="EClientSocket"/> constructor and used by <see cref="EReader"/>
        /// to signal when data is ready to be processed.
        /// </remarks>
        public EReaderSignal Signal { get; } = new EReaderMonitorSignal();

        private EClientSocket? _client;
        private bool _disposed;
        private volatile bool _isConnected;

        private readonly ManualResetEventSlim _nextValidIdEvent = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim _pingResponseEvent = new ManualResetEventSlim(false);
        private readonly object _orderIdLock = new object();
        private readonly object _pingLock = new object();
        private int _nextOrderId = -1;
        private long _lastServerTime;
        private long _pingStartTicks;

        // Market data caches for LAST and LAST_SIZE pairing
        private readonly ConcurrentDictionary<int, double> _lastByTicker = new();
        private readonly ConcurrentDictionary<int, int> _lastSizeByTicker = new();

        // Market data caches for BID and ASK prices
        private readonly ConcurrentDictionary<int, double> _bidByTicker = new();
        private readonly ConcurrentDictionary<int, double> _askByTicker = new();

        // Per-ticker handlers for routing market data to specific runners
        private readonly ConcurrentDictionary<int, Action<double, int>> _tickerHandlers = new();

        // Per-ticker handlers for bid/ask updates
        private readonly ConcurrentDictionary<int, Action<double, double>> _bidAskHandlers = new();

        /// <summary>
        /// Event fired when any last trade is received (legacy, fires for all tickers).
        /// </summary>
        public event Action<double, int>? OnLastTrade;

        /// <summary>
        /// Event fired when any order fill is received.
        /// Parameters: orderId, fillPrice, fillSize
        /// </summary>
        public event Action<int, double, int>? OnOrderFill;

        /// <summary>
        /// Event fired when order status changes.
        /// Parameters: orderId, status, filled, remaining
        /// </summary>
        public event Action<int, string, decimal, decimal>? OnOrderStatus;

        /// <summary>
        /// Event fired when an open order is received.
        /// Parameters: orderId, symbol, action, quantity, orderType, limitPrice, status
        /// </summary>
        public event Action<int, string, string, decimal, string, double, string>? OnOpenOrder;

        /// <summary>
        /// Event fired when all open orders have been received.
        /// </summary>
        public event Action? OnOpenOrdersEnd;

        /// <summary>
        /// Event fired when connectivity to IBKR is lost.
        /// Error code 1100: Connectivity lost.
        /// </summary>
        public event Action? OnConnectionLost;

        /// <summary>
        /// Event fired when connectivity to IBKR is restored.
        /// Error code 1101: Connectivity restored, data lost - resubscription required.
        /// Error code 1102: Connectivity restored, data maintained.
        /// Parameter: bool dataLost - true if market data subscriptions were lost and need resubscription.
        /// </summary>
        public event Action<bool>? OnConnectionRestored;

        /// <summary>
        /// Event fired when historical data bars are received.
        /// Parameters: reqId, bar
        /// </summary>
        public event Action<int, Bar>? OnHistoricalData;

        /// <summary>
        /// Event fired when historical data request completes.
        /// Parameters: reqId, startDateStr, endDateStr
        /// </summary>
        public event Action<int, string, string>? OnHistoricalDataEnd;

        /// <summary>
        /// Event fired when an order is rejected by IBKR.
        /// Parameters: orderId, errorCode, errorMessage
        /// </summary>
        /// <remarks>
        /// Common rejection error codes:
        /// <list type="bullet">
        ///   <item>103: Duplicate order id</item>
        ///   <item>110: Price does not conform to minimum price variation</item>
        ///   <item>201: Order rejected</item>
        ///   <item>399: Order message for warning</item>
        ///   <item>4108: Contract not available for short sale</item>
        ///   <item>4110: Small Cap restriction</item>
        /// </list>
        /// </remarks>
        public event Action<int, int, string>? OnOrderRejected;

        /// <summary>
        /// Gets whether the connection to IBKR is currently active.
        /// </summary>
        public bool IsConnected => _isConnected;

        // Track open orders
        private readonly ConcurrentDictionary<int, (string Symbol, string Action, decimal Qty, string Type, double LmtPrice, string Status)> _openOrders = new();

        // Track positions
        private readonly ConcurrentDictionary<string, (decimal Quantity, double AvgCost)> _positions = new();
        private readonly ManualResetEventSlim _positionsEndEvent = new ManualResetEventSlim(false);

        /// <summary>
        /// Gets all currently tracked open orders.
        /// </summary>
        public IReadOnlyDictionary<int, (string Symbol, string Action, decimal Qty, string Type, double LmtPrice, string Status)> OpenOrders => _openOrders;

        /// <summary>
        /// Gets all currently tracked positions.
        /// </summary>
        public IReadOnlyDictionary<string, (decimal Quantity, double AvgCost)> Positions => _positions;

        /// <summary>
        /// Event fired when a position update is received.
        /// Parameters: symbol, quantity, avgCost
        /// </summary>
        public event Action<string, decimal, double>? OnPosition;

        /// <summary>
        /// Event fired when all positions have been received.
        /// </summary>
        public event Action? OnPositionsEnd;

        // Track account values (margin, buying power, etc.)
        private readonly ConcurrentDictionary<string, double> _accountValues = new();
        private volatile bool _accountUpdatesSubscribed;

        /// <summary>
        /// Gets the available buying power (AvailableFunds-S).
        /// Returns 0 if account updates haven't been received yet.
        /// </summary>
        public double BuyingPower => _accountValues.TryGetValue("BuyingPower", out var bp) ? bp : 0;

        /// <summary>
        /// Gets the net liquidation value (NetLiquidation-S).
        /// Returns 0 if account updates haven't been received yet.
        /// </summary>
        public double NetLiquidation => _accountValues.TryGetValue("NetLiquidation", out var nl) ? nl : 0;

        /// <summary>
        /// Gets the available funds for new positions (AvailableFunds-S).
        /// Returns 0 if account updates haven't been received yet.
        /// </summary>
        public double AvailableFunds => _accountValues.TryGetValue("AvailableFunds", out var af) ? af : 0;

        /// <summary>
        /// Requests account updates from IBKR.
        /// Account values will be updated via updateAccountValue callback.
        /// </summary>
        /// <param name="accountNumber">Optional account number. If empty, uses the first managed account.</param>
        public void RequestAccountUpdates(string accountNumber = "")
        {
            if (_accountUpdatesSubscribed) return;

            _client?.reqAccountUpdates(true, accountNumber);
            _accountUpdatesSubscribed = true;
            Log("IB_INFO", "Subscribed to account updates");
        }

        /// <summary>
        /// Checks if there's sufficient buying power for the specified order value.
        /// </summary>
        /// <param name="orderValue">The estimated order value (price * quantity * margin requirement).</param>
        /// <param name="buffer">Safety buffer percentage (default: 10% = 0.10).</param>
        /// <returns>True if buying power is sufficient, false otherwise.</returns>
        public bool HasSufficientMargin(double orderValue, double buffer = 0.10)
        {
            double available = AvailableFunds;
            if (available <= 0) return true; // No account data yet, allow order to proceed

            double requiredWithBuffer = orderValue * (1 + buffer);
            return available >= requiredWithBuffer;
        }

        /// <summary>
        /// Gets the last server time received from a ping (Unix timestamp in seconds).
        /// Returns 0 if no ping has been performed yet.
        /// </summary>
        public long LastServerTime => Interlocked.Read(ref _lastServerTime);

        /// <summary>
        /// Requests all open orders from IBKR.
        /// Results come back via OnOpenOrder and OnOpenOrdersEnd events.
        /// </summary>
        public void RequestOpenOrders()
        {
            _client?.reqAllOpenOrders();
        }

        /// <summary>
        /// Requests open orders and waits for them to be received.
        /// </summary>
        public void RequestOpenOrdersAndWait(TimeSpan timeout)
        {
            using var completedEvent = new ManualResetEventSlim(false);

            void OnComplete() => completedEvent.Set();
            OnOpenOrdersEnd += OnComplete;

            try
            {
                // Clear existing orders before refreshing - they will be re-added by openOrder callbacks
                _openOrders.Clear();
                _client?.reqAllOpenOrders();
                completedEvent.Wait(timeout);
            }
            finally
            {
                OnOpenOrdersEnd -= OnComplete;
            }
        }

        /// <summary>
        /// Requests all positions from IBKR.
        /// Results come back via position() and positionEnd() callbacks.
        /// </summary>
        public void RequestPositions()
        {
            _client?.reqPositions();
        }

        /// <summary>
        /// Requests positions and waits for them to be received.
        /// </summary>
        /// <param name="timeout">Timeout for the request.</param>
        public void RequestPositionsAndWait(TimeSpan timeout)
        {
            _positionsEndEvent.Reset();
            _positions.Clear();
            _client?.reqPositions();
            _positionsEndEvent.Wait(timeout);
        }

        /// <summary>
        /// Cancels any active position subscription.
        /// </summary>
        public void CancelPositions()
        {
            _client?.cancelPositions();
        }

        /// <summary>
        /// Cancels an order by its order ID.
        /// </summary>
        /// <param name="orderId">The order ID to cancel.</param>
        public void CancelOrder(int orderId)
        {
            _client?.cancelOrder(orderId, new OrderCancel());
        }

        /// <summary>
        /// Cancels all open orders.
        /// </summary>
        public void CancelAllOrders()
        {
            _client?.reqGlobalCancel(new OrderCancel());
        }

        /// <summary>
        /// Modifies an existing order by resubmitting with the same order ID.
        /// </summary>
        /// <param name="orderId">The existing order ID to modify.</param>
        /// <param name="contract">The contract for the order.</param>
        /// <param name="order">The modified order with updated parameters.</param>
        /// <remarks>
        /// <para><b>IB API Behavior:</b></para>
        /// <para>The IB API treats a placeOrder call with an existing orderId as a modification.</para>
        /// <para>You can modify: price (LmtPrice, AuxPrice), quantity, order type, TIF, etc.</para>
        /// <para><b>Common Use Cases:</b></para>
        /// <list type="bullet">
        ///   <item>Adjust take profit price: Update <c>order.LmtPrice</c></item>
        ///   <item>Adjust stop loss price: Update <c>order.AuxPrice</c> (for STP orders)</item>
        ///   <item>Change quantity: Update <c>order.TotalQuantity</c></item>
        /// </list>
        /// </remarks>
        public void ModifyOrder(int orderId, IbContract contract, Order order)
        {
            if (_client == null)
            {
                Log("ORDER", "Cannot modify order - client not connected");
                return;
            }

            Log("ORDER", $"Modifying order #{orderId}: {order.Action} {order.TotalQuantity} {order.OrderType} @ {(order.OrderType == "LMT" ? $"${order.LmtPrice:F2}" : order.OrderType == "STP" ? $"${order.AuxPrice:F2}" : "MKT")}");
            _client.placeOrder(orderId, contract, order);
        }

        /// <summary>
        /// Attaches the EClientSocket instance to this wrapper.
        /// </summary>
        /// <param name="client">The client socket to attach.</param>
        /// <remarks>
        /// This must be called after creating the <see cref="EClientSocket"/> but before connecting.
        /// The client socket uses this wrapper to receive callbacks from the API.
        /// </remarks>
        public void AttachClient(EClientSocket client)
        {
            _client = client;
        }

        /// <summary>
        /// Logs a message to both console and session log file.
        /// </summary>
        private static void Log(string category, string message)
        {
            Console.WriteLine($"{TimeStamp.NowBracketed} {message}");
            SessionLogger?.LogEvent(category, message);
        }

        /// <summary>
        /// Sends a ping to the IBKR API server and waits for a response.
        /// This verifies that the connection is still active and the API is responding.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for a response. Default is 5 seconds.</param>
        /// <returns>A <see cref="PingResult"/> indicating success/failure and latency.</returns>
        /// <remarks>
        /// Uses the IB API's reqCurrentTime() method which triggers the currentTime() callback.
        /// The server responds with its current Unix timestamp.
        /// </remarks>
        public PingResult Ping(TimeSpan? timeout = null)
        {
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);

            lock (_pingLock)
            {
                if (_client == null || _disposed)
                {
                    return new PingResult { Success = false, ServerTime = 0, LatencyMs = 0 };
                }

                // Reset the event before sending request
                _pingResponseEvent.Reset();
                _pingStartTicks = Stopwatch.GetTimestamp();

                // Request current time from server
                _client.reqCurrentTime();

                // Wait for response
                bool received = _pingResponseEvent.Wait(effectiveTimeout);

                if (received)
                {
                    long elapsedTicks = Stopwatch.GetTimestamp() - _pingStartTicks;
                    long latencyMs = (elapsedTicks * 1000) / Stopwatch.Frequency;

                    return new PingResult
                    {
                        Success = true,
                        ServerTime = Interlocked.Read(ref _lastServerTime),
                        LatencyMs = latencyMs
                    };
                }

                return new PingResult { Success = false, ServerTime = 0, LatencyMs = 0 };
            }
        }

        /// <summary>
        /// Sends a ping to the IBKR API server asynchronously.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for a response. Default is 5 seconds.</param>
        /// <returns>A task that completes with the <see cref="PingResult"/>.</returns>
        public Task<PingResult> PingAsync(TimeSpan? timeout = null)
        {
            return Task.Run(() => Ping(timeout));
        }

        /// <summary>
        /// Registers a handler for market data from a specific ticker ID.
        /// </summary>
        /// <param name="tickerId">The ticker ID (request ID) to register the handler for.</param>
        /// <param name="handler">Callback invoked with (price, size) when market data arrives.</param>
        /// <remarks>
        /// This allows individual strategy runners to receive market data for their specific ticker
        /// without processing data for all tickers. The handler is called from the EReader thread.
        /// </remarks>
        public void RegisterTickerHandler(int tickerId, Action<double, int> handler)
        {
            _tickerHandlers[tickerId] = handler;
        }

        /// <summary>
        /// Registers a handler for bid/ask price updates on a specific ticker.
        /// </summary>
        /// <param name="tickerId">The ticker ID returned from reqMktData.</param>
        /// <param name="handler">Callback invoked with (bidPrice, askPrice) when bid or ask updates.</param>
        public void RegisterBidAskHandler(int tickerId, Action<double, double> handler)
        {
            _bidAskHandlers[tickerId] = handler;
        }

        /// <summary>
        /// Gets the last known bid and ask prices for a ticker.
        /// </summary>
        /// <param name="tickerId">The ticker ID.</param>
        /// <returns>Tuple of (bid, ask). Returns (0, 0) if not available.</returns>
        public (double Bid, double Ask) GetBidAsk(int tickerId)
        {
            double bid = _bidByTicker.TryGetValue(tickerId, out var b) ? b : 0;
            double ask = _askByTicker.TryGetValue(tickerId, out var a) ? a : 0;
            return (bid, ask);
        }

        /// <summary>
        /// Unregisters the handler for a specific ticker ID and clears cached data.
        /// </summary>
        /// <param name="tickerId">The ticker ID to unregister.</param>
        /// <remarks>
        /// Call this when stopping a strategy to prevent memory buildup from cached market data.
        /// </remarks>
        public void UnregisterTickerHandler(int tickerId)
        {
            _tickerHandlers.TryRemove(tickerId, out _);
            _bidAskHandlers.TryRemove(tickerId, out _);
            // Clear cached market data to prevent memory buildup
            _lastByTicker.TryRemove(tickerId, out _);
            _lastSizeByTicker.TryRemove(tickerId, out _);
            _bidByTicker.TryRemove(tickerId, out _);
            _askByTicker.TryRemove(tickerId, out _);
        }

        /// <summary>
        /// Waits for the next valid order ID to be received from IBKR.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for the order ID.</param>
        /// <returns><c>true</c> if the order ID was received within the timeout; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// The API sends the next valid order ID via <see cref="nextValidId"/> callback after connecting.
        /// This method blocks until that callback is received or the timeout expires.
        /// </remarks>
        public bool WaitForNextValidId(TimeSpan timeout)
        {
            return _nextValidIdEvent.Wait(timeout);
        }

        /// <summary>
        /// Consumes and returns the next available order ID, incrementing the internal counter.
        /// </summary>
        /// <returns>The next available order ID.</returns>
        /// <exception cref="InvalidOperationException">Thrown if order IDs have not been initialized yet.</exception>
        /// <remarks>
        /// This method is thread-safe. Each call returns a unique order ID.
        /// Call <see cref="WaitForNextValidId"/> first to ensure order IDs are initialized.
        /// </remarks>
        public int ConsumeNextOrderId()
        {
            lock (_orderIdLock)
            {
                if (_nextOrderId < 0)
                    throw new InvalidOperationException("nextOrderId not initialized.");

                int id = _nextOrderId;
                _nextOrderId++;
                return id;
            }
        }

        // -------------------------
        // Connection / Errors
        // -------------------------
        public void error(Exception e)
        {
            Log("IB_ERROR", $"IB ERROR (Exception): {e}");
        }

        public void error(string str)
        {
            Log("IB_ERROR", $"IB ERROR: {str}");
        }

        public void error(int id, int errorCode, string errorMsg)
        {
            // Codes 2100-2199 are informational status messages - suppress them
            if (errorCode >= 2100 && errorCode <= 2199)
            {
                return;
            }

            // Handle connectivity status codes
            switch (errorCode)
            {
                case 1100: // Connectivity lost
                    _isConnected = false;
                    Log("IB_ERROR", $"IB ERROR: id={id} code={errorCode} msg={errorMsg}");
                    OnConnectionLost?.Invoke();
                    return;

                case 1101: // Connectivity restored - data lost, need to resubscribe
                    _isConnected = true;
                    Log("IB_INFO", $"IB INFO: id={id} code={errorCode} msg={errorMsg}");
                    OnConnectionRestored?.Invoke(true); // dataLost = true
                    return;

                case 1102: // Connectivity restored - data maintained
                    _isConnected = true;
                    Log("IB_INFO", $"IB INFO: id={id} code={errorCode} msg={errorMsg}");
                    OnConnectionRestored?.Invoke(false); // dataLost = false
                    return;
            }

            // Code 202 = Order Canceled - remove from tracking (this is a confirmation, not an error)
            if (errorCode == 202 && id >= 0)
            {
                _openOrders.TryRemove(id, out _);
            }

            // Fire order rejection event for order-related errors
            // These indicate the order was not accepted by IBKR
            if (id >= 0 && IsOrderRejectionError(errorCode))
            {
                OnOrderRejected?.Invoke(id, errorCode, errorMsg);
            }

            Log("IB_ERROR", $"IB ERROR: id={id} code={errorCode} msg={errorMsg}");
        }

        public void error(int id, long time, int errorCode, string errorMsg, string advancedOrderRejectJson)
        {
            // Codes 2100-2199 are informational status messages - suppress them
            if (errorCode >= 2100 && errorCode <= 2199)
            {
                return;
            }

            // Handle connectivity status codes
            switch (errorCode)
            {
                case 1100: // Connectivity lost
                    _isConnected = false;
                    Log("IB_ERROR", $"IB ERROR: id={id} code={errorCode} msg={errorMsg}");
                    OnConnectionLost?.Invoke();
                    return;

                case 1101: // Connectivity restored - data lost, need to resubscribe
                    _isConnected = true;
                    Log("IB_INFO", $"IB INFO: id={id} code={errorCode} msg={errorMsg}");
                    OnConnectionRestored?.Invoke(true); // dataLost = true
                    return;

                case 1102: // Connectivity restored - data maintained
                    _isConnected = true;
                    Log("IB_INFO", $"IB INFO: id={id} code={errorCode} msg={errorMsg}");
                    OnConnectionRestored?.Invoke(false); // dataLost = false
                    return;
            }

            // Code 202 = Order Canceled - remove from tracking (this is a confirmation, not an error)
            if (errorCode == 202 && id >= 0)
            {
                _openOrders.TryRemove(id, out _);
            }

            // Fire order rejection event for order-related errors
            if (id >= 0 && IsOrderRejectionError(errorCode))
            {
                OnOrderRejected?.Invoke(id, errorCode, errorMsg);
            }

            Log("IB_ERROR", $"IB ERROR: id={id} code={errorCode} msg={errorMsg}");
        }

        /// <summary>
        /// Determines if an error code represents an order rejection.
        /// </summary>
        private static bool IsOrderRejectionError(int errorCode)
        {
            return errorCode switch
            {
                103 => true,  // Duplicate order id
                104 => true,  // Can't modify a filled order
                105 => true,  // Order being modified is not active
                106 => true,  // Can't transmit order
                107 => true,  // Cannot transmit order ID
                109 => true,  // Price does not conform to minTick
                110 => true,  // Price does not conform to minimum price variation
                135 => true,  // Can't find order with ID
                136 => true,  // This order cannot be cancelled
                161 => true,  // Cancel attempted while order is pending cancel
                201 => true,  // Order rejected
                203 => true,  // The security is not available or allowed
                399 => true,  // Order message for warning
                404 => true,  // Insufficient margin
                4108 => true, // Contract not available for short sale
                4110 => true, // Small cap restriction / No trading permission
                _ => false
            };
        }

        public void connectionClosed()
        {
            _isConnected = false;
            Log("IB_INFO", "IB connection closed.");
        }

        public void connectAck()
        {
            if (_client != null && _client.AsyncEConnect)
            {
                _client.startApi();
            }
        }

        // -------------------------
        // Order IDs
        // -------------------------
        public void nextValidId(int orderId)
        {
            lock (_orderIdLock)
            {
                _nextOrderId = orderId;
            }

            _isConnected = true;
            Log("IB_INFO", $"Received nextValidId: {orderId}");
            _nextValidIdEvent.Set();
        }

        // -------------------------
        // Market data (Last / Last Size / Bid / Ask)
        // -------------------------
        public void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
        {
            if (field == TickType.LAST)
            {
                _lastByTicker[tickerId] = price;

                if (_lastSizeByTicker.TryGetValue(tickerId, out int size))
                {
                    // Fire ticker-specific handler if registered
                    if (_tickerHandlers.TryGetValue(tickerId, out var handler))
                    {
                        handler(price, size);
                    }

                    // Fire global event (legacy)
                    OnLastTrade?.Invoke(price, size);
                }
            }
            else if (field == TickType.BID)
            {
                _bidByTicker[tickerId] = price;
                FireBidAskHandler(tickerId);
            }
            else if (field == TickType.ASK)
            {
                _askByTicker[tickerId] = price;
                FireBidAskHandler(tickerId);
            }
        }

        private void FireBidAskHandler(int tickerId)
        {
            if (_bidByTicker.TryGetValue(tickerId, out double bid) &&
                _askByTicker.TryGetValue(tickerId, out double ask) &&
                bid > 0 && ask > 0)
            {
                if (_bidAskHandlers.TryGetValue(tickerId, out var handler))
                {
                    handler(bid, ask);
                }
            }
        }

        public void tickSize(int tickerId, int field, decimal size)
        {
            if (field == TickType.LAST_SIZE)
            {
                _lastSizeByTicker[tickerId] = (int)size;

                if (_lastByTicker.TryGetValue(tickerId, out double price))
                {
                    // Fire ticker-specific handler if registered
                    if (_tickerHandlers.TryGetValue(tickerId, out var handler))
                    {
                        handler(price, (int)size);
                    }

                    // Fire global event (legacy)
                    OnLastTrade?.Invoke(price, (int)size);
                }
            }
        }

        // -------------------------
        // Fills
        // -------------------------
        public void execDetails(int reqId, IbContract contract, IBApi.Execution execution)
        {
            if (execution != null)
            {
                OnOrderFill?.Invoke(execution.OrderId, execution.Price, (int)execution.Shares);
            }
        }

        public void execDetailsEnd(int reqId) { }

        // -------------------------
        // Unused EWrapper members (required by interface)
        // -------------------------

        public void tickGeneric(int tickerId, int field, double value) { }
        public void tickString(int tickerId, int field, string value) { }
        public void tickEFP(int tickerId, int tickType, double basisPoints, string formattedBasisPoints, double totalDividends, int holdDays, string futureLastTradeDate, double dividendImpact, double dividendsToLastTradeDate) { }

        public void orderStatus(int orderId, string status, decimal filled, decimal remaining, double avgFillPrice, long permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice) 
        {
            // Update order status in our tracking dictionary
            if (_openOrders.TryGetValue(orderId, out var order))
            {
                _openOrders[orderId] = (order.Symbol, order.Action, order.Qty, order.Type, order.LmtPrice, status);
            }

            // Fire event for status change
            OnOrderStatus?.Invoke(orderId, status, filled, remaining);
        }

        public void openOrder(int orderId, IbContract contract, IBApi.Order order, IBApi.OrderState orderState) 
        {
            // Track the open order
            _openOrders[orderId] = (
                contract.Symbol,
                order.Action,
                order.TotalQuantity,
                order.OrderType,
                order.LmtPrice,
                orderState.Status
            );

            // Fire event
            OnOpenOrder?.Invoke(orderId, contract.Symbol, order.Action, order.TotalQuantity, order.OrderType, order.LmtPrice, orderState.Status);
        }

        public void openOrderEnd() 
        {
            OnOpenOrdersEnd?.Invoke();
        }
        public void updateAccountValue(string key, string value, string currency, string accountName) 
        {
            // Track important account values (only USD values)
            if (currency != "USD") return;

            // Parse and store key account metrics
            switch (key)
            {
                case "BuyingPower":
                case "NetLiquidation":
                case "AvailableFunds":
                case "ExcessLiquidity":
                case "RegTMargin":
                case "SMA":
                    if (double.TryParse(value, out double numValue))
                    {
                        _accountValues[key] = numValue;
                    }
                    break;
            }
        }
        public void updatePortfolio(IbContract contract, decimal position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName) { }
        public void updateAccountTime(string timestamp) { }
        public void accountDownloadEnd(string accountName) { }
        public void contractDetails(int reqId, IBApi.ContractDetails contractDetails) { }
        public void bondContractDetails(int reqId, IBApi.ContractDetails contractDetails) { }
        public void contractDetailsEnd(int reqId) { }
        public void managedAccounts(string accountsList) { }
        public void receiveFA(int faDataType, string faXmlData) { }
        public void historicalData(int reqId, Bar bar)
        {
            OnHistoricalData?.Invoke(reqId, bar);
        }
        public void historicalDataEnd(int reqId, string start, string end)
        {
            OnHistoricalDataEnd?.Invoke(reqId, start, end);
        }
        public void scannerParameters(string xml) { }
        public void scannerData(int reqId, int rank, IBApi.ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr) { }
        public void scannerDataEnd(int reqId) { }
        public void realtimeBar(int reqId, long time, double open, double high, double low, double close, decimal volume, decimal wap, int count) { }
        public void currentTime(long time)
        {
            // Store the server time and signal any waiting ping request
            Interlocked.Exchange(ref _lastServerTime, time);
            _pingResponseEvent.Set();
        }
        public void fundamentalData(int reqId, string data) { }
        public void deltaNeutralValidation(int reqId, IBApi.DeltaNeutralContract deltaNeutralContract) { }
        public void tickSnapshotEnd(int reqId) { }
        public void marketDataType(int reqId, int marketDataType) { }
        public void commissionAndFeesReport(IBApi.CommissionAndFeesReport report) { }
        public void position(string account, IbContract contract, decimal pos, double avgCost)
        {
            var symbol = contract.Symbol;
            if (pos != 0)
            {
                _positions[symbol] = (pos, avgCost);
            }
            else
            {
                _positions.TryRemove(symbol, out _);
            }
            OnPosition?.Invoke(symbol, pos, avgCost);
        }
        public void positionEnd()
        {
            _positionsEndEvent.Set();
            OnPositionsEnd?.Invoke();
        }
        public void accountSummary(int reqId, string account, string tag, string value, string currency) { }
        public void accountSummaryEnd(int reqId) { }
        public void verifyMessageAPI(string apiData) { }
        public void verifyCompleted(bool isSuccessful, string errorText) { }
        public void verifyAndAuthMessageAPI(string apiData, string xyzChallenge) { }
        public void verifyAndAuthCompleted(bool isSuccessful, string errorText) { }
        public void displayGroupList(int reqId, string groups) { }
        public void displayGroupUpdated(int reqId, string contractInfo) { }
        public void positionMulti(int reqId, string account, string modelCode, IbContract contract, decimal pos, double avgCost) { }
        public void positionMultiEnd(int reqId) { }
        public void accountUpdateMulti(int reqId, string account, string modelCode, string key, string value, string currency) { }
        public void accountUpdateMultiEnd(int reqId) { }
        public void securityDefinitionOptionParameter(int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, HashSet<string> expirations, HashSet<double> strikes) { }
        public void securityDefinitionOptionParameterEnd(int reqId) { }
        public void softDollarTiers(int reqId, IBApi.SoftDollarTier[] tiers) { }
        public void familyCodes(IBApi.FamilyCode[] familyCodes) { }
        public void symbolSamples(int reqId, IBApi.ContractDescription[] contractDescriptions) { }
        public void mktDepthExchanges(DepthMktDataDescription[] depthMktDataDescriptions) { }
        public void tickNews(int tickerId, long timeStamp, string providerCode, string articleId, string headline, string extraData) { }
        public void smartComponents(int reqId, Dictionary<int, KeyValuePair<string, char>> theMap) { }
        public void tickReqParams(int tickerId, double minTick, string bboExchange, int snapshotPermissions) { }
        public void newsProviders(IBApi.NewsProvider[] newsProviders) { }
        public void newsArticle(int requestId, int articleType, string articleText) { }
        public void historicalNews(int requestId, string time, string providerCode, string articleId, string headline) { }
        public void historicalNewsEnd(int requestId, bool hasMore) { }
        public void headTimestamp(int reqId, string headTimestamp) { }
        public void histogramData(int reqId, HistogramEntry[] data) { }
        public void historicalDataUpdate(int reqId, Bar bar) { }
        public void rerouteMktDataReq(int reqId, int conid, string exchange) { }
        public void rerouteMktDepthReq(int reqId, int conid, string exchange) { }
        public void marketRule(int marketRuleId, IBApi.PriceIncrement[] priceIncrements) { }
        public void pnl(int reqId, double dailyPnL, double unrealizedPnL, double realizedPnL) { }
        public void pnlSingle(int reqId, decimal pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value) { }
        public void historicalTicks(int reqId, IBApi.HistoricalTick[] ticks, bool done) { }
        public void historicalTicksBidAsk(int reqId, IBApi.HistoricalTickBidAsk[] ticks, bool done) { }
        public void historicalTicksLast(int reqId, IBApi.HistoricalTickLast[] ticks, bool done) { }
        public void tickByTickAllLast(int reqId, int tickType, long time, double price, decimal size, IBApi.TickAttribLast tickAttribLast, string exchange, string specialConditions) { }
        public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, decimal bidSize, decimal askSize, IBApi.TickAttribBidAsk tickAttribBidAsk) { }
        public void tickByTickMidPoint(int reqId, long time, double midPoint) { }
        public void orderBound(long orderId, int apiClientId, int apiOrderId) { }
        public void completedOrder(IbContract contract, IBApi.Order order, IBApi.OrderState orderState) { }
        public void completedOrdersEnd() { }
        public void replaceFAEnd(int reqId, string text) { }
        public void wshMetaData(int reqId, string dataJson) { }
        public void wshEventData(int reqId, string dataJson) { }
        public void historicalSchedule(int reqId, string startDateTime, string endDateTime, string timeZone, IBApi.HistoricalSession[] sessions) { }
        public void userInfo(int reqId, string whiteBrandingId) { }
        public void currentTimeInMillis(long time) { }
        public void updateMktDepth(int tickerId, int position, int operation, int side, double price, decimal size) { }
        public void updateMktDepthL2(int tickerId, int position, string marketMaker, int operation, int side, double price, decimal size, bool isSmartDepth) { }
        public void updateNewsBulletin(int msgId, int msgType, string message, string origExchange) { }
        public void tickOptionComputation(int tickerId, int field, int tickAttrib, double impliedVol, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice) { }

        // Protobuf callbacks (required by EWrapper interface in API 10.37)
        public void orderStatusProtoBuf(IBApi.protobuf.OrderStatus orderStatus) { }
        public void openOrderProtoBuf(IBApi.protobuf.OpenOrder openOrder) { }
        public void openOrdersEndProtoBuf(IBApi.protobuf.OpenOrdersEnd openOrdersEnd) { }
        public void errorProtoBuf(IBApi.protobuf.ErrorMessage errorMessage) { }
        public void execDetailsProtoBuf(IBApi.protobuf.ExecutionDetails executionDetails) { }
        public void execDetailsEndProtoBuf(IBApi.protobuf.ExecutionDetailsEnd executionDetailsEnd) { }

        /// <summary>
        /// Disposes resources used by the wrapper.
        /// </summary>
        /// <remarks>
        /// Releases all managed resources including:
        /// <list type="bullet">
        ///   <item><see cref="ManualResetEventSlim"/> instances used for synchronization</item>
        ///   <item>Market data caches and ticker handlers</item>
        ///   <item>Order and position tracking dictionaries</item>
        ///   <item>Event subscriber references</item>
        /// </list>
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Dispose all ManualResetEventSlim instances (releases native handles)
            _nextValidIdEvent.Dispose();
            _pingResponseEvent.Dispose();
            _positionsEndEvent.Dispose();

            // Clear all cached data
            _tickerHandlers.Clear();
            _lastByTicker.Clear();
            _lastSizeByTicker.Clear();
            _openOrders.Clear();
            _positions.Clear();

            // Clear event subscribers to prevent holding references
            OnLastTrade = null;
            OnOrderFill = null;
            OnOrderStatus = null;
            OnOpenOrder = null;
            OnOpenOrdersEnd = null;
            OnConnectionLost = null;
            OnConnectionRestored = null;
            OnHistoricalData = null;
            OnHistoricalDataEnd = null;
            OnPosition = null;
            OnPositionsEnd = null;

            _client = null;
        }
    }
}


