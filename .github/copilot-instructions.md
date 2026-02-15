# Copilot Instructions

## Project Architecture

**IdiotProof.Core** is a **headless background service** with NO console UI. All user interaction happens via **IdiotProof.Web** (Blazor frontend). Core should never have `Console.ReadLine`, `Console.ReadKey`, interactive menus, or user prompts.
┌─────────────────────────────────────────────────────────────────┐
│  IdiotProof.Web (Blazor)                                        │
│  - All UI/UX: Dashboard, Trade, Analyze, Backtest, Orders       │
│  - Log tab: Shows messages from Core                            │
│  - Sends commands to Core via SignalR/HTTP                      │
└───────────────────────────▲─────────────────────────────────────┘
                            │ HTTP API + SignalR
┌───────────────────────────▼─────────────────────────────────────┐
│  IdiotProof.Core (Headless Engine)                              │
│  - IBKR API connection                                          │
│  - Strategy execution (StrategyRunner)                          │
│  - Market data streaming to Web                                 │
│  - Receives commands: ActivateTrading, ClosePosition, etc.      │
│  - NO console menus or interactive prompts                      │
└─────────────────────────────────────────────────────────────────┘

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

## Alert System - Multi-Channel Notifications

The **Alert System** detects sudden price moves and notifies you via Discord, Email, SMS, or Telegram with **pre-calculated trade setups ready for one-click execution**.

### The Problem It Solves
> "By the time I notice a move, analyze it, calculate R:R, and fill in the order form, I'm just chasing the stock."

### The Solution
1. System detects sudden moves **INSTANTLY**
2. Pre-calculates LONG and SHORT setups with SL/TP/R:R
3. Sends alert with **ONE-CLICK execution** details
4. You just click "GO" - no scrambling

### Alert Channels
| Channel | Setup | Cost |
|---------|-------|------|
| **Discord** | Create webhook in server settings | FREE |
| **Email** | SMTP server (Gmail works) | FREE |
| **SMS** | Twilio account | ~$0.01/msg |
| **Telegram** | Create bot via @BotFather | FREE |

### Alert Message Format (Discord)🚨 **NVDA SUDDEN SPIKE** 🚨

**Price:** $142.50 (+5.2% in 3min)
**Volume:** 3.5x average
**Confidence:** 85%

💡 **Analysis:** Price spiking UP, accelerating (1.8x faster), high volume (3.5x avg)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📈 **LONG Setup** (ID: `a1b2c3d4`)
Entry:   $142.50
Stop:    $139.75 (-1.9%)
Target:  $149.38 (+4.8%)
Qty:     17 shares
Risk:    $46.75
Reward:  $116.96
R:R:     2.5:1

📉 **SHORT Setup** (ID: `e5f6g7h8`)
Entry:   $142.50
Stop:    $145.50 (+2.1%)
Target:  $135.00 (-5.3%)
Qty:     16 shares
Risk:    $48.00
Reward:  $120.00
R:R:     2.5:1

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
⏱️ Setups valid for 5 minutes | Alert ID: `12345678`
### Configuration File
Edit `IdiotProof.Core\Data\alert-config.json`:{
  "discord": {
    "enabled": true,
    "webhookUrl": "https://discord.com/api/webhooks/..."
  },
  "detection": {
    "minPercentChange": 3.0,
    "timeWindowMinutes": 3,
    "minVolumeRatio": 2.0,
    "minConfidence": 60,
    "cooldownMinutes": 5
  }
}
### Key Files
- `AlertService.cs` - Multi-channel alert sending
- `SuddenMoveDetector.cs` - Price spike detection
- `AlertConfigManager.cs` - Configuration loading
- `alert-config.json` - User configuration

## Gapper Scanner - Premarket Gap Detection

The **Gapper Scanner** monitors your watchlist for stocks gapping up/down during premarket. **YOU make the final trading decision** - it just alerts you with confidence scores.

### What It Tracks
| Metric | Description | Weight |
|--------|-------------|--------|
| **Gap %** | Price change from previous close | 0-30 pts |
| **Volume** | Premarket volume vs daily average | 0-30 pts |
| **Momentum** | Is price building or fading? | 0-25 pts |
| **Holding** | Is it near premarket high or fading? | 0-15 pts |

### Confidence Grades
| Score | Grade | Meaning |
|-------|-------|---------|
| 80-100 | A | Strong gapper - high conviction setup |
| 65-79 | B | Good gapper - worth watching closely |
| 50-64 | C | Moderate - needs more confirmation |
| 35-49 | D | Weak - probably avoid |
| 0-34 | F | Not a real gapper |

### Scanner Output╔══════════════════════════════════════════════════════════════════════════╗
║  GAPPER ALERT: NVDA   ↑+8.2%  $142.50  Vol:3.2x  Conf:85%               ║
║  Building momentum | 45 min to open | Action: YOUR CALL                 ║
║  Gap:25/30  Vol:25/30  Mom:20/25  Hold:15/15                            ║
╚══════════════════════════════════════════════════════════════════════════╝
### Scanner Commands
| Key | Action |
|-----|--------|
| **R** | Refresh summary table |
| **1-9** | Quick trade ticker by number |
| **H** | Hedge analysis (dual-account) |
| **Q** | Quit scanner |

### Quick Trade Feature
Press a number key (1-9) to quick trade a gapper. The system auto-calculates:
- **Entry Price**: Current price
- **Stop Loss**: Below premarket low or 2% (whichever is tighter)
- **Take Profit**: 2.5x risk distance
- **Trailing Stop**: Based on volatility (1-2%)
- **Quantity**: Based on $50 risk (configurable)
╔══════════════════════════════════════════════════════════════════════════╗
║  QUICK TRADE: NVDA   LONG                                                ║
╠══════════════════════════════════════════════════════════════════════════╣
║  Entry:     $142.50                                                      ║
║  Stop Loss: $139.75 (-1.9%)                                              ║
║  Take Profit: $149.38                                                    ║
║  Trailing Stop: 1.5%                                                     ║
╠══════════════════════════════════════════════════════════════════════════╣
║  Quantity: 18 shares                                                     ║
║  Risk: $49.50  |  Reward: $123.84  |  R:R = 2.5                         ║
╠══════════════════════════════════════════════════════════════════════════╣
║  [ENTER] Execute Trade  |  [ESC] Cancel                                  ║
╚══════════════════════════════════════════════════════════════════════════╝
### Dual-Account Hedging
Press **H** for hedge analysis. If enabled, places:
- **Primary Account**: LONG position
- **Secondary Account**: SHORT position

Profits from movement in either direction. Skips if price is too choppy.
// Enable in AppSettings
AppSettings.DualAccountHedgingEnabled = true;
AppSettings.AccountNumber = "U22434144";        // Primary (LONG)
AppSettings.SecondaryAccountNumber = "U23270497"; // Secondary (SHORT)
### Key Files
- `GapperScanner.cs` - Core scanner logic
- `GapperInfo` - Per-ticker tracking data
- `GapperConfidence` - Confidence breakdown
- `QuickTradeLevels' - Auto-calculated entry/SL/TP
- `QuickTradeCalculator` - Level calculation logic

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

### Order Direction SyntaxOrder()               - Opens a LONG position (default)
Order(IS.LONG)        - Opens a LONG position (explicit)
Order(IS.SHORT)       - Opens a SHORT position
Long()                - Alias for Order(IS.LONG)
Short()               - Alias for Order(IS.SHORT)
### Order Configuration (Chained Methods).Quantity(100)        - Sets order quantity
.PriceType(Price.Current)  - Sets price type for execution
.OrderType(OrderType.Market)  - Sets market vs limit order
.OutsideRTH()         - Allow entry order outside RTH (default: true)
.TakeProfitOutsideRTH()  - Allow TP order outside RTH (default: true)
### Position ClosingCloseLong()           - Create SELL order to close a long position
CloseShort()          - Create BUY order to cover a short position
**Example - Single Responsibility Chain:**Stock.Ticker("AAPL")
    .Entry(150)
    .Long()                // Sets direction only
    .Quantity(100)         // Sets quantity separately
    .PriceType(Price.VWAP) // Sets price type separately
    .OutsideRTH()          // Enables outside RTH for entry
    .TakeProfitOutsideRTH()  // Enables outside RTH for take profit
    .TakeProfit(160)
    .Build();
**Legacy Syntax (Deprecated but still works):**Buy()                 - Deprecated, use Order(IS.LONG)
Sell()                - Deprecated, use Order(IS.SHORT)
## All Available IdiotScript Indicators

### Price/VWAP ConditionsIsAboveVwap()         - Price above VWAP
IsBelowVwap()         - Price below VWAP
IsCloseAboveVwap()    - Candle CLOSED above VWAP (stronger signal)
IsVwapRejection()     - Wick above VWAP, close below (bearish rejection)
IsVwapRejected()      - Alias for IsVwapRejection()
### Gap ConditionsIsGapUp(pct)          - Price gapped up X% from previous close (e.g., IsGapUp(5) = 5%+)
IsGapDown(pct)        - Price gapped down X% from previous close (e.g., IsGapDown(3) = 3%+)
### EMA ConditionsIsEmaAbove(period)    - Price above EMA (e.g., IsEmaAbove(9))
IsEmaBelow(period)    - Price below EMA
IsEmaBetween(p1, p2)  - Price between two EMAs (e.g., IsEmaBetween(9, 21))
IsEmaTurningUp(period)- EMA slope turning positive/flat
### Momentum ConditionsIsMomentumAbove(val)  - Momentum >= threshold (e.g., IsMomentumAbove(0))
IsMomentumBelow(val)  - Momentum <= threshold
IsRocAbove(pct)       - Rate of Change >= % (e.g., IsRocAbove(2))
IsRocBelow(pct)       - Rate of Change <= %
### Trend/Strength ConditionsIsAdxAbove(val)       - ADX >= threshold (trend strength)
IsDiPositive()        - +DI > -DI (bullish directional movement)
IsDiNegative()        - -DI > +DI (bearish directional movement)
IsMacdBullish()       - MACD > Signal line
IsMacdBearish()       - MACD < Signal line
### RSI ConditionsIsRsiOversold(val)    - RSI <= threshold (e.g., IsRsiOversold(30))
IsRsiOverbought(val)  - RSI >= threshold (e.g., IsRsiOverbought(70))
### Pattern ConditionsIsHigherLows()        - Higher lows forming (bullish)
IsVolumeAbove(mult)   - Volume >= multiplier × average (e.g., IsVolumeAbove(1.5))
### Smart Order ManagementAdaptiveOrder()                   - Enable smart dynamic TP/SL adjustment (balanced mode)
AdaptiveOrder(IS.CONSERVATIVE)    - Protect gains, quick to take profits
AdaptiveOrder(IS.BALANCED)        - Equal priority to profit and protection  
AdaptiveOrder(IS.AGGRESSIVE)      - Maximize profit potential in strong trends
IsAdaptiveOrder()                 - Alias for AdaptiveOrder()
## AdaptiveOrder - Smart Dynamic Order Management

AdaptiveOrder monitors market conditions in real-time and dynamically adjusts take profit and stop loss levels.

### How It Works╔═══════════════════════════════════════════════════════════════════════════╗
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
### Adaptive Behavior Table╔═══════════════════════════════════════════════════════════════════════════╗
║  SCENARIO               │  TAKE PROFIT         │  STOP LOSS              ║
║─────────────────────────┼──────────────────────┼─────────────────────────║
║  Strong bullish (70+)   │  Extend +50%         │  Tighten (protect gain) ║
║  Moderate bull (30-70)  │  Keep original       │  Keep original          ║
║  Neutral (-30 to 30)    │  Reduce 25%          │  Widen (allow bounce)   ║
║  Moderate bear (-70-30) │  Reduce 50%          │  Keep original          ║
║  Strong bearish (<-70)  │  EXIT IMMEDIATELY    │  N/A - Emergency exit   ║
╚═══════════════════════════════════════════════════════════════════════════╝
### Example Strategy with AdaptiveOrderTicker(AAPL)
.Entry(150)
.TakeProfit(160)         # Original TP target
.StopLoss(145)           # Original SL level
.IsAboveVwap()
.IsEmaAbove(9)
.IsDiPositive()
.AdaptiveOrder(IS.AGGRESSIVE)  # System will dynamically adjust TP/SL
**Note:** AdaptiveOrder requires TakeProfit and/or StopLoss to be set. It modifies these values dynamically but needs starting points for calculation.

### How AdaptiveOrder Calculates Adjustments

#### Score-Based Take Profit Multiplier
The TP multiplier determines how much to extend or reduce the original take profit target:
Score Range        │ Multiplier Formula                          │ Example (TP=$160, Entry=$150)
───────────────────┼─────────────────────────────────────────────┼──────────────────────────────
70 to 100          │ 1.0 + (MaxExtension × (score-70)/30)        │ Score 100: TP → $167.50 (+75%)
30 to 70           │ 1.0 (no change)                             │ Score 50:  TP → $160.00
-30 to 30          │ 1.0 - (0.15 × (30-score)/60)                │ Score 0:   TP → $158.75 (-12.5%)
-70 to -30         │ 0.85 - ((MaxReduction/2-0.15) × (-30-s)/40) │ Score -50: TP → $156.25 (-37.5%)
< -70              │ 1.0 - MaxReduction                          │ Score -80: TP → $155.00 (-50%)
#### Score-Based Stop Loss Multiplier
The SL multiplier determines how much to tighten (move closer) or widen (move further) the stop:
Score Range        │ Multiplier Formula                          │ Example (SL=$145, Entry=$150)
───────────────────┼─────────────────────────────────────────────┼──────────────────────────────
70 to 100          │ 1.0 + (MaxTighten × (score-70)/30)          │ Score 100: SL → $147.50 (tighter)
0 to 70            │ 1.0 (no change)                             │ Score 50:  SL → $145.00
-50 to 0           │ 1.0 - (MaxWiden × (-score/50) × 0.5)        │ Score -25: SL → $144.38 (wider)
< -50              │ 1.0 - (MaxWiden × 0.5)                      │ Score -60: SL → $144.38 (wider)
Note: Multiplier > 1.0 = tighter stop (closer to entry), Multiplier < 1.0 = wider stop (further from entry)

### Mode Configuration Settings╔═══════════════════════════════════════════════════════════════════════════════════╗
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

### Indicator Weight Configuration (Default)VWAP Position:    15%  - Price relative to VWAP (5% above VWAP = +100 score)
EMA Stack:        20%  - Alignment of short/medium/long EMAs
RSI:              15%  - Overbought (70+) = negative, Oversold (30-) = positive
MACD:             20%  - MACD > Signal = +50, plus histogram strength ±50
ADX:              20%  - Trend strength × direction (ADX 50 = ±100)
Volume:           10%  - Volume ratio confirms current direction
────────────────────
Total:           100%
### Concrete Example: CIGL Adaptive Trade
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
### Adaptive TP Feedback Loop (Smart Trailing Take Profit)

The system extends TP when momentum is strong, then contracts it when momentum fades, allowing the price to eventually meet the target:
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
║  ├── RSI: Becoming overbought (70+) → strong negative                       ║
║  └── Result: Total Score drops to 30-69 → TP RETURNS TO ORIGINAL             ║
╠═══════════════════════════════════════════════════════════════════════════════╣
║  MOMENTUM EXHAUSTED                                                           ║
║  ├── MACD: Bearish crossover imminent                                        ║
║  ├── RSI: Overbought (75+) → strong negative                                 ║
║  └── Result: Score drops to 0-29 → TP REDUCES (multiplier 0.85-0.925)        ║
║      → Price finally meets the lowered TP target → PROFIT TAKEN              ║
╚═══════════════════════════════════════════════════════════════════════════════╝
### Timeline Example: Dynamic TP Adjustment
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
### TP Extension Visualization
Price
  ↑
$165 ─ ─ ─ ─ ─ ─ ─ ─ Extended TP (score 90+)
      │            ╱
$163 ─│─ ─ ─ ─ ─ ╱─ ─ ─ TP following momentum
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
## Indicator ASCII Visualizations

### VWAP Rejection Pattern      │ ← Wick above VWAP
VWAP ═╪═══════════════════════════
    ┌─┴─┐
    │   │ ← Close below = REJECTED
    └───┘
### Higher Lows Pattern                         /\
             /\         /  \
   /\       /  \       /    \
  /  \     /    \     /      \
 /    \___/  ↑   \___/        \
       Higher    Higher
         Low       Low
### Momentum Above Zero    /\                    /\
   /  \                  /  \     Price
  /    \                /    \
─/──────\──────────────/──────\────── Zero
         \            /        \
          \__________/
      ↑ Momentum > 0
### Volume Spike               ████
               ████   ← Volume spike (1.5x+)
Average ───────────────────────────
  ████      ████████      ████
  ████  ██  ████████  ██  ████
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

## Opening Bell Pattern Detection (Autonomous Trading)

The system automatically detects patterns around the RTH open (9:30 AM ET) to avoid traps and capitalize on clean setups.

### Pattern 1: First Candle Trap (9:30-9:31)
The first RTH candle is extremely volatile - even if it moves up, chances of closing profitable are low.
- **Action**: SKIP ALL ENTRIES during 9:30-9:31 candle
- **Filter**: `IsFirstRthCandle` → Entry threshold increased by 100 (effectively blocks entry)

### Pattern 2: Premarket Green Rush Warning
If a stock shows **3+ consecutive green candles** in the last 5 minutes of premarket (9:25-9:30), it's likely to crash after RTH opens.
- **Detection**: `HasPremarketGreenRushWarning` → true if 3+ green candles at end of premarket
- **Action**: EXIT any LONG position 3 minutes before RTH bell (9:27-9:30)
- **Reason**: Extended runs into open typically reverse hard
      Premarket End (9:25-9:30)              RTH Open
           ↓                                    ↓
    ████ ████ ████ ████ ████    |    █
    [G]  [G]  [G]  [G]  [G]     |    █ <-- CRASH!
                                |    █
    ← GREEN RUSH WARNING →      |    █████
         EXIT HERE!             |
### Pattern 3: Clean Rocket Pattern  
If premarket is clean (no green rush) AND stock rockets up after open, BUY for the move to HOD, then SHORT the fade.
- **Detection**: `HasCleanPremarket` + above VWAP + first 15 min (9:32-9:45)
- **Action**: Boost LONG entry confidence (threshold reduced by 15)
- **Follow-up**: When price reaches HOD, the Early HOD Exit logic kicks in and flips to SHORT
      Premarket                 RTH Open           HOD
           ↓                       ↓               ↓
    ██ ██ ██ ██ ██    |    ██ ████ ████████ ████████ ***
    [G][R][G][R][G]   |       ↑                      ↑
                      |    BUY here              SELL + SHORT
    ← CLEAN (mixed) → |    ← CLEAN ROCKET PATTERN →
### Pattern 4: RTH Volatility Window (9:30-9:32)
Reduced confidence during the first 2 minutes of RTH.
- **Action**: Entry threshold increased by 15 points
- **Reason**: Higher chance of false signals during initial volatility

### OpeningBellAnalysis Object
The `CandlestickAggregator.GetOpeningBellAnalysis(price, vwap)` returns:{
    IsFirstRthCandle: bool,        // 9:30-9:31 trap zone
    IsRthVolatilityWindow: bool,   // 9:30-9:32 reduced confidence
    HasGreenRushWarning: bool,     // Exit long signal
    PremarketGainPercent: double,  // % gain in last 5 min of premarket
    PremarketGreenCandles: int,    // Count of green candles (9:25-9:30)
    HasCleanPremarket: bool,       // No warning signs
    Recommendation: OpeningBellAction  // Suggested action
}
### OpeningBellAction EnumNormalTrading,       // Standard trading rules apply
HoldAndWatch,        // Premarket - wait for signals
ExitBeforeBell,      // Green rush detected - exit longs!
AvoidFirstCandle,    // 9:30-9:31 - skip entries
ReducedConfidence,   // 9:30-9:32 - stricter thresholds
CleanRocketBuy       // Clean premarket + above VWAP - buy opportunity
## Auto-Quantity Based on Price Tier

The default position size is now calculated automatically based on stock price "prestige":

| Price Tier | Price Range | Target Position | Example Qty |
|------------|-------------|-----------------|-------------|
| Premium | $500+ | ~$1,000 | 2 shares @ $500 |
| Blue Chip | $100-$500 | ~$1,000 | 5 shares @ $200 |
| Mid-Cap | $25-$100 | ~$600 | 12 shares @ $50 |
| Small-Cap | $5-$25 | ~$350 | 35 shares @ $10 |
| Penny | $1-$5 | ~$200 | 80 shares @ $2.50 |
| Micro | <$1 | ~$100 | 200 shares @ $0.50 |

To use auto-quantity, set `Quantity(0)` or omit `.Quantity()`:Ticker(NVDA)
.AutonomousTrading()
// Quantity auto-calculated: ~2 shares for ~$1,000 position
To override with explicit quantity:Ticker(NVDA)
.Quantity(10)  // Explicit: 10 shares regardless of price
.AutonomousTrading()
## ExitStrategy and IsProfitable (Single-Responsibility Pattern)
Use `ExitStrategy()` for exit timing and chain `.IsProfitable()` to only exit if profitable:ExitStrategy(IS.BELL)                    # Exit at session bell
ExitStrategy(IS.BELL).IsProfitable()     # Exit at bell only if profitable
ExitStrategy(15:30).IsProfitable()       # Exit at 3:30 PM only if profitable
## ASCII-Only Console Output
The console UI uses ASCII-only characters (no Unicode). Use:
- `*` for enabled, `o` for disabled
- `[OK]` for success, `[ERR]` for errors
- `+`, `-`, `|`, `=` for box drawing

## Complete Indicator ASCII Reference

### RSI (Relative Strength Index)RSI Scale (0-100):
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
### ADX (Average Directional Index)ADX Trend Strength:
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
### MACD (Moving Average Convergence Divergence)MACD Components:
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
### EMA (Exponential Moving Average)Price vs Multiple EMAs:
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
### Rate of Change (ROC)ROC % Scale:
+--------------------------------------------+
|  +5% ----- Strong Bullish Momentum ------- |
|  +2% =============== THRESHOLD =========== | <- IsRocAbove(2)
|   0% ------------ Neutral ---------------- |
|  -2% =============== THRESHOLD =========== | <- IsRocBelow(-2)
|  -5% ----- Strong Bearish Momentum ------- |
+--------------------------------------------+

ROC = ((Price - Price_N_bars_ago) / Price_N_bars_ago) × 100
### IsCloseAboveVwap (Strong VWAP Signal)IsCloseAboveVwap vs IsAboveVwap:
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
### Combined Strategy VisualizationBullish Continuation Setup:
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
## Indicator Combinations (Common Strategies)

### Trend FollowingTicker(AAPL)
.IsAdxAbove(25)      # Strong trend exists
.IsDiPositive()      # Bullish direction
.IsEmaAbove(9)       # Short-term bullish
.IsEmaAbove(21)      # Medium-term bullish
.IsMomentumAbove(0)  # Positive momentum
### Mean ReversionTicker(AAPL)
.IsRsiOversold(30)   # Oversold condition
.IsEmaBetween(9, 21) # In pullback zone
.IsVolumeAbove(1.5)  # Volume confirmation
### VWAP BounceTicker(AAPL)
.IsCloseAboveVwap()  # Strong VWAP reclaim
.IsEmaAbove(9)       # Short-term bullish
.IsHigherLows()      # Building support
## AutonomousTrading - AI-Driven Entry/Exit Decisions

AutonomousTrading enables fully automated trading where the system monitors all indicators and independently decides when to buy, sell, short, or close positions based on market score analysis.

### Quick StartTicker(AAPL)
.AutonomousTrading()
That's it! The system will:
1. Monitor VWAP, EMA, RSI, MACD, ADX, and Volume continuously
2. Calculate a market score (-100 to +100)
3. **Self-adjust thresholds** based on market conditions (more aggressive in strong trends, conservative when ranging)
4. Enter LONG when score >= dynamic threshold, SHORT when score <= negative threshold
5. Exit when momentum reverses
6. Flip direction (exit long → enter short) on strong reversals

### AutonomousTrading SyntaxAutonomousTrading()       - Enable self-adjusting autonomous trading
IsAutonomousTrading()     - Alias for AutonomousTrading()
**Note:** Any mode parameters (IS.CONSERVATIVE, IS.BALANCED, etc.) are accepted for backwards compatibility but ignored. The system always self-adjusts.

### Self-Adjusting Thresholds

The system automatically adjusts entry/exit thresholds based on real-time market conditions:
+============================================================================+
|  DYNAMIC THRESHOLD CALCULATION                                             |
+============================================================================+
|                                                                            |
|  Adjustment Factor        | Effect on Thresholds          | Reasoning     |
|  ─────────────────────────┼───────────────────────────────┼───────────────|
|  ADX >= 40 (strong trend) | Long: 50, Short: -50          | Aggressive    |
|  ADX 25-40 (moderate)     | Long: 60, Short: -60          | Slightly agg  |
|  ADX < 20 (ranging)       | Long: 75, Short: -75          | Conservative  |
|  ─────────────────────────┼───────────────────────────────┼───────────────|
|  ATR > 5% (high vol)      | +10 to thresholds             | Conservative  |
|  ATR 3-5% (moderate)      | +5 to thresholds              | Slight cons   |
|  ATR < 1% (low vol)       | -5 to thresholds              | Aggressive    |
|  ─────────────────────────┼───────────────────────────────┼───────────────|
|  Indicators 80%+ agree    | -10 to thresholds             | Aggressive    |
|  Indicators 60-80% agree  | -5 to thresholds              | Slight agg    |
|  Indicators < 40% agree   | +10 to thresholds             | Conservative  |
|  ─────────────────────────┼───────────────────────────────┼───────────────|
|  RSI > 75 (overbought)    | Long threshold +15            | Careful long  |
|  RSI < 25 (oversold)      | Short threshold -15           | Careful short |
|  ─────────────────────────┼───────────────────────────────┼───────────────|
|  First 15 minutes         | +10 to thresholds             | Volatility    |
|  Last 30 minutes          | +5 to thresholds              | EOD caution   |
|                                                                            |
+============================================================================+

Example: NVDA during strong uptrend (ADX=45, ATR=2%, Indicators=85% bullish)
  → Long threshold: 65 - 15 (ADX) - 10 (agreement) = 40 (very aggressive)
  → The system enters earlier when conditions strongly favor the trade

Example: SPY during ranging market (ADX=18, ATR=0.8%, mixed signals)
  → Long threshold: 65 + 10 (ranging) - 5 (low vol) + 10 (mixed) = 80 (conservative)
  → The system waits for stronger confirmation before entering
**Recommended Usage:**Ticker(NVDA)
.Name("NVDA Autonomous")
.Session(IS.RTH)
.Quantity(5)
.AutonomousTrading()
### How AutonomousTrading Works
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
### Self-Adjusting Behavior

The system automatically adjusts its behavior based on market conditions. There are no modes to configure - it's always optimal:
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
### Indicator Score Calculation
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
### AutonomousTrading vs AdaptiveOrder
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
### When Does AutonomousTrading Enter SHORT?

The system enters SHORT whenever the market score drops to **-70 or below** - it's NOT limited to HOD rejection. Common SHORT entry scenarios:

| Scenario | Description | Score Impact |
|----------|-------------|--------------|
| **Below VWAP + Falling** | Price well below VWAP with downward momentum | VWAP: -60, EMA: -80, MACD: -70 |
| **HOD Rejection** | Price hits high and reverses sharply | Triggers bearish indicators |
| **Failed Breakout** | Breaks level then fails back below | Momentum shifts negative |
| **Bearish Gap Down** | Opens weak and continues selling | All indicators start negative |

**Example: Stock falling below VWAP (not at HOD)**Price: $48.50 (2% below VWAP of $49.50)
+----------------------------------------------------------------+
| VWAP Score:   -60  (well below VWAP)                           |
| EMA Score:    -80  (below 9, 21, 50 EMAs)                      |
| MACD Score:   -70  (bearish cross, negative histogram)         |
| ADX Score:    -50  (ADX 35 with -DI > +DI)                     |
| RSI Score:    +20  (approaching oversold - slight positive)    |
| Volume Score: -30  (volume confirming down move)               |
+----------------------------------------------------------------+
| Total Score:  -78  --> SHORT ENTRY (below -70 threshold)       |
+----------------------------------------------------------------+
**Key Point**: The system shorts based on indicator alignment, not chart location. A stock can be far from HOD and still trigger a SHORT if bearish indicators are strong enough.

### When Does AutonomousTrading Enter LONG?

The system enters LONG whenever the market score rises to **+70 or above**. Common LONG entry scenarios:

| Scenario | Description | Score Impact |
|----------|-------------|--------------|
| **Above VWAP + Rising** | Price well above VWAP with upward momentum | VWAP: +60, EMA: +80, MACD: +70 |
| **LOD Bounce** | Price hits low and reverses sharply | Triggers bullish indicators |
| **Breakout Confirmation** | Breaks resistance with volume | Momentum shifts positive |
| **Bullish Gap Up** | Opens strong and continues buying | All indicators start positive |

**Example: Stock rising above VWAP**Price: $51.50 (3% above VWAP of $50.00)
+----------------------------------------------------------------+
| VWAP Score:   +80  (well above VWAP)                           |
| EMA Score:    +70  (above 9, 21, 50 EMAs)                      |
| MACD Score:   +60  (bullish cross, positive histogram)         |
| ADX Score:    +55  (ADX 40 with +DI > -DI)                     |
| RSI Score:    -10  (slightly overbought - minor negative)      |
| Volume Score: +40  (volume confirming up move)                 |
+----------------------------------------------------------------+
| Total Score:  +75  --> LONG ENTRY (above +70 threshold)        |
+----------------------------------------------------------------+
### Real-World Example: UBER Chart Analysis
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
### Key Signals That Drove Decisions
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
### AutonomousTrading Learning System

AutonomousTrading builds a **TickerProfile** for each symbol over time, learning what works specifically for that stock.

```
╔══════════════════════════════════════════════════════════════════════════════╗
║  TICKER PROFILE - NVDA                                                       ║
╠════════════════╦══════════╦═════════════╦═════════════════╦═══════════════╣
║ DATE           │ SCORE    │ POSITION    │ RISK            │ REWARD        ║
╠════════════════╬══════════╬═════════════╬═════════════════╬═══════════════╣
║ 2026-02-07     │ +75      │ LONG        │ $2.00           │ $5.00         ║
║ 2026-02-08     │ +80      │ LONG        │ $1.80           │ $4.50         ║
║ 2026-02-09     │ -30      │ SHORT       │ $2.50           │ $6.00         ║
║ 2026-02-10     │ +90      │ LONG        │ $2.20           │ $5.50         ║
╠════════════════╬══════════╬═════════════╬═════════════════╬═══════════════╣
║ AVERAGE         │ +53      │             │ $2.13           │ $5.50         ║
║ STDEV            │ 26       │             │ $0.25           │ $0.25          ║
╚════════════════╩══════════╩═════════════╩═════════════════╩═══════════════╝
```

### Ticker Profile Fields
- `DATE`: Date of the trade or analysis
- `SCORE`: Market score at the time (+100 to -100)
- `POSITION`: LONG, SHORT, or FLAT
- `RISK`: Risk amount per share
- `REWARD`: Target reward per share

### Usage
```csharp
// Load profile for a ticker
var profile = TickerProfileManager.Load("NVDA");

// Get average score for trade decisions
var avgScore = profile.GetAverageScore();

// Determine position size based on risk
var positionSize = profile.GetPositionSize(currentPrice, riskAmount);
```


## Note
Ignore the IdiotProof.Frontend project for now - it has build errors related to HeartbeatMessage that will be addressed later.

## Repeat Command
- Use the `Repeat()` command to restart the strategy after it has been completed through Take Profit, Stop Loss, or Trailing Stop Loss. This allows the strategy to re-enter when conditions are met again after an exit.