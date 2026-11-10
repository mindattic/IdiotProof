"""Score the gapper radar backtest against the baselines (IP-T500 amendment 02).

usage: python tools/momentum/edge/radar_backtest.py D:/IdiotProofData/radar/backtest-2026-09-10-2026-11-09.json
"""
import json, os, sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import edge  # noqa: E402

NS = [10, 20, 50]
LEVELS = [50, 20]


def ranked(rows, key):
    rs = [r for r in rows if key(r) is not None]
    return [r["Symbol"] for r in sorted(rs, key=key, reverse=True)]


def main():
    path = sys.argv[1]
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    days = json.load(open(path, encoding="utf-8"))
    lists = {
        "radar": lambda d: [r["Symbol"] for r in d["Rows"]],
        "B1 top D-1 high": lambda d: ranked(d["Rows"], lambda r: r["HighPctD1"]),
        "B2 top D-1 close": lambda d: ranked(d["Rows"], lambda r: r["ClosePctD1"]),
        "B3 top $vol surge": lambda d: ranked(d["Rows"], lambda r: r["DollarVolumeRatioD1"]),
    }
    res = {}
    for name, fn in lists.items():
        for lv in LEVELS:
            for n in NS:
                rec, prec = [], []
                for d in days:
                    runners = {s for s, h in d["HighPct"].items() if h is not None and h >= lv}
                    top = fn(d)[:n]
                    hits = sum(1 for s in top if s in runners)
                    prec.append(hits / n)
                    if runners:
                        rec.append(hits / len(runners))
                res[(name, lv, n)] = (np.array(prec), np.array(rec))
    L = ["# Gapper radar out of sample (IP-T500 amendment 02)", "", f"{len(days)} sessions {days[0]['Session']} .. {days[-1]['Session']}; "
         f"mean liquid names per session {np.mean([d['Liquid'] for d in days]):.0f}; mean candidates {np.mean([len(d['Rows']) for d in days]):.0f}.", ""]
    for lv in LEVELS:
        base = np.mean([sum(1 for h in d["HighPct"].values() if h is not None and h >= lv) / max(1, d["Liquid"]) for d in days])
        runners = np.mean([sum(1 for h in d["HighPct"].values() if h is not None and h >= lv) for d in days])
        L += [f"## High >= +{lv}% (base rate {base:.2%} of liquid names, {runners:.1f} runners per session)", "",
              "| list | " + " | ".join(f"precision@{n}" for n in NS) + " | " + " | ".join(f"recall@{n}" for n in NS) + " |",
              "|---|" + "---|" * (2 * len(NS))]
        for name in lists:
            cells = [f"{res[(name, lv, n)][0].mean():.1%}" for n in NS] + [f"{res[(name, lv, n)][1].mean():.1%}" for n in NS]
            L.append(f"| {name} | " + " | ".join(cells) + " |")
        L.append("")
    # pass rule: precision@20 at +50%, radar minus the best baseline, session bootstrap
    radar = res[("radar", 50, 20)][0]
    best_name = max([k for k in lists if k != "radar"], key=lambda k: res[(k, 50, 20)][0].mean())
    diff = radar - res[(best_name, 50, 20)][0]
    rng = np.random.default_rng(500)
    idx = rng.integers(0, len(diff), size=(5000, len(diff)))
    bm = diff[idx].mean(1)
    lo, hi = np.quantile(bm, [0.05, 0.95])
    ok = lo > 0
    L += ["## Pass rule", "", f"Precision@20 for >= +50%: radar {radar.mean():.1%} vs best baseline ({best_name}) {res[(best_name, 50, 20)][0].mean():.1%}; "
          f"difference {diff.mean():+.1%} [90%: {lo:+.1%}, {hi:+.1%}] -> **{'PASS' if ok else 'FAIL'}**.", ""]
    print("\n".join(L))
    edge.write("radar-backtest", L, {"path": path, "pass": bool(ok), "diff": float(diff.mean()), "lo": float(lo), "hi": float(hi)})


if __name__ == "__main__":
    main()
