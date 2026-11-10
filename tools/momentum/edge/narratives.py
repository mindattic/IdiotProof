"""What events does Momentum. say are behind his picks, and which of them produce real moves? (IP-T500, descriptive)

Each evening-watchlist / morning-update pick's spoken reason ("after hours gapper on earnings", "low float squeeze",
"day 2 continuation") is tagged with catalyst keys aligned to IdiotProof.Evidence.Gappers.CatalystTypes, plus a few
spoken-only keys for setups that are not company events. Then each key is crossed with what the stock did on the
trade date (Alpaca daily cross-section): high and close over the prior close, top-20 gainer, >= +20% high.
Fit split only (descriptive; validation outcomes stay unread until the carried choices are scored).

usage: python tools/momentum/edge/narratives.py
"""
import argparse, json, os, re, sys
from collections import defaultdict

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import edge  # noqa: E402  (splits, movers loader, known_before_open)

KEYS = [
    # company events (CatalystTypes keys)
    ("earnBeat", r"\bbeat(s|ing)?\b.{0,40}(earnings|estimates|expectations|revenue)|(earnings|revenue|eps).{0,40}\bbeat|record (revenue|quarter)"),
    ("earnOther", r"\bearnings\b|\bquarterly results\b|\bQ[1-4]\b"),
    ("acq", r"acqui(re|red|sition)|buyout|take[- ]private|merger|merge with|business combination"),
    ("pipe", r"private placement|strategic investment|\binvest(ment|ing|ed)? .{0,30}(million|\$)|stake in"),
    ("contract", r"\bcontract\b|partnership|partner(s|ed)? with|agreement with|\bdeal\b|\border\b|\bawarded\b|collaborat"),
    ("fda", r"\bfda\b|clinical|phase (1|2|3|one|two|three)|trial (data|results)|approval|biotech|drug"),
    ("rsplit", r"reverse (stock )?split|\bsplit\b"),
    ("offering", r"offering|dilution|diluti|\batm\b|warrants?|shelf|raise(d|s)? .{0,15}(million|capital|cash)"),
    ("theme", r"\bai\b|artificial intelligence|bitcoin|crypto|blockchain|quantum|nuclear|drone|data center|stablecoin|treasury|ethereum|solana|robot|space|defen[cs]e|rare earth|uranium|gold"),
    ("uplist", r"uplist|ipo\b|newly listed|recent ipo|just listed"),
    ("upgrade", r"upgrade|price target|initiat(es|ed) coverage|analyst"),
    ("newsGeneric", r"\bnews\b|announce|press release|catalyst|headline|pr\b"),
    # spoken-only market structure
    ("afterHours", r"after[- ]?hours|post[- ]?market|overnight|late (day|session)"),
    ("premarket", r"pre[- ]?market|this morning|gapp(ing|ed|er) up"),
    ("lowFloat", r"low float|float (of|under|is)|tiny float|small float|\bfloat\b"),
    ("squeeze", r"squeez|short interest|shorts? (are )?trapped|borrow|halt(ed|s)?\b"),
    ("day2", r"day[- ]?(2|two)|second day|continuation|yesterday'?s (runner|move|mover)|ran (yesterday|over|more than)|follow[- ]through"),
    ("technical", r"breakout|wedge|flag|vwap|support|resistance|double bottom|higher low|range|moving average|\bema\b|\bsma\b|consolidat"),
]
RX = [(k, re.compile(p, re.IGNORECASE)) for k, p in KEYS]


def tags(text):
    t = text or ""
    found = [k for k, rx in RX if rx.search(t)]
    if "earnBeat" in found and "earnOther" in found:
        found.remove("earnOther")
    company = {"earnBeat", "earnOther", "acq", "pipe", "contract", "fda", "rsplit", "offering", "theme", "uplist", "upgrade", "newsGeneric"}
    if not company.intersection(found):
        found.append("noCompanyEvent")
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--calls", default="D:/IdiotProofData/momentum/edge-calls.json")
    ap.add_argument("--movers", default="D:/IdiotProofData/momentum/movers")
    a = ap.parse_args()
    a.all = False  # validation outcomes are for confirming carried choices only (protocol), so this stays fit-only
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    calls = [c for c in json.load(open(a.calls, encoding="utf-8"))["calls"] if edge.known_before_open(c)]
    movers = edge.load_movers(a.movers)
    picks = {}
    for c in calls:
        if not c.get("sym") or not c.get("rk"):
            continue
        sp = edge.split_of(c["d"])
        if sp is None or (sp != "fit" and not a.all) or sp == "holdout":
            continue
        key = (c["d"], c["sym"])
        p = picks.setdefault(key, {"d": c["d"], "sym": c["sym"], "text": "", "k": c["k"]})
        p["text"] += " " + (c.get("why") or "") + " " + (c.get("q") or "")
    rows = []
    for (d, s), p in picks.items():
        r = movers.get(d, {}).get("rows", {}).get(s)
        if not r or r.get("HighPct") is None:
            continue
        rows.append({**p, "tags": tags(p["text"]), "hi": edge.wz(r["HighPct"]), "cl": edge.wz(r.get("ClosePct")), "rank": r.get("RankHigh"),
                     "gap": r.get("Gap"), "px": r.get("Pc")})
    base = {"n": len(rows), "hi": float(np.median([x["hi"] for x in rows])) if rows else None,
            "g20": float(np.mean([x["hi"] >= 20 for x in rows])) if rows else None,
            "top20": float(np.mean([(x["rank"] or 1e9) <= 20 for x in rows])) if rows else None,
            "clUp": float(np.mean([(x["cl"] or 0) > 0 for x in rows])) if rows else None}
    by = defaultdict(list)
    for x in rows:
        for t in x["tags"]:
            by[t].append(x)
    L = ["# What Momentum. says moves his picks, and what they did (IP-T500, descriptive)", "",
         f"Split: {'fit + validation' if a.all else 'fit'} (holdout never read). {base['n']} picks known before the open, with a daily bar on the trade date.",
         "Tags come from his spoken reason; several can fire per pick. Outcome is the trade date's daily bar against the prior close (winsorized at +300%).", "",
         f"All picks: median high {base['hi']:+.1f}%, high >= +20% on {base['g20']:.0%}, top-20 gainer {base['top20']:.0%}, closed green {base['clUp']:.0%}.", "",
         "| what he cited | picks | median high | high >= +20% | lift | top-20 gainer | closed green | median close |", "|---|---|---|---|---|---|---|---|"]
    for k, xs in sorted(by.items(), key=lambda kv: -len(kv[1])):
        if len(xs) < 10:
            continue
        g20 = float(np.mean([x["hi"] >= 20 for x in xs]))
        L.append(f"| {k} | {len(xs)} | {np.median([x['hi'] for x in xs]):+.1f}% | {g20:.0%} | {g20 / base['g20']:.2f} | "
                 f"{np.mean([(x['rank'] or 1e9) <= 20 for x in xs]):.0%} | {np.mean([(x['cl'] or 0) > 0 for x in xs]):.0%} | {np.median([x['cl'] or 0 for x in xs]):+.1f}% |")
    out = "\n".join(L)
    print(out)
    stamp = edge.write("narratives" + ("-all" if a.all else ""), L, {"base": base, "rows": rows})
    print(f"\nwrote runs/narratives-{stamp}.md", file=sys.stderr)


if __name__ == "__main__":
    main()
