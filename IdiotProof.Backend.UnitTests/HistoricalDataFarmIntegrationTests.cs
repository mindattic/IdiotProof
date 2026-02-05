// ============================================================================
// Historical Data Farm Integration Tests
// ============================================================================
// These tests require a live connection to IBKR Gateway/TWS.
// Run IBKR Gateway on port 4002 (paper) or 4001 (live) before running.
// 
// To run: dotnet test --filter "Category=Integration"
// ============================================================================

using System.Collections.Concurrent;
using IdiotProof.Backend.Helpers;
using IdiotProof.Backend.Models;
using IdiotProof.Backend.Services;
using NUnit.Framework;

namespace IdiotProof.Backend.UnitTests;

/// <summary>
/// Integration tests that connect to a live IBKR Gateway to verify Historical Data Farm activation.
/// These tests are marked as [Explicit] so they don't run in CI - only manually.
/// </summary>
[TestFixture]
[Category("Integration")]
[Explicit("Requires live IBKR Gateway connection")]
public class HistoricalDataFarmIntegrationTests
{
    private IbWrapper? _wrapper;
    private IBApi.EClientSocket? _client;
    private IBApi.EReader? _reader;
    private bool _connected;

    // Connection settings - live trading gateway
    private const string Host = "127.0.0.1";
    private const int Port = 4001;  // 4001 = live, 4002 = paper
    private const int ClientId = 99; // Use unique client ID for tests

    [SetUp]
    public void Setup()
    {
        _wrapper = new IbWrapper();
        _client = new IBApi.EClientSocket(_wrapper, _wrapper.Signal);
        _wrapper.AttachClient(_client);
    }

    [TearDown]
    public void TearDown()
    {
        if (_connected)
        {
            _client?.eDisconnect();
            Thread.Sleep(500);
        }
        _reader = null;
        _client = null;
        _wrapper?.Dispose();
        _wrapper = null;
    }

    private bool Connect()
    {
        try
        {
            _client!.eConnect(Host, Port, ClientId);

            // Start the reader thread
            _reader = new IBApi.EReader(_client, _wrapper!.Signal);
            _reader.Start();

            // Start message processing thread
            var processingThread = new Thread(() =>
            {
                while (_client.IsConnected())
                {
                    _wrapper!.Signal.waitForSignal();
                    _reader.processMsgs();
                }
            })
            {
                IsBackground = true
            };
            processingThread.Start();

            // Wait for connection
            Thread.Sleep(1000);

            if (_client.IsConnected())
            {
                _connected = true;
                TestContext.WriteLine($"[OK] Connected to IBKR Gateway at {Host}:{Port}");
                return true;
            }

            TestContext.WriteLine($"[FAIL] Could not connect to IBKR Gateway at {Host}:{Port}");
            return false;
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"[FAIL] Connection error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Tests that requesting historical data activates the Historical Data Farm.
    /// After this call, the IBKR Gateway should show "Historical Data Farm" as green/active.
    /// </summary>
    [Test]
    public void RequestHistoricalData_ActivatesHistoricalDataFarm()
    {
        // Arrange
        if (!Connect())
        {
            Assert.Inconclusive("Could not connect to IBKR Gateway. Ensure it's running on port 4001.");
            return;
        }

        var barsReceived = new List<IBApi.Bar>();
        var dataEndReceived = new ManualResetEventSlim(false);
        string? startDate = null;
        string? endDate = null;

        // Subscribe to historical data events
        _wrapper!.OnHistoricalData += (reqId, bar) =>
        {
            barsReceived.Add(bar);
            TestContext.WriteLine($"  Bar: {bar.Time} O={bar.Open:F2} H={bar.High:F2} L={bar.Low:F2} C={bar.Close:F2} V={bar.Volume}");
        };

        _wrapper.OnHistoricalDataEnd += (reqId, start, end) =>
        {
            startDate = start;
            endDate = end;
            TestContext.WriteLine($"[OK] Historical data complete: {start} to {end}");
            dataEndReceived.Set();
        };

        // Create contract for a liquid stock
        var contract = new IBApi.Contract
        {
            Symbol = "AAPL",
            SecType = "STK",
            Currency = "USD",
            Exchange = "SMART"
        };

        // Act - Request historical data (this should activate the Historical Data Farm)
        TestContext.WriteLine("\n=== Requesting Historical Data ===");
        TestContext.WriteLine("This request should activate the Historical Data Farm in IBKR Gateway...\n");

        _client!.reqHistoricalData(
            1001,              // reqId
            contract,          // contract
            "",                // endDateTime (empty = now)
            "1 D",             // durationStr (1 day of data)
            "1 min",           // barSizeSetting (1-minute bars)
            "TRADES",          // whatToShow
            0,                 // useRTH (0 = include extended hours)
            1,                 // formatDate
            false,             // keepUpToDate
            null               // chartOptions
        );

        // Wait for data with timeout
        bool received = dataEndReceived.Wait(TimeSpan.FromSeconds(30));

        // Assert
        Assert.That(received, Is.True, "Did not receive historical data within timeout");
        Assert.That(barsReceived.Count, Is.GreaterThan(0), "Should have received at least some bars");

        TestContext.WriteLine($"\n=== RESULTS ===");
        TestContext.WriteLine($"Bars received: {barsReceived.Count}");
        TestContext.WriteLine($"Date range: {startDate} to {endDate}");
        TestContext.WriteLine($"\nCheck IBKR Gateway - Historical Data Farm should now be GREEN/Active!");
    }

    /// <summary>
    /// Tests fetching historical data using the HistoricalDataService.
    /// </summary>
    [Test]
    public async Task HistoricalDataService_FetchesRealData()
    {
        // Arrange
        if (!Connect())
        {
            Assert.Inconclusive("Could not connect to IBKR Gateway. Ensure it's running on port 4001.");
            return;
        }

        // Wait for next valid ID
        Thread.Sleep(2000);

        var store = new HistoricalDataStore();
        var service = new HistoricalDataService(_client!, _wrapper!, store);

        // Act
        TestContext.WriteLine("\n=== Fetching via HistoricalDataService ===\n");

        int barCount = await service.FetchHistoricalDataAsync(
            symbol: "SPY",
            barCount: 50,
            useRTH: false
        );

        // Assert
        Assert.That(barCount, Is.GreaterThan(0), "Should have fetched some bars");

        var dataSet = store.GetHistoricalData("SPY");
        Assert.That(dataSet, Is.Not.Null);
        Assert.That(dataSet!.BarCount, Is.GreaterThan(0));

        var bars = dataSet.Bars;
        TestContext.WriteLine($"\n=== RESULTS ===");
        TestContext.WriteLine($"Bars stored: {bars.Count}");
        TestContext.WriteLine($"First bar: {bars.First().Time:yyyy-MM-dd HH:mm}");
        TestContext.WriteLine($"Last bar: {bars.Last().Time:yyyy-MM-dd HH:mm}");
        TestContext.WriteLine($"\nFirst 5 bars:");
        foreach (var bar in bars.Take(5))
        {
            TestContext.WriteLine($"  {bar.Time:HH:mm} O={bar.Open:F2} H={bar.High:F2} L={bar.Low:F2} C={bar.Close:F2} V={bar.Volume:N0}");
        }
    }

    /// <summary>
    /// Simple ping test to verify connection is working.
    /// </summary>
    [Test]
    public void Ping_VerifiesConnection()
    {
        // Arrange
        if (!Connect())
        {
            Assert.Inconclusive("Could not connect to IBKR Gateway. Ensure it's running on port 4001.");
            return;
        }

        // Wait for connection to stabilize
        Thread.Sleep(1000);

        // Act
        var pingResult = _wrapper!.Ping(TimeSpan.FromSeconds(5));

        // Assert
        Assert.That(pingResult.Success, Is.True, "Ping should succeed");
        Assert.That(pingResult.LatencyMs, Is.GreaterThan(0), "Latency should be positive");

        TestContext.WriteLine($"\n=== PING RESULTS ===");
        TestContext.WriteLine($"Success: {pingResult.Success}");
        TestContext.WriteLine($"Latency: {pingResult.LatencyMs}ms");
        TestContext.WriteLine($"Server Time (UTC): {pingResult.ServerTimeUtc:yyyy-MM-dd HH:mm:ss}");
    }

    /// <summary>
    /// Tests that requesting market data activates the Market Data Farm.
    /// After this call, the IBKR Gateway should show "Market Data Farm" as green/active.
    /// </summary>
    [Test]
    public void RequestMarketData_ActivatesMarketDataFarm()
    {
        // Arrange
        if (!Connect())
        {
            Assert.Inconclusive("Could not connect to IBKR Gateway. Ensure it's running on port 4001.");
            return;
        }

        var ticksReceived = new List<(int tickType, double price)>();
        var dataReceived = new ManualResetEventSlim(false);

        // Subscribe to market data events via the wrapper's OnLastTrade
        _wrapper!.OnLastTrade += (price, size) =>
        {
            ticksReceived.Add((0, price)); // 0 = LAST
            TestContext.WriteLine($"  LAST: ${price:F2} x {size}");
            dataReceived.Set();
        };

        // Create contract for a liquid stock
        var contract = new IBApi.Contract
        {
            Symbol = "AAPL",
            SecType = "STK",
            Currency = "USD",
            Exchange = "SMART"
        };

        // Act - Request market data (this should activate the Market Data Farm)
        TestContext.WriteLine("\n=== Requesting Real-Time Market Data ===");
        TestContext.WriteLine("This request should activate the Market Data Farm in IBKR Gateway...\n");

        int tickerId = 2001;
        _client!.reqMktData(
            tickerId,          // tickerId
            contract,          // contract
            "",                // genericTickList (empty = default ticks)
            false,             // snapshot (false = streaming)
            false,             // regulatorySnapshot
            null               // mktDataOptions
        );

        // Wait for some data with timeout (market data is streaming, so we just need a few ticks)
        bool received = dataReceived.Wait(TimeSpan.FromSeconds(10));

        // Cancel the subscription
        _client.cancelMktData(tickerId);

        // Assert
        Assert.That(received, Is.True, "Did not receive market data within timeout. Market may be closed.");

        TestContext.WriteLine($"\n=== RESULTS ===");
        TestContext.WriteLine($"Ticks received: {ticksReceived.Count}");
        TestContext.WriteLine($"\nCheck IBKR Gateway - Market Data Farm should now be GREEN/Active!");
    }

    /// <summary>
    /// Tests subscribing to multiple symbols' market data simultaneously.
    /// </summary>
    [Test]
    public void RequestMultipleMarketData_StreamsMultipleSymbols()
    {
        // Arrange
        if (!Connect())
        {
            Assert.Inconclusive("Could not connect to IBKR Gateway. Ensure it's running on port 4001.");
            return;
        }

        var symbols = new[] { "AAPL", "MSFT", "GOOGL" };
        var tickerIds = new Dictionary<string, int>
        {
            ["AAPL"] = 3001,
            ["MSFT"] = 3002,
            ["GOOGL"] = 3003
        };
        var dataReceived = new ConcurrentDictionary<int, List<double>>();
        var allDataReceived = new CountdownEvent(symbols.Length);

        foreach (var id in tickerIds.Values)
        {
            dataReceived[id] = new List<double>();
        }

        // Register handler for each ticker
        foreach (var kvp in tickerIds)
        {
            string symbol = kvp.Key;
            int tickerId = kvp.Value;
            _wrapper!.RegisterTickerHandler(tickerId, (price, size) =>
            {
                if (dataReceived[tickerId].Count == 0)
                {
                    TestContext.WriteLine($"  [{symbol}] First tick: ${price:F2} x {size}");
                    allDataReceived.Signal();
                }
                dataReceived[tickerId].Add(price);
            });
        }

        // Act - Request market data for each symbol
        TestContext.WriteLine("\n=== Requesting Multi-Symbol Market Data ===\n");

        foreach (var kvp in tickerIds)
        {
            var contract = new IBApi.Contract
            {
                Symbol = kvp.Key,
                SecType = "STK",
                Currency = "USD",
                Exchange = "SMART"
            };

            _client!.reqMktData(kvp.Value, contract, "", false, false, null);
            TestContext.WriteLine($"  Subscribed to {kvp.Key} (tickerId={kvp.Value})");
        }

        // Wait for at least one tick from each symbol
        bool allReceived = allDataReceived.Wait(TimeSpan.FromSeconds(15));

        // Cancel all subscriptions
        foreach (var tickerId in tickerIds.Values)
        {
            _client!.cancelMktData(tickerId);
            _wrapper!.UnregisterTickerHandler(tickerId);
        }

        // Assert
        TestContext.WriteLine($"\n=== RESULTS ===");
        foreach (var kvp in tickerIds)
        {
            int count = dataReceived[kvp.Value].Count;
            TestContext.WriteLine($"  [{kvp.Key}] Ticks received: {count}");
        }

        Assert.That(allReceived, Is.True, "Did not receive data from all symbols. Market may be closed.");
    }

    /// <summary>
    /// Tests that both Market Data Farm and Historical Data Farm can be activated together.
    /// </summary>
    [Test]
    public void ActivateBothDataFarms()
    {
        // Arrange
        if (!Connect())
        {
            Assert.Inconclusive("Could not connect to IBKR Gateway. Ensure it's running on port 4001.");
            return;
        }

        var historicalDataReceived = new ManualResetEventSlim(false);
        var marketDataReceived = new ManualResetEventSlim(false);
        int historicalBarCount = 0;
        int marketTickCount = 0;

        // Subscribe to historical data events
        _wrapper!.OnHistoricalDataEnd += (reqId, start, end) =>
        {
            TestContext.WriteLine($"[OK] Historical data complete: {historicalBarCount} bars");
            historicalDataReceived.Set();
        };

        _wrapper.OnHistoricalData += (reqId, bar) =>
        {
            historicalBarCount++;
        };

        // Subscribe to market data events
        int marketTickerId = 4001;
        _wrapper.RegisterTickerHandler(marketTickerId, (price, size) =>
        {
            if (marketTickCount == 0)
            {
                TestContext.WriteLine($"[OK] First market tick: ${price:F2}");
            }
            marketTickCount++;
            marketDataReceived.Set();
        });

        // Create contract
        var contract = new IBApi.Contract
        {
            Symbol = "SPY",
            SecType = "STK",
            Currency = "USD",
            Exchange = "SMART"
        };

        // Act
        TestContext.WriteLine("\n=== Activating Both Data Farms ===\n");

        // Request historical data (activates Historical Data Farm)
        TestContext.WriteLine("1. Requesting historical data...");
        _client!.reqHistoricalData(
            5001, contract, "", "1 D", "1 min", "TRADES", 0, 1, false, null
        );

        // Request market data (activates Market Data Farm)
        TestContext.WriteLine("2. Requesting market data...");
        _client.reqMktData(marketTickerId, contract, "", false, false, null);

        // Wait for both
        bool gotHistorical = historicalDataReceived.Wait(TimeSpan.FromSeconds(30));
        bool gotMarket = marketDataReceived.Wait(TimeSpan.FromSeconds(10));

        // Cleanup
        _client.cancelMktData(marketTickerId);
        _wrapper.UnregisterTickerHandler(marketTickerId);

        // Assert
        TestContext.WriteLine($"\n=== RESULTS ===");
        TestContext.WriteLine($"Historical bars: {historicalBarCount}");
        TestContext.WriteLine($"Market ticks: {marketTickCount}");
        TestContext.WriteLine($"\nBoth Data Farms should now be GREEN in IBKR Gateway!");

        Assert.That(gotHistorical, Is.True, "Did not receive historical data");
        // Market data might not come if market is closed, so we're lenient
        if (!gotMarket)
        {
            TestContext.WriteLine("WARNING: No market ticks received - market may be closed");
        }
    }
}


