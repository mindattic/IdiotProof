namespace IdiotProof.Monitor;

/// <summary>
/// Self-contained HTML templates for the replay harness. Tokens are replaced by
/// StrategyReplay: <c>__DATA__</c> (JSON), <c>__STRATEGY_HTML__</c> (phase-card
/// fragment) for the run page; <c>__SYMBOL__</c>/<c>__COUNT__</c>/<c>__RUNS__</c>
/// for the per-ticker index. The run page draws a candlestick chart on a canvas
/// (no chart lib), renders phase cards inline, and offers the strategy flow in
/// two tabs — a hand-rolled inline SVG and a Mermaid render loaded from CDN.
/// All clock values are US Eastern (produced ET by the harness).
/// </summary>
internal static class ReplayTemplates
{
    public const string Run = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="light dark">
<title>Replay · IdiotProof</title>
<script>(function(){var t;try{t=localStorage.getItem('ip-theme')}catch(e){}document.documentElement.setAttribute('data-theme',t==='light'?'light':'dark');})();</script>
<script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>
<style>
:root{--bg:#f3f4f7;--panel:#fff;--panel2:#f7f8fa;--edge:#e2e5ea;--grid:rgba(20,26,40,.06);--axis:rgba(20,26,40,.1);
--ink:#1a1f2b;--dim:#5b6472;--faint:#8b94a3;--up:#0f9d78;--down:#e0483d;--vwap:#7b52d6;--whigh:#e08a2c;
--accent:#FCD72B;--good:#0f9d78;--bad:#c2544a;--void:rgba(20,26,40,.05);--warn:#c2544a;
--shadow:0 1px 2px rgba(16,22,40,.06),0 8px 30px rgba(16,22,40,.08);
--mono:ui-monospace,"SF Mono","Cascadia Mono",Menlo,Consolas,monospace;--sans:"Segoe UI",system-ui,-apple-system,Arial,sans-serif;}
@media(prefers-color-scheme:dark){:root{--bg:#0a0d13;--panel:#10141d;--panel2:#0c1017;--edge:#1e2530;--grid:rgba(255,255,255,.045);--axis:rgba(255,255,255,.09);
--ink:#d7dbe4;--dim:#8b94a5;--faint:#5c6577;--up:#26a69a;--down:#ef5350;--vwap:#a98bff;--whigh:#ffa64d;--accent:#f5c518;--good:#26a69a;--bad:#ef5350;--void:rgba(255,255,255,.05);--warn:#ef5350;
--shadow:0 1px 2px rgba(0,0,0,.4),0 10px 40px rgba(0,0,0,.5);}}
:root[data-theme="light"]{--bg:#f3f4f7;--panel:#fff;--panel2:#f7f8fa;--edge:#e2e5ea;--grid:rgba(20,26,40,.06);--axis:rgba(20,26,40,.1);--ink:#1a1f2b;--dim:#5b6472;--faint:#8b94a3;--up:#0f9d78;--down:#e0483d;--vwap:#7b52d6;--whigh:#e08a2c;--accent:#FCD72B;--good:#0f9d78;--bad:#c2544a;--void:rgba(20,26,40,.05);--warn:#c2544a;--shadow:0 1px 2px rgba(16,22,40,.06),0 8px 30px rgba(16,22,40,.08);}
:root[data-theme="dark"]{--bg:#0a0d13;--panel:#10141d;--panel2:#0c1017;--edge:#1e2530;--grid:rgba(255,255,255,.045);--axis:rgba(255,255,255,.09);--ink:#d7dbe4;--dim:#8b94a5;--faint:#5c6577;--up:#26a69a;--down:#ef5350;--vwap:#a98bff;--whigh:#ffa64d;--accent:#f5c518;--good:#26a69a;--bad:#ef5350;--void:rgba(255,255,255,.05);--warn:#ef5350;--shadow:0 1px 2px rgba(0,0,0,.4),0 10px 40px rgba(0,0,0,.5);}
/* --panel-2 aliases --panel2 (server SVG uses the hyphenated name); --accent-ink
   is the readable accent for TEXT (dark gold on light, yellow on dark) so accent
   text never becomes unreadable yellow-on-white. */
:root{--panel-2:var(--panel2);--accent-ink:#8a6a00}
:root[data-theme="dark"]{--accent-ink:#f5c518}
@media(prefers-color-scheme:dark){:root:not([data-theme="light"]){--accent-ink:#f5c518}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font-family:var(--sans);line-height:1.5;-webkit-font-smoothing:antialiased}
.wrap{max-width:1180px;margin:0 auto;padding:24px 20px 64px}
.themebtn{position:fixed;top:14px;right:16px;z-index:30;width:36px;height:36px;border-radius:999px;border:1px solid var(--edge);background:var(--panel);color:var(--ink);cursor:pointer;font-size:16px;line-height:1;box-shadow:var(--shadow)}
.themebtn:hover{border-color:var(--accent)}
.tape{display:flex;flex-wrap:wrap;align-items:baseline;gap:8px 18px;padding:14px 18px;background:var(--panel);border:1px solid var(--edge);border-radius:12px 12px 0 0;box-shadow:var(--shadow)}
.sym{font-family:var(--mono);font-weight:700;font-size:20px;letter-spacing:.5px}.cname{color:var(--dim);font-size:13px}.tape .sp{flex:1}
.ohlc{display:flex;gap:14px;font-family:var(--mono);font-size:12.5px;color:var(--dim)}.ohlc b{color:var(--ink);font-weight:600}
.badge{font-family:var(--mono);font-size:11px;padding:3px 9px;border-radius:999px;border:1px solid var(--edge);color:var(--dim);background:var(--panel2);white-space:nowrap}
.badge.feed{color:var(--vwap);border-color:color-mix(in srgb,var(--vwap) 40%,var(--edge))}
button.tgl{margin-left:6px;font-family:var(--mono);font-size:11px;cursor:pointer;padding:4px 10px;border-radius:999px;border:1px solid var(--edge);background:var(--panel2);color:var(--dim)}
.verdict{display:flex;flex-wrap:wrap;align-items:center;gap:14px;padding:16px 18px;background:var(--panel);border:1px solid var(--edge);border-top:none}
.verdict .flag{font-family:var(--mono);font-weight:700;font-size:13px;letter-spacing:1px;padding:6px 12px;border-radius:7px;text-transform:uppercase}
.verdict .flag.yes{color:#1a1206;background:var(--accent)}.verdict .flag.no{color:var(--dim);background:var(--void);border:1px solid var(--edge)}
.verdict .hd{font-size:15px}.verdict .hd b{font-family:var(--mono);font-size:16px}.verdict .px{color:var(--accent-ink);font-weight:700}
.verdict .sub{color:var(--dim);font-size:13px;margin-left:auto;text-align:right;font-family:var(--mono)}
.card{background:var(--panel);border:1px solid var(--edge);box-shadow:var(--shadow)}
.chart{position:relative;border-top:none;border-radius:0 0 12px 12px;overflow:hidden}
canvas{display:block;width:100%;height:600px;touch-action:none}
#tip{position:absolute;pointer-events:none;z-index:5;opacity:0;transition:opacity .09s;background:color-mix(in srgb,var(--panel) 92%,transparent);backdrop-filter:blur(6px);border:1px solid var(--edge);border-radius:9px;padding:10px 11px;min-width:180px;font-family:var(--mono);font-size:11.5px;box-shadow:var(--shadow)}
#tip .tt{font-weight:700;margin-bottom:6px}#tip .tr{display:flex;justify-content:space-between;gap:16px;color:var(--dim)}#tip .tr span:last-child{color:var(--ink)}
#tip .ts{height:1px;background:var(--edge);margin:7px 0}#tip .yes{color:var(--good)}#tip .no{color:var(--bad)}
#tip .tf{margin-top:6px;color:#1a1206;background:var(--accent);text-align:center;border-radius:5px;padding:3px;font-weight:700}
.legend{display:flex;flex-wrap:wrap;gap:6px 18px;padding:12px 4px 2px;font-family:var(--mono);font-size:11.5px;color:var(--dim)}
.legend span{display:inline-flex;align-items:center;gap:6px}.sw{width:16px;border-top:2px solid;display:inline-block}
table.payoffs{width:100%;border-collapse:collapse;background:var(--panel);border:1px solid var(--edge);border-radius:11px;overflow:hidden;box-shadow:var(--shadow);font-size:13px}
table.payoffs th{text-align:left;font-family:var(--mono);font-size:11px;text-transform:uppercase;letter-spacing:.5px;color:var(--faint);padding:10px 12px;border-bottom:1px solid var(--edge)}
table.payoffs td{padding:9px 12px;border-bottom:1px solid var(--edge);color:var(--ink)}table.payoffs tr:last-child td{border-bottom:none}
table.payoffs td.mono{font-family:var(--mono)}table.payoffs td.pos{color:var(--good);font-weight:700}table.payoffs td.neg{color:var(--bad);font-weight:700}
table.payoffs td.rsn{color:var(--dim);font-family:var(--mono);font-size:11.5px}
h2{font-size:13px;text-transform:uppercase;letter-spacing:.8px;color:var(--faint);margin:26px 0 10px}
.phases{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:12px}
.phase{background:var(--panel);border:1px solid var(--edge);border-radius:11px;padding:13px 14px;box-shadow:var(--shadow)}
.phase-h{display:flex;flex-direction:column;margin-bottom:8px}.phase-n{font-weight:700;font-size:13px}.phase-s{font-size:11px;color:var(--faint)}
.phase ul{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:5px}
.phase li{font-size:13px;display:flex;justify-content:space-between;gap:10px}.phase li.mono{font-family:var(--mono);font-size:12px;color:var(--ink);display:block}
.phase .k{color:var(--dim)}.phase .v{color:var(--ink);font-family:var(--mono);text-align:right}.phase .warn{color:var(--warn)}
.phase[data-phase="entry"]{border-color:color-mix(in srgb,var(--accent) 45%,var(--edge))}
.flowcard{background:var(--panel);border:1px solid var(--edge);border-radius:11px;box-shadow:var(--shadow);overflow:hidden}
.tabs{display:flex;gap:2px;padding:8px 10px 0;border-bottom:1px solid var(--edge)}
.tab{font-family:var(--mono);font-size:12px;cursor:pointer;padding:7px 14px;border:1px solid transparent;border-bottom:none;border-radius:8px 8px 0 0;color:var(--dim);background:transparent}
.tab.on{color:var(--ink);background:var(--panel2);border-color:var(--edge)}
.pane{padding:16px;display:none;overflow-x:auto}.pane.on{display:block}
.mermaid{font-family:var(--mono)}
.notes{margin-top:14px;display:grid;grid-template-columns:1fr 1fr;gap:14px}
@media(max-width:760px){.notes{grid-template-columns:1fr}.ohlc{display:none}}
.note{background:var(--panel);border:1px solid var(--edge);border-radius:11px;padding:15px 16px;box-shadow:var(--shadow)}
.note h3{margin:0 0 8px;font-size:12px;text-transform:uppercase;letter-spacing:.7px;color:var(--faint)}
.note p{margin:0 0 8px;font-size:13.5px;color:var(--dim)}.note b{color:var(--ink)}.note .k{font-family:var(--mono);color:var(--ink)}
.gate{display:flex;align-items:center;gap:10px;font-size:13.5px;margin-bottom:6px}
.gate .n{font-family:var(--mono);font-size:11px;width:20px;height:20px;flex:none;display:grid;place-items:center;border-radius:5px;background:var(--panel2);border:1px solid var(--edge);color:var(--dim)}
.gate.pass .n{background:color-mix(in srgb,var(--good) 18%,var(--panel));color:var(--good)}
footer{margin-top:22px;font-size:12px;color:var(--faint);font-family:var(--mono);text-align:center}
</style>
</head>
<body>
<button id="themeBtn" class="themebtn" type="button" aria-label="Toggle light or dark theme"></button>
<div class="wrap">
  <div class="tape">
    <span class="sym" id="sym">—</span><span class="cname" id="cname"></span>
    <span class="sp"></span>
    <div class="ohlc" id="ohlc"></div>
  </div>
  <div class="verdict" id="verdict"></div>
  <div class="card chart"><canvas id="cv"></canvas><div id="tip"></div></div>
  <div class="legend">
    <span><i class="sw" style="border-color:var(--vwap)"></i>VWAP</span>
    <span><i class="sw" style="border-color:var(--whigh);border-style:dashed"></i>Window high</span>
    <span><i class="sw" style="width:0;height:0;border:0;border-left:6px solid transparent;border-right:6px solid transparent;border-bottom:9px solid var(--accent)"></i>Entry / fire (all conditions true)</span>
    <span><i class="sw" style="width:0;height:0;border:0;border-left:6px solid transparent;border-right:6px solid transparent;border-top:9px solid var(--down)"></i>Exit (stop / trail / target / sell-by)</span>
    <span style="color:var(--faint)">Bottom band = per-condition truth each minute (gold column = fire)</span>
  </div>

  <div id="payoffs"></div>

  <h2>Strategy</h2>
  <div class="phases">__STRATEGY_HTML__</div>

  <h2>Flow — steps &amp; branching</h2>
  <div class="flowcard">
    <div class="tabs">
      <button class="tab on" data-pane="svg" type="button">Inline SVG</button>
      <button class="tab" data-pane="mmd" type="button">Mermaid (CDN)</button>
    </div>
    <div class="pane on" id="pane-svg"><div id="flowsvg"></div></div>
    <div class="pane" id="pane-mmd"><pre class="mermaid" id="mmd"></pre></div>
  </div>

  <div class="notes">
    <div class="note">
      <h3>How to read this</h3>
      <p>Each minute bar was walked through the Monitor's real evaluator — the same <b class="k">WindowHigh</b>, cumulative <b class="k">VWAP</b>, and 20-bar volume ratio, and the strategy's actual conditions. A <b>fire</b> is the first in-session bar where every condition is true at once (entry is one-shot per day).</p>
      <p>Hover the chart to inspect any minute; the bottom band shows which condition was blocking on every other bar.</p>
    </div>
    <div class="note">
      <h3>Conditions are gate 1 of 3</h3>
      <div class="gate pass"><span class="n">1</span><span><b>Conditions</b> — shown above</span></div>
      <div class="gate"><span class="n">2</span><span><b>LLM voter quorum</b> — must approve</span></div>
      <div class="gate"><span class="n">3</span><span><b>Risk Guardian</b> — stop, sizing, circuit breaker</span></div>
      <p style="margin-top:8px">A confirmed match still has to clear the voter panel and the Risk Guardian before any order is placed. This replay proves gate 1 only.</p>
    </div>
    <div class="note" style="grid-column:1/-1">
      <h3>Data feed</h3>
      <p id="feednote"></p>
    </div>
  </div>
  <footer id="foot"></footer>
</div>

<script>
const DATA = __DATA__;
document.title = `${DATA.symbol} ${DATA.dateEt} replay · IdiotProof`;

/* theme toggle (moon/sun · default dark · localStorage) */
(function(){var r=document.documentElement,b=document.getElementById('themeBtn');
function ico(){b.textContent=r.getAttribute('data-theme')==='light'?'🌙':'☀️';}ico();
b.addEventListener('click',function(){var n=r.getAttribute('data-theme')==='light'?'dark':'light';r.setAttribute('data-theme',n);try{localStorage.setItem('ip-theme',n)}catch(e){}ico();redraw();});})();

/* header + verdict */
document.getElementById('sym').textContent=DATA.symbol;
document.getElementById('cname').textContent=`${DATA.strategy} · ${DATA.dateEt} · ${DATA.session}`;
(function(){var v=document.getElementById('verdict');
var pc=(DATA.payoffs||[]).length;
if(pc>0){var pnl=(+DATA.totalPnl),sign=pnl>0?'+':'';
 v.innerHTML=`<span class="flag yes">${pc} payoff${pc>1?'s':''}</span>`+
 `<div class="hd">${DATA.repeat?'Repeating':'One-shot'} · first entry <b>${DATA.firstFire} ET</b> ${DATA.side.toLowerCase()} <span class="px">@ $${(+DATA.entryPrice).toFixed(2)}</span> · total <span class="px">${sign}${pnl.toFixed(2)}%</span> across ${pc} round-trip${pc>1?'s':''}.</div>`+
 `<div class="sub">${DATA.feed} feed · generated ${DATA.generatedEt}</div>`;}
else{v.innerHTML=`<span class="flag no">No fire</span>`+
 `<div class="hd">No bar met all ${DATA.conditions.length} conditions in-session. Best: <b>${DATA.bestPassed}/${DATA.conditions.length}</b>${DATA.bestFail?` — waiting on <span class="px">${DATA.bestFail}</span>`:''}.</div>`+
 `<div class="sub">${DATA.feed} feed · generated ${DATA.generatedEt}</div>`;}})();
/* payoffs table */
(function(){var el=document.getElementById('payoffs');if(!el)return;var ps=DATA.payoffs||[];
if(!ps.length){el.style.display='none';return;}
var rows=ps.map((p,i)=>{var s=p.pnlPct>0?'pos':(p.pnlPct<0?'neg':'');var sg=p.pnlPct>0?'+':'';
 return `<tr><td>${i+1}</td><td class="mono">${p.entryEt}</td><td class="mono">$${(+p.entryPx).toFixed(2)}</td><td class="mono">${p.exitEt}</td><td class="mono">$${(+p.exitPx).toFixed(2)}</td><td class="mono ${s}">${sg}${(+p.pnlPct).toFixed(2)}%</td><td class="rsn">${p.reason}</td></tr>`}).join('');
el.innerHTML=`<h2>Payoffs — ${ps.length} round-trip${ps.length>1?'s':''} (times ET)</h2>`+
 `<table class="payoffs"><thead><tr><th>#</th><th>entry</th><th>@</th><th>exit</th><th>@</th><th>P&amp;L</th><th>reason</th></tr></thead><tbody>${rows}</tbody></table>`;
})();
document.getElementById('feednote').innerHTML = DATA.feed.indexOf('SIP')>=0
 ? `This replay ran on the <b>SIP</b> consolidated tape (delayed historical, free ≥15 min old). The live Monitor on the free <b>IEX</b> feed sees far less premarket activity for thin names — so a fire here does not mean the live system saw the same bars. Live premarket firing needs Alpaca real-time SIP (~$99/mo).`
 : `This replay ran on the <b>${DATA.feed}</b> feed.`;
document.getElementById('foot').textContent = `Faithful replay — IndicatorSnapshotBuilder + the strategy's real conditions + the shared ET session gate. ${DATA.bars.length} bars. Times US Eastern.`;

/* ── candlestick chart ── */
const cv=document.getElementById('cv'),ctx=cv.getContext('2d'),tip=document.getElementById('tip'),ohlcEl=document.getElementById('ohlc');
let bars=DATA.bars,conds=DATA.conditions,geo=null,hover=-1;
function css(n){return getComputedStyle(document.documentElement).getPropertyValue(n).trim()}
function f(n,d){return (+n).toLocaleString('en-US',{minimumFractionDigits:d,maximumFractionDigits:d})}
function fv(n){return n>=1000?(n/1000).toFixed(1)+'K':''+n}
function layout(){var rc=cv.getBoundingClientRect(),dpr=Math.min(devicePixelRatio||1,2);cv.width=rc.width*dpr|0;cv.height=rc.height*dpr|0;ctx.setTransform(dpr,0,0,dpr,0,0);
var W=rc.width,H=rc.height,pL=8,pR=58,pT=12,pB=22,priceH=(H-pT-pB)*.58|0,volTop=pT+priceH+10,volH=(H-pT-pB)*.12|0,condTop=volTop+volH+16,condH=(H-pB)-condTop,plotW=W-pL-pR;
var lo=1e9,hi=-1e9,vmax=0;for(var b of bars){lo=Math.min(lo,b.l);hi=Math.max(hi,b.h);vmax=Math.max(vmax,b.v)}var pd=(hi-lo)*.06;lo-=pd;hi+=pd;
var n=bars.length,slot=plotW/n,bw=Math.max(2.5,Math.min(11,slot*.62));
geo={W,H,pL,pR,pT,pB,priceH,volTop,volH,condTop,condH,plotW,lo,hi,vmax,n,slot,bw,x:i=>pL+slot*(i+.5),yP:p=>pT+priceH-((p-lo)/(hi-lo))*priceH,yV:v=>volTop+volH-(v/vmax)*volH};}
function ln(x1,y1,x2,y2,c,w,dash){ctx.save();ctx.strokeStyle=c;ctx.lineWidth=w;if(dash)ctx.setLineDash(dash);ctx.beginPath();ctx.moveTo(x1,y1);ctx.lineTo(x2,y2);ctx.stroke();ctx.restore()}
function draw(){if(!geo)return;var g=geo;ctx.clearRect(0,0,g.W,g.H);
var C={dim:css('--dim'),faint:css('--faint'),grid:css('--grid'),axis:css('--axis'),up:css('--up'),down:css('--down'),vwap:css('--vwap'),whigh:css('--whigh'),accent:css('--accent'),good:css('--good'),vd:css('--void')};
ctx.font='10px '+css('--mono');
for(var i=0;i<=5;i++){var p=g.lo+(g.hi-g.lo)*i/5,y=g.yP(p);ln(g.pL,y,g.pL+g.plotW,y,C.grid,1);ctx.fillStyle=C.faint;ctx.textAlign='left';ctx.textBaseline='middle';ctx.fillText(f(p,2),g.pL+g.plotW+6,y)}
ctx.save();ctx.strokeStyle=C.whigh;ctx.lineWidth=1.4;ctx.setLineDash([5,3]);ctx.beginPath();bars.forEach((b,i)=>{var x0=g.x(i)-g.slot/2,x1=g.x(i)+g.slot/2,y=g.yP(b.whigh);i===0?ctx.moveTo(x0,y):ctx.lineTo(x0,y);ctx.lineTo(x1,y)});ctx.stroke();ctx.restore();
ctx.save();ctx.strokeStyle=C.vwap;ctx.lineWidth=1.8;ctx.beginPath();bars.forEach((b,i)=>{var x=g.x(i),y=g.yP(b.vwap);i===0?ctx.moveTo(x,y):ctx.lineTo(x,y)});ctx.stroke();ctx.restore();
bars.forEach((b,i)=>{var x=g.x(i),up=b.c>=b.o,col=up?C.up:C.down;ln(x,g.yP(b.h),x,g.yP(b.l),col,1);var yO=g.yP(b.o),yC=g.yP(b.c),t=Math.min(yO,yC),h=Math.max(1.5,Math.abs(yO-yC));ctx.fillStyle=col;ctx.fillRect(x-g.bw/2,t,g.bw,h)});
bars.forEach((b,i)=>{var x=g.x(i);ctx.fillStyle=b.c>=b.o?C.up:C.down;ctx.globalAlpha=.5;var y=g.yV(b.v);ctx.fillRect(x-g.bw/2,y,g.bw,g.volTop+g.volH-y)});ctx.globalAlpha=1;
ctx.fillStyle=C.faint;ctx.textAlign='left';ctx.textBaseline='top';ctx.fillText('VOLUME',g.pL+2,g.volTop-1);
bars.forEach((b,i)=>{if(!b.fire)return;var x=g.x(i);ctx.save();ctx.globalAlpha=.1;ctx.fillStyle=C.accent;ctx.fillRect(x-g.bw/2-1,g.pT,g.bw+2,g.priceH);ctx.restore();var y=g.yP(b.l)+8;ctx.fillStyle=C.accent;ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(x-5,y+8);ctx.lineTo(x+5,y+8);ctx.closePath();ctx.fill()});
bars.forEach((b,i)=>{if(!b.exit)return;var x=g.x(i),y=g.yP(b.h)-8;ctx.fillStyle=C.down;ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(x-5,y-8);ctx.lineTo(x+5,y-8);ctx.closePath();ctx.fill()});
var rh=g.condH/Math.max(1,conds.length);ctx.textAlign='right';ctx.textBaseline='middle';ctx.font='9px '+css('--mono');
conds.forEach((label,ri)=>{var cy=g.condTop+rh*ri;ctx.fillStyle=C.faint;ctx.fillText(label.length>26?label.slice(0,25)+'…':label,g.pL+g.plotW+54,cy+rh/2);
bars.forEach((b,i)=>{var on=b.cnd[ri],x=g.x(i)-g.bw/2;ctx.fillStyle=on?(b.fire?C.accent:C.good):C.vd;ctx.globalAlpha=on?(b.fire?.95:.5):1;ctx.fillRect(x,cy+1.5,g.bw,rh-3)})});ctx.globalAlpha=1;
ctx.fillStyle=C.faint;ctx.textAlign='left';ctx.textBaseline='bottom';ctx.fillText('ENTRY CONDITIONS (filled = true · gold column = fire)',g.pL+2,g.condTop-3);
ctx.textAlign='center';ctx.textBaseline='top';ctx.font='10px '+css('--mono');var step=Math.ceil(bars.length/9);bars.forEach((b,i)=>{if(i%step===0)ctx.fillText(b.et,g.x(i),g.H-g.pB+5)});
if(hover>=0)ln(g.x(hover),g.pT,g.x(hover),g.condTop+g.condH,C.axis,1,[3,3]);}
function showTip(i,mx){var b=bars[i];ohlcEl.innerHTML=`<span>O <b>${f(b.o,2)}</b></span><span>H <b>${f(b.h,2)}</b></span><span>L <b>${f(b.l,2)}</b></span><span>C <b>${f(b.c,2)}</b></span><span>Vol <b>${fv(b.v)}</b></span>`;
var cr=conds.map((lab,j)=>`<div class="tr"><span>${lab}</span><span class="${b.cnd[j]?'yes':'no'}">${b.cnd[j]?'✓':'✗'}</span></div>`).join('');
tip.innerHTML=`<div class="tt">${b.et} ET${b.inSession?'':' (out of session)'}</div><div class="tr"><span>close</span><span>${f(b.c,2)}</span></div><div class="tr"><span>win hi</span><span>${f(b.whigh,2)}</span></div><div class="tr"><span>vwap</span><span>${f(b.vwap,2)}</span></div><div class="tr"><span>vol×</span><span>${f(b.volx,2)}</span></div><div class="ts"></div>${cr}${b.fire?'<div class="tf">▲ ENTRY</div>':''}${b.exit?'<div class="tf" style="background:var(--down);color:#fff">▼ EXIT</div>':''}`;
var rc=cv.getBoundingClientRect(),tw=tip.offsetWidth,l=Math.min(Math.max(mx-tw/2,6),rc.width-tw-6);tip.style.left=l+'px';tip.style.top='auto';tip.style.bottom='12px';tip.style.opacity='1';}
cv.addEventListener('pointermove',e=>{var rc=cv.getBoundingClientRect(),mx=e.clientX-rc.left;if(!geo)return;var i=Math.max(0,Math.min(bars.length-1,Math.round((mx-geo.pL)/geo.slot-.5)));if(i!==hover){hover=i;draw()}showTip(i,mx)});
cv.addEventListener('pointerleave',()=>{hover=-1;tip.style.opacity='0';ohlcEl.innerHTML='';draw()});
function redraw(){layout();draw()}addEventListener('resize',redraw);
new MutationObserver(redraw).observe(document.documentElement,{attributes:true,attributeFilter:['data-theme']});
redraw();

/* ── inline SVG flow — server-rendered by StrategyDefinition.ToSvg() ── */
document.getElementById('flowsvg').innerHTML = DATA.svgFlow || '<p style="color:var(--faint);font-family:var(--mono)">Flow SVG regenerates on the next replay of this strategy.</p>';

/* ── Mermaid (CDN) ── */
/* Build Mermaid from DATA.conditions at render time (not the baked DATA.mermaid):
   labels sanitized to [A-Za-z0-9 .-] so colons/slashes/parens can't break the v11
   parser — the actual cause of "Syntax error in text". */
function ipMermaid(){
  function s(x){return (''+x).replace(/[^A-Za-z0-9 .-]/g,' ').replace(/\s+/g,' ').trim();}
  var L=['flowchart TD','  setup["Setup '+s(DATA.symbol)+' '+s(DATA.session)+'"]'],prev='setup';
  (DATA.conditions||[]).forEach(function(c,i){L.push('  c'+i+'["'+s(c)+'"]');L.push('  '+prev+' --> c'+i);prev='c'+i;});
  L.push('  gate{"ALL conditions true"}','  '+prev+' --> gate','  wait["keep waiting"]',
    '  llm["Gate 2 LLM voter quorum"]','  risk["Gate 3 Risk Guardian"]','  order["Order '+s(DATA.side)+'"]',
    '  gate -->|no| wait','  gate -->|yes| llm','  llm --> risk','  risk --> order','  exit["Exit rules"]','  order --> exit');
  return L.join('\n');
}
document.getElementById('mmd').textContent=ipMermaid();
var mmdReady=false;
function renderMermaid(){if(mmdReady||typeof mermaid==='undefined')return;mmdReady=true;
var dark=(document.documentElement.getAttribute('data-theme')||(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light'))==='dark';
try{mermaid.initialize({startOnLoad:false,theme:dark?'dark':'neutral',securityLevel:'strict'});mermaid.run({querySelector:'.mermaid'});}catch(e){document.getElementById('mmd').textContent='Mermaid unavailable — see the Inline SVG tab.';}}
document.querySelectorAll('.tab').forEach(t=>t.onclick=function(){document.querySelectorAll('.tab').forEach(x=>x.classList.remove('on'));document.querySelectorAll('.pane').forEach(x=>x.classList.remove('on'));t.classList.add('on');document.getElementById('pane-'+t.dataset.pane).classList.add('on');if(t.dataset.pane==='mmd')renderMermaid();});
</script>
</body>
</html>
""";

    public const string Index = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="light dark">
<title>__SYMBOL__ replays · IdiotProof</title>
<script>(function(){var t;try{t=localStorage.getItem('ip-theme')}catch(e){}document.documentElement.setAttribute('data-theme',t==='light'?'light':'dark');})();</script>
<style>
:root{--bg:#f3f4f7;--panel:#fff;--panel2:#f7f8fa;--edge:#e2e5ea;--ink:#1a1f2b;--dim:#5b6472;--faint:#8b94a3;--accent:#FCD72B;--good:#0f9d78;--dimfire:#0f9d78;
--mono:ui-monospace,"SF Mono",Menlo,Consolas,monospace;--sans:"Segoe UI",system-ui,Arial,sans-serif;--shadow:0 1px 2px rgba(16,22,40,.06),0 8px 30px rgba(16,22,40,.08)}
@media(prefers-color-scheme:dark){:root{--bg:#0a0d13;--panel:#10141d;--panel2:#0c1017;--edge:#1e2530;--ink:#d7dbe4;--dim:#8b94a5;--faint:#5c6577;--accent:#f5c518;--good:#26a69a;--dimfire:#26a69a;--shadow:0 1px 2px rgba(0,0,0,.4),0 10px 40px rgba(0,0,0,.5)}}
:root[data-theme="dark"]{--bg:#0a0d13;--panel:#10141d;--panel2:#0c1017;--edge:#1e2530;--ink:#d7dbe4;--dim:#8b94a5;--faint:#5c6577;--accent:#f5c518;--good:#26a69a;--dimfire:#26a69a}
:root[data-theme="light"]{--bg:#f3f4f7;--panel:#fff;--panel2:#f7f8fa;--edge:#e2e5ea;--ink:#1a1f2b;--dim:#5b6472;--faint:#8b94a3;--accent:#FCD72B;--good:#0f9d78;--dimfire:#0f9d78}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font-family:var(--sans)}
.wrap{max-width:820px;margin:0 auto;padding:36px 20px 64px}
.themebtn{position:fixed;top:14px;right:16px;z-index:30;width:36px;height:36px;border-radius:999px;border:1px solid var(--edge);background:var(--panel);color:var(--ink);cursor:pointer;font-size:16px;line-height:1;box-shadow:var(--shadow)}
.themebtn:hover{border-color:var(--accent)}
h1{font-family:var(--mono);font-size:26px;margin:0 0 2px;letter-spacing:.5px}
.sub{color:var(--dim);font-size:14px;margin-bottom:24px}
.run{display:grid;grid-template-columns:110px 1fr auto;gap:14px;align-items:center;text-decoration:none;color:inherit;background:var(--panel);border:1px solid var(--edge);border-radius:11px;padding:14px 16px;margin-bottom:10px;box-shadow:var(--shadow);transition:border-color .12s}
.run:hover{border-color:var(--accent)}
.run .day{font-family:var(--mono);font-weight:700;font-size:15px}
.run .verdict{font-family:var(--mono);font-size:13px}
.run .fire{color:var(--dimfire);font-weight:700}.run .nofire{color:var(--faint)}
.run .feed{font-family:var(--mono);font-size:11px;color:var(--dim);border:1px solid var(--edge);border-radius:999px;padding:2px 8px;justify-self:end}
.run .gen{grid-column:1/-1;font-family:var(--mono);font-size:11px;color:var(--faint)}
.empty{color:var(--faint)}
footer{margin-top:26px;font-family:var(--mono);font-size:12px;color:var(--faint)}
</style>
</head>
<body>
<button id="themeBtn" class="themebtn" type="button" aria-label="Toggle light or dark theme"></button>
<div class="wrap">
  <h1>__SYMBOL__</h1>
  <div class="sub">__COUNT__ strategy replay(s) · newest first · times US Eastern</div>
  __RUNS__
  <footer>IdiotProof replay archive · /idiotproof/replays/__SYMBOL__/</footer>
</div>
<script>(function(){var r=document.documentElement,b=document.getElementById('themeBtn');
function ico(){b.textContent=r.getAttribute('data-theme')==='light'?'🌙':'☀️';}ico();
b.addEventListener('click',function(){var n=r.getAttribute('data-theme')==='light'?'dark':'light';r.setAttribute('data-theme',n);try{localStorage.setItem('ip-theme',n)}catch(e){}ico();});})();</script>
</body>
</html>
""";

    public const string RootIndex = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="light dark">
<title>Replay archive · IdiotProof</title>
<script>(function(){var t;try{t=localStorage.getItem('ip-theme')}catch(e){}document.documentElement.setAttribute('data-theme',t==='light'?'light':'dark');})();</script>
<style>
:root{--bg:#f3f4f7;--panel:#fff;--panel2:#f7f8fa;--edge:#e2e5ea;--ink:#1a1f2b;--dim:#5b6472;--faint:#8b94a3;--accent:#FCD72B;--good:#0f9d78;
--mono:ui-monospace,"SF Mono",Menlo,Consolas,monospace;--sans:"Segoe UI",system-ui,Arial,sans-serif;--shadow:0 1px 2px rgba(16,22,40,.06),0 8px 30px rgba(16,22,40,.08)}
@media(prefers-color-scheme:dark){:root{--bg:#0a0d13;--panel:#10141d;--panel2:#0c1017;--edge:#1e2530;--ink:#d7dbe4;--dim:#8b94a5;--faint:#5c6577;--accent:#f5c518;--good:#26a69a;--shadow:0 1px 2px rgba(0,0,0,.4),0 10px 40px rgba(0,0,0,.5)}}
:root[data-theme="dark"]{--bg:#0a0d13;--panel:#10141d;--panel2:#0c1017;--edge:#1e2530;--ink:#d7dbe4;--dim:#8b94a5;--faint:#5c6577;--accent:#f5c518;--good:#26a69a}
:root[data-theme="light"]{--bg:#f3f4f7;--panel:#fff;--panel2:#f7f8fa;--edge:#e2e5ea;--ink:#1a1f2b;--dim:#5b6472;--faint:#8b94a3;--accent:#FCD72B;--good:#0f9d78}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font-family:var(--sans)}
.wrap{max-width:900px;margin:0 auto;padding:36px 20px 64px}
.themebtn{position:fixed;top:14px;right:16px;z-index:30;width:36px;height:36px;border-radius:999px;border:1px solid var(--edge);background:var(--panel);color:var(--ink);cursor:pointer;font-size:16px;line-height:1;box-shadow:var(--shadow)}
.themebtn:hover{border-color:var(--accent)}
h1{font-family:var(--mono);font-size:26px;margin:0 0 2px;letter-spacing:.5px}
.sub{color:var(--dim);font-size:14px;margin-bottom:24px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px}
.tk{display:flex;flex-direction:column;gap:4px;text-decoration:none;color:inherit;background:var(--panel);border:1px solid var(--edge);border-radius:12px;padding:16px 18px;box-shadow:var(--shadow);transition:border-color .12s,transform .12s}
.tk:hover{border-color:var(--accent);transform:translateY(-1px)}
.tk .sym{font-family:var(--mono);font-weight:700;font-size:20px;letter-spacing:.5px}
.tk .cnt{font-family:var(--mono);font-size:11.5px;color:var(--dim)}
.tk .vd{font-family:var(--mono);font-size:13px;margin-top:2px}
.tk .fire{color:var(--good);font-weight:700}.tk .nofire{color:var(--faint)}
.tk .gen{font-family:var(--mono);font-size:10.5px;color:var(--faint)}
.empty{color:var(--faint)}
footer{margin-top:26px;font-family:var(--mono);font-size:12px;color:var(--faint)}
</style>
</head>
<body>
<button id="themeBtn" class="themebtn" type="button" aria-label="Toggle light or dark theme"></button>
<div class="wrap">
  <h1>Replay archive</h1>
  <div class="sub">__COUNT__ ticker(s) replayed · newest activity first · click a ticker for its full history (newest run first)</div>
  <div class="grid">__TICKERS__</div>
  <footer>IdiotProof · every strategy replay ever run · /idiotproof/replays/</footer>
</div>
<script>(function(){var r=document.documentElement,b=document.getElementById('themeBtn');
function ico(){b.textContent=r.getAttribute('data-theme')==='light'?'🌙':'☀️';}ico();
b.addEventListener('click',function(){var n=r.getAttribute('data-theme')==='light'?'dark':'light';r.setAttribute('data-theme',n);try{localStorage.setItem('ip-theme',n)}catch(e){}ico();});})();</script>
</body>
</html>
""";
}
