// ============================================================================
// Chart Data API Controller
// ============================================================================
// Provides REST endpoints for chart data, historical candles, and indicators
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using IdiotProof.Web.Services.TradingView;
using IdiotProof.Shared;

namespace IdiotProof.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChartController : ControllerBase
{
    private readonly HistoricalDataProvider _historicalData;
    private readonly ChartDataService _chartService;
    private readonly ILogger<ChartController> _logger;
    
    public ChartController(
        HistoricalDataProvider historicalData,
        ChartDataService chartService,
        ILogger<ChartController> logger)
    {
        _historicalData = historicalData;
        _chartService = chartService;
        _logger = logger;
    }
    
    /// <summary>
    /// Get available symbols with historical data.
    /// </summary>
    [HttpGet("symbols")]
    public ActionResult<List<string>> GetSymbols()
    {
        return _historicalData.GetAvailableSymbols();
    }
    
    /// <summary>
    /// Get available trading days for a symbol.
    /// </summary>
    [HttpGet("{symbol}/dates")]
    public async Task<ActionResult<List<string>>> GetTradingDays(string symbol, CancellationToken ct)
    {
        var days = await _historicalData.GetTradingDaysAsync(symbol, ct);
        return days.Select(d => d.ToString("yyyy-MM-dd")).ToList();
    }
    
    /// <summary>
    /// Get historical metadata for a symbol.
    /// </summary>
    [HttpGet("{symbol}/metadata")]
    public async Task<ActionResult<object>> GetMetadata(string symbol, CancellationToken ct)
    {
        var data = await _historicalData.LoadHistoricalDataAsync(symbol, ct);
        if (data == null)
            return NotFound($"No historical data found for {symbol}");
        
        return new
        {
            symbol = data.Symbol,
            fetchedAtUtc = data.FetchedAtUtc,
            barCount = data.BarCount,
            firstBarTime = data.FirstBarTime,
            lastBarTime = data.LastBarTime
        };
    }
    
    /// <summary>
    /// Get candle data for a symbol.
    /// </summary>
    [HttpGet("{symbol}/candles")]
    public async Task<ActionResult<List<ChartDataPoint>>> GetCandles(
        string symbol,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int interval = 1,
        CancellationToken ct = default)
    {
        var candles = await _historicalData.GetCandlesAsync(symbol, from, to, interval, ct);
        
        if (candles.Count == 0)
            return NotFound($"No candle data found for {symbol}");
        
        return candles;
    }
    
    /// <summary>
    /// Get candle data for a specific day.
    /// </summary>
    [HttpGet("{symbol}/candles/{date}")]
    public async Task<ActionResult<List<ChartDataPoint>>> GetDayCandles(
        string symbol,
        string date,
        [FromQuery] int interval = 1,
        CancellationToken ct = default)
    {
        if (!DateTime.TryParse(date, out var parsedDate))
            return BadRequest("Invalid date format. Use yyyy-MM-dd");
        
        var candles = await _historicalData.GetDayDataAsync(symbol, parsedDate, interval, ct);
        
        if (candles.Count == 0)
            return NotFound($"No candle data found for {symbol} on {date}");
        
        return candles;
    }
    
    /// <summary>
    /// Get chart bundle with candles and indicators.
    /// </summary>
    [HttpGet("{symbol}/bundle")]
    public async Task<ActionResult<ChartBundle>> GetChartBundle(
        string symbol,
        [FromQuery] string date,
        [FromQuery] int interval = 1,
        [FromQuery] bool vwap = true,
        [FromQuery] bool ema9 = true,
        [FromQuery] bool ema21 = true,
        [FromQuery] bool ema50 = false,
        [FromQuery] bool rsi = true,
        [FromQuery] bool macd = true,
        CancellationToken ct = default)
    {
        if (!DateTime.TryParse(date, out var parsedDate))
            return BadRequest("Invalid date format. Use yyyy-MM-dd");
        
        var candles = await _historicalData.GetDayDataAsync(symbol, parsedDate, interval, ct);
        
        if (candles.Count == 0)
            return NotFound($"No candle data found for {symbol} on {date}");
        
        var config = new ChartConfig
        {
            Symbol = symbol,
            Interval = interval.ToString(),
            ShowVwap = vwap,
            ShowEma9 = ema9,
            ShowEma21 = ema21,
            ShowEma50 = ema50,
            ShowRsi = rsi,
            ShowMacd = macd
        };
        
        var bundle = _chartService.PrepareChartData(candles, config);
        
        return bundle;
    }
}
