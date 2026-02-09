# Copilot Instructions

## Project Guidelines
- User intends IdiotScript chained conditions to be evaluated sequentially (state machine over time), not as a single simultaneous AND condition.
- In IdiotProof, the backend doesn't load strategies directly; it reads strategies retrieved from the frontend, which gets them from .idiot files. The data flow is: .idiot files → Frontend → Backend.
- IdiotScript commands should always include parentheses, even for flag-style commands without parameters (e.g., `IsAboveVwap()` not `IsAboveVwap`, `Breakout()` not `Breakout`). The parser accepts both forms for backwards compatibility, but the serializer outputs with parentheses.
- **Canonical vs Alias**: Use canonical (full) command names, not aliases. The parser accepts both, but the serializer outputs canonical forms:
  - `Quantity()` not `Qty()` (canonical)
  - `TakeProfit()` not `TP()` (canonical)
  - `StopLoss()` not `SL()` (canonical)
  - `TrailingStopLoss()` not `TSL()` (canonical)
  - `IsAboveVwap()` not `AboveVwap()` (canonical)
  - `ExitStrategy()` not `ClosePosition()` (canonical)

## Indicator Warm-Up Requirements
Technical indicators (EMA, ADX, RSI, etc.) require historical price bars to calculate properly. The backend uses 1-minute OHLC bars. **Start the backend early** to collect bars before trading.

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
| IsHigherLows(3)   | 3+ bars     | 5 minutes      |
| IsEmaTurningUp(N) | N+1 bars    | N+5 minutes    |
| IsVolumeAbove     | 20 bars     | 25 minutes     |
| IsCloseAboveVwap  | 1 bar       | 2 minutes      |
| IsVwapRejection   | 1 bar       | 2 minutes      |

**Recommended Start Times:**
- For premarket strategies (4:00 AM session): Start backend at **3:30 AM**
- For RTH strategies (9:30 AM session): Start backend at **9:00 AM**
- For after-hours strategies (4:00 PM session): Start backend at **3:30 PM**

During warm-up, indicator conditions will NOT trigger (preventing false entries).

## IdiotScript Execution Flow
- When reviewing IdiotScript, use the three-column execution flow visualization format showing [CONFIG] → [ENTRY CONDITIONS] → ✅ ORDER → [EXIT CONDITIONS] with boxes around each section. This helps visualize the sequential state machine evaluation order.

## IdiotScript Command Categories
Commands are evaluated in this order:

1. **CONFIG** (order doesn't matter within group):
   - `Ticker()`, `Name()`, `Session()`, `Quantity()`

2. **ENTRY CONDITIONS** (order matters - sequential state machine):
   - `Entry()`, `Breakout()`, `Pullback()`, `IsGapUp()`, `IsGapDown()`, `IsAboveVwap()`, `IsBelowVwap()`, `IsEmaAbove()`, etc.

3. **EXIT CONDITIONS** (after position is opened):
   - `TakeProfit()`, `TrailingStopLoss()`, `StopLoss()`, `ExitStrategy()`, `IsProfitable()`

## Entry vs Order Clarification
- **`Entry(price)`** = A **CONDITION** that triggers when price reaches a level (alias for `IsPriceAbove()`)
- **`Order(IS.LONG)`** or **`Order(IS.SHORT)`** = An **ACTION** that executes an order when all conditions are met
- **`Order()`** with no parameter defaults to `IS.LONG` (long position)
- These are NOT the same! Entry is a trigger condition, Order is the order execution.

### Single-Responsibility Pattern
Each fluent API method and DSL command should "do just one thing":
- Methods have at most ONE parameter
- If no parameter is specified, use the built-in default value
- Methods that previously had 2+ params are split into separate single-responsibility methods

### Order Direction Syntax
```
Order()               - Opens a LONG position (default)
Order(IS.LONG)        - Opens a LONG position (explicit)
Order(IS.SHORT)       - Opens a SHORT position
Long()                - Alias for Order(IS.LONG)
Short()               - Alias for Order(IS.SHORT)
```

### Order Configuration (Chained Methods)
```
.Quantity(100)        - Sets order quantity
.PriceType(Price.Current)  - Sets price type for execution
.OrderType(OrderType.Market)  - Sets market vs limit order
.OutsideRTH()         - Allow entry order outside RTH (default: true)
.TakeProfitOutsideRTH()  - Allow TP order outside RTH (default: true)
```

### Position Closing
```
CloseLong()           - Create SELL order to close a long position
CloseShort()          - Create BUY order to cover a short position
```

**Example - Single Responsibility Chain:**
```csharp
Stock.Ticker("AAPL")
    .Entry(150)
    .Long()                // Sets direction only
    .Quantity(100)         // Sets quantity separately
    .PriceType(Price.VWAP) // Sets price type separately
    .OutsideRTH()          // Enables outside RTH for entry
    .TakeProfitOutsideRTH()  // Enables outside RTH for take profit
    .TakeProfit(160)
    .Build();
```

**Legacy Syntax (Deprecated but still works):**
```
Buy()                 - Deprecated, use Order(IS.LONG)
Sell()                - Deprecated, use Order(IS.SHORT)
```

## All Available IdiotScript Indicators

### Price/VWAP Conditions
```
IsAboveVwap()         - Price above VWAP
IsBelowVwap()         - Price below VWAP
IsCloseAboveVwap()    - Candle CLOSED above VWAP (stronger signal)
IsVwapRejection()     - Wick above VWAP, close below (bearish rejection)
IsVwapRejected()      - Alias for IsVwapRejection()
```

### Gap Conditions
```
IsGapUp(pct)          - Price gapped up X% from previous close (e.g., IsGapUp(5) = 5%+)
IsGapDown(pct)        - Price gapped down X% from previous close (e.g., IsGapDown(3) = 3%+)
```

### EMA Conditions
```
IsEmaAbove(period)    - Price above EMA (e.g., IsEmaAbove(9))
IsEmaBelow(period)    - Price below EMA
IsEmaBetween(p1, p2)  - Price between two EMAs (e.g., IsEmaBetween(9, 21))
IsEmaTurningUp(period)- EMA slope turning positive/flat
```

### Momentum Conditions
```
IsMomentumAbove(val)  - Momentum >= threshold (e.g., IsMomentumAbove(0))
IsMomentumBelow(val)  - Momentum <= threshold
IsRocAbove(pct)       - Rate of Change >= % (e.g., IsRocAbove(2))
IsRocBelow(pct)       - Rate of Change <= %
```

### Trend/Strength Conditions
```
IsAdxAbove(val)       - ADX >= threshold (trend strength)
IsDiPositive()        - +DI > -DI (bullish directional movement)
IsDiNegative()        - -DI > +DI (bearish directional movement)
IsMacdBullish()       - MACD > Signal line
IsMacdBearish()       - MACD < Signal line
```

### RSI Conditions
```
IsRsiOversold(val)    - RSI <= threshold (e.g., IsRsiOversold(30))
IsRsiOverbought(val)  - RSI >= threshold (e.g., IsRsiOverbought(70))
```

### Pattern Conditions
```
IsHigherLows()        - Higher lows forming (bullish)
IsVolumeAbove(mult)   - Volume >= multiplier × average (e.g., IsVolumeAbove(1.5))
```

### Smart Order Management
```
AdaptiveOrder()                   - Enable smart dynamic TP/SL adjustment (balanced mode)
AdaptiveOrder(IS.CONSERVATIVE)    - Protect gains, quick to take profits
AdaptiveOrder(IS.BALANCED)        - Equal priority to profit and protection  
AdaptiveOrder(IS.AGGRESSIVE)      - Maximize profit potential in strong trends
IsAdaptiveOrder()                 - Alias for AdaptiveOrder()
```

## AdaptiveOrder - Smart Dynamic Order Management

AdaptiveOrder monitors market conditions in real-time and dynamically adjusts take profit and stop loss levels.

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

### Adaptive Behavior Table
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
### Example Strategy with AdaptiveOrder
```idiotscript
Ticker(AAPL)
.Entry(150)
.TakeProfit(160)         # Original TP target
.StopLoss(145)           # Original SL level
.IsAboveVwap()
.IsEmaAbove(9)
.IsDiPositive()
.AdaptiveOrder(IS.AGGRESSIVE)  # System will dynamically adjust TP/SL
```

**Note:** AdaptiveOrder requires TakeProfit and/or StopLoss to be set. It modifies these values dynamically but needs starting points for calculation.

### How AdaptiveOrder Calculates Adjustments

#### Score-Based Take Profit Multiplier
The TP multiplier determines how much to extend or reduce the original take profit target:

```
Score Range        │ Multiplier Formula                          │ Example (TP=$160, Entry=$150)
───────────────────┼─────────────────────────────────────────────┼──────────────────────────────
70 to 100          │ 1.0 + (MaxExtension × (score-70)/30)        │ Score 100: TP → $167.50 (+75%)
30 to 70           │ 1.0 (no change)                             │ Score 50:  TP → $160.00
-30 to 30          │ 1.0 - (0.15 × (30-score)/60)                │ Score 0:   TP → $158.75 (-12.5%)
-70 to -30         │ 0.85 - ((MaxReduction/2-0.15) × (-30-s)/40) │ Score -50: TP → $156.25 (-37.5%)
< -70              │ 1.0 - MaxReduction                          │ Score -80: TP → $155.00 (-50%)
```

#### Score-Based Stop Loss Multiplier
The SL multiplier determines how much to tighten (move closer) or widen (move further) the stop:

```
Score Range        │ Multiplier Formula                          │ Example (SL=$145, Entry=$150)
───────────────────┼─────────────────────────────────────────────┼──────────────────────────────
70 to 100          │ 1.0 + (MaxTighten × (score-70)/30)          │ Score 100: SL → $147.50 (tighter)
0 to 70            │ 1.0 (no change)                             │ Score 50:  SL → $145.00
-50 to 0           │ 1.0 - (MaxWiden × (-score/50) × 0.5)        │ Score -25: SL → $144.38 (wider)
< -50              │ 1.0 - (MaxWiden × 0.5)                      │ Score -60: SL → $144.38 (wider)
```

Note: Multiplier > 1.0 = tighter stop (closer to entry), Multiplier < 1.0 = wider stop (further from entry)

### Mode Configuration Settings
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
**Conservative**: Protects gains quickly, allows wider stops to avoid noise, exits sooner on bearish signals.  
**Balanced**: Standard risk/reward, moderate adjustments in both directions.  
**Aggressive**: Lets winners run longer, tighter stops to protect capital, stays in longer during drawdowns.

### Indicator Weight Configuration (Default)
```
VWAP Position:    15%  - Price relative to VWAP (5% above VWAP = +100 score)
EMA Stack:        20%  - Alignment of short/medium/long EMAs
RSI:              15%  - Overbought (70+) = negative, Oversold (30-) = positive
MACD:             20%  - MACD > Signal = +50, plus histogram strength ±50
ADX:              20%  - Trend strength × direction (ADX 50 = ±100)
Volume:           10%  - Volume ratio confirms current direction
────────────────────
Total:           100%
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

### Adaptive TP Feedback Loop (Smart Trailing Take Profit)

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
- `IS.AFTERHOURS.BLL` → 7:59 PM

## ExitStrategy and IsProfitable (Single-Responsibility Pattern)
Use `ExitStrategy()` for exit timing and chain `.IsProfitable()` to only exit if profitable:
```
ExitStrategy(IS.BELL)                    # Exit at session bell
ExitStrategy(IS.BELL).IsProfitable()     # Exit at bell only if profitable
ExitStrategy(15:30).IsProfitable()       # Exit at 3:30 PM only if profitable
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
|   70 =============== OVERBOUGHT ========== | <- IsRsiOverbought(70)
|   50 -------------- Neutral -------------- |
|   30 =============== OVERSOLD ============ | <- IsRsiOversold(30)
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
|  25+ =============== STRONG ============== | <- IsAdxAbove(25)
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
   ↑ +DI > -DI = Bullish (IsDiPositive)
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

IsEmaBetween(9, 21) - Pullback Zone:
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
|  +2% =============== THRESHOLD =========== | <- IsRocAbove(2)
|   0% ------------ Neutral ---------------- |
|  -2% =============== THRESHOLD =========== | <- IsRocBelow(-2)
|  -5% ----- Strong Bearish Momentum ------- |
+--------------------------------------------+

ROC = ((Price - Price_N_bars_ago) / Price_N_bars_ago) × 100
```

### IsCloseAboveVwap (Strong VWAP Signal)
```
IsCloseAboveVwap vs IsAboveVwap:
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
|  1. IsAboveVwap()    [✓] Price > VWAP       |
|  2. IsEmaAbove(9)    [✓] Price > EMA(9)     |
|  3. IsDiPositive()   [✓] +DI > -DI          |
|  4. IsMomentumAbove(0) [✓] Momentum > 0     |
|                                            |
|  All conditions met → Execute BUY order   |
+--------------------------------------------+

Strategy Flow:
  [CONFIG]          [ENTRY]           [EXIT]
  +--------+    +-------------+    +------------+
  |Ticker  |    |IsAboveVwap  |    |TakeProfit  |
  |Session | -> |IsEmaAbove   | -> |StopLoss    |
  |Qty     |    |IsDiPositive |    |ClosePos    |
  +--------+    +-------------+    +------------+
       ↓             ↓                 ↓
    Setup         Wait for          Manage
    params        signals           position
```

## Indicator Combinations (Common Strategies)

### Trend Following
```idiotscript
Ticker(AAPL)
.IsAdxAbove(25)      # Strong trend exists
.IsDiPositive()      # Bullish direction
.IsEmaAbove(9)       # Short-term bullish
.IsEmaAbove(21)      # Medium-term bullish
.IsMomentumAbove(0)  # Positive momentum
```

### Mean Reversion
```idiotscript
Ticker(AAPL)
.IsRsiOversold(30)   # Oversold condition
.IsEmaBetween(9, 21) # In pullback zone
.IsVolumeAbove(1.5)  # Volume confirmation
```

### VWAP Bounce
```idiotscript
Ticker(AAPL)
.IsCloseAboveVwap()  # Strong VWAP reclaim
.IsEmaAbove(9)       # Short-term bullish
.IsHigherLows()      # Building support
```

## AutonomousTrading - AI-Driven Entry/Exit Decisions

AutonomousTrading enables fully automated trading where the system monitors all indicators and independently decides when to buy, sell, short, or close positions based on market score analysis.

### Quick Start
```idiotscript
Ticker(AAPL)
.AutonomousTrading()
```

That's it! The system will:
1. Monitor VWAP, EMA, RSI, MACD, ADX, and Volume continuously
2. Calculate a market score (-100 to +100)
3. **Self-adjust thresholds** based on market conditions (more aggressive in strong trends, conservative when ranging)
4. Enter LONG when score >= dynamic threshold, SHORT when score <= negative threshold
5. Exit when momentum reverses
6. Flip direction (exit long → enter short) on strong reversals

### AutonomousTrading Syntax
```
AutonomousTrading()       - Enable self-adjusting autonomous trading
IsAutonomousTrading()     - Alias for AutonomousTrading()
```

**Note:** Any mode parameters (IS.CONSERVATIVE, IS.BALANCED, etc.) are accepted for backwards compatibility but ignored. The system always self-adjusts.

### Self-Adjusting Thresholds

The system automatically adjusts entry/exit thresholds based on real-time market conditions:

```
+===========================================================================+
|  DYNAMIC THRESHOLD CALCULATION                                            |
+===========================================================================+
|                                                                           |
|  Adjustment Factor        | Effect on Thresholds          | Reasoning    |
|  ─────────────────────────┼───────────────────────────────┼──────────────|
|  ADX >= 40 (strong trend) | Long: 50, Short: -50          | Aggressive   |
|  ADX 25-40 (moderate)     | Long: 60, Short: -60          | Slightly agg |
|  ADX < 20 (ranging)       | Long: 75, Short: -75          | Conservative |
|  ─────────────────────────┼───────────────────────────────┼──────────────|
|  ATR > 5% (high vol)      | +10 to thresholds             | Conservative |
|  ATR 3-5% (moderate)      | +5 to thresholds              | Slight cons  |
|  ATR < 1% (low vol)       | -5 to thresholds              | Aggressive   |
|  ─────────────────────────┼───────────────────────────────┼──────────────|
|  Indicators 80%+ agree    | -10 to thresholds             | Aggressive   |
|  Indicators 60-80% agree  | -5 to thresholds              | Slight agg   |
|  Indicators < 40% agree   | +10 to thresholds             | Conservative |
|  ─────────────────────────┼───────────────────────────────┼──────────────|
|  RSI > 75 (overbought)    | Long threshold +15            | Careful long |
|  RSI < 25 (oversold)      | Short threshold -15           | Careful short|
|  ─────────────────────────┼───────────────────────────────┼──────────────|
|  First 15 minutes         | +10 to thresholds             | Volatility   |
|  Last 30 minutes          | +5 to thresholds              | EOD caution  |
|                                                                           |
+===========================================================================+

Example: NVDA during strong uptrend (ADX=45, ATR=2%, Indicators=85% bullish)
  → Long threshold: 65 - 15 (ADX) - 10 (agreement) = 40 (very aggressive)
  → The system enters earlier when conditions strongly favor the trade

Example: SPY during ranging market (ADX=18, ATR=0.8%, mixed signals)
  → Long threshold: 65 + 10 (ranging) - 5 (low vol) + 10 (mixed) = 80 (conservative)
  → The system waits for stronger confirmation before entering
```

**Recommended Usage:**
```idiotscript
Ticker(NVDA)
.Name("NVDA Autonomous")
.Session(IS.RTH)
.Quantity(5)
.AutonomousTrading()
```

### How AutonomousTrading Works

```
+===========================================================================+
|  AUTONOMOUS TRADING FLOW                                                  |
+===========================================================================+
|                                                                           |
|  ┌─────────────────────────────────────────────────────────────────────┐  |
|  │  MARKET SCORE CALCULATION (every tick)                              │  |
|  │                                                                     │  |
|  │  VWAP Position   ─────── 15% weight ──────┐                        │  |
|  │  EMA Stack       ─────── 20% weight ──────┤                        │  |
|  │  RSI Level       ─────── 15% weight ──────┼──► SCORE (-100 to +100)│  |
|  │  MACD Signal     ─────── 20% weight ──────┤                        │  |
|  │  ADX Strength    ─────── 20% weight ──────┤                        │  |
|  │  Volume Ratio    ─────── 10% weight ──────┘                        │  |
|  └─────────────────────────────────────────────────────────────────────┘  |
|                              │                                            |
|                              ▼                                            |
|  ┌─────────────────────────────────────────────────────────────────────┐  |
|  │  ENTRY DECISION                                                     │  |
|  │                                                                     │  |
|  │  Score >= +70 (balanced) ──────────────────────► ENTER LONG         │  |
|  │  Score <= -70 (balanced) ──────────────────────► ENTER SHORT        │  |
|  │  Score between -70 and +70 ────────────────────► NO ACTION          │  |
|  └─────────────────────────────────────────────────────────────────────┘  |
|                              │                                            |
|                              ▼                                            |
|  ┌─────────────────────────────────────────────────────────────────────┐  |
|  │  EXIT DECISION (when position is open)                              │  |
|  │                                                                     │  |
|  │  LONG position + Score < +40 ──────────────────► EXIT LONG          │  |
|  │  SHORT position + Score > -40 ─────────────────► EXIT SHORT         │  |
|  │                                                                     │  |
|  │  With AllowDirectionFlip:                                          │  |
|  │  Exit LONG + Score <= -70 ─────────────────────► FLIP TO SHORT      │  |
|  │  Exit SHORT + Score >= +70 ────────────────────► FLIP TO LONG       │  |
|  └─────────────────────────────────────────────────────────────────────┘  |
+===========================================================================+
```

### Self-Adjusting Behavior

The system automatically adjusts its behavior based on market conditions. There are no modes to configure - it's always optimal:

```
+===========================================================================+
|  AUTOMATIC ADJUSTMENTS                                                    |
+===========================================================================+
|                                                                           |
|  STRONG TREND (ADX >= 40):                                               |
|    - Entry thresholds: 50/-50 (aggressive)                               |
|    - TP multiplier: 2.5x ATR (let winners run)                           |
|    - SL multiplier: 1.2x ATR (protect capital)                           |
|                                                                           |
|  MODERATE TREND (ADX 25-40):                                             |
|    - Entry thresholds: 60/-60                                            |
|    - TP multiplier: 2.2x ATR                                             |
|    - SL multiplier: 1.4x ATR                                             |
|                                                                           |
|  RANGING MARKET (ADX < 20):                                              |
|    - Entry thresholds: 75/-75 (conservative)                             |
|    - TP multiplier: 1.5x ATR (take quick profits)                        |
|    - SL multiplier: 2.0x ATR (allow for chop)                            |
|                                                                           |
+===========================================================================+
```

### Indicator Score Calculation

```
+===========================================================================+
|  INDICATOR SCORING DETAILS                                                |
+===========================================================================+
|                                                                           |
|  VWAP POSITION (15% weight)                                              |
|  ┌────────────────────────────────────────────────────────────────────┐  |
|  │  Price 5% above VWAP ──────────────────────────► +100 score        │  |
|  │  Price at VWAP ────────────────────────────────► 0 score           │  |
|  │  Price 5% below VWAP ──────────────────────────► -100 score        │  |
|  └────────────────────────────────────────────────────────────────────┘  |
|                                                                           |
|  EMA STACK (20% weight)                                                  |
|  ┌────────────────────────────────────────────────────────────────────┐  |
|  │  Price above ALL EMAs ─────────────────────────► +100 score        │  |
|  │  Price above SOME EMAs ────────────────────────► proportional      │  |
|  │  Price below ALL EMAs ─────────────────────────► -100 score        │  |
|  └────────────────────────────────────────────────────────────────────┘  |
|                                                                           |
|  RSI (15% weight)                                                        |
|  ┌────────────────────────────────────────────────────────────────────┐  |
|  │  RSI <= 30 (oversold) ─────────────────────────► +100 score        │  |
|  │  RSI 30-50 ────────────────────────────────────► -50 to 0          │  |
|  │  RSI 50-70 ────────────────────────────────────► 0 to +50          │  |
|  │  RSI >= 70 (overbought) ───────────────────────► -100 score        │  |
|  └────────────────────────────────────────────────────────────────────┘  |
|                                                                           |
|  MACD (20% weight)                                                       |
|  ┌────────────────────────────────────────────────────────────────────┐  |
|  │  MACD > Signal (bullish) ──────────────────────► +50 base          │  |
|  │  MACD < Signal (bearish) ──────────────────────► -50 base          │  |
|  │  Histogram strength ───────────────────────────► +/- 50 bonus      │  |
|  └────────────────────────────────────────────────────────────────────┘  |
|                                                                           |
|  ADX (20% weight)                                                        |
|  ┌────────────────────────────────────────────────────────────────────┐  |
|  │  +DI > -DI + ADX value ────────────────────────► +magnitude        │  |
|  │  -DI > +DI + ADX value ────────────────────────► -magnitude        │  |
|  │  ADX 50 = max magnitude (100)                                      │  |
|  └────────────────────────────────────────────────────────────────────┘  |
|                                                                           |
|  VOLUME (10% weight)                                                     |
|  ┌────────────────────────────────────────────────────────────────────┐  |
|  │  Volume > avg + Price > VWAP ──────────────────► +magnitude        │  |
|  │  Volume > avg + Price < VWAP ──────────────────► -magnitude        │  |
|  │  Volume ratio confirms current direction                           │  |
|  └────────────────────────────────────────────────────────────────────┘  |
|                                                                           |
+===========================================================================+
```

### AutonomousTrading vs AdaptiveOrder

```
+===========================================================================+
|  COMPARISON: AUTONOMOUS vs ADAPTIVE                                       |
+===========================================================================+
|                                                                           |
|  Feature              | AutonomousTrading      | AdaptiveOrder            |
|  ─────────────────────┼────────────────────────┼──────────────────────────|
|  Entry Decision       | AUTOMATIC (AI decides) | MANUAL (you set Entry)   |
|  Exit Decision        | AUTOMATIC (AI decides) | SEMI-AUTO (adjusts TP/SL)|
|  TP/SL Calculation    | ATR-based automatic    | Modifies your TP/SL      |
|  Direction            | Can flip long<->short  | Single direction only    |
|  Conditions Required  | NONE (just Ticker)     | You define conditions    |
|  Best For             | Fully hands-off        | Enhance manual strategy  |
|                                                                           |
+===========================================================================+

Use AutonomousTrading when: You want the AI to handle everything
Use AdaptiveOrder when: You have specific entry conditions but want smart exits
```

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
|  ────────────────────────────────────────────────────────────────|
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
   ┌─────────────────┐                  ┌─────────────────┐
   │ RSI: 28->45     │                  │ RSI: 55->78     │
   │ (oversold       │                  │ (overbought     │
   │  bounce)        │                  │  warning)       │
   │                 │                  │                 │
   │ +DI crosses     │                  │ MACD histogram  │
   │ above -DI       │                  │ shrinking       │
   │ (bullish)       │                  │ (momentum loss) │
   │                 │                  │                 │
   │ MACD bullish    │                  │ Volume declining│
   │ cross           │                  │ on push higher  │
   └────────┬────────┘                  └────────┬────────┘
            │                                     │
            ▼                                     ▼
     SCORE: +71                            SCORE: +40
     ENTRY LONG                            EXIT LONG


   REVERSAL TO SHORT                    EXHAUSTION AT SUPPORT
   ┌─────────────────┐                  ┌─────────────────┐
   │ -DI crosses     │                  │ RSI: 35         │
   │ above +DI       │                  │ (approaching    │
   │ (bearish)       │                  │  oversold)      │
   │                 │                  │                 │
   │ MACD bearish    │                  │ Price at EMA 21 │
   │ cross           │                  │ support level   │
   │                 │                  │                 │
   │ Price below     │                  │ Momentum slowing│
   │ VWAP            │                  │ at key level    │
   └────────┬────────┘                  └────────┬────────┘
            │                                     │
            ▼                                     ▼
     SCORE: -72                            SCORE: -28
     ENTRY SHORT                           EXIT SHORT
```

### AutonomousTrading Learning System

AutonomousTrading builds a **TickerProfile** for each symbol over time, learning what works specifically for that stock.

```
+===========================================================================+
|  TICKER LEARNING SYSTEM                                                   |
+===========================================================================+
|                                                                           |
|  WHAT IT LEARNS:                                                         |
|  +-------------------------------------------------------------------+   |
|  |  Optimal entry thresholds       - Score level with best win rate  |   |
|  |  Optimal exit thresholds        - When to exit for max profit     |   |
|  |  Time-of-day patterns           - Best hours/minutes to trade     |   |
|  |  Indicator correlations         - Which signals work best          |   |
|  |  Win rate at different levels   - Bucketed by entry score         |   |
|  |  Streak awareness               - More conservative after losses  |   |
|  +-------------------------------------------------------------------+   |
|                                                                           |
|  HOW IT ADAPTS:                                                          |
|  +-------------------------------------------------------------------+   |
|  |  1. After 10+ trades: Start adjusting thresholds                  |   |
|  |  2. After 20+ trades: Avoid historically poor time windows        |   |
|  |  3. After 50+ trades: Full confidence in learned patterns        |   |
|  |                                                                    |   |
|  |  Blending: Learned × Confidence + Default × (1 - Confidence)      |   |
|  +-------------------------------------------------------------------+   |
|                                                                           |
|  PERSISTENCE:                                                            |
|  +-------------------------------------------------------------------+   |
|  |  Profiles saved to: IdiotProof.Scripts\Profiles\SYMBOL.json       |   |
|  |  Accumulates across sessions - never loses learning               |   |
|  +-------------------------------------------------------------------+   |
|                                                                           |
+===========================================================================+
```

### TradeRecord Structure

Each trade records:
- Entry/exit time, price, score
- All indicator values at entry (RSI, ADX, MACD, etc.)
- Outcome (win/loss, P&L, duration)
- Used for correlation analysis

### Profile Adjustments

| Metric | Effect |
|--------|--------|
| Historical win rate > 60% | Slightly more aggressive |
| Loss streak >= 3 | Temporarily more conservative |
| Win rate at threshold 80 > threshold 70 | Adjust to 80 for this ticker |
| Time window win rate < 40% | Skip entries in that window |

## LSTM Neural Network Integration

The system includes **Long Short-Term Memory (LSTM)** neural networks for enhanced price direction prediction. LSTM networks excel at capturing temporal dependencies in sequential data like stock prices.

### How LSTM Works in IdiotProof

```
+===========================================================================+
|  LSTM ARCHITECTURE                                                        |
+===========================================================================+
|                                                                           |
|  INPUT FEATURES (from each candle):                                      |
|  +-------------------------------------------------------------------+   |
|  |  - Price (normalized)                                              |   |
|  |  - VWAP distance                                                   |   |
|  |  - EMA values (9, 21, 50)                                         |   |
|  |  - RSI, MACD, ADX                                                 |   |
|  |  - Volume ratio                                                    |   |
|  +-------------------------------------------------------------------+   |
|                                                                           |
|  LSTM CELL (memory gates):                                               |
|  +-------------------------------------------------------------------+   |
|  |  - Forget Gate: Decides what information to discard               |   |
|  |  - Input Gate: Decides what new information to store              |   |
|  |  - Output Gate: Decides what information to output                |   |
|  +-------------------------------------------------------------------+   |
|                                                                           |
|  OUTPUT:                                                                  |
|  +-------------------------------------------------------------------+   |
|  |  - Direction: -1.0 (bearish) to +1.0 (bullish)                    |   |
|  |  - Confidence: 0.0 to 1.0                                         |   |
|  |  - Predicted volatility                                           |   |
|  |  - Score adjustment: ±25 points applied to market score           |   |
|  +-------------------------------------------------------------------+   |
|                                                                           |
+===========================================================================+
```

### LSTM Integration Points

| Component | Integration |
|-----------|-------------|
| **AutonomousTrading** | LSTM prediction adjusts market score by up to ±25 points |
| **AdaptiveOrder** | LSTM volatility used for smarter TP/SL sizing |
| **Market Score** | LSTM direction/confidence factored into entry/exit decisions |

### LSTM Warm-Up Requirements

| Metric | Requirement |
|--------|-------------|
| Minimum data points | 10 candles before prediction |
| Optimal data points | 50+ candles for accurate prediction |
| Training interval | Every 15 minutes (automatic) |

### LSTM Prediction Output

```
LstmPrediction {
    Direction           // -1.0 to +1.0 (bearish to bullish)
    Confidence          // 0.0 to 1.0 (prediction confidence)
    PredictedChangePercent  // Expected price change %
    PredictedVolatility     // Expected volatility
    ScoreAdjustment     // ±25 to apply to market score
    IsUsable            // True if enough data for prediction
}
```

### Example: LSTM Score Adjustment

```
Market Score (before LSTM): +55
LSTM Prediction: Direction=+0.7, Confidence=0.8
LSTM Score Adjustment: +0.7 × 0.8 × 25 = +14

Adjusted Market Score: +55 + 14 = +69
```

### LSTM Effect on TP/SL

When LSTM predicts high volatility with high confidence:
- **Take Profit**: Extended more aggressively (expecting larger moves)
- **Stop Loss**: Widened to avoid noise stops

When LSTM predicts low volatility:
- **Take Profit**: Kept conservative (smaller expected moves)
- **Stop Loss**: Tightened (less noise expected)

### Training LSTM Models

LSTM models train automatically during live trading:
1. Data collected: Every completed candle with indicator values
2. Training triggered: Every 15 minutes
3. Model persisted: `Profiles/{TICKER}.lstm.json`

For offline training from historical data:
```csharp
var trainer = new LstmTrainingManager();
var candles = trainer.LoadTrainingData("NVDA");
var result = trainer.TrainFromHistoricalData("NVDA", candles, epochs: 20);
Console.WriteLine(result); // Shows training/validation accuracy
```

## Indicator Implementation Requirements
An IdiotScript indicator condition is "fully implemented" when it has ALL of these components:
1. **Condition class** exists (e.g., `MomentumAboveCondition` in `IndicatorConditions.cs`)
2. **`Stock.IsXxx()` method** exists in `Stock.cs` to add the condition
3. **Wire-up in `StrategyRunner.InitializeIndicatorCalculators()`** to set the callback (e.g., `GetMomentumValue`)
4. **Calculator class** exists (if needed) and is updated in `OnCandleComplete()`
5. **Context info display** in `GetEmaContextInfo()` method (shows indicator values when triggered)

### Exceptions
- **VWAP-based conditions** (IsAboveVwap, IsBelowVwap, etc.) don't need calculators - VWAP is passed directly to `Evaluate(price, vwap)`
- **Price-based conditions** (IsPriceAbove, IsPriceBelow, Entry, Breakout) don't need calculators - they use the price parameter

## Watchlist - Multi-Ticker Autonomous Trading

The **watchlist.json** file allows you to configure multiple tickers for autonomous trading in one place. Edit this file outside the app and restart to trade all configured tickers automatically.

### File Location
```
IdiotProof.Core\Scripts\watchlist.json
```

### File Format
```json
{
  "description": "My Trading Watchlist",
  "enabled": true,
  "session": "RTH",
  "tickers": [
    { "symbol": "NVDA", "quantity": 5, "name": "NVIDIA", "enabled": true },
    { "symbol": "AAPL", "quantity": 10, "name": "Apple", "enabled": true },
    { "symbol": "TSLA", "quantity": 3, "name": "Tesla", "enabled": false },
    { "symbol": "SPY", "quantity": 20, "name": "S&P 500 ETF", "enabled": true }
  ]
}
```

### Fields
| Field | Description |
|-------|-------------|
| `enabled` | Global on/off switch for all autonomous trading |
| `session` | Default session: `RTH`, `Premarket`, `AfterHours`, `Extended` |
| `tickers` | Array of ticker configurations |
| `tickers[].symbol` | Ticker symbol (e.g., "NVDA") |
| `tickers[].quantity` | Number of shares to trade |
| `tickers[].name` | Optional friendly name |
| `tickers[].enabled` | Enable/disable individual tickers |
| `tickers[].session` | Override session for this ticker |

### Usage in Code
```csharp
// Load watchlist
var watchlist = WatchlistManager.Load();

// Generate IdiotScript for each enabled ticker
foreach (var script in WatchlistManager.GenerateScripts())
{
    // script = "Ticker(NVDA).Name("NVDA Auto").Session(IS.RTH).Quantity(5).AutonomousTrading()"
}

// Print summary
WatchlistManager.PrintSummary();
```

### Quick Commands
```csharp
// Add or update a ticker
WatchlistManager.AddOrUpdate("META", 15);

// Disable without removing
WatchlistManager.Disable("TSLA");

// Remove completely
WatchlistManager.Remove("GME");
```

## Strategy Rules - Custom AI-Evaluated Trading Rules

The **strategy-rules.json** file allows you to define custom trading rules in plain text that ChatGPT evaluates alongside its indicator-based analysis. Rules work as **additional filters** - they enhance the AI's decision-making without overriding the market score logic.

### File Location
```
IdiotProof.Core\Data\strategy-rules.json
```

### How It Works
1. User defines rules as plain text (breakout levels, pullback requirements, etc.)
2. When the AI advisor evaluates a potential entry, it includes your custom rules in the prompt
3. ChatGPT analyzes entries against BOTH indicators AND your custom rules
4. The AI reports whether rules are MET, NOT_MET, or NO_RULES in its analysis

### File Format
```json
{
  "description": "Custom Strategy Rules",
  "enabled": true,
  "rules": [
    {
      "symbol": "CCHH",
      "enabled": true,
      "validUntil": "2026-02-10",
      "name": "Day 2 Breakout-Pullback",
      "rule": "Wait for breakout above $0.78, then pullback. Only enter if pullback holds above $0.70.",
      "levels": {
        "breakout": 0.78,
        "support": 0.70
      },
      "targets": [0.85, 1.00],
      "notes": "No chasing - pullback entries only"
    }
  ]
}
```

### Fields
| Field | Description |
|-------|-------------|
| `enabled` | Global on/off switch for all rules |
| `rules[].symbol` | Ticker symbol this rule applies to |
| `rules[].enabled` | Enable/disable individual rules |
| `rules[].validUntil` | **Expiration date (YYYY-MM-DD)**. Rule ignored after this date. For daily tips. |
| `rules[].name` | Friendly name for the strategy |
| `rules[].rule` | The rule in plain text - ChatGPT interprets this |
| `rules[].levels.breakout` | Price that must break for consideration |
| `rules[].levels.support` | Price that pullback must hold above |
| `rules[].targets` | Target prices for the trade |
| `rules[].notes` | Additional notes for the AI |

### AIAnalysis Response Fields
When ChatGPT evaluates with custom rules, it returns:
- `Action`: LONG, SHORT, or WAIT
- `Confidence`: 0-100
- `RuleStatus`: MET, NOT_MET, or NO_RULES
- `Reasoning`: Includes whether custom rules are satisfied
- `RiskFactors`: List of identified risks

### Example Custom Rules

```json
{
  "rules": [
    {
      "symbol": "TONN",
      "validUntil": "2026-02-10",
      "name": "Earnings Runner Day 2",
      "rule": "Price breaks above $0.94 (previous high). Wait for retest. Retest must hold above $0.90. RULE: No break = no trade.",
      "levels": { "breakout": 0.94, "support": 0.90 },
      "targets": [1.10, 1.30]
    },
    {
      "symbol": "SMX",
      "validUntil": "2026-02-10",
      "name": "High Volatility Explosive",
      "rule": "Break above $20.50, wait for pullback to $18.70. Only enter if above VWAP AND above EMAs.",
      "levels": { "breakout": 20.50, "support": 18.70 },
      "targets": [30.00, 40.00]
    }
  ]
}
```

### Usage in Code
```csharp
// Load rules
var config = StrategyRulesManager.Load();

// Get rules for a specific symbol
var rules = config.GetRulesForSymbol("CCHH");

// Get formatted rules for AI prompt
string rulesPrompt = StrategyRulesManager.GetRulesForPrompt("CCHH");

// Add or update a rule
StrategyRulesManager.AddOrUpdate(new StrategyRule { Symbol = "AAPL", Rule = "..." });

// Print summary
StrategyRulesManager.PrintSummary();
```

## Note
Ignore the IdiotProof.Frontend project for now - it has build errors related to HeartbeatMessage that will be addressed later.

## Repeat Command
- Use the `Repeat()` command to restart the strategy after it has been completed through Take Profit, Stop Loss, or Trailing Stop Loss. This allows the strategy to re-enter when conditions are met again after an exit.