"""Gapper study amendment 12 (IP-T625): per-session spread multipliers from quoted NBBO at simulated fills.

Implements amendment-12.json's calibration block exactly; run once after the registration commit.
Usage: python -I tools/gapper-study/a12_calibrate.py [--csv PATH] [--study DIR]
"""
import argparse
import csv
import datetime as dt
import hashlib
import json
import math
import os
import statistics
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_CSV = r"D:\IdiotProofData\gapper-study\quotes\entries-0830-buy-now.csv"
DEFAULT_STUDY = os.path.join(REPO, "docs", "research", "gapper-study-2026-11")
MAX_QUOTED = 0.50
MIN_LEGS = 1000
MIN_DAYS = 100
PREMARKET = (dt.time(4, 0), dt.time(9, 30))
REGULAR = (dt.time(9, 30), dt.time(16, 0))


def nth_sunday(year, month, n):
    d = dt.date(year, month, 1)
    d += dt.timedelta(days=(6 - d.weekday()) % 7)
    return d + dt.timedelta(weeks=n - 1)


def to_eastern(utc):
    # US rules since 2007: EDT from the 2nd Sunday of March 07:00 UTC to the 1st Sunday of November 06:00 UTC.
    start = dt.datetime.combine(nth_sunday(utc.year, 3, 2), dt.time(7, 0))
    end = dt.datetime.combine(nth_sunday(utc.year, 11, 1), dt.time(6, 0))
    return utc - dt.timedelta(hours=4 if start <= utc < end else 5)


def num(v):
    try:
        x = float(v)
    except ValueError:
        return math.nan
    return x


def session_of(at_utc):
    t = to_eastern(at_utc).time()
    if PREMARKET[0] <= t < PREMARKET[1]:
        return "premarket"
    if REGULAR[0] <= t < REGULAR[1]:
        return "regular"
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--csv", default=DEFAULT_CSV)
    ap.add_argument("--study", default=DEFAULT_STUDY)
    a = ap.parse_args()

    out_file = os.path.join(a.study, "data", "spread-multiplier-a12.json")
    if os.path.exists(out_file):
        sys.exit(f"{out_file} exists: amendment 12 is run once; a recalibration needs a new amendment.")
    with open(os.path.join(a.study, "data", "spread-multiplier.json"), encoding="utf-8") as f:
        frozen = json.load(f)["multiplier"]
    with open(a.csv, "rb") as f:
        csv_sha = hashlib.sha256(f.read()).hexdigest()

    seen, rows, duplicates = set(), [], 0
    with open(a.csv, newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            key = (r["symbol"], r["date"], r["leg"])
            if key in seen:
                duplicates += 1
                continue
            seen.add(key)
            rows.append(r)

    excluded = {"weight": 0, "noModelSpread": 0, "noQuote": 0, "quotedOver50pct": 0, "outsideSessions": 0}
    legs = []
    for r in rows:
        w, charged, q = num(r["weight"]), num(r["charged_spread"]), num(r["rel_spread_at"])
        if not (w > 0):
            excluded["weight"] += 1; continue
        if not (math.isfinite(charged) and charged > 0):
            excluded["noModelSpread"] += 1; continue
        if not (math.isfinite(q) and q >= 0):
            excluded["noQuote"] += 1; continue
        if q > MAX_QUOTED:
            excluded["quotedOver50pct"] += 1; continue
        at = dt.datetime.strptime(r["at_utc"], "%Y-%m-%dT%H:%M:%SZ")
        s = session_of(at)
        if s is None:
            excluded["outsideSessions"] += 1; continue
        legs.append({"symbol": r["symbol"], "date": r["date"], "leg": r["leg"], "w": w, "q": q, "cs": charged / frozen, "charged": charged, "session": s})

    result = {}
    for s in ("premarket", "regular"):
        l = [x for x in legs if x["session"] == s]
        days = len({x["date"] for x in l})
        den = sum(x["w"] * x["cs"] for x in l)
        m = sum(x["w"] * x["q"] for x in l) / den if den > 0 else math.nan
        ok = len(l) >= MIN_LEGS and days >= MIN_DAYS and math.isfinite(m) and m > 0
        result[s] = {
            "multiplier": m if ok else None,
            "computed": m,
            "rejected": not ok,
            "legs": len(l),
            "days": days,
            "entryLegs": sum(1 for x in l if x["leg"] == "entry"),
            "exitLegs": sum(1 for x in l if x["leg"] == "exit"),
            "overFrozen": m / frozen if math.isfinite(m) else None,
            "medianQuotedOverCs": statistics.median(x["q"] / x["cs"] for x in l) if l else None,
            "weightedMeanQuoted": sum(x["w"] * x["q"] for x in l) / sum(x["w"] for x in l) if l else None,
            "weightedMeanFrozenCharge": sum(x["w"] * x["charged"] for x in l) / sum(x["w"] for x in l) if l else None,
        }

    by_trade = {}
    for x in legs:
        by_trade.setdefault((x["symbol"], x["date"]), {})[x["leg"]] = x
    trades = [t for t in by_trade.values() if "entry" in t and "exit" in t]
    check = None
    if trades and all(result[s]["multiplier"] for s in ("premarket", "regular")):
        sw = sum(t["entry"]["w"] for t in trades)
        quoted = sum(t["entry"]["w"] * (t["entry"]["q"] + t["exit"]["q"]) / 2 for t in trades) / sw
        frozen_charge = sum(t["entry"]["w"] * t["entry"]["charged"] for t in trades) / sw
        a12 = sum(t["entry"]["w"] * t["entry"]["cs"] * (result[t["entry"]["session"]]["multiplier"] + result[t["exit"]["session"]]["multiplier"]) / 2 for t in trades) / sw
        check = {"trades": len(trades), "weightedMeanQuotedRoundTripSpread": quoted, "weightedMeanFrozenSpread": frozen_charge, "weightedMeanA12Spread": a12}

    git = subprocess.run(["git", "-C", REPO, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
    now = dt.datetime.now(dt.timezone.utc).replace(microsecond=0)
    doc = {
        "amendment": "A12", "ticket": "IP-T625",
        "premarket": result["premarket"]["multiplier"], "regular": result["regular"]["multiplier"],
        "statistic": "amendment-01-weighted ratio of sums: sum(w x quoted rel spread at fill) / sum(w x prior-days Corwin-Schultz), per leg session (ET 04:00-09:29 premarket, 09:30-15:59 regular)",
        "frozenMultiplier": frozen, "sessions": result, "tradeCheck": check,
        "rowsOnFile": len(rows) + duplicates, "duplicateRowsDropped": duplicates, "legsUsed": len(legs), "excluded": excluded,
        "maxQuotedSpread": MAX_QUOTED, "minLegs": MIN_LEGS, "minDays": MIN_DAYS,
        "csv": a.csv, "csvSha256": csv_sha, "gitHead": git, "computedUtc": now.isoformat().replace("+00:00", "Z"),
    }
    os.makedirs(os.path.dirname(out_file), exist_ok=True)
    with open(out_file, "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=2)

    stamp = now.strftime("%Y%m%d-%H%M")
    runs = os.path.join(a.study, "runs", "explore")
    with open(os.path.join(runs, f"a12-calibration-{stamp}.json"), "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=2)

    def p(v):
        return "n/a" if v is None else f"{v:.2%}"

    def x(v):
        return "n/a" if v is None else f"{v:.3f}"

    md = [
        "# Amendment 12: spread multipliers from quoted NBBO, per session (train, calibration)", "",
        f"Source `{os.path.basename(a.csv)}` (sha256 `{csv_sha[:12]}`), {len(rows) + duplicates:,} rows on file, {duplicates:,} duplicate rows dropped, {len(legs):,} eligible legs. "
        f"Excluded: {', '.join(f'{k} {v:,}' for k, v in excluded.items())}. Frozen multiplier {frozen:.3f} (data/spread-multiplier.json). Script `tools/gapper-study/a12_calibrate.py` at {git[:8]}.", "",
        "| session | legs (entry / exit) | days | A12 multiplier | over frozen | median quoted / cs (descriptive) | weighted mean quoted spread | weighted mean frozen charge | verdict |",
        "|---|---|---|---|---|---|---|---|---|",
    ]
    for s in ("premarket", "regular"):
        r = result[s]
        md.append(f"| {s} | {r['legs']:,} ({r['entryLegs']:,} / {r['exitLegs']:,}) | {r['days']:,} | {x(r['computed'])} | {x(r['overFrozen'])}x | {x(r['medianQuotedOverCs'])} | {p(r['weightedMeanQuoted'])} | {p(r['weightedMeanFrozenCharge'])} | {'rejected' if r['rejected'] else 'accepted'} |")
    md.append("")
    if check:
        md.append(f"Trades with both legs eligible: {check['trades']:,}. Weighted mean quoted round-trip spread {p(check['weightedMeanQuotedRoundTripSpread'])}; "
                  f"the frozen model charged {p(check['weightedMeanFrozenSpread'])}; A12 charges {p(check['weightedMeanA12Spread'])}.")
        md.append("")
    md.append("Calibration, not a test: no trial is logged. Applies only to trading tests registered after amendment 12 that name `\"costModel\": \"A12\"`; A01-A11 verdicts stand as reported.")
    with open(os.path.join(runs, f"a12-calibration-{stamp}.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(md) + "\n")
    print("\n".join(md))


if __name__ == "__main__":
    main()
