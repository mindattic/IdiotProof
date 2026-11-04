/* ip-chart.js — Chart.razor's bridge to TradingView Lightweight Charts (window.LightweightCharts). */
(function () {
    var charts = new WeakMap();

    function css(name, fallback) {
        var v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        return v || fallback;
    }

    function themeOptions() {
        return {
            layout: { background: { color: 'transparent' }, textColor: css('--text-secondary', '#8b949e') },
            grid: { vertLines: { color: css('--border', '#30363d') }, horzLines: { color: css('--border', '#30363d') } },
            timeScale: { timeVisible: true, secondsVisible: false },
            rightPriceScale: { borderVisible: false },
            localization: {
                timeFormatter: function (t) {
                    return new Date(t * 1000).toLocaleTimeString('en-US', { timeZone: 'America/New_York', hour: '2-digit', minute: '2-digit' }) + ' ET';
                }
            }
        };
    }

    function render(el, payload) {
        if (!el || !window.LightweightCharts) return;
        var state = charts.get(el);
        if (!state) {
            var chart = LightweightCharts.createChart(el, Object.assign({ autoSize: true }, themeOptions()));
            var candles = chart.addCandlestickSeries({
                upColor: css('--green', '#0f9d78'), downColor: css('--red', '#e0483d'),
                wickUpColor: css('--green', '#0f9d78'), wickDownColor: css('--red', '#e0483d'), borderVisible: false
            });
            var volume = chart.addHistogramSeries({ priceFormat: { type: 'volume' }, priceScaleId: 'vol' });
            chart.priceScale('vol').applyOptions({ scaleMargins: { top: 0.82, bottom: 0 } });
            state = { chart: chart, candles: candles, volume: volume };
            charts.set(el, state);
        }
        var bars = payload.bars || [];
        state.candles.setData(bars.map(function (b) { return { time: b.time, open: b.open, high: b.high, low: b.low, close: b.close }; }));
        state.volume.setData(bars.map(function (b) {
            return { time: b.time, value: b.volume, color: b.close >= b.open ? 'rgba(15,157,120,.35)' : 'rgba(224,72,61,.35)' };
        }));
        state.candles.setMarkers((payload.markers || []).map(function (m) {
            return { time: m.time, position: m.position, shape: m.shape, color: m.color, text: m.text };
        }));
        state.chart.timeScale().fitContent();
    }

    function dispose(el) {
        var state = el && charts.get(el);
        if (!state) return;
        state.chart.remove();
        charts.delete(el);
    }

    window.ipChart = { render: render, dispose: dispose };
})();
