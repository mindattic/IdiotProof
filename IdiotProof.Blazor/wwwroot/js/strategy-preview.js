'use strict';

// Schematic trade-blueprint previews for the Strategies expand panel.
// Draws a mini candlestick chart that illustrates the setup: higher-low
// consolidation below the key level, breakout bar, run to targets.
window.strategyPreview = (() => {

    function draw(canvasId, cfg) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        // requestAnimationFrame ensures the canvas has been laid out
        // and clientWidth/Height are non-zero before we size it.
        requestAnimationFrame(() => _paint(canvas, cfg));
    }

    function _paint(canvas, cfg) {
        const dpr = window.devicePixelRatio || 1;
        const W   = canvas.clientWidth  || 300;
        const H   = canvas.clientHeight || 160;
        canvas.width  = Math.round(W * dpr);
        canvas.height = Math.round(H * dpr);
        const ctx = canvas.getContext('2d');
        ctx.scale(dpr, dpr);

        const cs     = getComputedStyle(document.documentElement);
        const green  = cs.getPropertyValue('--green').trim()  || '#3fb950';
        const red    = cs.getPropertyValue('--red').trim()    || '#f85149';
        const brand  = cs.getPropertyValue('--brand').trim()  || '#d29922';
        const yellow = cs.getPropertyValue('--yellow').trim() || '#d29922';
        const vwapBlue = '#38bdf8';   // sky-blue — matches live-chart.js VWAP colour

        // Surface — matches .trading-card dark background
        ctx.fillStyle = '#0d1117';
        ctx.fillRect(0, 0, W, H);

        const PAD = { l: 4, r: 60, t: 10, b: 10 };
        const cW  = W - PAD.l - PAD.r;
        const cH  = H - PAD.t - PAD.b;

        const prices = [cfg.stop, cfg.holds, cfg.entry, cfg.t1, cfg.t2, cfg.rollingHigh, cfg.rollingLow, cfg.peakGiveback]
            .filter(v => v != null && v > 0);
        if (prices.length < 2) return;

        const minP  = Math.min(...prices) * 0.96;
        const maxP  = Math.max(...prices) * 1.04;
        const range = maxP - minP || 1;
        const toY   = p => PAD.t + cH * (1 - (p - minP) / range);

        // ── schematic candles (left 62% of chart width) ──────────────────
        const candleAreaW = cW * 0.62;
        const bars        = _makeBars(cfg);
        const slot        = candleAreaW / bars.length;
        const bodyW       = Math.max(slot * 0.55, 2.5);

        bars.forEach((b, i) => {
            const cx    = PAD.l + (i + 0.5) * slot;
            const isUp  = b.c >= b.o;
            const color = isUp ? green : red;

            ctx.strokeStyle = color;
            ctx.lineWidth   = 1;
            ctx.beginPath();
            ctx.moveTo(cx, toY(b.h));
            ctx.lineTo(cx, toY(b.l));
            ctx.stroke();

            const y1 = toY(Math.max(b.o, b.c));
            const y2 = toY(Math.min(b.o, b.c));
            ctx.fillStyle = color;
            ctx.fillRect(cx - bodyW / 2, y1, bodyW, Math.max(y2 - y1, 1.5));
        });

        // ── stop zone (red band from stop up to entry) ───────────────────
        if (cfg.entry != null && cfg.stop != null) {
            const yE = toY(cfg.entry), yS = toY(cfg.stop);
            ctx.fillStyle   = red;
            ctx.globalAlpha = 0.10;
            ctx.fillRect(PAD.l, yE, W - PAD.l - PAD.r, yS - yE);
            ctx.globalAlpha = 1;
        }

        // ── horizontal level lines + right-side price labels ─────────────
        const lx1 = PAD.l, lx2 = W - PAD.r;

        function hline(price, color, dash, lw) {
            if (price == null) return;
            const y = toY(price);
            ctx.strokeStyle = color;
            ctx.lineWidth   = lw;
            ctx.setLineDash(dash);
            ctx.beginPath();
            ctx.moveTo(lx1, y); ctx.lineTo(lx2, y);
            ctx.stroke();
            ctx.setLineDash([]);

            ctx.fillStyle    = color;
            ctx.font         = '10px "Roboto Mono","Courier New",monospace';
            ctx.textAlign    = 'left';
            ctx.textBaseline = 'middle';
            ctx.fillText('$' + price.toFixed(2), lx2 + 4, y);
        }

        hline(cfg.stop,  red,      [],     1.2);  // stop: solid red
        hline(cfg.holds, brand,    [4, 3], 1.2);  // holds support: dashed gold
        hline(cfg.entry, vwapBlue, [],     1.8);  // entry / VWAP: sky blue
        hline(cfg.t1,    yellow,   [],     1.5);  // T1: gold
        hline(cfg.t2,    green,    [],     1.8);  // T2: green

        // VWAP tag on the entry line — hasVwapAbove/hasVwapReclaim were previously
        // accepted but never read, so a strategy gated on VWAP looked identical to
        // one that wasn't.
        if ((cfg.hasVwapAbove || cfg.hasVwapReclaim) && cfg.entry != null) {
            const y = toY(cfg.entry);
            ctx.fillStyle = vwapBlue;
            ctx.font = '9px "Roboto Mono","Courier New",monospace';
            ctx.textAlign = 'left';
            ctx.textBaseline = 'bottom';
            ctx.fillText(cfg.hasVwapReclaim ? 'VWAP ↩' : 'VWAP', lx1 + 2, y - 3);
        }

        // Dynamic/trailing exit levels (rolling high/low, peak giveback) move
        // day-to-day, unlike a fixed stop/target — a double-dashed line + text
        // label signals "this follows the market," not a fixed $ price.
        function hlineDynamic(price, label, color) {
            if (price == null || !label) return;
            const y = toY(price);
            ctx.strokeStyle = color;
            ctx.lineWidth = 1;
            ctx.setLineDash([2, 2]);
            ctx.beginPath(); ctx.moveTo(lx1, y - 1.5); ctx.lineTo(lx2, y - 1.5); ctx.stroke();
            ctx.beginPath(); ctx.moveTo(lx1, y + 1.5); ctx.lineTo(lx2, y + 1.5); ctx.stroke();
            ctx.setLineDash([]);

            ctx.fillStyle = color;
            ctx.font = '9px "Roboto Mono","Courier New",monospace';
            ctx.textAlign = 'left';
            ctx.textBaseline = 'middle';
            ctx.fillText(label, lx2 + 4, y);
        }

        hlineDynamic(cfg.rollingHigh,   cfg.rollingHighLabel,   brand);
        hlineDynamic(cfg.rollingLow,    cfg.rollingLowLabel,    red);
        hlineDynamic(cfg.peakGiveback,  cfg.peakGivebackLabel,  yellow);
    }

    // Generate schematic OHLC bars: higher-low consolidation → breakout → run.
    // For a Short strategy, every price is mirrored around entry so the
    // schematic shows a breakDOWN (lower-high consolidation → run lower)
    // instead of illustrating a bullish setup for a strategy that profits
    // when price falls.
    function _makeBars(cfg) {
        const E = cfg.entry;
        if (E == null || E <= 0) return [];
        const T2 = cfg.t2 || (cfg.isShort ? E * 0.20 : E * 1.80);
        const T1 = cfg.t1 || (cfg.isShort ? E * 0.45 : E * 1.55);
        const bars = [
            { o: E*0.950, h: E*0.970, l: E*0.855, c: E*0.920 },           // red
            { o: E*0.920, h: E*0.962, l: E*0.882, c: E*0.900 },           // red  (higher low)
            { o: E*0.900, h: E*0.985, l: E*0.906, c: E*0.935 },           // red  (higher low)
            { o: E*0.935, h: E*1.055, l: E*0.932, c: E*1.042 },           // green breakout
            { o: E*1.042, h: E+(T1-E)*0.82, l: E*1.008, c: E+(T1-E)*0.78 }, // green run
            { o: E+(T1-E)*0.78, h: T2*1.012,   l: T1*0.940, c: T2*0.968 },  // green targets
        ];
        if (!cfg.isShort) return bars;
        const mirror = v => 2 * E - v;
        return bars.map(b => ({ o: mirror(b.o), h: mirror(b.l), l: mirror(b.h), c: mirror(b.c) }));
    }

    return { draw };
})();
