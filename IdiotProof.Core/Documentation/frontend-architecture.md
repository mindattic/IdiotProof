# IdiotProof Frontend Architecture

## Vision
A loosely-coupled web frontend that provides:
1. **TradingView Charts** - Real-time charting with drawing tools
2. **Natural Language Commands** - "Buy 100 NVDA at market" via ChatGPT
3. **Visual Analysis** - Screenshot/draw → ask "What do you see?"
4. **Real-time Communication** - SignalR for instant updates

## Technology Stack

### Frontend
- **Blazor WebAssembly** or **React** (your choice)
- **TradingView Lightweight Charts** (FREE - Apache 2.0 license)
  - GitHub: https://github.com/tradingview/lightweight-charts
  - NPM: `npm install lightweight-charts`
- **SignalR Client** for real-time data

### Backend (IdiotProof.Core)
- **SignalR Hub** for real-time communication
- **REST API** for commands and queries
- **ChatGPT Integration** (already exists) for natural language

## TradingView Lightweight Charts

### License: Apache 2.0 (FREE for commercial use)

```html
<script src="https://unpkg.com/lightweight-charts/dist/lightweight-charts.standalone.production.js"></script>
```

```javascript
const chart = LightweightCharts.createChart(document.getElementById('chart'), {
    width: 800,
    height: 400,
    layout: {
        backgroundColor: '#1e1e1e',
        textColor: '#d1d4dc',
    },
    grid: {
        vertLines: { color: '#2B2B43' },
        horzLines: { color: '#2B2B43' },
    },
});

const candlestickSeries = chart.addCandlestickSeries({
    upColor: '#26a69a',
    downColor: '#ef5350',
    borderVisible: false,
    wickUpColor: '#26a69a',
    wickDownColor: '#ef5350',
});

// Real-time updates via SignalR
connection.on("CandleUpdate", (candle) => {
    candlestickSeries.update({
        time: candle.time,
        open: candle.open,
        high: candle.high,
        low: candle.low,
        close: candle.close
    });
});
```

## SignalR Hub Design

### IdiotProof.Core/Hubs/TradingHub.cs

```csharp
public class TradingHub : Hub
{
    // Real-time price updates
    public async Task SubscribeTicker(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, symbol);
    }
    
    // Push candle updates
    public async Task BroadcastCandle(string symbol, CandleData candle)
    {
        await Clients.Group(symbol).SendAsync("CandleUpdate", candle);
    }
    
    // Natural language command
    public async Task<CommandResult> ExecuteCommand(string naturalLanguage)
    {
        // Parse with ChatGPT
        var intent = await _chatGptService.ParseCommand(naturalLanguage);
        
        // Execute
        return intent switch
        {
            BuyIntent buy => await ExecuteBuy(buy),
            SellIntent sell => await ExecuteSell(sell),
            AnalyzeIntent analyze => await RunAnalysis(analyze),
            _ => new CommandResult { Success = false, Message = "Unknown command" }
        };
    }
    
    // Screenshot analysis
    public async Task<AnalysisResult> AnalyzeScreenshot(byte[] imageData, string question)
    {
        // Send to ChatGPT Vision API
        return await _chatGptService.AnalyzeChart(imageData, question);
    }
}
```

## Natural Language Commands

### Examples
```
"Buy 100 NVDA at market"
→ Parses to: BUY 100 NVDA @ MKT

"Set stop loss at $140"
→ Parses to: STP $140.00 for current position

"What's the RSI on AAPL?"
→ Returns: RSI(14) = 65.4 (approaching overbought)

"Show me gappers above 5%"
→ Returns: Filtered gapper list

"Analyze this chart" + [screenshot]
→ ChatGPT Vision analyzes the pattern
```

## Visual Analysis Flow

```
┌─────────────────────────────────────────────────────────────────┐
│  1. User draws box on chart or takes screenshot                 │
│  2. Clicks "Ask ChatGPT"                                        │
│  3. Types: "What pattern is this? Should I buy?"                │
│  4. Frontend sends image + question to SignalR Hub              │
│  5. Hub calls ChatGPT Vision API                                │
│  6. Response displayed in chat panel                            │
└─────────────────────────────────────────────────────────────────┘
```

## Project Structure

```
IdiotProof/
├── IdiotProof.Core/              # Existing backend
│   ├── Hubs/
│   │   └── TradingHub.cs         # SignalR hub
│   ├── Services/
│   │   ├── GapperScanner.cs      # ✓ Already exists
│   │   ├── ChatGptService.cs     # Add Vision API support
│   │   └── NaturalLanguageParser.cs
│   └── Program.cs                # Add SignalR/Kestrel
│
├── IdiotProof.Web/               # NEW - Blazor/React frontend
│   ├── Components/
│   │   ├── Chart.razor           # TradingView wrapper
│   │   ├── GapperPanel.razor     # Gapper alerts
│   │   ├── CommandBar.razor      # Natural language input
│   │   └── ChatPanel.razor       # ChatGPT responses
│   ├── Services/
│   │   └── SignalRService.cs     # Hub connection
│   └── wwwroot/
│       └── js/
│           └── chart.js          # TradingView setup
```

## Phase 1: Minimal Viable Product

1. **Add SignalR to IdiotProof.Core**
   - Expose WebSocket endpoint
   - Broadcast real-time prices
   - Accept commands

2. **Create basic Blazor app**
   - TradingView chart
   - Gapper alert panel
   - Quick trade buttons

3. **Connect to existing GapperScanner**
   - Real-time alerts via SignalR
   - Quick trade execution

## Phase 2: ChatGPT Integration

1. **Natural language commands**
2. **Chart screenshot analysis**
3. **News/fundamentals lookup**

## Phase 3: Advanced Features

1. **Drawing tools synced with analysis**
2. **Multi-timeframe views**
3. **Pattern recognition overlays**
4. **Mobile-responsive design**

## Getting Started

```bash
# Create new Blazor WebAssembly project
dotnet new blazorwasm -o IdiotProof.Web

# Add SignalR client
cd IdiotProof.Web
dotnet add package Microsoft.AspNetCore.SignalR.Client

# Add to IdiotProof.Core
cd ../IdiotProof.Core
dotnet add package Microsoft.AspNetCore.SignalR
```

## Notes

- TradingView Lightweight Charts is FREE (Apache 2.0)
- No licensing fees for commercial use
- Full source available on GitHub
- Actively maintained by TradingView team
- Supports: Candlesticks, Lines, Areas, Bars, Histograms
- Drawing tools require custom implementation or TradingView Pro (paid)

## Alternative: TradingView Charting Library (Paid)

If you need advanced features:
- Full drawing tools
- Indicators library
- Save/load charts
- Multi-pane layouts

Cost: Contact TradingView for enterprise pricing
Free for personal/non-commercial use
