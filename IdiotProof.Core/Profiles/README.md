# Ticker Profiles

This folder contains learned ticker profiles for autonomous trading.

## What Gets Stored

Each backtest run saves a JSON file per ticker with:

- **Trade Statistics**: Win rate, profit factor, total P&L
- **Optimal Thresholds**: Learned entry/exit score levels
- **Time Patterns**: Best/worst hours to trade this ticker
- **Indicator Correlations**: Which indicators predict wins
- **Streak Data**: Current/longest win/loss streaks

## How Learning Works

1. **First Backtest**: Baseline profile created with default thresholds
2. **Subsequent Backtests**: Profile is updated, thresholds refined
3. **Live Trading**: Learned patterns adjust trading decisions:
   - Entry thresholds blend toward learned optimal values
   - Poor time windows are avoided
   - Conservative after loss streaks, aggressive after wins

## File Format

```
{symbol}.json
```

Example: `NVDA.json`, `AAPL.json`, `TSLA.json`

## Usage

- Run `4. Backtest` from the menu to build/update profiles
- Run `9. Profiles` to view learned data
- Profiles accumulate over time - more backtests = better accuracy
- Delete a JSON file to reset learning for that ticker
