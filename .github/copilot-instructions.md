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

## Note
Ignore the IdiotProof.Frontend project for now - it has build errors related to HeartbeatMessage that will be addressed later.