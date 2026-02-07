# IdiotProof

An autonomous trading system that uses AI-powered decision making to analyze markets and execute trades through Interactive Brokers.

## What It Does

IdiotProof continuously monitors market conditions across multiple technical indicators, calculates a composite "market score" for each ticker, and makes independent buy/sell decisions. It learns from each trade to improve ticker-specific performance over time.

```
MARKET DATA ──> INDICATORS ──> MARKET SCORE ──> DECISION ──> ORDER EXECUTION
                                    ↑
METADATA ──────────────────────────┘
(ATR, S/R, patterns)
```

## Key Features

- **Autonomous Trading**: AI calculates market score and decides entry/exit
- **Self-Calibration**: Dynamically adjusts thresholds based on performance
- **Ticker Metadata**: Stock-specific tuning (volatility, support/resistance, timing patterns)
- **Historical Caching**: Stores IBKR data locally to avoid API costs
- **Comprehensive Backtesting**: Test strategies against historical data
- **Position Sizing**: Adjusts size based on volatility and signal strength

## Quick Start

```csharp
// Backtest autonomous trading
var backtester = new AutonomousBacktester(historicalDataService);
var result = await backtester.RunAsync("NVDA", DateOnly.FromDateTime(DateTime.Today));

Console.WriteLine($"Win Rate: {result.WinRate:F1}%");
Console.WriteLine($"Profit: ${result.TotalPnL:F2}");
```

## Project Structure

| Project | Purpose |
|---------|---------|
| **IdiotProof.Core** | Main trading engine, backtesting, autonomous logic |
| **IdiotProof.Shared** | Common models, settings, constants |
| **IdiotProof.Core.UnitTests** | Comprehensive test suite |

## Documentation

- [Core README](IdiotProof.Core/README.md) - Detailed autonomous trading documentation
- [Architecture](IdiotProof.Core/ARCHITECTURE.md) - Database design and data flow
- [IdiotScript](IdiotProof.Core/Scripting/IdiotScript.README.md) - Legacy DSL reference

## Requirements

- .NET 10.0+
- Interactive Brokers TWS or IB Gateway
- Market data subscription

## License

MIT License
