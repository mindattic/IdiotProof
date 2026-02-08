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
// Backtest trading
var backtester = new Backtester(historicalDataService);
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

## AI Learning Methods

The system uses two complementary machine learning methods to optimize indicator weights for each ticker:

### METHOD 1: GENETIC ALGORITHM

Evolves optimal indicator weights through natural selection:

```
╔═══════════════════════════════════════════════════════════════════════════╗
║  GENETIC ALGORITHM FLOW                                                   ║
╠═══════════════════════════════════════════════════════════════════════════╣
║                                                                           ║
║  1. INITIALIZE: Create 20 random weight sets (population)                 ║
║                                                                           ║
║  2. EVALUATE: Test each on training data → fitness score                  ║
║                                                                           ║
║  3. SELECT: Keep top 4 (elites), tournament-select parents for rest       ║
║                                                                           ║
║  4. BREED: Parent1 × Parent2 → Child (crossover of weights)               ║
║                                                                           ║
║  5. MUTATE: Randomly perturb 15% of child's weights                       ║
║                                                                           ║
║  6. REPEAT: For N generations until validation stops improving            ║
║                                                                           ║
╚═══════════════════════════════════════════════════════════════════════════╝
```

**Key mechanics:**
- **Elitism**: Top 4 performers always survive to next generation
- **Tournament Selection**: Pick 3 random, select the best → ensures diverse parents
- **Crossover**: Blends weights from two parents  
- **Early stopping**: Stops if no validation improvement for 15 generations

### METHOD 2: NEURAL NETWORK

Learns weight adjustments through gradient descent:

```
╔═══════════════════════════════════════════════════════════════════════════╗
║  NEURAL NETWORK ARCHITECTURE                                              ║
╠═══════════════════════════════════════════════════════════════════════════╣
║                                                                           ║
║  INPUT (16)          HIDDEN (32)          OUTPUT (16)                     ║
║  ┌─────────┐         ┌─────────┐          ┌─────────┐                     ║
║  │ RSI     │ ──┐     │ Neuron1 │ ──┐      │ Weight1 │ → VWAP weight adj   ║
║  │ ADX     │ ──┼──►  │ Neuron2 │ ──┼──►   │ Weight2 │ → EMA weight adj    ║
║  │ MACD    │ ──┤     │   ...   │ ──┤      │   ...   │ → ...               ║
║  │ Volume  │ ──┤     │ Neuron32│ ──┘      │ Weight16│ → Volume weight adj ║
║  │  ...    │ ──┘     └─────────┘          └─────────┘                     ║
║  └─────────┘         tanh activation       linear output                  ║
║                                                                           ║
╚═══════════════════════════════════════════════════════════════════════════╝
```

**How it works:**
1. **Input**: Average indicator values from training data (RSI, ADX, MACD, etc.)
2. **Forward pass**: Compute hidden activations → compute output adjustments
3. **Apply**: Add outputs to base weights → test on training data
4. **Gradient estimation**: Perturb each hidden→output weight, measure fitness change
5. **Update**: Adjust weights in direction that improves fitness
6. **Decay**: Learning rate decays 1% per epoch (starts at 0.01)

### When Each Method Excels

| Scenario | Best Method |
|----------|-------------|
| Limited data | **Genetic** - works well with small samples |
| Complex patterns | **Neural** - can learn non-linear relationships |
| Noisy data | **Genetic** - more robust to outliers |
| Fast convergence needed | **Neural** - gradient descent is faster |

The learner runs both methods and picks the one with best **validation** fitness (not training) to prevent overfitting.

## License

MIT License
