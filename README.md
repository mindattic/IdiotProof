# IdiotProof Trading Strategy Bot

A fluent API framework for building multi-stage trading strategies with Interactive Brokers (IBKR).

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
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
    .Start(Time.PreMarket.Start)              // Start monitoring at 4:00 AM ET
    .Breakout(150.00)                         // Step 1: Price >= 150.00
    .Pullback(148.50)                         // Step 2: Price <= 148.50
    .AboveVwap()                              // Step 3: Price >= VWAP
    .Buy(quantity: 100, Price.Current)        // Step 4: Buy 100 shares
    .TakeProfit(155.00)                       // Exit at $155.00
    .StopLoss(147.00)                         // Stop loss at $147.00
    .TrailingStopLoss(Percent.Ten)            // 10% trailing stop
    .ClosePosition(Time.PreMarket.End.AddMinutes(-10))  // Close at 9:20 AM ET
    .End(Time.PreMarket.End);                 // Stop monitoring at 9:30 AM ET
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

**Default:** `OrderType.Market` (for new API), `OrderType.Limit` (for legacy API)

**Implementation Status:** ✅ Fully Implemented

---

## Helper Classes

### `Time` - Trading Session Periods

All times are defined in **Eastern Time (ET)** - the standard for US equity markets. The default timezone setting is EST (Eastern Standard Time).

| Property | Start (ET) | End (ET) | Description |
|----------|------------|----------|-------------|
| `Time.PreMarket` | 4:00 AM | 9:30 AM | Pre-market session |
| `Time.RTH` | 9:30 AM | 4:00 PM | Regular trading hours |
| `Time.AfterHours` | 4:00 PM | 8:00 PM | After-hours session |
| `Time.Extended` | 4:00 AM | 8:00 PM | Full extended hours |

**Usage:**
```csharp
Time.PreMarket.Start              // 4:00 AM ET
Time.PreMarket.End                // 9:30 AM ET
Time.RTH.Start                    // 9:30 AM ET (market open)
Time.RTH.End                      // 4:00 PM ET (market close)
Time.PreMarket.End.AddMinutes(-10) // 9:20 AM ET
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
    .Breakout(7.10)
    .Pullback(6.80)
    .AboveVwap()
    .Buy(quantity: 100, Price.Current)
    .TakeProfit(9.00)
    .StopLoss(6.50)
    .End(Time.PreMarket.End);
```

### Full-Featured Strategy

```csharp
Stock
    .Ticker("AAPL")
    .Start(Time.PreMarket.Start)
    .Breakout(150.00)
    .Pullback(148.00)
    .AboveVwap(buffer: 0.02)
    .Buy(quantity: 200, Price.Current)
    .TakeProfit(155.00)
    .TrailingStopLoss(Percent.Five)
    .TimeInForce(TIF.GTC)
    .OutsideRTH(outsideRth: true, takeProfit: true)
    .ClosePosition(Time.RTH.Start.AddMinutes(-10))
    .End(Time.RTH.Start);
```

### Custom Condition Strategy

```csharp
Stock
    .Ticker("CUSTOM")
    .Breakout(5.00)
    .When("Price in sweet spot", (price, vwap) => price >= 4.50 && price <= 4.80)
    .AboveVwap()
    .Buy(quantity: 500, Price.Current)
    .TakeProfit(6.00)
    .StopLoss(4.25)
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
| Take profit | ✅ | ✅ |
| Stop loss | ✅ | ✅ |
| Trailing stop loss | ✅ | ✅ |
| TimeInForce | ✅ | ✅ |
| OutsideRTH | ✅ | ✅ |
| OrderType | ✅ | ✅ |
| AllOrNone | ✅ | ✅ |
| Enabled/Disabled | ✅ | ✅ |
| VWAP calculation | N/A | ✅ |
| **Offline Backtesting** | ✅ | N/A |

### ⚠️ Partially Implemented (Builder only)

| Feature | Builder | StrategyRunner | Notes |
|---------|---------|----------------|-------|
| Start time | ✅ | ❌ | Property stored, monitoring not enforced |
| End time | ✅ | ❌ | Property stored, monitoring not enforced |
| ClosePosition time | ✅ | ❌ | Property stored, auto-close not implemented |
| Price type (Current/VWAP/Bid/Ask) | ✅ | ❌ | Property stored, order price logic not implemented |

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
