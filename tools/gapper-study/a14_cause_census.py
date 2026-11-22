"""Gapper study amendment 14 (IP-T632): the cause census - what was true at the D-1 close before a gapper day.

Implements amendment-14.json exactly on the train split (D in 2021-11-01..2024-10-31); nothing dated after
2024-10-31 is read (every loader filters at read time and the run refuses if a later date survives).
A clearly labelled NON-FROZEN sensitivity (IP-T633) drops symbol-days whose D bar looks like an unrecorded
reverse split and neutralises such bars in the tag history; the frozen numbers stay the headline.

Usage: python -I tools/gapper-study/a14_cause_census.py [--data DIR] [--a14 DIR] [--study DIR]
"""
import argparse
import datetime as dt
import hashlib
import itertools
import json
import math
import os
import subprocess
import sys

import numpy as np
import pandas as pd

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_DATA = r"D:\IdiotProofData\gapper-study\a13"
DEFAULT_A14 = r"D:\IdiotProofData\gapper-study\a14"
DEFAULT_SHARES = r"D:\IdiotProofData\sec-companyfacts\shares-outstanding.csv"
DEFAULT_STUDY = os.path.join(REPO, "docs", "research", "gapper-study-2026-11")

TRAIN_FROM, TRAIN_TO = "2021-11-01", "2024-10-31"
LAST_READ = "2024-10-31"
SEED = 1337
K = 100_000          # key stride for (id, day)
KM = 100_000_000     # key stride for (id, minute)
SAMPLE_SALT = "gapper-study-2026-11-amendment-01"

TAGS = ["ranYesterday", "ranLast5", "bigRunner60", "closedStrong", "volumeSurge", "reverseSplit90",
        "newListing180", "subDollar", "lowPriceLowAdv", "ssrD1", "crashedLast5", "hotTape",
        "dilutionFiling90", "eightK5", "insiderBuy30", "tinyShares"]
STRATIFIED_ONLY = {"insiderBuy30"}   # Form 4 detail exists only around candidate and sample days
COMBO_TAGS = [t for t in TAGS if t not in STRATIFIED_ONLY]
DILUTION_FORMS = {"S-1", "S-3", "424B3", "424B4", "424B5"}
EIGHTK_FORMS = {"8-K", "8-K/A"}


def day_of(s):
    return (np.datetime64(s, "D") - np.datetime64("1970-01-01", "D")).astype(np.int64)


def iso(d):
    return str(np.datetime64(int(d), "D"))


def refuse_after(name, max_day):
    if max_day > day_of(LAST_READ):
        sys.exit(f"refused: {name} holds a date after {LAST_READ} ({iso(max_day)}); validation and holdout stay unread.")


def et_minutes_from_utc(series):
    t = pd.to_datetime(series, utc=True, errors="coerce").dt.tz_convert("America/New_York").dt.tz_localize(None)
    return (t.values.astype("datetime64[m]").astype(np.int64))


# ---------------------------------------------------------------- loading

def load_daily(path):
    ids, parts, dates = {}, [], {}
    cut = LAST_READ
    for ch in pd.read_csv(path, header=None, names=["s", "d", "o", "h", "l", "c", "v"], dtype={"s": str, "d": str},
                          chunksize=1_000_000, keep_default_na=False):
        ch = ch[(ch["d"] <= cut) & (ch["v"] > 0)]
        if ch.empty:
            continue
        codes, uniq = pd.factorize(ch["s"])
        m = np.array([ids.setdefault(u, len(ids)) for u in uniq], dtype=np.int32)
        dcodes, duniq = pd.factorize(ch["d"])
        dm = np.array([dates.setdefault(u, day_of(u)) for u in duniq], dtype=np.int32)
        parts.append((m[codes], dm[dcodes], ch["o"].to_numpy(np.float64), ch["h"].to_numpy(np.float64),
                      ch["l"].to_numpy(np.float64), ch["c"].to_numpy(np.float64), ch["v"].to_numpy(np.float64)))
    cols = [np.concatenate([p[k] for p in parts]) for k in range(7)]
    del parts
    sym, day, o, h, l, c, v = cols
    order = np.lexsort((day, sym))
    sym, day, o, h, l, c, v = (a[order] for a in (sym, day, o, h, l, c, v))
    keep = np.ones(len(sym), bool)
    keep[1:] = ~((sym[1:] == sym[:-1]) & (day[1:] == day[:-1]))
    names = np.empty(len(ids), dtype=object)
    for k_, i_ in ids.items():
        names[i_] = k_
    return dict(sym=sym[keep], day=day[keep].astype(np.int64), o=o[keep], h=h[keep], l=l[keep], c=c[keep], v=v[keep],
                names=names, ids=ids, dupes=int((~keep).sum()))


def groupwise_cumsum(x, start_idx_of_row):
    cs = np.concatenate([[0.0], np.cumsum(x)])
    return cs


def lagged(a, lag, sym):
    out = np.full(len(a), np.nan)
    if lag == 0:
        return a.astype(np.float64).copy()
    out[lag:] = a[:-lag]
    bad = np.ones(len(a), bool)
    bad[lag:] = sym[lag:] != sym[:-lag]
    out[bad] = np.nan
    return out


def lead(a, k, sym):
    out = np.full(len(a), np.nan)
    out[:-k] = a[k:]
    bad = np.ones(len(a), bool)
    bad[:-k] = sym[:-k] != sym[k:]
    out[bad] = np.nan
    return out


def window_max(a, lo, hi, sym):
    out = np.full(len(a), -np.inf)
    for L in range(lo, hi + 1):
        x = lagged(a, L, sym)
        np.fmax(out, x, out=out)
    out[np.isinf(out)] = np.nan
    return out


def window_min(a, lo, hi, sym):
    return -window_max(-a, lo, hi, sym)


# ---------------------------------------------------------------- statistics

def per_day(dayidx, nd, mask, *vals):
    out = [np.bincount(dayidx[mask], minlength=nd).astype(np.float64)]
    for v in vals:
        out.append(np.bincount(dayidx[mask], weights=v[mask].astype(np.float64), minlength=nd))
    return out


def boot_rate_vs_base(W, n_d, y_d, N_d, Y_d):
    """Day-clustered bootstrap with the same day weights for the set and the base: lift draws and diff draws."""
    wn, wy, wN, wY = W @ n_d, W @ y_d, W @ N_d, W @ Y_d
    with np.errstate(invalid="ignore", divide="ignore"):
        rate = wy / wn
        base = wY / wN
        return rate / base, rate - base


def ci(x):
    x = x[np.isfinite(x)]
    if len(x) == 0:
        return None, None
    return float(np.quantile(x, 0.025)), float(np.quantile(x, 0.975))


def bh(pvals):
    p = np.asarray(pvals, float)
    n = len(p)
    order = np.argsort(p)
    q = np.empty(n)
    prev = 1.0
    for rank in range(n, 0, -1):
        i = order[rank - 1]
        prev = min(prev, p[i] * n / rank)
        q[i] = prev
    return q


def fnum(x, d=3):
    if x is None or (isinstance(x, float) and not math.isfinite(x)):
        return "n/a"
    return f"{x:.{d}f}"


def pct(x, d=2):
    if x is None or (isinstance(x, float) and not math.isfinite(x)):
        return "n/a"
    return f"{100 * x:.{d}f}%"


# ---------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default=DEFAULT_DATA)
    ap.add_argument("--a14", default=DEFAULT_A14)
    ap.add_argument("--shares", default=DEFAULT_SHARES)
    ap.add_argument("--study", default=DEFAULT_STUDY)
    a = ap.parse_args()
    t0 = dt.datetime.now()
    np.seterr(all="ignore")

    with open(os.path.join(a.study, "amendment-14.json"), encoding="utf-8") as f:
        a14 = json.load(f)
    with open(os.path.join(a.study, "amendment-13.json"), encoding="utf-8") as f:
        a13 = json.load(f)
    with open(os.path.join(a.study, "params.json"), encoding="utf-8") as f:
        params = json.load(f)
    elig = a13["eligibilityAtS"]
    guard = params["universe"]["unadjustedSplitGuard"]
    windows = [tuple(fd["eval"]) for fd in a13["signals"]["S3"]["folds"]]
    assert [t for t in a14["tags"]] == TAGS, "tag order differs from amendment-14.json"
    draws_tag, draws_combo = 10_000, 2_000
    min_combo = a14["combinations"]["minEligibleDays"]

    # ---- daily bars
    D = load_daily(os.path.join(a.data, "daily.csv"))
    sym, day, O, H, L, C, V = D["sym"], D["day"], D["o"], D["h"], D["l"], D["c"], D["v"]
    refuse_after("daily.csv", day.max())
    n = len(sym)
    print(f"[{dt.datetime.now():%H:%M:%S}] daily rows through {LAST_READ} with volume > 0: {n:,}", flush=True)

    ucount = np.bincount(day - day.min())
    sess_days = np.nonzero(ucount >= 0.2 * np.median(ucount[ucount > 0]))[0] + day.min()
    on_sess = np.isin(day, sess_days)
    stray = int((~on_sess).sum())
    if stray:
        sym, day, O, H, L, C, V = (x[on_sess] for x in (sym, day, O, H, L, C, V))
        n = len(sym)
    msi = np.searchsorted(sess_days, day).astype(np.int64)
    same_prev = np.zeros(n, bool)
    same_prev[1:] = sym[1:] == sym[:-1]
    first_row = np.ones(n, bool)
    first_row[1:] = ~same_prev[1:]
    sym_first_day = np.empty(sym.max() + 1, np.int64)
    sym_first_day[sym[first_row]] = day[first_row]
    keyd = sym.astype(np.int64) * K + day
    keym = sym.astype(np.int64) * K + msi

    # ---- splits
    sp = pd.read_csv(os.path.join(a.data, "splits.csv"), header=None, names=["s", "d", "old", "new"], dtype={"s": str, "d": str})
    if "ExDate" in str(sp.iloc[0, 1]):
        sp = sp.iloc[1:]
    sp = sp[sp["d"] <= LAST_READ]
    sp = sp[sp["s"].isin(D["ids"].keys()) & (sp["old"] > 0) & (sp["new"] > 0)]
    sp_sym = sp["s"].map(D["ids"]).to_numpy(np.int64)
    sp_day = sp["d"].map(day_of).to_numpy(np.int64)
    sp_fac = (sp["old"] / sp["new"]).to_numpy(np.float64)       # PriceFactor = Old / New (IdiotProof.Models.SplitEvent)
    sp_rev = sp_fac > 1.0
    f = np.ones(n)
    has_split = np.zeros(n, bool)
    pos = np.searchsorted(keyd, sp_sym * K + sp_day, "left")
    for p_, s_, fac in zip(pos, sp_sym, sp_fac):
        if p_ < n and sym[p_] == s_ and same_prev[p_]:
            f[p_] *= fac
            has_split[p_] = True
    sp_key = np.sort(sp_sym * K + sp_day)
    rsp_key = np.sort((sp_sym * K + sp_day)[sp_rev])

    # split-direction check: close on the ex-date bar over the prior close, against Old/New
    chk = has_split & same_prev
    idx = np.nonzero(chk)[0]
    ratio_obs = np.log(C[idx] / C[idx - 1]) / np.log(f[idx])
    ratio_obs = ratio_obs[np.isfinite(ratio_obs)]
    rev_idx = idx[f[idx] > 1]
    split_check = {
        "splitsMatchedToBars": int(len(idx)),
        "medianLogMoveOverLogOldNew": float(np.median(ratio_obs)) if len(ratio_obs) else None,
        "shareWithin0.5to1.5": float(np.mean((ratio_obs > 0.5) & (ratio_obs < 1.5))) if len(ratio_obs) else None,
        "reverseSplits(Old>New)": int(len(rev_idx)),
        "reverseMedianCloseExOverPrior": float(np.median(C[rev_idx] / C[rev_idx - 1])) if len(rev_idx) else None,
        "reverseMedianOldOverNew": float(np.median(f[rev_idx])) if len(rev_idx) else None,
        "convention": "OldRate/NewRate is the price multiplier; OldRate > NewRate is a reverse split (e.g. 10,1 = 1-for-10)",
    }

    # ---- per-bar derived series
    logf = np.log(f)
    cum_logf = np.cumsum(logf)
    grp_start = np.maximum.accumulate(np.where(first_row, np.arange(n), 0))
    F = np.exp(cum_logf - cum_logf[grp_start] + logf[grp_start] * 0)   # cumulative price factor within symbol
    Cp = np.full(n, np.nan)
    Cp[1:] = C[:-1] * f[1:]
    Cp[first_row] = np.nan
    r_up = H / Cp
    cc = C / Cp - 1.0
    raw_move = np.full(n, np.nan)
    raw_move[1:] = C[1:] / C[:-1] - 1.0
    U = same_prev & ~has_split & ((raw_move > guard["maxDailyUp"]) | (raw_move < guard["maxDailyDown"]))
    U_cs = np.concatenate([[0], np.cumsum(U)])
    dv_cs = np.concatenate([[0.0], np.cumsum(C * V)])
    vf_cs = np.concatenate([[0.0], np.cumsum(V * F)])
    rows = np.arange(n)

    # ADV20$ over the 20 sessions through S (inclusive), and present sessions
    j0 = np.searchsorted(keym, keym - 19, "left")
    cnt20 = rows - j0 + 1
    adv20d = (dv_cs[rows + 1] - dv_cs[j0]) / cnt20
    adv20s = (vf_cs[rows + 1] - vf_cs[j0]) / cnt20 / F
    # 20 sessions before S (exclusive) for volumeSurge / split signature
    jb = np.searchsorted(keym, keym - 20, "left")
    cntb = rows - jb
    with np.errstate(invalid="ignore", divide="ignore"):
        avgvol_before = np.where(cntb > 0, (vf_cs[rows] - vf_cs[jb]) / np.maximum(cntb, 1) / F, np.nan)

    # Split-like bar signature (NON-FROZEN, IP-T633): open or high > +200% over the adjusted prior close, share
    # volume no more than the prior 20-session average, and the median close of this bar and the next four
    # (train dates only) at least 2x the prior close (the new level holds).
    jump = same_prev & (((O / Cp - 1) > guard["maxDailyUp"]) | ((H / Cp - 1) > guard["maxDailyUp"])) & (V <= avgvol_before)
    split_like = np.zeros(n, bool)
    for j in np.nonzero(jump)[0]:
        e = j + 1
        while e < n and e < j + 5 and sym[e] == sym[j]:
            e += 1
        split_like[j] = np.median(C[j:e]) / Cp[j] >= 2.0
    del jump
    # Bad print (NON-FROZEN): open or high > +200% over the prior close while the low and the close stay within
    # 50% of it (a single absurd print, e.g. SLND 2023-02-16 open = high = 2000 on an $8.94 close).
    bad_print = same_prev & (((O / Cp - 1) > guard["maxDailyUp"]) | ((H / Cp - 1) > guard["maxDailyUp"])) \
        & (L / Cp < 1.5) & (C / Cp < 1.5)
    split_like_only = split_like.copy()
    split_like |= bad_print
    del j0, jb, cntb, dv_cs, vf_cs, adv20s, raw_move, logf, cum_logf, grp_start

    # ---- next bar D for each S row
    nxt_ok = np.zeros(n, bool)
    nxt_ok[:-1] = (sym[1:] == sym[:-1]) & (msi[1:] - msi[:-1] <= 5)
    dD = np.full(n, -1, np.int64)
    dD[:-1] = day[1:]
    dmsi = np.full(n, -1, np.int64)
    dmsi[:-1] = msi[1:]
    HD = np.full(n, np.nan); HD[:-1] = H[1:]
    OD = np.full(n, np.nan); OD[:-1] = O[1:]
    fD = np.ones(n); fD[:-1] = f[1:]
    slD = np.zeros(n, bool); slD[:-1] = split_like[1:]
    d_from, d_to = day_of(TRAIN_FROM), day_of(TRAIN_TO)

    # ---- A13 eligibility at S
    sym64 = sym.astype(np.int64)
    in_band = (C >= elig["closeMinInclusive"]) & (C < elig["closeMaxExclusive"])
    adv_ok = (adv20d >= elig["adv20DollarMin"]) & (cnt20 >= elig["minSessionsOfLast20"])
    lo = np.searchsorted(sp_key, sym64 * K + day - 25, "left")
    hi = np.searchsorted(sp_key, sym64 * K + dD, "right")
    no_split = (hi - lo) == 0
    gl = np.searchsorted(keyd, sym64 * K + dD - guard["lookbackDays"], "left")
    guard_hits = U_cs[rows + 1] - U_cs[gl]          # unexplained moves on bars dated [D-25, D)
    guard_ok = guard_hits == 0
    in_train = (dD >= d_from) & (dD <= d_to)
    eligible = nxt_ok & in_train & in_band & adv_ok & no_split & guard_ok
    funnel = {
        "rowsWithNextBarDInTrain": int((nxt_ok & in_train).sum()),
        "afterPriceBand": int((nxt_ok & in_train & in_band).sum()),
        "afterAdv20AndSessions": int((nxt_ok & in_train & in_band & adv_ok).sum()),
        "afterSplitExclusion": int((nxt_ok & in_train & in_band & adv_ok & no_split).sum()),
        "afterUnadjustedSplitGuard": int(eligible.sum()),
        "sRowsWithoutBarWithin5Sessions(inTrainRange)": int(((~nxt_ok) & (day >= d_from - 7) & (day < d_to)).sum()),
        "strayNonSessionBarsDropped": stray, "duplicateBarsDropped": D["dupes"],
    }
    gmove = HD / (C * fD) - 1.0     # f is 1 on eligible rows (no split S-25..D)

    # ---- CIK map, filings, shares
    cm = pd.read_csv(os.path.join(a.data, "cikmap.csv"), header=None, names=["s", "cik"], dtype=str, keep_default_na=False)
    if cm.iloc[0, 0] == "Symbol":
        cm = cm.iloc[1:]
    cm["cik"] = cm["cik"].str.strip()
    cm = cm[cm["cik"].str.isdigit()]
    sym_cik = np.full(len(D["names"]), -1, np.int64)
    for s_, c_ in zip(cm["s"], cm["cik"]):
        if s_ in D["ids"]:
            sym_cik[D["ids"][s_]] = int(c_)
    fl = pd.read_csv(os.path.join(a.data, "filings.csv"), sep="|", header=None, names=["s", "cik", "form", "acc", "filed"],
                     dtype=str, keep_default_na=False)
    fl = fl[fl["filed"] <= LAST_READ]
    fl = fl[fl["form"].isin(DILUTION_FORMS | EIGHTK_FORMS)].copy()
    fl["cik"] = fl["cik"].str.strip()
    fl = fl[fl["cik"].str.isdigit()]
    mapped_from_filings = 0
    for s_, c_ in fl.loc[fl["s"] != "", ["s", "cik"]].drop_duplicates("s").itertuples(index=False):
        if s_ in D["ids"] and sym_cik[D["ids"][s_]] < 0:
            sym_cik[D["ids"][s_]] = int(c_); mapped_from_filings += 1
    has_acc = fl["acc"].str.len() > 0
    tmin = np.empty(len(fl), np.int64)
    tmin[has_acc.to_numpy()] = et_minutes_from_utc(fl.loc[has_acc, "acc"])
    tmin[~has_acc.to_numpy()] = fl.loc[~has_acc, "filed"].map(day_of).to_numpy(np.int64) * 1440 + 17 * 60 + 30
    fcik = fl["cik"].astype(np.int64).to_numpy()
    accepted_share = float(has_acc.mean())

    def filing_count(forms, days):
        m = fl["form"].isin(forms).to_numpy()
        keys = np.sort(fcik[m] * KM + tmin[m])
        anchor = day * 1440 + 16 * 60
        ck = sym_cik[sym]
        lo_ = np.searchsorted(keys, ck * KM + anchor - days * 1440, "right")
        hi_ = np.searchsorted(keys, ck * KM + anchor, "right")
        return hi_ - lo_

    cik_known = sym_cik[sym] >= 0
    dil = filing_count(DILUTION_FORMS, 90) > 0
    ek = filing_count(EIGHTK_FORMS, 5) > 0

    sh = pd.read_csv(a.shares, dtype={"cik": np.int64, "end": str, "filed": str, "shares": np.float64, "source": str})
    sh = sh[sh["filed"] <= LAST_READ]
    sh = sh.sort_values(["cik", "filed", "end"], kind="mergesort")
    sh_key = sh["cik"].to_numpy(np.int64) * K + sh["filed"].map(day_of).to_numpy(np.int64)
    sh_cik = sh["cik"].to_numpy(np.int64)
    sh_val = sh["shares"].to_numpy()
    ck = sym_cik[sym]
    ix = np.searchsorted(sh_key, ck * K + day, "left") - 1     # filed strictly before D-1
    sh_ok = cik_known & (ix >= 0)
    sh_ok[sh_ok] = sh_cik[ix[sh_ok]] == ck[sh_ok]
    shares_at = np.where(sh_ok, sh_val[np.clip(ix, 0, None)], np.nan)
    del sh, sh_key, sh_cik, sh_val, fl

    # ---- tags at S (row-aligned); neutralise=True is the NON-FROZEN sensitivity
    first_sess = sess_days[0]

    def compute_tags(neutralise):
        ru, c1 = r_up.copy(), cc.copy()
        if neutralise:
            ru[split_like] = np.nan
            c1[split_like] = np.nan
        t, known = {}, {}
        t["ranYesterday"] = ru >= 1.20
        t["ranLast5"] = window_max(ru, 1, 4, sym) >= 1.20
        t["bigRunner60"] = window_max(ru, 5, 59, sym) >= 1.50
        t["closedStrong"] = (c1 >= 0.10) & (C >= L + 0.75 * (H - L))
        with np.errstate(invalid="ignore"):
            t["volumeSurge"] = V >= 5.0 * avgvol_before
        rl = np.searchsorted(rsp_key, sym64 * K + day - 90, "right")
        rh = np.searchsorted(rsp_key, sym64 * K + day, "right")
        t["reverseSplit90"] = (rh - rl) > 0
        fd_ = sym_first_day[sym]
        t["newListing180"] = (fd_ > first_sess) & (dD - fd_ <= 180)
        t["subDollar"] = C < 1.0
        t["lowPriceLowAdv"] = (C < 5.0) & (adv20d < 2_000_000)
        with np.errstate(invalid="ignore"):
            t["ssrD1"] = L <= 0.9 * Cp
        t["crashedLast5"] = window_min(c1, 0, 4, sym) <= -0.30
        t["dilutionFiling90"] = dil & cik_known
        known["dilutionFiling90"] = cik_known
        t["eightK5"] = ek & cik_known
        known["eightK5"] = cik_known
        t["insiderBuy30"] = np.zeros(n, bool)
        known["insiderBuy30"] = np.zeros(n, bool)
        t["tinyShares"] = sh_ok & (shares_at < 10_000_000)
        known["tinyShares"] = sh_ok
        # hotTape: count of eligible symbols with ranYesterday on date D, top quarter of train dates up to then
        ev = eligible & (~slD if neutralise else True)
        dates = np.unique(dD[ev])
        di = np.searchsorted(dates, dD[ev])
        cnt = np.bincount(di, weights=t["ranYesterday"][ev].astype(float), minlength=len(dates))
        hot = np.zeros(len(dates), bool)
        thr = np.zeros(len(dates))
        for k in range(len(dates)):
            thr[k] = np.quantile(cnt[:k + 1], 0.75)
            hot[k] = cnt[k] > thr[k]     # strictly above the expanding 75th percentile (ties at it are not top quarter)
        hp = np.searchsorted(dates, dD)
        hp_ok = (hp < len(dates))
        hp_ok[hp_ok] = dates[hp[hp_ok]] == dD[hp_ok]
        t["hotTape"] = hp_ok & hot[np.clip(hp, 0, len(dates) - 1)]
        for k_ in TAGS:
            known.setdefault(k_, np.ones(n, bool))
            t[k_] = np.asarray(t[k_], bool)
        return t, known, {"dates": len(dates), "hotDates": int(hot.sum()), "medianCount": float(np.median(cnt)),
                          "finalThreshold": float(thr[-1])}

    # ---- stratified population (candidate days + amendment 01 sample) for news and insider tags
    ud = pd.read_csv(os.path.join(a.a14, "udays.txt"), sep="|", header=None,
                     names=["s", "d", "cand", "excl", "ctl", "gap"], dtype=str, keep_default_na=False)
    for col in ("excl", "ctl"):
        ud[col] = ud[col].str.replace("\x00", "", regex=False).str.strip()
    ud = ud[ud["d"] <= LAST_READ]
    refuse_after("udays", day_of(ud["d"].max()))
    ud = ud[ud["s"].isin(D["ids"].keys())]
    ud_sym = ud["s"].map(D["ids"]).to_numpy(np.int64)
    ud_day = ud["d"].map(day_of).to_numpy(np.int64)
    ud_cand = ud["cand"].to_numpy() == "1"
    ud_incl = ud["excl"].to_numpy() == ""

    def in_sample(s_, d_):
        h_ = hashlib.sha256(f"{s_}|{d_}|{SAMPLE_SALT}".encode()).hexdigest()
        return int(h_[:8], 16) / 2 ** 32 < 0.10

    ud_samp = np.array([ud_incl[i] and not ud_cand[i] and in_sample(s_, d_)
                        for i, (s_, d_) in enumerate(zip(ud["s"], ud["d"]))], bool)
    # control rows: row of control symbol at D, linked to (gapper symbol, D)
    ctl = ud[ud["ctl"] != ""][["s", "d", "ctl"]].copy()
    w_ud = np.where(ud_incl & ud_cand, 1.0, np.where(ud_samp, 10.0, 0.0))
    # map universe rows to S rows (row whose next bar is D)
    dpos = np.searchsorted(keyd, ud_sym * K + ud_day, "left")
    ok = dpos < n
    ok[ok] = keyd[dpos[ok]] == (ud_sym * K + ud_day)[ok]
    ok &= dpos > 0
    ok[ok] = same_prev[dpos[ok]]
    strat_w = np.zeros(n)
    srow = dpos[ok & (w_ud > 0)] - 1
    strat_w[srow] = w_ud[ok & (w_ud > 0)]
    del ud

    # news: non-coverage items per symbol, ET minutes
    nw = pd.read_csv(os.path.join(a.a14, "news.txt"), sep="|", header=None, names=["s", "t", "typ"], dtype=str, keep_default_na=False)
    nw["typ"] = nw["typ"].str.replace("\x00", "", regex=False).str.strip()
    nw = nw[nw["s"].isin(D["ids"].keys())]
    nt = et_minutes_from_utc(nw["t"].str.slice(0, 19))
    refuse_after("news", (nt.max() // 1440))
    noncov = (nw["typ"] != "NoCatalyst").to_numpy()
    nkey = np.sort(nw["s"].map(D["ids"]).to_numpy(np.int64)[noncov] * KM + nt[noncov])
    del nw
    anchor = day * 1440 + 960
    fresh_cnt = np.searchsorted(nkey, sym64 * KM + dD * 1440 + 240, "left") - np.searchsorted(nkey, sym64 * KM + anchor, "right")
    d5 = sess_days[np.clip(dmsi - 5, 0, len(sess_days) - 1)]
    stale_cnt = np.searchsorted(nkey, sym64 * KM + anchor, "right") - np.searchsorted(nkey, sym64 * KM + d5 * 1440, "left")
    fresh = fresh_cnt > 0
    stale5 = (stale_cnt > 0) & ~fresh

    # insider purchases: knowable from max(transaction date, Form 4 filing date) through transaction date + 29
    ip = pd.read_csv(os.path.join(a.a14, "insiderP.txt"), sep="|", header=None, names=["claim", "td", "created"], dtype=str, keep_default_na=False)
    rc = pd.read_csv(os.path.join(a.a14, "claims.txt"), sep="|", header=None, names=["id", "tick", "art", "src", "typ"], dtype=str, keep_default_na=False)
    ip = ip.merge(rc[["id", "tick", "art"]], left_on="claim", right_on="id", how="left")
    insider_join = {"pRows": int(len(ip)), "joinedToTicker": int((ip["tick"].notna() & (ip["tick"] != "")).sum())}
    ip = ip[ip["tick"].notna() & (ip["tick"] != "") & ip["td"].str.match(r"^\d{4}-\d{2}-\d{2}$") & ip["art"].str.match(r"^\d{4}-\d{2}-\d{2}$")]
    ip = ip[(ip["td"] <= LAST_READ) & (ip["art"] <= LAST_READ) & (ip["td"] >= "2021-06-01")]
    ip = ip[ip["tick"].isin(D["ids"].keys())]
    i_sym = ip["tick"].map(D["ids"]).to_numpy(np.int64)
    i_td = ip["td"].map(day_of).to_numpy(np.int64)
    i_fd = ip["art"].map(day_of).to_numpy(np.int64)
    i_a = np.maximum(i_td, i_fd)
    i_b = i_td + 29
    okp = i_a <= i_b
    ka = np.sort(i_sym[okp] * K + i_a[okp])
    kb = np.sort(i_sym[okp] * K + i_b[okp])
    ins = (np.searchsorted(ka, sym64 * K + day, "right") - np.searchsorted(kb, sym64 * K + day, "left")) > 0
    insider_join["usableIntervals"] = int(okp.sum())
    del ip, rc

    # ---- events and controls
    evs = pd.read_csv(os.path.join(a.data, "events.csv"), header=None, names=["s", "d", "split", "gmax", "t20", "t50", "t100"],
                      dtype={"s": str, "d": str, "split": str}, keep_default_na=False)
    evs = evs[(evs["split"] == "train") & (evs["d"] >= TRAIN_FROM) & (evs["d"] <= TRAIN_TO)]
    evs = evs[evs["s"].isin(D["ids"].keys())]

    def s_row_of(symbols, dates):
        ks = symbols.map(D["ids"]).to_numpy(np.int64) * K + dates.map(day_of).to_numpy(np.int64)
        p_ = np.searchsorted(keyd, ks, "left")
        good = (p_ < n)
        good[good] = keyd[p_[good]] == ks[good]
        good &= p_ > 0
        good[good] = same_prev[p_[good]]
        return np.where(good, p_ - 1, -1)

    ev_row = s_row_of(evs["s"], evs["d"])
    ctl = ctl[ctl["s"].isin(D["ids"].keys())]
    ctl_row = s_row_of(ctl["s"], ctl["d"])
    ev_index = {(s_, d_): i for i, (s_, d_) in enumerate(zip(evs["s"], evs["d"]))}
    ctl_ev = np.array([ev_index.get((c_, d_), -1) for c_, d_ in zip(ctl["ctl"], ctl["d"])], np.int64)

    # ---- analysis for one version
    rng = np.random.default_rng(SEED)

    def run_version(neutralise, W10, W2):
        tags, known, hot_info = compute_tags(neutralise)
        pop = eligible & (~slD if neutralise else True)
        pidx = np.nonzero(pop)[0]
        Dd = dD[pidx]
        days_u = np.unique(Dd)
        nd = len(days_u)
        di = np.searchsorted(days_u, Dd)
        g = gmove[pidx]
        y = {"G20": g >= 0.20, "G50": g >= 0.50, "G100": g >= 1.00}
        N = len(pidx)
        base = {k: float(v.mean()) for k, v in y.items()}
        N_d, Y20_d, Y50_d = per_day(di, nd, np.ones(N, bool), y["G20"], y["G50"])
        assert W10.shape[1] == nd and W2.shape[1] == nd
        win_masks = [(Dd >= day_of(w0)) & (Dd <= day_of(w1)) for w0, w1 in windows]
        win_base = [float(y["G20"][m].mean()) for m in win_masks]
        T = {k: tags[k][pidx] for k in TAGS}
        Kn = {k: known[k][pidx] for k in TAGS}

        per_year = {}
        years = (Dd.astype("datetime64[D]").astype("datetime64[Y]").astype(int) + 1970)
        for yr in np.unique(years):
            m = years == yr
            per_year[int(yr)] = {"days": int(m.sum()), "G20": int(y["G20"][m].sum()), "G50": int(y["G50"][m].sum()),
                                 "G100": int(y["G100"][m].sum()), "sessions": int(len(np.unique(Dd[m])))}

        tag_rows, pvals, labels = [], [], []
        for k in TAGS:
            if k in STRATIFIED_ONLY:
                continue
            m, kn = T[k], Kn[k]
            nk = int(m.sum())
            row = {"tag": k, "eligibleDays": int(kn.sum()), "knownShare": float(kn.mean()), "days": nk,
                   "prevalence": nk / max(int(kn.sum()), 1)}
            for o in ("G20", "G50", "G100"):
                row[f"p{o}"] = float(y[o][m].mean()) if nk else None
                wo = kn & ~m
                row[f"p{o}Without"] = float(y[o][wo].mean()) if wo.any() else None
                row[f"coverage{o}"] = float(m[y[o]].mean()) if y[o].any() else None
                row[f"lift{o}"] = row[f"p{o}"] / base[o] if nk and base[o] > 0 else None
            if nk:
                n_d, y_d, y5_d = per_day(di, nd, m, y["G20"], y["G50"])
                lift_b, diff_b = boot_rate_vs_base(W10, n_d, y_d, N_d, Y20_d)
                row["liftG20Lo"], row["liftG20Hi"] = ci(lift_b)
                l5, _ = boot_rate_vs_base(W10, n_d, y5_d, N_d, Y50_d)
                row["liftG50Lo"], row["liftG50Hi"] = ci(l5)
                row["p"] = float((1 + np.sum(~(diff_b > 0))) / (len(diff_b) + 1))
                row["windowPG20"] = [float(y["G20"][m & wm].mean()) if (m & wm).any() else None for wm in win_masks]
                row["windowLift"] = [(row["windowPG20"][i] / win_base[i]) if row["windowPG20"][i] is not None else None for i in range(4)]
            else:
                row.update({"liftG20Lo": None, "liftG20Hi": None, "p": 1.0, "windowPG20": [None] * 4, "windowLift": [None] * 4})
            row["stable"] = bool(row["liftG20Lo"] is not None and row["liftG20Lo"] > 2 and all(
                row["windowPG20"][i] is not None and row["windowPG20"][i] > win_base[i] for i in range(4)))
            tag_rows.append(row); pvals.append(row["p"]); labels.append(("tag", k))

        # combinations
        combo_rows = []
        for size in a14["combinations"]["sizes"]:
            for combo in itertools.combinations(COMBO_TAGS, size):
                m = T[combo[0]].copy()
                for k in combo[1:]:
                    m &= T[k]
                nk = int(m.sum())
                if nk < min_combo:
                    continue
                n_d, y_d, y5_d = per_day(di, nd, m, y["G20"], y["G50"])
                lift_b, diff_b = boot_rate_vs_base(W2, n_d, y_d, N_d, Y20_d)
                lo_, hi_ = ci(lift_b)
                p20 = float(y["G20"][m].mean()); p50 = float(y["G50"][m].mean()); p100 = float(y["G100"][m].mean())
                wp = [float(y["G20"][m & wm].mean()) if (m & wm).any() else None for wm in win_masks]
                wn = [int((m & wm).sum()) for wm in win_masks]
                row = {"combo": "+".join(combo), "size": size, "days": nk, "pG20": p20, "pG50": p50, "pG100": p100,
                       "liftG20": p20 / base["G20"], "liftG50": p50 / base["G50"] if base["G50"] else None,
                       "liftG20Lo": lo_, "liftG20Hi": hi_,
                       "coverageG20": float(m[y["G20"]].mean()), "coverageG50": float(m[y["G50"]].mean()),
                       "windowPG20": wp, "windowDays": wn,
                       "windowLift": [(wp[i] / win_base[i]) if wp[i] is not None else None for i in range(4)],
                       "p": float((1 + np.sum(~(diff_b > 0))) / (len(diff_b) + 1))}
                row["stable"] = bool(lo_ is not None and lo_ > 2 and all(wp[i] is not None and wp[i] > win_base[i] for i in range(4)))
                combo_rows.append(row); pvals.append(row["p"]); labels.append(("combo", row["combo"]))

        # stratified tags (news, insider) on candidate + sample days with amendment 01 weights
        sw = strat_w[pidx]
        sm = sw > 0
        strat = {"rows": int(sm.sum()), "candidateRows": int(((sw == 1) & sm).sum()), "sampleRows": int((sw == 10).sum()),
                 "weightedDays": float(sw.sum())}
        sN_d, sY20_d, sY50_d = per_day(di, nd, sm, sw * y["G20"], sw * y["G50"])
        sw_N = np.bincount(di[sm], weights=sw[sm], minlength=nd)
        sbase = {o: float((sw * y[o]).sum() / sw.sum()) for o in y}
        strat["baseRates"] = sbase
        strat_rows = []
        stags = {"freshNews": fresh[pidx], "staleNews5": stale5[pidx], "noNewsEither": ~(fresh[pidx] | stale5[pidx]),
                 "insiderBuy30": ins[pidx]}
        for k, mk in stags.items():
            m = mk & sm
            wsum = float(sw[m].sum())
            row = {"tag": k, "rows": int(m.sum()), "weightedPrevalence": wsum / float(sw.sum())}
            for o in ("G20", "G50", "G100"):
                row[f"p{o}"] = float((sw[m] * y[o][m]).sum() / wsum) if wsum else None
                wo = sm & ~mk
                row[f"p{o}Without"] = float((sw[wo] * y[o][wo]).sum() / sw[wo].sum()) if wo.any() else None
                yo = sm & y[o]
                row[f"coverage{o}"] = float(sw[yo & mk].sum() / sw[yo].sum()) if yo.any() else None
                row[f"lift{o}"] = row[f"p{o}"] / sbase[o] if wsum and sbase[o] else None
            if m.any():
                n_d = np.bincount(di[m], weights=sw[m], minlength=nd)
                y_d = np.bincount(di[m], weights=(sw * y["G20"])[m], minlength=nd)
                lift_b, diff_b = boot_rate_vs_base(W10, n_d, y_d, sw_N, sY20_d)
                row["liftG20Lo"], row["liftG20Hi"] = ci(lift_b)
                row["p"] = float((1 + np.sum(~(diff_b > 0))) / (len(diff_b) + 1))
            else:
                row.update({"liftG20Lo": None, "liftG20Hi": None, "p": 1.0})
            strat_rows.append(row)
            if k != "noNewsEither":
                pvals.append(row["p"]); labels.append(("stratified", k))

        q = bh(pvals)
        qmap = {lab: float(qq) for lab, qq in zip(labels, q)}
        for r in tag_rows:
            r["q"] = qmap[("tag", r["tag"])]
        for r in combo_rows:
            r["q"] = qmap[("combo", r["combo"])]
        for r in strat_rows:
            r["q"] = qmap.get(("stratified", r["tag"]))

        # unexplained remainder and exclusive hierarchy (frozen tag order; unknown counts as not carrying)
        hier = {}
        any_tag = np.zeros(N, bool)
        for k in TAGS:
            any_tag |= T[k]
        no_hot = np.zeros(N, bool)
        for k in TAGS:
            if k != "hotTape":
                no_hot |= T[k]
        # supplementary (post hoc, not frozen): only the narrow tags, those carried by < 10% of known eligible days
        broad = [r["tag"] for r in tag_rows if r["prevalence"] >= 0.10]
        narrow = np.zeros(N, bool)
        for k in TAGS:
            if k not in broad:
                narrow |= T[k]
        for o in ("ALL", "G20", "G50", "G100"):
            sel = np.ones(N, bool) if o == "ALL" else y[o]
            remaining = sel.copy()
            parts = []
            for k in TAGS:
                hit = remaining & T[k]
                parts.append({"tag": k, "days": int(hit.sum()), "share": float(hit.sum() / max(sel.sum(), 1))})
                remaining &= ~hit
            parts.append({"tag": "none", "days": int(remaining.sum()), "share": float(remaining.sum() / max(sel.sum(), 1))})
            hier[o] = {"total": int(sel.sum()), "exclusive": parts,
                       "noTagShare": float((sel & ~any_tag).sum() / max(sel.sum(), 1)),
                       "noSymbolTagShare(excl. hotTape, supplementary)": float((sel & ~no_hot).sum() / max(sel.sum(), 1)),
                       "noNarrowTagShare(supplementary)": float((sel & ~narrow).sum() / max(sel.sum(), 1)),
                       "broadTagsLeftOut": broad}

        top = np.argsort(-g)[:20]
        top20 = []
        for j in top:
            r_ = pidx[j]
            top20.append({"symbol": D["names"][sym[r_]], "D": iso(dD[r_]), "S close": float(C[r_]), "D open": float(OD[r_]),
                          "D high": float(HD[r_]), "highOverClose": float(g[j] + 1), "openOverClose": float(OD[r_] / C[r_]),
                          "D volume / prior 20-session avg": float(V[r_ + 1] / avgvol_before[r_ + 1]) if avgvol_before[r_ + 1] > 0 else None,
                          "splitLike": bool(split_like[r_ + 1])})
        return {"N": N, "sessions": nd, "base": base, "windowBase": win_base, "perYear": per_year, "tags": tag_rows,
                "combos": combo_rows, "stratified": {"info": strat, "rows": strat_rows}, "hierarchy": hier,
                "top20": top20, "hotTape": hot_info, "testsInBh": len(pvals),
                "splitLikeDaysRemoved": None}, tags, known

    # day weights (same day set for both versions? the sensitivity may drop whole days: build per version)
    def weights_for(dayset_size, draws):
        return rng.multinomial(dayset_size, np.full(dayset_size, 1.0 / dayset_size), size=draws).astype(np.float64)

    nd_frozen = len(np.unique(dD[eligible]))
    W10 = weights_for(nd_frozen, draws_tag)
    W2 = weights_for(nd_frozen, draws_combo)
    print(f"[{dt.datetime.now():%H:%M:%S}] frozen version: {int(eligible.sum()):,} eligible symbol-days on {nd_frozen} sessions", flush=True)
    frozen, tags_f, known_f = run_version(False, W10, W2)
    pop_s = eligible & ~slD
    nd_sens = len(np.unique(dD[pop_s]))
    if nd_sens != nd_frozen:
        W10s, W2s = weights_for(nd_sens, draws_tag), weights_for(nd_sens, draws_combo)
    else:
        W10s, W2s = W10, W2
    print(f"[{dt.datetime.now():%H:%M:%S}] sensitivity version", flush=True)
    sens, _, _ = run_version(True, W10s, W2s)
    del W10, W2, W10s, W2s
    pe = np.nonzero(eligible)[0]
    g_e = gmove[pe]
    sl_e = slD[pe]
    bp_e = np.zeros(len(pe), bool)
    bp_e = bad_print[np.minimum(pe + 1, n - 1)]
    named = ["DVLT", "BINI", "EMPD", "FFAI", "MTVA", "YYAI", "DCX", "NBRVF", "MSPR", "NTRP", "ATNXQ", "AGPU", "CHGA",
             "SUNE", "NIXX", "SVRN", "HTGMQ", "SLND", "OLOX", "TPST"]
    named_rows = []
    for r_, gg, s_l in zip(pe, g_e, sl_e):
        nm = D["names"][sym[r_]]
        if nm in named and gg >= 1.0:
            named_rows.append({"symbol": nm, "D": iso(dD[r_]), "highOverClose": float(gg + 1), "openOverClose": float(OD[r_] / C[r_]),
                               "removedBySensitivity": bool(s_l)})
    sens["namedSymbolsCheck"] = named_rows
    sens["splitLikeDaysRemoved"] = {"days": int(sl_e.sum()), "badPrintDays": int(bp_e.sum()),
                                    "G20": int((sl_e & (g_e >= .2)).sum()),
                                    "G50": int((sl_e & (g_e >= .5)).sum()), "G100": int((sl_e & (g_e >= 1)).sum()),
                                    "examples": [{"symbol": D["names"][sym[r_]], "D": iso(dD[r_]), "openOverClose": float(OD[r_] / C[r_])}
                                                 for r_ in pe[sl_e][np.argsort(-g_e[sl_e])][:25]]}

    # ---- events vs controls (frozen tags) and G20 recovery
    ev_t = {t_: evs[t_].astype(str).str.strip().isin(["1", "True", "true"]).to_numpy() for t_ in ("t20", "t50", "t100")}
    evc = {}
    for t_, label in (("t20", "Tier20"), ("t50", "Tier50"), ("t100", "Tier100")):
        em = ev_t[t_] & (ev_row >= 0)
        cm_ = (ctl_ev >= 0) & (ctl_row >= 0)
        cm_[cm_] = ev_t[t_][ctl_ev[cm_]]
        rws = ev_row[em]; crw = ctl_row[cm_]
        rowsout = []
        for k in TAGS:
            kn_e = known_f[k][rws]; kn_c = known_f[k][crw]
            pe_ = float(tags_f[k][rws][kn_e].mean()) if kn_e.any() else None
            pc_ = float(tags_f[k][crw][kn_c].mean()) if kn_c.any() else None
            if k == "insiderBuy30":
                pe_ = float(ins[rws].mean()); pc_ = float(ins[crw].mean())
            rowsout.append({"tag": k, "events": pe_, "controls": pc_,
                            "ratio": (pe_ / pc_) if (pe_ is not None and pc_) else None,
                            "eventsKnown": float(kn_e.mean()) if len(kn_e) else None})
        evc[label] = {"events": int(em.sum()), "eventsOnFile": int(ev_t[t_].sum()), "controls": int(cm_.sum()), "rows": rowsout}
    t20 = ev_t["t20"] & (ev_row >= 0)
    r20 = ev_row[t20]
    rec_all = float((gmove[r20] >= 0.20).mean())
    in_pop = eligible[r20]
    recovery = {"tier20EventsOnFile": int(ev_t["t20"].sum()), "withBarsDAndDminus1": int(t20.sum()),
                "G20AmongThose": rec_all, "inA14Population": int(in_pop.sum()),
                "G20AmongPopulationEvents": float((gmove[r20][in_pop] >= 0.20).mean()) if in_pop.any() else None,
                "eventsNotInPopulationReasons": {
                    "priceBand": int((~in_band[r20]).sum()), "adv20OrSessions": int((~adv_ok[r20]).sum()),
                    "splitExclusion": int((~no_split[r20]).sum()), "unadjustedGuard": int((~guard_ok[r20]).sum()),
                    "noNextBarWithin5": int((~nxt_ok[r20]).sum())}}
    pop_g20 = int((gmove[pe] >= 0.2).sum())
    ev_keys = set(zip(evs["s"][ev_t["t20"]], evs["d"][ev_t["t20"]]))
    pop_g20_in_events = sum(1 for r_ in pe[gmove[pe] >= 0.2] if (D["names"][sym[r_]], iso(dD[r_])) in ev_keys)
    recovery["populationG20DaysThatAreTier20Events"] = pop_g20_in_events
    recovery["populationG20Days"] = pop_g20

    # ---- outputs
    git = subprocess.run(["git", "-C", REPO, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
    now = dt.datetime.now(dt.timezone.utc).replace(microsecond=0)
    stamp = now.strftime("%Y%m%d-%H%M")
    doc = {
        "amendment": "A14", "ticket": "IP-T632", "sensitivityTicket": "IP-T633", "computedUtc": now.isoformat().replace("+00:00", "Z"),
        "gitHead": git, "split": "train", "dFrom": TRAIN_FROM, "dTo": TRAIN_TO, "lastDateRead": LAST_READ,
        "evaluationWindows": windows, "drawsTag": draws_tag, "drawsCombo": draws_combo, "seed": SEED,
        "funnel": funnel, "splitConvention": split_check,
        "cik": {"symbolsWithCik": int((sym_cik >= 0).sum()), "mappedFromFilings": mapped_from_filings,
                "filingsWithAcceptanceTime": accepted_share},
        "insiderJoin": insider_join,
        "frozen": frozen, "sensitivityNonFrozen": sens,
        "eventsVsControls": evc, "g20Recovery": recovery,
        "runSeconds": (dt.datetime.now() - t0).total_seconds(),
    }
    runs = os.path.join(a.study, "runs", "explore")
    jpath = os.path.join(runs, f"a14-cause-census-{stamp}.json")
    with open(jpath, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, indent=1)
    write_ledger(os.path.join(runs, "a14-ledger-pending.jsonl"), frozen, sens, now)
    mpath = os.path.join(runs, f"a14-cause-census-{stamp}.md")
    with open(mpath, "w", encoding="utf-8") as fh:
        fh.write(render_md(doc, stamp))
    print(f"wrote {jpath}\nwrote {mpath}\nseconds {doc['runSeconds']:.0f}")


def write_ledger(path, frozen, sens, now):
    utc = now.isoformat().replace("+00:00", "Z")
    with open(path, "w", encoding="utf-8") as fh:
        for track, v in (("a14-cause-census", frozen), ("a14-cause-census-split-sensitivity", sens)):
            for r in v["tags"] + v["combos"]:
                name = r.get("tag") or r.get("combo")
                lift = r.get("liftG20")
                fh.write(json.dumps({
                    "id": None, "utc": utc, "track": track, "config": f"A14 {name}",
                    "metric": "P(G20) lift over base rate", "foldValues": r["windowLift"], "pooled": lift,
                    "rows": r["days"],
                    "note": f"pG20={fnum(r['pG20'], 4)} pG50={fnum(r['pG50'], 4)} q={fnum(r['q'], 4)} stable={str(r['stable']).lower()}"}) + "\n")
            for r in v["stratified"]["rows"]:
                if r["tag"] == "noNewsEither":
                    continue
                fh.write(json.dumps({
                    "id": None, "utc": utc, "track": track, "config": f"A14 {r['tag']} (candidate+sample days, A01 weights)",
                    "metric": "P(G20) lift over base rate", "foldValues": [None] * 4, "pooled": r["liftG20"],
                    "rows": r["rows"], "note": f"pG20={fnum(r['pG20'], 4)} pG50={fnum(r['pG50'], 4)} q={fnum(r['q'], 4)} stable=n/a"}) + "\n")


def render_md(doc, stamp):
    fz, se = doc["frozen"], doc["sensitivityNonFrozen"]
    L = []
    w = L.append
    w("# Explore: the cause census - what was true the evening before a gapper day (amendment 14)\n")
    w(f"Train only (D {doc['dFrom']} .. {doc['dTo']}); nothing dated after {doc['lastDateRead']} was read. IP-T632, "
      f"`tools/gapper-study/a14_cause_census.py`, run {doc['computedUtc']} at {doc['gitHead'][:8]}. Descriptive: no verdict, "
      "nothing promoted, no trade simulated. Unweighted (every A13-eligible symbol-day), except the news / insider table.\n")
    w("Evaluation windows (amendment 13 S3 eval ranges, by D): " + "; ".join(f"w{i + 1} {a}..{b}" for i, (a, b) in enumerate(doc["evaluationWindows"])) + "\n")

    hz = fz["hierarchy"]
    w("## Headline\n")
    w(f"- **{fz['N']:,} eligible symbol-days** on {fz['sessions']} sessions. Base rates: P(G20) {pct(fz['base']['G20'])}, "
      f"P(G50) {pct(fz['base']['G50'])}, P(G100) {pct(fz['base']['G100'])} "
      f"({hz['G20']['total']:,} / {hz['G50']['total']:,} / {hz['G100']['total']:,} days).")
    w(f"- **Unexplained remainder (no tag at all):** {pct(hz['G20']['noTagShare'], 1)} of G20 days, {pct(hz['G50']['noTagShare'], 1)} of G50 days, "
      f"{pct(hz['G100']['noTagShare'], 1)} of G100 days (vs {pct(hz['ALL']['noTagShare'], 1)} of all eligible days). "
      f"Without the market-wide hotTape tag (supplementary, not frozen): {pct(hz['G20']['noSymbolTagShare(excl. hotTape, supplementary)'], 1)} / "
      f"{pct(hz['G50']['noSymbolTagShare(excl. hotTape, supplementary)'], 1)} / {pct(hz['G100']['noSymbolTagShare(excl. hotTape, supplementary)'], 1)}. "
      f"Counting only the narrow tags (post hoc, supplementary: leaving out {', '.join(hz['G20']['broadTagsLeftOut'])}, each carried by >= 10% of eligible days): "
      f"{pct(hz['G20']['noNarrowTagShare(supplementary)'], 1)} of G20, {pct(hz['G50']['noNarrowTagShare(supplementary)'], 1)} of G50, "
      f"{pct(hz['G100']['noNarrowTagShare(supplementary)'], 1)} of G100 days carry none (vs {pct(hz['ALL']['noNarrowTagShare(supplementary)'], 1)} of all days).")
    sr = se["splitLikeDaysRemoved"]
    w(f"- NON-FROZEN split sensitivity (IP-T633) removes {sr['days']:,} D days that look like an unrecorded reverse split or a bad print "
      f"({sr['badPrintDays']} bad prints), of which {sr['G20']} G20, {sr['G50']} G50, {sr['G100']} G100; "
      f"its remainder: {pct(se['hierarchy']['G20']['noTagShare'], 1)} / {pct(se['hierarchy']['G50']['noTagShare'], 1)} / {pct(se['hierarchy']['G100']['noTagShare'], 1)}; "
      f"base rates {pct(se['base']['G20'])} / {pct(se['base']['G50'])} / {pct(se['base']['G100'])}.\n")

    def recipe_table(v, title, k=15):
        st = sorted([r for r in v["combos"] if r["stable"]], key=lambda r: -r["liftG20"])
        w(f"### {title}: {len(st)} stable of {len(v['combos'])} combinations tested (>= 500 days)\n")
        w("| recipe | days | P(G20) | P(G50) | P(G100) | lift G20 [95%] | lift G50 | share of G20 days | q | w1..w4 P(G20) |")
        w("|---|---|---|---|---|---|---|---|---|---|")
        for r in st[:k]:
            w(f"| {r['combo'].replace('+', ' + ')} | {r['days']:,} | {pct(r['pG20'], 1)} | {pct(r['pG50'], 1)} | {pct(r['pG100'], 1)} | "
              f"{fnum(r['liftG20'], 1)} [{fnum(r['liftG20Lo'], 1)}, {fnum(r['liftG20Hi'], 1)}] | {fnum(r['liftG50'], 1)} | {pct(r['coverageG20'], 1)} | "
              f"{fnum(r['q'], 4)} | {' / '.join(pct(x, 1) for x in r['windowPG20'])} |")
        w("")

    w("## Recipes: the stable combinations, ranked by lift\n")
    w("Stable = lift lower bound > 2 overall and P(G20) above that window's base rate in all four windows. "
      f"Window base rates (frozen): {' / '.join(pct(x) for x in fz['windowBase'])}.\n")
    recipe_table(fz, "Frozen")
    recipe_table(se, "NON-FROZEN split sensitivity (IP-T633)")

    def tag_table(v, title):
        w(f"### {title}\n")
        w("| tag | known | days | prevalence | P(G20) with / without | P(G50) with / without | P(G100) with | lift G20 [95%] | lift G50 [95%] | share of G20 / G50 / G100 days | q | stable |")
        w("|---|---|---|---|---|---|---|---|---|---|---|---|")
        for r in sorted(v["tags"], key=lambda r: -(r["liftG20"] or 0)):
            w(f"| {r['tag']} | {pct(r['knownShare'], 0)} | {r['days']:,} | {pct(r['prevalence'], 1)} | {pct(r['pG20'], 1)} / {pct(r['pG20Without'], 1)} | "
              f"{pct(r['pG50'], 1)} / {pct(r['pG50Without'], 1)} | {pct(r['pG100'], 2)} | {fnum(r['liftG20'], 1)} [{fnum(r['liftG20Lo'], 1)}, {fnum(r['liftG20Hi'], 1)}] | "
              f"{fnum(r['liftG50'], 1)} [{fnum(r.get('liftG50Lo'), 1)}, {fnum(r.get('liftG50Hi'), 1)}] | "
              f"{pct(r['coverageG20'], 1)} / {pct(r['coverageG50'], 1)} / {pct(r['coverageG100'], 1)} | {fnum(r['q'], 4)} | {'yes' if r['stable'] else ''} |")
        w("")

    w("## Per tag (ranked by lift)\n")
    w("insiderBuy30 is not in this table: Form 4 transaction detail exists only around candidate and sample days, "
      "so it is reported in the stratified table below. Lift = P(G20 | tag) / base rate.\n")
    tag_table(fz, "Frozen")
    tag_table(se, "NON-FROZEN split sensitivity (IP-T633)")

    w("## Unexplained remainder and exclusive hierarchy (frozen tag order)\n")
    w("| first matching tag | all eligible | G20 | G50 | G100 | G20 (sensitivity) | G50 (sensitivity) |")
    w("|---|---|---|---|---|---|---|")
    for i, part in enumerate(hz["ALL"]["exclusive"]):
        t_ = part["tag"]
        w(f"| {t_} | {pct(part['share'], 1)} | {pct(hz['G20']['exclusive'][i]['share'], 1)} | {pct(hz['G50']['exclusive'][i]['share'], 1)} | "
          f"{pct(hz['G100']['exclusive'][i]['share'], 1)} | {pct(se['hierarchy']['G20']['exclusive'][i]['share'], 1)} | {pct(se['hierarchy']['G50']['exclusive'][i]['share'], 1)} |")
    w("")

    w("## Frozen train events versus their matched controls (tag prevalence at D-1, frozen tags)\n")
    ev = doc["eventsVsControls"]
    w("| tag | " + " | ".join(f"{k} events / controls (ratio)" for k in ev) + " |")
    w("|---|" + "---|" * len(ev))
    for i, t_ in enumerate(TAGS):
        cells = []
        for k in ev:
            r = ev[k]["rows"][i]
            cells.append(f"{pct(r['events'], 1)} / {pct(r['controls'], 1)} ({fnum(r['ratio'], 1)})")
        w(f"| {t_} | " + " | ".join(cells) + " |")
    w("\n" + "; ".join(f"{k}: {v['events']:,} events with D-1 bars (of {v['eventsOnFile']:,}), {v['controls']:,} controls" for k, v in ev.items())
      + ". Controls exist only for the study's gapper days (one per day), so Tier50/Tier100 events that are not gapper days have none.\n")
    rc = doc["g20Recovery"]
    w(f"**G20 recovery:** of {rc['withBarsDAndDminus1']:,} train Tier20 events with D and D-1 daily bars, {pct(rc['G20AmongThose'], 1)} have a regular-hours "
      f"D high >= +20% over the D-1 close; {rc['inA14Population']:,} are in the A14 population and {pct(rc['G20AmongPopulationEvents'], 1)} of those are G20. "
      f"Conversely {rc['populationG20DaysThatAreTier20Events']:,} of the {rc['populationG20Days']:,} population G20 days are frozen Tier20 events. "
      f"Events outside the population: {rc['eventsNotInPopulationReasons']}.\n")

    st = fz["stratified"]
    w("## News and insider table (candidate days + amendment 01 sample, A01 weights; frozen population)\n")
    w(f"{st['info']['rows']:,} rows ({st['info']['candidateRows']:,} candidate, {st['info']['sampleRows']:,} sample x 10). Weighted base rates: "
      f"P(G20) {pct(st['info']['baseRates']['G20'])}, P(G50) {pct(st['info']['baseRates']['G50'])}. Rows are A14-eligible days that are also in the frozen U(D) "
      "candidate census or the 10% sample.\n")
    w("| tag | rows | weighted prevalence | P(G20) with / without | P(G50) with / without | lift G20 [95%] | share of G20 / G50 days | q |")
    w("|---|---|---|---|---|---|---|---|")
    for r in st["rows"]:
        w(f"| {r['tag']} | {r['rows']:,} | {pct(r['weightedPrevalence'], 1)} | {pct(r['pG20'], 1)} / {pct(r['pG20Without'], 1)} | {pct(r['pG50'], 1)} / {pct(r['pG50Without'], 1)} | "
          f"{fnum(r['liftG20'], 1)} [{fnum(r['liftG20Lo'], 1)}, {fnum(r['liftG20Hi'], 1)}] | {pct(r['coverageG20'], 1)} / {pct(r['coverageG50'], 1)} | {fnum(r['q'], 4)} |")
    w("")

    w("## Sanity checks\n")
    w("Eligible symbol-days per year (frozen): " + "; ".join(f"{y}: {v['days']:,} days on {v['sessions']} sessions, G20 {v['G20']:,}, G50 {v['G50']:,}, G100 {v['G100']:,}"
                                                         for y, v in fz["perYear"].items()) + "\n")
    w(f"Eligibility funnel: {doc['funnel']}\n")
    w(f"Split convention check: {doc['splitConvention']}\n")
    w(f"CIK: {doc['cik']}. Insider join: {doc['insiderJoin']}. hotTape: {fz['hotTape']}.\n")
    w("### The 20 largest D high / D-1 close ratios (frozen population)\n")
    w("| symbol | D | S close | D open | D high | high/close | open/close | D vol / 20d avg | split-like |")
    w("|---|---|---|---|---|---|---|---|---|")
    for r in fz["top20"]:
        w(f"| {r['symbol']} | {r['D']} | {fnum(r['S close'], 4)} | {fnum(r['D open'], 4)} | {fnum(r['D high'], 4)} | {fnum(r['highOverClose'], 2)} | "
          f"{fnum(r['openOverClose'], 2)} | {fnum(r['D volume / prior 20-session avg'], 2)} | {'yes' if r['splitLike'] else ''} |")
    w("")
    w("### NON-FROZEN split-like D days removed (largest 25)\n")
    w(", ".join(f"{e['symbol']} {e['D']} (open {fnum(e['openOverClose'], 1)}x)" for e in sr["examples"]) + "\n")
    w("Named-symbol check (population days with D high >= 2x the D-1 close for the symbols flagged by the A13 run, plus SLND, OLOX, TPST): "
      + ", ".join(f"{e['symbol']} {e['D']} ({fnum(e['highOverClose'], 1)}x, {'removed' if e['removedBySensitivity'] else 'kept'})" for e in se["namedSymbolsCheck"]) + "\n")
    w("## What this says\n")
    w("_(filled in by hand after the run; see the report text committed with this file)_\n")
    return "\n".join(L) + "\n"


if __name__ == "__main__":
    main()
