# Running IdiotProof - Complete Setup Guide

## Quick Start

### 1. Start the Web Frontend First
```powershell
cd IdiotProof.Web
dotnet run
```
The web frontend will start on `http://localhost:5000`

### 2. Start IBKR TWS or Gateway
- Open TWS or IB Gateway
- Enable API connections (Configure > API > Settings)
- Use port **4001** (live) or **4002** (paper)

### 3. Start the Core Trading Engine
```powershell
cd IdiotProof.Core
dotnet run
```
Core will connect to IBKR and start streaming data to the web frontend.

### 4. Open Browser
Navigate to: **http://localhost:5000/trade**

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          IDIOTPROOF ARCHITECTURE                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐       ┌──────────────────────┐       ┌────────────────┐   │
│  │   IBKR       │──────▶│   IdiotProof.Core    │──────▶│ IdiotProof.Web │   │
│  │  TWS/Gateway │       │                      │       │                │   │
│  │  Port 4001/2 │       │  • Trading Engine    │       │  • Dashboard   │   │
│  └──────────────┘       │  • Indicators        │       │  • Charts      │   │
│                         │  • Alert Detection   │       │  • Trade UI    │   │
│                         │  • Order Execution   │       │  • AI Advisor  │   │
│                         └──────────┬───────────┘       └───────┬────────┘   │
│                                    │                            │           │
│                                    │ HTTP POST                  │ SignalR   │
│                                    │ /api/marketdata/*          │           │
│                                    ▼                            ▼           │
│                         ┌────────────────────────────────────────────┐      │
│                         │              Browser                        │      │
│                         │  • TradingView Charts (real-time)          │      │
│                         │  • Risk Guardian UI                        │      │
│                         │  • Audio Alerts                            │      │
│                         └────────────────────────────────────────────┘      │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Data Flow

### Live Price Updates
1. IBKR sends tick → Core receives via `IbWrapper.RegisterTickerHandler()`
2. Core calls `WebFrontendClient.OnPriceTickAsync()`
3. Web receives via `MarketDataController` POST endpoint
4. Web broadcasts via `MarketDataBroadcaster` → SignalR
5. Browser receives via `MarketDataHub.ReceiveTick` event
6. JavaScript calls `updateChartTick()` to update TradingView chart

### Alerts
1. Core's `SuddenMoveDetector` detects price spike
2. `AlertService` sends to Discord/Email/SMS
3. `AlertWebIntegration` also sends to Web via HTTP
4. Web broadcasts alert via SignalR
5. Browser plays audio alert and shows notification

---

## Web Pages

| Route | Purpose |
|-------|---------|
| `/` | Home - Overview and status |
| `/trade` | **Main Trading Interface** - Live charts, AI advisor, trade execution |
| `/trade/NVDA` | Pre-select symbol on Trade page |
| `/analyze` | Deep analysis mode with AI |
| `/backtest` | Run backtests with historical data |

---

## Configuration

### Core Settings (`IdiotProof.Core/Settings/Settings.cs`)
```csharp
// IBKR Connection
public const int Port = 4001;        // 4001=live, 4002=paper
public const string AccountNumber = "U12345678";

// Web Frontend
public static string? WebFrontendUrl = "http://localhost:5000";
```

### Alert Configuration (`IdiotProof.Core/Data/alert-config.json`)
```json
{
  "discord": { "enabled": true, "webhookUrl": "..." },
  "email": { "enabled": false },
  "detection": {
    "minPercentChange": 3.0,
    "timeWindowMinutes": 3
  }
}
```

---

## Troubleshooting

### "Web frontend not running"
Start `IdiotProof.Web` before `IdiotProof.Core`.

### Charts not updating
1. Check browser console for SignalR errors
2. Verify Core is connected to IBKR (check console output)
3. Ensure market is open or you're using extended hours data

### No audio on alerts
Click anywhere on the page first to enable audio (browser security policy).

---

## Development

### Running Both Projects Simultaneously
Option 1: Two terminals
```powershell
# Terminal 1
cd IdiotProof.Web && dotnet run

# Terminal 2  
cd IdiotProof.Core && dotnet run
```

Option 2: Visual Studio
- Right-click Solution → Properties → Startup Project
- Select "Multiple startup projects"
- Set both IdiotProof.Web and IdiotProof.Core to "Start"
