# IdiotProof - Autonomous Trading System

An AI-powered autonomous trading agent that makes informed, real-time buy/sell decisions using technical indicators, historical patterns, and continuous market analysis.

```
+===========================================================================+
|  AUTONOMOUS TRADING ARCHITECTURE                                         |
+===========================================================================+
|                                                                           |
|   MARKET DATA ──┬──> INDICATORS ──> MARKET SCORE ──> DECISION ENGINE    |
|                 │                        │                    │          |
|   HISTORICAL ───┤                        │                    ▼          |
|   BARS          │                        │           ┌───────────────┐   |
|                 │                        │           │  LONG/SHORT/  │   |
|   METADATA ─────┤                        ▼           │    HOLD       │   |
|   (ATR, Levels) │               ADJUSTMENTS          └───────┬───────┘   |
|                 │                                            │          |
|   PROFILES ─────┘                                            ▼          |
|   (Learning)                                           ORDER EXECUTION  |
|                                                                          |
+===========================================================================+
```

## Philosophy

IdiotProof evolved from a "recipe-driven" strategy execution engine to an **intelligent autonomous agent**. Instead of following fixed rules, the system:

1. **Continuously analyzes** market conditions across 10+ indicators
2. **Calculates a market score** (-100 to +100) representing bullish/bearish strength
3. **Makes independent decisions** about entry, sizing, and exits
4. **Learns from each trade** to improve ticker-specific performance
5. **Adapts in real-time** to changing market regimes

---

## Quick Start

```csharp
// Create the autonomous trading engine
var backtester = new AutonomousBacktester(historicalDataService);

// Run a backtest
var result = await backtester.RunAsync("NVDA", DateOnly.FromDateTime(DateTime.Today));

// Or for live trading with the strategy runner
Stock.Ticker("NVDA")
    .AutonomousTrading(IS.BALANCED)  // Let the AI handle everything
    .Build();
```

---

## Core Components

### 1. Data Stack

```
+=======================+========================================+
|  Layer                |  Purpose                               |
+=======================+========================================+
|  /History/SYMBOL.json |  Cached OHLCV bars (from IBKR API)    |
|  /Metadata/SYMBOL.json|  Stock characteristics, ATR, S/R      |
|  /Profiles/SYMBOL.json|  Learned patterns, win rates          |
+=======================+========================================+
```

### 2. Market Score Calculation

The system calculates a composite score from weighted indicators:

| Indicator | Weight | Bullish Signal | Bearish Signal |
|-----------|--------|----------------|----------------|
| **VWAP Position** | 15% | Price 5%+ above | Price 5%+ below |
| **EMA Stack** | 20% | Price > EMA9 > EMA21 | Price < EMA9 < EMA21 |
| **RSI** | 15% | Oversold bounce | Overbought reversal |
| **MACD** | 20% | MACD > Signal | MACD < Signal |
| **ADX + DI** | 20% | ADX 25+ with +DI > -DI | ADX 25+ with -DI > +DI |
| **Volume** | 10% | Above avg + price up | Above avg + price down |

**Score Interpretation:**
- `+70 to +100` → Strong bullish → ENTER LONG
- `+40 to +69` → Moderate bullish → HOLD or scale in
- `-39 to +39` → Neutral → NO ACTION
- `-69 to -40` → Moderate bearish → HOLD short or caution
- `-100 to -70` → Strong bearish → ENTER SHORT

### 3. Ticker Metadata

The system builds a behavioral profile for each stock:

```csharp
// Volatility & Risk
Beta, ATR(14), AvgVolume, FloatShares, ShortInterest

// Fundamentals  
Sector, Industry, MarketCap, EarningsDate, ExDividendDate

// Technical Context
52WeekHigh/Low, SupportLevels, ResistanceLevels

// Time Patterns
BestHours, AvoidHours, HOD/LOD timing patterns, GapFillRate
```

**How Metadata Affects Decisions:**

| Factor | Effect |
|--------|--------|
| Near earnings | Skip trading (too risky) |
| High beta | Reduce position 50-75% |
| Low float | Reduce position, tighter stops |
| Near support (long) | +10 to entry score |
| Near typical HOD time | Caution for new longs |

### 4. Self-Calibration

The system dynamically adjusts its parameters based on performance:

```
+===========================================================================+
|  DYNAMIC CALIBRATOR                                                       |
+===========================================================================+
|                                                                           |
|  Entry Thresholds:  Raised after losses, lowered when missing profits   |
|  TP Multiplier:     Reduced if frequently stopped out near TP           |
|  SL Multiplier:     Widened if stopped out then price reverses          |
|  Volume Filter:     Loosened if blocking too many good setups           |
|  Trend Requirement: Toggled based on market regime (trending/ranging)   |
|                                                                           |
+===========================================================================+
```

---

## Configuration Options

```csharp
var config = new AutonomousBacktestConfig
{
    // Core settings
    Mode = AutonomousMode.Balanced,      // Conservative, Balanced, Aggressive, Optimized
    AllowShort = true,                   // Enable short positions
    AllowDirectionFlip = true,           // Exit long → enter short
    
    // Risk management
    TakeProfitAtrMultiplier = 1.5,       // TP = Entry ± (ATR × 1.5)
    StopLossAtrMultiplier = 2.5,         // SL = Entry ∓ (ATR × 2.5)
    UseTrailingTakeProfit = true,        // Trail TP as price moves
    
    // Filters
    RequireTrendAlignment = true,        // Price must align with EMA
    RequireVolumeConfirmation = true,    // Volume > average
    MinIndicatorConfirmation = 6,        // X of 10 indicators must agree
    
    // Self-calibration
    EnableSelfCalibration = true,        // AI adjusts parameters
    
    // Metadata integration
    UseTickerMetadata = true,            // Use stock-specific tuning
    UseMetadataPositionSizing = true,    // Reduce size for volatile stocks
    AvoidDaysNearEarnings = 2,           // Skip trading near earnings
};
```

---

## Trading Modes

| Mode | Entry Threshold | Risk Profile | Best For |
|------|-----------------|--------------|----------|
| **Conservative** | Score ≥ 85 | Small positions, tight stops | Capital preservation |
| **Balanced** | Score ≥ 75 | Standard sizing | General trading |
| **Aggressive** | Score ≥ 65 | Larger positions, wider stops | Trend following |
| **Optimized** | Score ≥ 55 + confirmations | Dynamic sizing, trailing TP | Maximum profit |

---

## Data Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           AUTONOMOUS TRADING LOOP                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   1. FETCH DATA                                                             │
│      ├── Check /History/SYMBOL.json (cached bars)                          │
│      ├── If not found: Fetch from IBKR API → Save to cache                 │
│      └── Load /Metadata/SYMBOL.json (or build from history)                │
│                                                                             │
│   2. CALCULATE INDICATORS                                                   │
│      ├── VWAP, EMA(9,21,200), RSI(14), MACD, ADX, +DI/-DI                  │
│      ├── ATR, Volume ratio, Momentum, ROC                                  │
│      └── Warm-up period: ~50 bars for reliable signals                     │
│                                                                             │
│   3. COMPUTE MARKET SCORE                                                   │
│      ├── Weight each indicator                                             │
│      ├── Apply metadata adjustments (support/resistance, timing)           │
│      └── Result: Score -100 to +100                                        │
│                                                                             │
│   4. EXECUTE DECISION                                                       │
│      ├── NOT IN POSITION: Check entry thresholds                           │
│      │   ├── Score ≥ Long Threshold → BUY                                  │
│      │   └── Score ≤ Short Threshold → SHORT                               │
│      └── IN POSITION: Check exit conditions                                │
│          ├── Take Profit hit → EXIT                                        │
│          ├── Stop Loss hit → EXIT                                          │
│          ├── Score reverses → EXIT (possibly flip)                         │
│          └── Trailing stop adjusted based on high-water mark              │
│                                                                             │
│   5. RECORD & LEARN                                                         │
│      ├── Save trade to /Profiles/SYMBOL.json                               │
│      ├── Update ticker-specific statistics                                 │
│      └── Calibrator adjusts parameters for next trade                      │
│                                                                             │
│   6. REPEAT (every minute bar)                                             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Storage Architecture

### Current: File-Based

```
IdiotProof.Core/
├── History/              # Cached OHLCV data (JSON)
│   ├── NVDA.json
│   ├── AAPL.json
│   └── ...
├── Metadata/             # Stock characteristics (JSON)
│   ├── NVDA.metadata.json
│   └── ...
├── Profiles/             # Learning data (JSON)
│   ├── NVDA.json
│   └── ...
└── Scripts/              # IdiotScript files (legacy)
    └── *.idiot
```

### Planned: SQLite + DuckDB Hybrid

For better performance and analytics:

```sql
-- SQLite: Transactional data
CREATE TABLE trades (
    id INTEGER PRIMARY KEY,
    symbol TEXT NOT NULL,
    entry_time DATETIME,
    exit_time DATETIME,
    direction TEXT,  -- 'LONG' or 'SHORT'
    entry_price REAL,
    exit_price REAL,
    shares INTEGER,
    pnl REAL,
    entry_score REAL,
    exit_score REAL
);

CREATE TABLE positions (
    symbol TEXT PRIMARY KEY,
    direction TEXT,
    entry_price REAL,
    shares INTEGER,
    entry_time DATETIME,
    current_stop REAL,
    current_target REAL
);

-- DuckDB: Analytics (columnar, fast aggregations)
CREATE TABLE bars (
    symbol VARCHAR,
    timestamp TIMESTAMP,
    open DOUBLE,
    high DOUBLE,
    low DOUBLE,
    close DOUBLE,
    volume BIGINT
);

-- Query: Average daily range by symbol
SELECT symbol, 
       AVG((high - low) / open * 100) as avg_daily_range_pct
FROM bars
GROUP BY symbol;
```

---

## Backtesting

```csharp
var backtester = new AutonomousBacktester(histService);

// Run backtest
var result = await backtester.RunAsync(
    symbol: "NVDA",
    date: new DateOnly(2025, 12, 15),
    startingCapital: 1000.00m,
    config: new AutonomousBacktestConfig 
    { 
        Mode = AutonomousMode.Optimized,
        EnableSelfCalibration = true,
        UseTickerMetadata = true
    }
);

// Analyze results
Console.WriteLine($"Total Trades: {result.TotalTrades}");
Console.WriteLine($"Win Rate: {result.WinRate:F1}%");
Console.WriteLine($"Profit Factor: {result.ProfitFactor:F2}");
Console.WriteLine($"Total Return: {result.TotalReturnPercent:F2}%");

// View trade log
foreach (var trade in result.Trades)
{
    Console.WriteLine(trade);
}
```

---

## Legacy: IdiotScript

The original recipe-based approach is still supported for explicit rule-based strategies.

See [IdiotScript.README.md](Scripting/IdiotScript.README.md) for the full DSL reference.

```idiotscript
// Legacy approach: Explicit rules
Ticker(AAPL)
.IsGapUp(5)
.IsAboveVwap()
.IsEmaAbove(9)
.Long()
.TakeProfit(160)
.StopLoss(145)

// Modern approach: AI decides
Ticker(AAPL)
.AutonomousTrading(IS.BALANCED)
```

---

## Project Structure

```
IdiotProof/
├── IdiotProof.Core/          # Main trading engine
│   ├── Services/
│   │   ├── AutonomousBacktester.cs    # Backtesting engine
│   │   ├── HistoricalDataCache.cs     # Bar data caching
│   │   ├── TickerMetadataService.cs   # Metadata analysis
│   │   └── SentimentService.cs        # News/sentiment
│   ├── Learning/
│   │   ├── TickerProfile.cs           # Per-ticker learning
│   │   └── MetadataAnalyzer.cs        # Pattern detection
│   ├── Strategy/
│   │   └── StrategyRunner.cs          # Live execution
│   └── History/                       # Cached bar data
│       └── *.json
├── IdiotProof.Shared/        # Common models & settings
│   ├── Models/
│   │   └── TickerMetadata.cs
│   └── Settings/
│       └── SettingsManager.cs
└── IdiotProof.Core.UnitTests/ # Tests
```

---

## Roadmap

### Near-Term
- [ ] SQLite integration for trades & positions
- [ ] DuckDB integration for historical analytics
- [ ] Multi-day backtesting with overnight holds
- [ ] Portfolio-level position management

### Medium-Term
- [ ] Sector rotation based on metadata correlations
- [ ] Earnings calendar integration
- [ ] Options support (covered calls, protective puts)
- [ ] Web dashboard for monitoring

### Long-Term
- [ ] Neo4j for ticker relationship graphs
- [ ] ML model for score calculation
- [ ] Multi-broker support (beyond IBKR)

---

## Connection Requirements

- Interactive Brokers TWS or IB Gateway
- Market data subscription for target symbols
- Real-time data for live trading (delayed OK for backtesting)

---

## License

MIT License - See LICENSE file
