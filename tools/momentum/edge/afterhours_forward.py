"""After-hours sign forward test (IP-T501, momentum-edge amendment 06).

usage:
  idiotproof gapper radar-backtest --from 2026-11-11 --to <last session>      (writes the JSON)
  python tools/momentum/edge/afterhours_forward.py <backtest.json>           interim: counts only
  python tools/momentum/edge/afterhours_forward.py <backtest.json> --final   the one evaluation

The interim mode never prints an outcome. --final refuses to run before the window has ended (amendment 06).
"""
import datetime as dt
import json, os, sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import edge  # noqa: E402

WINDOW_START = "2026-11-11"
WINDOW_END = "2027-01-29"
EXTENDED_END = "2027-03-31"
MIN_POSITIVE = 30
MIN_PCT, MIN_DOLLARS = 10.0, 250_000.0
RUN_PCT = 50.0
DRAWS, SEED, CI = 5000, 501, 0.90


def ah(row, min_pct=MIN_PCT):
    p, d = row.get("AfterHoursPct"), row.get("AfterHoursDollarVolume")
    if p is None or d is None:
        return None
    return p >= min_pct and d >= MIN_DOLLARS


def sessions_in_window(days, end):
    return [d for d in days if WINDOW_START <= d["Session"] <= end]


def rr_by_session(days, level, min_pct, only=lambda r: True):
    """Per session: (runs AH+, n AH+, runs AH-, n AH-)."""
    out = []
    for d in days:
        hp = d["HighPct"]
        c = [0, 0, 0, 0]
        for r in d["Rows"]:
            if not only(r):
                continue
            s = ah(r, min_pct)
            if s is None:
                continue
            run = (hp.get(r["Symbol"]) or 0) >= level
            i = 0 if s else 2
            c[i] += run
            c[i + 1] += 1
        out.append(c)
    return np.array(out, dtype=float).reshape(-1, 4)


def rr(m):
    a, n1, b, n0 = m.sum(0)
    if n1 == 0 or n0 == 0 or b == 0:
        return None
    return (a / n1) / (b / n0)


def boot(m):
    rng = np.random.default_rng(SEED)
    vals = []
    for _ in range(DRAWS):
        v = rr(m[rng.integers(0, len(m), len(m))])
        if v is not None:
            vals.append(v)
    lo, hi = np.quantile(vals, [(1 - CI) / 2, 1 - (1 - CI) / 2])
    return float(lo), float(hi)


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    path, final = sys.argv[1], "--final" in sys.argv
    days = sorted(json.load(open(path, encoding="utf-8")), key=lambda d: d["Session"])
    if days and "AfterHoursPct" not in (days[0]["Rows"][0] if days[0]["Rows"] else {"AfterHoursPct": 0}):
        sys.exit("This backtest has no after-hours fields: rerun gapper radar-backtest on a build with IP-T501.")
    today = dt.date.today().isoformat()
    win = sessions_in_window(days, WINDOW_END)
    pos = sum(1 for d in win for r in d["Rows"] if ah(r))
    end = WINDOW_END
    if final and pos < MIN_POSITIVE and today > WINDOW_END:
        end = EXTENDED_END
        win = sessions_in_window(days, end)
        pos = sum(1 for d in win for r in d["Rows"] if ah(r))
    unknown = sum(1 for d in win for r in d["Rows"] if ah(r) is None)
    counts = (f"{len(win)} sessions {win[0]['Session'] if win else '-'} .. {win[-1]['Session'] if win else '-'} "
              f"(window {WINDOW_START} .. {end}); {sum(len(d['Rows']) for d in win)} candidate rows, {pos} AH+, {unknown} unknown.")
    if not final:
        print("Interim (counts only, amendment 06): " + counts)
        return
    if today <= end or not win or win[-1]["Session"] < end:
        sys.exit(f"Window not over (ends {end}, data through {win[-1]['Session'] if win else '-'}): no evaluation. {counts}")

    L = ["# After-hours sign forward test (IP-T501, amendment 06)", "", counts, ""]
    m = rr_by_session(win, RUN_PCT, MIN_PCT)
    a, n1, b, n0 = m.sum(0)
    r = rr(m)
    if n1 < MIN_POSITIVE or r is None:
        verdict, lo, hi = "INCONCLUSIVE", None, None
    else:
        lo, hi = boot(m)
        verdict = "PASS" if lo > 1 else "FAIL"
    L += ["## Primary", "", f"P(high >= +{RUN_PCT:.0f}% | AH+) = {a:.0f}/{n1:.0f}; P(... | AH-) = {b:.0f}/{n0:.0f}; "
          f"RR {r if r is None else round(r, 2)} [90%: {lo if lo is None else round(lo, 2)}, {hi if hi is None else round(hi, 2)}] -> **{verdict}**.", ""]

    base = np.mean([sum(1 for h in d["HighPct"].values() if h is not None and h >= RUN_PCT) / max(1, d["Liquid"]) for d in win])
    L += ["## Reported, not tested", "", "| subset | level | AH threshold | AH+ runs / n | AH- runs / n | RR | marginal lift |", "|---|---|---|---|---|---|---|"]
    top10 = lambda row: (row.get("RankHighD1") or 10**9) <= 10 or (row.get("RankCloseD1") or 10**9) <= 10
    for name, only in (("all candidates", lambda row: True), ("top-10 movers", top10)):
        for level in (50.0, 20.0):
            for t in (10.0, 5.0):
                mm = rr_by_session(win, level, t, only)
                a2, n12, b2, n02 = mm.sum(0)
                lift = (a2 / n12) / base if n12 and level == 50.0 and base > 0 else None
                v = rr(mm)
                L.append(f"| {name} | +{level:.0f}% | +{t:.0f}% | {a2:.0f}/{n12:.0f} | {b2:.0f}/{n02:.0f} | "
                         f"{'-' if v is None else f'{v:.2f}'} | {'-' if lift is None else f'{lift:.1f}x'} |")
    L.append("")
    print("\n".join(L))
    edge.write("afterhours-forward", L, {"path": path, "verdict": verdict, "rr": r, "lo": lo, "hi": hi, "positive": int(n1)})


if __name__ == "__main__":
    main()
