// ============================================================================
// TradingView Lightweight Charts Integration
// ============================================================================
// Using the FREE, open-source TradingView Lightweight Charts library
// https://github.com/nicholasxuu/lightweight-charts
// NO API KEY NEEDED - we're using our own historical data!
// ============================================================================

let chart = null;
let candleSeries = null;
let volumeSeries = null;
let overlays = {};
let markers = [];

// Chart configuration
const CHART_COLORS = {
    background: '#0d1117',
    text: '#f0f6fc',
    grid: '#21262d',
    crosshair: '#58a6ff',
    
    // Candles
    upColor: '#3fb950',
    downColor: '#f85149',
    wickUp: '#3fb950',
    wickDown: '#f85149',
    
    // Volume
    volumeUp: 'rgba(63, 185, 80, 0.3)',
    volumeDown: 'rgba(248, 81, 73, 0.3)',
    
    // Overlays
    vwap: '#00BCD4',
    ema9: '#FF9800',
    ema21: '#2196F3',
    ema50: '#9C27B0',
    ema200: '#F44336',
    
    // Markers
    entryLong: '#3fb950',
    entryShort: '#f85149',
    exitProfit: '#58a6ff',
    exitLoss: '#f85149'
};

/**
 * Initialize the chart in a container element.
 * @param {string} containerId - The ID of the container element
 * @param {object} options - Chart options
 */
window.initializeChart = function(containerId, options = {}) {
    const container = document.getElementById(containerId);
    if (!container) {
        console.error('Chart container not found:', containerId);
        return null;
    }
    
    // Clear existing chart
    if (chart) {
        chart.remove();
    }
    
    // Create chart
    chart = LightweightCharts.createChart(container, {
        width: container.clientWidth,
        height: options.height || 400,
        layout: {
            background: { type: 'solid', color: CHART_COLORS.background },
            textColor: CHART_COLORS.text,
        },
        grid: {
            vertLines: { color: CHART_COLORS.grid },
            horzLines: { color: CHART_COLORS.grid },
        },
        crosshair: {
            mode: LightweightCharts.CrosshairMode.Normal,
            vertLine: {
                color: CHART_COLORS.crosshair,
                width: 1,
                style: LightweightCharts.LineStyle.Dashed,
            },
            horzLine: {
                color: CHART_COLORS.crosshair,
                width: 1,
                style: LightweightCharts.LineStyle.Dashed,
            },
        },
        timeScale: {
            borderColor: CHART_COLORS.grid,
            timeVisible: true,
            secondsVisible: false,
        },
        rightPriceScale: {
            borderColor: CHART_COLORS.grid,
        },
    });
    
    // Create candlestick series
    candleSeries = chart.addCandlestickSeries({
        upColor: CHART_COLORS.upColor,
        downColor: CHART_COLORS.downColor,
        borderDownColor: CHART_COLORS.downColor,
        borderUpColor: CHART_COLORS.upColor,
        wickDownColor: CHART_COLORS.wickDown,
        wickUpColor: CHART_COLORS.wickUp,
    });
    
    // Create volume series
    volumeSeries = chart.addHistogramSeries({
        color: CHART_COLORS.volumeUp,
        priceFormat: {
            type: 'volume',
        },
        priceScaleId: 'volume',
        scaleMargins: {
            top: 0.8,
            bottom: 0,
        },
    });
    
    // Handle resize
    const resizeObserver = new ResizeObserver(entries => {
        for (const entry of entries) {
            chart.applyOptions({
                width: entry.contentRect.width,
            });
        }
    });
    resizeObserver.observe(container);
    
    console.log('Chart initialized');
    return true;
};

/**
 * Set candle data on the chart.
 * @param {Array} candles - Array of {time, open, high, low, close, volume}
 */
window.setCandleData = function(candles) {
    if (!candleSeries || !candles || candles.length === 0) {
        console.error('Cannot set candle data: series not initialized or no data');
        return false;
    }
    
    // Transform candles for the chart
    const candleData = candles.map(c => ({
        time: c.time,
        open: c.open,
        high: c.high,
        low: c.low,
        close: c.close,
    }));
    
    candleSeries.setData(candleData);
    
    // Set volume data with color based on candle direction
    const volumeData = candles.map(c => ({
        time: c.time,
        value: c.volume,
        color: c.close >= c.open ? CHART_COLORS.volumeUp : CHART_COLORS.volumeDown,
    }));
    
    volumeSeries.setData(volumeData);
    
    // Fit content
    chart.timeScale().fitContent();
    
    console.log('Set', candles.length, 'candles');
    return true;
};

/**
 * Add an overlay line (VWAP, EMA, etc.)
 * @param {string} name - Unique name for this overlay
 * @param {Array} points - Array of {time, value}
 * @param {string} color - Line color
 * @param {number} lineWidth - Line width
 */
window.addOverlay = function(name, points, color, lineWidth = 2) {
    if (!chart) return false;
    
    // Remove existing overlay with same name
    if (overlays[name]) {
        chart.removeSeries(overlays[name]);
    }
    
    // Create line series
    const lineSeries = chart.addLineSeries({
        color: color || CHART_COLORS.ema9,
        lineWidth: lineWidth,
        crosshairMarkerVisible: false,
        priceLineVisible: false,
        lastValueVisible: false,
    });
    
    lineSeries.setData(points.map(p => ({ time: p.time, value: p.value })));
    
    overlays[name] = lineSeries;
    
    console.log('Added overlay:', name);
    return true;
};

/**
 * Remove an overlay.
 */
window.removeOverlay = function(name) {
    if (overlays[name]) {
        chart.removeSeries(overlays[name]);
        delete overlays[name];
    }
};

/**
 * Add trade markers (entries, exits).
 * @param {Array} trades - Array of trade markers
 */
window.addTradeMarkers = function(trades) {
    if (!candleSeries) return false;
    
    markers = trades.map(t => ({
        time: t.time,
        position: t.isEntry 
            ? (t.isLong ? 'belowBar' : 'aboveBar')
            : (t.isProfit ? 'aboveBar' : 'belowBar'),
        color: t.isEntry 
            ? (t.isLong ? CHART_COLORS.entryLong : CHART_COLORS.entryShort)
            : (t.isProfit ? CHART_COLORS.exitProfit : CHART_COLORS.exitLoss),
        shape: t.isEntry 
            ? (t.isLong ? 'arrowUp' : 'arrowDown')
            : 'circle',
        text: t.label || '',
        size: 2,
    }));
    
    candleSeries.setMarkers(markers);
    
    console.log('Added', markers.length, 'trade markers');
    return true;
};

/**
 * Clear all trade markers.
 */
window.clearTradeMarkers = function() {
    markers = [];
    if (candleSeries) {
        candleSeries.setMarkers([]);
    }
};

/**
 * Add horizontal price line (support/resistance).
 */
window.addPriceLine = function(price, color, title, style = 'solid') {
    if (!candleSeries) return null;

    const lineStyle = style === 'dashed' 
        ? LightweightCharts.LineStyle.Dashed 
        : LightweightCharts.LineStyle.Solid;

    return candleSeries.createPriceLine({
        price: price,
        color: color,
        lineWidth: 1,
        lineStyle: lineStyle,
        axisLabelVisible: true,
        title: title,
    });
};

// ============================================================================
// REAL-TIME TICK UPDATES
// ============================================================================

let currentCandle = null;
let currentCandleTime = 0;

/**
 * Updates the chart with a real-time tick.
 * Creates or updates the current (incomplete) candle.
 * @param {object} tick - { symbol, price, timestamp, volume }
 */
window.updateChartTick = function(tick) {
    if (!candleSeries) return false;

    // Round to current minute (Unix timestamp)
    const candleTime = Math.floor(tick.timestamp / 60) * 60;

    if (candleTime !== currentCandleTime) {
        // New candle - finalize the old one if exists
        if (currentCandle && currentCandleTime > 0) {
            candleSeries.update(currentCandle);
        }

        // Start new candle
        currentCandleTime = candleTime;
        currentCandle = {
            time: candleTime,
            open: tick.price,
            high: tick.price,
            low: tick.price,
            close: tick.price
        };
    } else {
        // Update existing candle
        if (currentCandle) {
            currentCandle.high = Math.max(currentCandle.high, tick.price);
            currentCandle.low = Math.min(currentCandle.low, tick.price);
            currentCandle.close = tick.price;
        }
    }

    // Update chart
    if (currentCandle) {
        candleSeries.update(currentCandle);
    }

    return true;
};

/**
 * Updates chart with a candle update (partial or complete).
 * @param {object} candle - { time, open, high, low, close, volume, isComplete }
 */
window.updateChartCandle = function(candle) {
    if (!candleSeries) return false;

    candleSeries.update({
        time: candle.time,
        open: candle.open,
        high: candle.high,
        low: candle.low,
        close: candle.close
    });

    // Update volume if we have volume series
    if (volumeSeries && candle.volume) {
        volumeSeries.update({
            time: candle.time,
            value: candle.volume,
            color: candle.close >= candle.open ? CHART_COLORS.volumeUp : CHART_COLORS.volumeDown
        });
    }

    // If complete, reset current candle tracking
    if (candle.isComplete) {
        currentCandleTime = 0;
        currentCandle = null;
    }

    return true;
};

/**
 * Gets the current (incomplete) candle data.
 */
window.getCurrentCandle = function() {
    return currentCandle;
};

// ============================================================================
// END REAL-TIME SECTION
// ============================================================================

/**
 * Set visible time range.
 */
window.setVisibleRange = function(from, to) {
    if (!chart) return;
    chart.timeScale().setVisibleRange({ from, to });
};

/**
 * Scroll to a specific time.
 */
window.scrollToTime = function(time) {
    if (!chart) return;
    chart.timeScale().scrollToPosition(-10, false);
};

/**
 * Fit content to view.
 */
window.fitContent = function() {
    if (!chart) return;
    chart.timeScale().fitContent();
};

/**
 * Get screenshot of the chart as base64 PNG.
 */
window.getChartScreenshot = function() {
    if (!chart) return null;
    return chart.takeScreenshot().toDataURL('image/png');
};

/**
 * Clean up the chart.
 */
window.destroyChart = function() {
    if (chart) {
        chart.remove();
        chart = null;
        candleSeries = null;
        volumeSeries = null;
        overlays = {};
        markers = [];
    }
};

// ============================================================================
// TRADE LEVEL LINES (Entry, Stop Loss, Take Profit)
// ============================================================================

let tradeLevelLines = {};

/**
 * Shows trade level lines on the chart.
 * @param {object} setup - { entryPrice, stopLoss, takeProfit, isLong }
 */
window.showTradeLevels = function(setup) {
    clearTradeLevels();

    if (!candleSeries || !setup) return false;

    // Entry line (cyan)
    if (setup.entryPrice > 0) {
        tradeLevelLines.entry = candleSeries.createPriceLine({
            price: setup.entryPrice,
            color: '#00BCD4',
            lineWidth: 2,
            lineStyle: LightweightCharts.LineStyle.Solid,
            axisLabelVisible: true,
            title: 'ENTRY',
        });
    }

    // Stop Loss line (red)
    if (setup.stopLoss > 0) {
        tradeLevelLines.stopLoss = candleSeries.createPriceLine({
            price: setup.stopLoss,
            color: '#f85149',
            lineWidth: 2,
            lineStyle: LightweightCharts.LineStyle.Dashed,
            axisLabelVisible: true,
            title: 'SL',
        });
    }

    // Take Profit line (green)
    if (setup.takeProfit > 0) {
        tradeLevelLines.takeProfit = candleSeries.createPriceLine({
            price: setup.takeProfit,
            color: '#3fb950',
            lineWidth: 2,
            lineStyle: LightweightCharts.LineStyle.Dashed,
            axisLabelVisible: true,
            title: 'TP',
        });
    }

    console.log('Trade levels shown:', setup);
    return true;
};

/**
 * Updates trade level lines without recreating.
 */
window.updateTradeLevels = function(setup) {
    // For simplicity, just recreate
    return showTradeLevels(setup);
};

/**
 * Clears all trade level lines.
 */
window.clearTradeLevels = function() {
    if (candleSeries) {
        Object.values(tradeLevelLines).forEach(line => {
            try { candleSeries.removePriceLine(line); } catch {}
        });
    }
    tradeLevelLines = {};
};

/**
 * Shows alert level on the chart with blinking effect.
 */
window.showAlertLevel = function(price, symbol, type) {
    if (!candleSeries) return;

    const color = type === 'spike' ? '#FF5722' : '#2196F3';

    const line = candleSeries.createPriceLine({
        price: price,
        color: color,
        lineWidth: 3,
        lineStyle: LightweightCharts.LineStyle.Solid,
        axisLabelVisible: true,
        title: `🚨 ${symbol} ${type.toUpperCase()}`,
    });

    // Remove after 30 seconds
    setTimeout(() => {
        try { candleSeries.removePriceLine(line); } catch {}
    }, 30000);

    return line;
};

console.log('TradingView Lightweight Charts module loaded');
