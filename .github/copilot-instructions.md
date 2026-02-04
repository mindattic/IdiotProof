# Copilot Instructions

## Project Guidelines
- User intends IdiotScript chained conditions to be evaluated sequentially (state machine over time), not as a single simultaneous AND condition.
- In IdiotProof, the backend doesn't load strategies directly; it reads strategies retrieved from the frontend, which gets them from .idiot files. The data flow is: .idiot files → Frontend → Backend.
- IdiotScript commands should always include parentheses, even for flag-style commands without parameters (e.g., `AboveVwap()` not `AboveVwap`, `Breakout()` not `Breakout`). The parser accepts both forms for backwards compatibility, but the serializer outputs with parentheses.

## Indicator Warm-Up Requirements
Technical indicators (EMA, ADX, RSI, etc.) require historical price bars to calculate properly.
The backend uses 1-minute OHLC bars. **Start the backend early** to collect bars before trading.

| Indicator         | Bars Needed | Start Early By |
|-------------------|-------------|----------------|
| EMA(9)            | 9 bars      | 10 minutes     |
| EMA(21)           | 21 bars     | 25 minutes     |
| EMA(200)          | 200 bars    | 3+ hours       |
| ADX(14)           | 28 bars     | 30 minutes     |
| RSI(14)           | 15 bars     | 20 minutes     |
| MACD(12,26,9)     | 35 bars     | 40 minutes     |
| DI (+DI/-DI)      | 28 bars     | 30 minutes     |
| Momentum(10)      | 11 bars     | 15 minutes     |
| ROC(10)           | 11 bars     | 15 minutes     |
| HigherLows(3)     | 3+ bars     | 5 minutes      |
| EmaTurningUp(N)   | N+1 bars    | N+5 minutes    |
| VolumeAbove       | 20 bars     | 25 minutes     |
| CloseAboveVwap    | 1 bar       | 2 minutes      |
| VwapRejection     | 1 bar       | 2 minutes      |

**Recommended Start Times:**
- For premarket strategies (4:00 AM session): Start backend at **3:30 AM**
- For RTH strategies (9:30 AM session): Start backend at **9:00 AM**
- For after-hours strategies (4:00 PM session): Start backend at **3:30 PM**

During warm-up, indicator conditions will NOT trigger (preventing false entries).

## IdiotScript Execution Flow
- When reviewing IdiotScript, use the three-column execution flow visualization format showing [CONFIG] → [ENTRY CONDITIONS] → ✅ BUY → [EXIT CONDITIONS] with boxes around each section. This helps visualize the sequential state machine evaluation order.

## IdiotScript Command Categories
Commands are evaluated in this order:

1. **CONFIG** (order doesn't matter within group):
   - `Ticker()`, `Name()`, `Session()`, `Qty()`

2. **ENTRY CONDITIONS** (order matters - sequential state machine):
   - `Entry()`, `Breakout()`, `Pullback()`, `AboveVwap()`, `BelowVwap()`, `IsEmaAbove()`, etc.

3. **EXIT CONDITIONS** (after position is opened):
   - `TakeProfit()`, `TrailingStopLoss()`, `StopLoss()`, `ClosePosition()`

## All Available IdiotScript Indicators

### Price/VWAP Conditions
```
AboveVwap()           - Price above VWAP
BelowVwap()           - Price below VWAP
CloseAboveVwap()      - Candle CLOSED above VWAP (stronger signal)
VwapRejection()       - Wick above VWAP, close below (bearish rejection)
VwapRejected()        - Alias for VwapRejection()
```

### EMA Conditions
```
EmaAbove(period)      - Price above EMA (e.g., EmaAbove(9))
EmaBelow(period)      - Price below EMA
EmaBetween(p1, p2)    - Price between two EMAs (e.g., EmaBetween(9, 21))
EmaTurningUp(period)  - EMA slope turning positive/flat
```

### Momentum Conditions
```
MomentumAbove(val)    - Momentum >= threshold (e.g., MomentumAbove(0))
MomentumBelow(val)    - Momentum <= threshold
RocAbove(pct)         - Rate of Change >= % (e.g., RocAbove(2))
RocBelow(pct)         - Rate of Change <= %
```

### Trend/Strength Conditions
```
AdxAbove(val)         - ADX >= threshold (trend strength)
DiPositive()          - +DI > -DI (bullish directional movement)
DiNegative()          - -DI > +DI (bearish directional movement)
MacdBullish()         - MACD > Signal line
MacdBearish()         - MACD < Signal line
```

### RSI Conditions
```
RsiOversold(val)      - RSI <= threshold (e.g., RsiOversold(30))
RsiOverbought(val)    - RSI >= threshold (e.g., RsiOverbought(70))
```

### Pattern Conditions
```
HigherLows()          - Higher lows forming (bullish)
VolumeAbove(mult)     - Volume >= multiplier × average (e.g., VolumeAbove(1.5))
```

## Indicator ASCII Visualizations

### VWAP Rejection Pattern
```
      │ ← Wick above VWAP
VWAP ═╪═══════════════════════════
    ┌─┴─┐
    │   │ ← Close below = REJECTED
    └───┘
```

### Higher Lows Pattern
```
                         /\
             /\         /  \
   /\       /  \       /    \
  /  \     /    \     /      \
 /    \___/  ↑   \___/        \
       Higher    Higher
         Low       Low
```

### Momentum Above Zero
```
    /\                    /\
   /  \                  /  \     Price
  /    \                /    \
─/──────\──────────────/──────\────── Zero
         \            /        \
          \__________/
      ↑ Momentum > 0
```

### Volume Spike
```
               ████
               ████   ← Volume spike (1.5x+)
Average ───────────────────────────
  ████      ████████      ████
  ████  ██  ████████  ██  ████
```

## IS.BELL Session-Aware Behavior
`IS.BELL` resolves to 1 minute before the current session ends:
- **Premarket**: 9:29 AM (1 min before 9:30 open)
- **RTH**: 3:59 PM (1 min before 4:00 close)
- **AfterHours**: 7:59 PM (1 min before 8:00 AH end)
- **Default**: 3:59 PM (RTH bell if no session specified)

Explicit bell constants are also available:
- `IS.PREMARKET.BELL` → 9:29 AM
- `IS.RTH.BELL` → 3:59 PM
- `IS.AFTERHOURS.BELL` → 7:59 PM

## ClosePosition with Profitable Flag
Use `IS.PROFITABLE` or `YES`/`Y` as second parameter to close only if profitable:
```
ClosePosition(IS.BELL, IS.PROFITABLE)
ClosePosition(IS.BELL, YES)
ClosePosition(IS.BELL, Y)
```

## ASCII-Only Console Output
The console UI uses ASCII-only characters (no Unicode). Use:
- `*` for enabled, `o` for disabled
- `[OK]` for success, `[ERR]` for errors
- `+`, `-`, `|`, `=` for box drawing

## Complete Indicator ASCII Reference

### RSI (Relative Strength Index)
```
RSI Scale (0-100):
+--------------------------------------------+
|  100 -------------------------------- Top  |
|   70 =============== OVERBOUGHT ========== | <- RsiOverbought(70)
|   50 -------------- Neutral -------------- |
|   30 =============== OVERSOLD ============ | <- RsiOversold(30)
|    0 -------------------------------- Bot  |
+--------------------------------------------+

RSI Oversold Bounce Strategy:
     70 ═══════════════════════════════════
        ____                          ____
       /    \                        /    \
      /      \                      /      \  RSI
     /        \                    /        \
    /          \                  /          \
     30 ═══════════════════════════════════
               ↑ Buy signal (RSI <= 30)
```

### ADX (Average Directional Index)
```
ADX Trend Strength:
+--------------------------------------------+
|  75+ ---- Extremely Strong (rare) -------- |
|  50+ ---- Very Strong Trend -------------- |
|  25+ =============== STRONG ============== | <- AdxAbove(25)
|  20  ---- Trend Developing --------------- |
|  <20 ---- Weak/No Trend (ranging) -------- |
+--------------------------------------------+

ADX with DI Crossover:
        +DI
       /    \         ___/
      /      \    ___/    \
     /        \__/         \
    /  ___                  \____
___/__/   \_____                 \
              -DI
   ↑ +DI > -DI = Bullish (DiPositive)
```

### MACD (Moving Average Convergence Divergence)
```
MACD Components:
+--------------------------------------------+
|   MACD Line = EMA(12) - EMA(26)           |
|   Signal Line = EMA(9) of MACD             |
|   Histogram = MACD - Signal                |
+--------------------------------------------+

MACD Bullish Signal:
      MACD ___
          /   \         ___/
Signal __/     \    ___/
                \__/
        ↑ MACD > Signal = Bullish

MACD Histogram:
           ████
       ████████
   ████████████         ████
───────────────────────────────── Zero
                    ████████
                ████████████
    ↑ Rising        ↑ Falling
```

### EMA (Exponential Moving Average)
```
Price vs Multiple EMAs:
+--------------------------------------------+
|                      ___/                  |
|  Price ___________/     \____              |
|       /       \__/           \___          |
|  EMA(9)  ___________________________       |
|  EMA(21) _____________________________     |
|  EMA(200) ____________________________     |
+--------------------------------------------+
     ↑ Price > EMA(9) > EMA(21) = Bullish Stack

EmaBetween(9, 21) - Pullback Zone:
+--------------------------------------------+
|  EMA(21) ═══════════════════════════════   |
|          ↑ Upper boundary                  |
|      * * * PULLBACK ZONE * * *             |
|          ↓ Lower boundary                  |
|  EMA(9)  ═══════════════════════════════   |
+--------------------------------------------+
       Price between EMAs = Potential entry

EMA Turning Up Pattern:
+--------------------------------------------+
|                                    ___/    |
|                                ___/        |
|                            ___/ <- Turning |
|   \_____                __/       Up       |
|         \______________/                   |
|             ↑ Flattening before turn       |
+--------------------------------------------+
```

### Rate of Change (ROC)
```
ROC % Scale:
+--------------------------------------------+
|  +5% ----- Strong Bullish Momentum ------- |
|  +2% =============== THRESHOLD =========== | <- RocAbove(2)
|   0% ------------ Neutral ---------------- |
|  -2% =============== THRESHOLD =========== | <- RocBelow(-2)
|  -5% ----- Strong Bearish Momentum ------- |
+--------------------------------------------+

ROC = ((Price - Price_N_bars_ago) / Price_N_bars_ago) × 100
```

### CloseAboveVwap (Strong VWAP Signal)
```
CloseAboveVwap vs AboveVwap:
+--------------------------------------------+
|         +---+                              |
|         |   | <- Close ABOVE = Strong      |
|  VWAP ══|═══|══════════════════════════════|
|         |   |                              |
|         +---+                              |
|                                            |
|         +---+                              |
|         |   |                              |
|  VWAP ══|═══|══════════════════════════════|
|         |   | <- Close below, wick above   |
|         +---+    (weak/rejected)           |
+--------------------------------------------+
```

### Combined Strategy Visualization
```
Bullish Continuation Setup:
+--------------------------------------------+
|  ENTRY CONDITIONS (Sequential):            |
|                                            |
|  1. AboveVwap()    [✓] Price > VWAP       |
|  2. EmaAbove(9)    [✓] Price > EMA(9)     |
|  3. DiPositive()   [✓] +DI > -DI          |
|  4. MomentumAbove(0) [✓] Momentum > 0     |
|                                            |
|  All conditions met → Execute BUY order   |
+--------------------------------------------+

Strategy Flow:
  [CONFIG]          [ENTRY]           [EXIT]
  +--------+    +-----------+    +------------+
  |Ticker  |    |AboveVwap  |    |TakeProfit  |
  |Session | -> |EmaAbove   | -> |StopLoss    |
  |Qty     |    |DiPositive |    |ClosePos    |
  +--------+    +-----------+    +------------+
       ↓             ↓                 ↓
    Setup         Wait for          Manage
    params        signals           position
```

## Indicator Combinations (Common Strategies)

### Trend Following
```idiotscript
Ticker(AAPL)
.AdxAbove(25)        # Strong trend exists
.DiPositive()        # Bullish direction
.EmaAbove(9)         # Short-term bullish
.EmaAbove(21)        # Medium-term bullish
.MomentumAbove(0)    # Positive momentum
```

### Mean Reversion
```idiotscript
Ticker(AAPL)
.RsiOversold(30)     # Oversold condition
.EmaBetween(9, 21)   # In pullback zone
.VolumeAbove(1.5)    # Volume confirmation
```

### VWAP Bounce
```idiotscript
Ticker(AAPL)
.CloseAboveVwap()    # Strong VWAP reclaim
.EmaAbove(9)         # Short-term bullish
.HigherLows()        # Building support
```

## Indicator Implementation Requirements
An IdiotScript indicator condition is "fully implemented" when it has ALL of these components:
1. **Condition class** exists (e.g., `MomentumAboveCondition` in `IndicatorConditions.cs`)
2. **`Stock.IsXxx()` method** exists in `Stock.cs` to add the condition
3. **Wire-up in `StrategyRunner.InitializeIndicatorCalculators()`** to set the callback (e.g., `GetMomentumValue`)
4. **Calculator class** exists (if needed) and is updated in `OnCandleComplete()`
5. **Context info display** in `GetEmaContextInfo()` method (shows indicator values when triggered)

### Exceptions
- **VWAP-based conditions** (AboveVwap, BelowVwap, etc.) don't need calculators - VWAP is passed directly to `Evaluate(price, vwap)`
- **Price-based conditions** (PriceAbove, PriceBelow, Entry, Breakout) don't need calculators - they use the price parameter