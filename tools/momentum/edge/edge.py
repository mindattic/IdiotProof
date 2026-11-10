"""Momentum. edge study (IP-T500). Protocol: docs/research/momentum-edge-2026-11/protocol.md.

usage:
  python tools/momentum/edge/edge.py fit       # H1/H2/H3 on the fit split, writes runs/fit-*.md|json and fit-choices.json
  python tools/momentum/edge/edge.py validate  # scores fit-choices.json on the validation split, once per choices file
Inputs: --calls (build-artifact.ps1 -JsonOut), --movers, --bars (defaults under D:/IdiotProofData/momentum).
"""
import argparse, datetime as dt, hashlib, json, math, os, sys
from collections import defaultdict
from zoneinfo import ZoneInfo

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
STUDY = os.path.join(ROOT, "docs", "research", "momentum-edge-2026-11")
RUNS = os.path.join(STUDY, "runs")
ET = ZoneInfo("America/New_York")
EVAL = {"target-hit", "triggered-no-target", "stopped", "not-triggered"}
TRIG = {"target-hit", "triggered-no-target", "stopped"}

P = json.load(open(os.path.join(STUDY, "params.json"), encoding="utf-8"))
SPL = P["splits"]


def split_of(d):
    if d <= SPL["fitEnd"]:
        return "fit"
    if SPL["validationStart"] <= d <= SPL["validationEnd"]:
        return "validation"
    if SPL["holdoutStart"] <= d <= SPL["holdoutEnd"]:
        return "holdout"
    return None


# ── minute bars (FileBackedBarStore format) ─────────────────────────────────
REC = np.dtype({"names": ["ticks", "o", "h", "l", "c", "v", "n", "vw"],
                "formats": ["<i8", "<i8", "<i8", "<i8", "<i8", "<i8", "<i4", "<i8"],
                "offsets": [0, 8, 16, 24, 32, 40, 48, 52], "itemsize": 64})
EPOCH_TICKS = 621355968000000000  # 1970-01-01 in .NET ticks
_bar_cache = {}


def read_month(path):
    if path in _bar_cache:
        return _bar_cache[path]
    days = {}
    if os.path.exists(path):
        buf = open(path, "rb").read()
        pos = 16
        while pos + 12 <= len(buf):
            daynum = int.from_bytes(buf[pos:pos + 4], "little", signed=True)
            count = int.from_bytes(buf[pos + 4:pos + 8], "little", signed=True)
            end = pos + 12 + count * 64
            if end > len(buf):
                break
            days[dt.date.fromordinal(daynum + 1)] = np.frombuffer(buf[pos + 12:end], dtype=REC)
            pos = end
    _bar_cache[path] = days
    return days


def day_bars(bars_root, sym, d):
    """ET minute-of-day, o, h, l, c, v, session VWAP from 04:00; None when the day has no bars."""
    date = dt.date.fromisoformat(d)
    rec = read_month(os.path.join(bars_root, sym.upper(), date.strftime("%Y-%m") + ".bin")).get(date)
    if rec is None or len(rec) == 0:
        return None
    rec = np.sort(rec, order="ticks")
    off = dt.datetime(date.year, date.month, date.day, 12, tzinfo=ET).utcoffset().total_seconds() / 60
    unix_min = (rec["ticks"] - EPOCH_TICKS) // 600000000
    mod = (unix_min + off).astype(np.int64) % 1440
    o, h, l, c = (rec[k] / 1e6 for k in ("o", "h", "l", "c"))
    v = rec["v"] / 100.0
    vw = rec["vw"] / 1e6
    px = np.where(vw > 0, vw, (h + l + c) / 3)
    cum_v = np.cumsum(v)
    vwap = np.where(cum_v > 0, np.cumsum(px * v) / np.maximum(cum_v, 1e-9), c)
    keep = (mod >= 240) & (mod < 1200)
    return {"m": mod[keep], "o": o[keep], "h": h[keep], "l": l[keep], "c": c[keep], "v": v[keep], "vwap": vwap[keep]}


def hm(s):
    a, b = s.split(":")
    return int(a) * 60 + int(b)


# ── movers ───────────────────────────────────────────────────────────────────
def load_movers(root):
    out = {}
    for name in sorted(os.listdir(root)):
        if not name.endswith(".json"):
            continue
        j = json.load(open(os.path.join(root, name), encoding="utf-8"))
        rows = {r["S"]: r for r in j["Rows"]}
        out[j["Date"]] = {"prev": j.get("Prev"), "rows": rows}
    return out


# ── stats ────────────────────────────────────────────────────────────────────
RNG = np.random.default_rng(P["bootstrap"]["seed"])


def boot_mean_by_day(day_vals, draws=None):
    """day_vals: list of lists of per-trade values, one list per day. Returns (mean, lo, hi, p_le_0)."""
    draws = draws or P["bootstrap"]["draws"]
    sums = np.array([sum(x) for x in day_vals], float)
    cnts = np.array([len(x) for x in day_vals], float)
    if cnts.sum() == 0:
        return None
    mean = sums.sum() / cnts.sum()
    idx = RNG.integers(0, len(sums), size=(draws, len(sums)))
    bm = sums[idx].sum(1) / np.maximum(cnts[idx].sum(1), 1)
    a = (1 - P["bootstrap"]["ci"]) / 2
    return float(mean), float(np.quantile(bm, a)), float(np.quantile(bm, 1 - a)), float((bm <= 0).mean())


def wilson(k, n, z=1.6449):
    if n == 0:
        return (math.nan, math.nan)
    p = k / n
    den = 1 + z * z / n
    mid = (p + z * z / (2 * n)) / den
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / den
    return mid - half, mid + half


def holm(pvals):
    order = sorted(range(len(pvals)), key=lambda i: pvals[i])
    adj, run = [0.0] * len(pvals), 0.0
    for r, i in enumerate(order):
        run = max(run, min(1.0, (len(pvals) - r) * pvals[i]))
        adj[i] = run
    return adj


def wz(x):
    return None if x is None else min(float(x), P["winsorPct"])


def fmt(x, d=2, sign=True):
    if x is None or (isinstance(x, float) and not math.isfinite(x)):
        return "-"
    return (f"{x:+.{d}f}" if sign else f"{x:.{d}f}")


# ── H1: his evening picks vs a D-1 screen ────────────────────────────────────
def h1(calls, movers, split):
    sc = P["screen"]
    # Keyed by trade date (not by video): two evening videos for one date share the screen baseline,
    # and as two units they made the day-clustered bootstrap count one market day twice.
    by_video = defaultdict(list)
    for c in calls:
        if c["k"] == "wl" and c.get("sym") and c.get("rk") and split_of(c["d"]) == split:
            by_video[("all", c["d"])].append(c["sym"])
    days, ranks, outside = [], [], 0
    for (vid, d), syms in sorted(by_video.items(), key=lambda kv: kv[0][1]):
        syms = list(dict.fromkeys(syms))
        md = movers.get(d)
        if not md or not md["prev"] or md["prev"] not in movers:
            continue
        prev_rows = movers[md["prev"]]["rows"]
        elig = [r for r in prev_rows.values() if r.get("C") is not None and sc["minClose"] <= r["C"] <= sc["maxClose"] and (r.get("V") or 0) >= sc["minVolume"]]
        s1 = [r["S"] for r in sorted(elig, key=lambda r: -(r.get("ClosePct") or -1e9))]
        s2 = [r["S"] for r in sorted(elig, key=lambda r: -(r.get("HighPct") or -1e9))]
        out = lambda s: md["rows"].get(s)
        his = [out(s) for s in syms if out(s)]
        a = [out(s) for s in s1[:sc["topN"]] if out(s)]
        b = [out(s) for s in s2[:sc["topN"]] if out(s)]
        if not his or not a or not b:
            continue
        f = lambda rows, k: float(np.mean([wz(r[k]) for r in rows if r.get(k) is not None]))
        days.append({"d": d, "hisHi": f(his, "HighPct"), "hisCl": f(his, "ClosePct"), "s1Hi": f(a, "HighPct"), "s1Cl": f(a, "ClosePct"),
                     "s2Hi": f(b, "HighPct"), "s2Cl": f(b, "ClosePct")})
        pos = {s: i + 1 for i, s in enumerate(s1)}
        for s in syms:
            if s in pos:
                ranks.append(pos[s])
            else:
                outside += 1
                ranks.append(None)
    res = {"days": len(days), "picks": len(ranks)}
    for k in ("hisHi", "hisCl", "s1Hi", "s1Cl", "s2Hi", "s2Cl"):
        res[k] = float(np.mean([x[k] for x in days])) if days else None
    for scr in ("s1", "s2"):
        for m in ("Hi", "Cl"):
            res[f"diff_{scr}{m}"] = boot_mean_by_day([[x["his" + m] - x[scr + m]] for x in days]) if days else None
    for n in sc["overlapN"]:
        res[f"inTop{n}"] = sum(1 for r in ranks if r is not None and r <= n) / max(1, len(ranks))
    res["notInScreenFilter"] = outside / max(1, len(ranks))
    res["medianRankInS1"] = float(np.median([r for r in ranks if r is not None])) if any(r is not None for r in ranks) else None
    return res


# ── H2: what worked inside his calls ────────────────────────────────────────
def band(x, edges, unit=""):
    if x is None:
        return "unknown"
    for a, b in zip(edges[:-1], edges[1:]):
        if a <= x < b:
            return f"[{a}{unit}, {b}{unit})"
    return f">= {edges[-1]}{unit}"


def features(c):
    h = P["h2"]
    gap = (c["op"] / c["pc"] - 1) * 100 if c.get("op") and c.get("pc") else None
    trd = (c["tr"] / c["pc"] - 1) * 100 if c.get("tr") and c.get("pc") else None
    f = [("kind", "evening watchlist" if c["k"] == "wl" else "morning update"), ("rank", f"#{c.get('rk')}"),
         ("price", band(c.get("pc"), h["priceBands"], "")), ("prev-day gainer rank", band(c.get("pr"), h["prevRankBands"])),
         ("gap at open %", band(gap, h["gapBands"])), ("trigger over prior close %", band(trd, h["triggerDistBands"]))]
    if c["v"] in TRIG:
        f.append(("trigger time", "premarket" if c.get("trT") and c["trT"] < "09:30" else "regular hours"))
    if c.get("op") and c.get("tr"):
        f.append(("opened over trigger", "yes" if c["op"] >= c["tr"] else "no"))
    for t in c.get("tags") or []:
        f.append(("setup tag", t))
    return f


def level_stats(rows):
    ev = [c for c in rows if c["v"] in EVAL]
    tr = [c for c in ev if c["v"] in TRIG]
    pn = [c["pnl"] for c in ev if c.get("pnl") is not None]
    tf = sum(1 for c in tr if c["v"] == "target-hit")
    return {"eval": len(ev), "trig": len(tr), "tf": tf, "stopped": sum(1 for c in tr if c["v"] == "stopped"),
            "tfRate": tf / len(tr) if tr else None, "trigRate": len(tr) / len(ev) if ev else None,
            "pnlMean": float(np.mean(pn)) if pn else None, "pnlN": len(pn)}


def h2(calls, split):
    rows = [c for c in calls if split_of(c["d"]) == split and c.get("sym") and c["v"] in EVAL]
    base = level_stats(rows)
    groups = defaultdict(list)
    for c in rows:
        for fam, lev in features(c):
            groups[(fam, lev)].append(c)
    table = []
    for (fam, lev), rs in groups.items():
        s = level_stats(rs)
        s.update({"family": fam, "level": lev, "lift": (s["tfRate"] / base["tfRate"]) if s["tfRate"] is not None and base["tfRate"] else None})
        table.append(s)
    table.sort(key=lambda s: (s["family"], s["level"]))
    return base, table


# ── H3: entry/exit rules on his pick-days ────────────────────────────────────
def pick_days(calls, split):
    seen = {}
    for c in calls:
        if split_of(c["d"]) != split or not c.get("sym") or c["v"] == "unverifiable":
            continue
        key = (c["d"], c["sym"])
        if key not in seen or (seen[key]["k"] == "am" and c["k"] == "wl"):
            seen[key] = c
    return list(seen.values())


def entry_index(b, kind, trigger, pc):
    m = b["m"]
    rth = np.nonzero(m >= 570)[0]
    if len(rth) == 0:
        return None
    i0 = rth[0]
    sig = None
    if kind == "open":
        return i0
    if kind == "his-trigger":
        if not trigger:
            return None
        hit = np.nonzero(b["h"] >= trigger)[0]
        sig = hit[0] if len(hit) else None
    elif kind == "orb5-high":
        orb = np.nonzero((m >= 570) & (m < 570 + P["h3"]["orbMinutes"]))[0]
        if len(orb) == 0:
            return None
        lvl = b["h"][orb].max()
        hit = np.nonzero((m >= 575) & (b["h"] > lvl))[0]
        sig = hit[0] if len(hit) else None
    elif kind == "pm-high-break":
        pm = np.nonzero(m < 570)[0]
        if len(pm) == 0:
            return None
        lvl = b["h"][pm].max()
        hit = np.nonzero((m >= 570) & (b["h"] > lvl))[0]
        sig = hit[0] if len(hit) else None
    elif kind == "vwap-reclaim":
        a, z = (hm(x) for x in P["h3"]["vwapReclaimWindow"])
        win = np.nonzero((m >= a) & (m <= z))[0]
        below = False
        for i in win:
            if b["c"][i] < b["vwap"][i]:
                below = True
            elif below and b["c"][i] > b["vwap"][i]:
                sig = i
                break
    if sig is None or sig + 1 >= len(m):
        return None
    return sig + 1


def simulate(b, ei, tp, stop, tex):
    """Gross % return of one trade entered at the open of bar ei."""
    m = b["m"]
    tmin = hm(tex)
    if m[ei] >= tmin:
        return None
    entry = b["o"][ei]
    if entry <= 0:
        return None
    n = len(m)
    t_idx = np.nonzero(m[ei:] >= tmin)[0]
    end = ei + (t_idx[0] if len(t_idx) else n - ei)  # exit at the open of this bar (or the last close)
    seg = slice(ei, end)
    exit_px, exit_i = None, end
    cands = []
    if stop != "vwap":
        sl = entry * (1 + stop / 100)
        hit = np.nonzero(b["l"][seg] <= sl)[0]
        if len(hit):
            i = ei + hit[0]
            cands.append((i, 0, min(sl, b["o"][i])))
    else:
        hit = np.nonzero(b["c"][seg] < b["vwap"][seg])[0]
        if len(hit):
            i = ei + hit[0]
            if i + 1 < n:
                cands.append((i + 1, 0, b["o"][i + 1]))
    if tp is not None:
        lv = entry * (1 + tp / 100)
        hit = np.nonzero(b["h"][seg] >= lv)[0]
        if len(hit):
            i = ei + hit[0]
            cands.append((i, 1, max(lv, b["o"][i]) if b["o"][i] > lv and i > ei else lv))
    if cands:
        cands.sort(key=lambda x: (x[0], x[1]))  # same bar: stop first (conservative)
        exit_i, _, exit_px = cands[0]
        if exit_i > end:
            exit_px = None
    if exit_px is None:
        exit_px = b["o"][end] if end < n else b["c"][n - 1]
    return (exit_px / entry - 1) * 100


def rules():
    h = P["h3"]
    for e in h["entries"]:
        for tp in h["takeProfitPct"]:
            for s in h["stop"]:
                for t in h["timeExit"]:
                    yield (e, tp, s, t)


def rule_name(r):
    e, tp, s, t = r
    return f"{e} tp={'none' if tp is None else f'+{tp}%'} stop={'vwap' if s == 'vwap' else f'{s}%'} out={t}"


def h3(calls, split, bars_root, rule_list=None, filt=None):
    picks = pick_days(calls, split)
    if filt:
        picks = [c for c in picks if filt(c)]
    loaded = []
    for c in picks:
        b = day_bars(bars_root, c["sym"], c["d"])
        if b is not None and len(b["m"]) > 10:
            loaded.append((c, b))
    out = {}
    for r in (rule_list or list(rules())):
        e, tp, s, t = r
        per_day = defaultdict(list)
        for c, b in loaded:
            ei = entry_index(b, e, c.get("tr"), c.get("pc"))
            if ei is None:
                continue
            g = simulate(b, ei, tp, s, t)
            if g is None or not math.isfinite(g):
                continue
            per_day[c["d"]].append(g)
        vals = [v for vs in per_day.values() for v in vs]
        res = {"rule": rule_name(r), "key": list(r), "trades": len(vals), "days": len(per_day), "picks": len(loaded)}
        if vals:
            res["gross"] = float(np.mean(vals))
            res["win2"] = float(np.mean([v - P["h3"]["costRoundTripPct"] > 0 for v in vals]))
            for cost in [P["h3"]["costRoundTripPct"]] + P["h3"]["costSensitivityPct"]:
                res[f"net{cost}"] = float(np.mean(vals)) - cost
            dv = [[v - P["h3"]["costRoundTripPct"] for v in vs] for vs in per_day.values()]
            res["boot2"] = boot_mean_by_day(dv)
        out[rule_name(r)] = res
    return out, len(picks), len(loaded)


# ── stages ───────────────────────────────────────────────────────────────────
def known_before_open(c):
    """Amendment 01: a call counts only when its video was public before that session's 09:30 ET open."""
    return bool(c.get("pub")) and c["pub"] < f"{c['d']} 09:30"


def load(a):
    calls = json.load(open(a.calls, encoding="utf-8"))["calls"]
    kept = [c for c in calls if known_before_open(c)]
    print(f"calls {len(calls)}, known before the open {len(kept)}, dropped {len(calls) - len(kept)}", file=sys.stderr)
    return kept, load_movers(a.movers)


def write(name, md_lines, js):
    os.makedirs(RUNS, exist_ok=True)
    stamp = dt.datetime.now(ET).strftime("%Y%m%d-%H%M")
    with open(os.path.join(RUNS, f"{name}-{stamp}.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(md_lines) + "\n")
    with open(os.path.join(RUNS, f"{name}-{stamp}.json"), "w", encoding="utf-8") as f:
        json.dump(js, f, indent=1, default=str)
    return stamp


def h1_lines(r, title):
    L = [f"## {title}", "", f"{r['days']} evening watchlists, {r['picks']} picks with outcomes.", "",
         "| set | mean high over prior close | mean close over prior close |", "|---|---|---|",
         f"| his 3 picks | {fmt(r['hisHi'], 1)}% | {fmt(r['hisCl'], 1)}% |",
         f"| S1: top 3 by D-1 close gain | {fmt(r['s1Hi'], 1)}% | {fmt(r['s1Cl'], 1)}% |",
         f"| S2: top 3 by D-1 high gain | {fmt(r['s2Hi'], 1)}% | {fmt(r['s2Cl'], 1)}% |", "",
         "| his minus | high: mean [90% CI] | close: mean [90% CI] |", "|---|---|---|"]
    for s in ("s1", "s2"):
        hi, cl = r[f"diff_{s}Hi"], r[f"diff_{s}Cl"]
        L.append(f"| {s.upper()} | {fmt(hi[0], 1)} [{fmt(hi[1], 1)}, {fmt(hi[2], 1)}] | {fmt(cl[0], 1)} [{fmt(cl[1], 1)}, {fmt(cl[2], 1)}] |")
    L += ["", f"His picks inside S1's top 3 / 10 / 20: {r['inTop3']:.0%} / {r['inTop10']:.0%} / {r['inTop20']:.0%}; outside the screen's price/volume filter: {r['notInScreenFilter']:.0%}; median rank in S1: {fmt(r['medianRankInS1'], 0, False)}.", ""]
    return L


def stage_fit(a):
    calls, movers = load(a)
    r1 = h1(calls, movers, "fit")
    base, table = h2(calls, "fit")
    hp = P["h2"]
    carry = sorted([s for s in table if s["trig"] >= hp["minTriggered"] and s["lift"] and s["lift"] >= hp["minLift"]], key=lambda s: -s["lift"])[:hp["maxCarry"]]
    ignore = [s for s in table if s["trig"] >= hp["minTriggered"] and s["lift"] is not None and s["lift"] <= hp["ignoreLift"]]
    r3, npk, nld = h3(calls, "fit", a.bars)
    ranked = sorted([x for x in r3.values() if x["trades"] >= P["h3"]["minFitTrades"]], key=lambda x: -x[f"net{P['h3']['costRoundTripPct']}"])
    top = ranked[:P["h3"]["carry"]]
    best_filter = carry[0] if carry else None
    choices = {"h2": [{"family": s["family"], "level": s["level"], "fitLift": s["lift"], "fitTfRate": s["tfRate"]} for s in carry],
               "h3": [x["key"] for x in top],
               "h4": {"filter": {"family": best_filter["family"], "level": best_filter["level"]} if best_filter else None, "rule": top[0]["key"] if top else None},
               "madeEt": dt.datetime.now(ET).isoformat()}
    with open(os.path.join(STUDY, "fit-choices.json"), "w", encoding="utf-8") as f:
        json.dump(choices, f, indent=1)

    L = ["# Momentum. edge study: fit split (IP-T500)", "", f"Fit = trade dates through {SPL['fitEnd']}. Generated {dt.datetime.now(ET):%Y-%m-%d %H:%M} ET.", ""]
    L += h1_lines(r1, "H1 selection: his evening picks vs a D-1 screen")
    L += ["## H2 what worked inside his calls", "",
          f"Base (fit): {base['eval']} evaluated, {base['trig']} triggered ({fmt(100 * base['trigRate'], 0, False)}%), target first {fmt(100 * base['tfRate'], 0, False)}% of triggered, mean plan P&L {fmt(base['pnlMean'])}% over {base['pnlN']} entered.", "",
          "| family | level | evaluated | triggered | target first | lift | stopped | mean plan P&L |", "|---|---|---|---|---|---|---|---|"]
    for s in table:
        if s["eval"] >= 15:
            L.append(f"| {s['family']} | {s['level']} | {s['eval']} | {s['trig']} | {fmt(100 * s['tfRate'], 0, False) + '%' if s['tfRate'] is not None else '-'} | {fmt(s['lift'], 2, False)} | {s['stopped']} | {fmt(s['pnlMean'])}% (n {s['pnlN']}) |")
    L += ["", "Carried to validation: " + ("; ".join(f"{s['family']} = {s['level']} (lift {s['lift']:.2f})" for s in carry) or "none"),
          "Ignore candidates (lift <= 0.67): " + ("; ".join(f"{s['family']} = {s['level']} (lift {s['lift']:.2f})" for s in ignore) or "none"), ""]
    L += ["## H3 entry/exit rules on his pick-days", "",
          f"{npk} pick-days in fit, {nld} with minute bars. 240 rules. Net = gross minus the round trip.", "",
          "| rule | trades | gross | net 1.0% | net 2.0% [90% CI] | net 3.4% | win at 2% |", "|---|---|---|---|---|---|---|"]
    for x in ranked[:25]:
        bt = x["boot2"]
        L.append(f"| {x['rule']} | {x['trades']} | {fmt(x['gross'])}% | {fmt(x['net1.0'])}% | {fmt(x['net2.0'])}% [{fmt(bt[1])}, {fmt(bt[2])}] | {fmt(x['net3.4'])}% | {x['win2']:.0%} |")
    L += ["", "Gross by entry (best exit per entry):", ""]
    for e in P["h3"]["entries"]:
        best = max((x for x in r3.values() if x["key"][0] == e and x["trades"] >= 20), key=lambda x: x["gross"], default=None)
        if best:
            L.append(f"- {e}: best gross {fmt(best['gross'])}% over {best['trades']} trades ({best['rule']})")
    L += ["", f"Rules with positive net at 2.0%: {sum(1 for x in ranked if x['net2.0'] > 0)} of {len(ranked)} with >= {P['h3']['minFitTrades']} trades.",
          "Carried to validation (top 5 by net at 2.0%): " + "; ".join(x["rule"] for x in top), ""]
    stamp = write("fit", L, {"h1": r1, "h2": {"base": base, "table": table, "carry": carry}, "h3": r3, "choices": choices})
    with open(os.path.join(RUNS, "ledger.jsonl"), "a", encoding="utf-8") as f:
        for x in r3.values():
            f.write(json.dumps({"stage": "fit", "stamp": stamp, "rule": x["rule"], "trades": x["trades"], "net2": x.get("net2.0")}) + "\n")
    print("\n".join(L))


def stage_validate(a):
    path = os.path.join(STUDY, "fit-choices.json")
    raw = open(path, "rb").read()
    h = hashlib.sha256(raw).hexdigest()[:16]
    mark = os.path.join(RUNS, f"validated-{h}.json")
    if os.path.exists(mark) and not a.force:
        sys.exit(f"fit-choices {h} was already validated ({mark}); a second look needs an amendment")
    ch = json.loads(raw)
    calls, movers = load(a)
    r1 = h1(calls, movers, "validation")
    base, table = h2(calls, "validation")
    look = {(s["family"], s["level"]): s for s in table}
    L = ["# Momentum. edge study: validation (IP-T500)", "", f"Choices {h} from fit; validation = {SPL['validationStart']}..{SPL['validationEnd']}.", ""]
    L += h1_lines(r1, "H1 on validation")
    L += ["## H2 carried levels", "", f"Validation base: target first {fmt(100 * (base['tfRate'] or 0), 0, False)}% of {base['trig']} triggered.", "",
          "| level | triggered | target first | Wilson 90% | one-sided p | Holm | pass |", "|---|---|---|---|---|---|---|"]
    from scipy.stats import binomtest
    ps, rows2 = [], []
    for c in ch["h2"]:
        s = look.get((c["family"], c["level"]))
        if not s or not s["trig"]:
            rows2.append((c, None, 1.0)); ps.append(1.0); continue
        p = binomtest(s["tf"], s["trig"], base["tfRate"], alternative="greater").pvalue
        rows2.append((c, s, p)); ps.append(p)
    adj = holm(ps) if ps else []
    for (c, s, p), ap in zip(rows2, adj):
        lo, hi = wilson(s["tf"], s["trig"]) if s else (math.nan, math.nan)
        L.append(f"| {c['family']} = {c['level']} | {s['trig'] if s else 0} | {fmt(100 * s['tfRate'], 0, False) + '%' if s and s['tfRate'] is not None else '-'} | [{fmt(100 * lo, 0, False)}, {fmt(100 * hi, 0, False)}]% | {p:.3f} | {ap:.3f} | {'yes' if s and ap < 0.10 and lo > base['tfRate'] else 'no'} |")
    rl = [tuple(k) for k in ch["h3"]]
    r3, npk, nld = h3(calls, "validation", a.bars, rule_list=rl)
    ps = [r3[rule_name(r)]["boot2"][3] if r3[rule_name(r)].get("boot2") else 1.0 for r in rl]
    adj = holm(ps) if ps else []
    L += ["", "## H3 carried rules", "", f"{npk} pick-days, {nld} with bars.", "",
          "| rule | trades | gross | net 2.0% [90% CI] | net 3.4% | p (boot) | Holm | pass |", "|---|---|---|---|---|---|---|---|"]
    passed = []
    for r, ap in zip(rl, adj):
        x = r3[rule_name(r)]
        bt = x.get("boot2") or (None, None, None, 1.0)
        ok = bt[1] is not None and bt[1] > 0 and ap < 0.10
        if ok:
            passed.append(x["rule"])
        L.append(f"| {x['rule']} | {x['trades']} | {fmt(x.get('gross'))}% | {fmt(x.get('net2.0'))}% [{fmt(bt[1])}, {fmt(bt[2])}] | {fmt(x.get('net3.4'))}% | {bt[3]:.3f} | {ap:.3f} | {'yes' if ok else 'no'} |")
    h4 = ch.get("h4") or {}
    if h4.get("filter") and h4.get("rule"):
        fam, lev = h4["filter"]["family"], h4["filter"]["level"]
        r4, _, _ = h3(calls, "validation", a.bars, rule_list=[tuple(h4["rule"])], filt=lambda c: (fam, lev) in features(c))
        x = next(iter(r4.values()))
        bt = x.get("boot2") or (None, None, None, 1.0)
        L += ["", "## H4 best filter x best rule", "", f"{fam} = {lev}, {x['rule']}: {x['trades']} trades, net 2.0% {fmt(x.get('net2.0'))}% [{fmt(bt[1])}, {fmt(bt[2])}], p {bt[3]:.3f}: {'pass' if bt[1] is not None and bt[1] > 0 else 'fail'}"]
    L += ["", f"Survivors for the holdout: {', '.join(passed) or 'none'}"]
    stamp = write("validation", L, {"choicesHash": h, "h1": r1, "h2": {"base": base}, "h3": r3})
    json.dump({"hash": h, "stamp": stamp, "passed": passed}, open(mark, "w"))
    print("\n".join(L))


def stage_holdout_h1(a):
    """Amendment 03: H1 on the holdout, one run ever."""
    unlock = os.path.join(STUDY, "holdout.unlock")
    mark = os.path.join(RUNS, "holdout-run.json")
    if not os.path.exists(unlock):
        sys.exit(f"missing {unlock}")
    if os.path.exists(mark):
        sys.exit(f"the holdout was already run ({mark})")
    calls, movers = load(a)
    calls = [c for c in calls if c["d"] <= "2026-11-09"]
    r1 = h1(calls, movers, "holdout")
    lo1, lo2 = r1["diff_s1Hi"][1], r1["diff_s2Hi"][1]
    ok = lo1 > 0 and lo2 > 0

    # reported, not tested: share of his picks and of all liquid names reaching +50%
    def rate50(split):
        his, base, seen = [], [], set()
        for c in calls:
            if c["k"] == "wl" and c.get("sym") and c.get("rk") and split_of(c["d"]) == split:
                if (c["d"], c["sym"]) in seen:  # one row per scenario: count each pick once
                    continue
                seen.add((c["d"], c["sym"]))
                r = (movers.get(c["d"]) or {}).get("rows", {}).get(c["sym"])
                if r and r.get("HighPct") is not None:
                    his.append(r["HighPct"] >= 50)
        for d, md in movers.items():
            if split_of(d) == split and (split != "holdout" or d <= "2026-11-09"):
                base += [r["HighPct"] >= 50 for r in md["rows"].values() if r.get("HighPct") is not None]
        return (float(np.mean(his)) if his else None, len(his), float(np.mean(base)) if base else None)

    L = ["# Momentum. edge study: H1 holdout (IP-T500 amendment 03)", "", f"Holdout trade dates {SPL['holdoutStart']}..2026-11-09. One run.", ""]
    L += h1_lines(r1, "H1 on the holdout")
    L += [f"Pass rule: both high-difference lower bounds > 0: S1 {fmt(lo1, 1)}, S2 {fmt(lo2, 1)} -> **{'PASS' if ok else 'FAIL'}**.", ""]
    for s in ("fit", "validation", "holdout"):
        hr, n, br = rate50(s)
        L.append(f"- {s}: his picks high >= +50% {fmt(100 * hr if hr is not None else None, 1, False)}% of {n}; liquid-name base {fmt(100 * br if br is not None else None, 2, False)}%" + (f"; lift {hr / br:.1f}x" if hr and br else ""))
    stamp = write("holdout-h1", L, {"h1": r1, "pass": ok})
    json.dump({"stamp": stamp, "test": "H1", "pass": ok, "lo": [lo1, lo2]}, open(mark, "w"))
    print("\n".join(L))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("stage", choices=["fit", "validate", "holdout-h1"])
    ap.add_argument("--calls", default="D:/IdiotProofData/momentum/edge-calls.json")
    ap.add_argument("--movers", default="D:/IdiotProofData/momentum/movers")
    ap.add_argument("--bars", default="D:/IdiotProofData/momentum/bars")
    ap.add_argument("--force", action="store_true")
    a = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    {"fit": stage_fit, "validate": stage_validate, "holdout-h1": stage_holdout_h1}[a.stage](a)


if __name__ == "__main__":
    main()
