# IdiotProof Trading Strategy Bot

A fluent API framework for building multi-stage trading strategies with Interactive Brokers (IBKR).

## Table of Contents

- [Overview](#overview)
- [Project Structure](#project-structure)
- [Quick Start](#quick-start)
- [MAUI Frontend (Strategy Builder)](#maui-frontend-strategy-builder)
- [Running in Release Mode](#running-in-release-mode)
  - [IPC Communication Logging](#ipc-communication-logging)
- [Fluent API Reference](#fluent-api-reference)
  - [Strategy Builder Methods](#strategy-builder-methods)
  - [Condition Methods](#condition-methods)
  - [Order Methods](#order-methods)
  - [Exit Strategy Methods](#exit-strategy-methods)
  - [Timing Methods](#timing-methods)
  - [Configuration Methods](#configuration-methods)
- [Default Values Reference](#default-values-reference)
- [Helper Classes](#helper-classes)
- [Examples](#examples)
- [Implementation Status](#implementation-status)
- [Connection Resilience](#connection-resilience)
- [IB Gateway Configuration for Resilience](#ib-gateway-configuration-for-resilience)
- [Validation Rules](#validation-rules)

---

## Overview

IdiotProof provides a clean, readable fluent API for defining complex trading strategies that execute automatically when conditions are met.

**Key Features:**
- Multi-step condition chains (breakout → pullback → VWAP confirmation)
- Automatic order execution with IBKR
- Take profit and stop loss management
- Trailing stop loss with high-water mark tracking
- ATR-based volatility-adaptive stops
- Time-based strategy windows
- Pre-market, RTH, and after-hours support
- **WYSIWYG Strategy Builder UI** (MAUI Frontend)

---

## Project Structure

```
IdiotProof/
├── IdiotProof.csproj              # Main backend service (console app)
├── IdiotProof.Frontend/           # MAUI Blazor Hybrid Strategy Builder UI
│   ├── Components/
│   │   ├── Layout/                # MainLayout with tab navigation
│   │   └── Pages/                 # Design.razor, Strategies.razor, Settings.razor
│   ├── Services/
│   │   ├── IStrategyService.cs    # Strategy persistence interface
│   │   ├── StrategyService.cs     # Individual JSON file management
│   │   ├── IBackendService.cs     # Backend communication interface
│   │   └── BackendService.cs      # Backend IPC (placeholder)
│   ├── wwwroot/
│   │   ├── css/app.css            # Dark theme styling
│   │   └── index.html             # Blazor WebView host
│   └── MainPage.xaml              # MAUI page hosting BlazorWebView
├── IdiotProof.Shared/             # Shared models between frontend and backend
│   ├── Models/
│   │   ├── StrategyDefinition.cs  # Complete strategy container
│   │   ├── StrategySegment.cs     # Single segment in a strategy chain
│   │   ├── SegmentParameter.cs    # Parameter definition for a segment
│   │   └── SegmentFactory.cs      # Creates segment templates
│   ├── Enums/
│   │   ├── SegmentType.cs         # Segment types (Breakout, Buy, etc.)
│   │   └── StrategyEnums.cs       # Shared enums (Price, TradingSession, etc.)
│   └── Services/
│       └── StrategyJsonParser.cs  # JSON parsing utilities
├── Helpers/
│   └── StrategyLoader.cs          # Loads JSON → TradingStrategy for backend
├── Strategy/
│   ├── Stock.cs                   # Fluent builder entry point
│   ├── Strategy.cs                # TradingStrategy container
│   └── ...                        # Conditions, OrderAction, etc.
└── IdiotProof.UnitTests/          # Unit tests
```

---

## Quick Start

```csharp
var strategy = Stock
    .Ticker("AAPL")
    .SessionDuration(TradingSession.PreMarketEndEarly)  // 4:00 AM - 9:20 AM ET
    .Breakout(150.00)                         // Step 1: Price >= 150.00
    .Pullback(148.50)                         // Step 2: Price <= 148.50
    .IsAboveVwap()                              // Step 3: Price >= VWAP
    .Buy(quantity: 100, Price.Current)        // Step 4: Buy 100 shares
    .TakeProfit(155.00)                       // Exit at $155.00
    .TrailingStopLoss(Atr.Balanced)           // 2× ATR trailing stop (adapts to volatility)
    .ClosePosition(MarketTime.PreMarket.Ending)  // Close at 9:20 AM ET
    .Build();

// Or with percentage-based trailing stop:
// .TrailingStopLoss(Percent.Ten)            // 10% trailing stop
```

---

## MAUI Frontend (Strategy Builder)

The MAUI Frontend provides a WYSIWYG interface for building trading strategies without writing code.

### Features

- **Drag-and-Drop Segments**: Build strategies by dragging segments like "AboveVwap", "Buy", "TakeProfit" onto a canvas
- **Dynamic Parameter Editing**: Each segment has configurable parameters (enums become dropdowns, booleans become checkboxes, etc.)
- **Live Code Preview**: See the generated fluent API code in real-time
- **Date-Based Collections**: Strategies are saved per-date as JSON files
- **Validation**: Automatic validation ensures strategies are well-formed

### Running the Frontend

```bash
cd IdiotProof.Frontend
dotnet build
dotnet run
```

### How It Works

1. **Design Tab**: Create strategies by dragging segments from the toolbar onto the canvas
   - Search/filter segments using the toolbar search box
   - Click or drag segments to add them
   - Reorder by dragging within the canvas or using ▲/▼ buttons
   - Edit parameters in the right panel

2. **Strategies Tab**: View, manage, and activate strategies
   - Toggle strategies enabled/disabled
   - Rename, clone, or delete strategies
   - Edit a strategy in the Designer (double-click or Edit button)
   - Start the backend with enabled strategies

3. **Settings Tab**: Configure IBKR connection settings

### JSON Strategy Files

Each strategy is saved as an **individual JSON file** in date-based folders:
```
%AppData%\IdiotProof\Strategies\
├── 2025-01-15/
│   ├── VIVS_Breakout_Strategy.json
│   ├── CATX_VWAP_Scalp.json
│   └── ...
├── 2025-01-16/
│   └── ...
```

**Benefits of individual files:**
- Easy to manually edit or review
- Duplicate filenames get a-z suffix automatically
- Windows-compliant naming (invalid characters replaced)
- Git-friendly (can track individual strategy changes)

### Loading JSON Strategies in Backend

```csharp
// In Program.cs, load strategies from JSON instead of hardcoding:
var strategies = StrategyLoader.LoadFromJson();

// Or hybrid approach:
var strategies = new List<TradingStrategy>();
strategies.AddRange(StrategyLoader.LoadFromJson());  // From UI
strategies.Add(Stock.Ticker("AAPL")...);             // Hardcoded
```

### Segment Categories

| Category | Segments |
|----------|----------|
| 📍 Start | Ticker |
| ⏰ Session | SessionDuration |
| 💰 Price Conditions | Breakout, Pullback, PriceAbove, PriceBelow |
| 📊 VWAP Conditions | AboveVwap, BelowVwap |
| 📈 Indicators | IsRsi, IsMacd, IsAdx, IsDI |
| 🛒 Orders | Buy, Sell, Close |
| 🛡️ Risk Management | TakeProfit, TakeProfitRange, StopLoss, TrailingStopLoss |
| 📤 Position Management | ClosePosition |
| ⚙️ Order Config | TimeInForce, OutsideRTH, AllOrNone |

---

## Running in Release Mode

For long-running processes like overnight trading bots, **always run in Release mode** for better performance and lower memory usage.

### Why Release Mode?

| Aspect | Debug | Release |
|--------|-------|---------|
| Memory Usage | Higher | **Lower** |
| CPU Usage | Higher | **Lower** |
| JIT Optimization | Disabled | **Enabled** |
| Debug Symbols | Loaded | Not loaded |
| GC Pressure | Higher | **Lower** |
| Variable Preservation | For inspection | Optimized away |

### Building in Release

**Command Line:**
```bash
dotnet build -c Release
dotnet run -c Release --no-build
```

**Visual Studio:**
1. Change configuration dropdown from `Debug` to `Release`
2. Build → Build Solution (Ctrl+Shift+B)
3. Run without debugger: Debug → Start Without Debugging (Ctrl+F5)

### Running Overnight

For bots that run all night:

```bash
# Build once
dotnet build -c Release

# Run without debugger attached
dotnet run -c Release --no-build
```

> **Important:** Running with `F5` (Start Debugging) keeps the debugger attached, which increases memory usage. Use `Ctrl+F5` (Start Without Debugging) for production runs.

### Common Overnight Memory Leak Patterns

Even in Release mode, watch out for:

| Pattern | Issue | Fix |
|---------|-------|-----|
| Unbounded lists | Log buffers grow forever | Cap size or rotate |
| Event handlers | Not unsubscribed | Implement `IDisposable` |
| IB subscriptions | Market data accumulates | Cancel unused requests |
| Console output | Large log buffers | Write to file, not console |

### Monitoring Memory Usage

```bash
# Windows - check Working Set in Task Manager
# Or use Performance Monitor (perfmon)

# Linux/Mac
top -p $(pgrep -f IdiotProof)
```

**Recommended:** If memory grows steadily over hours, you likely have a leak.

### IPC Communication Logging

The backend includes a dedicated IPC logger (`IpcLogger`) that tracks all communication between the Console and Backend applications. This is useful for debugging overnight runs and verifying that methods are firing correctly.

**Log Location:**
```
MyDocuments/IdiotProof/Logs/
├── ipc_YYYY-MM-DD.log    # IPC communication log (daily)
├── session_*.txt         # Session logs (on exit)
└── crash_*.txt           # Crash dumps (if any)
```

> **Note:** Logs are stored in the same `MyDocuments\IdiotProof\` folder as Settings and Strategies for easy access.

**What Gets Logged:**
| Event | Description |
|-------|-------------|
| `[CONNECTION]` | Client connect/disconnect events |
| `[REQUEST]` | Incoming IPC requests (GetStatus, GetOrders, etc.) |
| `[BROADCAST]` | Messages sent to all clients (OrderUpdate, TradeUpdate) |
| `[HEARTBEAT]` | Periodic heartbeat broadcasts |
| `[ERROR]` | Any IPC communication errors |

**Example Log Output:**
```
2025-01-15 22:30:15.123 [CONNECTION] Client=abc123 Status=CONNECTED
2025-01-15 22:30:15.456 [REQUEST] Client=000000 Type=GetStatus
2025-01-15 22:30:16.789 [BROADCAST] Type=OrderUpdate Clients=1
2025-01-15 03:45:12.111 [CONNECTION] Client=abc123 Status=DISCONNECTED
```

**Monitor Script:**

Use the included PowerShell script to analyze overnight activity:

```powershell
# Show summary of today's IPC activity
.\IdiotProof.Backend\monitor-ipc.ps1 -Summary

# Show last 100 log entries
.\IdiotProof.Backend\monitor-ipc.ps1 -Lines 100

# Watch logs in real-time
.\IdiotProof.Backend\monitor-ipc.ps1 -Watch
```

**Disabling IPC Logging:**

If you want to disable IPC logging (e.g., for production), set:
```csharp
IpcLogger.Enabled = false;
```

---

## Fluent API Reference

### Strategy Builder Methods

#### `Stock.Ticker(string symbol)`
Creates a new strategy builder for the specified stock symbol.

```csharp
Stock.Ticker("AAPL")    // Start building a strategy for AAPL
```

**Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `symbol` | `string` | Yes | Stock ticker symbol |

**Returns:** `Stock` builder instance

---

#### `.Exchange(string exchange)`
Sets the exchange for order routing (default: "SMART").

```csharp
.Exchange("NASDAQ")
```

**Parameters:**
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `exchange` | `string` | `"SMART"` | Exchange identifier |

**Returns:** `Stock` builder (chainable)

---

#### `.Currency(string currency)`
Sets the currency for the order (default: "USD").

```csharp
.Currency("USD")
```

**Returns:** `Stock` builder (chainable)

---

#### `.Enabled(bool enabled)`
Enables or disables the strategy. Disabled strategies are skipped during execution.

```csharp
.Enabled(false)    // Strategy won't run
```

**Returns:** `Stock` builder (chainable)

---

#### `.Exchange(ContractExchange exchange)`
Sets the exchange using a predefined exchange type.

```csharp
.Exchange(ContractExchange.Smart)    // Default: SMART routing
.Exchange(ContractExchange.Pink)     // OTC/Pink Sheets
```

**Parameters:**
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `exchange` | `ContractExchange` | `Smart` | Predefined exchange type |

**Returns:** `Stock` builder (chainable)

---

#### `.PrimaryExchange(string primaryExchange)`
Sets the primary exchange for SMART routing (required for some OTC stocks).

```csharp
.PrimaryExchange("PINK")
.PrimaryExchange("NASDAQ")
```

**Returns:** `Stock` builder (chainable)

---

#### `.SessionDuration(TradingSession session)`
Sets the time window using a predefined trading session. **Recommended** - easy to comment out for testing.

```csharp
.SessionDuration(TradingSession.PreMarket)          // 4:00 AM - 9:30 AM ET
.SessionDuration(TradingSession.PreMarketEndEarly)  // 4:00 AM - 9:20 AM ET (exit early)
.SessionDuration(TradingSession.RTH)                // 9:30 AM - 4:00 PM ET
.SessionDuration(TradingSession.Active)             // No time restrictions
```

**Available Sessions:**

| Session | Start | End | Use Case |
|---------|-------|-----|----------|
| `Active` | - | - | No time restrictions (24/7 monitoring) |
| `PreMarket` | 4:00 AM | 9:30 AM | Full pre-market |
| `PreMarketEndEarly` | 4:00 AM | 9:20 AM | Exit before market open |
| `PreMarketStartLate` | 4:10 AM | 9:30 AM | Skip initial volatility |
| `RTH` | 9:30 AM | 4:00 PM | Regular trading hours |
| `RTHEndEarly` | 9:30 AM | 3:50 PM | Exit before close |
| `RTHStartLate` | 9:40 AM | 4:00 PM | Skip open volatility |
| `AfterHours` | 4:00 PM | 8:00 PM | After-hours session |
| `AfterHoursEndEarly` | 4:00 PM | 7:50 PM | Exit early |
| `Extended` | 4:00 AM | 8:00 PM | All sessions |

**Returns:** `Stock` builder (chainable)

---

#### `.SessionDuration(TimeOnly startTime, TimeOnly endTime)`
Sets a custom time window for strategy monitoring.

```csharp
.SessionDuration(new TimeOnly(4, 0), new TimeOnly(9, 30))    // Custom window
.SessionDuration(MarketTime.PreMarket.Start, MarketTime.PreMarket.Ending)  // Using MarketTime helpers
```

**Returns:** `Stock` builder (chainable)

---

### Condition Methods

Conditions are evaluated sequentially. Each must be satisfied before moving to the next.

#### `.Breakout(double level)`
Triggers when price rises to or above the specified level.

```csharp
.Breakout(7.10)    // Triggers when price >= $7.10
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.Pullback(double level)`
Triggers when price falls to or below the specified level.

```csharp
.Pullback(6.80)    // Triggers when price <= $6.80
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.IsAboveVwap(double buffer = 0)`
Triggers when price is at or above VWAP plus optional buffer.

```csharp
.IsAboveVwap()           // Price >= VWAP
.IsAboveVwap(0.02)       // Price >= VWAP + $0.02
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.IsBelowVwap(double buffer = 0)`
Triggers when price is at or below VWAP minus optional buffer.

```csharp
.IsBelowVwap()           // Price <= VWAP
.IsBelowVwap(0.05)       // Price <= VWAP - $0.05
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.IsPriceAbove(double level)`
Triggers when price is strictly above the level (not equal).

```csharp
.IsPriceAbove(10.00)     // Price > $10.00
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.IsPriceBelow(double level)`
Triggers when price is strictly below the level (not equal).

```csharp
.IsPriceBelow(9.50)      // Price < $9.50
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.When(string name, Func<double, double, bool> condition)`
Adds a custom condition with a descriptive name.

```csharp
.When("Price in range", (price, vwap) => price >= 4.50 && price <= 4.80)
```

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | `string` | Display name for the condition |
| `condition` | `Func<double, double, bool>` | Function receiving (price, vwap), returns true when satisfied |

**Implementation Status:** ✅ Fully Implemented

---

#### `.Condition(IStrategyCondition condition)`
Adds any custom condition implementing the `IStrategyCondition` interface.

```csharp
.Condition(new MyCustomCondition())
```

**Implementation Status:** ✅ Fully Implemented

---

### Technical Indicator Conditions

These conditions check technical indicators for entry/exit signals. They require indicator data to be provided externally.

#### `.IsRsi(RsiState state, double? threshold = null)`
Triggers when RSI (Relative Strength Index) is in the specified state.

```csharp
.IsRsi(RsiState.Oversold)           // RSI <= 30 (default)
.IsRsi(RsiState.Overbought)         // RSI >= 70 (default)
.IsRsi(RsiState.Overbought, 80)     // RSI >= 80 (custom threshold)
```

**Parameters:**
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `state` | `RsiState` | Required | `Overbought` or `Oversold` |
| `threshold` | `double?` | 70/30 | Custom threshold value |

**RSI Interpretation:**
| State | Default Threshold | Trading Signal |
|-------|-------------------|----------------|
| Overbought | >= 70 | Potential selling pressure |
| Oversold | <= 30 | Potential buying opportunity |

**Implementation Status:** ✅ Fully Implemented

---

#### `.IsAdx(Comparison comparison, double threshold)`
Triggers when ADX (Average Directional Index) meets the comparison threshold.

```csharp
.IsAdx(Comparison.Gte, 25)          // ADX >= 25 (strong trend)
.IsAdx(Comparison.Lte, 20)          // ADX <= 20 (weak/no trend)
.IsAdx(Comparison.Gt, 30)           // ADX > 30
```

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `comparison` | `Comparison` | `Gte`, `Lte`, `Gt`, `Lt`, or `Eq` |
| `threshold` | `double` | ADX value (0-100) |

**ADX Interpretation:**
| ADX Range | Trend Strength | Trading Implication |
|-----------|----------------|---------------------|
| < 20 | Weak/No Trend | Range-bound trading |
| 20-25 | Developing | Trend may be forming |
| 25-50 | Strong | Follow the trend |
| 50-75 | Very Strong | Strong momentum |
| > 75 | Extreme | Watch for exhaustion |

**Implementation Status:** ✅ Fully Implemented

---

#### `.IsMacd(MacdState state)`
Triggers when MACD (Moving Average Convergence Divergence) is in the specified state.

```csharp
.IsMacd(MacdState.Bullish)          // MACD > Signal line
.IsMacd(MacdState.Bearish)          // MACD < Signal line
.IsMacd(MacdState.AboveZero)        // MACD line > 0
.IsMacd(MacdState.BelowZero)        // MACD line < 0
.IsMacd(MacdState.HistogramRising)  // Histogram increasing
.IsMacd(MacdState.HistogramFalling) // Histogram decreasing
```

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `state` | `MacdState` | The MACD state to check |

**MACD States:**
| State | Condition | Trading Signal |
|-------|-----------|----------------|
| Bullish | MACD > Signal | Bullish momentum |
| Bearish | MACD < Signal | Bearish momentum |
| AboveZero | MACD > 0 | In uptrend |
| BelowZero | MACD < 0 | In downtrend |
| HistogramRising | Histogram increasing | Momentum building |
| HistogramFalling | Histogram decreasing | Momentum fading |

**Implementation Status:** ✅ Fully Implemented

---

#### `.IsDI(DiDirection direction, double minDifference = 0)`
Triggers when Directional Indicators (+DI/-DI) show the specified relationship.

```csharp
.IsDI(DiDirection.Positive)         // +DI > -DI (bullish)
.IsDI(DiDirection.Negative)         // -DI > +DI (bearish)
.IsDI(DiDirection.Positive, 5)      // +DI > -DI by at least 5
```

**Parameters:**
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `direction` | `DiDirection` | Required | `Positive` or `Negative` |
| `minDifference` | `double` | 0 | Minimum difference between +DI and -DI |

**DI Interpretation:**
| Direction | Condition | Trading Signal |
|-----------|-----------|----------------|
| Positive | +DI > -DI | Bullish pressure dominates |
| Negative | -DI > +DI | Bearish pressure dominates |

**Best Practice:** Combine DI with ADX for confirmation:
```csharp
Stock.Ticker("AAPL")
    .IsAdx(Comparison.Gte, 25)       // Strong trend
    .IsDI(DiDirection.Positive)      // Bullish direction
    .Buy(100, Price.Current)
    .Build();
```

**Implementation Status:** ✅ Fully Implemented

---

### Combining Indicators Example

```csharp
// Complete indicator-based strategy
Stock.Ticker("AAPL")
    .SessionDuration(TradingSession.RTH)
    .IsRsi(RsiState.Oversold)            // RSI <= 30 (oversold)
    .IsAdx(Comparison.Gte, 25)           // ADX >= 25 (strong trend)
    .IsDI(DiDirection.Positive)          // Bullish direction
    .IsMacd(MacdState.Bullish)           // MACD > Signal
    .Breakout(150)                       // Price breaks $150
    .Buy(100, Price.Current)
    .TakeProfit(155)
    .StopLoss(148)
    .Build();
```

---

### Order Methods

#### `.Buy(int quantity, Price priceType)`
Creates a buy order with the new fluent API. Returns `StrategyBuilder` for exit configuration.

```csharp
.Buy(quantity: 100, Price.Current)    // Buy 100 shares at current price
.Buy(quantity: 500, Price.VWAP)       // Buy 500 shares at VWAP
```

**Parameters:**
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `quantity` | `int` | Required | Number of shares |
| `priceType` | `Price` | `Price.Current` | Price type (Current, VWAP, Bid, Ask) |

**Returns:** `StrategyBuilder` (for chaining exit strategies)

**Implementation Status:** ✅ Fully Implemented

---

#### `.Sell(int quantity, Price priceType)`
Creates a sell order. Returns `StrategyBuilder` for exit configuration.

```csharp
.Sell(quantity: 100, Price.Current)
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.Close(int quantity, OrderSide positionSide, Price priceType, OrderType orderType)`
Creates an order to close an existing position.

```csharp
// Close a long position (sells shares)
.Close(quantity: 100, positionSide: OrderSide.Buy)

// Close a short position (buys to cover)
.Close(quantity: 50, positionSide: OrderSide.Sell)
```

**Parameters:**
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `quantity` | `int` | Required | Number of shares to close |
| `positionSide` | `OrderSide` | `Buy` | Your current position side |
| `priceType` | `Price` | `Current` | Price type for execution |
| `orderType` | `OrderType` | `Limit` | Order type |

**Behavior:**
- Long position (`OrderSide.Buy`) → Creates SELL order
- Short position (`OrderSide.Sell`) → Creates BUY order

**Returns:** `StrategyBuilder` (for chaining)

**Implementation Status:** ✅ Fully Implemented

---

#### `.CloseLong(int quantity, Price priceType, OrderType orderType)`
Shorthand for closing a long position. Creates a SELL order.

```csharp
.CloseLong(quantity: 100)    // Sells 100 shares
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.CloseShort(int quantity, Price priceType, OrderType orderType)`
Shorthand for closing a short position. Creates a BUY order to cover.

```csharp
.CloseShort(quantity: 50)    // Buys 50 shares to cover
```

**Implementation Status:** ✅ Fully Implemented

---

### Exit Strategy Methods

These methods are called on `StrategyBuilder` after `.Buy()` or `.Sell()`.

#### `.TakeProfit(double price)`
Sets an absolute take profit price. Submits a limit sell order after entry fill.

```csharp
.TakeProfit(9.00)    // Sell when price reaches $9.00
```

**Behavior:**
- Submits limit order immediately after entry fills
- Cancels stop loss order if take profit fills first
- Order type: Limit (LMT)

**Implementation Status:** ✅ Fully Implemented

---

#### `.TakeProfit(double lowTarget, double highTarget)`
Sets a dynamic take profit range that adjusts based on ADX trend strength.

```csharp
.TakeProfit(4.00, 4.80)    // Conservative $4.00, Aggressive $4.80
```

**ADX-Based Rules:**
| ADX Value | Trend Strength | Take Profit Target |
|-----------|----------------|-------------------|
| < 15 | No Trend | Low target (conservative) |
| 15-25 | Developing | Interpolated between targets |
| 25-35 | Strong | High target (aggressive) |
| > 35 | Very Strong | High target or beyond |
| Rolling Over | Fading | Exit early |

**Implementation Status:** ✅ Fully Implemented

---

#### `.TakeProfit(double lowTarget, double highTarget, thresholds...)`
Sets ADX-based take profit with custom threshold values.

```csharp
.TakeProfit(
    lowTarget: 4.00,
    highTarget: 4.80,
    weakThreshold: 15.0,        // Default: 15
    developingThreshold: 25.0,  // Default: 25
    strongThreshold: 35.0,      // Default: 35
    exitOnRollover: true        // Default: true
)
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.StopLoss(double price)`
Sets a fixed stop loss price. Submits a stop order after entry fill.

```csharp
.StopLoss(6.50)    // Sell if price drops to $6.50
```

**Behavior:**
- Submits stop order immediately after entry fills
- Cancels take profit order if stop loss fills first
- Order type: Stop (STP)

**Implementation Status:** ✅ Fully Implemented

---

#### `.TrailingStopLoss(double percent)`
Enables a percentage-based trailing stop loss that follows price upward.

```csharp
.TrailingStopLoss(Percent.Ten)      // 10% trailing stop
.TrailingStopLoss(Percent.Five)     // 5% trailing stop
.TrailingStopLoss(0.08)             // 8% trailing stop
```

**Behavior:**
1. Initializes at entry price × (1 - percent)
2. Tracks high-water mark (highest price since entry)
3. Recalculates stop as highWaterMark × (1 - percent)
4. Stop only moves UP, never down
5. Triggers immediate market sell when price <= trailing stop
6. Cancels take profit order when triggered

**Implementation Status:** ✅ Fully Implemented

---

#### `.TrailingStopLoss(AtrStopLossConfig config)`
Enables an ATR-based trailing stop loss that adapts to market volatility.

```csharp
// Preset configurations
.TrailingStopLoss(Atr.Tight)        // 1.5× ATR (more stops, smaller losses)
.TrailingStopLoss(Atr.Balanced)     // 2.0× ATR (recommended for swing trading)
.TrailingStopLoss(Atr.Loose)        // 3.0× ATR (trend following, fewer stops)
.TrailingStopLoss(Atr.VeryLoose)    // 4.0× ATR (long-term positions)

// Custom multiplier
.TrailingStopLoss(Atr.Multiplier(2.5))  // 2.5× ATR

// Custom with bounds
.TrailingStopLoss(Atr.WithBounds(
    multiplier: 2.0,
    minStopPercent: 0.02,   // At least 2% away
    maxStopPercent: 0.20    // At most 20% away
))
```

**How ATR Trailing Stop Works:**
1. ATR (Average True Range) is calculated from price volatility over 14 periods
2. Stop distance = ATR × Multiplier (e.g., $1.20 ATR × 2.0 = $2.40)
3. Stop trails upward as price makes new highs
4. Triggers sell when price drops ATR distance below high water mark
5. Automatically adapts to volatility - tighter in calm markets, wider in volatile ones

**ATR Multiplier Guidelines:**

| Multiplier | Preset | Use Case | Characteristics |
|------------|--------|----------|-----------------|
| 1.5× | `Atr.Tight` | Scalping, quick trades | More stops, smaller losses |
| 2.0× | `Atr.Balanced` | Swing trading | Good risk/reward balance |
| 3.0× | `Atr.Loose` | Trend following | Fewer stops, larger swings |
| 4.0× | `Atr.VeryLoose` | Long-term positions | Maximum room to breathe |

**Example with Real Numbers:**

| ATR Value | Multiplier | Stop Distance | Entry $50, High $55 → Stop |
|-----------|------------|---------------|----------------------------|
| $1.20 | 1.5× | $1.80 | $53.20 |
| $1.20 | 2.0× | $2.40 | $52.60 |
| $1.20 | 3.0× | $3.60 | $51.40 |

**Why Use ATR Instead of Percentages?**
- **Adapts to volatility**: A volatile stock needs a wider stop; ATR calculates this automatically
- **Scientifically grounded**: Based on actual price movement, not arbitrary percentages
- **Reduces whipsaws**: Avoids being stopped out by normal market noise

**Implementation Status:** ✅ Fully Implemented

---

### Timing Methods

#### `.Start(TimeOnly startTime)`
Sets the time to begin monitoring the strategy.

```csharp
.Start(Time.PreMarket.Start)           // Start at 4:00 AM ET
.Start(new TimeOnly(4, 30))            // Start at 4:30 AM ET
```

**Implementation Status:** ⚠️ Property Stored (StrategyRunner monitoring not yet implemented)

---

#### `.ClosePosition(TimeOnly time)`
Sets the time to force-close the position if still open.

```csharp
.ClosePosition(Time.PreMarket.End.AddMinutes(-10))    // Close at 9:20 AM ET
```

**Implementation Status:** ⚠️ Property Stored (StrategyRunner auto-close not yet implemented)

---

#### `.End(TimeOnly endTime)`
Sets the time to stop monitoring the strategy. Also builds and returns the strategy.

```csharp
.End(Time.PreMarket.End)    // Stop at 9:30 AM ET
```

**Returns:** `TradingStrategy` (terminal method)

**Implementation Status:** ⚠️ Property Stored (StrategyRunner monitoring not yet implemented)

---

### Configuration Methods

#### `.TimeInForce(TimeInForce tif)`
Sets the time-in-force for orders.

```csharp
.TimeInForce(TIF.GTC)          // Good Till Cancelled
.TimeInForce(TIF.Day)          // Day order
.TimeInForce(TIF.IOC)          // Immediate or Cancel
.TimeInForce(TIF.FOK)          // Fill or Kill
```

**Default:** `TIF.GoodTillCancel`

**Implementation Status:** ✅ Fully Implemented

---

#### `.OutsideRTH(bool outsideRth, bool takeProfit)`
Configures whether orders can execute outside regular trading hours.

```csharp
.OutsideRTH(outsideRth: true, takeProfit: true)    // Both allowed outside RTH
```

**Default:** Both `true`

**Implementation Status:** ✅ Fully Implemented

---

#### `.OrderType(OrderType type)`
Sets the order type for entry.

```csharp
.OrderType(OrderType.Market)
.OrderType(OrderType.Limit)
```

**Default:** `OrderType.Limit` (safer for GTC orders)

**Implementation Status:** ✅ Fully Implemented

---

#### `.AllOrNone(bool allOrNone)`
Requires the entire order to be filled at once or not at all.

```csharp
.AllOrNone()           // Must fill all shares or none
.AllOrNone(true)       // Same as above
.AllOrNone(false)      // Allow partial fills (default)
```

**Warning:** AllOrNone orders may take longer to fill or may not fill at all if the full quantity is not available at your price.

**Default:** `false` (partial fills allowed)

**Implementation Status:** ✅ Fully Implemented

---

#### `.Build()`
Builds and returns the strategy with current configuration. Terminal method.

```csharp
.Build()    // Returns TradingStrategy
```

**Returns:** `TradingStrategy`

**Implementation Status:** ✅ Fully Implemented

---

## Default Values Reference

When building strategies with the fluent API, the following default values are used if a method is not explicitly called.

### Stock Builder Defaults

Properties that apply to the strategy before the order:

| Property | Default Value | Method to Override |
|----------|---------------|-------------------|
| Exchange | `"SMART"` | `.Exchange()` |
| PrimaryExchange | `null` | `.PrimaryExchange()` |
| Currency | `"USD"` | `.Currency()` |
| SecType | `"STK"` | N/A |
| Enabled | `true` | `.Enabled()` |
| StartTime | `null` (no restriction) | `.Start()`, `.SessionDuration()` |
| EndTime | `null` (no restriction) | `.End()`, `.SessionDuration()` |
| Session | `null` (no restriction) | `.SessionDuration()` |
| Notes | `null` | `.WithNotes()` |

### Condition Method Defaults

| Method | Parameter | Default |
|--------|-----------|---------|
| `.IsAboveVwap(buffer)` | buffer | `0` |
| `.IsBelowVwap(buffer)` | buffer | `0` |
| `.IsRsi(state, threshold)` | threshold | `null` (70 for overbought, 30 for oversold) |
| `.IsAdx(comparison, threshold)` | threshold | `25` |
| `.IsDI(direction, minDifference)` | minDifference | `0` |

### Order Method Defaults

Parameters for `.Buy()`, `.Sell()`, `.Close()`:

| Parameter | Default Value |
|-----------|---------------|
| `priceType` | `Price.Current` |
| `orderType` | `OrderType.Limit` |
| `positionSide` (Close only) | `OrderSide.Buy` (closes long position) |

### Strategy Builder Defaults

Properties available after `.Buy()`, `.Sell()`, or `.Close()`:

| Property | Default Value | Method to Override |
|----------|---------------|-------------------|
| TakeProfit | `null` (disabled) | `.TakeProfit()` |
| StopLoss | `null` (disabled) | `.StopLoss()` |
| TrailingStopLoss | disabled | `.TrailingStopLoss()` |
| TrailingStopPercent | `0` | `.TrailingStopLoss(percent)` |
| AtrStopLoss | `null` | `.TrailingStopLoss(Atr.*)` |
| ClosePositionTime | `null` (no auto-close) | `.ClosePosition()` |
| CloseOnlyIfProfitable | `true` | `.ClosePosition(time, false)` |
| TimeInForce | `GoodTillCancel` | `.TimeInForce()` |
| OutsideRth | `true` | `.OutsideRTH()` |
| TakeProfitOutsideRth | `true` | `.OutsideRTH(_, false)` |
| AllOrNone | `false` | `.AllOrNone()` |
| AdxTakeProfit | `null` | `.TakeProfit(low, high)` |

### ADX Take Profit Defaults

When using `.TakeProfit(lowTarget, highTarget)`:

| Property | Default Value | Description |
|----------|---------------|-------------|
| WeakTrendThreshold | `15.0` | ADX below this = weak/no trend |
| DevelopingThreshold | `25.0` | ADX at this level = trend developing |
| StrongTrendThreshold | `35.0` | ADX above this = strong trend |
| ExitOnAdxRollover | `true` | Exit when ADX peaks and falls |

### ATR Stop Loss Defaults

When using `Atr.Multiplier()` or `Atr.WithBounds()`:

| Property | Default Value | Description |
|----------|---------------|-------------|
| Period | `14` | Periods for ATR calculation |
| IsTrailing | `true` | Stop trails price upward |
| MinStopPercent | `0.01` (1%) | Minimum stop distance |
| MaxStopPercent | `0.25` (25%) | Maximum stop distance |

### Quick Reference Card

```
┌─────────────────────────────────────────────────────────────────────┐
│  FLUENT API DEFAULTS - QUICK REFERENCE                              │
├─────────────────────────────────────────────────────────────────────┤
│  Stock Builder:                                                     │
│    Exchange = "SMART"    Currency = "USD"    Enabled = true         │
│    StartTime = null      EndTime = null      Session = null         │
├─────────────────────────────────────────────────────────────────────┤
│  Order Methods (Buy/Sell/Close):                                    │
│    priceType = Price.Current                                        │
│    orderType = OrderType.Limit                                      │
│    positionSide = OrderSide.Buy (Close only)                        │
├─────────────────────────────────────────────────────────────────────┤
│  Strategy Builder (after Buy/Sell/Close):                           │
│    TimeInForce = GTC     OutsideRth = true    AllOrNone = false     │
│    TakeProfit = null     StopLoss = null      TrailingStop = off    │
│    ClosePositionTime = null                   CloseOnlyIfProfit=true│
├─────────────────────────────────────────────────────────────────────┤
│  Condition Defaults:                                                │
│    AboveVwap/BelowVwap buffer = 0                                   │
│    IsRsi threshold = null (70/30)                                   │
│    IsAdx threshold = 25                                             │
│    IsDI minDifference = 0                                           │
├─────────────────────────────────────────────────────────────────────┤
│  ADX TakeProfit Defaults:                                           │
│    WeakThreshold = 15    DevelopingThreshold = 25                   │
│    StrongThreshold = 35  ExitOnRollover = true                      │
├─────────────────────────────────────────────────────────────────────┤
│  ATR StopLoss Defaults:                                             │
│    Period = 14           IsTrailing = true                          │
│    MinStopPercent = 1%   MaxStopPercent = 25%                       │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Helper Classes

### `TradingSession` - Predefined Time Windows

Use with `.SessionDuration()` for easy time window configuration.

| Session | Start (ET) | End (ET) | Description |
|---------|------------|----------|-------------|
| `Active` | - | - | No time restrictions (24/7 monitoring) |
| `PreMarket` | 4:00 AM | 9:30 AM | Full pre-market |
| `PreMarketEndEarly` | 4:00 AM | 9:20 AM | Exit 10 min before open |
| `PreMarketStartLate` | 4:10 AM | 9:30 AM | Skip initial volatility |
| `RTH` | 9:30 AM | 4:00 PM | Regular trading hours |
| `RTHEndEarly` | 9:30 AM | 3:50 PM | Exit 10 min before close |
| `RTHStartLate` | 9:40 AM | 4:00 PM | Skip open volatility |
| `AfterHours` | 4:00 PM | 8:00 PM | After-hours session |
| `AfterHoursEndEarly` | 4:00 PM | 7:50 PM | Exit 10 min early |
| `Extended` | 4:00 AM | 8:00 PM | All sessions |

**Usage:**
```csharp
.SessionDuration(TradingSession.PreMarketEndEarly)    // Most common for pre-market
.SessionDuration(TradingSession.Active)               // No restrictions
```

---

### `Atr` - ATR-Based Stop Loss Factory

Factory for creating volatility-adaptive stop loss configurations. ATR (Average True Range) measures market volatility and adapts stops automatically.

**Preset Configurations:**

| Property | Multiplier | Use Case |
|----------|------------|----------|
| `Atr.Tight` | 1.5× | Scalping, quick trades - more stops, smaller losses |
| `Atr.Balanced` | 2.0× | Swing trading - recommended default |
| `Atr.Loose` | 3.0× | Trend following - fewer stops, larger swings |
| `Atr.VeryLoose` | 4.0× | Long-term positions - maximum room |

**Custom Multipliers:**

```csharp
Atr.Multiplier(2.5)                    // 2.5× ATR
Atr.Multiplier(2.0, period: 20)        // 2× ATR with 20-period calculation
Atr.Multiplier(2.0, isTrailing: false) // Fixed stop (not trailing)
```

**With Min/Max Bounds:**

```csharp
Atr.WithBounds(
    multiplier: 2.0,
    minStopPercent: 0.02,   // At least 2% away (floor)
    maxStopPercent: 0.20,   // At most 20% away (ceiling)
    period: 14,
    isTrailing: true
)
```

**Example Calculations:**

| ATR | Multiplier | Stop Distance | High Water Mark $50 → Stop |
|-----|------------|---------------|----------------------------|
| $1.00 | 2.0× | $2.00 | $48.00 |
| $1.50 | 2.0× | $3.00 | $47.00 |
| $2.00 | 2.0× | $4.00 | $46.00 |
| $1.00 | 3.0× | $3.00 | $47.00 |

**AtrStopLossConfig Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Multiplier` | `double` | 2.0 | ATR multiplier for stop distance |
| `Period` | `int` | 14 | Periods for ATR calculation |
| `IsTrailing` | `bool` | `true` | Whether stop trails price upward |
| `MinStopPercent` | `double` | 0.01 | Minimum stop distance (1%) |
| `MaxStopPercent` | `double` | 0.25 | Maximum stop distance (25%) |

---

### `MarketTime` - Trading Session Periods

All times are defined in **Eastern Time (ET)** - the standard for US equity markets. The default timezone setting is EST (Eastern Standard Time).

| Property | Start (ET) | End (ET) | Description |
|----------|------------|----------|-------------|
| `MarketTime.PreMarket` | 4:00 AM | 9:30 AM | Pre-market session |
| `MarketTime.RTH` | 9:30 AM | 4:00 PM | Regular trading hours |
| `MarketTime.AfterHours` | 4:00 PM | 8:00 PM | After-hours session |
| `MarketTime.Extended` | 4:00 AM | 8:00 PM | Full extended hours |

**Properties:**
| Property | Description |
|----------|-------------|
| `.Start` | Session start time |
| `.End` | Session end time |
| `.Starting` | 10 minutes after start (skip volatility) |
| `.Ending` | 10 minutes before end (exit early) |
| `.StartLocal` | Start time in local timezone |
| `.EndLocal` | End time in local timezone |

**Usage:**
```csharp
MarketTime.PreMarket.Start              // 4:00 AM ET
MarketTime.PreMarket.End                // 9:30 AM ET
MarketTime.PreMarket.Ending             // 9:20 AM ET (10 min before end)
MarketTime.RTH.Start                    // 9:30 AM ET (market open)
MarketTime.RTH.End                      // 4:00 PM ET (market close)
MarketTime.PreMarket.End.AddMinutes(-10) // 9:20 AM ET (manual offset)
```

**Timezone Conversion:**
```csharp
// Convert Eastern to local timezone
var localOpen = TimezoneHelper.ToLocal(Time.RTH.Start, MarketTimeZone.PST);  // 6:30 AM PT

// Convert local to Eastern
var easternTime = TimezoneHelper.ToEastern(new TimeOnly(6, 30), MarketTimeZone.PST);  // 9:30 AM ET
```

---

### Timezone Configuration

The default timezone is **EST (Eastern Standard Time)** - the standard for US equity markets and IBKR.

| Timezone | IBKR API String | Market Open | Market Close |
|----------|-----------------|-------------|--------------|
| EST | US/Eastern | 9:30 AM | 4:00 PM |
| CST | US/Central | 8:30 AM | 3:00 PM |
| MST | US/Mountain | 7:30 AM | 2:00 PM |
| PST | US/Pacific | 6:30 AM | 1:00 PM |

**Configuration:**
```csharp
// In Settings.cs
public const MarketTimeZone Timezone = MarketTimeZone.EST;  // Default
```

---

### `Price` - Order Price Types

| Value | Description |
|-------|-------------|
| `Price.Current` | Execute at current market price |
| `Price.VWAP` | Execute at VWAP price |
| `Price.Bid` | Execute at bid price |
| `Price.Ask` | Execute at ask price |

---

### `Percent` - Common Percentage Values

| Property | Value | Description |
|----------|-------|-------------|
| `Percent.One` | 0.01 | 1% |
| `Percent.Two` | 0.02 | 2% |
| `Percent.Three` | 0.03 | 3% |
| `Percent.Four` | 0.04 | 4% |
| `Percent.Five` | 0.05 | 5% |
| `Percent.Six` | 0.06 | 6% |
| `Percent.Seven` | 0.07 | 7% |
| `Percent.Eight` | 0.08 | 8% |
| `Percent.Nine` | 0.09 | 9% |
| `Percent.Ten` | 0.10 | 10% |
| `Percent.Fifteen` | 0.15 | 15% |
| `Percent.Twenty` | 0.20 | 20% |
| `Percent.TwentyFive` | 0.25 | 25% |
| `Percent.Fifty` | 0.50 | 50% |

**Custom Percentages:**
```csharp
Percent.Custom(12)    // Returns 0.12 (12%)
```

---

### `TIF` - Time In Force Shortcuts

| Property | Alias | Description |
|----------|-------|-------------|
| `TIF.Day` | - | Expires at end of trading day |
| `TIF.GoodTillCancel` | `TIF.GTC` | Active until filled or cancelled |
| `TIF.ImmediateOrCancel` | `TIF.IOC` | Fill immediately or cancel |
| `TIF.FillOrKill` | `TIF.FOK` | Fill entire order or cancel |

---

## Examples

### Basic Pre-Market Strategy

```csharp
Stock
    .Ticker("NAMM")
    .SessionDuration(TradingSession.PreMarketEndEarly)
    .Breakout(7.10)
    .Pullback(6.80)
    .IsAboveVwap()
    .Buy(quantity: 100, Price.Current)
    .TakeProfit(9.00)
    .StopLoss(6.50)
    .Build();
```

### ADX-Based Dynamic Take Profit

```csharp
Stock
    .Ticker("VIVS")
    .SessionDuration(TradingSession.PreMarketEndEarly)
    .IsPriceAbove(2.40)
    .IsAboveVwap()
    .Buy(quantity: 208, Price.Current)
    .TakeProfit(4.00, 4.80)                    // ADX-based: $4.00 (weak) to $4.80 (strong)
    .ClosePosition(MarketTime.PreMarket.Ending)
    .Build();
```

### ATR-Based Volatility-Adaptive Stop Loss

```csharp
Stock
    .Ticker("AAPL")
    .SessionDuration(TradingSession.PreMarketEndEarly)
    .IsPriceAbove(150.00)
    .IsAboveVwap()
    .Buy(quantity: 100, Price.Current)
    .TakeProfit(160.00, 175.00)
    .TrailingStopLoss(Atr.Balanced)            // 2.0× ATR trailing stop
    .ClosePosition(MarketTime.PreMarket.Ending, false)
    .Build();
```

### ATR Stop with Custom Multiplier

```csharp
Stock
    .Ticker("TSLA")
    .SessionDuration(TradingSession.RTH)
    .Breakout(250.00)
    .Pullback(245.00)
    .IsAboveVwap()
    .Buy(quantity: 50, Price.Current)
    .TakeProfit(270.00)
    .TrailingStopLoss(Atr.Multiplier(2.5))     // Custom 2.5× ATR
    .Build();
```

### ATR Stop with Min/Max Bounds

```csharp
Stock
    .Ticker("NVDA")
    .SessionDuration(TradingSession.PreMarket)
    .IsPriceAbove(500.00)
    .IsAboveVwap()
    .Buy(quantity: 20, Price.Current)
    .TakeProfit(550.00)
    .TrailingStopLoss(Atr.WithBounds(
        multiplier: 2.0,
        minStopPercent: 0.02,    // At least 2% ($10 on $500)
        maxStopPercent: 0.10     // At most 10% ($50 on $500)
    ))
    .Build();
```

### Full-Featured Strategy

```csharp
Stock
    .Ticker("AAPL")
    .SessionDuration(TradingSession.PreMarket)
    .Breakout(150.00)
    .Pullback(148.00)
    .IsAboveVwap(buffer: 0.02)
    .Buy(quantity: 200, Price.Current)
    .TakeProfit(155.00)
    .TrailingStopLoss(Percent.Five)
    .TimeInForce(TIF.GTC)
    .OutsideRTH(outsideRth: true, takeProfit: true)
    .ClosePosition(MarketTime.PreMarket.Ending)
    .Build();
```

### OTC/Pink Sheet Strategy

```csharp
Stock
    .Ticker("RPGL")
    .Exchange(ContractExchange.Pink)           // Pink Sheets routing
    .SessionDuration(TradingSession.PreMarketEndEarly)
    .IsPriceAbove(0.88)
    .IsAboveVwap()
    .Buy(quantity: 568, Price.Current)
    .TakeProfit(1.30, 1.70)
    .Build();
```

### Closing a Position Strategy

```csharp
Stock
    .Ticker("AAPL")
    .IsPriceAbove(155)                           // When price hits target
    .CloseLong(quantity: 100)                  // Sell to close long position
    .TimeInForce(TIF.GTC)
    .OutsideRTH(true)
    .Build();
```

### Custom Condition Strategy

```csharp
Stock
    .Ticker("CUSTOM")
    .SessionDuration(TradingSession.Always)    // No time restrictions
    .Breakout(5.00)
    .When("Price in sweet spot", (price, vwap) => price >= 4.50 && price <= 4.80)
    .IsAboveVwap()
    .Buy(quantity: 500, Price.Current)
    .TakeProfit(6.00)
    .StopLoss(4.25)
    .AllOrNone()                               // Must fill all 500 shares
    .Build();
```

### Disabled Strategy (for reference)

```csharp
Stock
    .Ticker("DISABLED")
    .Enabled(false)           // Won't execute
    .Breakout(10.00)
    .Buy(quantity: 100, Price.Current)
    .TakeProfit(12.00)
    .Build();
```

---

## Implementation Status

### ✅ Fully Implemented

| Feature | Builder | StrategyRunner |
|---------|---------|----------------|
| Breakout condition | ✅ | ✅ |
| Pullback condition | ✅ | ✅ |
| AboveVwap condition | ✅ | ✅ |
| BelowVwap condition | ✅ | ✅ |
| PriceAbove condition | ✅ | ✅ |
| PriceBelow condition | ✅ | ✅ |
| Custom condition (When) | ✅ | ✅ |
| Buy order | ✅ | ✅ |
| Sell order | ✅ | ✅ |
| Close/CloseLong/CloseShort | ✅ | ✅ |
| Take profit (fixed) | ✅ | ✅ |
| Take profit (ADX-based) | ✅ | ⚠️ |
| Stop loss | ✅ | ✅ |
| Trailing stop loss (%) | ✅ | ✅ |
| Trailing stop loss (ATR) | ✅ | ✅ |
| TimeInForce | ✅ | ✅ |
| OutsideRTH | ✅ | ✅ |
| OrderType | ✅ | ✅ |
| AllOrNone | ✅ | ✅ |
| Enabled/Disabled | ✅ | ✅ |
| SessionDuration | ✅ | ⚠️ |
| TradingSession enum | ✅ | ⚠️ |
| Exchange (SMART/Pink) | ✅ | ✅ |
| VWAP calculation | N/A | ✅ |
| ATR calculation | N/A | ✅ |
| **Offline Backtesting** | ✅ | N/A |

### ⚠️ Partially Implemented (Builder only)

| Feature | Builder | StrategyRunner | Notes |
|---------|---------|----------------|-------|
| SessionDuration | ✅ | ❌ | Property stored, time window enforcement not yet implemented |
| Start time | ✅ | ❌ | Property stored, monitoring not enforced |
| End time | ✅ | ❌ | Property stored, monitoring not enforced |
| ClosePosition time | ✅ | ❌ | Property stored, auto-close not implemented |
| Price type (Current/VWAP/Bid/Ask) | ✅ | ❌ | Property stored, order price logic not implemented |
| ADX-based TakeProfit | ✅ | ❌ | Config stored, ADX calculation not implemented |

---

## Backtesting

The backtester allows you to test strategies against historical data **without** requiring an IB Gateway connection.

### Quick Start

```csharp
using IdiotProof.UnitTests;

// Define your strategy
var strategy = Stock.Ticker("AAPL")
    .Breakout(150)
    .Buy(100, Price.Current)
    .TakeProfit(160)
    .StopLoss(145)
    .Build();

// Run backtest with generated data
var bars = Backtester.GenerateTestScenario(
    startPrice: 140,
    breakoutPrice: 150,
    pullbackPrice: 148,
    finalPrice: 165);

var result = Backtester.Run(strategy, bars);
Console.WriteLine(result);  // Prints formatted results
```

### Backtest Output

```
╔═══════════════════════════════════════════════════════════════════╗
║  BACKTEST RESULTS: AAPL                                           ║
╠═══════════════════════════════════════════════════════════════════╣
║  Period: 2024-01-15 04:00 to 2024-01-15 04:53                     ║
║  Bars:   54                                                       ║
╠═══════════════════════════════════════════════════════════════════╣
║  TRADES                                                           ║
║  Total:          3        (2W / 1L)                               ║
║  Win Rate:       66.7%                                            ║
║  Profit Factor:  2.50                                             ║
╠═══════════════════════════════════════════════════════════════════╣
║  PROFIT & LOSS                                                    ║
║  Gross Profit:   $  2,500.00                                      ║
║  Gross Loss:     $  1,000.00                                      ║
║  Net P&L:        $  1,494.00                                      ║
╠═══════════════════════════════════════════════════════════════════╣
║  RISK METRICS                                                     ║
║  Max Drawdown:   $    500.00 (0.5%)                               ║
║  Final Equity:   $101,494.00                                      ║
║  Return:         1.49%                                            ║
╚═══════════════════════════════════════════════════════════════════╝
```

### Using IB Gateway for Historical Data

To fetch real historical data from IB Gateway:

```
╔═══════════════════════════════════════════════════════════════════╗
║  IB GATEWAY SETUP FOR HISTORICAL DATA                             ║
╠═══════════════════════════════════════════════════════════════════╣
║                                                                   ║
║  1. Start IB Gateway (not TWS)                                    ║
║     - Paper Trading: Port 4002                                    ║
║     - Live Trading:  Port 4001                                    ║
║                                                                   ║
║  2. Enable API in Gateway:                                        ║
║     Configure → Settings → API → Settings                         ║
║     x Enable ActiveX and Socket Clients                           ║
║     x Read-Only API (recommended for backtesting)                 ║
║                                                                   ║
║  3. Use reqHistoricalData() to fetch bars:                        ║
║                                                                   ║
║     client.reqHistoricalData(                                     ║
║         reqId: 1,                                                 ║
║         contract: contract,                                       ║
║         endDateTime: "",           // Empty = now                 ║
║         durationStr: "1 D",        // 1 day of data               ║
║         barSizeSetting: "1 min",   // 1-minute bars               ║
║         whatToShow: "TRADES",                                     ║
║         useRTH: 0,                 // Include extended hours      ║
║         formatDate: 1,                                            ║
║         keepUpToDate: false,                                      ║
║         chartOptions: null                                        ║
║     );                                                            ║
║                                                                   ║
║  4. Handle data in IbWrapper:                                     ║
║     - historicalData(int reqId, Bar bar)                          ║
║     - historicalDataEnd(int reqId, string start, string end)      ║
║                                                                   ║
║  PACING RULES:                                                    ║
║  • Max 60 requests per 10 minutes                                 ║
║  • Wait 15+ seconds between identical requests                    ║
║                                                                   ║
╚═══════════════════════════════════════════════════════════════════╝
```

### Backtest Configuration

| Option | Default | Description |
|--------|---------|-------------|
| `InitialCash` | `100,000` | Starting account balance |
| `CommissionPerTrade` | `1.00` | Commission per trade (entry + exit) |
| `SlippagePerShare` | `0.00` | Simulated slippage per share |
| `VerboseLogging` | `true` | Print trade details during backtest |

### Limitations

The backtester is a **simplified simulation** with these limitations:

- **No order book simulation** - fills occur at bar close price
- **No slippage modeling** - limit orders fill exactly at limit price
- **No partial fills** - orders fill completely or not at all
- **No market impact** - large orders don't move price
- **Single position** - only one position at a time per strategy

---

## Connection Resilience

IdiotProof includes automatic handling for IBKR connection interruptions. The bot will continue running through brief disconnects without losing your position state.

### How It Works

The IB API reports connectivity status through error codes:

| Error Code | Meaning | Bot Behavior |
|------------|---------|--------------|
| **1100** | Connectivity lost | Displays warning, pauses trading, waits for reconnection |
| **1101** | Connectivity restored (data lost) | Resubscribes to market data automatically |
| **1102** | Connectivity restored (data maintained) | Resumes normal operation immediately |

### Automatic Recovery

When connectivity is restored:

1. **Data Maintained (1102):** The bot continues immediately - all market data subscriptions are still active.

2. **Data Lost (1101):** The bot automatically:
   - Cancels stale market data subscriptions
   - Resubscribes to all symbols
   - Resumes monitoring once data flows again

### What Is Preserved During Disconnects

| Component | Preserved? | Notes |
|-----------|------------|-------|
| Strategy state | ✅ Yes | Condition progress, step tracking |
| Open positions | ✅ Yes | IBKR maintains these server-side |
| Pending orders | ✅ Yes | IBKR maintains these server-side |
| Trailing stop levels | ✅ Yes | High-water marks are preserved |
| Market data subscriptions | ⚠️ Maybe | Depends on error code (1101 vs 1102) |

### Monitoring Connection Status

The bot displays connection events in the console:

```
[08:00:04 AM] *** CONNECTION LOST - Waiting for reconnection... ***
IB ERROR: id=-1 code=1100 msg=Connectivity between IBKR and Trader Workstation has been lost.

[08:01:15 AM] *** CONNECTION RESTORED ***
[08:01:15 AM] Data maintained - continuing normal operation.
```

Or with data loss:

```
[08:01:15 AM] *** CONNECTION RESTORED ***
[08:01:15 AM] Data was lost during disconnect - resubscribing to market data...
[08:01:15 AM] Resubscribing to market data for 2 symbol(s)...
[08:01:16 AM] Market data resubscription complete. Resuming trading.
```

---

## IB Gateway Configuration for Resilience

To minimize disconnects during overnight trading, configure IB Gateway with these recommended settings.

### Required Settings

**Configure → Settings → API → Settings:**

| Setting | Recommended | Description |
|---------|-------------|-------------|
| Enable ActiveX and Socket Clients | ✅ Checked | Required for API connections |
| Socket port | 4001 (live) / 4002 (paper) | Must match `Settings.Port` |
| Allow connections from localhost only | ✅ Checked | Security best practice |
| Read-Only API | ❌ Unchecked | Needed for order submission |

### Stability Settings

**Configure → Settings → Lock and Exit:**

| Setting | Recommended | Description |
|---------|-------------|-------------|
| Auto restart | ✅ Enable | Gateway restarts after crashes |
| Auto logon | ✅ Enable | Reconnects after restarts |
| Store settings on server | ✅ Enable | Preserves settings across reinstalls |

**Configure → Settings → General → Trading Mode:**

| Setting | Recommended | Description |
|---------|-------------|-------------|
| Paper or Live | As needed | Must match your intention |
| API Settings preserved | ✅ Yes | Survives restarts |

### Connection Timeout Prevention

IB Gateway may disconnect idle connections. To prevent this:

1. **Heartbeat Pings:** IdiotProof sends periodic `reqCurrentTime()` calls to keep the connection alive.

2. **Configure heartbeat interval** in `Settings.cs`:
   ```csharp
   public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(15);
   ```

3. **Gateway auto-logoff:** Ensure auto-logoff is disabled or set to a time after market close.

### Network Resilience

For overnight stability:

| Recommendation | Why |
|----------------|-----|
| Wired connection | More stable than WiFi |
| Static IP for trading PC | Prevents DHCP issues |
| UPS battery backup | Survives brief power outages |
| Disable sleep/hibernate | Keeps connection alive |

### IBKR Server Farms

The error messages mention server "farms" - these are IBKR's regional data centers:

| Farm | Purpose |
|------|---------|
| `usfarm` | US trading, market data |
| `ushmds` | US historical market data |
| `secdefil` | Security definitions |
| `cashfarm` | Cash/Forex data |

A partial disconnect (e.g., `ushmds` down but `usfarm` connected) may not affect real-time trading but will prevent historical data requests.

### Common Disconnect Causes

| Cause | Solution |
|-------|----------|
| Gateway timeout | Enable heartbeat in IdiotProof |
| Network glitch | Bot auto-reconnects |
| IBKR server maintenance | Usually brief, bot waits |
| Gateway auto-logoff | Disable or set late time |
| API rate limiting | Reduce request frequency |

### Testing Reconnection

To test the reconnection logic without real risk:

1. Connect to paper trading (port 4002)
2. Start the bot with strategies
3. Briefly disconnect network cable
4. Reconnect and verify bot resumes

---

## Validation Rules

The following combinations are invalid and will throw exceptions:

| Invalid Combination | Error |
|---------------------|-------|
| No conditions before Buy/Sell | `InvalidOperationException` |
| Start time after End time | Should throw validation error |
| Negative quantity | Should throw validation error |
| Take profit below entry (for buy) | Should throw validation error |
| Stop loss above entry (for buy) | Should throw validation error |

---

## License

MIT License - See LICENSE file for details.
