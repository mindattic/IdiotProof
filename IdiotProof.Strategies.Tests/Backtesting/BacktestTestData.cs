using IdiotProof.Models;

namespace IdiotProof.Strategies.Tests.Backtesting;

/// <summary>
/// Deterministic candle builders for backtest tests. Hand-crafted bars so the
/// expected triggers and P&amp;L can be computed by hand and asserted exactly.
/// </summary>
internal static class BacktestTestData
{
    private static readonly DateTime SessionStart =
        new(2026, 5, 29, 13, 30, 0, DateTimeKind.Utc); // 09:30 ET

    public static Candle Bar(int minute, decimal open, decimal high, decimal low, decimal close, decimal volume = 1000m, string symbol = "TST")
    {
        var start = SessionStart.AddMinutes(minute);
        return new Candle
        {
            Symbol = symbol,
            StartUtc = start,
            EndUtc = start.AddMinutes(1),
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
        };
    }
}
