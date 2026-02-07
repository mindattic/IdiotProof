# IdiotProof Database Architecture

## Current State: File-Based Storage

```
IdiotProof.Core/
├── History/SYMBOL.json      # 30 days of 1-min OHLCV bars per ticker
├── Metadata/SYMBOL.json     # Stock characteristics, patterns
├── Profiles/SYMBOL.json     # Learning data, trade history
└── Scripts/*.idiot          # Legacy strategy definitions
```

**Pros:**
- Zero infrastructure
- Human-readable for debugging
- Git-friendly for versioning
- Fast enough for single-ticker backtesting

**Cons:**
- Slow for multi-ticker queries
- No indexing for time range queries
- Duplicate data across files
- No ACID guarantees

---

## Proposed Architecture: SQLite + DuckDB Hybrid

```
+===========================================================================+
|  RECOMMENDED ARCHITECTURE                                                 |
+===========================================================================+
|                                                                           |
|   ┌─────────────────────────────────────────────────────────────────┐    |
|   │                        APPLICATION LAYER                        │    |
|   └─────────────────────────────────────────────────────────────────┘    |
|                    │                              │                       |
|                    ▼                              ▼                       |
|   ┌────────────────────────────┐   ┌────────────────────────────────┐    |
|   │         SQLite             │   │          DuckDB                │    |
|   │   (OLTP - Transactional)   │   │   (OLAP - Analytics)           │    |
|   ├────────────────────────────┤   ├────────────────────────────────┤    |
|   │  • Trades                  │   │  • Historical Bars             │    |
|   │  • Positions               │   │  • Tick Data                   │    |
|   │  • Metadata                │   │  • Aggregations                │    |
|   │  • Configuration           │   │  • Backtesting                 │    |
|   │  • Profiles                │   │  • Pattern Detection           │    |
|   └────────────────────────────┘   └────────────────────────────────┘    |
|              │                                    │                       |
|              │         Shared via files:          │                       |
|              │    idiotproof.sqlite               │                       |
|              │    idiotproof.duckdb               │                       |
|              └────────────────────────────────────┘                       |
|                                                                           |
+===========================================================================+
```

---

## Why This Hybrid Approach?

### SQLite for Transactional Data

| Feature | Benefit |
|---------|---------|
| **ACID Transactions** | Safe concurrent read/write for positions |
| **Row-oriented** | Fast single-row lookups (positions, config) |
| **Mature** | Battle-tested, excellent .NET support |
| **Embedded** | Single file, no server process |
| **Triggers** | Can log changes automatically |

**Best for:**
- Current positions (read/write every tick)
- Trade history (append-only, query by date/symbol)
- Ticker metadata (read-heavy, occasional updates)
- System configuration (rarely changes)
- Learning profiles (evolving, semi-structured)

### DuckDB for Analytics

| Feature | Benefit |
|---------|---------|
| **Columnar Storage** | 10-100x faster for aggregations over OHLCV |
| **SIMD Vectorization** | Parallel processing of bar data |
| **SQL Interface** | Familiar query language |
| **Embedded** | Single file, no server |
| **Parquet Support** | Can query external files directly |

**Best for:**
- Historical bars (millions of rows per ticker)
- Backtesting queries (range scans, aggregations)
- Pattern detection (window functions)
- Analytics dashboards (GROUP BY, HAVING)

---

## Schema Design

### SQLite Schema

```sql
-- ==========================================================================
-- TRADES: Historical trade records
-- ==========================================================================
CREATE TABLE trades (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL,
    entry_time TEXT NOT NULL,        -- ISO8601 datetime
    exit_time TEXT,                  -- NULL if still open
    direction TEXT NOT NULL,         -- 'LONG' or 'SHORT'
    entry_price REAL NOT NULL,
    exit_price REAL,
    shares INTEGER NOT NULL,
    entry_score REAL,
    exit_score REAL,
    entry_reason TEXT,
    exit_reason TEXT,
    pnl REAL,
    commission REAL DEFAULT 0,
    metadata_adjustment INTEGER,     -- Score adjustment from metadata
    created_at TEXT DEFAULT (datetime('now'))
);

CREATE INDEX idx_trades_symbol ON trades(symbol);
CREATE INDEX idx_trades_entry_time ON trades(entry_time);
CREATE INDEX idx_trades_symbol_date ON trades(symbol, date(entry_time));

-- ==========================================================================
-- POSITIONS: Currently open positions
-- ==========================================================================
CREATE TABLE positions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL UNIQUE,
    direction TEXT NOT NULL,
    entry_price REAL NOT NULL,
    shares INTEGER NOT NULL,
    entry_time TEXT NOT NULL,
    entry_score REAL,
    current_stop REAL,
    current_target REAL,
    highest_since_entry REAL,        -- For trailing stop
    lowest_since_entry REAL,
    updated_at TEXT DEFAULT (datetime('now'))
);

-- ==========================================================================
-- TICKER_METADATA: Stock characteristics (replaces JSON files)
-- ==========================================================================
CREATE TABLE ticker_metadata (
    symbol TEXT PRIMARY KEY,
    
    -- Volatility & Risk
    beta REAL,
    atr_14day REAL,
    avg_volume INTEGER,
    float_shares INTEGER,
    short_interest_pct REAL,
    days_to_cover REAL,
    
    -- Fundamentals
    sector TEXT,
    industry TEXT,
    market_cap INTEGER,
    earnings_date TEXT,
    ex_dividend_date TEXT,
    
    -- Technical
    high_52week REAL,
    low_52week REAL,
    
    -- Correlation
    correlation_spy REAL,
    sector_etf TEXT,
    correlation_sector REAL,
    
    -- Behavioral Patterns (JSON for flexibility)
    daily_extremes_json TEXT,
    gap_behavior_json TEXT,
    vwap_behavior_json TEXT,
    support_levels_json TEXT,
    resistance_levels_json TEXT,
    hourly_performance_json TEXT,
    avoid_hours_json TEXT,
    best_hours_json TEXT,
    
    -- Timestamps
    created_at TEXT DEFAULT (datetime('now')),
    updated_at TEXT DEFAULT (datetime('now')),
    bars_analyzed INTEGER,
    days_analyzed INTEGER
);

-- ==========================================================================
-- TICKER_PROFILES: Learning data per ticker
-- ==========================================================================
CREATE TABLE ticker_profiles (
    symbol TEXT PRIMARY KEY,
    
    -- Aggregate stats
    total_trades INTEGER DEFAULT 0,
    wins INTEGER DEFAULT 0,
    losses INTEGER DEFAULT 0,
    total_pnl REAL DEFAULT 0,
    
    -- Learned thresholds
    optimal_long_threshold INTEGER,
    optimal_short_threshold INTEGER,
    optimal_tp_multiplier REAL,
    optimal_sl_multiplier REAL,
    
    -- Time patterns (JSON)
    best_entry_hours_json TEXT,
    worst_entry_hours_json TEXT,
    
    -- Confidence
    confidence_level REAL DEFAULT 0,  -- 0-1 based on trade count
    
    -- Timestamps
    created_at TEXT DEFAULT (datetime('now')),
    updated_at TEXT DEFAULT (datetime('now'))
);

-- ==========================================================================
-- CALIBRATOR_STATE: Self-calibration parameters
-- ==========================================================================
CREATE TABLE calibrator_state (
    symbol TEXT PRIMARY KEY,
    long_entry_threshold INTEGER DEFAULT 65,
    short_entry_threshold INTEGER DEFAULT -65,
    take_profit_atr REAL DEFAULT 1.5,
    stop_loss_atr REAL DEFAULT 2.5,
    min_volume_ratio REAL DEFAULT 1.2,
    require_trend_alignment INTEGER DEFAULT 1,
    min_indicator_confirmation INTEGER DEFAULT 6,
    consecutive_losses INTEGER DEFAULT 0,
    consecutive_wins INTEGER DEFAULT 0,
    updated_at TEXT DEFAULT (datetime('now'))
);

-- ==========================================================================
-- SYSTEM_CONFIG: Global configuration
-- ==========================================================================
CREATE TABLE system_config (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,
    description TEXT,
    updated_at TEXT DEFAULT (datetime('now'))
);
```

### DuckDB Schema

```sql
-- ==========================================================================
-- BARS: Historical OHLCV data (primary analytics table)
-- ==========================================================================
CREATE TABLE bars (
    symbol VARCHAR NOT NULL,
    timestamp TIMESTAMP NOT NULL,
    open DOUBLE NOT NULL,
    high DOUBLE NOT NULL,
    low DOUBLE NOT NULL,
    close DOUBLE NOT NULL,
    volume BIGINT NOT NULL,
    vwap DOUBLE,                     -- Pre-calculated VWAP
    bar_type VARCHAR DEFAULT '1min', -- '1min', '5min', 'daily'
    PRIMARY KEY (symbol, timestamp, bar_type)
);

-- Partitioned storage by symbol for efficient queries
-- DuckDB handles this automatically with columnar storage

-- ==========================================================================
-- INDICATOR_CACHE: Pre-calculated indicators for fast backtesting
-- ==========================================================================
CREATE TABLE indicator_cache (
    symbol VARCHAR NOT NULL,
    timestamp TIMESTAMP NOT NULL,
    ema_9 DOUBLE,
    ema_21 DOUBLE,
    ema_200 DOUBLE,
    rsi_14 DOUBLE,
    macd DOUBLE,
    macd_signal DOUBLE,
    macd_histogram DOUBLE,
    adx DOUBLE,
    plus_di DOUBLE,
    minus_di DOUBLE,
    atr_14 DOUBLE,
    volume_ratio DOUBLE,
    PRIMARY KEY (symbol, timestamp)
);

-- ==========================================================================
-- EXAMPLE ANALYTICS QUERIES
-- ==========================================================================

-- Average daily range by symbol
SELECT 
    symbol,
    AVG((high - low) / open * 100) as avg_daily_range_pct,
    COUNT(DISTINCT DATE_TRUNC('day', timestamp)) as days
FROM bars
WHERE bar_type = '1min'
GROUP BY symbol;

-- Find HOD/LOD timing patterns
WITH daily_extremes AS (
    SELECT 
        symbol,
        DATE_TRUNC('day', timestamp) as date,
        MAX(high) as day_high,
        MIN(low) as day_low,
        FIRST(timestamp ORDER BY high DESC) as hod_time,
        FIRST(timestamp ORDER BY low ASC) as lod_time
    FROM bars
    WHERE EXTRACT(HOUR FROM timestamp) BETWEEN 9 AND 16
    GROUP BY symbol, DATE_TRUNC('day', timestamp)
)
SELECT 
    symbol,
    AVG(EXTRACT(MINUTE FROM hod_time - date) + EXTRACT(HOUR FROM hod_time - date) * 60) as avg_hod_minutes,
    AVG(EXTRACT(MINUTE FROM lod_time - date) + EXTRACT(HOUR FROM lod_time - date) * 60) as avg_lod_minutes
FROM daily_extremes
GROUP BY symbol;

-- Backtest query: Get all bars with indicators
SELECT 
    b.symbol,
    b.timestamp,
    b.open, b.high, b.low, b.close, b.volume,
    i.ema_9, i.ema_21, i.rsi_14, i.macd, i.adx, i.plus_di, i.minus_di
FROM bars b
JOIN indicator_cache i ON b.symbol = i.symbol AND b.timestamp = i.timestamp
WHERE b.symbol = 'NVDA'
  AND b.timestamp BETWEEN '2025-01-01' AND '2025-01-31'
ORDER BY b.timestamp;
```

---

## Implementation Plan

### Phase 1: Core Migration (SQLite)

1. **Add Microsoft.Data.Sqlite NuGet package**
2. **Create `TradingDatabase` service class**
3. **Migrate Trades storage**
   - Keep JSON as backup/export format
   - Write to both during transition
4. **Migrate Positions storage**
5. **Migrate Ticker Metadata**
6. **Migrate Profiles**

### Phase 2: Analytics Layer (DuckDB)

1. **Add DuckDB.NET NuGet package**
2. **Create `HistoricalDataStore` service class**
3. **Migrate bar data from JSON**
4. **Pre-calculate indicators during import**
5. **Update AutonomousBacktester to query DuckDB**

### Phase 3: Optimization

1. **Add connection pooling**
2. **Implement caching layer for hot paths**
3. **Add background indicator calculation**
4. **Create aggregation tables for common queries**

---

## File Locations

```
IdiotProof.Core/
├── Data/
│   ├── idiotproof.sqlite        # SQLite database
│   ├── idiotproof.duckdb        # DuckDB database
│   └── migrations/              # SQL migration scripts
│       ├── 001_initial.sql
│       ├── 002_add_metadata.sql
│       └── ...
├── History/                     # Legacy JSON (for export/import)
├── Metadata/                    # Legacy JSON (for export/import)
└── Profiles/                    # Legacy JSON (for export/import)
```

---

## Why Not Neo4j?

Neo4j would be excellent for:
- "Find stocks that behave like NVDA"
- Sector → Industry → Ticker hierarchies
- Correlation networks
- Pattern similarity graphs

**Decision:** Add Neo4j later when we need relationship queries. For now:
- SQLite handles transactional data perfectly
- DuckDB handles time series analytics excellently
- Both are embedded (no server overhead)

---

## Performance Expectations

| Operation | Current (JSON) | SQLite | DuckDB |
|-----------|---------------|--------|--------|
| Load ticker metadata | 5-10ms | <1ms | N/A |
| Save trade | 10-20ms | <1ms | N/A |
| Load 30 days bars | 200-500ms | N/A | 10-20ms |
| Backtest (1000 bars) | 500ms | N/A | 50ms |
| Multi-ticker query | N/A (manual) | 5-10ms | <5ms |

---

## NuGet Packages

```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
<PackageReference Include="DuckDB.NET.Data" Version="0.10.0" />
```

---

## Migration Strategy

```csharp
// During startup
if (!File.Exists(DatabasePath))
{
    // First run: Create database
    CreateSchema();
    
    // Import existing JSON data
    await ImportLegacyJsonAsync();
}
else
{
    // Existing database: Run migrations
    await RunMigrationsAsync();
}
```
