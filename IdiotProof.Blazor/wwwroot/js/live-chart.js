/* live-chart.js — live strategy canvas chart for /live/{id}
   Called from LiveChart.razor via JSInterop: window.liveChart.draw(dataJson)
   DATA shape matches ReplayTemplates.cs DATA exactly so the same canvas logic
   renders both static replays and live bars. */
(function () {
    var cv, ctx, tip, ohlcEl, bars, conds, geo, hover;

    function css(n) { return getComputedStyle(document.documentElement).getPropertyValue(n).trim(); }
    function f(n, d) { return (+n).toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d }); }
    function fv(n) { return n >= 1000 ? (n / 1000).toFixed(1) + 'K' : '' + n; }

    function ln(x1, y1, x2, y2, c, w, dash) {
        ctx.save(); ctx.strokeStyle = c; ctx.lineWidth = w;
        if (dash) ctx.setLineDash(dash);
        ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke(); ctx.restore();
    }

    function layout() {
        var rc = cv.getBoundingClientRect(), dpr = Math.min(devicePixelRatio || 1, 2);
        cv.width = rc.width * dpr | 0; cv.height = rc.height * dpr | 0;
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        var W = rc.width, H = rc.height, pL = 8, pR = 58, pT = 12, pB = 22,
            priceH = (H - pT - pB) * .58 | 0,
            volTop = pT + priceH + 10, volH = (H - pT - pB) * .12 | 0,
            condTop = volTop + volH + 16, condH = (H - pB) - condTop, plotW = W - pL - pR;
        var lo = 1e9, hi = -1e9, vmax = 0;
        for (var b of bars) { lo = Math.min(lo, b.l); hi = Math.max(hi, b.h); vmax = Math.max(vmax, b.v); }
        var pd = (hi - lo) * .06; lo -= pd; hi += pd;
        var n = bars.length, slot = plotW / n, bw = Math.max(2.5, Math.min(11, slot * .62));
        geo = {
            W, H, pL, pR, pT, pB, priceH, volTop, volH, condTop, condH, plotW, lo, hi, vmax, n, slot, bw,
            x: function (i) { return pL + slot * (i + .5); },
            yP: function (p) { return pT + priceH - ((p - lo) / (hi - lo)) * priceH; },
            yV: function (v) { return volTop + volH - (v / vmax) * volH; }
        };
    }

    function draw() {
        if (!geo || !bars || !bars.length) return;
        var g = geo;
        ctx.clearRect(0, 0, g.W, g.H);
        var C = {
            dim: css('--dim'), faint: css('--faint'), grid: css('--grid') || css('--edge'),
            axis: css('--axis') || css('--dim'), up: css('--up') || '#26a69a',
            down: css('--down') || '#ef5350', vwap: css('--vwap') || '#6c8ebf',
            whigh: css('--whigh') || '#f5a623', accent: css('--accent') || '#f5c518',
            good: css('--good') || '#26a69a', vd: css('--void') || css('--edge')
        };
        ctx.font = '10px ' + css('--mono');

        // price grid
        for (var i = 0; i <= 5; i++) {
            var p = g.lo + (g.hi - g.lo) * i / 5, y = g.yP(p);
            ln(g.pL, y, g.pL + g.plotW, y, C.grid, 1);
            ctx.fillStyle = C.faint; ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
            ctx.fillText(f(p, 2), g.pL + g.plotW + 6, y);
        }

        // window high step line
        ctx.save(); ctx.strokeStyle = C.whigh; ctx.lineWidth = 1.4; ctx.setLineDash([5, 3]);
        ctx.beginPath();
        bars.forEach(function (b, i) {
            var x0 = g.x(i) - g.slot / 2, x1 = g.x(i) + g.slot / 2, y = g.yP(b.whigh);
            i === 0 ? ctx.moveTo(x0, y) : ctx.lineTo(x0, y); ctx.lineTo(x1, y);
        }); ctx.stroke(); ctx.restore();

        // VWAP line
        ctx.save(); ctx.strokeStyle = C.vwap; ctx.lineWidth = 1.8; ctx.beginPath();
        bars.forEach(function (b, i) {
            var x = g.x(i), y = g.yP(b.vwap); i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
        }); ctx.stroke(); ctx.restore();

        // candles
        bars.forEach(function (b, i) {
            var x = g.x(i), up = b.c >= b.o, col = up ? C.up : C.down;
            ln(x, g.yP(b.h), x, g.yP(b.l), col, 1);
            var yO = g.yP(b.o), yC = g.yP(b.c), t = Math.min(yO, yC), h = Math.max(1.5, Math.abs(yO - yC));
            ctx.fillStyle = col; ctx.fillRect(x - g.bw / 2, t, g.bw, h);
        });

        // volume bars
        bars.forEach(function (b, i) {
            var x = g.x(i); ctx.fillStyle = b.c >= b.o ? C.up : C.down; ctx.globalAlpha = .5;
            var y = g.yV(b.v); ctx.fillRect(x - g.bw / 2, y, g.bw, g.volTop + g.volH - y);
        }); ctx.globalAlpha = 1;
        ctx.fillStyle = C.faint; ctx.textAlign = 'left'; ctx.textBaseline = 'top';
        ctx.fillText('VOLUME', g.pL + 2, g.volTop - 1);

        // entry fire arrows
        bars.forEach(function (b, i) {
            if (!b.fire) return;
            var x = g.x(i);
            ctx.save(); ctx.globalAlpha = .1; ctx.fillStyle = C.accent;
            ctx.fillRect(x - g.bw / 2 - 1, g.pT, g.bw + 2, g.priceH); ctx.restore();
            var yp = g.yP(b.c);
            ln(x - 9, yp, x + 9, yp, C.accent, 1.6);
            ctx.fillStyle = C.accent; ctx.beginPath();
            ctx.moveTo(x, yp); ctx.lineTo(x - 5, yp + 10); ctx.lineTo(x + 5, yp + 10);
            ctx.closePath(); ctx.fill();
        });

        // exit arrows
        bars.forEach(function (b, i) {
            if (!b.exit) return;
            var x = g.x(i);
            ctx.save(); ctx.globalAlpha = .1; ctx.fillStyle = C.down;
            ctx.fillRect(x - g.bw / 2 - 1, g.pT, g.bw + 2, g.priceH); ctx.restore();
            var yp = g.yP(b.c);
            ln(x - 9, yp, x + 9, yp, C.down, 1.6);
            ctx.fillStyle = C.down; ctx.beginPath();
            ctx.moveTo(x, yp); ctx.lineTo(x - 5, yp - 10); ctx.lineTo(x + 5, yp - 10);
            ctx.closePath(); ctx.fill();
        });

        // condition Gantt
        var rh = g.condH / Math.max(1, conds.length);
        ctx.textAlign = 'right'; ctx.textBaseline = 'middle'; ctx.font = '9px ' + css('--mono');
        conds.forEach(function (label, ri) {
            var cy = g.condTop + rh * ri;
            ctx.fillStyle = C.faint;
            ctx.fillText(label.length > 26 ? label.slice(0, 25) + '…' : label, g.pL + g.plotW + 54, cy + rh / 2);
            bars.forEach(function (b, i) {
                var on = b.cnd && b.cnd[ri], x = g.x(i) - g.bw / 2;
                ctx.fillStyle = on ? (b.fire ? C.accent : C.good) : C.vd;
                ctx.globalAlpha = on ? (b.fire ? .95 : .5) : 1;
                ctx.fillRect(x, cy + 1.5, g.bw, rh - 3);
            });
        }); ctx.globalAlpha = 1;
        ctx.fillStyle = C.faint; ctx.textAlign = 'left'; ctx.textBaseline = 'bottom';
        ctx.fillText('ENTRY CONDITIONS (filled = true · gold column = fire)', g.pL + 2, g.condTop - 3);

        // time axis
        ctx.textAlign = 'center'; ctx.textBaseline = 'top'; ctx.font = '10px ' + css('--mono');
        var step = Math.ceil(bars.length / 9);
        bars.forEach(function (b, i) { if (i % step === 0) ctx.fillText(b.et, g.x(i), g.H - g.pB + 5); });

        // hover crosshair
        if (hover >= 0) ln(g.x(hover), g.pT, g.x(hover), g.condTop + g.condH, C.axis, 1, [3, 3]);
    }

    function showTip(i, mx) {
        var b = bars[i];
        if (!b) return;
        if (ohlcEl) ohlcEl.innerHTML =
            '<span>O <b>' + f(b.o, 2) + '</b></span>' +
            '<span>H <b>' + f(b.h, 2) + '</b></span>' +
            '<span>L <b>' + f(b.l, 2) + '</b></span>' +
            '<span>C <b>' + f(b.c, 2) + '</b></span>' +
            '<span>Vol <b>' + fv(b.v) + '</b></span>';
        var cr = (conds || []).map(function (lab, j) {
            return '<div class="tr"><span>' + lab + '</span><span class="' +
                (b.cnd && b.cnd[j] ? 'yes' : 'no') + '">' +
                (b.cnd && b.cnd[j] ? '✓' : '✗') + '</span></div>';
        }).join('');
        var status = b.fire ? '<div class="tf">▲ ENTRY</div>' :
                     b.exit ? '<div class="tf" style="background:var(--down);color:#fff">▼ EXIT</div>' : '';
        if (tip) {
            tip.innerHTML =
                '<div class="tt">' + b.et + ' ET' + (b.inSession ? '' : ' (out of session)') + '</div>' +
                '<div class="tr"><span>close</span><span>' + f(b.c, 2) + '</span></div>' +
                '<div class="tr"><span>win hi</span><span>' + f(b.whigh, 2) + '</span></div>' +
                '<div class="tr"><span>vwap</span><span>' + f(b.vwap, 2) + '</span></div>' +
                '<div class="tr"><span>vol×</span><span>' + f(b.volx, 2) + '</span></div>' +
                '<div class="ts"></div>' + cr + status;
            var rc = cv.getBoundingClientRect(), tw = tip.offsetWidth;
            var l = Math.min(Math.max(mx - tw / 2, 6), rc.width - tw - 6);
            tip.style.left = l + 'px'; tip.style.top = 'auto'; tip.style.bottom = '12px';
            tip.style.opacity = '1';
        }
    }

    function redraw() { layout(); draw(); }

    window.liveChart = {
        draw: function (dataJson) {
            var DATA = typeof dataJson === 'string' ? JSON.parse(dataJson) : dataJson;
            bars = DATA.bars || [];
            conds = DATA.conditions || [];
            cv = document.getElementById('cv');
            tip = document.getElementById('tip');
            ohlcEl = document.getElementById('ohlc');
            if (!cv) return;
            ctx = cv.getContext('2d');
            hover = -1;

            cv.onpointermove = function (e) {
                var rc = cv.getBoundingClientRect(), mx = e.clientX - rc.left;
                if (!geo) return;
                var i = Math.max(0, Math.min(bars.length - 1, Math.round((mx - geo.pL) / geo.slot - .5)));
                if (i !== hover) { hover = i; draw(); }
                showTip(i, mx);
            };
            cv.onpointerleave = function () {
                hover = -1;
                if (tip) tip.style.opacity = '0';
                if (ohlcEl) ohlcEl.innerHTML = '';
                draw();
            };

            // one-time listeners
            if (!window.__liveChartListening) {
                window.__liveChartListening = true;
                window.addEventListener('resize', redraw);
                new MutationObserver(redraw).observe(
                    document.documentElement,
                    { attributes: true, attributeFilter: ['data-theme'] }
                );
            }

            redraw();
        }
    };
})();
