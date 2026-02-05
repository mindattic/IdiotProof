# IdiotProof Trading Strategy Bot

A fluent API framework for building multi-stage trading strategies with Interactive Brokers (IBKR).

## Table of Contents

- [Overview](#overview)
- [Project Structure](#project-structure)
- [Quick Start](#quick-start)
- [Single-Responsibility Pattern](#single-responsibility-pattern)
- [API and DSL Parity](#api-and-dsl-parity)
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
- [AdaptiveOrder - Smart Dynamic Order Management](#adaptiveorder---smart-dynamic-order-management)
  - [How It Works](#how-it-works)
  - [Adaptive Behavior](#adaptive-behavior)
  - [Mode Configuration](#mode-configuration)
  - [Adaptive TP Feedback Loop](#adaptive-tp-feedback-loop)
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
- **Single-Responsibility Pattern** - Each method does one thing
- **API/DSL Parity** - Fluent API and IdiotScript commands are equivalent

---

## Single-Responsibility Pattern

IdiotProof follows a **single-responsibility pattern** where each fluent API method and DSL command does exactly one thing:

- Methods have **at most ONE parameter**
- If no parameter is specified, use the built-in default value
- Methods that previously had 2+ params are split into separate single-responsibility methods

### Example - Single Responsibility Chain:
```csharp
Stock.Ticker("AAPL")
    .Entry(150)              // Condition: Price >= 150
    .Long()                  // Sets direction only
    .Quantity(100)           // Sets quantity separately
    .PriceType(Price.VWAP)   // Sets price type separately
    .OutsideRTH()            // Enables outside RTH for entry
    .TakeProfitOutsideRTH()  // Enables outside RTH for take profit
    .TakeProfit(160)
    .Build();
```

---

## API and DSL Parity

The Fluent API and IdiotScript DSL are designed to be equivalent. Every IdiotScript command maps directly to a Fluent API method.

### Entry vs Order Clarification

**Important distinction:**
- **`Entry(price)`** = A **CONDITION** that triggers when price reaches a level (alias for `IsPriceAbove()`)
- **`Order(IS.LONG)`** or **`Order(IS.SHORT)`** = An **ACTION** that executes an order when all conditions are met
- **`Order()`** with no parameter defaults to `IS.LONG` (long position)

These are NOT the same! Entry is a trigger condition, Order is the order execution.

### Order Direction Syntax

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.Order(OrderDirection.Long)` | `Order(IS.LONG)` | Opens a LONG position |
| `.Order(OrderDirection.Short)` | `Order(IS.SHORT)` | Opens a SHORT position |
| `.Long()` | `Long()` | Alias for Order(IS.LONG) |
| `.Short()` | `Short()` | Alias for Order(IS.SHORT) |
| `.CloseLong()` | `CloseLong()` | Create SELL order to close a long position |
| `.CloseShort()` | `CloseShort()` | Create BUY order to cover a short position |

### Legacy Syntax (Deprecated but still works)
```csharp
.Buy(100, Price.Current)     // Deprecated, use .Long().Quantity(100)
.Sell(100, Price.Current)    // Deprecated, use .Short().Quantity(100)
```

### IdiotScript Parentheses Convention

IdiotScript commands should **always include parentheses**, even for flag-style commands without parameters:

```idiotscript
// Correct (with parentheses)
IsAboveVwap()
Breakout()
Long()

// Deprecated (without parentheses) - still works for backwards compatibility
IsAboveVwap
Breakout
Long
```

### Canonical vs Alias Commands

IdiotScript supports both **canonical** (full) and **alias** (shorthand) forms. The parser accepts both, but the serializer always outputs the **canonical** form.

| Canonical (Preferred) | Alias (Shorthand) | Description |
|-----------------------|-------------------|-------------|
| `Quantity(100)` | `Qty(100)` | Order quantity |
| `TakeProfit(155)` | `TP(155)` | Take profit price |
| `StopLoss(145)` | `SL(145)` | Stop loss price |
| `TrailingStopLoss(10)` | `TSL(10)` | Trailing stop loss % |
| `IsAboveVwap()` | `AboveVwap()` | VWAP condition |
| `IsBelowVwap()` | `BelowVwap()` | VWAP condition |
| `IsEmaAbove(9)` | `EmaAbove(9)` | EMA condition |
| `ExitStrategy(IS.BELL)` | `ClosePosition(IS.BELL)` | Exit timing |

**Best Practice:** Use canonical forms in new scripts for clarity. Aliases are supported for backwards compatibility and quick typing.

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
// Modern Single-Responsibility Syntax
var strategy = Stock
    .Ticker("AAPL")
    .SessionDuration(TradingSession.PreMarketEndEarly)  // 4:00 AM - 9:20 AM ET
    .Breakout(150.00)                         // Step 1: Price >= 150.00
    .Pullback(148.50)                         // Step 2: Price <= 148.50
    .IsAboveVwap()                            // Step 3: Price >= VWAP
    .Long()                                   // Step 4: Open LONG position
    .Quantity(100)                            // Set quantity
    .TakeProfit(155.00)                       // Exit at $155.00
    .TrailingStopLoss(Atr.Balanced)           // 2× ATR trailing stop
    .ExitStrategy(IS.BELL)                    // Close at session end
    .IsProfitable()                           // Only if profitable
    .Build();

// Or with percentage-based trailing stop:
// .TrailingStopLoss(Percent.Ten)            // 10% trailing stop
```

**IdiotScript Equivalent:**
```idiotscript
Ticker(AAPL)
.Session(IS.PREMARKET.END_EARLY)
.Breakout(150.00)
.Pullback(148.50)
.IsAboveVwap()
.Long()
.Qty(100)
.TakeProfit(155.00)
.TrailingStopLoss(IS.ATR.BALANCED)
.ExitStrategy(IS.BELL).IsProfitable()
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
| 💰 Price Conditions | Breakout, Pullback, IsPriceAbove, IsPriceBelow, IsGapUp, IsGapDown |
| 📊 VWAP Conditions | IsAboveVwap, IsBelowVwap, IsCloseAboveVwap, IsVwapRejection |
| 📈 Indicators | IsRsi, IsMacd, IsAdx, IsDI, IsEmaAbove, IsEmaBelow, IsEmaBetween, IsEmaTurningUp |
| 📉 Momentum | IsMomentumAbove, IsMomentumBelow, IsRocAbove, IsRocBelow |
| 📊 Patterns | IsHigherLows, IsVolumeAbove |
| 🛒 Orders | Long, Short, CloseLong, CloseShort |
| 🛡️ Risk Management | TakeProfit, TakeProfitRange, StopLoss, TrailingStopLoss, AdaptiveOrder |
| 📤 Position Management | ExitStrategy, IsProfitable |
| ⚙️ Order Config | TimeInForce, OutsideRTH, TakeProfitOutsideRTH, AllOrNone, PriceType, OrderType |

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
<ProjectRoot>/Logs/
├── ipc_YYYY-MM-DD.log         # IPC communication log (daily)
├── session_state.log          # Current session state (overwritten every 20 min)
├── session_*_final.log        # Final session logs (on normal exit)
├── session_*_crash.log        # Crash logs (on unhandled exception)
└── crash_*.txt                # Crash dumps (if any)
```

> **Note:** Logs are stored in the project root `Logs/` folder for easy access during development.

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

---

## Complete Indicator Reference

This section provides a comprehensive reference for all available indicators with their Fluent API methods, IdiotScript equivalents, ASCII visualizations, and warm-up requirements.

### Indicator Warm-Up Requirements

Technical indicators require historical price bars to calculate properly. The backend uses 1-minute OHLC bars.

```
╔══════════════════════════════════════════════════════════════════════╗
║  INDICATOR WARM-UP REQUIREMENTS                                      ║
╠══════════════════════════════════════════════════════════════════════╣
║  Indicator         │ Bars Needed │ Start Early By │ Period          ║
║  ──────────────────┼─────────────┼────────────────┼────────────────  ║
║  EMA(9)            │ 9 bars      │ 10 minutes     │ Configurable    ║
║  EMA(21)           │ 21 bars     │ 25 minutes     │ Configurable    ║
║  EMA(200)          │ 200 bars    │ 3+ hours       │ Configurable    ║
║  ADX(14)           │ 28 bars     │ 30 minutes     │ Fixed (14)      ║
║  RSI(14)           │ 15 bars     │ 20 minutes     │ Fixed (14)      ║
║  MACD(12,26,9)     │ 35 bars     │ 40 minutes     │ Fixed           ║
║  DI (+DI/-DI)      │ 28 bars     │ 30 minutes     │ Uses ADX        ║
║  Momentum(10)      │ 11 bars     │ 15 minutes     │ Fixed (10)      ║
║  ROC(10)           │ 11 bars     │ 15 minutes     │ Fixed (10)      ║
║  HigherLows(3)     │ 3+ bars     │ 5 minutes      │ Configurable    ║
║  EmaTurningUp(N)   │ N+1 bars    │ N+5 minutes    │ Configurable    ║
║  VolumeAbove       │ 20 bars     │ 25 minutes     │ Fixed (20)      ║
║  CloseAboveVwap    │ 1 bar       │ 2 minutes      │ N/A             ║
║  VwapRejection     │ 1 bar       │ 2 minutes      │ N/A             ║
╚══════════════════════════════════════════════════════════════════════╝
```

**Recommended Start Times:**
- For premarket strategies (4:00 AM session): Start backend at **3:30 AM**
- For RTH strategies (9:30 AM session): Start backend at **9:00 AM**
- For after-hours strategies (4:00 PM session): Start backend at **3:30 PM**

---

### Price Conditions

#### Breakout / Entry

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.Breakout(level)` | `Breakout(150.00)` | Price >= level |
| `.Entry(level)` | `Entry(150.00)` | Price >= level (alias) |
| `.IsPriceAbove(level)` | `IsPriceAbove(150.00)` | Price >= level (alias) |

```
    Breakout/Entry Condition
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║   $150.00 ═════════════════════════════════════════════    ║ ← Breakout Level
    ║          /                                                 ║
    ║         /   ← Price rises to $150 = TRIGGERED             ║
    ║        /                                                   ║
    ║   ____/                                                    ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented

---

#### Pullback

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.Pullback(level)` | `Pullback(148.00)` | Price <= level |
| `.IsPriceBelow(level)` | `IsPriceBelow(148.00)` | Price < level |

```
    Pullback Condition
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║        /\                                                  ║
    ║       /  \                                                 ║
    ║      /    \                                                ║
    ║     /      \___                                            ║
    ║   $148.00 ═════════════════════════════════════════════    ║ ← Pullback Level
    ║              ↑ Price drops to $148 = TRIGGERED            ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented

---

### VWAP Conditions

#### AboveVwap / BelowVwap

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsAboveVwap()` | `IsAboveVwap()` | Price >= VWAP |
| `.IsAboveVwap(0.02)` | `IsAboveVwap(0.02)` | Price >= VWAP + $0.02 buffer |
| `.IsBelowVwap()` | `IsBelowVwap()` | Price <= VWAP |
| `.IsBelowVwap(0.05)` | `IsBelowVwap(0.05)` | Price <= VWAP - $0.05 buffer |

```
    VWAP Conditions
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║   Price ───────────────────────────────                    ║
    ║                       ↑ AboveVwap() = TRUE                 ║
    ║   VWAP  ═══════════════════════════════════════════════    ║
    ║                       ↓ BelowVwap() = TRUE                 ║
    ║   Price ───────────────────────────────                    ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** None (real-time VWAP)

---

#### CloseAboveVwap (Strong VWAP Signal)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsCloseAboveVwap()` | `IsCloseAboveVwap()` | Last candle CLOSED above VWAP |

```
    CloseAboveVwap vs AboveVwap
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║          ┌───┐                                             ║
    ║          │   │ ← Close ABOVE VWAP = Strong signal         ║
    ║   VWAP ══│═══│══════════════════════════════════════════   ║
    ║          │   │                                             ║
    ║          └───┘                                             ║
    ║                                                            ║
    ║          ┌───┐                                             ║
    ║          │   │                                             ║
    ║   VWAP ══│═══│══════════════════════════════════════════   ║
    ║          │   │ ← Close below, wick above (weak signal)    ║
    ║          └───┘                                             ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** 1 candle

---

#### VwapRejection (Bearish Signal)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsVwapRejection()` | `IsVwapRejection()` | Wick above VWAP, close below |
| | `IsVwapRejected()` | Alias |

```
    VWAP Rejection Pattern (Bearish)
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║          │ ← Wick above VWAP (buyers tried)               ║
    ║   VWAP ══╪══════════════════════════════════════════════   ║
    ║        ┌─┴─┐                                               ║
    ║        │   │ ← Close below = REJECTED (sellers won)       ║
    ║        └───┘                                               ║
    ║                                                            ║
    ║   Signal: Failed VWAP reclaim = Bearish                   ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** 1 candle

---

### EMA Conditions

#### EmaAbove / EmaBelow

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsEmaAbove(9)` | `IsEmaAbove(9)` | Price >= EMA(9) |
| `.IsEmaAbove(21)` | `IsEmaAbove(21)` | Price >= EMA(21) |
| `.IsEmaBelow(200)` | `IsEmaBelow(200)` | Price <= EMA(200) |

```
    EMA Conditions
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║   Price Above EMA = Bullish                                ║
    ║        ______/                                             ║
    ║       /   ↑ EmaAbove(9) = TRUE                            ║
    ║   EMA(9) ═══════════════════════════════════════════════   ║
    ║                                                            ║
    ║   Bullish Stack (Price > EMA9 > EMA21 > EMA200):          ║
    ║                                                            ║
    ║   Price  ─────────────────────────────                     ║
    ║   EMA(9)  ════════════════════════════                     ║
    ║   EMA(21) ════════════════════════════                     ║
    ║   EMA(200) ════════════════════════════                    ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** N candles for EMA(N)

---

#### EmaBetween (Pullback Zone)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsEmaBetween(9, 21)` | `IsEmaBetween(9, 21)` | Price between EMA(9) and EMA(21) |

```
    EmaBetween - Pullback Zone Strategy
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║   EMA(21) ═══════════════════════════════════════════════  ║ ← Upper boundary
    ║            ↑                                               ║
    ║        * * * PULLBACK ZONE * * *                          ║
    ║            ↓  (potential entry)                           ║
    ║   EMA(9)  ═══════════════════════════════════════════════  ║ ← Lower boundary
    ║                                                            ║
    ║   Example:                                                 ║
    ║        /\                                                  ║
    ║       /  \___    ← Price pulls back into zone             ║
    ║   ──────────────────────────────                          ║
    ║   ──────────────────────────────                          ║
    ║              ↑ EmaBetween(9,21) = TRUE                    ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** Max(period1, period2) candles

---

#### EmaTurningUp (Slope Detection)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsEmaTurningUp(9)` | `IsEmaTurningUp(9)` | EMA(9) slope is flat or positive |

```
    EMA Turning Up Pattern
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║                                        ___/                ║
    ║                                    ___/                    ║
    ║                                ___/ ← Turning Up!          ║
    ║   \_____                   __/                             ║
    ║         \________________/                                 ║
    ║              ↑ Flattening before turn                      ║
    ║                                                            ║
    ║   EmaTurningUp(9) detects when:                           ║
    ║   Current EMA >= Previous EMA (slope >= 0)                ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** N+1 candles for EMA(N)

---

### Momentum Indicators

#### MomentumAbove / MomentumBelow

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsMomentumAbove(0)` | `IsMomentumAbove(0)` | Momentum >= 0 (positive) |
| `.IsMomentumBelow(0)` | `IsMomentumBelow(0)` | Momentum <= 0 (negative) |

```
    Momentum Indicator
    ╔════════════════════════════════════════════════════════════╗
    ║   Formula: Momentum = Current Price - Price N periods ago  ║
    ╠════════════════════════════════════════════════════════════╣
    ║                                                            ║
    ║        /\                    /\                            ║
    ║       /  \                  /  \     Price                 ║
    ║      /    \                /    \                          ║
    ║   ──/──────\──────────────/──────\────────── Zero Line    ║
    ║             \            /        \                        ║
    ║              \__________/                                  ║
    ║        ↑                                                   ║
    ║   MomentumAbove(0) = TRUE    MomentumBelow(0) = TRUE      ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** 11 candles

---

#### RocAbove / RocBelow (Rate of Change)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsRocAbove(2)` | `IsRocAbove(2)` | ROC >= 2% (strong bullish) |
| `.IsRocBelow(-2)` | `IsRocBelow(-2)` | ROC <= -2% (strong bearish) |

```
    Rate of Change (ROC)
    ╔════════════════════════════════════════════════════════════╗
    ║   Formula: ROC = ((Current - Previous) / Previous) × 100   ║
    ╠════════════════════════════════════════════════════════════╣
    ║                                                            ║
    ║   +5% ───────────────────────────────── Strong Bullish    ║
    ║   +2% ═══════════════════════════════════ Threshold       ║
    ║    0% ───────────────────────────────── Zero Line         ║
    ║   -2% ═══════════════════════════════════ Threshold       ║
    ║   -5% ───────────────────────────────── Strong Bearish    ║
    ║                                                            ║
    ║   RocAbove(2) = Price up 2%+ from 10 bars ago             ║
    ║   RocBelow(-2) = Price down 2%+ from 10 bars ago          ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** 11 candles

---

### RSI (Relative Strength Index)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsRsi(RsiState.Oversold)` | `IsRsiOversold()` | RSI <= 30 |
| `.IsRsi(RsiState.Oversold, 25)` | `IsRsiOversold(25)` | RSI <= 25 |
| `.IsRsi(RsiState.Overbought)` | `IsRsiOverbought()` | RSI >= 70 |
| `.IsRsi(RsiState.Overbought, 80)` | `IsRsiOverbought(80)` | RSI >= 80 |

```
    RSI Scale (0-100)
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║   100 ─────────────────────────────────────────── Top     ║
    ║    70 ══════════════ OVERBOUGHT ═══════════════════════   ║ ← RsiOverbought(70)
    ║    50 ─────────────── Neutral ────────────────────────    ║
    ║    30 ══════════════ OVERSOLD ═════════════════════════   ║ ← RsiOversold(30)
    ║     0 ─────────────────────────────────────────── Bot     ║
    ║                                                            ║
    ║   RSI Oversold Bounce Strategy:                           ║
    ║        70 ════════════════════════════════════════════    ║
    ║           ____                          ____              ║
    ║          /    \                        /    \             ║
    ║         /      \                      /      \  RSI       ║
    ║        /        \                    /        \           ║
    ║       /          \                  /          \          ║
    ║        30 ════════════════════════════════════════════    ║
    ║                  ↑ Buy signal (RSI <= 30)                 ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** 15 candles

---

### ADX (Average Directional Index)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsAdx(Comparison.Gte, 25)` | `IsAdxAbove(25)` | ADX >= 25 (strong trend) |

```
    ADX Trend Strength
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║   75+ ────────── Extremely Strong (rare) ────────────     ║
    ║   50+ ────────── Very Strong Trend ──────────────────     ║
    ║   25+ ═══════════════ STRONG ═══════════════════════════  ║ ← AdxAbove(25)
    ║   20  ────────── Trend Developing ───────────────────     ║
    ║   <20 ────────── Weak/No Trend (ranging) ────────────     ║
    ║                                                            ║
    ║   ADX measures TREND STRENGTH (not direction)             ║
    ║   Use with DI for direction:                              ║
    ║     - AdxAbove(25) + DiPositive() = Strong bullish trend  ║
    ║     - AdxAbove(25) + DiNegative() = Strong bearish trend  ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** 28 candles

---

### DI (Directional Index)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsDI(DiDirection.Positive)` | `IsDiPositive()` | +DI > -DI (bullish) |
| `.IsDiPositive()` | `IsDiPositive()` | Shorthand for above |
| `.IsDI(DiDirection.Positive, 5)` | `IsDiPositive(5)` | +DI > -DI by at least 5 |
| `.IsDI(DiDirection.Negative)` | `IsDiNegative()` | -DI > +DI (bearish) |
| `.IsDiNegative()` | `IsDiNegative()` | Shorthand for above |

```
    Directional Index (+DI / -DI)
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║   +DI (Bullish Pressure)                                   ║
    ║         /    \         ___/                                ║
    ║        /      \    ___/    \                               ║
    ║       /        \__/         \                              ║
    ║      /  ___                  \____                         ║
    ║   __/__/   \_____                 \                        ║
    ║                  -DI (Bearish Pressure)                    ║
    ║         ↑                                                  ║
    ║   +DI > -DI = DiPositive() = Bullish                      ║
    ║                                                            ║
    ║   Best Practice: Combine with ADX                         ║
    ║   .IsAdx(Comparison.Gte, 25)  // Strong trend             ║
    ║   .IsDiPositive()             // Bullish direction        ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** 28 candles (uses ADX)

---

### MACD (Moving Average Convergence Divergence)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsMacd(MacdState.Bullish)` | `IsMacdBullish()` | MACD > Signal line |
| `.IsMacdBullish()` | `IsMacdBullish()` | Shorthand for above |
| `.IsMacd(MacdState.Bearish)` | `IsMacdBearish()` | MACD < Signal line |
| `.IsMacdBearish()` | `IsMacdBearish()` | Shorthand for above |
| `.IsMacd(MacdState.AboveZero)` | `IsMacdAboveZero()` | MACD line > 0 |
| `.IsMacd(MacdState.BelowZero)` | `IsMacdBelowZero()` | MACD line < 0 |

```
    MACD Components
    ╔════════════════════════════════════════════════════════════╗
    ║   MACD Line = EMA(12) - EMA(26)                            ║
    ║   Signal Line = EMA(9) of MACD                             ║
    ║   Histogram = MACD - Signal                                ║
    ╠════════════════════════════════════════════════════════════╣
    ║                                                            ║
    ║   MACD Bullish Signal:                                     ║
    ║         MACD ___                                           ║
    ║             /   \         ___/                             ║
    ║   Signal __/     \    ___/                                 ║
    ║                   \__/                                     ║
    ║           ↑ MACD > Signal = Bullish (MacdBullish())       ║
    ║                                                            ║
    ║   MACD Histogram:                                          ║
    ║              ████                                          ║
    ║          ████████                                          ║
    ║      ████████████         ████                             ║
    ║   ─────────────────────────────────────────── Zero        ║
    ║                       ████████                             ║
    ║                   ████████████                             ║
    ║       ↑ Rising        ↑ Falling                           ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** 35 candles

---

### Pattern Detection

#### HigherLows

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsHigherLows()` | `IsHigherLows()` | Default 3-bar lookback |
| `.IsHigherLows(5)` | `IsHigherLows(5)` | 5-bar lookback |

```
    Higher Lows Pattern (Bullish)
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║                              /\                            ║
    ║                  /\         /  \                           ║
    ║        /\       /  \       /    \                          ║
    ║       /  \     /    \     /      \                         ║
    ║      /    \___/  ↑   \___/        \                        ║
    ║             Higher    Higher                               ║
    ║               Low       Low                                ║
    ║                                                            ║
    ║   HigherLows() detects:                                   ║
    ║     Low[0] > Low[1] > Low[2]                              ║
    ║   (most recent low is highest)                            ║
    ║                                                            ║
    ║   Signal: Building support, bullish momentum              ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** 3+ candles (configurable)

---

#### VolumeAbove

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsVolumeAbove(1.5)` | `IsVolumeAbove(1.5)` | Volume >= 1.5x average |
| `.IsVolumeAbove(2.0)` | `IsVolumeAbove(2.0)` | Volume >= 2x average |

```
    Volume Spike Detection
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║                  ████                                      ║
    ║                  ████  ← Volume spike (1.5x+)             ║
    ║   Average ──────────────────────────────────────────────   ║
    ║     ████      ████████      ████                           ║
    ║     ████  ██  ████████  ██  ████                           ║
    ║                                                            ║
    ║   Common Multipliers:                                      ║
    ║     1.5x = Moderate spike (50% above average)             ║
    ║     2.0x = Strong spike (100% above average)              ║
    ║     3.0x = Exceptional spike (200% above average)         ║
    ║                                                            ║
    ║   Use: Confirm breakouts with volume                      ║
    ║   .Breakout(150).IsVolumeAbove(1.5).Long()...            ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** 20 candles

---

### Gap Conditions

#### IsGapUp / GapUp (canonical / alias)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsGapUp(5)` | `IsGapUp(5)` | Price gapped up >= 5% from previous close (canonical) |
| `.GapUp(5)` | `GapUp(5)` | Alias for IsGapUp(5) |
| `.IsGapUp(10)` | `IsGapUp(10%)` | Price gapped up >= 10% (% sign optional) |

```
    Gap Up Detection
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║               Gap Up ┌────┐                                ║
    ║               (5%+)  │    │  Current Price                 ║
    ║                      └────┘                                ║
    ║                         ↑                                  ║
    ║       ──────────────────────────────── Gap                 ║
    ║                         ↓                                  ║
    ║       ┌────┐                                               ║
    ║       │    │  Previous Close                               ║
    ║       └────┘                                               ║
    ║                                                            ║
    ║   Formula: ((Current - PrevClose) / PrevClose) * 100      ║
    ║                                                            ║
    ║   Use Case: Gap-and-Go strategies, morning momentum       ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Example Strategy:**
```idiotscript
Ticker(NVDA)
.Session(IS.PREMARKET)
.IsGapUp(5)                  // Gapped up 5%+ from previous close
.IsAboveVwap()               // Holding above VWAP
.IsDiPositive()              // Bullish directional movement
.Order(IS.LONG)
.TakeProfit(+2%)             // 2% profit target
.StopLoss(-1%)               // 1% stop loss
.Build()
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** Previous session close required (auto-fetched from historical data)

---

#### IsGapDown / GapDown (canonical / alias)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsGapDown(3)` | `IsGapDown(3)` | Price gapped down >= 3% from previous close (canonical) |
| `.GapDown(3)` | `GapDown(3)` | Alias for IsGapDown(3) |
| `.IsGapDown(5)` | `IsGapDown(5%)` | Price gapped down >= 5% (% sign optional) |

```
    Gap Down Detection
    ╔════════════════════════════════════════════════════════════╗
    ║                                                            ║
    ║       ┌────┐                                               ║
    ║       │    │  Previous Close                               ║
    ║       └────┘                                               ║
    ║                         ↑                                  ║
    ║       ──────────────────────────────── Gap                 ║
    ║                         ↓                                  ║
    ║               Gap Down ┌────┐                              ║
    ║               (3%+)    │    │  Current Price               ║
    ║                        └────┘                              ║
    ║                                                            ║
    ║   Formula: ((PrevClose - Current) / PrevClose) * 100      ║
    ║                                                            ║
    ║   Use Case: Gap-fill reversals, panic selling bounces     ║
    ║                                                            ║
    ╚════════════════════════════════════════════════════════════╝
```

**Example Strategy (Short on Gap Down):**
```idiotscript
Ticker(NVDA)
.Session(IS.RTH)
.IsGapDown(5)                // Gapped down 5%+ from previous close
.IsBelowVwap()               // Holding below VWAP
.IsDiNegative()              // Bearish directional movement
.Order(IS.SHORT)
.TakeProfit(-3%)             // 3% profit on short
.StopLoss(+2%)               // 2% stop loss on short
.Build()
```

**Example Strategy (Long on Gap Down Bounce):**
```idiotscript
Ticker(AAPL)
.Session(IS.RTH)
.IsGapDown(3)                // Gapped down 3%+ (oversold)
.IsAboveVwap()               // Reclaiming VWAP (strength returning)
.IsRsiOversold(30)           // RSI confirms oversold bounce
.Order(IS.LONG)
.TakeProfit(+2%)
.Build()
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** Previous session close required (auto-fetched from historical data)

---

### Execution Behavior

#### Repeat / IsRepeat (canonical / alias)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.Repeat()` | `Repeat()` | Repeat strategy after trade completes (default: enabled) |
| `.Repeat(true)` | `Repeat(YES)` | Explicitly enable repeat mode |
| `.Repeat(false)` | `Repeat(NO)` | Disable repeat mode (one-shot) |
| `.IsRepeat()` | `IsRepeat()` | Alias for Repeat() |

```
    Repeat Strategy Flow
    ╔════════════════════════════════════════════════════════════════════╗
    ║                                                                    ║
    ║   ┌──────────────────────────────────────────────────────────────┐ ║
    ║   │                                                              │ ║
    ║   ▼                                                              │ ║
    ║  [WAIT FOR CONDITIONS]                                           │ ║
    ║        │                                                         │ ║
    ║        ▼ (all conditions met)                                    │ ║
    ║  [PLACE ENTRY ORDER]                                             │ ║
    ║        │                                                         │ ║
    ║        ▼ (entry filled)                                          │ ║
    ║  [MONITOR POSITION]                                              │ ║
    ║        │                                                         │ ║
    ║        ├── TakeProfit hit ────┐                                  │ ║
    ║        ├── StopLoss hit ──────┤                                  │ ║
    ║        ├── TrailingStop hit ──┤                                  │ ║
    ║        └── ExitStrategy hit ──┤                                  │ ║
    ║                               ▼                                  │ ║
    ║                    ┌──────────────────┐                          │ ║
    ║                    │  RepeatEnabled?  │                          │ ║
    ║                    └──────────────────┘                          │ ║
    ║                       │           │                              │ ║
    ║                    YES│           │NO                            │ ║
    ║                       ▼           ▼                              │ ║
    ║                  [RESET]     [COMPLETE]                          │ ║
    ║                      │                                           │ ║
    ║                      └───────────────────────────────────────────┘ ║
    ║                                                                    ║
    ║   Reset clears: Order IDs, fill prices, condition index           ║
    ║   Reset keeps:  VWAP tracking, session high/low, indicators       ║
    ║                                                                    ║
    ╚════════════════════════════════════════════════════════════════════╝
```

**Repeat Triggers (will reset and wait for conditions again):**
- TakeProfit filled
- StopLoss filled  
- TrailingStopLoss filled
- ExitStrategy triggered (if profitable when IsProfitable chained)

**No Repeat (strategy completes):**
- Entry order cancelled
- Missed the boat (price moved away)
- Session ended before entry

**Example - Repeating Day Trading Strategy:**
```idiotscript
Ticker(NVDA)
.Session(IS.RTH)
.Entry(150.00)
.IsAboveVwap()
.IsDiPositive()
.Order(IS.LONG)
.Quantity(10)
.TakeProfit(152.00)          // +$2 profit target
.StopLoss(149.00)            // -$1 stop loss
.Repeat()                    // After TP/SL, wait for conditions again
.Build()
```

This strategy will:
1. Wait for price to hit $150 + above VWAP + DI positive
2. Enter 10 shares long
3. Exit at $152 (profit) or $149 (stop)
4. **Reset** and go back to step 1, looking for the next opportunity

**Example - One-Shot Earnings Play:**
```idiotscript
Ticker(AAPL)
.Session(IS.PREMARKET)
.IsGapUp(5)                  // Gap up 5%+ on earnings
.IsAboveVwap()
.Order(IS.LONG)
.TakeProfit(+3%)
.StopLoss(-2%)
.Repeat(NO)                  // One trade only
.Build()
```

**Implementation Status:** ✅ Fully Implemented | **Warm-up:** None required

---

### Complete IdiotScript Command Reference

```
╔════════════════════════════════════════════════════════════════════════╗
║  IDIOTSCRIPT COMMAND REFERENCE (Single-Responsibility Pattern)         ║
╠════════════════════════════════════════════════════════════════════════╣
║                                                                         ║
║  CONFIGURATION (order doesn't matter within group):                     ║
║    Ticker(AAPL)              - Set stock symbol                        ║
║    Name("Strategy Name")     - Set strategy name                       ║
║    Session(IS.PREMARKET)     - Set trading session                     ║
║    Qty(100)                  - Set order quantity (can also chain      ║
║                                after Long()/Short())                   ║
║    Enabled(YES)              - Enable/disable strategy                 ║
║                                                                         ║
║  ENTRY CONDITIONS (order matters - sequential state machine):           ║
║    Entry(150.00)             - Price >= level (CONDITION)              ║
║    Breakout(150.00)          - Price >= level (alias for Entry)        ║
║    Pullback(148.00)          - Price <= level                          ║
║    IsPriceAbove(150.00)      - Price > level                           ║
║    IsPriceBelow(148.00)      - Price < level                           ║
║                                                                         ║
║  VWAP CONDITIONS:                                                       ║
║    IsAboveVwap()             - Price >= VWAP                           ║
║    IsBelowVwap()             - Price <= VWAP                           ║
║    IsCloseAboveVwap()        - Candle closed above VWAP (strong)       ║
║    IsVwapRejection()         - Wick above, close below VWAP            ║
║                                                                         ║
║  EMA CONDITIONS:                                                        ║
║    IsEmaAbove(9)             - Price >= EMA(9)                         ║
║    IsEmaBelow(200)           - Price <= EMA(200)                       ║
║    IsEmaBetween(9, 21)       - Price between EMA(9) and EMA(21)        ║
║    IsEmaTurningUp(9)         - EMA(9) slope >= 0                       ║
║                                                                         ║
║  MOMENTUM CONDITIONS:                                                   ║
║    IsMomentumAbove(0)        - Momentum >= threshold                   ║
║    IsMomentumBelow(0)        - Momentum <= threshold                   ║
║    IsRocAbove(2)             - Rate of Change >= 2%                    ║
║    IsRocBelow(-2)            - Rate of Change <= -2%                   ║
║                                                                         ║
║  RSI CONDITIONS:                                                        ║
║    IsRsiOversold()           - RSI <= 30 (default)                     ║
║    IsRsiOversold(25)         - RSI <= 25                               ║
║    IsRsiOverbought()         - RSI >= 70 (default)                     ║
║    IsRsiOverbought(80)       - RSI >= 80                               ║
║                                                                         ║
║  TREND CONDITIONS:                                                      ║
║    IsAdxAbove(25)            - ADX >= 25 (strong trend)                ║
║    IsDiPositive()            - +DI > -DI (bullish)                     ║
║    IsDiNegative()            - -DI > +DI (bearish)                     ║
║    IsMacdBullish()           - MACD > Signal line                      ║
║    IsMacdBearish()           - MACD < Signal line                      ║
║                                                                         ║
║  PATTERN CONDITIONS:                                                    ║
║    IsHigherLows()            - Higher lows forming (default 3 bars)    ║
║    IsHigherLows(5)           - Higher lows with 5-bar lookback         ║
║    IsVolumeAbove(1.5)        - Volume >= 1.5x average                  ║
║                                                                         ║
║  GAP CONDITIONS:                                                        ║
║    IsGapUp(5)                - Price gapped up 5%+ from prev close     ║
║    IsGapDown(3)              - Price gapped down 3%+ from prev close   ║
║                                                                         ║
║  ORDER ACTIONS (after conditions are met):                              ║
║    Order(IS.LONG)            - Opens a LONG position (explicit)        ║
║    Order(IS.SHORT)           - Opens a SHORT position                  ║
║    Order()                   - Opens a LONG position (default)         ║
║    Long()                    - Alias for Order(IS.LONG)                ║
║    Short()                   - Alias for Order(IS.SHORT)               ║
║    CloseLong()               - Close LONG position                     ║
║    CloseShort()              - Close SHORT position                    ║
║                                                                         ║
║  ORDER CONFIGURATION (chained after Order):                             ║
║    .Quantity(100)            - Sets order quantity (canonical)         ║
║    .Qty(100)                 - Alias for Quantity                      ║
║    .PriceType(IS.VWAP)       - Sets price type for execution           ║
║    .OrderType(IS.MARKET)     - Sets market vs limit order              ║
║    .OutsideRTH()             - Allow entry outside RTH (default: true) ║
║    .TakeProfitOutsideRTH()   - Allow TP outside RTH (default: true)    ║
║                                                                         ║
║  EXIT CONDITIONS (after position is opened):                            ║
║    TakeProfit(155.00)        - Take profit at price (canonical)        ║
║    TP(155.00)                - Alias for TakeProfit                    ║
║    StopLoss(145.00)          - Stop loss at price (canonical)          ║
║    SL(145.00)                - Alias for StopLoss                      ║
║    TrailingStopLoss(IS.TIGHT) - Trailing stop (canonical)              ║
║    TSL(5)                    - Alias for TrailingStopLoss              ║
║    ExitStrategy(IS.BELL)     - Close at session end (canonical)        ║
║    .IsProfitable()           - Only exit if profitable (chained)       ║
║                                                                         ║
║  SMART ORDER MANAGEMENT:                                                ║
║    AdaptiveOrder()           - Enable smart dynamic TP/SL (balanced)   ║
║    AdaptiveOrder(IS.CONSERVATIVE) - Protect gains quickly              ║
║    AdaptiveOrder(IS.BALANCED)     - Equal priority profit/protection   ║
║    AdaptiveOrder(IS.AGGRESSIVE)   - Maximize profit in strong trends   ║
║    IsAdaptiveOrder()         - Alias for AdaptiveOrder()               ║
║                                                                         ║
║  ORDER CONFIG:                                                          ║
║    TimeInForce(IS.GTC)       - Order time in force                     ║
║    OutsideRTH(YES)           - Allow extended hours                    ║
║    AllOrNone(YES)            - Require full fill                       ║
║    Repeat(YES)               - Repeat strategy after exit              ║
║                                                                         ║
╚════════════════════════════════════════════════════════════════════════╝
```

---

### Strategy Example with All Components

**IdiotScript:**
```idiotscript
# Complete Strategy Example with Single-Responsibility Pattern
Ticker(AAPL)
.Name("Trend Continuation")
.Session(IS.PREMARKET)

# Entry Conditions (evaluated sequentially):
.Entry(150.00)           # 1. Price breaks $150
.IsHigherLows()          # 2. Higher lows forming
.IsAboveVwap()           # 3. Above VWAP
.IsEmaAbove(9)           # 4. Above short-term EMA
.IsDiPositive()          # 5. Bullish direction
.IsMomentumAbove(0)      # 6. Positive momentum

# Order Action:
.Long()                  # Open LONG position
.Qty(100)                # 100 shares

# Exit Conditions:
.TakeProfit(160.00)
.StopLoss(145.00)
.TrailingStopLoss(IS.MODERATE)
.ExitStrategy(IS.BELL).IsProfitable()
```

**Equivalent Fluent API:**
```csharp
Stock.Ticker("AAPL")
    .WithName("Trend Continuation")
    .SessionDuration(TradingSession.PreMarket)
    .IsPriceAbove(150.00)
    .IsHigherLows()
    .IsAboveVwap()
    .IsEmaAbove(9)
    .IsDiPositive()
    .IsMomentumAbove(0)
    .Long()
    .Quantity(100)
    .TakeProfit(160.00)
    .StopLoss(145.00)
    .TrailingStopLoss(Percent.Ten)
    .ExitStrategy(IS.BELL)
    .IsProfitable()
    .Build();
```

**Execution Flow Visualization:**
```
╔═══════════════════════════════════════════════════════════════════════════════╗
║  [CONFIG]           →   [ENTRY CONDITIONS]          →   ✅ ORDER              ║
║  ┌────────────────┐     ┌──────────────────────┐        ┌────────────────┐    ║
║  │ Ticker(AAPL)   │     │ Entry(150.00)        │        │ Long()         │    ║
║  │ Session(PRE)   │  →  │ IsHigherLows()       │  →     │ Qty(100)       │    ║
║  │ Name(...)      │     │ IsAboveVwap()        │        └────────────────┘    ║
║  └────────────────┘     │ IsEmaAbove(9)        │              │               ║
║                         │ IsDiPositive()       │              ↓               ║
║                         │ IsMomentumAbove(0)   │     [EXIT CONDITIONS]        ║
║                         └──────────────────────┘     ┌────────────────────┐   ║
║                                                      │ TakeProfit(160)    │   ║
║                                                      │ StopLoss(145)      │   ║
║                                                      │ TrailingStopLoss() │   ║
║                                                      │ ExitStrategy(BELL) │   ║
║                                                      │ .IsProfitable()    │   ║
║                                                      └────────────────────┘   ║
╚═══════════════════════════════════════════════════════════════════════════════╝
```

---

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
// Complete indicator-based strategy with modern syntax
Stock.Ticker("AAPL")
    .SessionDuration(TradingSession.RTH)
    .IsRsi(RsiState.Oversold)            // RSI <= 30 (oversold)
    .IsAdx(Comparison.Gte, 25)           // ADX >= 25 (strong trend)
    .IsDiPositive()                      // Bullish direction
    .IsMacdBullish()                     // MACD > Signal
    .Breakout(150)                       // Price breaks $150
    .Long()                              // Open LONG position
    .Quantity(100)
    .TakeProfit(155)
    .StopLoss(148)
    .Build();
```

**IdiotScript Equivalent:**
```idiotscript
Ticker(AAPL)
.Session(IS.RTH)
.IsRsiOversold()
.IsAdxAbove(25)
.IsDiPositive()
.IsMacdBullish()
.Breakout(150)
.Long()
.Qty(100)
.TakeProfit(155)
.StopLoss(148)
```

---

### Order Methods

Orders follow the single-responsibility pattern. Set direction first, then chain configuration methods.

#### `.Order(OrderDirection direction)`
Creates an order with the specified direction. Returns `StrategyBuilder` for additional configuration.

```csharp
.Order(OrderDirection.Long)    // Open a LONG position
.Order(OrderDirection.Short)   // Open a SHORT position
```

**IdiotScript:** `Order(IS.LONG)` or `Order(IS.SHORT)`

**Returns:** `StrategyBuilder` (for chaining exit strategies and configuration)

---

#### `.Long()`
Creates a LONG order (buy to open). Shorthand for `.Order(OrderDirection.Long)`.

```csharp
.Long()    // Open a LONG position
```

**IdiotScript:** `Long()`

---

#### `.Short()`
Creates a SHORT order (sell to open). Shorthand for `.Order(OrderDirection.Short)`.

```csharp
.Short()    // Open a SHORT position
```

**IdiotScript:** `Short()`

---

#### `.Quantity(int quantity)`
Sets the order quantity. Can be placed in two locations:

**Option 1: Config section** (before conditions) - Recommended for readability:
```csharp
Stock.Ticker("AAPL")
    .Qty(100)                // Set quantity early
    .Breakout(150)
    .Long()                  // No need to repeat quantity
    .TakeProfit(160)
    .Build();
```

**Option 2: After order direction** (chained after `.Long()` or `.Short()`):
```csharp
Stock.Ticker("AAPL")
    .Breakout(150)
    .Long().Quantity(100)    // Set quantity after direction
    .TakeProfit(160)
    .Build();
```

**IdiotScript Examples:**
```idiotscript
# Option 1: Config section (recommended)
Ticker(AAPL)
.Qty(100)
.Breakout(150)
.Long()
.TakeProfit(160)

# Option 2: After order
Ticker(AAPL)
.Breakout(150)
.Long()
.Qty(100)
.TakeProfit(160)
```

**Note:** If quantity is specified in both locations, the config-level value is used.

**Aliases:** `Qty(100)` or `Quantity(100)`

---

#### `.PriceType(Price priceType)`
Sets the price type for order execution.

```csharp
.Long().Quantity(100).PriceType(Price.VWAP)    // Execute at VWAP
```

| Price Type | Description |
|------------|-------------|
| `Price.Current` | Current market price (default) |
| `Price.VWAP` | Volume-weighted average price |
| `Price.Bid` | Bid price |
| `Price.Ask` | Ask price |

**IdiotScript:** `PriceType(IS.VWAP)`

---

#### `.OrderType(OrderType type)`
Sets the order type for entry.

```csharp
.Long().OrderType(OrderType.Market)    // Market order
.Long().OrderType(OrderType.Limit)     // Limit order (default)
```

**IdiotScript:** `OrderType(IS.MARKET)` or `OrderType(IS.LIMIT)`

---

#### `.OutsideRTH()`
Allows entry orders to execute outside regular trading hours. Enabled by default.

```csharp
.Long().OutsideRTH()    // Allow entry outside RTH (default: true)
```

**IdiotScript:** `OutsideRTH()`

---

#### `.TakeProfitOutsideRTH()`
Allows take profit orders to execute outside regular trading hours. Enabled by default.

```csharp
.Long().TakeProfitOutsideRTH()    // Allow TP outside RTH (default: true)
```

**IdiotScript:** `TakeProfitOutsideRTH()`

---

#### `.CloseLong()`
Creates a SELL order to close an existing long position.

```csharp
.CloseLong()    // Sell to close long position
```

**IdiotScript:** `CloseLong()`

---

#### `.CloseShort()`
Creates a BUY order to cover an existing short position.

```csharp
.CloseShort()    // Buy to cover short position
```

**IdiotScript:** `CloseShort()`

---

### Legacy Order Methods (Deprecated)

These methods still work but are deprecated in favor of the single-responsibility pattern.

#### `.Buy(int quantity, Price priceType)` *(Deprecated)*
Creates a buy order with the old API. Use `.Long().Quantity(n)` instead.

```csharp
// Deprecated
.Buy(quantity: 100, Price.Current)

// Modern equivalent
.Long().Quantity(100).PriceType(Price.Current)
```

---

#### `.Sell(int quantity, Price priceType)` *(Deprecated)*
Creates a sell order with the old API. Use `.Short().Quantity(n)` instead.

```csharp
// Deprecated
.Sell(quantity: 100, Price.Current)

// Modern equivalent
.Short().Quantity(100).PriceType(Price.Current)
```

---

### Exit Strategy Methods

These methods are called on `StrategyBuilder` after `.Long()`, `.Short()`, or their legacy equivalents.

#### `.TakeProfit(double price)`
Sets an absolute take profit price. Submits a limit sell order after entry fill.

```csharp
.TakeProfit(9.00)    // Sell when price reaches $9.00
```

**IdiotScript:** `TakeProfit(9.00)` or `TP(9.00)`

**Behavior:**
- Submits limit order immediately after entry fills
- Cancels stop loss order if take profit fills first
- Order type: Limit (LMT)

---

#### `.TakeProfit(double lowTarget, double highTarget)`
Sets a dynamic take profit range that adjusts based on ADX trend strength.

```csharp
.TakeProfit(4.00, 4.80)    // Conservative $4.00, Aggressive $4.80
```

**IdiotScript:** `TakeProfit(4.00, 4.80)` or `TakeProfitRange(4.00, 4.80)`

**ADX-Based Rules:**
| ADX Value | Trend Strength | Take Profit Target |
|-----------|----------------|-------------------|
| < 15 | No Trend | Low target (conservative) |
| 15-25 | Developing | Interpolated between targets |
| 25-35 | Strong | High target (aggressive) |
| > 35 | Very Strong | High target or beyond |
| Rolling Over | Fading | Exit early |

---

#### `.StopLoss(double price)`
Sets a fixed stop loss price. Submits a stop order after entry fill.

```csharp
.StopLoss(6.50)    // Sell if price drops to $6.50
```

**IdiotScript:** `StopLoss(6.50)` or `SL(6.50)`

**Behavior:**
- Submits stop order immediately after entry fills
- Cancels take profit order if stop loss fills first
- Order type: Stop (STP)

---

#### `.TrailingStopLoss(double percent)`
Enables a percentage-based trailing stop loss that follows price upward.

```csharp
.TrailingStopLoss(Percent.Ten)      // 10% trailing stop
.TrailingStopLoss(Percent.Five)     // 5% trailing stop
.TrailingStopLoss(0.08)             // 8% trailing stop
```

**IdiotScript:** `TrailingStopLoss(IS.TEN_PERCENT)` or `TSL(10)`

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

---

#### `.ExitStrategy(TimeOnly time)` / `.ExitStrategy(IS exitTime)`
Sets the time to force-close the position if still open. Use with `.IsProfitable()` to only exit if in profit.

```csharp
.ExitStrategy(IS.BELL)                 // Exit at session bell
.ExitStrategy(new TimeOnly(15, 30))    // Exit at 3:30 PM
```

**IdiotScript:** `ExitStrategy(IS.BELL)` or `ExitStrategy(15:30)`

**IS.BELL Session-Aware Behavior:**
`IS.BELL` resolves to 1 minute before the current session ends:
- **Premarket**: 9:29 AM (1 min before 9:30 open)
- **RTH**: 3:59 PM (1 min before 4:00 close)
- **AfterHours**: 7:59 PM (1 min before 8:00 AH end)
- **Default**: 3:59 PM (RTH bell if no session specified)

Explicit bell constants are also available:
- `IS.PREMARKET.BELL` → 9:29 AM
- `IS.RTH.BELL` → 3:59 PM
- `IS.AFTERHOURS.BELL` → 7:59 PM

---

#### `.IsProfitable()`
Chain after `.ExitStrategy()` to only exit if the position is profitable.

```csharp
.ExitStrategy(IS.BELL).IsProfitable()    // Exit at bell only if profitable
.ExitStrategy(new TimeOnly(15, 30)).IsProfitable()  // Exit at 3:30 PM only if profitable
```

**IdiotScript:** `ExitStrategy(IS.BELL).IsProfitable()` or `IsProfitable()`

---

#### `.ClosePosition(TimeOnly time)` *(Legacy)*
Sets the time to force-close the position if still open. Use `.ExitStrategy()` for new code.

```csharp
.ClosePosition(Time.PreMarket.End.AddMinutes(-10))    // Close at 9:20 AM ET
```

---

#### `.End(TimeOnly endTime)`
Sets the time to stop monitoring the strategy. Also builds and returns the strategy.

```csharp
.End(Time.PreMarket.End)    // Stop at 9:30 AM ET
```

**Returns:** `TradingStrategy` (terminal method)

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

## AdaptiveOrder - Smart Dynamic Order Management

AdaptiveOrder monitors market conditions in real-time and dynamically adjusts take profit and stop loss levels to maximize profit while managing risk.

### How It Works

```
╔═══════════════════════════════════════════════════════════════════════════╗
║  MARKET ANALYSIS                                                          ║
║                                                                           ║
║  The system continuously evaluates multiple indicators:                   ║
║                                                                           ║
║  1. VWAP Position (15%): Bullish above, bearish below                    ║
║  2. EMA Stack (20%): Short-term vs long-term trend alignment             ║
║  3. RSI (15%): Overbought/oversold for reversal risk                     ║
║  4. MACD (20%): Momentum direction and strength                          ║
║  5. ADX (20%): Trend strength (strong trends get wider targets)          ║
║  6. Volume (10%): Confirmation of price moves                            ║
║                                                                           ║
║  These are combined into a Market Score (-100 to +100)                   ║
║  Positive = bullish, Negative = bearish                                  ║
╚═══════════════════════════════════════════════════════════════════════════╝
```

### Adaptive Behavior

```
╔═══════════════════════════════════════════════════════════════════════════╗
║  SCENARIO               │  TAKE PROFIT         │  STOP LOSS              ║
║─────────────────────────┼──────────────────────┼─────────────────────────║
║  Strong bullish (70+)   │  Extend +50%         │  Tighten (protect gain) ║
║  Moderate bull (30-70)  │  Keep original       │  Keep original          ║
║  Neutral (-30 to 30)    │  Reduce 25%          │  Widen (allow bounce)   ║
║  Moderate bear (-70-30) │  Reduce 50%          │  Keep original          ║
║  Strong bearish (<-70)  │  EXIT IMMEDIATELY    │  N/A - Emergency exit   ║
╚═══════════════════════════════════════════════════════════════════════════╝
```

### Usage

```csharp
// Fluent API (Single-Responsibility Pattern)
Stock.Ticker("AAPL")
    .Entry(150)
    .IsAboveVwap()
    .Long()
    .Quantity(100)
    .TakeProfit(160)
    .StopLoss(145)
    .AdaptiveOrder(AdaptiveMode.Aggressive)
    .Build();
```

```idiotscript
// IdiotScript
Ticker(AAPL)
.Entry(150)
.IsAboveVwap()
.Long()
.Qty(100)
.TakeProfit(160)
.StopLoss(145)
.AdaptiveOrder(IS.AGGRESSIVE)
```

### Mode Configuration

```
╔═══════════════════════════════════════════════════════════════════════════════════╗
║  Setting                  │  CONSERVATIVE  │  BALANCED  │  AGGRESSIVE             ║
╠═══════════════════════════╪════════════════╪════════════╪═════════════════════════╣
║  MaxTakeProfitExtension   │     25%        │    50%     │     75%                 ║
║  MaxTakeProfitReduction   │     60%        │    50%     │     30%                 ║
║  MaxStopLossTighten       │     30%        │    50%     │     60%                 ║
║  MaxStopLossWiden         │     40%        │    25%     │     15%                 ║
║  EmergencyExitThreshold   │     -60        │    -70     │     -80                 ║
║  MinScoreChangeForAdjust  │     10         │    15      │     20                  ║
╚═══════════════════════════════════════════════════════════════════════════════════╝
```

**Conservative**: Protects gains quickly, allows wider stops to avoid noise, exits sooner on bearish signals.
**Balanced**: Standard risk/reward, moderate adjustments in both directions.
**Aggressive**: Lets winners run longer, tighter stops to protect capital, stays in longer during drawdowns.

### Adaptive TP Feedback Loop

The system extends TP when momentum is strong, then contracts it when momentum fades, allowing the price to eventually meet the target:

```
╔═══════════════════════════════════════════════════════════════════════════════╗
║  MOMENTUM STRONG (Price approaching TP fast)                                  ║
║  ├── MACD: Bullish with strong histogram → +70 to +100 score                 ║
║  ├── ADX: High (40+) with +DI > -DI → +80 to +100 score                      ║
║  ├── RSI: Not yet overbought (50-65) → +0 to +37 score                       ║
║  └── Result: Total Score 70-100 → TP EXTENDS (multiplier 1.0 to 1.75)        ║
╠═══════════════════════════════════════════════════════════════════════════════╣
║  MOMENTUM FADING (Price slowing near extended TP)                             ║
║  ├── MACD: Histogram shrinking → score drops                                 ║
║  ├── ADX: Still high but momentum slowing                                    ║
║  ├── RSI: Becoming overbought (70+) → NEGATIVE contribution (-10 to -100)    ║
║  └── Result: Total Score drops to 30-69 → TP RETURNS TO ORIGINAL             ║
╠═══════════════════════════════════════════════════════════════════════════════╣
║  MOMENTUM EXHAUSTED                                                           ║
║  ├── MACD: Bearish crossover imminent                                        ║
║  ├── RSI: Overbought (75+) → strong negative                                 ║
║  └── Result: Score drops to 0-29 → TP REDUCES (multiplier 0.85-0.925)        ║
║      → Price finally meets the lowered TP target → PROFIT TAKEN              ║
╚═══════════════════════════════════════════════════════════════════════════════╝
```

### Timeline Example: Dynamic TP Adjustment

```
Entry: $150  |  Original TP: $160  |  Profit Range: $10
Mode: AGGRESSIVE (75% max extension)

Timeline:
─────────────────────────────────────────────────────────────────────────────
T=0   Price $151, Score +85 (strong momentum)
      → TP Multiplier = 1.375 → TP extended to $163.75

T=5   Price $158, Score +90 (very strong, approaching original TP)
      → TP Multiplier = 1.50 → TP extended to $165.00
      → Price would have hit $160 but TP is now $165!

T=10  Price $162, Score +75 (RSI overbought, MACD histogram shrinking)
      → TP Multiplier = 1.125 → TP reduced to $161.25

T=15  Price $161, Score +50 (momentum fading)
      → TP Multiplier = 1.0 → TP back to $160.00

T=20  Price $160, Score +45
      → TP still at $160.00 → *** TAKE PROFIT FILLED ***
─────────────────────────────────────────────────────────────────────────────
Result: Captured $10 profit. System extended TP during strong momentum,
        then contracted it when momentum faded, allowing fill at optimal time.
```

### TP Extension Visualization

```
Price
  ↑
$165 ─ ─ ─ ─ ─ ─ ─ ─ Extended TP (score 90+)
      │            ╱
$163 ─│─ ─ ─ ─ ─ ╱─ ─ TP following momentum
      │        ╱
$161 ─│─ ─ ─ ╱─ ─ ─ ─ TP reducing as RSI overbought
      │    ╱    ↘
$160 ─│─ ╱─ ─ ─ ─ █ ← FILLED HERE (TP came down to meet price)
      │╱        ↗
$158 ─│─ ─ ─ ─ ─ Price path
      │      ╱
$155 ─│─ ─ ╱
      │  ╱
$150 ─█╱─ ─ ─ ─ ─ Entry
      │
      └──────────────────────────────────→ Time
         T=0  T=5  T=10  T=15  T=20
```

### RSI Overbought Detection (Key Mechanism)

The RSI component is crucial for detecting when momentum is exhausted:

```
RSI and Score Contribution:
+────────────────────────────────────────────────────────────────+
│  RSI Value  │  Score Contribution  │  Effect on TP            │
├─────────────┼──────────────────────┼──────────────────────────┤
│  30 or less │  +100 (oversold)     │  Extend (bullish bounce) │
│  40         │  +25                 │  Slight extend           │
│  50         │  0 (neutral)         │  No change               │
│  60         │  +25                 │  Slight extend           │
│  70         │  0 (threshold)       │  No change               │
│  75         │  -17                 │  Start reducing TP       │
│  80         │  -33                 │  Reduce TP               │
│  85         │  -50                 │  Significant reduction   │
│  90+        │  -67 to -100         │  Maximum reduction       │
+────────────────────────────────────────────────────────────────+

As price rapidly approaches TP:
  → RSI rises toward 70+ (overbought)
  → RSI score contribution becomes NEGATIVE
  → Total score drops
  → TP multiplier reduces
  → TP target comes down to meet the price
```

### Concrete Example: CIGL Adaptive Trade

```
Entry: $4.15  |  Original TP: $4.80  |  Original SL: $3.90
Profit Range: $0.65  |  Loss Range: $0.25
Mode: AGGRESSIVE

Market Score: +85 (Strong Bullish)
├── VWAP Score:   +80  (price 4% above VWAP)
├── EMA Score:    +100 (price above all EMAs)
├── RSI Score:    -30  (RSI at 75, overbought caution)
├── MACD Score:   +90  (bullish with strong histogram)
├── ADX Score:    +100 (ADX 63 with +DI > -DI)
└── Volume Score: +60  (1.6x average volume)

TP Multiplier = 1.0 + (0.75 × (85-70)/30) = 1.375
SL Multiplier = 1.0 + (0.60 × (85-70)/30) = 1.30

Adjusted TP: $4.15 + ($0.65 × 1.375) = $5.04  (+21% from entry)
Adjusted SL: $4.15 - ($0.25 / 1.30) = $3.96   (tighter protection)
```

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
│  Order Methods (Long/Short with chained config):                    │
│    Quantity = required (must be set)                                │
│    PriceType = Price.Current                                        │
│    OrderType = OrderType.Limit                                      │
│    OutsideRTH = true                                                │
│    TakeProfitOutsideRTH = true                                      │
├─────────────────────────────────────────────────────────────────────┤
│  Strategy Builder (after Long/Short):                               │
│    TimeInForce = GTC     AllOrNone = false                          │
│    TakeProfit = null     StopLoss = null      TrailingStop = off    │
│    ExitStrategy = null   IsProfitable = false                       │
│    AdaptiveOrder = null (disabled)                                  │
├─────────────────────────────────────────────────────────────────────┤
│  Condition Defaults:                                                │
│    IsAboveVwap/IsBelowVwap buffer = 0                               │
│    IsRsi threshold = null (70/30)                                   │
│    IsAdx threshold = 25                                             │
│    IsDI minDifference = 0                                           │
│    IsHigherLows lookbackBars = 3                                    │
├─────────────────────────────────────────────────────────────────────┤
│  ADX TakeProfit Defaults:                                           │
│    WeakThreshold = 15    DevelopingThreshold = 25                   │
│    StrongThreshold = 35  ExitOnRollover = true                      │
├─────────────────────────────────────────────────────────────────────┤
│  ATR StopLoss Defaults:                                             │
│    Period = 14           IsTrailing = true                          │
│    MinStopPercent = 1%   MaxStopPercent = 25%                       │
├─────────────────────────────────────────────────────────────────────┤
│  AdaptiveOrder Mode Defaults:                                       │
│    Mode = Balanced       MaxTPExtension = 50%                       │
│    MaxTPReduction = 50%  EmergencyExitThreshold = -70               │
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

The `PriceType` setting determines what price is used when executing an order. This is set using `.PriceType()` after `.Long()` or `.Short()`.

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `Price.Current` | `PriceType(IS.CURRENT)` | Execute at current market price **(default)** |
| `Price.VWAP` | `PriceType(IS.VWAP)` | Execute at Volume-Weighted Average Price |
| `Price.Bid` | `PriceType(IS.BID)` | Execute at bid price (highest buyer price) |
| `Price.Ask` | `PriceType(IS.ASK)` | Execute at ask price (lowest seller price) |

**Detailed Reference:**

```
+------------------------------------------------------------------------+
¦  PRICE TYPE REFERENCE                                                  ¦
+------------------------------------------------------------------------¦
¦                                                                        ¦
¦  CURRENT (Default)                                                     ¦
¦  ─────────────────                                                     ¦
¦  Best for:  Fast execution when you need to enter/exit immediately    ¦
¦  Risk:      May experience slippage in fast-moving or illiquid mkts   ¦
¦  Use case:  Breakout entries, stop loss exits, time-sensitive trades  ¦
¦                                                                        ¦
¦  VWAP                                                                  ¦
¦  ────                                                                  ¦
¦  Best for:  Getting a fair average price over the session             ¦
¦  Risk:      May not fill if price moves away from VWAP                ¦
¦  Note:      VWAP resets at market open each day                       ¦
¦  Warning:   Unreliable in pre-market/after-hours (low volume)         ¦
¦  Use case:  Mean-reversion strategies, institutional-style entries    ¦
¦                                                                        ¦
¦  BID                                                                   ¦
¦  ───                                                                   ¦
¦  Best for:  Selling with limit order at current bid                   ¦
¦  Risk:      May not fill if bid drops before execution                ¦
¦  Use case:  Exiting long positions at best available buyer price      ¦
¦                                                                        ¦
¦  ASK                                                                   ¦
¦  ───                                                                   ¦
¦  Best for:  Buying with limit order at current ask                    ¦
¦  Risk:      May not fill if ask rises before execution                ¦
¦  Use case:  Entering long positions at best available seller price    ¦
¦                                                                        ¦
+------------------------------------------------------------------------+
```

**Usage Examples:**

```csharp
// Fluent API - Execute at current market price (default)
.Long()
.Quantity(100)
// PriceType defaults to Price.Current

// Fluent API - Execute at VWAP
.Long()
.Quantity(100)
.PriceType(Price.VWAP)

// Fluent API - Execute at Ask (for buying)
.Long()
.Quantity(100)
.PriceType(Price.Ask)
```

```idiotscript
# IdiotScript - Execute at VWAP
.Long()
.Qty(100)
.PriceType(IS.VWAP)

# IdiotScript - Execute at current price (explicit)
.Long()
.Qty(100)
.PriceType(IS.CURRENT)
```

**Best Practices:**

| Scenario | Recommended PriceType | Reason |
|----------|----------------------|--------|
| Breakout entry | `Current` | Speed matters; get in before momentum fades |
| Pre-market/After-hours | `Current` | VWAP unreliable with low volume |
| Mean reversion | `VWAP` | Fair price entry for counter-trend trades |
| Limit order selling | `Bid` | Sell at what buyers are willing to pay |
| Limit order buying | `Ask` | Buy at what sellers are offering |
| Stop loss exit | `Current` | Exit immediately regardless of price |

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
    .Long()
    .Quantity(100)
    .TakeProfit(9.00)
    .StopLoss(6.50)
    .Build();
```

**IdiotScript:**
```idiotscript
Ticker(NAMM)
.Session(IS.PREMARKET.END_EARLY)
.Breakout(7.10)
.Pullback(6.80)
.IsAboveVwap()
.Long()
.Qty(100)
.TakeProfit(9.00)
.StopLoss(6.50)
```

### ADX-Based Dynamic Take Profit

```csharp
Stock
    .Ticker("VIVS")
    .SessionDuration(TradingSession.PreMarketEndEarly)
    .IsPriceAbove(2.40)
    .IsAboveVwap()
    .Long()
    .Quantity(208)
    .TakeProfit(4.00, 4.80)    // ADX-based: $4.00 (weak) to $4.80 (strong)
    .ExitStrategy(IS.BELL)
    .Build();
```

### ATR-Based Volatility-Adaptive Stop Loss

```csharp
Stock
    .Ticker("AAPL")
    .SessionDuration(TradingSession.PreMarketEndEarly)
    .IsPriceAbove(150.00)
    .IsAboveVwap()
    .Long()
    .Quantity(100)
    .TakeProfit(160.00, 175.00)
    .TrailingStopLoss(Atr.Balanced)    // 2.0× ATR trailing stop
    .ExitStrategy(IS.BELL)
    .Build();
```

### Short Position Strategy

```csharp
Stock
    .Ticker("TSLA")
    .SessionDuration(TradingSession.RTH)
    .IsVwapRejection()           // Bearish signal
    .IsMacdBearish()
    .IsDiNegative()
    .Short()                     // Open SHORT position
    .Quantity(50)
    .TakeProfit(240.00)          // Target below entry
    .StopLoss(260.00)            // Stop above entry
    .Build();
```

**IdiotScript:**
```idiotscript
Ticker(TSLA)
.Session(IS.RTH)
.IsVwapRejection()
.IsMacdBearish()
.IsDiNegative()
.Short()
.Qty(50)
.TakeProfit(240.00)
.StopLoss(260.00)
```

### ATR Stop with Custom Multiplier

```csharp
Stock
    .Ticker("TSLA")
    .SessionDuration(TradingSession.RTH)
    .Breakout(250.00)
    .Pullback(245.00)
    .IsAboveVwap()
    .Long()
    .Quantity(50)
    .TakeProfit(270.00)
    .TrailingStopLoss(Atr.Multiplier(2.5))    // Custom 2.5× ATR
    .Build();
```

### ATR Stop with Min/Max Bounds

```csharp
Stock
    .Ticker("NVDA")
    .SessionDuration(TradingSession.PreMarket)
    .IsPriceAbove(500.00)
    .IsAboveVwap()
    .Long()
    .Quantity(20)
    .TakeProfit(550.00)
    .TrailingStopLoss(Atr.WithBounds(
        multiplier: 2.0,
        minStopPercent: 0.02,    // At least 2% ($10 on $500)
        maxStopPercent: 0.10     // At most 10% ($50 on $500)
    ))
    .Build();
```

### Full-Featured Strategy with AdaptiveOrder

```csharp
Stock
    .Ticker("AAPL")
    .SessionDuration(TradingSession.PreMarket)
    .Breakout(150.00)
    .Pullback(148.00)
    .IsAboveVwap()
    .IsEmaAbove(9)
    .IsDiPositive()
    .Long()
    .Quantity(200)
    .PriceType(Price.VWAP)
    .TakeProfit(155.00)
    .TrailingStopLoss(Percent.Five)
    .AdaptiveOrder(AdaptiveMode.Aggressive)   // Smart TP/SL adjustment
    .TimeInForce(TIF.GTC)
    .OutsideRTH()
    .TakeProfitOutsideRTH()
    .ExitStrategy(IS.BELL)
    .IsProfitable()
    .Build();
```

**IdiotScript:**
```idiotscript
Ticker(AAPL)
.Session(IS.PREMARKET)
.Breakout(150.00)
.Pullback(148.00)
.IsAboveVwap()
.IsEmaAbove(9)
.IsDiPositive()
.Long()
.Qty(200)
.PriceType(IS.VWAP)
.TakeProfit(155.00)
.TrailingStopLoss(IS.FIVE_PERCENT)
.AdaptiveOrder(IS.AGGRESSIVE)
.TimeInForce(IS.GTC)
.OutsideRTH()
.TakeProfitOutsideRTH()
.ExitStrategy(IS.BELL).IsProfitable()
```

### OTC/Pink Sheet Strategy

```csharp
Stock
    .Ticker("RPGL")
    .Exchange(ContractExchange.Pink)    // Pink Sheets routing
    .SessionDuration(TradingSession.PreMarketEndEarly)
    .IsPriceAbove(0.88)
    .IsAboveVwap()
    .Long()
    .Quantity(568)
    .TakeProfit(1.30, 1.70)
    .Build();
```

### Closing a Position Strategy

```csharp
Stock
    .Ticker("AAPL")
    .IsPriceAbove(155)                    // When price hits target
    .CloseLong()                          // Sell to close long position
    .TimeInForce(TIF.GTC)
    .OutsideRTH()
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
    .Long()
    .Quantity(500)
    .TakeProfit(6.00)
    .StopLoss(4.25)
    .AllOrNone()                    // Must fill all 500 shares
    .Build();
```

### Disabled Strategy (for reference)

```csharp
Stock
    .Ticker("DISABLED")
    .Enabled(false)              // Won't execute
    .Breakout(10.00)
    .Long()
    .Quantity(100)
    .TakeProfit(12.00)
    .Build();
```

---

## Command Implementation Details

This section provides detailed implementation breakdowns for each IdiotScript command, showing which components are required and where they are located.

### Implementation Checklist

For a command to be **fully implemented**, it must have ALL of these components:

| Component | Description | Required For |
|-----------|-------------|--------------|
| **Condition class** | The actual condition logic (e.g., `PullbackCondition`) | All conditions |
| **Stock method** | Fluent API method (e.g., `Stock.Pullback()`) | All conditions |
| **Parser pattern** | Regex or matching logic in `IdiotScriptParser.cs` | IdiotScript support |
| **Calculator class** | For indicator-based conditions (EMA, RSI, etc.) | Indicator conditions |
| **StrategyRunner wire-up** | Callback assignment in `InitializeIndicatorCalculators()` | Indicator conditions |
| **ValidCommands entry** | Listed in `IdiotScriptValidator.ValidCommands` | Validation |
| **Segment conversion** | Converts parsed condition to `StrategySegment` | Frontend support |

### Exceptions to the Checklist

- **VWAP conditions** (`IsAboveVwap`, `IsBelowVwap`, etc.) don't need calculators - VWAP is passed directly to `Evaluate(price, vwap)`
- **Price conditions** (`Entry`, `Breakout`, `Pullback`, `IsPriceAbove`) don't need calculators - they use the price parameter directly

---

### Pullback() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | ✅ | `PriceConditions.cs` - `PullbackCondition` |
| Stock.Pullback() method | ✅ | `Stock.cs` |
| Parser regex | ✅ | `IdiotScriptParser.cs` - `PULLBACK(?:\((\$?[\d.]*)\))?` |
| Segment conversion | ✅ | `IdiotScriptParser.cs` - `SegmentType.Pullback` |
| State machine runner | ✅ | `PullbackRunner.cs` |
| Validator | ✅ | `ValidCommands` includes `"PULLBACK"` |

**Evaluation:** `currentPrice <= Level`

**State Machine (PullbackRunner):**
1. **WaitingForBreakout** - Wait for price >= BreakoutLevel
2. **WaitingForPullback** - Wait for price <= PullbackLevel  
3. **WaitingForVwapReclaim** - Wait for price >= VWAP + buffer
4. **OrderSubmitted/Done** - Entry filled

---

### IsAboveVwap() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | ✅ | `PriceConditions.cs` - `AboveVwapCondition` |
| Stock.IsAboveVwap() method | ✅ | `Stock.cs` |
| Parser matching | ✅ | `IdiotScriptParser.cs` - handles `ABOVEVWAP`, `VWAP`, `ISABOVEVWAP` |
| Validator | ✅ | `ValidCommands` includes `"ABOVEVWAP"`, `"ISABOVEVWAP"` |

**Evaluation:** `vwap > 0 && currentPrice >= (vwap + Buffer)`

**Note:** VWAP-based condition - doesn't need a calculator or warm-up period. VWAP is passed directly to `Evaluate(price, vwap)` by the `StrategyRunner`.

---

### IsCloseAboveVwap() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | ✅ | `IndicatorConditions.cs` - `CloseAboveVwapCondition` |
| Stock.IsCloseAboveVwap() method | ✅ | `Stock.cs` |
| Parser regex | ✅ | `IdiotScriptParser.cs` - `^(?:IS)?CLOSEABOVEVWAP(?:\(\))?$` |
| StrategyRunner wire-up | ✅ | `StrategyRunner.cs` - `GetLastClose` callback to `CandlestickAggregator` |
| Validator | ✅ | `ValidCommands` includes `"CLOSEABOVEVWAP"`, `"ISCLOSEABOVEVWAP"` |

**Evaluation:** `lastClose > vwap` (uses completed candle close, not current price)

**Difference from IsAboveVwap():**
- `IsAboveVwap()` - Current tick price >= VWAP (can trigger on wicks)
- `IsCloseAboveVwap()` - Last **completed candle close** > VWAP (stronger confirmation)

**Supported Syntax:**
```idiotscript
.IsCloseAboveVwap()    # Candle closed above VWAP
.CloseAboveVwap()      # Alias
```

**Use Case:** More reliable VWAP reclaim signal - confirms institutional buying, not just a fake wick breakout.

---

### Long() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Fluent API method | ✅ | `Stock.cs` - `Long() => Order(OrderDirection.Long)` |
| Order() method | ✅ | `Stock.cs` - creates `StrategyBuilder` |
| Parser matching | ✅ | `IdiotScriptParser.cs` - handles `LONG`, `LONG()`, `IS.LONG` |
| Validator | ✅ | `ValidCommands` includes `"LONG"` |

**Equivalent Forms:**
```idiotscript
.Long()
.Order(IS.LONG)
.Order()           # defaults to LONG
```

---

### TakeProfit() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Fluent API method | ✅ | `Stock.cs` - `TakeProfit(double price)` |
| Parser regex | ✅ | `IdiotScriptParser.cs` - `^(?:TP\|TAKEPROFIT)\((\$?[\d.]+)\)$` |
| Parser method | ✅ | `IdiotScriptParser.cs` - `TryParseTakeProfit()` |
| StrategyRunner tracking | ✅ | `StrategyRunner.cs` - `_takeProfitOrderId`, `_takeProfitFilled` |
| Order submission | ✅ | `StrategyRunner` submits TP limit orders after entry fills |
| AdaptiveOrder integration | ✅ | Dynamic TP adjustment with `_currentAdaptiveTakeProfitPrice` |
| Validator | ✅ | `ValidCommands` includes `"TP"`, `"TAKEPROFIT"` |

**Supported Syntax:**
```idiotscript
.TakeProfit(160)      # Fixed price
.TP(160)              # Alias
.TP($160)             # With dollar sign
```

---

### StopLoss() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Fluent API method | ✅ | `Stock.cs` - `StopLoss(double price)` |
| Parser regex | ✅ | `IdiotScriptParser.cs` - `^(?:SL\|STOPLOSS)\((\$?[\d.]+)\)$` |
| Parser method | ✅ | `IdiotScriptParser.cs` - `TryParseStopLoss()` |
| StrategyRunner tracking | ✅ | `StrategyRunner.cs` - `_stopLossOrderId`, `_stopLossFilled` |
| Order submission | ✅ | `StrategyRunner` submits SL stop orders after entry fills |
| AdaptiveOrder integration | ✅ | Dynamic SL adjustment with `_currentAdaptiveStopLossPrice` |
| Validator | ✅ | `ValidCommands` includes `"SL"`, `"STOPLOSS"` |

**Supported Syntax:**
```idiotscript
.StopLoss(145)        # Fixed price
.SL(145)              # Alias
.SL($145)             # With dollar sign
```

**Related Commands:**
- `TrailingStopLoss(percent)` - Percentage-based trailing stop (e.g., `.TSL(5)` for 5%)
- `TrailingStopLoss(IS.ATR)` - ATR-based trailing stop with volatility adjustment

---

### IsProfitable() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Fluent API method | ✅ | `Stock.cs` - `IsProfitable()` |
| Parser regex | ✅ | `IdiotScriptParser.cs` - `^(?:IS)?PROFITABLE(?:\(\))?$` |
| Parser method | ✅ | `IdiotScriptParser.cs` - `TryParseIsProfitable()` |
| OrderAction property | ✅ | `OrderAction.cs` - `ClosePositionOnlyIfProfitable` |
| StrategyRunner logic | ✅ | `StrategyRunner.cs` - checks `isProfitable` before closing |
| Validator | ✅ | `ValidCommands` includes `"PROFITABLE"`, `"ISPROFITABLE"` |

**Usage:** Chain with `ExitStrategy()` to only exit if the position is profitable at the specified time.

**Supported Syntax:**
```idiotscript
.ExitStrategy(IS.BELL).IsProfitable()    # Exit at bell only if profitable
.ExitStrategy(15:30).IsProfitable()       # Exit at 3:30 PM only if profitable
```

**How it works:**
1. At the scheduled exit time, checks if `currentPrice > entryPrice` (for longs)
2. If profitable → closes position
3. If not profitable → holds position (logs "Position NOT profitable")

---

### Qty() / Quantity() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Fluent API method | ✅ | `Stock.cs` - `Quantity(int)` and `Qty(int)` alias |
| Parser regex | ✅ | `IdiotScriptParser.cs` - `^(?:QTY\|QUANTITY)\((\d+)\)$` |
| Validator | ✅ | `ValidCommands` includes `"QTY"`, `"QUANTITY"` |

**Note:** Both `Qty()` and `Quantity()` are aliases that do the same thing. Use whichever you prefer.

---

### IsEmaAbove() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | ✅ | `IndicatorConditions.cs` - `EmaAboveCondition` |
| Stock.IsEmaAbove() method | ✅ | `Stock.cs` |
| Parser regex | ✅ | `IdiotScriptParser.cs` - `^(?:IS)?EMAABOVE\((\d+)\)$` |
| Calculator class | ✅ | `EmaCalculator.cs` |
| StrategyRunner wire-up | ✅ | `GetOrCreateEmaCalculator()` - `GetEmaValue` callback |
| Validator | ✅ | `ValidCommands` includes `"EMAABOVE"`, `"ISEMAABOVE"` |

**Evaluation:** `currentPrice >= emaValue`

**Supported Syntax:**
```idiotscript
.IsEmaAbove(9)         # Price above EMA(9)
.IsEmaAbove(21)        # Price above EMA(21)
.IsEmaAbove(200)       # Price above EMA(200)
.EmaAbove(9)           # Alias
```

**Warm-up Required:** EMA(N) needs N bars to calculate properly. See [Indicator Warm-Up Requirements](#indicator-warm-up-requirements).

**Related EMA Conditions:**
- `IsEmaBelow(period)` - Price below EMA
- `IsEmaBetween(short, long)` - Price between two EMAs (pullback zone)
- `IsEmaTurningUp(period)` - EMA slope turning positive

---

### IsMacdBullish() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | ✅ | `IndicatorConditions.cs` - `MacdCondition` |
| Stock.IsMacdBullish() method | ✅ | `Stock.cs` - alias for `IsMacd(MacdState.Bullish)` |
| Parser regex | ✅ | `IdiotScriptParser.cs` - `^(?:IS)?MACDBULLISH(?:\(\))?$` |
| Calculator class | ✅ | `MacdCalculator.cs` |
| StrategyRunner wire-up | ✅ | `GetMacdValues` callback returns `(MacdLine, SignalLine, Histogram, PreviousHistogram)` |
| Validator | ✅ | `ValidCommands` includes `"MACDBULLISH"`, `"ISMACDBULLISH"` |

**Evaluation:** `macdLine > signalLine`

**Supported Syntax:**
```idiotscript
.IsMacdBullish()       # MACD line above Signal line
.MacdBullish()         # Alias
```

**Warm-up Required:** MACD(12,26,9) needs 35 bars to calculate properly.

**Related MACD Conditions:**
- `IsMacdBearish()` - MACD line below Signal line
- `IsMacd(MacdState.AboveZero)` - MACD line above zero (uptrend)
- `IsMacd(MacdState.HistogramRising)` - Histogram increasing

---

### IsMomentumAbove() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | ✅ | `IndicatorConditions.cs` - `MomentumAboveCondition` |
| Stock.IsMomentumAbove() method | ✅ | `Stock.cs` |
| Parser regex | ✅ | `IdiotScriptParser.cs` - `^(?:IS)?MOMENTUMABOVE\((-?\d+(?:\.\d+)?)\)$` |
| Calculator class | ✅ | `MomentumCalculator.cs` |
| StrategyRunner wire-up | ✅ | `GetMomentumValue` callback to `MomentumCalculator.CurrentValue` |
| Validator | ✅ | `ValidCommands` includes `"MOMENTUMABOVE"`, `"ISMOMENTUMABOVE"` |

**Evaluation:** `momentum >= threshold`

**Supported Syntax:**
```idiotscript
.IsMomentumAbove(0)    # Positive momentum (most common)
.IsMomentumAbove(5)    # Strong positive momentum
.MomentumAbove(0)      # Alias
```

**Warm-up Required:** Momentum(10) needs 11 bars to calculate properly.

**Related Momentum Conditions:**
- `IsMomentumBelow(threshold)` - Momentum below threshold (bearish)

---

### AdaptiveOrder() Implementation

**Status:** ✅ Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Config class | ✅ | `AdaptiveOrderConfig.cs` |
| Stock.AdaptiveOrder() method | ✅ | `Stock.cs` - accepts mode string or `AdaptiveOrderConfig` |
| Parser regex | ✅ | `IdiotScriptParser.cs` - `^(?:IS)?ADAPTIVEORDER(?:\(([A-Za-z0-9_.]*)\))?$` |
| Parser method | ✅ | `IdiotScriptParser.cs` - `TryParseAdaptiveOrder()` |
| StrategyRunner logic | ✅ | `MonitorAdaptiveOrder()` - calculates market score, adjusts TP/SL |
| Score calculation | ✅ | `CalculateMarketScore()` - uses VWAP, EMA, RSI, MACD, ADX, Volume |
| TP/SL multipliers | ✅ | `CalculateTakeProfitMultiplier()`, `CalculateStopLossMultiplier()` |
| Validator | ✅ | `ValidCommands` includes `"ADAPTIVEORDER"`, `"ISADAPTIVEORDER"` |

**Supported Syntax:**
```idiotscript
.AdaptiveOrder()                   # Balanced mode (default)
.AdaptiveOrder(IS.BALANCED)        # Balanced mode (explicit)
.AdaptiveOrder(IS.CONSERVATIVE)    # Protect gains, quick to take profits
.AdaptiveOrder(IS.AGGRESSIVE)      # Maximize profit potential in strong trends
.IsAdaptiveOrder()                 # Alias for AdaptiveOrder()
```

**How it works:**
1. Monitors market conditions in real-time after entry
2. Calculates a Market Score (-100 to +100) using multiple indicators
3. Dynamically adjusts TakeProfit and StopLoss based on score
4. Emergency exits if score drops below threshold (mode-dependent)

**Mode Settings:**
| Setting | Conservative | Balanced | Aggressive |
|---------|--------------|----------|------------|
| MaxTakeProfitExtension | 25% | 50% | 75% |
| MaxTakeProfitReduction | 60% | 50% | 30% |
| MaxStopLossTighten | 30% | 50% | 60% |
| MaxStopLossWiden | 40% | 25% | 15% |
| EmergencyExitThreshold | -60 | -70 | -80 |

**Note:** AdaptiveOrder requires `TakeProfit()` and/or `StopLoss()` to be set. It modifies these values dynamically but needs starting points.

See [AdaptiveOrder - Smart Dynamic Order Management](#adaptiveorder---smart-dynamic-order-management) for detailed documentation.

---

### IsHigherLows()

✅ **Fully Implemented**

| Component | Location | Details |
|-----------|----------|---------|
| Condition Class | `IndicatorConditions.cs` | `HigherLowsCondition` |
| Stock Method | `Stock.cs` | `IsHigherLows(int lookbackBars = 3)` |
| Parser Regex | `IdiotScriptParser.cs` | `^(?:IS)?HIGHERLOWS(?:\((\d*)\))?$` |
| Calculator | Not needed | Uses `CandlestickAggregator.GetRecentLows()` |
| StrategyRunner Wire-up | `StrategyRunner.cs` | `GetRecentLows` callback |
| Validator | `IdiotScriptValidator.cs` | `"HIGHERLOWS"`, `"ISHIGHERLOWS"` |
| Segment Conversion | `SegmentFactory.cs` | Returns `ConditionType.IndicatorBased` |

**Usage:**
```idiotscript
.IsHigherLows()       # Default: 3 consecutive higher lows
.IsHigherLows(4)      # Custom: 4 consecutive higher lows
```

**Evaluation Logic:**
```
Checks if recent candle lows form ascending pattern (bullish):
Low[N] > Low[N-1] > Low[N-2] > ...

Example (lookbackBars=3):
Candle 1: Low = $150
Candle 2: Low = $151 (higher)
Candle 3: Low = $152 (higher) → ✓ IsHigherLows() = TRUE
```

**Visual:**
```
                         /\
             /\         /  \
   /\       /  \       /    \
  /  \     /    \     /      \
 /    \___/  ↑   \___/        \
       Higher    Higher
         Low       Low
```

---

### Indicator Condition Implementation Pattern

All indicator conditions follow the same pattern. Here's `IsMomentumAbove()` as an example:

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | ✅ | `IndicatorConditions.cs` - `MomentumAboveCondition` |
| Stock method | ✅ | `Stock.cs` - `IsMomentumAbove(double threshold)` |
| Calculator class | ✅ | `MomentumCalculator.cs` |
| StrategyRunner wire-up | ✅ | `InitializeIndicatorCalculators()` - `GetMomentumValue` callback |
| Parser matching | ✅ | `IdiotScriptParser.cs` |
| Validator | ✅ | `ValidCommands` includes `"MOMENTUMABOVE"`, `"ISMOMENTUMABOVE"` |

**Required Callback Wire-up in StrategyRunner:**
```csharp
case MomentumAboveCondition momentumAbove:
    momentumAbove.GetMomentumValue = () => _momentumCalculator?.Value ?? 0;
    break;
```

---

## Implementation Status

### ✅ Fully Implemented

| Feature | Builder | StrategyRunner | IdiotScript |
|---------|---------|----------------|-------------|
| **Price Conditions** |
| Breakout condition | ✅ | ✅ | `Breakout()`, `Entry()` |
| Pullback condition | ✅ | ✅ | `Pullback()` |
| PriceAbove condition | ✅ | ✅ | `Entry()`, `IsPriceAbove()` |
| PriceBelow condition | ✅ | ✅ | `IsPriceBelow()` |
| Custom condition (When) | ✅ | ✅ | N/A |
| **VWAP Conditions** |
| AboveVwap condition | ✅ | ✅ | `IsAboveVwap()` |
| BelowVwap condition | ✅ | ✅ | `IsBelowVwap()` |
| CloseAboveVwap condition | ✅ | ✅ | `IsCloseAboveVwap()` |
| VwapRejection condition | ✅ | ✅ | `IsVwapRejection()` |
| **EMA Conditions** |
| EmaAbove condition | ✅ | ✅ | `IsEmaAbove()` |
| EmaBelow condition | ✅ | ✅ | `IsEmaBelow()` |
| EmaBetween condition | ✅ | ✅ | `IsEmaBetween()` |
| EmaTurningUp condition | ✅ | ✅ | `IsEmaTurningUp()` |
| **Momentum Conditions** |
| MomentumAbove condition | ✅ | ✅ | `IsMomentumAbove()` |
| MomentumBelow condition | ✅ | ✅ | `IsMomentumBelow()` |
| RocAbove condition | ✅ | ✅ | `IsRocAbove()` |
| RocBelow condition | ✅ | ✅ | `IsRocBelow()` |
| **Trend Indicators** |
| RSI condition | ✅ | ✅ | `IsRsiOversold()`, `IsRsiOverbought()` |
| ADX condition | ✅ | ✅ | `IsAdxAbove()` |
| DI condition | ✅ | ✅ | `IsDiPositive()`, `IsDiNegative()` |
| MACD condition | ✅ | ✅ | `IsMacdBullish()`, `IsMacdBearish()` |
| **Pattern Conditions** |
| HigherLows condition | ✅ | ✅ | `IsHigherLows()` |
| VolumeAbove condition | ✅ | ✅ | `IsVolumeAbove()` |
| **Gap Conditions** |
| GapUp condition | ✅ | ✅ | `IsGapUp()` |
| GapDown condition | ✅ | ✅ | `IsGapDown()` |
| **Orders** |
| Long order | ✅ | ✅ | `Long()`, `Order(IS.LONG)` |
| Short order | ✅ | ✅ | `Short()`, `Order(IS.SHORT)` |
| Close/CloseLong/CloseShort | ✅ | ✅ | `CloseLong()`, `CloseShort()` |
| Quantity | ✅ | ✅ | `Qty()`, `Quantity()` |
| **Exit Strategies** |
| Take profit (fixed) | ✅ | ✅ | `TakeProfit()`, `TP()` |
| Take profit (ADX-based) | ✅ | ✅ | `TakeProfitRange()` |
| Stop loss | ✅ | ✅ | `StopLoss()`, `SL()` |
| Trailing stop loss (%) | ✅ | ✅ | `TrailingStopLoss()`, `TSL()` |
| Trailing stop loss (ATR) | ✅ | ✅ | `TrailingStopLoss(IS.ATR)` |
| ExitStrategy time | ✅ | ✅ | `ExitStrategy(IS.BELL)` |
| IsProfitable | ✅ | ✅ | `.IsProfitable()` |
| **Smart Order Management** |
| AdaptiveOrder | ✅ | ✅ | `AdaptiveOrder()`, `IsAdaptiveOrder()` |
| **Order Configuration** |
| TimeInForce | ✅ | ✅ | `TimeInForce()` |
| OutsideRTH | ✅ | ✅ | `OutsideRTH()` |
| TakeProfitOutsideRTH | ✅ | ✅ | `TakeProfitOutsideRTH()` |
| OrderType | ✅ | ✅ | `OrderType()` |
| PriceType | ✅ | ✅ | `PriceType()` |
| AllOrNone | ✅ | ✅ | `AllOrNone()` |
| Repeat | ✅ | ✅ | `Repeat()` |
| Enabled/Disabled | ✅ | ✅ | `Enabled()` |
| **Infrastructure** |
| SessionDuration | ✅ | ✅ | `Session()` |
| TradingSession enum | ✅ | ✅ | `IS.PREMARKET`, etc. |
| Exchange (SMART/Pink) | ✅ | ✅ | N/A |
| VWAP calculation | N/A | ✅ | N/A |
| ATR calculation | N/A | ✅ | N/A |
| Historical warm-up | N/A | ✅ | N/A |
| **Offline Backtesting** | ✅ | N/A | N/A |

### Indicator Calculator Status

| Calculator | File | Warm-up Bars | Status |
|------------|------|--------------|--------|
| EmaCalculator | `EmaCalculator.cs` | N bars | ✅ |
| AdxCalculator | `AdxCalculator.cs` | 28 bars | ✅ |
| RsiCalculator | `RsiCalculator.cs` | 15 bars | ✅ |
| MacdCalculator | `MacdCalculator.cs` | 35 bars | ✅ |
| MomentumCalculator | `MomentumCalculator.cs` | 11 bars | ✅ |
| RocCalculator | `RocCalculator.cs` | 11 bars | ✅ |
| VolumeCalculator | `VolumeCalculator.cs` | 20 bars | ✅ |
| AtrCalculator | `AtrCalculator.cs` | 14 bars | ✅ |
| CandlestickAggregator | `CandlestickAggregator.cs` | 1 bar | ✅ |

---

## Backtesting

The backtester allows you to test strategies against historical data **without** requiring an IB Gateway connection.

### Quick Start

```csharp
using IdiotProof.UnitTests;

// Define your strategy with modern syntax
var strategy = Stock.Ticker("AAPL")
    .Breakout(150)
    .Long()
    .Quantity(100)
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
