# IdiotProof Trading Strategy Bot

A fluent API framework for building multi-stage trading strategies with Interactive Brokers (IBKR).

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Running in Release Mode](#running-in-release-mode)
- [Fluent API Reference](#fluent-api-reference)
  - [Strategy Builder Methods](#strategy-builder-methods)
  - [Condition Methods](#condition-methods)
  - [Order Methods](#order-methods)
  - [Exit Strategy Methods](#exit-strategy-methods)
  - [Timing Methods](#timing-methods)
  - [Configuration Methods](#configuration-methods)
- [Helper Classes](#helper-classes)
- [Examples](#examples)
- [Implementation Status](#implementation-status)

---

## Overview

IdiotProof provides a clean, readable fluent API for defining complex trading strategies that execute automatically when conditions are met.

**Key Features:**
- Multi-step condition chains (breakout → pullback → VWAP confirmation)
- Automatic order execution with IBKR
- Take profit and stop loss management
- Trailing stop loss with high-water mark tracking
- Time-based strategy windows
- Pre-market, RTH, and after-hours support

---

## Quick Start

```csharp
var strategy = Stock
    .Ticker("AAPL")
    .SessionDuration(TradingSession.PreMarketEndEarly)  // 4:00 AM - 9:20 AM ET
    .Breakout(150.00)                         // Step 1: Price >= 150.00
    .Pullback(148.50)                         // Step 2: Price <= 148.50
    .AboveVwap()                              // Step 3: Price >= VWAP
    .Buy(quantity: 100, Price.Current)        // Step 4: Buy 100 shares
    .TakeProfit(155.00)                       // Exit at $155.00
    .StopLoss(147.00)                         // Stop loss at $147.00
    .TrailingStopLoss(Percent.Ten)            // 10% trailing stop
    .ClosePosition(MarketTime.PreMarket.Ending)  // Close at 9:20 AM ET
    .Build();
```

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

#### `.AboveVwap(double buffer = 0)`
Triggers when price is at or above VWAP plus optional buffer.

```csharp
.AboveVwap()           // Price >= VWAP
.AboveVwap(0.02)       // Price >= VWAP + $0.02
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.BelowVwap(double buffer = 0)`
Triggers when price is at or below VWAP minus optional buffer.

```csharp
.BelowVwap()           // Price <= VWAP
.BelowVwap(0.05)       // Price <= VWAP - $0.05
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.PriceAbove(double level)`
Triggers when price is strictly above the level (not equal).

```csharp
.PriceAbove(10.00)     // Price > $10.00
```

**Implementation Status:** ✅ Fully Implemented

---

#### `.PriceBelow(double level)`
Triggers when price is strictly below the level (not equal).

```csharp
.PriceBelow(9.50)      // Price < $9.50
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
Enables trailing stop loss that follows price upward.

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
    .AboveVwap()
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
    .PriceAbove(2.40)
    .AboveVwap()
    .Buy(quantity: 208, Price.Current)
    .TakeProfit(4.00, 4.80)                    // ADX-based: $4.00 (weak) to $4.80 (strong)
    .ClosePosition(MarketTime.PreMarket.Ending)
    .Build();
```

### Full-Featured Strategy

```csharp
Stock
    .Ticker("AAPL")
    .SessionDuration(TradingSession.PreMarket)
    .Breakout(150.00)
    .Pullback(148.00)
    .AboveVwap(buffer: 0.02)
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
    .PriceAbove(0.88)
    .AboveVwap()
    .Buy(quantity: 568, Price.Current)
    .TakeProfit(1.30, 1.70)
    .Build();
```

### Closing a Position Strategy

```csharp
Stock
    .Ticker("AAPL")
    .PriceAbove(155)                           // When price hits target
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
    .AboveVwap()
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
| Trailing stop loss | ✅ | ✅ |
| TimeInForce | ✅ | ✅ |
| OutsideRTH | ✅ | ✅ |
| OrderType | ✅ | ✅ |
| AllOrNone | ✅ | ✅ |
| Enabled/Disabled | ✅ | ✅ |
| SessionDuration | ✅ | ⚠️ |
| TradingSession enum | ✅ | ⚠️ |
| Exchange (SMART/Pink) | ✅ | ✅ |
| VWAP calculation | N/A | ✅ |
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
