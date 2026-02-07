# IdiotScript - Recipe-Based Trading Strategies

> **Legacy Documentation**: IdiotScript represents the original "recipe-driven" approach to automated trading.
> For the new AI-powered autonomous trading system, see the main [README.md](../README.md).

## Overview

IdiotScript is a domain-specific language (DSL) for defining multi-stage trading strategies. It allows you to create "recipes" that execute automatically when specific market conditions are met.

```
+===========================================================================+
|  IDIOTSCRIPT EXECUTION FLOW                                              |
+===========================================================================+
|                                                                           |
|  [CONFIG] --> [ENTRY CONDITIONS] --> ORDER --> [EXIT CONDITIONS]         |
|                                                                           |
|  Ticker(AAPL)    IsGapUp(5)           Long()     TakeProfit(160)        |
|  Session(RTH)    IsAboveVwap()                   StopLoss(145)          |
|  Quantity(100)   IsEmaAbove(9)                   TrailingStopLoss(2)    |
|                  IsDiPositive()                                          |
|                                                                           |
+===========================================================================+
```

## When to Use IdiotScript vs Autonomous Trading

| Use Case | Recommended Approach |
|----------|---------------------|
| Specific, rule-based strategies you've tested | IdiotScript |
| "Buy at VWAP bounce with EMA confirmation" | IdiotScript |
| Let the system find opportunities | Autonomous Trading |
| Maximize profit with dynamic adaptation | Autonomous Trading |
| Learning how the system works | IdiotScript (explicit rules) |

## Quick Start

### Example Strategy: Gap Up Momentum

```idiotscript
Ticker(NVDA)
.Session(IS.RTH)
.Quantity(100)
.IsGapUp(3)
.IsAboveVwap()
.IsEmaAbove(9)
.IsDiPositive()
.Long()
.TakeProfit(150)
.StopLoss(140)
.TrailingStopLoss(2)
```

### Equivalent Fluent API (C#)

```csharp
Stock.Ticker("NVDA")
    .Session(Session.RTH)
    .Quantity(100)
    .IsGapUp(3)
    .IsAboveVwap()
    .IsEmaAbove(9)
    .IsDiPositive()
    .Long()
    .TakeProfit(150)
    .StopLoss(140)
    .TrailingStopLoss(2)
    .Build();
```

---

## Command Reference

### Configuration Commands

| Command | Description | Example |
|---------|-------------|---------|
| `Ticker(symbol)` | Set the trading symbol | `Ticker(AAPL)` |
| `Name(name)` | Strategy name | `Name("Gap Strategy")` |
| `Session(session)` | Trading session | `Session(IS.RTH)` |
| `Quantity(shares)` | Order quantity | `Quantity(100)` |

### Entry Conditions

Entry conditions are evaluated **sequentially** as a state machine.
Each condition must be true (in order) before the order executes.

#### Price/VWAP Conditions

| Command | Description |
|---------|-------------|
| `IsAboveVwap()` | Price is above VWAP |
| `IsBelowVwap()` | Price is below VWAP |
| `IsCloseAboveVwap()` | Candle CLOSED above VWAP (stronger) |
| `IsVwapRejection()` | Wick above VWAP, close below |
| `Entry(price)` | Price reaches level (alias for IsPriceAbove) |
| `Breakout(price)` | Price breaks above level |
| `Pullback(price)` | Price pulls back to level |

#### Gap Conditions

| Command | Description |
|---------|-------------|
| `IsGapUp(pct)` | Price gapped up X% from previous close |
| `IsGapDown(pct)` | Price gapped down X% from previous close |

#### EMA Conditions

| Command | Description |
|---------|-------------|
| `IsEmaAbove(period)` | Price above EMA |
| `IsEmaBelow(period)` | Price below EMA |
| `IsEmaBetween(p1, p2)` | Price between two EMAs |
| `IsEmaTurningUp(period)` | EMA slope turning positive |

#### Momentum Conditions

| Command | Description |
|---------|-------------|
| `IsMomentumAbove(val)` | Momentum >= threshold |
| `IsMomentumBelow(val)` | Momentum <= threshold |
| `IsRocAbove(pct)` | Rate of Change >= % |
| `IsRocBelow(pct)` | Rate of Change <= % |

#### Trend/Strength Conditions

| Command | Description |
|---------|-------------|
| `IsAdxAbove(val)` | ADX >= threshold (trend strength) |
| `IsDiPositive()` | +DI > -DI (bullish) |
| `IsDiNegative()` | -DI > +DI (bearish) |
| `IsMacdBullish()` | MACD > Signal line |
| `IsMacdBearish()` | MACD < Signal line |

#### RSI Conditions

| Command | Description |
|---------|-------------|
| `IsRsiOversold(val)` | RSI <= threshold |
| `IsRsiOverbought(val)` | RSI >= threshold |

#### Pattern Conditions

| Command | Description |
|---------|-------------|
| `IsHigherLows()` | Higher lows forming |
| `IsLowerHighs()` | Lower highs forming |
| `IsVolumeAbove(mult)` | Volume >= multiplier × average |

### Order Commands

| Command | Description |
|---------|-------------|
| `Order(IS.LONG)` | Execute a long order |
| `Order(IS.SHORT)` | Execute a short order |
| `Long()` | Alias for Order(IS.LONG) |
| `Short()` | Alias for Order(IS.SHORT) |

### Exit Commands

| Command | Description |
|---------|-------------|
| `TakeProfit(price)` | Exit at profit target |
| `StopLoss(price)` | Exit at loss limit |
| `TrailingStopLoss(pct)` | Trail stop by percentage |
| `ExitStrategy(time)` | Exit at specific time |
| `IsProfitable()` | Modifier: only exit if profitable |

---

## Sequential Condition Evaluation

IdiotScript conditions are evaluated as a **state machine**:

```
Condition 1 ─────> Condition 2 ─────> Condition 3 ─────> ORDER
    ↓                 ↓                  ↓
  Wait             Triggered           Must still
  until            moves to            be true
  true             next state
```

This means:
- `IsGapUp(5)` must become true first
- THEN `IsAboveVwap()` is checked
- THEN `IsEmaAbove(9)` is checked
- Only when ALL are satisfied does the order execute

---

## Example Strategies

### VWAP Bounce Long

```idiotscript
Ticker(AAPL)
.Session(IS.RTH)
.Quantity(50)
.IsAboveVwap()
.IsEmaAbove(9)
.IsHigherLows()
.Long()
.TakeProfit(155)
.StopLoss(148)
```

### Mean Reversion (RSI Oversold)

```idiotscript
Ticker(AMD)
.Session(IS.RTH)
.IsRsiOversold(30)
.IsEmaBetween(9, 21)
.IsVolumeAbove(1.5)
.Long()
.TakeProfit(120)
.StopLoss(110)
```

### Trend Following (ADX + DI)

```idiotscript
Ticker(TSLA)
.IsAdxAbove(25)
.IsDiPositive()
.IsEmaAbove(21)
.IsMomentumAbove(0)
.Long()
.TrailingStopLoss(3)
```

### Gap Fade (Gap Fill Strategy)

```idiotscript
Ticker(META)
.IsGapUp(5)
.IsBelowVwap()
.IsRsiOverbought(70)
.Short()
.TakeProfit(300)
.StopLoss(320)
```

---

## Canonical vs Alias Commands

The parser accepts both forms, but **canonical** is preferred:

| Canonical (Use This) | Alias (Works) |
|---------------------|---------------|
| `Quantity(100)` | `Qty(100)` |
| `TakeProfit(155)` | `TP(155)` |
| `StopLoss(145)` | `SL(145)` |
| `TrailingStopLoss(10)` | `TSL(10)` |
| `IsAboveVwap()` | `AboveVwap()` |
| `ExitStrategy(IS.BELL)` | `ClosePosition(IS.BELL)` |

---

## Files and Storage

IdiotScript files use the `.idiot` extension and are stored in:

```
IdiotProof.Core/Scripts/
├── GapMomentum.idiot
├── VwapBounce.idiot
├── TrendFollow.idiot
└── ...
```

---

## Migration to Autonomous Trading

If you want to migrate an IdiotScript strategy to autonomous trading:

1. The autonomous system already uses all the same indicators
2. Your entry conditions become part of the market score calculation
3. TP/SL are calculated dynamically based on ATR and market conditions

**Example Migration:**

```idiotscript
// OLD: IdiotScript (explicit rules)
Ticker(AAPL)
.IsAboveVwap()
.IsEmaAbove(9)
.IsDiPositive()
.Long()
.TakeProfit(160)
.StopLoss(145)

// NEW: Autonomous (AI decides)
Ticker(AAPL)
.AutonomousTrading(IS.BALANCED)
```

The autonomous system will:
- Monitor all indicators continuously
- Enter when conditions align strongly
- Calculate TP/SL empirically from metadata
- Exit when momentum reverses
- Learn from each trade to improve

---

## See Also

- [Main README](../README.md) - Autonomous Trading System
- [copilot-instructions.md](../../.github/copilot-instructions.md) - Full command reference
