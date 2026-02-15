// ============================================================================
// TradingView Integration Service
// ============================================================================
// Options for TradingView integration:
//
// 1. TradingView Widget (FREE) - Basic charts with limited customization
//    - Embed via iframe
//    - Can't add custom PineScript
//    - Good for quick visualization
//
// 2. TradingView Lightweight Charts (FREE, Open Source)
//    - Full control over rendering
//    - Add our own indicators calculated server-side
//    - Best option for custom integration
//
// 3. TradingView Charting Library (PAID)
//    - Full PineScript support
//    - Requires license from TradingView
//
// This implementation uses option 2 (Lightweight Charts) with
// server-side indicator calculations.
// ============================================================================

namespace IdiotProof.Web.Services.TradingView;

/// <summary>
/// Configuration for the TradingView chart component.
/// </summary>
public sealed class ChartConfig
{
    public string Symbol { get; set; } = "AAPL";
    public string Interval { get; set; } = "1";  // 1, 5, 15, 30, 60, D, W, M
    public bool ShowVwap { get; set; } = true;
    public bool ShowEma9 { get; set; } = true;
    public bool ShowEma21 { get; set; } = true;
    public bool ShowEma50 { get; set; } = false;
    public bool ShowEma200 { get; set; } = false;
    public bool ShowRsi { get; set; } = true;
    public bool ShowMacd { get; set; } = true;
    public bool ShowVolume { get; set; } = true;
    public string Theme { get; set; } = "dark";
}

/// <summary>
/// Data point for lightweight charts.
/// </summary>
public sealed class ChartDataPoint
{
    public long Time { get; set; }  // Unix timestamp
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
}

/// <summary>
/// Overlay indicator data (VWAP, EMA, etc.)
/// </summary>
public sealed class OverlayData
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#2196F3";
    public int LineWidth { get; set; } = 2;
    public List<(long Time, double Value)> Points { get; set; } = [];
}

/// <summary>
/// Oscillator data (RSI, MACD histogram, etc.)
/// </summary>
public sealed class OscillatorData
{
    public string Name { get; set; } = "";
    public double? UpperBound { get; set; }
    public double? LowerBound { get; set; }
    public List<OscillatorLine> Lines { get; set; } = [];
}

public sealed class OscillatorLine
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#2196F3";
    public List<(long Time, double Value)> Points { get; set; } = [];
}

/// <summary>
/// Service for preparing chart data with indicators.
/// </summary>
public sealed class ChartDataService
{
    private readonly ILogger<ChartDataService> _logger;
    
    public ChartDataService(ILogger<ChartDataService> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Prepares chart data with calculated indicators.
    /// </summary>
    public ChartBundle PrepareChartData(
        IList<ChartDataPoint> candles, 
        ChartConfig config)
    {
        var bundle = new ChartBundle
        {
            Symbol = config.Symbol,
            Candles = candles.ToList()
        };
        
        // Calculate and add overlays
        if (config.ShowVwap)
        {
            bundle.Overlays.Add(CalculateVwap(candles));
        }
        
        if (config.ShowEma9)
        {
            bundle.Overlays.Add(CalculateEma(candles, 9, "#FF9800", "EMA 9"));
        }
        
        if (config.ShowEma21)
        {
            bundle.Overlays.Add(CalculateEma(candles, 21, "#2196F3", "EMA 21"));
        }
        
        if (config.ShowEma50)
        {
            bundle.Overlays.Add(CalculateEma(candles, 50, "#9C27B0", "EMA 50"));
        }
        
        if (config.ShowEma200)
        {
            bundle.Overlays.Add(CalculateEma(candles, 200, "#F44336", "EMA 200"));
        }
        
        // Calculate oscillators
        if (config.ShowRsi)
        {
            bundle.Oscillators.Add(CalculateRsi(candles, 14));
        }
        
        if (config.ShowMacd)
        {
            bundle.Oscillators.Add(CalculateMacd(candles, 12, 26, 9));
        }
        
        return bundle;
    }
    
    private OverlayData CalculateVwap(IList<ChartDataPoint> candles)
    {
        var overlay = new OverlayData
        {
            Name = "VWAP",
            Color = "#00BCD4",
            LineWidth = 2
        };
        
        double cumulativeTypicalPriceVolume = 0;
        double cumulativeVolume = 0;
        
        foreach (var candle in candles)
        {
            var typicalPrice = (candle.High + candle.Low + candle.Close) / 3;
            cumulativeTypicalPriceVolume += typicalPrice * candle.Volume;
            cumulativeVolume += candle.Volume;
            
            var vwap = cumulativeVolume > 0 
                ? cumulativeTypicalPriceVolume / cumulativeVolume 
                : typicalPrice;
            
            overlay.Points.Add((candle.Time, Math.Round(vwap, 2)));
        }
        
        return overlay;
    }
    
    private OverlayData CalculateEma(IList<ChartDataPoint> candles, int period, string color, string name)
    {
        var overlay = new OverlayData
        {
            Name = name,
            Color = color,
            LineWidth = period <= 21 ? 1 : 2
        };
        
        double multiplier = 2.0 / (period + 1);
        double? ema = null;
        
        for (int i = 0; i < candles.Count; i++)
        {
            if (i < period - 1)
            {
                // Not enough data yet, use SMA
                var sum = candles.Take(i + 1).Sum(c => c.Close);
                ema = sum / (i + 1);
            }
            else if (i == period - 1)
            {
                // First EMA = SMA
                ema = candles.Take(period).Average(c => c.Close);
            }
            else
            {
                // EMA = (Close - EMA_prev) * multiplier + EMA_prev
                ema = (candles[i].Close - ema!.Value) * multiplier + ema.Value;
            }
            
            overlay.Points.Add((candles[i].Time, Math.Round(ema!.Value, 2)));
        }
        
        return overlay;
    }
    
    private OscillatorData CalculateRsi(IList<ChartDataPoint> candles, int period = 14)
    {
        var oscillator = new OscillatorData
        {
            Name = "RSI",
            UpperBound = 70,
            LowerBound = 30
        };
        
        var rsiLine = new OscillatorLine
        {
            Name = "RSI",
            Color = "#E91E63"
        };
        
        double avgGain = 0;
        double avgLoss = 0;
        
        for (int i = 1; i < candles.Count; i++)
        {
            var change = candles[i].Close - candles[i - 1].Close;
            var gain = change > 0 ? change : 0;
            var loss = change < 0 ? -change : 0;
            
            if (i <= period)
            {
                avgGain += gain;
                avgLoss += loss;
                
                if (i == period)
                {
                    avgGain /= period;
                    avgLoss /= period;
                }
            }
            else
            {
                avgGain = (avgGain * (period - 1) + gain) / period;
                avgLoss = (avgLoss * (period - 1) + loss) / period;
            }
            
            if (i >= period)
            {
                var rs = avgLoss > 0 ? avgGain / avgLoss : 100;
                var rsi = 100 - (100 / (1 + rs));
                rsiLine.Points.Add((candles[i].Time, Math.Round(rsi, 2)));
            }
        }
        
        oscillator.Lines.Add(rsiLine);
        return oscillator;
    }
    
    private OscillatorData CalculateMacd(IList<ChartDataPoint> candles, int fast = 12, int slow = 26, int signal = 9)
    {
        var oscillator = new OscillatorData
        {
            Name = "MACD"
        };
        
        // Calculate EMAs
        var fastEma = CalculateEmaValues(candles, fast);
        var slowEma = CalculateEmaValues(candles, slow);
        
        // MACD line = Fast EMA - Slow EMA
        var macdValues = new List<double>();
        for (int i = 0; i < candles.Count; i++)
        {
            if (i >= slow - 1)
            {
                macdValues.Add(fastEma[i] - slowEma[i]);
            }
        }
        
        // Signal line = EMA of MACD
        var signalValues = CalculateEmaOnValues(macdValues, signal);
        
        // Create lines
        var macdLine = new OscillatorLine { Name = "MACD", Color = "#2196F3" };
        var signalLine = new OscillatorLine { Name = "Signal", Color = "#FF9800" };
        var histogramLine = new OscillatorLine { Name = "Histogram", Color = "#4CAF50" };
        
        for (int i = 0; i < macdValues.Count; i++)
        {
            var candle = candles[slow - 1 + i];
            macdLine.Points.Add((candle.Time, Math.Round(macdValues[i], 4)));
            
            if (i >= signal - 1)
            {
                var sigVal = signalValues[i - signal + 1];
                signalLine.Points.Add((candle.Time, Math.Round(sigVal, 4)));
                histogramLine.Points.Add((candle.Time, Math.Round(macdValues[i] - sigVal, 4)));
            }
        }
        
        oscillator.Lines.Add(macdLine);
        oscillator.Lines.Add(signalLine);
        oscillator.Lines.Add(histogramLine);
        
        return oscillator;
    }
    
    private List<double> CalculateEmaValues(IList<ChartDataPoint> candles, int period)
    {
        var result = new List<double>();
        double multiplier = 2.0 / (period + 1);
        double? ema = null;
        
        for (int i = 0; i < candles.Count; i++)
        {
            if (i < period - 1)
            {
                var sum = candles.Take(i + 1).Sum(c => c.Close);
                ema = sum / (i + 1);
            }
            else if (i == period - 1)
            {
                ema = candles.Take(period).Average(c => c.Close);
            }
            else
            {
                ema = (candles[i].Close - ema!.Value) * multiplier + ema.Value;
            }
            
            result.Add(ema!.Value);
        }
        
        return result;
    }
    
    private List<double> CalculateEmaOnValues(List<double> values, int period)
    {
        var result = new List<double>();
        double multiplier = 2.0 / (period + 1);
        double? ema = null;
        
        for (int i = 0; i < values.Count; i++)
        {
            if (i < period - 1)
            {
                var sum = values.Take(i + 1).Sum();
                ema = sum / (i + 1);
            }
            else if (i == period - 1)
            {
                ema = values.Take(period).Average();
            }
            else
            {
                ema = (values[i] - ema!.Value) * multiplier + ema.Value;
            }
            
            if (i >= period - 1)
            {
                result.Add(ema!.Value);
            }
        }
        
        return result;
    }
}

/// <summary>
/// Complete chart data bundle for the frontend.
/// </summary>
public sealed class ChartBundle
{
    public string Symbol { get; set; } = "";
    public List<ChartDataPoint> Candles { get; set; } = [];
    public List<OverlayData> Overlays { get; set; } = [];
    public List<OscillatorData> Oscillators { get; set; } = [];
}
