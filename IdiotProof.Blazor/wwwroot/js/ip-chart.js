/* ip-chart.js — Chart.razor's bridge to TradingView Lightweight Charts (window.LightweightCharts). */
(function () {
    var charts = new WeakMap();

    function css(name, fallback) {
        var v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        return v || fallback;
    }

    // tickMarkType: 0 year, 1 month, 2 day of month, 3 time, 4 time with seconds.
    function etTick(t, tickMarkType) {
        var d = new Date(t * 1000), tz = { timeZone: 'America/New_York' };
        if (tickMarkType === 0) return d.toLocaleDateString('en-US', Object.assign({ year: 'numeric' }, tz));
        if (tickMarkType === 1) return d.toLocaleDateString('en-US', Object.assign({ month: 'short' }, tz));
        if (tickMarkType === 2) return d.toLocaleDateString('en-US', Object.assign({ month: 'short', day: 'numeric' }, tz));
        return d.toLocaleTimeString('en-US', Object.assign({ hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }, tz));
    }

    function themeOptions() {
        return {
            layout: { background: { color: 'transparent' }, textColor: css('--text-secondary', '#8b949e') },
            grid: { vertLines: { color: css('--border', '#30363d') }, horzLines: { color: css('--border', '#30363d') } },
            // Axis ticks default to UTC; label them on the ET clock like the rest of the app.
            timeScale: { timeVisible: true, secondsVisible: false, tickMarkFormatter: etTick },
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
            state = { chart: chart, candles: candles, stop: stop, levels: [], tip: tip, ghost: true, forks: [] };
            charts.set(el, state);
            chart.subscribeCrosshairMove(function (p) {
                var f = null;
                if (p && p.time !== undefined) {
                    // Segments of different If() blocks can overlap; the latest-starting one is the most specific.
                    for (var i = 0; i < state.forks.length; i++) {
                        var s = state.forks[i];
                        var end = Math.min(s.until !== undefined ? s.until : s.time, state.cut);
                        if (p.time >= s.time && p.time <= end && (!f || s.time >= f.time)) f = s;
                    }
                }
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
        state.cut = cut;
        state.forks = (payload.forks || []).filter(function (f) { return f.time <= cut; });
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
