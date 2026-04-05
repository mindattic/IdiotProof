// ============================================================================
// Historical Data Provider - Interface for loading candle data
// ============================================================================

using IdiotProof.Models;

namespace IdiotProof.Services;

/// <summary>
/// Interface for providing historical candle data.
/// </summary>
public interface IHistoricalDataProvider
{
    /// <summary>
    /// Loads 1-minute candles for a ticker on a specific date.
    /// </summary>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="date">The date to load data for.</param>
    /// <returns>A BackTestSession containing all candles for the day.</returns>
    Task<BackTestSession> LoadSessionAsync(string symbol, DateOnly date);

    /// <summary>
    /// Checks if data is available for a ticker on a specific date.
    /// </summary>
    bool IsDataAvailable(string symbol, DateOnly date);

    /// <summary>
    /// Gets the available date range for a ticker.
    /// </summary>
    (DateOnly start, DateOnly end)? GetAvailableDateRange(string symbol);
}

/// <summary>
/// Loads historical data from CSV files.
/// Expected format: DateTime,Open,High,Low,Close,Volume[,Vwap]
/// </summary>
public sealed class CsvHistoricalDataProvider : IHistoricalDataProvider
{
    private readonly string dataDirectory;

    public CsvHistoricalDataProvider(string dataDirectory)
    {
        this.dataDirectory = dataDirectory;
        
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }
    }

    /// <inheritdoc />
    public async Task<BackTestSession> LoadSessionAsync(string symbol, DateOnly date)
    {
        var filePath = GetFilePath(symbol, date);
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"No data file found for {symbol} on {date:yyyy-MM-dd}. " +
                $"Expected path: {filePath}");
        }

        var lines = await File.ReadAllLinesAsync(filePath);
        var candles = new List<BackTestCandle>();

        // Skip header row if present
        int startIndex = lines.Length > 0 && lines[0].Contains("Open") ? 1 : 0;

        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            try
            {
                var fields = line.Split(',');
                candles.Add(BackTestCandle.FromCsv(fields));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to parse line {i + 1}: {ex.Message}");
            }
        }

        var session = new BackTestSession
        {
            Symbol = symbol.ToUpperInvariant(),
            Date = date,
            Candles = candles.OrderBy(c => c.Timestamp).ToList()
        };

        // Calculate VWAP if not provided in data
        if (candles.Count > 0 && candles[0].Vwap == 0)
        {
            session.CalculateVwap();
        }

        return session;
    }

    /// <inheritdoc />
    public bool IsDataAvailable(string symbol, DateOnly date)
    {
        return File.Exists(GetFilePath(symbol, date));
    }

    /// <inheritdoc />
    public (DateOnly start, DateOnly end)? GetAvailableDateRange(string symbol)
    {
        var pattern = $"{symbol.ToUpperInvariant()}_*.csv";
        var files = Directory.GetFiles(dataDirectory, pattern);
        
        if (files.Length == 0) return null;

        var dates = files
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(f => f.Split('_').LastOrDefault())
            .Where(d => d != null)
            .Select(d => DateOnly.TryParse(d, out var date) ? date : (DateOnly?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .OrderBy(d => d)
            .ToList();

        return dates.Count > 0 ? (dates.First(), dates.Last()) : null;
    }

    private string GetFilePath(string symbol, DateOnly date)
    {
        // Try multiple naming conventions
        var patterns = new[]
        {
            $"{symbol.ToUpperInvariant()}_{date:yyyy-MM-dd}.csv",
            $"{symbol.ToUpperInvariant()}_{date:yyyyMMdd}.csv",
            $"{symbol.ToUpperInvariant()}/{date:yyyy-MM-dd}.csv",
            $"{date:yyyy-MM-dd}/{symbol.ToUpperInvariant()}.csv"
        };

        foreach (var pattern in patterns)
        {
            var path = Path.Combine(dataDirectory, pattern);
            if (File.Exists(path)) return path;
        }

        // Return default pattern
        return Path.Combine(dataDirectory, patterns[0]);
    }
}

/// <summary>
/// Generates synthetic candle data for testing purposes.
/// </summary>
public sealed class SyntheticDataProvider : IHistoricalDataProvider
{
    private readonly Random random = new();

    /// <inheritdoc />
    public Task<BackTestSession> LoadSessionAsync(string symbol, DateOnly date)
    {
        var candles = GenerateSyntheticDay(symbol, date);
        
        var session = new BackTestSession
        {
            Symbol = symbol.ToUpperInvariant(),
            Date = date,
            Candles = candles
        };

        session.CalculateVwap();
        return Task.FromResult(session);
    }

    /// <inheritdoc />
    public bool IsDataAvailable(string symbol, DateOnly date) => true;

    /// <inheritdoc />
    public (DateOnly start, DateOnly end)? GetAvailableDateRange(string symbol) =>
        (DateOnly.FromDateTime(DateTime.Today.AddYears(-1)), DateOnly.FromDateTime(DateTime.Today));

    private List<BackTestCandle> GenerateSyntheticDay(string symbol, DateOnly date)
    {
        var candles = new List<BackTestCandle>();
        
        // Generate a realistic price pattern
        double basePrice = symbol.ToUpperInvariant() switch
        {
            "AAPL" => 180.0,
            "TSLA" => 250.0,
            "NVDA" => 500.0,
            _ => 10.0 + random.NextDouble() * 90  // Random $10-$100
        };

        double price = basePrice;
        double volatility = basePrice * 0.001;  // 0.1% per minute base volatility
        
        // Market open spike
        bool hasGap = random.NextDouble() > 0.7;
        if (hasGap)
        {
            price *= 1 + (random.NextDouble() * 0.05 - 0.01);  // -1% to +5% gap
        }

        // Generate RTH candles (9:30 AM to 4:00 PM = 390 minutes)
        var startTime = date.ToDateTime(new TimeOnly(9, 30));
        
        for (int i = 0; i < 390; i++)
        {
            var timestamp = startTime.AddMinutes(i);
            
            // Increase volatility at open and close
            double periodVolatility = volatility;
            if (i < 30) periodVolatility *= 2;  // First 30 mins
            if (i > 360) periodVolatility *= 1.5;  // Last 30 mins

            // Random walk with mean reversion
            double change = (random.NextDouble() * 2 - 1) * periodVolatility;
            double meanReversion = (basePrice - price) * 0.001;
            price += change + meanReversion;

            // Generate OHLC from the price movement
            double open = price;
            double high = price + random.NextDouble() * periodVolatility;
            double low = price - random.NextDouble() * periodVolatility;
            double close = price + (random.NextDouble() * 2 - 1) * periodVolatility * 0.5;

            // Ensure OHLC consistency
            high = Math.Max(high, Math.Max(open, close));
            low = Math.Min(low, Math.Min(open, close));

            // Generate volume (higher at open/close)
            long baseVolume = 10000 + random.Next(50000);
            if (i < 30 || i > 360) baseVolume *= 2;

            candles.Add(new BackTestCandle
            {
                Timestamp = timestamp,
                Open = Math.Round(open, 2),
                High = Math.Round(high, 2),
                Low = Math.Round(low, 2),
                Close = Math.Round(close, 2),
                Volume = baseVolume
            });

            price = close;  // Next candle starts at this close
        }

        return candles;
    }
}
