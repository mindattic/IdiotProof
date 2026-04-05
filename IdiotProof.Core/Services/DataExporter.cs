// ============================================================================
// Data Exporter - Export backtest data to various formats
// ============================================================================

using IdiotProof.Models;
using System.Text;

namespace IdiotProof.Services;

/// <summary>
/// Exports backtest data to various formats.
/// </summary>
public sealed class DataExporter
{
    private readonly BackTestSession session;

    public DataExporter(BackTestSession session)
    {
        this.session = session;
    }

    /// <summary>
    /// Exports session data to CSV format.
    /// </summary>
    public async Task ExportToCsvAsync(string filePath, bool includeHeader = true)
    {
        var sb = new StringBuilder();

        if (includeHeader)
        {
            sb.AppendLine("DateTime,Open,High,Low,Close,Volume,VWAP");
        }

        foreach (var candle in session.Candles)
        {
            sb.AppendLine($"{candle.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                $"{candle.Open:F2}," +
                $"{candle.High:F2}," +
                $"{candle.Low:F2}," +
                $"{candle.Close:F2}," +
                $"{candle.Volume}," +
                $"{candle.Vwap:F2}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    /// <summary>
    /// Exports trades to CSV format.
    /// </summary>
    public async Task ExportTradesToCsvAsync(string filePath, List<Trade> trades)
    {
        var sb = new StringBuilder();
        sb.AppendLine("EntryTime,ExitTime,EntryPrice,ExitPrice,Quantity,IsLong,ExitReason,PnL,PnLPercent,Duration");

        foreach (var trade in trades)
        {
            sb.AppendLine($"{trade.EntryTime:yyyy-MM-dd HH:mm:ss}," +
                $"{trade.ExitTime:yyyy-MM-dd HH:mm:ss}," +
                $"{trade.EntryPrice:F2}," +
                $"{trade.ExitPrice:F2}," +
                $"{trade.Quantity}," +
                $"{trade.IsLong}," +
                $"{trade.ExitReason}," +
                $"{trade.PnL:F2}," +
                $"{trade.PnLPercent:F2}," +
                $"{trade.Duration.TotalMinutes:F0}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    /// <summary>
    /// Generates a sample data file for testing.
    /// </summary>
    public static async Task GenerateSampleDataFileAsync(
        string symbol,
        DateOnly date,
        string dataDirectory,
        double basePrice = 100.0,
        double volatility = 0.02)
    {
        var provider = new SyntheticDataProvider();
        var session = await provider.LoadSessionAsync(symbol, date);

        // Create data directory if it doesn't exist
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }

        var exporter = new DataExporter(session);
        string filePath = Path.Combine(dataDirectory, $"{symbol}_{date:yyyy-MM-dd}.csv");
        await exporter.ExportToCsvAsync(filePath);

        Console.WriteLine($"[OK] Sample data file created: {filePath}");
        Console.WriteLine($"     {session.CandleCount} candles generated");
        Console.WriteLine($"     Price range: ${session.Low:F2} - ${session.High:F2}");
    }

    /// <summary>
    /// Exports session summary to JSON format.
    /// </summary>
    public async Task ExportSessionSummaryAsync(string filePath)
    {
        var summary = new
        {
            session.Symbol,
            Date = session.Date.ToString("yyyy-MM-dd"),
            session.Open,
            session.High,
            session.Low,
            session.Close,
            session.Range,
            session.Change,
            session.ChangePercent,
            session.TotalVolume,
            session.CandleCount,
            StartTime = session.StartTime.ToString("HH:mm"),
            EndTime = session.EndTime.ToString("HH:mm")
        };

        string json = System.Text.Json.JsonSerializer.Serialize(summary, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(filePath, json);
    }
}

/// <summary>
/// Imports data from various external sources.
/// </summary>
public sealed class DataImporter
{
    private readonly string dataDirectory;

    public DataImporter(string dataDirectory)
    {
        this.dataDirectory = dataDirectory;

        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }
    }

    /// <summary>
    /// Lists all available data files.
    /// </summary>
    public List<(string Symbol, DateOnly Date, string FilePath)> ListAvailableData()
    {
        var results = new List<(string Symbol, DateOnly Date, string FilePath)>();

        var csvFiles = Directory.GetFiles(dataDirectory, "*.csv", SearchOption.AllDirectories);

        foreach (var file in csvFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var parts = fileName.Split('_');

            if (parts.Length >= 2)
            {
                string symbol = parts[0].ToUpperInvariant();
                if (DateOnly.TryParse(parts[1], out var date))
                {
                    results.Add((symbol, date, file));
                }
            }
        }

        return results.OrderBy(r => r.Symbol).ThenBy(r => r.Date).ToList();
    }

    /// <summary>
    /// Gets available dates for a symbol.
    /// </summary>
    public List<DateOnly> GetAvailableDates(string symbol)
    {
        return ListAvailableData()
            .Where(d => d.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Date)
            .OrderBy(d => d)
            .ToList();
    }

    /// <summary>
    /// Gets all unique symbols with data.
    /// </summary>
    public List<string> GetAvailableSymbols()
    {
        return ListAvailableData()
            .Select(d => d.Symbol)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    /// <summary>
    /// Prints a summary of available data.
    /// </summary>
    public void PrintDataSummary()
    {
        var data = ListAvailableData();

        if (data.Count == 0)
        {
            Console.WriteLine($"No data files found in: {Path.GetFullPath(dataDirectory)}");
            Console.WriteLine();
            Console.WriteLine("To get started:");
            Console.WriteLine("  1. Use --synthetic flag to generate test data");
            Console.WriteLine("  2. Or place CSV files in the Data folder");
            return;
        }

        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine("| AVAILABLE DATA                                   |");
        Console.WriteLine("+--------------------------------------------------+");

        var symbols = data.GroupBy(d => d.Symbol).OrderBy(g => g.Key);

        foreach (var group in symbols)
        {
            var dates = group.OrderBy(g => g.Date).ToList();
            Console.WriteLine($"| {group.Key}");
            Console.WriteLine($"|   Dates: {dates.First().Date:yyyy-MM-dd} to {dates.Last().Date:yyyy-MM-dd}");
            Console.WriteLine($"|   Files: {dates.Count}");
            Console.WriteLine("|");
        }

        Console.WriteLine("+--------------------------------------------------+");
    }
}
