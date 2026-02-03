# Copilot Instructions

## Project Guidelines
- User intends IdiotScript chained conditions to be evaluated sequentially (state machine over time), not as a single simultaneous AND condition.
- In IdiotProof, the backend doesn't load strategies directly; it reads strategies retrieved from the frontend, which gets them from .idiot files. The data flow is: .idiot files → Frontend → Backend.
- IdiotScript commands should always include parentheses, even for flag-style commands without parameters (e.g., `AboveVwap()` not `AboveVwap`, `Breakout()` not `Breakout`). The parser accepts both forms for backwards compatibility, but the serializer outputs with parentheses.

## Indicator Warm-Up Requirements
Technical indicators (EMA, ADX, RSI, etc.) require historical price bars to calculate properly.
The backend uses 1-minute OHLC bars. **Start the backend early** to collect bars before trading.

| Indicator      | Bars Needed | Start Early By |
|----------------|-------------|----------------|
| EMA(9)         | 9 bars      | 10 minutes     |
| EMA(21)        | 21 bars     | 25 minutes     |
| EMA(200)       | 200 bars    | 3+ hours       |
| ADX(14)        | 28 bars     | 30 minutes     |
| RSI(14)        | 15 bars     | 20 minutes     |
| MACD(12,26,9)  | 35 bars     | 40 minutes     |
| DI (+DI/-DI)   | 28 bars     | 30 minutes     |

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