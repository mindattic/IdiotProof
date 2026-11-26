"""Split-table exposure (IP-T644): flags upward basis changes (reverse splits) that GapperStudySplits has no row for and
sizes their effect on daily-bar returns. TRAIN only (2021-11-01..2024-10-31). Reads the two read-only CSV exports of the
frozen tables, never the database. A heuristic, not the IP-T642 detector: raw open / prior close >= 3, no recorded split
within 5 days, the level holds (median close of the next 5 sessions is >= 2x or <= 0.5x the prior close) and the volume
steps (median volume of the next 5 sessions is <= 0.5x or >= 2x the prior 5).

Usage: python -I tools/gapper-study/split_exposure.py [--daily PATH] [--splits PATH] [--out unrecorded_up.csv]
"""
import argparse

import pandas as pd

TRAIN_FROM, TRAIN_TO = "2021-11-01", "2024-10-31"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--daily", default=r"D:\IdiotProofData\gapper-study\a13\daily.csv")
    ap.add_argument("--splits", default=r"D:\IdiotProofData\gapper-study\a13\splits.csv")
    ap.add_argument("--out", default="unrecorded_up.csv")
    a = ap.parse_args()

    d = pd.read_csv(a.daily, header=None, names=["s", "d", "o", "h", "l", "c", "v"], dtype={"s": str}, parse_dates=["d"])
    sp = pd.read_csv(a.splits, header=None, names=["s", "ex", "old", "new"], dtype={"s": str}, parse_dates=["ex"])
    d = d.sort_values(["s", "d"]).reset_index(drop=True)
    g = d.groupby("s", sort=False)
    d["pc"] = g["c"].shift(1)
    d["vprev"] = g["v"].transform(lambda x: x.shift(1).rolling(5).median())
    d["vnext"] = g["v"].transform(lambda x: x[::-1].shift(1).rolling(5).median()[::-1])
    d["cnext"] = g["c"].transform(lambda x: x[::-1].shift(1).rolling(5).median()[::-1])
    d["oc"] = d["o"] / d["pc"]
    d["cc"] = d["c"] / d["pc"]
    tr = (d.d >= TRAIN_FROM) & (d.d <= TRAIN_TO)

    cand = d[tr & ((d.oc >= 3) | (d.oc <= 1 / 3)) & (d.pc > 0)].copy()
    cand["hold"] = (cand.cnext / cand.pc >= 2) | (cand.cnext / cand.pc <= 0.5)
    cand["vstep"] = (cand.vnext / cand.vprev <= 0.5) | (cand.vnext / cand.vprev >= 2)
    rec = sp.groupby("s")["ex"].apply(list).to_dict()
    cand["rec"] = [any(abs((e - day).days) <= 5 for e in rec.get(s, [])) for s, day in zip(cand.s, cand.d)]
    cand["rev"] = cand.oc >= 3
    print("candidates (raw open / prior close >= 3 or <= 1/3), train:", len(cand))
    print(cand.groupby(["rec", "rev"]).size())

    un_rev = cand[~cand.rec & cand.rev]
    conf = un_rev[un_rev.hold & un_rev.vstep]
    print("unrecorded upward, level holds:", int(un_rev.hold.sum()), "; and volume steps:", len(conf))
    print("by year:", conf.groupby(conf.d.dt.year).size().to_dict(), "; symbols:", conf.s.nunique())
    print("median open / prior close:", round(conf.oc.median(), 2))
    conf.to_csv(a.out, index=False)

    keys = set(zip(conf.s, conf.d))
    d["bad"] = [(s, day) in keys for s, day in zip(d.s, d.d)]
    band = tr & (d.pc >= 0.10) & (d.pc <= 20)
    x = d[band]
    for lab, col in [("close-to-open", "oc"), ("close-to-close", "cc")]:
        print(f"{lab}, prior close $0.10-$20, mean %: {(x[col] - 1).mean() * 100:.4f}; without the detected days: "
              f"{(x[~x.bad][col] - 1).mean() * 100:.4f}; n {len(x)}, dropped {int(x.bad.sum())}")

    d["nxo"] = g["o"].shift(-1) / d["c"] - 1
    d["nxc"] = g["c"].shift(-1) / d["c"] - 1
    d["nxbad"] = d.groupby("s", sort=False)["bad"].shift(-1)
    x = d[tr & (d.pc >= 0.10) & (d.pc <= 20) & (d.h / d.pc >= 1.2) & d.nxo.notna()]
    keep = x[~(x.nxbad == True)]  # noqa: E712
    print(f"signal days with high >= 1.2x prior close: {len(x)}; the next day is a detected day on {len(x) - len(keep)}")
    print(f"  close -> next open mean %: {x.nxo.mean() * 100:.3f}; without those: {keep.nxo.mean() * 100:.3f}")
    print(f"  close -> next close mean %: {x.nxc.mean() * 100:.3f}; without those: {keep.nxc.mean() * 100:.3f}")


if __name__ == "__main__":
    main()
