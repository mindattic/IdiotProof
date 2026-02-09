// ============================================================================
// Live Trading Test Runner
// ============================================================================
//
// Executes live order tests during RTH to validate BUY/SELL/SHORT/COVER.
//
// TEST SEQUENCE:
// 1. Check RTH hours and IBKR connection
// 2. Get current price for test stock
// 3. Test BUY (enter long) - places market buy, waits for fill
// 4. Test SELL (exit long) - places market sell, waits for fill
// 5. Test SHORT (enter short) - places market sell short, waits for fill
// 6. Test COVER (exit short) - places market buy to cover, waits for fill
// 7. Verify all positions are flat
// 8. Report results
//
// SAFETY:
// - Uses 1 share quantity
// - Market orders for fast fills
// - 30 second timeout per order
// - Automatic cleanup on failure
// - Paper trading recommended
//
// ============================================================================

using IBApi;
using IdiotProof.Helpers;
using IdiotProof.Logging;
using IdiotProof.Models;
using IdiotProof.Settings;
using System.Diagnostics;
using IbContract = IBApi.Contract;

namespace IdiotProof.Testing;

/// <summary>
/// Runs live trading tests to validate order execution.
/// </summary>
public sealed class LiveTradingTestRunner : IDisposable
{
    private readonly IbWrapper _wrapper;
    private readonly EClientSocket _client;
    private readonly LiveTradingTestConfig _config;
    private readonly LiveTestSummary _summary;
    
    // Contract for test stock
    private IbContract? _contract;
    
    // Price tracking
    private double _lastPrice;
    private double _bidPrice;
    private double _askPrice;
    
    // Order tracking
    private int _pendingOrderId = -1;
    private bool _orderFilled;
    private double _fillPrice;
    private int _filledQuantity;
    private string? _orderError;
    private readonly ManualResetEventSlim _fillEvent = new(false);
    
    // Position tracking
    private int _currentPosition;
    
    // Quantity to trade (calculated from price)
    private int _testQuantity;
    
    // State
    private bool _disposed;

    public LiveTradingTestRunner(IbWrapper wrapper, EClientSocket client, LiveTradingTestConfig? config = null)
    {
        _wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _config = config ?? new LiveTradingTestConfig();
        _summary = new LiveTestSummary { Symbol = _config.Symbol };
        
        // Validate config
        _config.Validate();
    }

    /// <summary>
    /// Runs all live trading tests.
    /// </summary>
    /// <returns>Summary of test results.</returns>
    public async Task<LiveTestSummary> RunAllTestsAsync()
    {
        _summary.TestStartTime = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Pre-flight checks
            if (!RunPreFlightChecks())
            {
                _summary.TestEndTime = DateTime.Now;
                _summary.TotalDuration = stopwatch.Elapsed;
                return _summary;
            }

            // Subscribe to events
            SubscribeToEvents();

            // Subscribe to market data
            await SubscribeToMarketDataAsync();

            // Wait for initial price
            if (!await WaitForPriceAsync())
            {
                _summary.AddResult(TestResult.Fail("MarketData", "Failed to receive market data within timeout"));
                return _summary;
            }

            Log($"Current price for {_config.Symbol}: ${_lastPrice:F2} (Bid: ${_bidPrice:F2}, Ask: ${_askPrice:F2})", ConsoleColor.Cyan);

            // Calculate quantity: price * 3, rounded up to nearest 5
            _testQuantity = _config.Quantity > 0 
                ? _config.Quantity 
                : LiveTradingTestConfig.CalculateQuantity(_lastPrice);
            
            var estimatedCost = _lastPrice * _testQuantity;
            Log($"Test quantity: {_testQuantity} shares (~${estimatedCost:F2} per trade)", ConsoleColor.Cyan);

            // Run test sequence
            if (_config.FullSuite)
            {
                // Test 1: BUY (enter long)
                await RunBuyTestAsync();

                // Test 2: SELL (exit long)
                if (_currentPosition > 0)
                {
                    await RunSellTestAsync();
                }

                // Wait between test groups
                await Task.Delay(LiveTradingTestConfig.DelayBetweenTestsMs);

                // Test 3: SHORT (enter short) - if not skipped
                if (!_config.SkipShortTests)
                {
                    await RunShortTestAsync();

                    // Test 4: COVER (exit short)
                    if (_currentPosition < 0)
                    {
                        await RunCoverTestAsync();
                    }
                }
                else
                {
                    Log("Skipping SHORT/COVER tests as configured", ConsoleColor.Yellow);
                }
            }

            // Cleanup: Ensure we're flat
            if (_config.AutoCleanup && _currentPosition != 0)
            {
                await CleanupPositionAsync();
            }

            // Final position check
            await RunPositionCheckAsync();
        }
        catch (Exception ex)
        {
            _summary.AddResult(TestResult.Fail("Unexpected", $"Test runner error: {ex.Message}", ex.StackTrace));
            
            // Emergency cleanup
            if (_config.AutoCleanup && _currentPosition != 0)
            {
                try
                {
                    await CleanupPositionAsync();
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
        finally
        {
            UnsubscribeFromEvents();
            stopwatch.Stop();
            _summary.TotalDuration = stopwatch.Elapsed;
            _summary.TestEndTime = DateTime.Now;
        }

        return _summary;
    }

    /// <summary>
    /// Runs pre-flight checks before testing.
    /// </summary>
    private bool RunPreFlightChecks()
    {
        // Check 1: RTH hours
        var now = TimezoneHelper.GetCurrentTime(IdiotProof.Enums.MarketTimeZone.EST);
        var rthStart = new TimeOnly(9, 30);
        var rthEnd = new TimeOnly(16, 0);

        if (now < rthStart || now >= rthEnd)
        {
            _summary.AddResult(TestResult.Fail("RTH Check", 
                $"Tests must run during RTH (9:30 AM - 4:00 PM ET). Current time: {now}"));
            return false;
        }
        _summary.AddResult(TestResult.Pass("RTH Check", $"Within RTH window ({now})"));

        // Check 2: Connection
        if (!_wrapper.IsConnected)
        {
            _summary.AddResult(TestResult.Fail("Connection", "Not connected to IBKR"));
            return false;
        }
        _summary.AddResult(TestResult.Pass("Connection", "Connected to IBKR"));

        // Check 3: Paper trading warning
        if (!AppSettings.IsPaperTrading)
        {
            Log("WARNING: Running live trading tests on LIVE account!", ConsoleColor.Red);
            Log("Press Ctrl+C to abort or any key to continue...", ConsoleColor.Yellow);
            Console.ReadKey(true);
        }
        else
        {
            _summary.AddResult(TestResult.Pass("Account Type", "Paper trading account"));
        }

        return true;
    }

    /// <summary>
    /// Subscribes to market data for the test stock.
    /// </summary>
    private Task SubscribeToMarketDataAsync()
    {
        _contract = new IbContract
        {
            Symbol = _config.Symbol,
            SecType = "STK",
            Exchange = "SMART",
            Currency = "USD"
        };

        int tickerId = _wrapper.ConsumeNextOrderId();
        
        _wrapper.RegisterTickerHandler(tickerId, (price, size) =>
        {
            _lastPrice = price;
        });

        // Request market data
        _client.reqMktData(tickerId, _contract, "", false, false, null);
        
        Log($"Subscribed to market data for {_config.Symbol} (tickerId: {tickerId})", ConsoleColor.DarkGray);
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits for initial price data.
    /// </summary>
    private async Task<bool> WaitForPriceAsync()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (_lastPrice > 0)
            {
                return true;
            }
            await Task.Delay(100);
        }

        return false;
    }

    /// <summary>
    /// Test: BUY (enter long position).
    /// </summary>
    private async Task RunBuyTestAsync()
    {
        Log("\n=== TEST: BUY (Enter Long) ===", ConsoleColor.Yellow);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ResetOrderState();
            
            var orderId = _wrapper.ConsumeNextOrderId();
            _pendingOrderId = orderId;

            var order = new Order
            {
                Action = "BUY",
                OrderType = "MKT",
                TotalQuantity = _config.Quantity,
                Tif = "GTC",
                Account = AppSettings.AccountNumber
            };

            Log($"Placing BUY order: {_config.Quantity} shares of {_config.Symbol} @ MKT (OrderId: {orderId})", ConsoleColor.Cyan);
            
            _client.placeOrder(orderId, _contract, order);

            // Wait for fill
            var filled = await WaitForFillAsync(LiveTradingTestConfig.OrderFillTimeoutSeconds);
            stopwatch.Stop();

            if (filled)
            {
                _currentPosition += _filledQuantity;
                _summary.AddResult(TestResult.Pass("BUY", 
                    $"Filled {_filledQuantity} @ ${_fillPrice:F2}", 
                    _fillPrice, 
                    orderId, 
                    stopwatch.Elapsed));
                Log($"[OK] BUY filled @ ${_fillPrice:F2} in {stopwatch.ElapsedMilliseconds}ms", ConsoleColor.Green);
            }
            else
            {
                var error = _orderError ?? "Order did not fill within timeout";
                _summary.AddResult(TestResult.Fail("BUY", error, _orderError));
                Log($"[FAIL] BUY failed: {error}", ConsoleColor.Red);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _summary.AddResult(TestResult.Fail("BUY", $"Exception: {ex.Message}", ex.StackTrace));
            Log($"[FAIL] BUY exception: {ex.Message}", ConsoleColor.Red);
        }
    }

    /// <summary>
    /// Test: SELL (exit long position).
    /// </summary>
    private async Task RunSellTestAsync()
    {
        Log("\n=== TEST: SELL (Exit Long) ===", ConsoleColor.Yellow);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ResetOrderState();
            
            var orderId = _wrapper.ConsumeNextOrderId();
            _pendingOrderId = orderId;

            var order = new Order
            {
                Action = "SELL",
                OrderType = "MKT",
                TotalQuantity = _config.Quantity,
                Tif = "GTC",
                Account = AppSettings.AccountNumber
            };

            Log($"Placing SELL order: {_config.Quantity} shares of {_config.Symbol} @ MKT (OrderId: {orderId})", ConsoleColor.Cyan);
            
            _client.placeOrder(orderId, _contract, order);

            // Wait for fill
            var filled = await WaitForFillAsync(LiveTradingTestConfig.OrderFillTimeoutSeconds);
            stopwatch.Stop();

            if (filled)
            {
                _currentPosition -= _filledQuantity;
                _summary.AddResult(TestResult.Pass("SELL", 
                    $"Filled {_filledQuantity} @ ${_fillPrice:F2}", 
                    _fillPrice, 
                    orderId, 
                    stopwatch.Elapsed));
                Log($"[OK] SELL filled @ ${_fillPrice:F2} in {stopwatch.ElapsedMilliseconds}ms", ConsoleColor.Green);
            }
            else
            {
                var error = _orderError ?? "Order did not fill within timeout";
                _summary.AddResult(TestResult.Fail("SELL", error, _orderError));
                Log($"[FAIL] SELL failed: {error}", ConsoleColor.Red);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _summary.AddResult(TestResult.Fail("SELL", $"Exception: {ex.Message}", ex.StackTrace));
            Log($"[FAIL] SELL exception: {ex.Message}", ConsoleColor.Red);
        }
    }

    /// <summary>
    /// Test: SHORT (enter short position via sell).
    /// </summary>
    private async Task RunShortTestAsync()
    {
        Log("\n=== TEST: SHORT (Enter Short) ===", ConsoleColor.Yellow);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ResetOrderState();
            
            var orderId = _wrapper.ConsumeNextOrderId();
            _pendingOrderId = orderId;

            // SHORT is done by selling shares you don't own
            // IBKR automatically treats this as a short sale
            var order = new Order
            {
                Action = "SELL",
                OrderType = "MKT",
                TotalQuantity = _config.Quantity,
                Tif = "GTC",
                Account = AppSettings.AccountNumber
            };

            Log($"Placing SHORT order: {_config.Quantity} shares of {_config.Symbol} @ MKT (OrderId: {orderId})", ConsoleColor.Cyan);
            
            _client.placeOrder(orderId, _contract, order);

            // Wait for fill
            var filled = await WaitForFillAsync(LiveTradingTestConfig.OrderFillTimeoutSeconds);
            stopwatch.Stop();

            if (filled)
            {
                _currentPosition -= _filledQuantity;
                _summary.AddResult(TestResult.Pass("SHORT", 
                    $"Filled {_filledQuantity} @ ${_fillPrice:F2}", 
                    _fillPrice, 
                    orderId, 
                    stopwatch.Elapsed));
                Log($"[OK] SHORT filled @ ${_fillPrice:F2} in {stopwatch.ElapsedMilliseconds}ms", ConsoleColor.Green);
            }
            else
            {
                var error = _orderError ?? "Order did not fill within timeout";
                
                // Check if this is a short sale rejection
                if (error.Contains("short", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("locate", StringComparison.OrdinalIgnoreCase))
                {
                    _summary.AddResult(TestResult.Fail("SHORT", 
                        $"Stock not shortable: {error}", 
                        "Try a different stock or check margin requirements"));
                }
                else
                {
                    _summary.AddResult(TestResult.Fail("SHORT", error, _orderError));
                }
                Log($"[FAIL] SHORT failed: {error}", ConsoleColor.Red);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _summary.AddResult(TestResult.Fail("SHORT", $"Exception: {ex.Message}", ex.StackTrace));
            Log($"[FAIL] SHORT exception: {ex.Message}", ConsoleColor.Red);
        }
    }

    /// <summary>
    /// Test: COVER (exit short position via buy).
    /// </summary>
    private async Task RunCoverTestAsync()
    {
        Log("\n=== TEST: COVER (Exit Short) ===", ConsoleColor.Yellow);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ResetOrderState();
            
            var orderId = _wrapper.ConsumeNextOrderId();
            _pendingOrderId = orderId;

            // COVER is done by buying shares to close the short position
            var order = new Order
            {
                Action = "BUY",
                OrderType = "MKT",
                TotalQuantity = _config.Quantity,
                Tif = "GTC",
                Account = AppSettings.AccountNumber
            };

            Log($"Placing COVER order: {_config.Quantity} shares of {_config.Symbol} @ MKT (OrderId: {orderId})", ConsoleColor.Cyan);
            
            _client.placeOrder(orderId, _contract, order);

            // Wait for fill
            var filled = await WaitForFillAsync(LiveTradingTestConfig.OrderFillTimeoutSeconds);
            stopwatch.Stop();

            if (filled)
            {
                _currentPosition += _filledQuantity;
                _summary.AddResult(TestResult.Pass("COVER", 
                    $"Filled {_filledQuantity} @ ${_fillPrice:F2}", 
                    _fillPrice, 
                    orderId, 
                    stopwatch.Elapsed));
                Log($"[OK] COVER filled @ ${_fillPrice:F2} in {stopwatch.ElapsedMilliseconds}ms", ConsoleColor.Green);
            }
            else
            {
                var error = _orderError ?? "Order did not fill within timeout";
                _summary.AddResult(TestResult.Fail("COVER", error, _orderError));
                Log($"[FAIL] COVER failed: {error}", ConsoleColor.Red);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _summary.AddResult(TestResult.Fail("COVER", $"Exception: {ex.Message}", ex.StackTrace));
            Log($"[FAIL] COVER exception: {ex.Message}", ConsoleColor.Red);
        }
    }

    /// <summary>
    /// Check that position is flat.
    /// </summary>
    private Task RunPositionCheckAsync()
    {
        Log("\n=== POSITION CHECK ===", ConsoleColor.Yellow);

        if (_currentPosition == 0)
        {
            _summary.AddResult(TestResult.Pass("Position Check", "Position is flat (0 shares)"));
            Log("[OK] Position is flat", ConsoleColor.Green);
        }
        else
        {
            _summary.AddResult(TestResult.Fail("Position Check", 
                $"Position is not flat: {_currentPosition} shares",
                "Manual cleanup may be required"));
            Log($"[FAIL] Position not flat: {_currentPosition} shares", ConsoleColor.Red);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Emergency cleanup of any open position.
    /// </summary>
    private async Task CleanupPositionAsync()
    {
        Log("\n=== CLEANUP ===", ConsoleColor.Yellow);

        try
        {
            ResetOrderState();
            
            var orderId = _wrapper.ConsumeNextOrderId();
            _pendingOrderId = orderId;

            string action = _currentPosition > 0 ? "SELL" : "BUY";
            int quantity = Math.Abs(_currentPosition);

            var order = new Order
            {
                Action = action,
                OrderType = "MKT",
                TotalQuantity = quantity,
                Tif = "GTC",
                Account = AppSettings.AccountNumber
            };

            Log($"Cleanup: {action} {quantity} shares to flatten position", ConsoleColor.Yellow);
            
            _client.placeOrder(orderId, _contract, order);

            var filled = await WaitForFillAsync(30);

            if (filled)
            {
                _currentPosition = 0;
                Log($"[OK] Cleanup complete @ ${_fillPrice:F2}", ConsoleColor.Green);
            }
            else
            {
                Log($"[FAIL] Cleanup failed: {_orderError ?? "Timeout"}", ConsoleColor.Red);
            }
        }
        catch (Exception ex)
        {
            Log($"[FAIL] Cleanup exception: {ex.Message}", ConsoleColor.Red);
        }
    }

    /// <summary>
    /// Resets order tracking state for a new order.
    /// </summary>
    private void ResetOrderState()
    {
        _pendingOrderId = -1;
        _orderFilled = false;
        _fillPrice = 0;
        _filledQuantity = 0;
        _orderError = null;
        _fillEvent.Reset();
    }

    /// <summary>
    /// Waits for an order to fill.
    /// </summary>
    private async Task<bool> WaitForFillAsync(int timeoutSeconds)
    {
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (_orderFilled)
            {
                return true;
            }

            if (_orderError != null)
            {
                return false;
            }

            await Task.Delay(50);
        }

        return false;
    }

    /// <summary>
    /// Subscribes to order fill events.
    /// </summary>
    private void SubscribeToEvents()
    {
        _wrapper.OnOrderFill += OnOrderFill;
        _wrapper.OnOrderRejected += OnOrderRejected;
    }

    /// <summary>
    /// Unsubscribes from order fill events.
    /// </summary>
    private void UnsubscribeFromEvents()
    {
        _wrapper.OnOrderFill -= OnOrderFill;
        _wrapper.OnOrderRejected -= OnOrderRejected;
    }

    /// <summary>
    /// Handles order fill events.
    /// </summary>
    private void OnOrderFill(int orderId, double avgFillPrice, int filledQty)
    {
        if (orderId != _pendingOrderId) return;

        _orderFilled = true;
        _fillPrice = avgFillPrice;
        _filledQuantity = filledQty;
        _fillEvent.Set();

        Log($"  Fill received: OrderId={orderId}, Price=${avgFillPrice:F2}, Qty={filledQty}", ConsoleColor.DarkGray);
    }

    /// <summary>
    /// Handles order rejection events.
    /// </summary>
    private void OnOrderRejected(int orderId, int errorCode, string reason)
    {
        if (orderId != _pendingOrderId) return;

        _orderError = $"Error {errorCode}: {reason}";
        _fillEvent.Set();

        Log($"  Order rejected: OrderId={orderId}, Code={errorCode}, Reason={reason}", ConsoleColor.DarkRed);
    }

    /// <summary>
    /// Logs a message to console.
    /// </summary>
    private void Log(string message, ConsoleColor? color = null)
    {
        if (color.HasValue)
        {
            Console.ForegroundColor = color.Value;
            Console.WriteLine(message);
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeFromEvents();
        _fillEvent.Dispose();
    }
}
