// ============================================================================
// Web Frontend Client - Pushes Live Data from Core to Web
// ============================================================================
// This runs in IdiotProof.Core and sends price ticks, candles, and alerts
// to the IdiotProof.Web frontend via HTTP.
//
// Usage:
// 1. Initialize with Web frontend URL
// 2. Call OnPriceTick() when IBKR sends price updates
// 3. Call OnCandleComplete() when a candle closes
// 4. Call SendAlert() when SuddenMoveDetector fires
// ============================================================================

using System.Net.Http.Json;
using System.Text.Json;

namespace IdiotProof.Services;

/// <summary>
/// Configuration for web frontend connection.
/// </summary>
public sealed class WebFrontendConfig
{
    /// <summary>
    /// Base URL of the IdiotProof.Web frontend.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";
    
    /// <summary>
    /// Whether to enable pushing data to web frontend.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Timeout for HTTP requests.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// Whether to batch ticks (more efficient).
    /// </summary>
    public bool BatchTicks { get; set; } = true;
    
    /// <summary>
    /// Batch size before sending.
    /// </summary>
    public int BatchSize { get; set; } = 10;
    
    /// <summary>
    /// Maximum time to hold a batch before sending.
    /// </summary>
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Pushes market data to the web frontend.
/// </summary>
public sealed class WebFrontendClient : IDisposable
{
    private readonly WebFrontendConfig _config;
    private readonly HttpClient _httpClient;
    private readonly List<TickPayload> _tickBatch = new();
    private readonly object _batchLock = new();
    private readonly Timer? _batchTimer;
    private bool _disposed;
    
    public WebFrontendClient(WebFrontendConfig? config = null)
    {
        _config = config ?? new WebFrontendConfig();
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_config.BaseUrl),
            Timeout = _config.Timeout
        };
        
        if (_config.BatchTicks)
        {
            _batchTimer = new Timer(FlushBatch, null, _config.BatchTimeout, _config.BatchTimeout);
        }
    }
    
    /// <summary>
    /// Sends a price tick to the web frontend.
    /// </summary>
    public async Task OnPriceTickAsync(string symbol, double price, double bid = 0, double ask = 0, long volume = 0)
    {
        if (!_config.Enabled) return;
        
        var tick = new TickPayload
        {
            Symbol = symbol,
            Price = price,
            Bid = bid,
            Ask = ask,
            Volume = volume
        };
        
        if (_config.BatchTicks)
        {
            lock (_batchLock)
            {
                _tickBatch.Add(tick);
                
                if (_tickBatch.Count >= _config.BatchSize)
                {
                    _ = FlushBatchAsync();
                }
            }
        }
        else
        {
            await SendTickAsync(tick);
        }
    }
    
    /// <summary>
    /// Sends a completed candle to the web frontend.
    /// </summary>
    public async Task OnCandleCompleteAsync(string symbol, DateTime time, double open, double high, double low, double close, long volume)
    {
        if (!_config.Enabled) return;
        
        try
        {
            var candle = new
            {
                symbol,
                time = new DateTimeOffset(time).ToUnixTimeSeconds(),
                open,
                high,
                low,
                close,
                volume
            };
            
            await _httpClient.PostAsJsonAsync("/api/marketdata/candle", candle);
        }
        catch (Exception ex)
        {
            // Log but don't throw - web frontend being down shouldn't stop trading
            Console.WriteLine($"[WebClient] Failed to send candle: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sets daily reference data for a symbol.
    /// </summary>
    public async Task SetDailyDataAsync(string symbol, double prevClose, double dayOpen, double dayHigh, double dayLow, double avgVolume)
    {
        if (!_config.Enabled) return;
        
        try
        {
            var data = new
            {
                symbol,
                prevClose,
                dayOpen,
                dayHigh,
                dayLow,
                avgVolume
            };
            
            await _httpClient.PostAsJsonAsync("/api/marketdata/daily", data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebClient] Failed to set daily data: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sends an alert to the web frontend.
    /// </summary>
    public async Task SendAlertAsync(
        string symbol,
        string type,
        string severity,
        double price,
        double changePercent,
        int confidence,
        string reason,
        object? longSetup = null,
        object? shortSetup = null)
    {
        if (!_config.Enabled) return;
        
        try
        {
            var alert = new
            {
                symbol,
                type,
                severity,
                price,
                changePercent,
                confidence,
                reason,
                longSetup,
                shortSetup
            };
            
            await _httpClient.PostAsJsonAsync("/api/marketdata/alert", alert);
            Console.WriteLine($"[WebClient] Alert sent: {symbol} {type}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebClient] Failed to send alert: {ex.Message}");
        }
    }
    
    private async Task SendTickAsync(TickPayload tick)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("/api/marketdata/tick", tick);
        }
        catch
        {
            // Silent fail - don't spam console with tick errors
        }
    }
    
    private void FlushBatch(object? state)
    {
        _ = FlushBatchAsync();
    }
    
    private async Task FlushBatchAsync()
    {
        TickPayload[] ticksToSend;
        
        lock (_batchLock)
        {
            if (_tickBatch.Count == 0) return;
            
            ticksToSend = _tickBatch.ToArray();
            _tickBatch.Clear();
        }
        
        try
        {
            await _httpClient.PostAsJsonAsync("/api/marketdata/ticks", ticksToSend);
        }
        catch
        {
            // Silent fail
        }
    }
    
    /// <summary>
    /// Tests connection to web frontend.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/marketdata/symbols");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _batchTimer?.Dispose();
        _httpClient.Dispose();
    }
    
    private sealed class TickPayload
    {
        public string Symbol { get; set; } = "";
        public double Price { get; set; }
        public double Bid { get; set; }
        public double Ask { get; set; }
        public long Volume { get; set; }
    }
}
