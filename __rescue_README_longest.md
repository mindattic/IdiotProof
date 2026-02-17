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
- [AutonomousTrading - AI-Driven Entry/Exit](#autonomoustrading---ai-driven-entryexit-decisions)
  - [Quick Start](#quick-start)
  - [How AutonomousTrading Works](#how-autonomoustrading-works)
  - [Mode Configuration](#mode-configuration-1)
  - [AutonomousTrading vs AdaptiveOrder](#autonomoustrading-vs-adaptiveorder)
  - [Real-World Example: UBER](#real-world-example-uber-chart-analysis)
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
- Multi-step condition chains (breakout ΓåÆ pullback ΓåÆ VWAP confirmation)
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
Γö£ΓöÇΓöÇ IdiotProof.csproj              # Main backend service (console app)
Γö£ΓöÇΓöÇ IdiotProof.Frontend/           # MAUI Blazor Hybrid Strategy Builder UI
Γöé   Γö£ΓöÇΓöÇ Components/
Γöé   Γöé   Γö£ΓöÇΓöÇ Layout/                # MainLayout with tab navigation
Γöé   Γöé   ΓööΓöÇΓöÇ Pages/                 # Design.razor, Strategies.razor, Settings.razor
Γöé   Γö£ΓöÇΓöÇ Services/
Γöé   Γöé   Γö£ΓöÇΓöÇ IStrategyService.cs    # Strategy persistence interface
Γöé   Γöé   Γö£ΓöÇΓöÇ StrategyService.cs     # Individual JSON file management
Γöé   Γöé   Γö£ΓöÇΓöÇ IBackendService.cs     # Backend communication interface
Γöé   Γöé   ΓööΓöÇΓöÇ BackendService.cs      # Backend IPC (placeholder)
Γöé   Γö£ΓöÇΓöÇ wwwroot/
Γöé   Γöé   Γö£ΓöÇΓöÇ css/app.css            # Dark theme styling
Γöé   Γöé   ΓööΓöÇΓöÇ index.html             # Blazor WebView host
Γöé   ΓööΓöÇΓöÇ MainPage.xaml              # MAUI page hosting BlazorWebView
Γö£ΓöÇΓöÇ IdiotProof.Shared/             # Shared models between frontend and backend
Γöé   Γö£ΓöÇΓöÇ Models/
Γöé   Γöé   Γö£ΓöÇΓöÇ StrategyDefinition.cs  # Complete strategy container
Γöé   Γöé   Γö£ΓöÇΓöÇ StrategySegment.cs     # Single segment in a strategy chain
Γöé   Γöé   Γö£ΓöÇΓöÇ SegmentParameter.cs    # Parameter definition for a segment
Γöé   Γöé   ΓööΓöÇΓöÇ SegmentFactory.cs      # Creates segment templates
Γöé   Γö£ΓöÇΓöÇ Enums/
Γöé   Γöé   Γö£ΓöÇΓöÇ SegmentType.cs         # Segment types (Breakout, Buy, etc.)
Γöé   Γöé   ΓööΓöÇΓöÇ StrategyEnums.cs       # Shared enums (Price, TradingSession, etc.)
Γöé   ΓööΓöÇΓöÇ Services/
Γöé       ΓööΓöÇΓöÇ StrategyJsonParser.cs  # JSON parsing utilities
Γö£ΓöÇΓöÇ Helpers/
Γöé   ΓööΓöÇΓöÇ StrategyLoader.cs          # Loads JSON ΓåÆ TradingStrategy for backend
Γö£ΓöÇΓöÇ Strategy/
Γöé   Γö£ΓöÇΓöÇ Stock.cs                   # Fluent builder entry point
Γöé   Γö£ΓöÇΓöÇ Strategy.cs                # TradingStrategy container
Γöé   ΓööΓöÇΓöÇ ...                        # Conditions, OrderAction, etc.
ΓööΓöÇΓöÇ IdiotProof.UnitTests/          # Unit tests
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
    .TrailingStopLoss(Atr.Balanced)           // 2├ù ATR trailing stop
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
   - Reorder by dragging within the canvas or using Γû▓/Γû╝ buttons
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
Γö£ΓöÇΓöÇ 2025-01-15/
Γöé   Γö£ΓöÇΓöÇ VIVS_Breakout_Strategy.json
Γöé   Γö£ΓöÇΓöÇ CATX_VWAP_Scalp.json
Γöé   ΓööΓöÇΓöÇ ...
Γö£ΓöÇΓöÇ 2025-01-16/
Γöé   ΓööΓöÇΓöÇ ...
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
| ≡ƒôì Start | Ticker |
| ΓÅ░ Session | SessionDuration |
| ≡ƒÆ░ Price Conditions | Breakout, Pullback, IsPriceAbove, IsPriceBelow, IsGapUp, IsGapDown |
| ≡ƒôè VWAP Conditions | IsAboveVwap, IsBelowVwap, IsCloseAboveVwap, IsVwapRejection |
| ≡ƒôê Indicators | IsRsi, IsMacd, IsAdx, IsDI, IsEmaAbove, IsEmaBelow, IsEmaBetween, IsEmaTurningUp |
| ≡ƒôë Momentum | IsMomentumAbove, IsMomentumBelow, IsRocAbove, IsRocBelow |
| ≡ƒôè Patterns | IsHigherLows, IsVolumeAbove |
| ≡ƒ¢Æ Orders | Long, Short, CloseLong, CloseShort |
| ≡ƒ¢í∩╕Å Risk Management | TakeProfit, TakeProfitRange, StopLoss, TrailingStopLoss, AdaptiveOrder |
| ≡ƒôñ Position Management | ExitStrategy, IsProfitable |
| ΓÜÖ∩╕Å Order Config | TimeInForce, OutsideRTH, TakeProfitOutsideRTH, AllOrNone, PriceType, OrderType |

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
2. Build ΓåÆ Build Solution (Ctrl+Shift+B)
3. Run without debugger: Debug ΓåÆ Start Without Debugging (Ctrl+F5)

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
Γö£ΓöÇΓöÇ ipc_YYYY-MM-DD.log         # IPC communication log (daily)
Γö£ΓöÇΓöÇ session_state.log          # Current session state (overwritten every 20 min)
Γö£ΓöÇΓöÇ session_*_final.log        # Final session logs (on normal exit)
Γö£ΓöÇΓöÇ session_*_crash.log        # Crash logs (on unhandled exception)
ΓööΓöÇΓöÇ crash_*.txt                # Crash dumps (if any)
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
ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
Γòæ  INDICATOR WARM-UP REQUIREMENTS                                      Γòæ
ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
Γòæ  Indicator         Γöé Bars Needed Γöé Start Early By Γöé Period          Γòæ
Γòæ  ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓö╝ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓö╝ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓö╝ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ  Γòæ
Γòæ  EMA(9)            Γöé 9 bars      Γöé 10 minutes     Γöé Configurable    Γòæ
Γòæ  EMA(21)           Γöé 21 bars     Γöé 25 minutes     Γöé Configurable    Γòæ
Γòæ  EMA(200)          Γöé 200 bars    Γöé 3+ hours       Γöé Configurable    Γòæ
Γòæ  ADX(14)           Γöé 28 bars     Γöé 30 minutes     Γöé Fixed (14)      Γòæ
Γòæ  RSI(14)           Γöé 15 bars     Γöé 20 minutes     Γöé Fixed (14)      Γòæ
Γòæ  MACD(12,26,9)     Γöé 35 bars     Γöé 40 minutes     Γöé Fixed           Γòæ
Γòæ  DI (+DI/-DI)      Γöé 28 bars     Γöé 30 minutes     Γöé Uses ADX        Γòæ
Γòæ  Momentum(10)      Γöé 11 bars     Γöé 15 minutes     Γöé Fixed (10)      Γòæ
Γòæ  ROC(10)           Γöé 11 bars     Γöé 15 minutes     Γöé Fixed (10)      Γòæ
Γòæ  HigherLows(3)     Γöé 3+ bars     Γöé 5 minutes      Γöé Configurable    Γòæ
Γòæ  EmaTurningUp(N)   Γöé N+1 bars    Γöé N+5 minutes    Γöé Configurable    Γòæ
Γòæ  VolumeAbove       Γöé 20 bars     Γöé 25 minutes     Γöé Fixed (20)      Γòæ
Γòæ  CloseAboveVwap    Γöé 1 bar       Γöé 2 minutes      Γöé N/A             Γòæ
Γòæ  VwapRejection     Γöé 1 bar       Γöé 2 minutes      Γöé N/A             Γòæ
ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
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
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ   $150.00 ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ    Γòæ ΓåÉ Breakout Level
    Γòæ          /                                                 Γòæ
    Γòæ         /   ΓåÉ Price rises to $150 = TRIGGERED             Γòæ
    Γòæ        /                                                   Γòæ
    Γòæ   ____/                                                    Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented

---

#### Pullback

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.Pullback(level)` | `Pullback(148.00)` | Price <= level |
| `.IsPriceBelow(level)` | `IsPriceBelow(148.00)` | Price < level |

```
    Pullback Condition
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ        /\                                                  Γòæ
    Γòæ       /  \                                                 Γòæ
    Γòæ      /    \                                                Γòæ
    Γòæ     /      \___                                            Γòæ
    Γòæ   $148.00 ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ    Γòæ ΓåÉ Pullback Level
    Γòæ              Γåæ Price drops to $148 = TRIGGERED            Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented

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
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ   Price ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ                    Γòæ
    Γòæ                       Γåæ AboveVwap() = TRUE                 Γòæ
    Γòæ   VWAP  ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ    Γòæ
    Γòæ                       Γåô BelowVwap() = TRUE                 Γòæ
    Γòæ   Price ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ                    Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** None (real-time VWAP)

---

#### CloseAboveVwap (Strong VWAP Signal)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsCloseAboveVwap()` | `IsCloseAboveVwap()` | Last candle CLOSED above VWAP |

```
    CloseAboveVwap vs AboveVwap
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ          ΓöîΓöÇΓöÇΓöÇΓöÉ                                             Γòæ
    Γòæ          Γöé   Γöé ΓåÉ Close ABOVE VWAP = Strong signal         Γòæ
    Γòæ   VWAP ΓòÉΓòÉΓöéΓòÉΓòÉΓòÉΓöéΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ   Γòæ
    Γòæ          Γöé   Γöé                                             Γòæ
    Γòæ          ΓööΓöÇΓöÇΓöÇΓöÿ                                             Γòæ
    Γòæ                                                            Γòæ
    Γòæ          ΓöîΓöÇΓöÇΓöÇΓöÉ                                             Γòæ
    Γòæ          Γöé   Γöé                                             Γòæ
    Γòæ   VWAP ΓòÉΓòÉΓöéΓòÉΓòÉΓòÉΓöéΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ   Γòæ
    Γòæ          Γöé   Γöé ΓåÉ Close below, wick above (weak signal)    Γòæ
    Γòæ          ΓööΓöÇΓöÇΓöÇΓöÿ                                             Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** 1 candle

---

#### VwapRejection (Bearish Signal)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsVwapRejection()` | `IsVwapRejection()` | Wick above VWAP, close below |
| | `IsVwapRejected()` | Alias |

```
    VWAP Rejection Pattern (Bearish)
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ          Γöé ΓåÉ Wick above VWAP (buyers tried)               Γòæ
    Γòæ   VWAP ΓòÉΓòÉΓò¬ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ   Γòæ
    Γòæ        ΓöîΓöÇΓö┤ΓöÇΓöÉ                                               Γòæ
    Γòæ        Γöé   Γöé ΓåÉ Close below = REJECTED (sellers won)       Γòæ
    Γòæ        ΓööΓöÇΓöÇΓöÇΓöÿ                                               Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Signal: Failed VWAP reclaim = Bearish                   Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** 1 candle

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
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ   Price Above EMA = Bullish                                Γòæ
    Γòæ        ______/                                             Γòæ
    Γòæ       /   Γåæ EmaAbove(9) = TRUE                            Γòæ
    Γòæ   EMA(9) ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ   Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Bullish Stack (Price > EMA9 > EMA21 > EMA200):          Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Price  ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ                     Γòæ
    Γòæ   EMA(9)  ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ                     Γòæ
    Γòæ   EMA(21) ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ                     Γòæ
    Γòæ   EMA(200) ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ                    Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** N candles for EMA(N)

---

#### EmaBetween (Pullback Zone)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsEmaBetween(9, 21)` | `IsEmaBetween(9, 21)` | Price between EMA(9) and EMA(21) |

```
    EmaBetween - Pullback Zone Strategy
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ   EMA(21) ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ  Γòæ ΓåÉ Upper boundary
    Γòæ            Γåæ                                               Γòæ
    Γòæ        * * * PULLBACK ZONE * * *                          Γòæ
    Γòæ            Γåô  (potential entry)                           Γòæ
    Γòæ   EMA(9)  ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ  Γòæ ΓåÉ Lower boundary
    Γòæ                                                            Γòæ
    Γòæ   Example:                                                 Γòæ
    Γòæ        /\                                                  Γòæ
    Γòæ       /  \___    ΓåÉ Price pulls back into zone             Γòæ
    Γòæ   ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ                          Γòæ
    Γòæ   ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ                          Γòæ
    Γòæ              Γåæ EmaBetween(9,21) = TRUE                    Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** Max(period1, period2) candles

---

#### EmaTurningUp (Slope Detection)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsEmaTurningUp(9)` | `IsEmaTurningUp(9)` | EMA(9) slope is flat or positive |

```
    EMA Turning Up Pattern
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ                                        ___/                Γòæ
    Γòæ                                    ___/                    Γòæ
    Γòæ                                ___/ ΓåÉ Turning Up!          Γòæ
    Γòæ   \_____                   __/                             Γòæ
    Γòæ         \________________/                                 Γòæ
    Γòæ              Γåæ Flattening before turn                      Γòæ
    Γòæ                                                            Γòæ
    Γòæ   EmaTurningUp(9) detects when:                           Γòæ
    Γòæ   Current EMA >= Previous EMA (slope >= 0)                Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** N+1 candles for EMA(N)

---

### Momentum Indicators

#### MomentumAbove / MomentumBelow

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsMomentumAbove(0)` | `IsMomentumAbove(0)` | Momentum >= 0 (positive) |
| `.IsMomentumBelow(0)` | `IsMomentumBelow(0)` | Momentum <= 0 (negative) |

```
    Momentum Indicator
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ   Formula: Momentum = Current Price - Price N periods ago  Γòæ
    ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
    Γòæ                                                            Γòæ
    Γòæ        /\                    /\                            Γòæ
    Γòæ       /  \                  /  \     Price                 Γòæ
    Γòæ      /    \                /    \                          Γòæ
    Γòæ   ΓöÇΓöÇ/ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ\ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ/ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ\ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Zero Line    Γòæ
    Γòæ             \            /        \                        Γòæ
    Γòæ              \__________/                                  Γòæ
    Γòæ        Γåæ                                                   Γòæ
    Γòæ   MomentumAbove(0) = TRUE    MomentumBelow(0) = TRUE      Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** 11 candles

---

#### RocAbove / RocBelow (Rate of Change)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsRocAbove(2)` | `IsRocAbove(2)` | ROC >= 2% (strong bullish) |
| `.IsRocBelow(-2)` | `IsRocBelow(-2)` | ROC <= -2% (strong bearish) |

```
    Rate of Change (ROC)
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ   Formula: ROC = ((Current - Previous) / Previous) ├ù 100   Γòæ
    ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
    Γòæ                                                            Γòæ
    Γòæ   +5% ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Strong Bullish    Γòæ
    Γòæ   +2% ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ Threshold       Γòæ
    Γòæ    0% ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Zero Line         Γòæ
    Γòæ   -2% ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ Threshold       Γòæ
    Γòæ   -5% ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Strong Bearish    Γòæ
    Γòæ                                                            Γòæ
    Γòæ   RocAbove(2) = Price up 2%+ from 10 bars ago             Γòæ
    Γòæ   RocBelow(-2) = Price down 2%+ from 10 bars ago          Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** 11 candles

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
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ   100 ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Top     Γòæ
    Γòæ    70 ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ OVERBOUGHT ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ   Γòæ ΓåÉ RsiOverbought(70)
    Γòæ    50 ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Neutral ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ    Γòæ
    Γòæ    30 ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ OVERSOLD ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ   Γòæ ΓåÉ RsiOversold(30)
    Γòæ     0 ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Bot     Γòæ
    Γòæ                                                            Γòæ
    Γòæ   RSI Oversold Bounce Strategy:                           Γòæ
    Γòæ        70 ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ    Γòæ
    Γòæ           ____                          ____              Γòæ
    Γòæ          /    \                        /    \             Γòæ
    Γòæ         /      \                      /      \  RSI       Γòæ
    Γòæ        /        \                    /        \           Γòæ
    Γòæ       /          \                  /          \          Γòæ
    Γòæ        30 ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ    Γòæ
    Γòæ                  Γåæ Buy signal (RSI <= 30)                 Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** 15 candles

---

### ADX (Average Directional Index)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsAdx(Comparison.Gte, 25)` | `IsAdxAbove(25)` | ADX >= 25 (strong trend) |

```
    ADX Trend Strength
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ   75+ ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Extremely Strong (rare) ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ     Γòæ
    Γòæ   50+ ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Very Strong Trend ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ     Γòæ
    Γòæ   25+ ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ STRONG ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉ  Γòæ ΓåÉ AdxAbove(25)
    Γòæ   20  ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Trend Developing ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ     Γòæ
    Γòæ   <20 ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Weak/No Trend (ranging) ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ     Γòæ
    Γòæ                                                            Γòæ
    Γòæ   ADX measures TREND STRENGTH (not direction)             Γòæ
    Γòæ   Use with DI for direction:                              Γòæ
    Γòæ     - AdxAbove(25) + DiPositive() = Strong bullish trend  Γòæ
    Γòæ     - AdxAbove(25) + DiNegative() = Strong bearish trend  Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** 28 candles

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
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ   +DI (Bullish Pressure)                                   Γòæ
    Γòæ         /    \         ___/                                Γòæ
    Γòæ        /      \    ___/    \                               Γòæ
    Γòæ       /        \__/         \                              Γòæ
    Γòæ      /  ___                  \____                         Γòæ
    Γòæ   __/__/   \_____                 \                        Γòæ
    Γòæ                  -DI (Bearish Pressure)                    Γòæ
    Γòæ         Γåæ                                                  Γòæ
    Γòæ   +DI > -DI = DiPositive() = Bullish                      Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Best Practice: Combine with ADX                         Γòæ
    Γòæ   .IsAdx(Comparison.Gte, 25)  // Strong trend             Γòæ
    Γòæ   .IsDiPositive()             // Bullish direction        Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** 28 candles (uses ADX)

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
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ   MACD Line = EMA(12) - EMA(26)                            Γòæ
    Γòæ   Signal Line = EMA(9) of MACD                             Γòæ
    Γòæ   Histogram = MACD - Signal                                Γòæ
    ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
    Γòæ                                                            Γòæ
    Γòæ   MACD Bullish Signal:                                     Γòæ
    Γòæ         MACD ___                                           Γòæ
    Γòæ             /   \         ___/                             Γòæ
    Γòæ   Signal __/     \    ___/                                 Γòæ
    Γòæ                   \__/                                     Γòæ
    Γòæ           Γåæ MACD > Signal = Bullish (MacdBullish())       Γòæ
    Γòæ                                                            Γòæ
    Γòæ   MACD Histogram:                                          Γòæ
    Γòæ              ΓûêΓûêΓûêΓûê                                          Γòæ
    Γòæ          ΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûê                                          Γòæ
    Γòæ      ΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûê         ΓûêΓûêΓûêΓûê                             Γòæ
    Γòæ   ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Zero        Γòæ
    Γòæ                       ΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûê                             Γòæ
    Γòæ                   ΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûê                             Γòæ
    Γòæ       Γåæ Rising        Γåæ Falling                           Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** 35 candles

---

### Pattern Detection

#### HigherLows

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsHigherLows()` | `IsHigherLows()` | Default 3-bar lookback |
| `.IsHigherLows(5)` | `IsHigherLows(5)` | 5-bar lookback |

```
    Higher Lows Pattern (Bullish)
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ                              /\                            Γòæ
    Γòæ                  /\         /  \                           Γòæ
    Γòæ        /\       /  \       /    \                          Γòæ
    Γòæ       /  \     /    \     /      \                         Γòæ
    Γòæ      /    \___/  Γåæ   \___/        \                        Γòæ
    Γòæ             Higher    Higher                               Γòæ
    Γòæ               Low       Low                                Γòæ
    Γòæ                                                            Γòæ
    Γòæ   HigherLows() detects:                                   Γòæ
    Γòæ     Low[0] > Low[1] > Low[2]                              Γòæ
    Γòæ   (most recent low is highest)                            Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Signal: Building support, bullish momentum              Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** 3+ candles (configurable)

---

#### VolumeAbove

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsVolumeAbove(1.5)` | `IsVolumeAbove(1.5)` | Volume >= 1.5x average |
| `.IsVolumeAbove(2.0)` | `IsVolumeAbove(2.0)` | Volume >= 2x average |

```
    Volume Spike Detection
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ                  ΓûêΓûêΓûêΓûê                                      Γòæ
    Γòæ                  ΓûêΓûêΓûêΓûê  ΓåÉ Volume spike (1.5x+)             Γòæ
    Γòæ   Average ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ   Γòæ
    Γòæ     ΓûêΓûêΓûêΓûê      ΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûê      ΓûêΓûêΓûêΓûê                           Γòæ
    Γòæ     ΓûêΓûêΓûêΓûê  ΓûêΓûê  ΓûêΓûêΓûêΓûêΓûêΓûêΓûêΓûê  ΓûêΓûê  ΓûêΓûêΓûêΓûê                           Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Common Multipliers:                                      Γòæ
    Γòæ     1.5x = Moderate spike (50% above average)             Γòæ
    Γòæ     2.0x = Strong spike (100% above average)              Γòæ
    Γòæ     3.0x = Exceptional spike (200% above average)         Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Use: Confirm breakouts with volume                      Γòæ
    Γòæ   .Breakout(150).IsVolumeAbove(1.5).Long()...            Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** 20 candles

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
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ               Gap Up ΓöîΓöÇΓöÇΓöÇΓöÇΓöÉ                                Γòæ
    Γòæ               (5%+)  Γöé    Γöé  Current Price                 Γòæ
    Γòæ                      ΓööΓöÇΓöÇΓöÇΓöÇΓöÿ                                Γòæ
    Γòæ                         Γåæ                                  Γòæ
    Γòæ       ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Gap                 Γòæ
    Γòæ                         Γåô                                  Γòæ
    Γòæ       ΓöîΓöÇΓöÇΓöÇΓöÇΓöÉ                                               Γòæ
    Γòæ       Γöé    Γöé  Previous Close                               Γòæ
    Γòæ       ΓööΓöÇΓöÇΓöÇΓöÇΓöÿ                                               Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Formula: ((Current - PrevClose) / PrevClose) * 100      Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Use Case: Gap-and-Go strategies, morning momentum       Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
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

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** Previous session close required (auto-fetched from historical data)

---

#### IsGapDown / GapDown (canonical / alias)

| Fluent API | IdiotScript | Description |
|------------|-------------|-------------|
| `.IsGapDown(3)` | `IsGapDown(3)` | Price gapped down >= 3% from previous close (canonical) |
| `.GapDown(3)` | `GapDown(3)` | Alias for IsGapDown(3) |
| `.IsGapDown(5)` | `IsGapDown(5%)` | Price gapped down >= 5% (% sign optional) |

```
    Gap Down Detection
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                            Γòæ
    Γòæ       ΓöîΓöÇΓöÇΓöÇΓöÇΓöÉ                                               Γòæ
    Γòæ       Γöé    Γöé  Previous Close                               Γòæ
    Γòæ       ΓööΓöÇΓöÇΓöÇΓöÇΓöÿ                                               Γòæ
    Γòæ                         Γåæ                                  Γòæ
    Γòæ       ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ Gap                 Γòæ
    Γòæ                         Γåô                                  Γòæ
    Γòæ               Gap Down ΓöîΓöÇΓöÇΓöÇΓöÇΓöÉ                              Γòæ
    Γòæ               (3%+)    Γöé    Γöé  Current Price               Γòæ
    Γòæ                        ΓööΓöÇΓöÇΓöÇΓöÇΓöÿ                              Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Formula: ((PrevClose - Current) / PrevClose) * 100      Γòæ
    Γòæ                                                            Γòæ
    Γòæ   Use Case: Gap-fill reversals, panic selling bounces     Γòæ
    Γòæ                                                            Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
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

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** Previous session close required (auto-fetched from historical data)

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
    ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
    Γòæ                                                                    Γòæ
    Γòæ   ΓöîΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÉ Γòæ
    Γòæ   Γöé                                                              Γöé Γòæ
    Γòæ   Γû╝                                                              Γöé Γòæ
    Γòæ  [WAIT FOR CONDITIONS]                                           Γöé Γòæ
    Γòæ        Γöé                                                         Γöé Γòæ
    Γòæ        Γû╝ (all conditions met)                                    Γöé Γòæ
    Γòæ  [PLACE ENTRY ORDER]                                             Γöé Γòæ
    Γòæ        Γöé                                                         Γöé Γòæ
    Γòæ        Γû╝ (entry filled)                                          Γöé Γòæ
    Γòæ  [MONITOR POSITION]                                              Γöé Γòæ
    Γòæ        Γöé                                                         Γöé Γòæ
    Γòæ        Γö£ΓöÇΓöÇ TakeProfit hit ΓöÇΓöÇΓöÇΓöÇΓöÉ                                  Γöé Γòæ
    Γòæ        Γö£ΓöÇΓöÇ StopLoss hit ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöñ                                  Γöé Γòæ
    Γòæ        Γö£ΓöÇΓöÇ TrailingStop hit ΓöÇΓöÇΓöñ                                  Γöé Γòæ
    Γòæ        ΓööΓöÇΓöÇ ExitStrategy hit ΓöÇΓöÇΓöñ                                  Γöé Γòæ
    Γòæ                               Γû╝                                  Γöé Γòæ
    Γòæ                    ΓöîΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÉ                          Γöé Γòæ
    Γòæ                    Γöé  RepeatEnabled?  Γöé                          Γöé Γòæ
    Γòæ                    ΓööΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÿ                          Γöé Γòæ
    Γòæ                       Γöé           Γöé                              Γöé Γòæ
    Γòæ                    YESΓöé           ΓöéNO                            Γöé Γòæ
    Γòæ                       Γû╝           Γû╝                              Γöé Γòæ
    Γòæ                  [RESET]     [COMPLETE]                          Γöé Γòæ
    Γòæ                      Γöé                                           Γöé Γòæ
    Γòæ                      ΓööΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÿ Γòæ
    Γòæ                                                                    Γòæ
    Γòæ   Reset clears: Order IDs, fill prices, condition index           Γòæ
    Γòæ   Reset keeps:  VWAP tracking, session high/low, indicators       Γòæ
    Γòæ                                                                    Γòæ
    ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
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

**Implementation Status:** Γ£à Fully Implemented | **Warm-up:** None required

---

### Complete IdiotScript Command Reference

```
ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
Γòæ  IDIOTSCRIPT COMMAND REFERENCE (Single-Responsibility Pattern)         Γòæ
ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
Γòæ                                                                         Γòæ
Γòæ  CONFIGURATION (order doesn't matter within group):                     Γòæ
Γòæ    Ticker(AAPL)              - Set stock symbol                        Γòæ
Γòæ    Name("Strategy Name")     - Set strategy name                       Γòæ
Γòæ    Session(IS.PREMARKET)     - Set trading session                     Γòæ
Γòæ    Qty(100)                  - Set order quantity (can also chain      Γòæ
Γòæ                                after Long()/Short())                   Γòæ
Γòæ    Enabled(YES)              - Enable/disable strategy                 Γòæ
Γòæ                                                                         Γòæ
Γòæ  ENTRY CONDITIONS (order matters - sequential state machine):           Γòæ
Γòæ    Entry(150.00)             - Price >= level (CONDITION)              Γòæ
Γòæ    Breakout(150.00)          - Price >= level (alias for Entry)        Γòæ
Γòæ    Pullback(148.00)          - Price <= level                          Γòæ
Γòæ    IsPriceAbove(150.00)      - Price > level                           Γòæ
Γòæ    IsPriceBelow(148.00)      - Price < level                           Γòæ
Γòæ                                                                         Γòæ
Γòæ  VWAP CONDITIONS:                                                       Γòæ
Γòæ    IsAboveVwap()             - Price >= VWAP                           Γòæ
Γòæ    IsBelowVwap()             - Price <= VWAP                           Γòæ
Γòæ    IsCloseAboveVwap()        - Candle closed above VWAP (strong)       Γòæ
Γòæ    IsVwapRejection()         - Wick above, close below VWAP            Γòæ
Γòæ                                                                         Γòæ
Γòæ  EMA CONDITIONS:                                                        Γòæ
Γòæ    IsEmaAbove(9)             - Price >= EMA(9)                         Γòæ
Γòæ    IsEmaBelow(200)           - Price <= EMA(200)                       Γòæ
Γòæ    IsEmaBetween(9, 21)       - Price between EMA(9) and EMA(21)        Γòæ
Γòæ    IsEmaTurningUp(9)         - EMA(9) slope >= 0                       Γòæ
Γòæ                                                                         Γòæ
Γòæ  MOMENTUM CONDITIONS:                                                   Γòæ
Γòæ    IsMomentumAbove(0)        - Momentum >= threshold                   Γòæ
Γòæ    IsMomentumBelow(0)        - Momentum <= threshold                   Γòæ
Γòæ    IsRocAbove(2)             - Rate of Change >= 2%                    Γòæ
Γòæ    IsRocBelow(-2)            - Rate of Change <= -2%                   Γòæ
Γòæ                                                                         Γòæ
Γòæ  RSI CONDITIONS:                                                        Γòæ
Γòæ    IsRsiOversold()           - RSI <= 30 (default)                     Γòæ
Γòæ    IsRsiOversold(25)         - RSI <= 25                               Γòæ
Γòæ    IsRsiOverbought()         - RSI >= 70 (default)                     Γòæ
Γòæ    IsRsiOverbought(80)       - RSI >= 80                               Γòæ
Γòæ                                                                         Γòæ
Γòæ  TREND CONDITIONS:                                                      Γòæ
Γòæ    IsAdxAbove(25)            - ADX >= 25 (strong trend)                Γòæ
Γòæ    IsDiPositive()            - +DI > -DI (bullish)                     Γòæ
Γòæ    IsDiNegative()            - -DI > +DI (bearish)                     Γòæ
Γòæ    IsMacdBullish()           - MACD > Signal line                      Γòæ
Γòæ    IsMacdBearish()           - MACD < Signal line                      Γòæ
Γòæ                                                                         Γòæ
Γòæ  PATTERN CONDITIONS:                                                    Γòæ
Γòæ    IsHigherLows()            - Higher lows forming (default 3 bars)    Γòæ
Γòæ    IsHigherLows(5)           - Higher lows with 5-bar lookback         Γòæ
Γòæ    IsVolumeAbove(1.5)        - Volume >= 1.5x average                  Γòæ
Γòæ                                                                         Γòæ
Γòæ  GAP CONDITIONS:                                                        Γòæ
Γòæ    IsGapUp(5)                - Price gapped up 5%+ from prev close     Γòæ
Γòæ    IsGapDown(3)              - Price gapped down 3%+ from prev close   Γòæ
Γòæ                                                                         Γòæ
Γòæ  ORDER ACTIONS (after conditions are met):                              Γòæ
Γòæ    Order(IS.LONG)            - Opens a LONG position (explicit)        Γòæ
Γòæ    Order(IS.SHORT)           - Opens a SHORT position                  Γòæ
Γòæ    Order()                   - Opens a LONG position (default)         Γòæ
Γòæ    Long()                    - Alias for Order(IS.LONG)                Γòæ
Γòæ    Short()                   - Alias for Order(IS.SHORT)               Γòæ
Γòæ    CloseLong()               - Close LONG position                     Γòæ
Γòæ    CloseShort()              - Close SHORT position                    Γòæ
Γòæ                                                                         Γòæ
Γòæ  ORDER CONFIGURATION (chained after Order):                             Γòæ
Γòæ    .Quantity(100)            - Sets order quantity (canonical)         Γòæ
Γòæ    .Qty(100)                 - Alias for Quantity                      Γòæ
Γòæ    .PriceType(IS.VWAP)       - Sets price type for execution           Γòæ
Γòæ    .OrderType(IS.MARKET)     - Sets market vs limit order              Γòæ
Γòæ    .OutsideRTH()             - Allow entry outside RTH (default: true) Γòæ
Γòæ    .TakeProfitOutsideRTH()   - Allow TP outside RTH (default: true)    Γòæ
Γòæ                                                                         Γòæ
Γòæ  EXIT CONDITIONS (after position is opened):                            Γòæ
Γòæ    TakeProfit(155.00)        - Take profit at price (canonical)        Γòæ
Γòæ    TP(155.00)                - Alias for TakeProfit                    Γòæ
Γòæ    StopLoss(145.00)          - Stop loss at price (canonical)          Γòæ
Γòæ    SL(145.00)                - Alias for StopLoss                      Γòæ
Γòæ    TrailingStopLoss(IS.TIGHT) - Trailing stop (canonical)              Γòæ
Γòæ    TSL(5)                    - Alias for TrailingStopLoss              Γòæ
Γòæ    ExitStrategy(IS.BELL)     - Close at session end (canonical)        Γòæ
Γòæ    .IsProfitable()           - Only exit if profitable (chained)       Γòæ
Γòæ                                                                         Γòæ
Γòæ  SMART ORDER MANAGEMENT:                                                Γòæ
Γòæ    AdaptiveOrder()           - Enable smart dynamic TP/SL (balanced)   Γòæ
Γòæ    AdaptiveOrder(IS.CONSERVATIVE) - Protect gains quickly              Γòæ
Γòæ    AdaptiveOrder(IS.BALANCED)     - Equal priority profit/protection   Γòæ
Γòæ    AdaptiveOrder(IS.AGGRESSIVE)   - Maximize profit in strong trends   Γòæ
Γòæ    IsAdaptiveOrder()         - Alias for AdaptiveOrder()               Γòæ
Γòæ                                                                         Γòæ
Γòæ  ORDER CONFIG:                                                          Γòæ
Γòæ    TimeInForce(IS.GTC)       - Order time in force                     Γòæ
Γòæ    OutsideRTH(YES)           - Allow extended hours                    Γòæ
Γòæ    AllOrNone(YES)            - Require full fill                       Γòæ
Γòæ    Repeat(YES)               - Repeat strategy after exit              Γòæ
Γòæ                                                                         Γòæ
ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
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
ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
Γòæ  [CONFIG]           ΓåÆ   [ENTRY CONDITIONS]          ΓåÆ   Γ£à ORDER              Γòæ
Γòæ  ΓöîΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÉ     ΓöîΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÉ        ΓöîΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÉ    Γòæ
Γòæ  Γöé Ticker(AAPL)   Γöé     Γöé Entry(150.00)        Γöé        Γöé Long()         Γöé    Γòæ
Γòæ  Γöé Session(PRE)   Γöé  ΓåÆ  Γöé IsHigherLows()       Γöé  ΓåÆ     Γöé Qty(100)       Γöé    Γòæ
Γòæ  Γöé Name(...)      Γöé     Γöé IsAboveVwap()        Γöé        ΓööΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÿ    Γòæ
Γòæ  ΓööΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÿ     Γöé IsEmaAbove(9)        Γöé              Γöé               Γòæ
Γòæ                         Γöé IsDiPositive()       Γöé              Γåô               Γòæ
Γòæ                         Γöé IsMomentumAbove(0)   Γöé     [EXIT CONDITIONS]        Γòæ
Γòæ                         ΓööΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÿ     ΓöîΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÉ   Γòæ
Γòæ                                                      Γöé TakeProfit(160)    Γöé   Γòæ
Γòæ                                                      Γöé StopLoss(145)      Γöé   Γòæ
Γòæ                                                      Γöé TrailingStopLoss() Γöé   Γòæ
Γòæ                                                      Γöé ExitStrategy(BELL) Γöé   Γòæ
Γòæ                                                      Γöé .IsProfitable()    Γöé   Γòæ
Γòæ                                                      ΓööΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÿ   Γòæ
ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

---

#### `.Breakout(double level)`
Triggers when price rises to or above the specified level.

```csharp
.Breakout(7.10)    // Triggers when price >= $7.10
```

**Implementation Status:** Γ£à Fully Implemented

---

#### `.Pullback(double level)`
Triggers when price falls to or below the specified level.

```csharp
.Pullback(6.80)    // Triggers when price <= $6.80
```

**Implementation Status:** Γ£à Fully Implemented

---

#### `.IsAboveVwap(double buffer = 0)`
Triggers when price is at or above VWAP plus optional buffer.

```csharp
.IsAboveVwap()           // Price >= VWAP
.IsAboveVwap(0.02)       // Price >= VWAP + $0.02
```

**Implementation Status:** Γ£à Fully Implemented

---

#### `.IsBelowVwap(double buffer = 0)`
Triggers when price is at or below VWAP minus optional buffer.

```csharp
.IsBelowVwap()           // Price <= VWAP
.IsBelowVwap(0.05)       // Price <= VWAP - $0.05
```

**Implementation Status:** Γ£à Fully Implemented

---

#### `.IsPriceAbove(double level)`
Triggers when price is strictly above the level (not equal).

```csharp
.IsPriceAbove(10.00)     // Price > $10.00
```

**Implementation Status:** Γ£à Fully Implemented

---

#### `.IsPriceBelow(double level)`
Triggers when price is strictly below the level (not equal).

```csharp
.IsPriceBelow(9.50)      // Price < $9.50
```

**Implementation Status:** Γ£à Fully Implemented

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

**Implementation Status:** Γ£à Fully Implemented

---

#### `.Condition(IStrategyCondition condition)`
Adds any custom condition implementing the `IStrategyCondition` interface.

```csharp
.Condition(new MyCustomCondition())
```

**Implementation Status:** Γ£à Fully Implemented

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

**Implementation Status:** Γ£à Fully Implemented

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

**Implementation Status:** Γ£à Fully Implemented

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

**Implementation Status:** Γ£à Fully Implemented

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

**Implementation Status:** Γ£à Fully Implemented

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
1. Initializes at entry price ├ù (1 - percent)
2. Tracks high-water mark (highest price since entry)
3. Recalculates stop as highWaterMark ├ù (1 - percent)
4. Stop only moves UP, never down
5. Triggers immediate market sell when price <= trailing stop
6. Cancels take profit order when triggered

**Implementation Status:** Γ£à Fully Implemented

---

#### `.TrailingStopLoss(AtrStopLossConfig config)`
Enables an ATR-based trailing stop loss that adapts to market volatility.

```csharp
// Preset configurations
.TrailingStopLoss(Atr.Tight)        // 1.5├ù ATR (more stops, smaller losses)
.TrailingStopLoss(Atr.Balanced)     // 2.0├ù ATR (recommended for swing trading)
.TrailingStopLoss(Atr.Loose)        // 3.0├ù ATR (trend following, fewer stops)
.TrailingStopLoss(Atr.VeryLoose)    // 4.0├ù ATR (long-term positions)

// Custom multiplier
.TrailingStopLoss(Atr.Multiplier(2.5))  // 2.5├ù ATR

// Custom with bounds
.TrailingStopLoss(Atr.WithBounds(
    multiplier: 2.0,
    minStopPercent: 0.02,   // At least 2% away
    maxStopPercent: 0.20    // At most 20% away
))
```

**How ATR Trailing Stop Works:**
1. ATR (Average True Range) is calculated from price volatility over 14 periods
2. Stop distance = ATR ├ù Multiplier (e.g., $1.20 ATR ├ù 2.0 = $2.40)
3. Stop trails upward as price makes new highs
4. Triggers sell when price drops ATR distance below high water mark
5. Automatically adapts to volatility - tighter in calm markets, wider in volatile ones

**ATR Multiplier Guidelines:**

| Multiplier | Preset | Use Case | Characteristics |
|------------|--------|----------|-----------------|
| 1.5├ù | `Atr.Tight` | Scalping, quick trades | More stops, smaller losses |
| 2.0├ù | `Atr.Balanced` | Swing trading | Good risk/reward balance |
| 3.0├ù | `Atr.Loose` | Trend following | Fewer stops, larger swings |
| 4.0├ù | `Atr.VeryLoose` | Long-term positions | Maximum room to breathe |

**Example with Real Numbers:**

| ATR Value | Multiplier | Stop Distance | Entry $50, High $55 ΓåÆ Stop |
|-----------|------------|---------------|----------------------------|
| $1.20 | 1.5├ù | $1.80 | $53.20 |
| $1.20 | 2.0├ù | $2.40 | $52.60 |
| $1.20 | 3.0├ù | $3.60 | $51.40 |

**Why Use ATR Instead of Percentages?**
- **Adapts to volatility**: A volatile stock needs a wider stop; ATR calculates this automatically
- **Scientifically grounded**: Based on actual price movement, not arbitrary percentages
- **Reduces whipsaws**: Avoids being stopped out by normal market noise

**Implementation Status:** Γ£à Fully Implemented

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
- `IS.PREMARKET.BELL` ΓåÆ 9:29 AM
- `IS.RTH.BELL` ΓåÆ 3:59 PM
- `IS.AFTERHOURS.BELL` ΓåÆ 7:59 PM

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

**Implementation Status:** Γ£à Fully Implemented

---

#### `.OutsideRTH(bool outsideRth, bool takeProfit)`
Configures whether orders can execute outside regular trading hours.

```csharp
.OutsideRTH(outsideRth: true, takeProfit: true)    // Both allowed outside RTH
```

**Default:** Both `true`

**Implementation Status:** Γ£à Fully Implemented

---

#### `.OrderType(OrderType type)`
Sets the order type for entry.

```csharp
.OrderType(OrderType.Market)
.OrderType(OrderType.Limit)
```

**Default:** `OrderType.Limit` (safer for GTC orders)

**Implementation Status:** Γ£à Fully Implemented

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

**Implementation Status:** Γ£à Fully Implemented

---

#### `.Build()`
Builds and returns the strategy with current configuration. Terminal method.

```csharp
.Build()    // Returns TradingStrategy
```

**Returns:** `TradingStrategy`

**Implementation Status:** Γ£à Fully Implemented

---

## AdaptiveOrder - Smart Dynamic Order Management

AdaptiveOrder monitors market conditions in real-time and dynamically adjusts take profit and stop loss levels to maximize profit while managing risk.

### How It Works

```
ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
Γòæ  MARKET ANALYSIS                                                          Γòæ
Γòæ                                                                           Γòæ
Γòæ  The system continuously evaluates multiple indicators:                   Γòæ
Γòæ                                                                           Γòæ
Γòæ  1. VWAP Position (15%): Bullish above, bearish below                    Γòæ
Γòæ  2. EMA Stack (20%): Short-term vs long-term trend alignment             Γòæ
Γòæ  3. RSI (15%): Overbought/oversold for reversal risk                     Γòæ
Γòæ  4. MACD (20%): Momentum direction and strength                          Γòæ
Γòæ  5. ADX (20%): Trend strength (strong trends get wider targets)          Γòæ
Γòæ  6. Volume (10%): Confirmation of price moves                            Γòæ
Γòæ                                                                           Γòæ
Γòæ  These are combined into a Market Score (-100 to +100)                   Γòæ
Γòæ  Positive = bullish, Negative = bearish                                  Γòæ
ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

### Adaptive Behavior

```
ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
Γòæ  SCENARIO               Γöé  TAKE PROFIT         Γöé  STOP LOSS              Γòæ
ΓòæΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓö╝ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓö╝ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓòæ
Γòæ  Strong bullish (70+)   Γöé  Extend +50%         Γöé  Tighten (protect gain) Γòæ
Γòæ  Moderate bull (30-70)  Γöé  Keep original       Γöé  Keep original          Γòæ
Γòæ  Neutral (-30 to 30)    Γöé  Reduce 25%          Γöé  Widen (allow bounce)   Γòæ
Γòæ  Moderate bear (-70-30) Γöé  Reduce 50%          Γöé  Keep original          Γòæ
Γòæ  Strong bearish (<-70)  Γöé  EXIT IMMEDIATELY    Γöé  N/A - Emergency exit   Γòæ
ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
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
ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
Γòæ  Setting                  Γöé  CONSERVATIVE  Γöé  BALANCED  Γöé  AGGRESSIVE             Γòæ
ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¬ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¬ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¬ΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
Γòæ  MaxTakeProfitExtension   Γöé     25%        Γöé    50%     Γöé     75%                 Γòæ
Γòæ  MaxTakeProfitReduction   Γöé     60%        Γöé    50%     Γöé     30%                 Γòæ
Γòæ  MaxStopLossTighten       Γöé     30%        Γöé    50%     Γöé     60%                 Γòæ
Γòæ  MaxStopLossWiden         Γöé     40%        Γöé    25%     Γöé     15%                 Γòæ
Γòæ  EmergencyExitThreshold   Γöé     -60        Γöé    -70     Γöé     -80                 Γòæ
Γòæ  MinScoreChangeForAdjust  Γöé     10         Γöé    15      Γöé     20                  Γòæ
ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

**Conservative**: Protects gains quickly, allows wider stops to avoid noise, exits sooner on bearish signals.
**Balanced**: Standard risk/reward, moderate adjustments in both directions.
**Aggressive**: Lets winners run longer, tighter stops to protect capital, stays in longer during drawdowns.

### Adaptive TP Feedback Loop

The system extends TP when momentum is strong, then contracts it when momentum fades, allowing the price to eventually meet the target:

```
ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
Γòæ  MOMENTUM STRONG (Price approaching TP fast)                                  Γòæ
Γòæ  Γö£ΓöÇΓöÇ MACD: Bullish with strong histogram ΓåÆ +70 to +100 score                 Γòæ
Γòæ  Γö£ΓöÇΓöÇ ADX: High (40+) with +DI > -DI ΓåÆ +80 to +100 score                      Γòæ
Γòæ  Γö£ΓöÇΓöÇ RSI: Not yet overbought (50-65) ΓåÆ +0 to +37 score                       Γòæ
Γòæ  ΓööΓöÇΓöÇ Result: Total Score 70-100 ΓåÆ TP EXTENDS (multiplier 1.0 to 1.75)        Γòæ
ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
Γòæ  MOMENTUM FADING (Price slowing near extended TP)                             Γòæ
Γòæ  Γö£ΓöÇΓöÇ MACD: Histogram shrinking ΓåÆ score drops                                 Γòæ
Γòæ  Γö£ΓöÇΓöÇ ADX: Still high but momentum slowing                                    Γòæ
Γòæ  Γö£ΓöÇΓöÇ RSI: Becoming overbought (70+) ΓåÆ NEGATIVE contribution (-10 to -100)    Γòæ
Γòæ  ΓööΓöÇΓöÇ Result: Total Score drops to 30-69 ΓåÆ TP RETURNS TO ORIGINAL             Γòæ
ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
Γòæ  MOMENTUM EXHAUSTED                                                           Γòæ
Γòæ  Γö£ΓöÇΓöÇ MACD: Bearish crossover imminent                                        Γòæ
Γòæ  Γö£ΓöÇΓöÇ RSI: Overbought (75+) ΓåÆ strong negative                                 Γòæ
Γòæ  ΓööΓöÇΓöÇ Result: Score drops to 0-29 ΓåÆ TP REDUCES (multiplier 0.85-0.925)        Γòæ
Γòæ      ΓåÆ Price finally meets the lowered TP target ΓåÆ PROFIT TAKEN              Γòæ
ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

### Timeline Example: Dynamic TP Adjustment

```
Entry: $150  |  Original TP: $160  |  Profit Range: $10
Mode: AGGRESSIVE (75% max extension)

Timeline:
ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ
T=0   Price $151, Score +85 (strong momentum)
      ΓåÆ TP Multiplier = 1.375 ΓåÆ TP extended to $163.75

T=5   Price $158, Score +90 (very strong, approaching original TP)
      ΓåÆ TP Multiplier = 1.50 ΓåÆ TP extended to $165.00
      ΓåÆ Price would have hit $160 but TP is now $165!

T=10  Price $162, Score +75 (RSI overbought, MACD histogram shrinking)
      ΓåÆ TP Multiplier = 1.125 ΓåÆ TP reduced to $161.25

T=15  Price $161, Score +50 (momentum fading)
      ΓåÆ TP Multiplier = 1.0 ΓåÆ TP back to $160.00

T=20  Price $160, Score +45
      ΓåÆ TP still at $160.00 ΓåÆ *** TAKE PROFIT FILLED ***
ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ
Result: Captured $10 profit. System extended TP during strong momentum,
        then contracted it when momentum faded, allowing fill at optimal time.
```

### TP Extension Visualization

```
Price
  Γåæ
$165 ΓöÇ ΓöÇ ΓöÇ ΓöÇ ΓöÇ ΓöÇ ΓöÇ ΓöÇ Extended TP (score 90+)
      Γöé            Γò▒
$163 ΓöÇΓöéΓöÇ ΓöÇ ΓöÇ ΓöÇ ΓöÇ Γò▒ΓöÇ ΓöÇ TP following momentum
      Γöé        Γò▒
$161 ΓöÇΓöéΓöÇ ΓöÇ ΓöÇ Γò▒ΓöÇ ΓöÇ ΓöÇ ΓöÇ TP reducing as RSI overbought
      Γöé    Γò▒    Γåÿ
$160 ΓöÇΓöéΓöÇ Γò▒ΓöÇ ΓöÇ ΓöÇ ΓöÇ Γûê ΓåÉ FILLED HERE (TP came down to meet price)
      ΓöéΓò▒        Γåù
$158 ΓöÇΓöéΓöÇ ΓöÇ ΓöÇ ΓöÇ ΓöÇ Price path
      Γöé      Γò▒
$155 ΓöÇΓöéΓöÇ ΓöÇ Γò▒
      Γöé  Γò▒
$150 ΓöÇΓûêΓò▒ΓöÇ ΓöÇ ΓöÇ ΓöÇ ΓöÇ Entry
      Γöé
      ΓööΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓåÆ Time
         T=0  T=5  T=10  T=15  T=20
```

### RSI Overbought Detection (Key Mechanism)

The RSI component is crucial for detecting when momentum is exhausted:

```
RSI and Score Contribution:
+ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ+
Γöé  RSI Value  Γöé  Score Contribution  Γöé  Effect on TP            Γöé
Γö£ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓö╝ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓö╝ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöñ
Γöé  30 or less Γöé  +100 (oversold)     Γöé  Extend (bullish bounce) Γöé
Γöé  40         Γöé  +25                 Γöé  Slight extend           Γöé
Γöé  50         Γöé  0 (neutral)         Γöé  No change               Γöé
Γöé  60         Γöé  +25                 Γöé  Slight extend           Γöé
Γöé  70         Γöé  0 (threshold)       Γöé  No change               Γöé
Γöé  75         Γöé  -17                 Γöé  Start reducing TP       Γöé
Γöé  80         Γöé  -33                 Γöé  Reduce TP               Γöé
Γöé  85         Γöé  -50                 Γöé  Significant reduction   Γöé
Γöé  90+        Γöé  -67 to -100         Γöé  Maximum reduction       Γöé
+ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ+

As price rapidly approaches TP:
  ΓåÆ RSI rises toward 70+ (overbought)
  ΓåÆ RSI score contribution becomes NEGATIVE
  ΓåÆ Total score drops
  ΓåÆ TP multiplier reduces
  ΓåÆ TP target comes down to meet the price
```

### Concrete Example: CIGL Adaptive Trade

```
Entry: $4.15  |  Original TP: $4.80  |  Original SL: $3.90
Profit Range: $0.65  |  Loss Range: $0.25
Mode: AGGRESSIVE

Market Score: +85 (Strong Bullish)
Γö£ΓöÇΓöÇ VWAP Score:   +80  (price 4% above VWAP)
Γö£ΓöÇΓöÇ EMA Score:    +100 (price above all EMAs)
Γö£ΓöÇΓöÇ RSI Score:    -30  (RSI at 75, overbought caution)
Γö£ΓöÇΓöÇ MACD Score:   +90  (bullish with strong histogram)
Γö£ΓöÇΓöÇ ADX Score:    +100 (ADX 63 with +DI > -DI)
ΓööΓöÇΓöÇ Volume Score: +60  (1.6x average volume)

TP Multiplier = 1.0 + (0.75 ├ù (85-70)/30) = 1.375
SL Multiplier = 1.0 + (0.60 ├ù (85-70)/30) = 1.30

Adjusted TP: $4.15 + ($0.65 ├ù 1.375) = $5.04  (+21% from entry)
Adjusted SL: $4.15 - ($0.25 / 1.30) = $3.96   (tighter protection)
```

---

## AutonomousTrading - AI-Driven Entry/Exit Decisions

AutonomousTrading enables fully automated trading where the system monitors all indicators and independently decides when to buy, sell, short, or close positions based on market score analysis.

### Quick Start

```idiotscript
Ticker(AAPL)
.AutonomousTrading(IS.BALANCED)
```

That's it! The system will:
1. Monitor VWAP, EMA, RSI, MACD, ADX, and Volume continuously
2. Calculate a market score (-100 to +100)
3. Enter LONG when score >= threshold, SHORT when score <= negative threshold
4. Exit when momentum reverses
5. Optionally flip direction (exit long ΓåÆ enter short)

### AutonomousTrading Syntax

| Command | Description |
|---------|-------------|
| `AutonomousTrading()` | Enable autonomous trading (balanced mode) |
| `AutonomousTrading(IS.CONSERVATIVE)` | Fewer trades, higher thresholds (80/-80) |
| `AutonomousTrading(IS.BALANCED)` | Standard thresholds (70/-70) |
| `AutonomousTrading(IS.AGGRESSIVE)` | More trades, lower thresholds (60/-60) |
| `IsAutonomousTrading()` | Alias for AutonomousTrading() |

### Fluent API

```csharp
// Balanced mode (default)
Stock.Ticker("AAPL")
    .AutonomousTrading(AutonomousMode.Balanced)
    .Build();

// Aggressive mode
Stock.Ticker("UBER")
    .AutonomousTrading(AutonomousMode.Aggressive)
    .Quantity(100)
    .Build();
```

### How AutonomousTrading Works

```
+===========================================================================+
|  AUTONOMOUS TRADING FLOW                                                  |
+===========================================================================+
|                                                                           |
|  +---------------------------------------------------------------------+  |
|  |  MARKET SCORE CALCULATION (every tick)                              |  |
|  |                                                                     |  |
|  |  VWAP Position   ------- 15% weight ------+                        |  |
|  |  EMA Stack       ------- 20% weight ------+                        |  |
|  |  RSI Level       ------- 15% weight ------+--> SCORE (-100 to +100)|  |
|  |  MACD Signal     ------- 20% weight ------+                        |  |
|  |  ADX Strength    ------- 20% weight ------+                        |  |
|  |  Volume Ratio    ------- 10% weight ------+                        |  |
|  +---------------------------------------------------------------------+  |
|                              |                                            |
|                              v                                            |
|  +---------------------------------------------------------------------+  |
|  |  ENTRY DECISION                                                     |  |
|  |                                                                     |  |
|  |  Score >= +70 (balanced) ------------------------> ENTER LONG       |  |
|  |  Score <= -70 (balanced) ------------------------> ENTER SHORT      |  |
|  |  Score between -70 and +70 ----------------------> NO ACTION        |  |
|  +---------------------------------------------------------------------+  |
|                              |                                            |
|                              v                                            |
|  +---------------------------------------------------------------------+  |
|  |  EXIT DECISION (when position is open)                              |  |
|  |                                                                     |  |
|  |  LONG position + Score < +40 --------------------> EXIT LONG        |  |
|  |  SHORT position + Score > -40 -------------------> EXIT SHORT       |  |
|  |                                                                     |  |
|  |  With AllowDirectionFlip:                                          |  |
|  |  Exit LONG + Score <= -70 -----------------------> FLIP TO SHORT    |  |
|  |  Exit SHORT + Score >= +70 ----------------------> FLIP TO LONG     |  |
|  +---------------------------------------------------------------------+  |
+===========================================================================+
```

### Mode Configuration

| Setting | CONSERVATIVE | BALANCED | AGGRESSIVE |
|---------|--------------|----------|------------|
| LongEntryThreshold | 80 | 70 | 60 |
| ShortEntryThreshold | -80 | -70 | -60 |
| LongExitThreshold | 60 | 40 | 20 |
| ShortExitThreshold | -60 | -40 | -20 |
| TakeProfitAtrMultiplier | 1.5 | 2.0 | 2.5 |
| StopLossAtrMultiplier | 1.0 | 1.5 | 2.0 |
| MinSecondsBetweenTrades | 60 | 30 | 15 |
| AllowShort | false | true | true |
| AllowDirectionFlip | false | true | true |

### Indicator Score Calculation

```
+===========================================================================+
|  INDICATOR SCORING DETAILS                                                |
+===========================================================================+
|                                                                           |
|  VWAP POSITION (15% weight)                                              |
|  +--------------------------------------------------------------------+  |
|  |  Price 5% above VWAP --------------------------> +100 score        |  |
|  |  Price at VWAP --------------------------------> 0 score           |  |
|  |  Price 5% below VWAP --------------------------> -100 score        |  |
|  +--------------------------------------------------------------------+  |
|                                                                           |
|  EMA STACK (20% weight)                                                  |
|  +--------------------------------------------------------------------+  |
|  |  Price above ALL EMAs -------------------------> +100 score        |  |
|  |  Price above SOME EMAs ------------------------> proportional      |  |
|  |  Price below ALL EMAs -------------------------> -100 score        |  |
|  +--------------------------------------------------------------------+  |
|                                                                           |
|  RSI (15% weight)                                                        |
|  +--------------------------------------------------------------------+  |
|  |  RSI <= 30 (oversold) -------------------------> +100 score        |  |
|  |  RSI 30-50 ------------------------------------> -50 to 0          |  |
|  |  RSI 50-70 ------------------------------------> 0 to +50          |  |
|  |  RSI >= 70 (overbought) -----------------------> -100 score        |  |
|  +--------------------------------------------------------------------+  |
|                                                                           |
|  MACD (20% weight)                                                       |
|  +--------------------------------------------------------------------+  |
|  |  MACD > Signal (bullish) ----------------------> +50 base          |  |
|  |  MACD < Signal (bearish) ----------------------> -50 base          |  |
|  |  Histogram strength ----------------------------> +/- 50 bonus     |  |
|  +--------------------------------------------------------------------+  |
|                                                                           |
|  ADX (20% weight)                                                        |
|  +--------------------------------------------------------------------+  |
|  |  +DI > -DI + ADX value ------------------------> +magnitude        |  |
|  |  -DI > +DI + ADX value ------------------------> -magnitude        |  |
|  |  ADX 50 = max magnitude (100)                                      |  |
|  +--------------------------------------------------------------------+  |
|                                                                           |
|  VOLUME (10% weight)                                                     |
|  +--------------------------------------------------------------------+  |
|  |  Volume > avg + Price > VWAP ------------------> +magnitude        |  |
|  |  Volume > avg + Price < VWAP ------------------> -magnitude        |  |
|  |  Volume ratio confirms current direction                           |  |
|  +--------------------------------------------------------------------+  |
|                                                                           |
+===========================================================================+
```

### AutonomousTrading vs AdaptiveOrder

| Feature | AutonomousTrading | AdaptiveOrder |
|---------|-------------------|---------------|
| Entry Decision | AUTOMATIC (AI decides) | MANUAL (you set Entry) |
| Exit Decision | AUTOMATIC (AI decides) | SEMI-AUTO (adjusts TP/SL) |
| TP/SL Calculation | ATR-based automatic | Modifies your TP/SL |
| Direction | Can flip long<->short | Single direction only |
| Conditions Required | NONE (just Ticker) | You define conditions |
| Best For | Fully hands-off | Enhance manual strategy |

**Use AutonomousTrading when:** You want the AI to handle everything
**Use AdaptiveOrder when:** You have specific entry conditions but want smart exits

### Real-World Example: UBER Chart Analysis

```
+===========================================================================+
|  UBER AUTONOMOUS TRADING SIMULATION                                       |
|  Date: Feb 5, 2026 | LOD: $73.50 | HOD: $78.30 | Close: $74.28           |
+===========================================================================+

PRICE CHART (1-minute bars):
     $79 |
         |                    HOD
     $78 |                   *****
         |                  *     **
     $77 |                 *        **
         |                *           ***
     $76 |               *               **
  VWAP-->|==============*==================***===================
     $75 |             *                      ***
         |            *                          **
     $74 |           *                             ****  END
         |          *                                 **
     $73 |   LOD ***
         |________|_________|_________|_________|_________|______
              9:00      10:00     12:00     14:00     16:00

TRADING ACTIONS:

1. LOD DETECTION (~9:30 AM, $74.00)
   +----------------------------------------------------------------+
   | Score: +71 (crossing above +70 threshold)                      |
   | Indicators: RSI oversold bounce, +DI crossing above -DI        |
   | MACD bullish cross, EMA 9 reclaimed                            |
   +----------------------------------------------------------------+
   >>> ENTER LONG @ $74.50 | TP: $78.50 | SL: $73.00

2. HOD EXIT (~12:30 PM, $78.00)
   +----------------------------------------------------------------+
   | Score: +40 (dropped below +40 exit threshold)                  |
   | Indicators: RSI 78 overbought, MACD histogram shrinking        |
   | Momentum exhaustion detected                                   |
   +----------------------------------------------------------------+
   >>> EXIT LONG @ $78.00 | P&L: +$3.50/share (+4.7%)

3. DIRECTION FLIP (~1:00 PM, $76.50)
   +----------------------------------------------------------------+
   | Score: -72 (crossed below -70 threshold)                       |
   | Indicators: -DI > +DI, MACD bearish cross, below VWAP          |
   | AllowDirectionFlip: true                                       |
   +----------------------------------------------------------------+
   >>> ENTER SHORT @ $76.50 | TP: $73.50 | SL: $78.50

4. CLOSE EXIT (~3:30 PM, $74.50)
   +----------------------------------------------------------------+
   | Score: -28 (rose above -40 exit threshold)                     |
   | Indicators: RSI 35 approaching oversold, EMA support           |
   | Momentum slowing at support                                    |
   +----------------------------------------------------------------+
   >>> EXIT SHORT @ $74.50 | P&L: +$2.00/share (+2.6%)

TOTAL RESULT:
+==================================================================+
|  Trade 1: LONG  $74.50 -> $78.00  |  +$3.50/share  (+4.7%)       |
|  Trade 2: SHORT $76.50 -> $74.50  |  +$2.00/share  (+2.6%)       |
|  -----------------------------------------------------------------
|  TOTAL GAIN:                      |  +$5.50/share  (+7.3%)       |
|  On 100 shares:                   |  +$550 profit                |
+==================================================================+
```

### Key Signals That Drove Decisions

```
+===========================================================================+
|  SIGNAL BREAKDOWN                                                         |
+===========================================================================+

   LOD REVERSAL                         HOD EXHAUSTION
   +-----------------+                  +-----------------+
   | RSI: 28->45     |                  | RSI: 55->78     |
   | (oversold       |                  | (overbought     |
   |  bounce)        |                  |  warning)       |
   |                 |                  |                 |
   | +DI crosses     |                  | MACD histogram  |
   | above -DI       |                  | shrinking       |
   | (bullish)       |                  | (momentum loss) |
   |                 |                  |                 |
   | MACD bullish    |                  | Volume declining|
   | cross           |                  | on push higher  |
   +--------+--------+                  +--------+--------+
            |                                     |
            v                                     v
     SCORE: +71                            SCORE: +40
     ENTRY LONG                            EXIT LONG


   REVERSAL TO SHORT                    EXHAUSTION AT SUPPORT
   +-----------------+                  +-----------------+
   | -DI crosses     |                  | RSI: 35         |
   | above +DI       |                  | (approaching    |
   | (bearish)       |                  |  oversold)      |
   |                 |                  |                 |
   | MACD bearish    |                  | Price at EMA 21 |
   | cross           |                  | support level   |
   |                 |                  |                 |
   | Price below     |                  | Momentum slowing|
   | VWAP            |                  | at key level    |
   +--------+--------+                  +--------+--------+
            |                                     |
            v                                     v
     SCORE: -72                            SCORE: -28
     ENTRY SHORT                           EXIT SHORT
```

### Indicator Timeline Walkthrough

```
+===========================================================================+
|  PHASE 1: AT THE LOD (~$73.50)                                           |
+===========================================================================+
|  Indicator       |  Reading           |  Score Contribution              |
|-----------------+--------------------|----------------------------------|
|  VWAP Position   |  Price << VWAP     |  -80 (15%) = -12 pts             |
|  EMA Stack       |  Below all EMAs    |  -100 (20%) = -20 pts            |
|  RSI             |  ~28 (oversold!)   |  +100 (15%) = +15 pts            |
|  MACD            |  Bearish cross     |  -60 (20%) = -12 pts             |
|  ADX             |  ~25, -DI > +DI    |  -50 (20%) = -10 pts             |
|  Volume          |  High selling      |  -40 (10%) = -4 pts              |
|-----------------+--------------------|----------------------------------|
|  TOTAL SCORE     |                    |  -43 (no action yet)             |
+===========================================================================+
|  Note: RSI oversold is hinting at reversal approaching                   |
+===========================================================================+

+===========================================================================+
|  PHASE 2: REVERSAL CONFIRMED (~$74.50)                                   |
+===========================================================================+
|  Indicator       |  Change                  |  New Score                  |
|-----------------|--------------------------|------------------------------|
|  VWAP Position   |  Still below but rising  |  -40 = -6 pts               |
|  EMA Stack       |  Crossing above EMA 9    |  +33 = +7 pts               |
|  RSI             |  32 -> 50 (recovering)   |  +60 = +9 pts               |
|  MACD            |  BULLISH CROSS!          |  +80 = +16 pts              |
|  ADX             |  +DI crossing above -DI! |  +80 = +16 pts              |
|  Volume          |  Buying volume spike     |  +80 = +8 pts               |
|-----------------|--------------------------|------------------------------|
|  TOTAL SCORE     |  BREAKTHROUGH!           |  +71 (ENTRY LONG!)          |
+===========================================================================+

+===========================================================================+
|  PHASE 3: APPROACHING HOD (~$78.00)                                      |
+===========================================================================+
|  Indicator       |  Reading             |  Score Contribution            |
|-----------------|---------------------|----------------------------------|
|  VWAP Position   |  +4% above VWAP      |  +80 = +12 pts                 |
|  EMA Stack       |  Extended above all  |  +100 = +20 pts                |
|  RSI             |  78 (OVERBOUGHT!)    |  -27 = -4 pts                  |
|  MACD            |  Histogram declining |  +20 = +4 pts                  |
|  ADX             |  ADX 45, +DI weak    |  +30 = +6 pts                  |
|  Volume          |  Declining on push   |  +20 = +2 pts                  |
|-----------------|---------------------|----------------------------------|
|  TOTAL SCORE     |  EXHAUSTION          |  +40 (EXIT LONG!)              |
+===========================================================================+
```

---

### AutonomousTrading Learning System

AutonomousTrading builds a **TickerProfile** for each symbol over time, learning what works specifically for that stock.

#### What It Learns

| Category | Details |
|----------|---------|
| **Entry Thresholds** | Optimal score level with best win rate for this ticker |
| **Exit Thresholds** | When to exit for maximum profit |
| **Time-of-Day Patterns** | Best hours/minutes to trade (15-min buckets) |
| **Indicator Correlations** | Which signals work best (RSI oversold, MACD cross, etc.) |
| **Win Rate by Score** | Bucketed by entry score level |
| **Streak Awareness** | More conservative after loss streaks |

#### How It Adapts

```
+===========================================================================+
|  LEARNING PROGRESSION                                                     |
+===========================================================================+
|                                                                           |
|  TRADES      CONFIDENCE    ADAPTATION                                     |
|  ------      ----------    ----------                                     |
|  0-9         0-18%         Use default thresholds only                   |
|  10-19       20-38%        Start blending learned thresholds             |
|  20-49       40-98%        Avoid poor time windows, adjust thresholds    |
|  50+         100%          Full confidence in learned patterns           |
|                                                                           |
|  Blending Formula:                                                       |
|  threshold = default * (1 - confidence) + learned * confidence           |
|                                                                           |
+===========================================================================+
```

#### TickerProfile Structure

```csharp
public class TickerProfile
{
    // Overall Statistics
    public int TotalTrades { get; set; }
    public int TotalWins { get; set; }
    public double WinRate => TotalWins / TotalTrades * 100;
    public double NetProfit { get; set; }
    public int Confidence => Math.Min(100, TotalTrades * 2);

    // Learned Optimal Values
    public int OptimalLongEntryThreshold { get; set; }  // e.g., 72 instead of 70
    public int OptimalShortEntryThreshold { get; set; } // e.g., -68 instead of -70

    // Time Windows
    public Dictionary<string, TimeWindowStats> TimeWindowStats { get; set; }

    // Indicator Correlations
    public List<IndicatorCorrelation> IndicatorCorrelations { get; set; }
    // e.g., "RSI Oversold" -> 82% win rate for AAPL
}
```

#### Persistence

Profiles are automatically saved to:
```
%APPDATA%\IdiotProof\TickerProfiles\SYMBOL.json
```

Example profile file:
```json
{
  "symbol": "AAPL",
  "totalTrades": 47,
  "totalWins": 31,
  "winRate": 65.9,
  "netProfit": 1247.50,
  "optimalLongEntryThreshold": 72,
  "optimalShortEntryThreshold": -68,
  "timeWindowStats": {
    "09:30": { "winRate": 71.4, "totalTrades": 7 },
    "10:00": { "winRate": 58.3, "totalTrades": 12 },
    "14:00": { "winRate": 38.5, "totalTrades": 13 }
  }
}
```

#### Profile Adjustments

| Metric | Effect |
|--------|--------|
| Historical win rate > 60% | Slightly more aggressive entries |
| Loss streak >= 3 | Temporarily more conservative |
| Win rate at threshold 80 > 70 | Use 80 for this ticker |
| Time window win rate < 40% | Skip entries in that window |
| RSI oversold entries win 82% | Weight RSI higher for this ticker |

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
ΓöîΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÉ
Γöé  FLUENT API DEFAULTS - QUICK REFERENCE                              Γöé
Γö£ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöñ
Γöé  Stock Builder:                                                     Γöé
Γöé    Exchange = "SMART"    Currency = "USD"    Enabled = true         Γöé
Γöé    StartTime = null      EndTime = null      Session = null         Γöé
Γö£ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöñ
Γöé  Order Methods (Long/Short with chained config):                    Γöé
Γöé    Quantity = required (must be set)                                Γöé
Γöé    PriceType = Price.Current                                        Γöé
Γöé    OrderType = OrderType.Limit                                      Γöé
Γöé    OutsideRTH = true                                                Γöé
Γöé    TakeProfitOutsideRTH = true                                      Γöé
Γö£ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöñ
Γöé  Strategy Builder (after Long/Short):                               Γöé
Γöé    TimeInForce = GTC     AllOrNone = false                          Γöé
Γöé    TakeProfit = null     StopLoss = null      TrailingStop = off    Γöé
Γöé    ExitStrategy = null   IsProfitable = false                       Γöé
Γöé    AdaptiveOrder = null (disabled)                                  Γöé
Γö£ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöñ
Γöé  Condition Defaults:                                                Γöé
Γöé    IsAboveVwap/IsBelowVwap buffer = 0                               Γöé
Γöé    IsRsi threshold = null (70/30)                                   Γöé
Γöé    IsAdx threshold = 25                                             Γöé
Γöé    IsDI minDifference = 0                                           Γöé
Γöé    IsHigherLows lookbackBars = 3                                    Γöé
Γö£ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöñ
Γöé  ADX TakeProfit Defaults:                                           Γöé
Γöé    WeakThreshold = 15    DevelopingThreshold = 25                   Γöé
Γöé    StrongThreshold = 35  ExitOnRollover = true                      Γöé
Γö£ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöñ
Γöé  ATR StopLoss Defaults:                                             Γöé
Γöé    Period = 14           IsTrailing = true                          Γöé
Γöé    MinStopPercent = 1%   MaxStopPercent = 25%                       Γöé
Γö£ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöñ
Γöé  AdaptiveOrder Mode Defaults:                                       Γöé
Γöé    Mode = Balanced       MaxTPExtension = 50%                       Γöé
Γöé    MaxTPReduction = 50%  EmergencyExitThreshold = -70               Γöé
ΓööΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÿ
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
| `Atr.Tight` | 1.5├ù | Scalping, quick trades - more stops, smaller losses |
| `Atr.Balanced` | 2.0├ù | Swing trading - recommended default |
| `Atr.Loose` | 3.0├ù | Trend following - fewer stops, larger swings |
| `Atr.VeryLoose` | 4.0├ù | Long-term positions - maximum room |

**Custom Multipliers:**

```csharp
Atr.Multiplier(2.5)                    // 2.5├ù ATR
Atr.Multiplier(2.0, period: 20)        // 2├ù ATR with 20-period calculation
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

| ATR | Multiplier | Stop Distance | High Water Mark $50 ΓåÆ Stop |
|-----|------------|---------------|----------------------------|
| $1.00 | 2.0├ù | $2.00 | $48.00 |
| $1.50 | 2.0├ù | $3.00 | $47.00 |
| $2.00 | 2.0├ù | $4.00 | $46.00 |
| $1.00 | 3.0├ù | $3.00 | $47.00 |

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
┬ª  PRICE TYPE REFERENCE                                                  ┬ª
+------------------------------------------------------------------------┬ª
┬ª                                                                        ┬ª
┬ª  CURRENT (Default)                                                     ┬ª
┬ª  ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ                                                     ┬ª
┬ª  Best for:  Fast execution when you need to enter/exit immediately    ┬ª
┬ª  Risk:      May experience slippage in fast-moving or illiquid mkts   ┬ª
┬ª  Use case:  Breakout entries, stop loss exits, time-sensitive trades  ┬ª
┬ª                                                                        ┬ª
┬ª  VWAP                                                                  ┬ª
┬ª  ΓöÇΓöÇΓöÇΓöÇ                                                                  ┬ª
┬ª  Best for:  Getting a fair average price over the session             ┬ª
┬ª  Risk:      May not fill if price moves away from VWAP                ┬ª
┬ª  Note:      VWAP resets at market open each day                       ┬ª
┬ª  Warning:   Unreliable in pre-market/after-hours (low volume)         ┬ª
┬ª  Use case:  Mean-reversion strategies, institutional-style entries    ┬ª
┬ª                                                                        ┬ª
┬ª  BID                                                                   ┬ª
┬ª  ΓöÇΓöÇΓöÇ                                                                   ┬ª
┬ª  Best for:  Selling with limit order at current bid                   ┬ª
┬ª  Risk:      May not fill if bid drops before execution                ┬ª
┬ª  Use case:  Exiting long positions at best available buyer price      ┬ª
┬ª                                                                        ┬ª
┬ª  ASK                                                                   ┬ª
┬ª  ΓöÇΓöÇΓöÇ                                                                   ┬ª
┬ª  Best for:  Buying with limit order at current ask                    ┬ª
┬ª  Risk:      May not fill if ask rises before execution                ┬ª
┬ª  Use case:  Entering long positions at best available seller price    ┬ª
┬ª                                                                        ┬ª
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
    .TrailingStopLoss(Atr.Balanced)    // 2.0├ù ATR trailing stop
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
    .TrailingStopLoss(Atr.Multiplier(2.5))    // Custom 2.5├ù ATR
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

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | Γ£à | `PriceConditions.cs` - `PullbackCondition` |
| Stock.Pullback() method | Γ£à | `Stock.cs` |
| Parser regex | Γ£à | `IdiotScriptParser.cs` - `PULLBACK(?:\((\$?[\d.]*)\))?` |
| Segment conversion | Γ£à | `IdiotScriptParser.cs` - `SegmentType.Pullback` |
| State machine runner | Γ£à | `PullbackRunner.cs` |
| Validator | Γ£à | `ValidCommands` includes `"PULLBACK"` |

**Evaluation:** `currentPrice <= Level`

**State Machine (PullbackRunner):**
1. **WaitingForBreakout** - Wait for price >= BreakoutLevel
2. **WaitingForPullback** - Wait for price <= PullbackLevel  
3. **WaitingForVwapReclaim** - Wait for price >= VWAP + buffer
4. **OrderSubmitted/Done** - Entry filled

---

### IsAboveVwap() Implementation

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | Γ£à | `PriceConditions.cs` - `AboveVwapCondition` |
| Stock.IsAboveVwap() method | Γ£à | `Stock.cs` |
| Parser matching | Γ£à | `IdiotScriptParser.cs` - handles `ABOVEVWAP`, `VWAP`, `ISABOVEVWAP` |
| Validator | Γ£à | `ValidCommands` includes `"ABOVEVWAP"`, `"ISABOVEVWAP"` |

**Evaluation:** `vwap > 0 && currentPrice >= (vwap + Buffer)`

**Note:** VWAP-based condition - doesn't need a calculator or warm-up period. VWAP is passed directly to `Evaluate(price, vwap)` by the `StrategyRunner`.

---

### IsCloseAboveVwap() Implementation

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | Γ£à | `IndicatorConditions.cs` - `CloseAboveVwapCondition` |
| Stock.IsCloseAboveVwap() method | Γ£à | `Stock.cs` |
| Parser regex | Γ£à | `IdiotScriptParser.cs` - `^(?:IS)?CLOSEABOVEVWAP(?:\(\))?$` |
| StrategyRunner wire-up | Γ£à | `StrategyRunner.cs` - `GetLastClose` callback to `CandlestickAggregator` |
| Validator | Γ£à | `ValidCommands` includes `"CLOSEABOVEVWAP"`, `"ISCLOSEABOVEVWAP"` |

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

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Fluent API method | Γ£à | `Stock.cs` - `Long() => Order(OrderDirection.Long)` |
| Order() method | Γ£à | `Stock.cs` - creates `StrategyBuilder` |
| Parser matching | Γ£à | `IdiotScriptParser.cs` - handles `LONG`, `LONG()`, `IS.LONG` |
| Validator | Γ£à | `ValidCommands` includes `"LONG"` |

**Equivalent Forms:**
```idiotscript
.Long()
.Order(IS.LONG)
.Order()           # defaults to LONG
```

---

### TakeProfit() Implementation

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Fluent API method | Γ£à | `Stock.cs` - `TakeProfit(double price)` |
| Parser regex | Γ£à | `IdiotScriptParser.cs` - `^(?:TP\|TAKEPROFIT)\((\$?[\d.]+)\)$` |
| Parser method | Γ£à | `IdiotScriptParser.cs` - `TryParseTakeProfit()` |
| StrategyRunner tracking | Γ£à | `StrategyRunner.cs` - `_takeProfitOrderId`, `_takeProfitFilled` |
| Order submission | Γ£à | `StrategyRunner` submits TP limit orders after entry fills |
| AdaptiveOrder integration | Γ£à | Dynamic TP adjustment with `_currentAdaptiveTakeProfitPrice` |
| Validator | Γ£à | `ValidCommands` includes `"TP"`, `"TAKEPROFIT"` |

**Supported Syntax:**
```idiotscript
.TakeProfit(160)      # Fixed price
.TP(160)              # Alias
.TP($160)             # With dollar sign
```

---

### StopLoss() Implementation

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Fluent API method | Γ£à | `Stock.cs` - `StopLoss(double price)` |
| Parser regex | Γ£à | `IdiotScriptParser.cs` - `^(?:SL\|STOPLOSS)\((\$?[\d.]+)\)$` |
| Parser method | Γ£à | `IdiotScriptParser.cs` - `TryParseStopLoss()` |
| StrategyRunner tracking | Γ£à | `StrategyRunner.cs` - `_stopLossOrderId`, `_stopLossFilled` |
| Order submission | Γ£à | `StrategyRunner` submits SL stop orders after entry fills |
| AdaptiveOrder integration | Γ£à | Dynamic SL adjustment with `_currentAdaptiveStopLossPrice` |
| Validator | Γ£à | `ValidCommands` includes `"SL"`, `"STOPLOSS"` |

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

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Fluent API method | Γ£à | `Stock.cs` - `IsProfitable()` |
| Parser regex | Γ£à | `IdiotScriptParser.cs` - `^(?:IS)?PROFITABLE(?:\(\))?$` |
| Parser method | Γ£à | `IdiotScriptParser.cs` - `TryParseIsProfitable()` |
| OrderAction property | Γ£à | `OrderAction.cs` - `ClosePositionOnlyIfProfitable` |
| StrategyRunner logic | Γ£à | `StrategyRunner.cs` - checks `isProfitable` before closing |
| Validator | Γ£à | `ValidCommands` includes `"PROFITABLE"`, `"ISPROFITABLE"` |

**Usage:** Chain with `ExitStrategy()` to only exit if the position is profitable at the specified time.

**Supported Syntax:**
```idiotscript
.ExitStrategy(IS.BELL).IsProfitable()    # Exit at bell only if profitable
.ExitStrategy(15:30).IsProfitable()       # Exit at 3:30 PM only if profitable
```

**How it works:**
1. At the scheduled exit time, checks if `currentPrice > entryPrice` (for longs)
2. If profitable ΓåÆ closes position
3. If not profitable ΓåÆ holds position (logs "Position NOT profitable")

---

### Qty() / Quantity() Implementation

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Fluent API method | Γ£à | `Stock.cs` - `Quantity(int)` and `Qty(int)` alias |
| Parser regex | Γ£à | `IdiotScriptParser.cs` - `^(?:QTY\|QUANTITY)\((\d+)\)$` |
| Validator | Γ£à | `ValidCommands` includes `"QTY"`, `"QUANTITY"` |

**Note:** Both `Qty()` and `Quantity()` are aliases that do the same thing. Use whichever you prefer.

---

### IsEmaAbove() Implementation

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | Γ£à | `IndicatorConditions.cs` - `EmaAboveCondition` |
| Stock.IsEmaAbove() method | Γ£à | `Stock.cs` |
| Parser regex | Γ£à | `IdiotScriptParser.cs` - `^(?:IS)?EMAABOVE\((\d+)\)$` |
| Calculator class | Γ£à | `EmaCalculator.cs` |
| StrategyRunner wire-up | Γ£à | `GetOrCreateEmaCalculator()` - `GetEmaValue` callback |
| Validator | Γ£à | `ValidCommands` includes `"EMAABOVE"`, `"ISEMAABOVE"` |

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

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | Γ£à | `IndicatorConditions.cs` - `MacdCondition` |
| Stock.IsMacdBullish() method | Γ£à | `Stock.cs` - alias for `IsMacd(MacdState.Bullish)` |
| Parser regex | Γ£à | `IdiotScriptParser.cs` - `^(?:IS)?MACDBULLISH(?:\(\))?$` |
| Calculator class | Γ£à | `MacdCalculator.cs` |
| StrategyRunner wire-up | Γ£à | `GetMacdValues` callback returns `(MacdLine, SignalLine, Histogram, PreviousHistogram)` |
| Validator | Γ£à | `ValidCommands` includes `"MACDBULLISH"`, `"ISMACDBULLISH"` |

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

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | Γ£à | `IndicatorConditions.cs` - `MomentumAboveCondition` |
| Stock.IsMomentumAbove() method | Γ£à | `Stock.cs` |
| Parser regex | Γ£à | `IdiotScriptParser.cs` - `^(?:IS)?MOMENTUMABOVE\((-?\d+(?:\.\d+)?)\)$` |
| Calculator class | Γ£à | `MomentumCalculator.cs` |
| StrategyRunner wire-up | Γ£à | `GetMomentumValue` callback to `MomentumCalculator.CurrentValue` |
| Validator | Γ£à | `ValidCommands` includes `"MOMENTUMABOVE"`, `"ISMOMENTUMABOVE"` |

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

**Status:** Γ£à Fully Implemented

| Component | Status | Location |
|-----------|--------|----------|
| Config class | Γ£à | `AdaptiveOrderConfig.cs` |
| Stock.AdaptiveOrder() method | Γ£à | `Stock.cs` - accepts mode string or `AdaptiveOrderConfig` |
| Parser regex | Γ£à | `IdiotScriptParser.cs` - `^(?:IS)?ADAPTIVEORDER(?:\(([A-Za-z0-9_.]*)\))?$` |
| Parser method | Γ£à | `IdiotScriptParser.cs` - `TryParseAdaptiveOrder()` |
| StrategyRunner logic | Γ£à | `MonitorAdaptiveOrder()` - calculates market score, adjusts TP/SL |
| Score calculation | Γ£à | `CalculateMarketScore()` - uses VWAP, EMA, RSI, MACD, ADX, Volume |
| TP/SL multipliers | Γ£à | `CalculateTakeProfitMultiplier()`, `CalculateStopLossMultiplier()` |
| Validator | Γ£à | `ValidCommands` includes `"ADAPTIVEORDER"`, `"ISADAPTIVEORDER"` |

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

Γ£à **Fully Implemented**

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
Candle 3: Low = $152 (higher) ΓåÆ Γ£ô IsHigherLows() = TRUE
```

**Visual:**
```
                         /\
             /\         /  \
   /\       /  \       /    \
  /  \     /    \     /      \
 /    \___/  Γåæ   \___/        \
       Higher    Higher
         Low       Low
```

---

### Indicator Condition Implementation Pattern

All indicator conditions follow the same pattern. Here's `IsMomentumAbove()` as an example:

| Component | Status | Location |
|-----------|--------|----------|
| Condition class | Γ£à | `IndicatorConditions.cs` - `MomentumAboveCondition` |
| Stock method | Γ£à | `Stock.cs` - `IsMomentumAbove(double threshold)` |
| Calculator class | Γ£à | `MomentumCalculator.cs` |
| StrategyRunner wire-up | Γ£à | `InitializeIndicatorCalculators()` - `GetMomentumValue` callback |
| Parser matching | Γ£à | `IdiotScriptParser.cs` |
| Validator | Γ£à | `ValidCommands` includes `"MOMENTUMABOVE"`, `"ISMOMENTUMABOVE"` |

**Required Callback Wire-up in StrategyRunner:**
```csharp
case MomentumAboveCondition momentumAbove:
    momentumAbove.GetMomentumValue = () => _momentumCalculator?.Value ?? 0;
    break;
```

---

## Implementation Status

### Γ£à Fully Implemented

| Feature | Builder | StrategyRunner | IdiotScript |
|---------|---------|----------------|-------------|
| **Price Conditions** |
| Breakout condition | Γ£à | Γ£à | `Breakout()`, `Entry()` |
| Pullback condition | Γ£à | Γ£à | `Pullback()` |
| PriceAbove condition | Γ£à | Γ£à | `Entry()`, `IsPriceAbove()` |
| PriceBelow condition | Γ£à | Γ£à | `IsPriceBelow()` |
| Custom condition (When) | Γ£à | Γ£à | N/A |
| **VWAP Conditions** |
| AboveVwap condition | Γ£à | Γ£à | `IsAboveVwap()` |
| BelowVwap condition | Γ£à | Γ£à | `IsBelowVwap()` |
| CloseAboveVwap condition | Γ£à | Γ£à | `IsCloseAboveVwap()` |
| VwapRejection condition | Γ£à | Γ£à | `IsVwapRejection()` |
| **EMA Conditions** |
| EmaAbove condition | Γ£à | Γ£à | `IsEmaAbove()` |
| EmaBelow condition | Γ£à | Γ£à | `IsEmaBelow()` |
| EmaBetween condition | Γ£à | Γ£à | `IsEmaBetween()` |
| EmaTurningUp condition | Γ£à | Γ£à | `IsEmaTurningUp()` |
| **Momentum Conditions** |
| MomentumAbove condition | Γ£à | Γ£à | `IsMomentumAbove()` |
| MomentumBelow condition | Γ£à | Γ£à | `IsMomentumBelow()` |
| RocAbove condition | Γ£à | Γ£à | `IsRocAbove()` |
| RocBelow condition | Γ£à | Γ£à | `IsRocBelow()` |
| **Trend Indicators** |
| RSI condition | Γ£à | Γ£à | `IsRsiOversold()`, `IsRsiOverbought()` |
| ADX condition | Γ£à | Γ£à | `IsAdxAbove()` |
| DI condition | Γ£à | Γ£à | `IsDiPositive()`, `IsDiNegative()` |
| MACD condition | Γ£à | Γ£à | `IsMacdBullish()`, `IsMacdBearish()` |
| **Pattern Conditions** |
| HigherLows condition | Γ£à | Γ£à | `IsHigherLows()` |
| VolumeAbove condition | Γ£à | Γ£à | `IsVolumeAbove()` |
| **Gap Conditions** |
| GapUp condition | Γ£à | Γ£à | `IsGapUp()` |
| GapDown condition | Γ£à | Γ£à | `IsGapDown()` |
| **Orders** |
| Long order | Γ£à | Γ£à | `Long()`, `Order(IS.LONG)` |
| Short order | Γ£à | Γ£à | `Short()`, `Order(IS.SHORT)` |
| Close/CloseLong/CloseShort | Γ£à | Γ£à | `CloseLong()`, `CloseShort()` |
| Quantity | Γ£à | Γ£à | `Qty()`, `Quantity()` |
| **Exit Strategies** |
| Take profit (fixed) | Γ£à | Γ£à | `TakeProfit()`, `TP()` |
| Take profit (ADX-based) | Γ£à | Γ£à | `TakeProfitRange()` |
| Stop loss | Γ£à | Γ£à | `StopLoss()`, `SL()` |
| Trailing stop loss (%) | Γ£à | Γ£à | `TrailingStopLoss()`, `TSL()` |
| Trailing stop loss (ATR) | Γ£à | Γ£à | `TrailingStopLoss(IS.ATR)` |
| ExitStrategy time | Γ£à | Γ£à | `ExitStrategy(IS.BELL)` |
| IsProfitable | Γ£à | Γ£à | `.IsProfitable()` |
| **Smart Order Management** |
| AdaptiveOrder | Γ£à | Γ£à | `AdaptiveOrder()`, `IsAdaptiveOrder()` |
| **Order Configuration** |
| TimeInForce | Γ£à | Γ£à | `TimeInForce()` |
| OutsideRTH | Γ£à | Γ£à | `OutsideRTH()` |
| TakeProfitOutsideRTH | Γ£à | Γ£à | `TakeProfitOutsideRTH()` |
| OrderType | Γ£à | Γ£à | `OrderType()` |
| PriceType | Γ£à | Γ£à | `PriceType()` |
| AllOrNone | Γ£à | Γ£à | `AllOrNone()` |
| Repeat | Γ£à | Γ£à | `Repeat()` |
| Enabled/Disabled | Γ£à | Γ£à | `Enabled()` |
| **Infrastructure** |
| SessionDuration | Γ£à | Γ£à | `Session()` |
| TradingSession enum | Γ£à | Γ£à | `IS.PREMARKET`, etc. |
| Exchange (SMART/Pink) | Γ£à | Γ£à | N/A |
| VWAP calculation | N/A | Γ£à | N/A |
| ATR calculation | N/A | Γ£à | N/A |
| Historical warm-up | N/A | Γ£à | N/A |
| **Offline Backtesting** | Γ£à | N/A | N/A |

### Indicator Calculator Status

| Calculator | File | Warm-up Bars | Status |
|------------|------|--------------|--------|
| EmaCalculator | `EmaCalculator.cs` | N bars | Γ£à |
| AdxCalculator | `AdxCalculator.cs` | 28 bars | Γ£à |
| RsiCalculator | `RsiCalculator.cs` | 15 bars | Γ£à |
| MacdCalculator | `MacdCalculator.cs` | 35 bars | Γ£à |
| MomentumCalculator | `MomentumCalculator.cs` | 11 bars | Γ£à |
| RocCalculator | `RocCalculator.cs` | 11 bars | Γ£à |
| VolumeCalculator | `VolumeCalculator.cs` | 20 bars | Γ£à |
| AtrCalculator | `AtrCalculator.cs` | 14 bars | Γ£à |
| CandlestickAggregator | `CandlestickAggregator.cs` | 1 bar | Γ£à |

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
ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
Γòæ  BACKTEST RESULTS: AAPL                                           Γòæ
ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
Γòæ  Period: 2024-01-15 04:00 to 2024-01-15 04:53                     Γòæ
Γòæ  Bars:   54                                                       Γòæ
ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
Γòæ  TRADES                                                           Γòæ
Γòæ  Total:          3        (2W / 1L)                               Γòæ
Γòæ  Win Rate:       66.7%                                            Γòæ
Γòæ  Profit Factor:  2.50                                             Γòæ
ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
Γòæ  PROFIT & LOSS                                                    Γòæ
Γòæ  Gross Profit:   $  2,500.00                                      Γòæ
Γòæ  Gross Loss:     $  1,000.00                                      Γòæ
Γòæ  Net P&L:        $  1,494.00                                      Γòæ
ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
Γòæ  RISK METRICS                                                     Γòæ
Γòæ  Max Drawdown:   $    500.00 (0.5%)                               Γòæ
Γòæ  Final Equity:   $101,494.00                                      Γòæ
Γòæ  Return:         1.49%                                            Γòæ
ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
```

### Using IB Gateway for Historical Data

To fetch real historical data from IB Gateway:

```
ΓòöΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòù
Γòæ  IB GATEWAY SETUP FOR HISTORICAL DATA                             Γòæ
ΓòáΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòú
Γòæ                                                                   Γòæ
Γòæ  1. Start IB Gateway (not TWS)                                    Γòæ
Γòæ     - Paper Trading: Port 4002                                    Γòæ
Γòæ     - Live Trading:  Port 4001                                    Γòæ
Γòæ                                                                   Γòæ
Γòæ  2. Enable API in Gateway:                                        Γòæ
Γòæ     Configure ΓåÆ Settings ΓåÆ API ΓåÆ Settings                         Γòæ
Γòæ     x Enable ActiveX and Socket Clients                           Γòæ
Γòæ     x Read-Only API (recommended for backtesting)                 Γòæ
Γòæ                                                                   Γòæ
Γòæ  3. Use reqHistoricalData() to fetch bars:                        Γòæ
Γòæ                                                                   Γòæ
Γòæ     client.reqHistoricalData(                                     Γòæ
Γòæ         reqId: 1,                                                 Γòæ
Γòæ         contract: contract,                                       Γòæ
Γòæ         endDateTime: "",           // Empty = now                 Γòæ
Γòæ         durationStr: "1 D",        // 1 day of data               Γòæ
Γòæ         barSizeSetting: "1 min",   // 1-minute bars               Γòæ
Γòæ         whatToShow: "TRADES",                                     Γòæ
Γòæ         useRTH: 0,                 // Include extended hours      Γòæ
Γòæ         formatDate: 1,                                            Γòæ
Γòæ         keepUpToDate: false,                                      Γòæ
Γòæ         chartOptions: null                                        Γòæ
Γòæ     );                                                            Γòæ
Γòæ                                                                   Γòæ
Γòæ  4. Handle data in IbWrapper:                                     Γòæ
Γòæ     - historicalData(int reqId, Bar bar)                          Γòæ
Γòæ     - historicalDataEnd(int reqId, string start, string end)      Γòæ
Γòæ                                                                   Γòæ
Γòæ  PACING RULES:                                                    Γòæ
Γòæ  ΓÇó Max 60 requests per 10 minutes                                 Γòæ
Γòæ  ΓÇó Wait 15+ seconds between identical requests                    Γòæ
Γòæ                                                                   Γòæ
ΓòÜΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓòÉΓò¥
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
| Strategy state | Γ£à Yes | Condition progress, step tracking |
| Open positions | Γ£à Yes | IBKR maintains these server-side |
| Pending orders | Γ£à Yes | IBKR maintains these server-side |
| Trailing stop levels | Γ£à Yes | High-water marks are preserved |
| Market data subscriptions | ΓÜá∩╕Å Maybe | Depends on error code (1101 vs 1102) |

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

**Configure ΓåÆ Settings ΓåÆ API ΓåÆ Settings:**

| Setting | Recommended | Description |
|---------|-------------|-------------|
| Enable ActiveX and Socket Clients | Γ£à Checked | Required for API connections |
| Socket port | 4001 (live) / 4002 (paper) | Must match `Settings.Port` |
| Allow connections from localhost only | Γ£à Checked | Security best practice |
| Read-Only API | Γ¥î Unchecked | Needed for order submission |

### Stability Settings

**Configure ΓåÆ Settings ΓåÆ Lock and Exit:**

| Setting | Recommended | Description |
|---------|-------------|-------------|
| Auto restart | Γ£à Enable | Gateway restarts after crashes |
| Auto logon | Γ£à Enable | Reconnects after restarts |
| Store settings on server | Γ£à Enable | Preserves settings across reinstalls |

**Configure ΓåÆ Settings ΓåÆ General ΓåÆ Trading Mode:**

| Setting | Recommended | Description |
|---------|-------------|-------------|
| Paper or Live | As needed | Must match your intention |
| API Settings preserved | Γ£à Yes | Survives restarts |

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
