# TODO

## Strategy Ghost Overlay + Branching Visualization

**Goal:** Author a strategy, press play, and watch it unfold on the chart as a translucent "ghost" trade path. When the strategy hits a condition, fork the ghost into pass/fail branches so the full decision tree is visible in-place.

### Prerequisites (not yet wired)
- [ ] Add TradingView Lightweight Charts to `IdiotProof.Frontend/wwwroot/` (script + interop JS)
- [ ] Create `IdiotProof.Frontend/Components/Chart.razor` wrapping the chart via JS interop
- [ ] Feed candles from `IdiotProof.Core/Services/HistoricalDataService.cs` (IBKR source) into the chart — SignalR for live, REST for historical
- [ ] Mount the chart on `TickerWorkspace.razor`

### Ghost replay
- [ ] Extend `IdiotProof.Core/Services/StrategySimulator.cs` to emit a timeline of evaluation events (timestamp, condition evaluated, pass/fail, branch taken, entry/exit)
- [ ] Render the simulator output as a translucent overlay series on the chart (entries, exits, stops)
- [ ] Scrub/playback controls (play, pause, step, speed)

### Branching
- [ ] Map `StrategyDefinition.ConditionalBlocks` / `ConditionalBranch` (IdiotScript) onto the ghost timeline
- [ ] At each branch point, fork the ghost into visible pass/fail paths (different colors / dashed for the non-taken branch)
- [ ] Hover/click a branch node to show the condition that was evaluated and the values at that moment

### Nice-to-haves
- [ ] Compare multiple ghost runs side-by-side (e.g., same strategy across different tickers or dates)
- [ ] Export the branch tree as a standalone diagram
