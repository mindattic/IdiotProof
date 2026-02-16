# Running IdiotProof - Complete Setup Guide

## Quick Start

### 1. Start IBKR TWS or Gateway
- Open TWS or IB Gateway
- Enable API connections (Configure > API > Settings)
- Use port **4001** (live) or **4002** (paper)

### 2. Start the Web Frontend
```powershell
cd IdiotProof.Web
dotnet run
```
The web frontend will start on `http://localhost:5000`

### 3. Start the Core Trading Engine (Headless)
```powershell
cd IdiotProof.Core
dotnet run
```
Core runs as a **headless background service** - no interactive menus.
It auto-activates trading if your watchlist has enabled tickers.

### 4. Open Browser
Navigate to: **http://localhost:5000**

All interaction happens via the Web UI:
- **Dashboard** - Overview and status
- **Trade** - Live charts and order execution
- **Log** - View all Core messages in real-time
- **AI Chatbox** - Available on every screen (bottom of page)

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     IDIOTPROOF HEADLESS ARCHITECTURE                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐       ┌──────────────────────┐       ┌────────────────┐   │
│  │   IBKR       │──────▶│   IdiotProof.Core    │──────▶│ IdiotProof.Web │   │
│  │  TWS/Gateway │       │   (HEADLESS)         │       │                │   │
│  │  Port 4001/2 │       │                      │       │  • Dashboard   │   │
│  └──────────────┘       │  • Trading Engine    │       │  • Trade/Charts│   │
│                         │  • Indicators        │       │  • Log Tab     │   │
│                         │  • Alert Detection   │       │  • Orders      │   │
│                         │  • Order Execution   │       │  • AI Chatbox  │   │
│                         │  • NO Console UI     │       │                │   │
│                         └──────────┬───────────┘       └───────┬────────┘   │
│                                    │                            │           │
│                                    │ HTTP POST                  │ SignalR   │
│                                    │ /api/marketdata/*          │           │
│                                    ▼                            ▼           │
│                         ┌────────────────────────────────────────────┐      │
│                         │              Browser                        │      │
│                         │  • TradingView Charts (real-time)          │      │
│                         │  • Rich Log Viewer (color-coded)           │      │
│                         │  • Global AI Chatbox                       │      │
│                         │  • Audio Alerts                            │      │
│                         └────────────────────────────────────────────┘      │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Core Behavior (Headless)

**IdiotProof.Core runs silently with no console interaction:**

| Feature | Behavior |
|---------|----------|
| **Auto-Activation** | Starts trading automatically if watchlist has enabled tickers |
| **Heartbeat** | Logs status every 5 minutes |
| **Logging** | All messages sent to Web UI's Log tab |
| **Commands** | Receives commands from Web (activate, deactivate, close positions) |
| **Shutdown** | Ctrl+C for graceful shutdown |

**No console menus or prompts** - all interaction via Web UI.

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
| `/` | **Dashboard** - Overview and status |
| `/trade` | Live charts, AI advisor, trade execution |
| `/trade/NVDA` | Pre-select symbol on Trade page |
| `/orders` | View and manage open orders |
| `/analyze` | Deep analysis mode with AI |
| `/backtest` | Run backtests with historical data |
| `/log` | **Log Viewer** - Real-time logs from Core |

### Log Tab Features
- 🎨 **Color-coded** severity levels (Debug, Info, Success, Warning, Error, Alert)
- 🏷️ **Category filtering** (Trade, Order, Alert, Connection, AI, Heartbeat)
- 🔍 **Full-text search** across all logs
- 📋 **Click-to-copy** any log entry
- 💬 **Click to chat** - sends log to AI chatbox for analysis
- 📜 **Auto-scroll** toggle

### Global AI Chatbox
The AI chatbox is **available on every screen** (fixed at bottom):
- Minimizable/expandable
- Context-aware (knows current symbol)
- Quick action buttons for common questions
- Click any log entry to discuss it with AI

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
