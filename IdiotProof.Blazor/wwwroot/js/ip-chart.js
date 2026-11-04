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
        if (state.tip && state.tip.parentNode) state.tip.parentNode.removeChild(state.tip);
        state.chart.remove();
        charts.delete(el);
    }

    // Ghost replay: bars after `upto` are translucent, markers/levels/stop path appear only up to `upto`,
    // and hovering a fork shows which branch held and the indicator values at that bar.
    function ghost(el, payload, upto) {
        if (!el || !window.LightweightCharts) return;
        var state = charts.get(el);
        if (!state || !state.ghost) {
            if (state) dispose(el);
            var chart = LightweightCharts.createChart(el, Object.assign({ autoSize: true }, themeOptions()));
            var candles = chart.addCandlestickSeries({ borderVisible: false });
            var stop = chart.addLineSeries({ color: 'rgba(224,72,61,.55)', lineWidth: 2, lineStyle: 2, priceLineVisible: false, lastValueVisible: false });
            var tip = document.createElement('div');
            tip.className = 'ghost-tip';
            tip.setAttribute('role', 'tooltip');
            tip.style.cssText = 'position:absolute;display:none;z-index:5;pointer-events:none;max-width:28rem;padding:.4rem .55rem;border-radius:.35rem;font-size:.75rem;line-height:1.3;white-space:pre-line;background:var(--surface,#161b22);color:var(--text-primary,#e6edf3);border:1px solid var(--border,#30363d)';
            el.style.position = 'relative';
            el.appendChild(tip);
            state = { chart: chart, candles: candles, stop: stop, levels: [], tip: tip, ghost: true, forks: {} };
            charts.set(el, state);
            chart.subscribeCrosshairMove(function (p) {
                var f = p && p.time !== undefined ? state.forks[p.time] : null;
                if (!f || !p.point) { tip.style.display = 'none'; return; }
                tip.textContent = f.lines.join('\n');
                tip.style.left = Math.min(p.point.x + 12, el.clientWidth - 260) + 'px';
                tip.style.top = (p.point.y + 12) + 'px';
                tip.style.display = 'block';
            });
        }
        var bars = payload.bars || [];
        var cut = upto >= 0 && upto < bars.length ? bars[upto].time : (bars.length ? bars[bars.length - 1].time : 0);
        var up = css('--green', '#0f9d78'), down = css('--red', '#e0483d'), faded = 'rgba(139,148,158,.25)';
        state.candles.setData(bars.map(function (b) {
            var c = b.time <= cut ? (b.close >= b.open ? up : down) : faded;
            return { time: b.time, open: b.open, high: b.high, low: b.low, close: b.close, color: c, wickColor: c, borderColor: c };
        }));
        state.candles.setMarkers((payload.markers || []).filter(function (m) { return m.time <= cut; }).map(function (m) {
            return { time: m.time, position: m.position, shape: m.shape, color: m.color, text: m.text };
        }));
        state.stop.setData((payload.stopPath || []).filter(function (p) { return p.time <= cut; }).map(function (p) { return { time: p.time, value: p.value }; }));

        state.levels.forEach(function (s) { state.chart.removeSeries(s); });
        state.levels = [];
        state.forks = {};
        (payload.forks || []).forEach(function (f) { if (f.time <= cut) state.forks[f.time] = f; });
        (payload.levels || []).forEach(function (l) {
            if (l.from > cut) return;
            var end = Math.min(l.to, cut);
            var pts = end > l.from ? [{ time: l.from, value: l.price }, { time: end, value: l.price }] : [{ time: l.from, value: l.price }];
            var s = state.chart.addLineSeries({ color: l.color, lineWidth: l.dashed ? 1 : 2, lineStyle: l.dashed ? 2 : 0, priceLineVisible: false, lastValueVisible: false, crosshairMarkerVisible: false });
            s.setData(pts);
            state.levels.push(s);
        });
        if (!state.fitted) { state.chart.timeScale().fitContent(); state.fitted = true; }
    }

    function download(name, mime, text) {
        var url = URL.createObjectURL(new Blob([text], { type: mime }));
        var a = document.createElement('a');
        a.href = url; a.download = name;
        document.body.appendChild(a); a.click(); a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
    }

    window.ipChart = { render: render, dispose: dispose, ghost: ghost, download: download };
})();
