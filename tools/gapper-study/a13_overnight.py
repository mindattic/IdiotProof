"""Gapper study amendment 13 (IP-T632): the close-to-open hold (H31 primary, H32 descriptive). TRAIN only.

Implements amendment-13.json exactly as registered (the JSON governs). Reads only the two read-only CSV
exports of the frozen research tables (GapperStudyDailyBars, GapperStudySplits); never the database, the
Hub or Alpaca. Refuses any signal day after the registered signalDays.to (2024-10-30): validation and the
holdout are never read for signals or results. Writes runs/explore/a13-overnight-<stamp>.md/.json and the
nine ledger trials into runs/explore/a13-ledger-pending.jsonl (not ledger.jsonl). Research only: nothing
here arms, sizes, places or exits an order.

Usage: python -I tools/gapper-study/a13_overnight.py [--daily PATH] [--splits PATH] [--study DIR] [--trials N]
"""
import argparse
import datetime as dt
import gc
import hashlib
import json
import math
import os
import subprocess
import sys
import time
from statistics import NormalDist

import numpy as np
import pandas as pd

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_DAILY = r"D:\IdiotProofData\gapper-study\a13\daily.csv"
DEFAULT_SPLITS = r"D:\IdiotProofData\gapper-study\a13\splits.csv"
DEFAULT_STUDY = os.path.join(REPO, "docs", "research", "gapper-study-2026-11")
LOAD_CUTOFF = "2024-11-15"       # bars after this are dropped while reading (exits need at most S + 5 sessions)
MIN_SYMBOLS_PER_SESSION = 500    # a calendar date with fewer traded symbols is not a session
FEATURES = ["ret1", "ret2", "ret5", "ret20", "gapS", "intradayS", "rangeS", "closeLocationS", "volRatioS", "volRatioS1",
            "logPrice", "logAdv20Dollars", "sigma20", "maxRet10", "maxRet60", "sessionsSinceUp50", "countUp20Last20",
            "corwinSchultz20", "dayOfWeek", "daysSinceReverseSplit", "lastReverseSplitRatio", "sessionsSinceFirstBar",
            "eligibleSymbolsUp20OnS"]
EPOCH = np.datetime64("1970-01-01", "D")


def log(msg):
    print(f"[{dt.datetime.now().strftime('%H:%M:%S')}] {msg}", flush=True)


def day_num(s):
    return int((np.datetime64(s, "D") - EPOCH).astype(np.int64))


def day_str(n):
    return str(EPOCH + np.timedelta64(int(n), "D"))


# ---------------------------------------------------------------- loading

def load_daily(path, cutoff):
    from pandas.api.types import union_categoricals
    names = ["sym", "date", "o", "h", "l", "c", "v"]
    dtypes = {"sym": str, "date": str, "o": np.float32, "h": np.float32, "l": np.float32, "c": np.float32, "v": np.float64}
    parts, total, zero_vol = [], 0, 0
    for ch in pd.read_csv(path, header=None, names=names, dtype=dtypes, chunksize=1_500_000, engine="c"):
        total += len(ch)
        ch = ch[ch["date"] <= cutoff]
        zero_vol += int((ch["v"] <= 0).sum())
        ch = ch[ch["v"] > 0]
        out = pd.DataFrame({
            "sym": ch["sym"].astype("category"),
            "d": (pd.to_datetime(ch["date"], format="%Y-%m-%d").values.astype("datetime64[D]") - EPOCH).astype(np.int32),
            "o": ch["o"].values, "h": ch["h"].values, "l": ch["l"].values, "c": ch["c"].values,
            "v": ch["v"].values.astype(np.float32),
        })
        parts.append(out)
        del ch
        gc.collect()
    cats = union_categoricals([p["sym"] for p in parts], ignore_order=True)
    sym = pd.Categorical(cats, categories=sorted(cats.categories))
    df = pd.concat([p.drop(columns="sym") for p in parts], ignore_index=True)
    del parts
    gc.collect()
    df["sym"] = sym
    return df, total, zero_vol


def load_splits(path):
    s = pd.read_csv(path, header=None, names=["sym", "ex", "old", "new"], dtype={"sym": str, "ex": str, "old": float, "new": float})
    bad = int(((s["old"] <= 0) | (s["new"] <= 0)).sum())
    s = s[(s["old"] > 0) & (s["new"] > 0)].drop_duplicates()
    s["exn"] = s["ex"].map(day_num)
    s["pf"] = s["old"] / s["new"]   # SplitEvent.PriceFactor = OldRate / NewRate: old-basis price x pf = new-basis price
    by = {}
    for sym, g in s.sort_values(["sym", "exn"]).groupby("sym", sort=False):
        by[sym] = (g["exn"].values.astype(np.int64), g["pf"].values.astype(np.float64))
    return by, len(s), bad


# ---------------------------------------------------------------- per-symbol panel

def rolling_sum(dense, w):
    c = np.concatenate([[0.0], np.cumsum(dense)])
    idx = np.arange(1, len(dense) + 1)
    return c[idx] - c[np.maximum(idx - w, 0)]


def rolling_max(dense, w):
    pad = np.concatenate([np.full(w - 1, -np.inf), dense])
    return np.lib.stride_tricks.sliding_window_view(pad, w).max(axis=1)


def symbol_rows(sym, d, o, h, l, c, v, splits, sess_index, sess_days, cfg):
    """Every eligible signal day S of one symbol, with its features, cost and trade. Bars ascending, volume > 0."""
    n = len(d)
    if n < 2:
        return None, 0
    off = d - sess_days[0]
    si = np.where((off >= 0) & (off < len(sess_index)), sess_index[np.clip(off, 0, len(sess_index) - 1)], -1)
    keep = si >= 0
    if not keep.all():
        d, o, h, l, c, v, si = d[keep], o[keep], h[keep], l[keep], c[keep], v[keep], si[keep]
        n = len(d)
        if n < 2:
            return None, 0
    o, h, l, c, v = (x.astype(np.float64) for x in (o, h, l, c, v))
    ex, pf = splits if splits is not None else (np.zeros(0, np.int64), np.zeros(0))

    # back-adjust every bar to the latest basis: ratios between any two bars are then split-consistent
    k_after = np.searchsorted(ex, d, side="right")            # splits with ExDate <= bar date
    suffix = np.concatenate([np.cumprod(pf[::-1])[::-1], [1.0]]) if len(pf) else np.ones(1)
    F = suffix[k_after]                                       # product of pf with ExDate > bar date
    ao, ah, al, ac, av = o * F, h * F, l * F, c * F, v / F

    prev_c = np.concatenate([[np.nan], ac[:-1]])
    r1 = ac / prev_c - 1.0
    lr = np.log(ac / prev_c)

    # frozen unadjusted-split guard: a raw close-to-close move outside [maxDailyDown, maxDailyUp] not explained by a split
    raw_move = np.concatenate([[0.0], c[1:] / c[:-1] - 1.0])
    explained = np.concatenate([[False], k_after[1:] > k_after[:-1]])
    flagged = ((raw_move > cfg["guardUp"]) | (raw_move < cfg["guardDown"])) & ~explained
    flagged[0] = False
    flag_days = d[flagged]

    # dense session arrays from the first bar's session to the last
    base = si[0]
    L = si[-1] - base + 1
    pos = si - base
    present = np.zeros(L)
    present[pos] = 1.0
    dollars = np.zeros(L)
    dollars[pos] = c * v
    vol = np.zeros(L)
    vol[pos] = av
    cnt20 = rolling_sum(present, 20)
    dol20 = rolling_sum(dollars, 20)
    vol20 = rolling_sum(vol, 20)

    # classic Corwin-Schultz pair estimates (TradingCosts.CorwinSchultzSpread), keyed at the first bar of the pair
    kcs = 3 - 2 * math.sqrt(2)
    h1, l1, h2, l2 = ah[:-1], al[:-1], ah[1:], al[1:]
    okp = (l1 > 0) & (l2 > 0)
    with np.errstate(divide="ignore", invalid="ignore"):
        beta = np.log(h1 / l1) ** 2 + np.log(h2 / l2) ** 2
        gamma = np.log(np.maximum(h1, h2) / np.minimum(l1, l2)) ** 2
        alpha = (np.sqrt(2 * beta) - np.sqrt(beta)) / kcs - np.sqrt(gamma / kcs)
        s_pair = np.maximum(0.0, 2 * (np.exp(alpha) - 1) / (1 + np.exp(alpha)))
    cs_sum = np.zeros(L)
    cs_cnt = np.zeros(L)
    cs_sum[pos[:-1][okp]] = s_pair[okp]
    cs_cnt[pos[:-1][okp]] = 1.0
    # window ending at bar t (pos p): pairs whose first bar is at pos p-19..p-1
    cs_csum = np.concatenate([[0.0], np.cumsum(cs_sum)])
    cs_ccnt = np.concatenate([[0.0], np.cumsum(cs_cnt)])

    r1d = np.full(L, np.nan)
    r1d[pos] = r1
    max10 = rolling_max(np.where(np.isnan(r1d), -np.inf, r1d), 10)
    max60 = rolling_max(np.where(np.isnan(r1d), -np.inf, r1d), 60)
    up20 = rolling_sum(np.where(r1d >= 0.20, 1.0, 0.0), 20)
    up50_pos = np.where(r1d >= 0.50, np.arange(L), -10 ** 9)
    last_up50 = np.maximum.accumulate(up50_pos)
    ffc = np.full(L, np.nan)
    ffc[pos] = ac
    ffc = pd.Series(ffc).ffill().values

    # sigma: GapperCostModel.Sigma over the last 21 closes through S (sample stdev of up to 20 log returns, >= 3 closes)
    lr0 = np.where(np.isnan(lr), 0.0, lr)
    c1 = np.concatenate([[0.0], np.cumsum(lr0)])
    c2 = np.concatenate([[0.0], np.cumsum(lr0 * lr0)])

    rev_mask = pf > 1.0
    rev_ex, rev_pf = ex[rev_mask], pf[rev_mask]

    t_all = np.arange(n)
    in_range = (d >= cfg["fitFrom"]) & (d <= cfg["sigTo"]) & (t_all >= 1)
    ts = t_all[in_range]
    if len(ts) == 0:
        return None, 0
    p = pos[ts]
    cnt = cnt20[p]
    adv_d = dol20[p] / np.maximum(cnt, 1)
    adv_s = vol20[p] / np.maximum(cnt, 1)
    raw_close = c[ts]

    # exit: the symbol's next bar within 5 sessions, else -100%
    nxt = ts + 1
    has_next = nxt < n
    nxt_c = np.minimum(nxt, n - 1)
    gap_sess = np.where(has_next, si[nxt_c] - si[ts], 10 ** 6)
    exits = gap_sess <= cfg["exitSessions"]
    d_exit = np.where(exits, d[nxt_c], sess_days_of(si[ts] + cfg["exitSessions"], cfg))
    gross = np.where(exits, ao[nxt_c] / ac[ts] - 1.0, -1.0)

    # split exclusion: no split ex-date from S-25 calendar days through D
    lo = np.searchsorted(ex, d[ts] - cfg["splitBefore"], side="left")
    hi = np.searchsorted(ex, d_exit, side="right")
    split_hit = hi > lo
    glo = np.searchsorted(flag_days, d[ts] - cfg["guardDays"], side="left")
    ghi = np.searchsorted(flag_days, d[ts], side="right")
    guard_hit = ghi > glo
    guard_own = flagged[ts]

    elig = ((raw_close >= cfg["pxMin"]) & (raw_close < cfg["pxMax"]) & (adv_d >= cfg["advMin"]) & (cnt >= cfg["minSessions"])
            & ~split_hit & ~guard_hit)
    # population before the split/guard exclusions (for counts only)
    pre = (raw_close >= cfg["pxMin"]) & (raw_close < cfg["pxMax"]) & (adv_d >= cfg["advMin"]) & (cnt >= cfg["minSessions"])
    stats = {"pre": int(pre.sum()), "split": int((pre & split_hit).sum()), "guard": int((pre & ~split_hit & guard_hit).sum()),
             "guardOwnS": int((pre & ~split_hit & guard_hit & guard_own).sum())}
    if not elig.any():
        return None, stats
    ts, p = ts[elig], p[elig]
    sel = elig
    cnt, adv_d, adv_s, raw_close = cnt[sel], adv_d[sel], adv_s[sel], raw_close[sel]
    gross, d_exit, exits = gross[sel], d_exit[sel], exits[sel]
    nxt_c = nxt_c[sel]

    cs_n = cs_ccnt[p] - cs_ccnt[np.maximum(p - 19, 0)]
    cs = np.where(cs_n > 0, (cs_csum[p] - cs_csum[np.maximum(p - 19, 0)]) / np.maximum(cs_n, 1), np.nan)
    a = np.maximum(1, ts - 19)
    m = ts - a + 1
    sm = c1[ts + 1] - c1[a]
    sq = c2[ts + 1] - c2[a]
    var = np.where(m >= 2, (sq - sm * sm / np.maximum(m, 1)) / np.maximum(m - 1, 1), np.nan)
    sigma = np.sqrt(np.maximum(var, 0.0))
    sigma = np.where(m >= 2, sigma, np.nan)

    shares = cfg["notional"] / ac[ts]                      # S basis: 1000 / S close (adjusted basis cancels in shares/adv)
    with np.errstate(divide="ignore", invalid="ignore"):
        impact = np.where((shares > 0) & (adv_s > 0) & (sigma > 0), cfg["impactConstant"] * sigma * np.sqrt(shares / adv_s), 0.0)
    spread_term = np.maximum(np.where(np.isnan(cs), 0.0, cs), cfg["floor"])
    cost = spread_term + 2 * impact

    pc = prev_c[ts]
    feats = {}
    feats["ret1"] = ac[ts] / pc - 1
    for kk, name in ((2, "ret2"), (5, "ret5"), (20, "ret20")):
        back = p - kk
        feats[name] = np.where(back >= 0, ac[ts] / ffc[np.maximum(back, 0)] - 1, np.nan)
    feats["gapS"] = ao[ts] / pc - 1
    feats["intradayS"] = ac[ts] / ao[ts] - 1
    feats["rangeS"] = np.where(al[ts] > 0, ah[ts] / al[ts] - 1, np.nan)
    rng_ = ah[ts] - al[ts]
    feats["closeLocationS"] = np.where(rng_ > 0, (ac[ts] - al[ts]) / np.where(rng_ > 0, rng_, 1), 0.5)
    feats["volRatioS"] = av[ts] / adv_s
    pprev = pos[ts - 1]
    adv_prev = vol20[pprev] / np.maximum(cnt20[pprev], 1)
    feats["volRatioS1"] = av[ts - 1] / adv_prev
    feats["logPrice"] = np.log(raw_close)
    feats["logAdv20Dollars"] = np.log(adv_d)
    feats["sigma20"] = sigma
    feats["maxRet10"] = np.where(np.isfinite(max10[p]), max10[p], np.nan)
    feats["maxRet60"] = np.where(np.isfinite(max60[p]), max60[p], np.nan)
    feats["sessionsSinceUp50"] = np.minimum(np.where(last_up50[p] >= 0, p - last_up50[p], 250), 250).astype(np.float64)
    feats["countUp20Last20"] = up20[p]
    feats["corwinSchultz20"] = cs
    feats["dayOfWeek"] = ((d[ts] + 3) % 7).astype(np.float64)
    if len(rev_ex):
        kr = np.searchsorted(rev_ex, d[ts], side="right") - 1
        has = kr >= 0
        feats["daysSinceReverseSplit"] = np.where(has, np.minimum(d[ts] - rev_ex[np.maximum(kr, 0)], 365), 366).astype(np.float64)
        feats["lastReverseSplitRatio"] = np.where(has, rev_pf[np.maximum(kr, 0)], 0.0)
    else:
        feats["daysSinceReverseSplit"] = np.full(len(ts), 366.0)
        feats["lastReverseSplitRatio"] = np.zeros(len(ts))
    feats["sessionsSinceFirstBar"] = np.minimum(si[ts] - si[0], 500).astype(np.float64)

    out = {
        "S": d[ts].astype(np.int32), "sess": si[ts].astype(np.int32), "gross": gross, "exited": exits, "Dexit": d_exit.astype(np.int32),
        "cost": cost, "cs": cs.astype(np.float32), "impact": impact.astype(np.float32), "sClose": raw_close.astype(np.float32),
        "sOpen": o[ts].astype(np.float32), "sHigh": h[ts].astype(np.float32), "sLow": l[ts].astype(np.float32), "sVol": v[ts].astype(np.float32),
        "dOpenRaw": np.where(exits, o[nxt_c], np.nan).astype(np.float32), "adjFactor": np.where(exits, F[nxt_c] / F[ts], np.nan).astype(np.float32),
        "prevCloseRaw": c[ts - 1].astype(np.float32),
    }
    for k_, v_ in feats.items():
        out[k_] = v_.astype(np.float32)
    return out, stats


def sess_days_of(idx, cfg):
    arr = cfg["sessDaysArr"]
    return arr[np.minimum(idx, len(arr) - 1)]


# ---------------------------------------------------------------- statistics

def cluster_boot(day_sum, day_cnt, draws, seed, ci):
    """WeightedPopulation.Mean with unit weights: resample whole days, ratio of summed values to summed trades."""
    nd = len(day_sum)
    est = day_sum.sum() / day_cnt.sum() if day_cnt.sum() > 0 else math.nan
    if nd < 5:
        return est, math.nan, math.nan
    rng = np.random.default_rng(seed)
    boot = np.empty(draws)
    step = max(1, 4_000_000 // nd)
    for b0 in range(0, draws, step):
        b1 = min(draws, b0 + step)
        idx = rng.integers(0, nd, size=(b1 - b0, nd))
        boot[b0:b1] = day_sum[idx].sum(axis=1) / day_cnt[idx].sum(axis=1)
    boot.sort()
    tail = (1 - ci) / 2
    return est, boot[int(math.floor(tail * (draws - 1)))], boot[int(math.ceil((1 - tail) * (draws - 1)))]


def by_day(days, values):
    u, inv = np.unique(days, return_inverse=True)
    s = np.bincount(inv, weights=values)
    c = np.bincount(inv).astype(np.float64)
    return u, s, c


def spa_test(series, resamples, block, seed):
    """Port of RealityCheck.Run (Hansen 2005 consistent SPA, Politis-Romano stationary bootstrap). series: k x n."""
    k, n = series.shape
    means = series.mean(axis=1)
    rng = np.random.default_rng(seed)
    p = 1.0 / block
    sqrt_n = math.sqrt(n)
    t = rng.integers(0, n, size=resamples)
    idx = np.empty((resamples, n), dtype=np.int32)
    idx[:, 0] = t
    for i in range(1, n):
        restart = rng.random(resamples) < p
        fresh = rng.integers(0, n, size=resamples)
        t = np.where(restart, fresh, (t + 1) % n)
        idx[:, i] = t
    boot = np.empty((resamples, k))
    for j in range(k):
        boot[:, j] = series[j][idx].mean(axis=1)
    z = sqrt_n * (boot - means)
    omega = np.sqrt(np.maximum(1e-24, z.var(axis=0)))
    white_stat = (sqrt_n * means).max()
    spa_stat = max(0.0, (sqrt_n * means / omega).max())
    thr = -math.sqrt(2 * math.log(math.log(max(n, 3))))
    centered = np.where(sqrt_n * means / omega >= thr, means, 0.0)
    v_white = (sqrt_n * (boot - means)).max(axis=1)
    v_spa = np.maximum(0.0, (sqrt_n * (boot - centered) / omega).max(axis=1))
    return {"configs": k, "periods": n, "best": int(means.argmax()), "bestMean": float(means.max()),
            "whiteP": float((v_white >= white_stat).mean()), "spaP": float((v_spa >= spa_stat).mean()), "resamples": resamples, "meanBlockLength": block}


ND = NormalDist()


def moments(x):
    n = len(x)
    if n == 0:
        return 0, 0.0, 0.0, 0.0, 3.0
    m = float(np.mean(x))
    dd = x - m
    m2, m3, m4 = float(np.mean(dd ** 2)), float(np.mean(dd ** 3)), float(np.mean(dd ** 4))
    sd = math.sqrt(m2)
    return n, m, sd, (m3 / sd ** 3 if sd > 0 else 0.0), (m4 / m2 ** 2 if m2 > 0 else 3.0)


def sharpe(mo):
    return mo[1] / mo[2] if mo[2] > 0 else 0.0


def expected_max_sharpe(trials, var_sr):
    if trials <= 1:
        return 0.0
    g = 0.5772156649015329
    sd = math.sqrt(max(0.0, var_sr))
    return sd * ((1 - g) * ND.inv_cdf(1 - 1.0 / trials) + g * ND.inv_cdf(1 - 1.0 / (trials * math.e)))


def psr(mo, bench):
    n, _, sd, sk, ku = mo
    if n < 2 or sd <= 0:
        return math.nan
    sr = sharpe(mo)
    den = 1 - sk * sr + (ku - 1) / 4 * sr * sr
    if den <= 0:
        den = 1e-12
    return ND.cdf((sr - bench) * math.sqrt(n - 1) / math.sqrt(den))


# ---------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--daily", default=DEFAULT_DAILY)
    ap.add_argument("--splits", default=DEFAULT_SPLITS)
    ap.add_argument("--study", default=DEFAULT_STUDY)
    ap.add_argument("--trials", type=int, default=5676, help="DSR trial count N: the ledger total after this run")
    a = ap.parse_args()
    t0 = time.time()

    with open(os.path.join(a.study, "amendment-13.json"), encoding="utf-8") as f:
        a13 = json.load(f)
    with open(os.path.join(a.study, "params.json"), encoding="utf-8") as f:
        params = json.load(f)
    el = a13["eligibilityAtS"]
    sig_from, sig_to = el["signalDays"]["from"], el["signalDays"]["to"]
    train = params["splits"]["train"]
    if sig_to > train["to"] or sig_to >= params["splits"]["validation"]["from"]:
        sys.exit("refusing: signal days reach past train")
    guard = params["universe"]["unadjustedSplitGuard"]
    st = params["statistics"]
    impact_constant = params["costModel"]["impact"]["impactConstant"]
    notional = params["costModel"]["impact"]["tradeNotionalUsd"]
    s3 = a13["signals"]["S3"]
    h31 = next(h for h in a13["hypotheses"] if h["id"] == "H31")
    me = h31["minEffect"]
    list_sizes = a13["signals"]["listSizes"]
    folds = s3["folds"]
    embargo = s3["embargoSessions"]
    if s3["features"][15] != "sessionsSinceUp50 (cap 250)" or len(s3["features"]) != len(FEATURES):
        sys.exit("feature list differs from the registration")
    cost_floor = 0.010   # amendment-13 costModel.roundTrip: max(CS, 0.010)
    draws, seed, ci = st["bootstrapDraws"], st["seed"], st["ciLevel"]

    # ---- data
    log("reading splits")
    splits, n_splits, bad_splits = load_splits(a.splits)
    log(f"reading daily bars (dates <= {LOAD_CUTOFF})")
    with open(a.daily, "rb") as f:
        sha = hashlib.sha256()
        for blk in iter(lambda: f.read(1 << 24), b""):
            sha.update(blk)
    daily_sha = sha.hexdigest()
    bars, total_rows, zero_vol = load_daily(a.daily, LOAD_CUTOFF)
    log(f"{total_rows:,} rows in the file; kept {len(bars):,} (volume > 0, <= {LOAD_CUTOFF}); dropped {zero_vol:,} zero-volume rows")

    counts = np.bincount(bars["d"].values - bars["d"].values.min())
    dmin = int(bars["d"].values.min())
    sess_days = np.flatnonzero(counts >= MIN_SYMBOLS_PER_SESSION) + dmin
    thin_dates = int(((counts > 0) & (counts < MIN_SYMBOLS_PER_SESSION)).sum())
    sess_index = np.full(int(sess_days[-1] - sess_days[0] + 1) + 400, -1, dtype=np.int64)
    sess_index[sess_days - sess_days[0]] = np.arange(len(sess_days))
    log(f"{len(sess_days)} sessions {day_str(sess_days[0])}..{day_str(sess_days[-1])}; {thin_dates} thin dates dropped")

    cfg = {"guardUp": guard["maxDailyUp"], "guardDown": guard["maxDailyDown"], "guardDays": guard["lookbackDays"],
           "fitFrom": day_num(sig_from), "sigTo": day_num(sig_to), "exitSessions": 5, "splitBefore": 25,
           "pxMin": el["closeMinInclusive"], "pxMax": el["closeMaxExclusive"], "advMin": el["adv20DollarMin"],
           "minSessions": el["minSessionsOfLast20"], "notional": notional, "impactConstant": impact_constant, "floor": cost_floor,
           "sessDaysArr": sess_days}

    bars.sort_values(["sym", "d"], inplace=True, kind="mergesort")
    codes = bars["sym"].cat.codes.values
    cats = bars["sym"].cat.categories
    dd = bars["d"].values.astype(np.int64)
    O, H, Lo, C, V = (bars[k].values for k in ("o", "h", "l", "c", "v"))
    bounds = np.flatnonzero(np.diff(codes)) + 1
    starts = np.concatenate([[0], bounds])
    ends = np.concatenate([bounds, [len(codes)]])
    del bars
    gc.collect()

    log(f"building eligible signal days for {len(starts):,} symbols")
    chunks, syms = [], []
    agg = {"pre": 0, "split": 0, "guard": 0, "guardOwnS": 0}
    for i, (s0, s1) in enumerate(zip(starts, ends)):
        sym = cats[codes[s0]]
        out, stt = symbol_rows(sym, dd[s0:s1], O[s0:s1], H[s0:s1], Lo[s0:s1], C[s0:s1], V[s0:s1], splits.get(sym), sess_index, sess_days, cfg)
        if stt:
            for k in agg:
                agg[k] += stt[k]
        if out is not None:
            chunks.append(out)
            syms.append(np.full(len(out["S"]), codes[s0], dtype=np.int32))
        if (i + 1) % 2000 == 0:
            log(f"  {i + 1:,} symbols")
    del O, H, Lo, C, V, dd, codes
    gc.collect()
    df = pd.DataFrame(index=pd.RangeIndex(sum(len(c_["S"]) for c_ in chunks)))
    for k in list(chunks[0].keys()):
        df[k] = np.concatenate([c_.pop(k) for c_ in chunks])
    df["symCode"] = np.concatenate(syms)
    del chunks, syms
    gc.collect()
    df["sym"] = pd.Categorical.from_codes(df["symCode"].values, categories=cats)
    if df["S"].max() > day_num(sig_to):
        sys.exit("refusing: a signal day after signalDays.to was produced")
    up20 = df.groupby("S")["ret1"].apply(lambda x: float((x >= 0.20).sum()))
    df["eligibleSymbolsUp20OnS"] = df["S"].map(up20).astype(np.float32)
    df["label"] = (df["gross"] >= 0.10).astype(np.int8)
    df.sort_values(["S", "symCode"], inplace=True, kind="mergesort")
    df.reset_index(drop=True, inplace=True)
    log(f"{len(df):,} eligible symbol-days; {df['S'].nunique()} signal days")

    # windows (the four evaluation windows by signal day)
    win = np.full(len(df), -1, dtype=np.int8)
    for k, fo in enumerate(folds):
        m = (df["S"] >= day_num(fo["eval"][0])) & (df["S"] <= day_num(fo["eval"][1]))
        win[m.values] = k
    df["win"] = win
    eval_days = sess_days[(sess_days >= day_num(folds[0]["eval"][0])) & (sess_days <= day_num(folds[-1]["eval"][1]))]

    # ---- S3 (LightGBM, rolling origin, 5-session embargo)
    import lightgbm as lgb
    df["p3"] = np.nan
    fold_info = []
    X_all = df[FEATURES].values.astype(np.float32)
    for k, fo in enumerate(folds):
        ev0 = day_num(fo["eval"][0])
        ev_sess = int(np.searchsorted(sess_days, ev0, side="left"))
        fit = ((df["S"] >= day_num(fo["fit"][0])) & (df["S"] <= day_num(fo["fit"][1])) & (df["sess"] < ev_sess - embargo)).values
        ev = (df["win"] == k).values
        model = lgb.LGBMClassifier(objective="binary", verbose=-1, **s3["params"])
        model.fit(X_all[fit], df["label"].values[fit])
        df.loc[ev, "p3"] = model.predict_proba(X_all[ev])[:, 1]
        y_ev = df["label"].values[ev]
        pr = df["p3"].values[ev]
        auc = auc_score(y_ev, pr)
        fold_info.append({"fold": k + 1, "fitRows": int(fit.sum()), "fitPositives": int(df["label"].values[fit].sum()),
                          "fitLastS": day_str(df["S"].values[fit].max()), "evalRows": int(ev.sum()), "evalPositives": int(y_ev.sum()), "evalAuc": auc})
        log(f"S3 fold {k + 1}: fit {fit.sum():,} rows (last S {day_str(df['S'].values[fit].max())}), eval {ev.sum():,}, AUC {auc:.3f}")
        del model
        gc.collect()
    del X_all
    gc.collect()

    # ---- lists
    s1_mask = (df["volRatioS"] >= 3.0).values
    s2_mask = s1_mask & (df["ret1"] >= 0.20).values & (df["sClose"] >= df["sLow"] + 0.75 * (df["sHigh"] - df["sLow"])).values

    def top_lists(mask, score, nmax, rows):
        sub = df.loc[mask & rows, ["S", "symCode"]].copy()
        sub["score"] = score[mask & rows]
        sub = sub[np.isfinite(sub["score"].values)]
        sub.sort_values(["S", "score", "symCode"], ascending=[True, False, True], inplace=True, kind="mergesort")
        sub["rank"] = sub.groupby("S").cumcount() + 1
        return sub[sub["rank"] <= nmax]

    eval_rows = (df["win"] >= 0).values
    train_rows = np.ones(len(df), dtype=bool)
    ret1 = df["ret1"].values.astype(np.float64)
    lists = {"S1": top_lists(s1_mask, ret1, max(list_sizes), eval_rows),
             "S2": top_lists(s2_mask, ret1, max(list_sizes), eval_rows),
             "S3": top_lists(np.isfinite(df["p3"].values), df["p3"].values, max(list_sizes), eval_rows)}
    train_lists = {"S1": top_lists(s1_mask, ret1, max(list_sizes), train_rows), "S2": top_lists(s2_mask, ret1, max(list_sizes), train_rows)}

    gross_all = df["gross"].values
    cost_all = df["cost"].values
    S_all = df["S"].values
    win_all = df["win"].values

    def month_of(days):
        return (EPOCH + days.astype("timedelta64[D]")).astype("datetime64[M]").astype(str)

    def trade_stats(rows_idx, with_lomo=True):
        g = gross_all[rows_idx] * 100
        cst = cost_all[rows_idx] * 100
        net = g - cst
        days = S_all[rows_idx]
        res = {"trades": int(len(rows_idx)), "days": int(len(np.unique(days)))}
        if len(rows_idx) == 0:
            return res, net
        u, s, c = by_day(days, net)
        est, low, high = cluster_boot(s, c, draws, seed, ci)
        res.update({"meanNet": est, "low": low, "high": high, "meanGross": float(g.mean()), "meanCost": float(cst.mean()),
                    "medianCost": float(np.median(cst))})
        _, s2, c2 = by_day(days, g - 2 * cst)
        res["net2x"] = s2.sum() / c2.sum()
        res["net2xLow"], res["net2xHigh"] = cluster_boot(s2, c2, draws, seed, ci)[1:]
        _, s3_, c3 = by_day(days, g - 3.0)
        res["netFlat3"] = s3_.sum() / c3.sum()
        res["netFlat3Low"], res["netFlat3High"] = cluster_boot(s3_, c3, draws, seed, ci)[1:]
        w = win_all[rows_idx]
        res["windows"] = [float(net[w == k].mean()) if (w == k).any() else None for k in range(len(folds))]
        res["windowTrades"] = [int((w == k).sum()) for k in range(len(folds))]
        res["exitedMinus100"] = int((gross_all[rows_idx] <= -0.999999).sum())
        if with_lomo:
            mon = month_of(u)
            lows = []
            for mm in np.unique(mon):
                keep = mon != mm
                lows.append((mm, cluster_boot(s[keep], c[keep], draws, seed, ci)[1]))
            worst = min(lows, key=lambda z: z[1])
            res["lomoLow"], res["lomoWorstMonth"] = worst[1], worst[0]
        return res, net

    configs = []
    for sg in ("S1", "S2", "S3"):
        for nn in list_sizes:
            lst = lists[sg]
            idx = lst.index[lst["rank"] <= nn].values
            configs.append({"name": f"{sg} top{nn}", "signal": sg, "n": nn, "idx": idx})
    log("statistics for the nine configurations")
    series = []
    for cf in configs:
        res, net = trade_stats(cf["idx"])
        cf["res"], cf["net"] = res, net
        days = S_all[cf["idx"]]
        u, s, c = by_day(days, net)
        daily = np.zeros(len(eval_days))
        pos_ = np.searchsorted(eval_days, u)
        daily[pos_] = s / c
        series.append(daily)
        log(f"  {cf['name']}: {res['trades']} trades, {res['days']} days, net {res.get('meanNet', float('nan')):+.3f}% [{res.get('low', float('nan')):+.3f}, {res.get('high', float('nan')):+.3f}]")
    spa = spa_test(np.vstack(series), draws, 5.0, seed)
    log(f"SPA p = {spa['spaP']:.4f} (White {spa['whiteP']:.4f})")

    mos = [moments(cf["net"] / 100) for cf in configs]
    srs = [sharpe(mo) for mo in mos]
    var_sr = float(np.var(srs, ddof=1)) if len(srs) > 1 else 0.0
    emax = expected_max_sharpe(a.trials, var_sr)
    for cf, mo in zip(configs, mos):
        cf["res"]["sharpe"] = sharpe(mo)
        cf["res"]["dsr"] = psr(mo, emax) if mo[0] >= 2 else math.nan

    eligible_sel = [cf for cf in configs if cf["res"]["trades"] >= me["minTrades"] and cf["res"]["days"] >= me["minDays"]]
    selected = max(eligible_sel, key=lambda cf: cf["res"]["low"]) if eligible_sel else None
    failures = []
    if selected is None:
        failures.append("no configuration has >= 150 trades on >= 40 days")
    else:
        r = selected["res"]
        if not (r["meanNet"] >= me["meanNetPctPerTrade"]):
            failures.append(f"mean net {r['meanNet']:+.2f}% < +{me['meanNetPctPerTrade']:.1f}%")
        if not (r["low"] > me["clusteredLowerBoundAbove"]):
            failures.append(f"day-clustered lower bound {r['low']:+.2f}% not > 0")
        pos_w = sum(1 for x in r["windows"] if x is not None and x > 0)
        if pos_w < 3:
            failures.append(f"{pos_w} of 4 windows positive < 3")
        if not (r["lomoLow"] > me["leaveOneMonthOutLowerBoundAbove"]):
            failures.append(f"leave-one-month-out lower bound {r['lomoLow']:+.2f}% not > 0")
        if not (spa["spaP"] < me["spaPBelow"]):
            failures.append(f"SPA p {spa['spaP']:.3f} not < {me['spaPBelow']}")
        if not (r["dsr"] >= me["dsrMin"]):
            failures.append(f"DSR {r['dsr']:.3f} < {me['dsrMin']}")
    h31_pass = selected is not None and not failures

    # ---- sensitivity (NOT frozen, reported only): the unadjusted-split guard extended to the S->D move itself.
    # The export's split table misses splits recorded under a company's earlier ticker, so an unrecorded reverse
    # split on D shows as a +1,000% "overnight return". The frozen guard reads bars through S only (amendment 13:
    # nothing after S is read for eligibility), so it cannot catch these. Here a list trade whose split-adjusted
    # S close -> D open move is outside the guard bounds is dropped from the list (no replacement).
    artifact = (df["exited"].values & ((gross_all > cfg["guardUp"]) | (gross_all < cfg["guardDown"])))
    sens = []
    for cf in configs:
        keep = cf["idx"][~artifact[cf["idx"]]]
        res, net = trade_stats(keep)
        if res["trades"] >= 2:
            mo = moments(net / 100)
            res["sharpe"] = sharpe(mo)
            res["dsr"] = psr(mo, emax)
        res["dropped"] = int(artifact[cf["idx"]].sum())
        res["droppedUp"] = int((artifact[cf["idx"]] & (gross_all[cf["idx"]] > 0)).sum())
        sens.append({"name": cf["name"], **res})
    sens_base_eval = cluster_boot(*by_day(S_all[eval_rows & ~artifact], gross_all[eval_rows & ~artifact] * 100)[1:], draws, seed, ci)
    sens_base_train = cluster_boot(*by_day(S_all[~artifact], gross_all[~artifact] * 100)[1:], draws, seed, ci)
    n_artifact_all = int(artifact.sum())
    n_artifact_up = int((artifact & (gross_all > 0)).sum())

    # ---- train-wide S1/S2 (descriptive)
    log("S1/S2 over all of train")
    train_desc = []
    for sg in ("S1", "S2"):
        for nn in list_sizes:
            lst = train_lists[sg]
            idx = lst.index[lst["rank"] <= nn].values
            res, _ = trade_stats(idx, with_lomo=False)
            train_desc.append({"name": f"{sg} top{nn}", **res})

    # ---- H32 anatomy
    def anatomy(g, net):
        n_ = len(g)
        out = {"n": n_}
        for key, m in (("ge10", g >= 0.10), ("ge20", g >= 0.20), ("le-10", g <= -0.10), ("le-20", g <= -0.20)):
            out[key] = {"share": float(m.mean()) if n_ else math.nan, "meanGross": float(g[m].mean() * 100) if m.any() else math.nan}
        out["meanGross"] = float(g.mean() * 100) if n_ else math.nan
        out["medianGross"] = float(np.median(g) * 100) if n_ else math.nan
        if net is not None and n_:
            for nm, vals in (("net", net), ("gross", g * 100)):
                srt = np.sort(vals)[::-1]
                tot = srt.sum()
                out[f"top1_{nm}"] = float(srt[:max(1, math.ceil(0.01 * n_))].sum() / tot) if tot != 0 else math.nan
                out[f"top5_{nm}"] = float(srt[:max(1, math.ceil(0.05 * n_))].sum() / tot) if tot != 0 else math.nan
        return out

    h32 = [{"name": cf["name"], **anatomy(gross_all[cf["idx"]], cf["net"])} for cf in configs]
    base_eval = anatomy(gross_all[eval_rows], None)
    base_train = anatomy(gross_all, None)
    base_eval_ci = cluster_boot(*by_day(S_all[eval_rows], gross_all[eval_rows] * 100)[1:], draws, seed, ci)
    base_train_ci = cluster_boot(*by_day(S_all, gross_all * 100)[1:], draws, seed, ci)
    base_cost = float(cost_all.mean() * 100)

    # ---- sanity
    per_year = df.groupby(df["S"].map(lambda x: day_str(x)[:4])).agg(rows=("S", "size"), days=("S", "nunique"))
    per_year["perDay"] = per_year["rows"] / per_year["days"]
    union_idx = np.unique(np.concatenate([cf["idx"] for cf in configs] + [train_lists[s_].index[train_lists[s_]["rank"] <= max(list_sizes)].values for s_ in ("S1", "S2")]))
    u = df.loc[union_idx]
    u_ex = u[u["exited"]]
    cols = ["sym", "S", "sOpen", "sHigh", "sLow", "sClose", "sVol", "prevCloseRaw", "Dexit", "dOpenRaw", "adjFactor", "gross", "cost"]
    top20 = u_ex.sort_values("gross", ascending=False).head(20)[cols]
    bot20 = u_ex.sort_values("gross", ascending=True).head(20)[cols]
    n_minus100_lists = int((~u["exited"]).sum())
    n_minus100_all = int((~df["exited"]).sum())
    adj_nontrivial = int((np.abs(df["adjFactor"].fillna(1.0).values - 1) > 1e-9).sum())
    late_exit = int((df.loc[union_idx, "Dexit"] > day_num(train["to"])).sum())
    absurd = int((u_ex["gross"] > 5).sum())

    # ---- write
    now = dt.datetime.now(dt.timezone.utc)
    stamp = now.strftime("%Y%m%d-%H%M")
    utc_iso = now.strftime("%Y-%m-%dT%H:%M:%S.%f") + "Z"
    git = subprocess.run(["git", "-C", REPO, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
    runs = os.path.join(a.study, "runs", "explore")

    def P(x, d_=2):
        return "n/a" if x is None or (isinstance(x, float) and not math.isfinite(x)) else f"{x:+.{d_}f}%"

    def F3(x):
        return "n/a" if x is None or not math.isfinite(x) else f"{x:.3f}"

    def SH(x):
        return "n/a" if x is None or not math.isfinite(x) else f"{x * 100:.1f}%"

    trials = []
    for cf in configs:
        r = cf["res"]
        trials.append({"id": None, "utc": utc_iso, "track": "a13-overnight-hold", "config": f"A13-H31 {cf['name']}",
                       "metric": "mean net % per trade after the A13 cost", "foldValues": r.get("windows"), "pooled": r.get("meanNet"),
                       "rows": r["trades"],
                       "note": f"low={F3(r.get('low'))} high={F3(r.get('high'))} meanGross={F3(r.get('meanGross'))} days={r['days']} dsr={F3(r.get('dsr'))}"})
    with open(os.path.join(runs, "a13-ledger-pending.jsonl"), "w", encoding="utf-8") as f:
        for tr in trials:
            f.write(json.dumps(tr, separators=(",", ":")) + "\n")

    def rows_table(frame):
        lines = ["| symbol | S | S open | S high | S low | S close | S volume | prior close | D | D open (raw) | split factor S->D | overnight | cost |",
                 "|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
        for _, z in frame.iterrows():
            lines.append(f"| {z['sym']} | {day_str(z['S'])} | {z['sOpen']:.4f} | {z['sHigh']:.4f} | {z['sLow']:.4f} | {z['sClose']:.4f} | {z['sVol']:,.0f} | {z['prevCloseRaw']:.4f} | {day_str(z['Dexit'])} | {z['dOpenRaw']:.4f} | {z['adjFactor']:.3f} | {P(z['gross'] * 100, 1)} | {P(z['cost'] * 100, 1)} |")
        return lines

    sel = selected["res"] if selected else {}
    md = []
    md.append("# Explore: the close-to-open hold (amendment 13, H31/H32, IP-T632)")
    md.append("")
    md.append(f"Train only (signal days {sig_from}..{sig_to}); validation and the holdout are not read (the script refuses any signal day after {sig_to}). Every choice is amendment-13.json's, registered and pushed before this script was written. Data: the read-only CSV exports of `GapperStudyDailyBars` (sha256 {daily_sha[:12]}) and `GapperStudySplits`, no database, Hub or Alpaca call. Script `tools/gapper-study/a13_overnight.py` at {git[:8]}. Research only: nothing here arms, sizes, places or exits an order.")
    md.append("")
    md.append("## Verdict")
    md.append("")
    if selected is None:
        md.append("**H31 fails on train**: no configuration reaches 150 trades on 40 days.")
    elif h31_pass:
        md.append(f"**H31 passes on train** with {selected['name']}. Per amendment 13 this is written into amendment-13.md as a dated note first, then validation is read once.")
    else:
        md.append(f"**H31 fails on train.** The train-selected configuration is **{selected['name']}** (highest day-clustered lower bound among those with >= {me['minTrades']} trades on >= {me['minDays']} days): {sel['trades']:,} trades on {sel['days']} days, gross {P(sel['meanGross'])}, mean A13 cost {P(sel['meanCost'])}, net **{P(sel['meanNet'])}** [{P(sel['low'])}, {P(sel['high'])}]. Failed: {'; '.join(failures)}. Per amendment 13: stop. Validation is not read, and no other signal, list size, exit or cost is tried under A13.")
    md.append("")
    md.append("## What this says")
    md.append("")
    md.append(what_this_says(selected, configs, h31_pass, failures, spa, base_train_ci, base_eval_ci, base_cost, h32, P, SH, sens, absurd))
    md.append("")
    md.append("## Population")
    md.append("")
    md.append(f"- Daily bars: {total_rows:,} rows in the export; {zero_vol:,} rows with zero volume dropped (they carry no trade, so they are neither a bar to buy at nor a bar to exit at); bars after {LOAD_CUTOFF} dropped on read. {len(sess_days)} sessions (dates with >= {MIN_SYMBOLS_PER_SESSION} traded symbols; {thin_dates} thinner dates ignored). Splits: {n_splits:,} usable rows ({bad_splits} with a zero rate ignored).")
    md.append(f"- Price, ADV and sessions screen at S (close in [${cfg['pxMin']:.2f}, ${cfg['pxMax']:.2f}), 20-session average dollar volume >= ${cfg['advMin']:,}, >= {cfg['minSessions']} of the last 20 sessions traded), S {sig_from}..{sig_to}: {agg['pre']:,} symbol-days; then removed: {agg['split']:,} with a split ex-date from S-25 calendar days through D, {agg['guard']:,} by the unadjusted-split guard (of which {agg['guardOwnS']:,} because of S's own close-to-close move). **Eligible: {len(df):,} symbol-days** on {df['S'].nunique()} signal days.")
    md.append("")
    md.append("| year | eligible symbol-days | signal days | per day |")
    md.append("|---|---|---|---|")
    for yr, z in per_year.iterrows():
        md.append(f"| {yr} | {int(z['rows']):,} | {int(z['days'])} | {z['perDay']:,.0f} |")
    md.append("")
    md.append(f"- Base rate (every eligible symbol-day, buy the S close, sell the next open, split-adjusted): train mean overnight {P(base_train_ci[0], 3)} [{P(base_train_ci[1], 3)}, {P(base_train_ci[2], 3)}], median {P(base_train['medianGross'], 3)}; evaluation windows {P(base_eval_ci[0], 3)} [{P(base_eval_ci[1], 3)}, {P(base_eval_ci[2], 3)}]. Mean A13 cost over all eligible symbol-days {P(base_cost)}. Closed at -100% (no bar within 5 sessions): {n_minus100_all:,} of {len(df):,}.")
    md.append("")
    md.append("## Cost (amendment 13, frozen)")
    md.append("")
    md.append(f"Round trip = max(classic Corwin-Schultz spread over the 20 sessions through S, {cost_floor:.1%}) + 2 x `TradingCosts.SquareRootImpact` (shares = ${notional:,} / S close, ADV = 20-session average share volume, sigma = 20-session daily log-return sigma, impact constant {impact_constant} from params.json costModel.impact). Corwin-Schultz: the 2012 two-bar estimator on split-adjusted daily highs and lows of consecutive bars in the window, negative pair estimates set to 0, averaged (the exact `TradingCosts.CorwinSchultzSpread` formula); no pair = spread 0, floor applies.")
    md.append("")
    md.append("## S3 model (LightGBM, frozen parameters, rolling origin)")
    md.append("")
    md.append("| fold | fit rows | fit positives | last fit S (after the 5-session embargo) | eval rows | eval positives | eval AUC |")
    md.append("|---|---|---|---|---|---|---|")
    for fi in fold_info:
        md.append(f"| {fi['fold']} | {fi['fitRows']:,} | {fi['fitPositives']:,} | {fi['fitLastS']} | {fi['evalRows']:,} | {fi['evalPositives']:,} | {fi['evalAuc']:.3f} |")
    md.append("")
    md.append("## H31: nine configurations on the four evaluation windows (2022-11-01..2024-10-30)")
    md.append("")
    md.append("Equal weight per trade; day-clustered bootstrap 95% (10,000 draws, seed 1337) by signal day; LOMO = the lowest day-clustered lower bound with any one month dropped; DSR = per-trade deflated Sharpe at N = "
              f"{a.trials:,} with the variance of these nine Sharpes ({var_sr:.5f}; expected max Sharpe {emax:.4f}).")
    md.append("")
    md.append("| config | trades | days | gross = break-even | mean cost | net [95%] | windows 1/2/3/4 | LOMO low | Sharpe | DSR | net 2x cost [95%] | net flat 3% [95%] | -100% exits |")
    md.append("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for cf in configs:
        r = cf["res"]
        if r["trades"] == 0:
            md.append(f"| {cf['name']} | 0 | 0 | | | | | | | | | | |")
            continue
        mark = " (selected)" if cf is selected else ""
        md.append(f"| {cf['name']}{mark} | {r['trades']:,} | {r['days']} | {P(r['meanGross'])} | {P(r['meanCost'])} | {P(r['meanNet'])} [{P(r['low'])}, {P(r['high'])}] | {' / '.join(P(x) for x in r['windows'])} | {P(r['lomoLow'])} ({r['lomoWorstMonth']}) | {r['sharpe']:.3f} | {F3(r['dsr'])} | {P(r['net2x'])} [{P(r['net2xLow'])}, {P(r['net2xHigh'])}] | {P(r['netFlat3'])} [{P(r['netFlat3Low'])}, {P(r['netFlat3High'])}] | {r['exitedMinus100']} |")
    md.append("")
    md.append(f"SPA (Hansen consistent, stationary bootstrap of the daily mean net per configuration over all {spa['periods']} evaluation sessions, 0 on a day without a trade, mean block 5, {spa['resamples']:,} resamples, seed 1337, benchmark 0): **p = {spa['spaP']:.4f}** (White's reality check p = {spa['whiteP']:.4f}; best daily mean {configs[spa['best']]['name']}).")
    md.append("")
    if selected is not None:
        md.append("H31 checklist for the selected configuration:")
        md.append("")
        md.append("| criterion | required | observed | pass |")
        md.append("|---|---|---|---|")
        pos_w = sum(1 for x in sel["windows"] if x is not None and x > 0)
        checks = [("mean net per trade", f">= +{me['meanNetPctPerTrade']:.1f}%", P(sel["meanNet"]), sel["meanNet"] >= me["meanNetPctPerTrade"]),
                  ("day-clustered 95% lower bound", "> 0", P(sel["low"]), sel["low"] > 0),
                  ("trades", f">= {me['minTrades']}", f"{sel['trades']:,}", sel["trades"] >= me["minTrades"]),
                  ("days", f">= {me['minDays']}", f"{sel['days']}", sel["days"] >= me["minDays"]),
                  ("windows positive", "3 of 4", f"{pos_w} of 4", pos_w >= 3),
                  ("leave-one-month-out lower bound", "> 0", P(sel["lomoLow"]), sel["lomoLow"] > 0),
                  ("SPA p across the nine", f"< {me['spaPBelow']}", f"{spa['spaP']:.4f}", spa["spaP"] < me["spaPBelow"]),
                  (f"DSR at N = {a.trials:,}", f">= {me['dsrMin']}", F3(sel["dsr"]), sel["dsr"] >= me["dsrMin"])]
        for nm, req, obs, ok in checks:
            md.append(f"| {nm} | {req} | {obs} | {'yes' if ok else 'no'} |")
        md.append("")
    md.append("## Sensitivity, not frozen: the split guard applied to the overnight move itself")
    md.append("")
    md.append(f"The export's split table misses splits recorded under a company's earlier ticker (for example DVLT: 0.111 to 11.20 overnight on 2023-01-27 with volume down about 100x and the new price level held on the following sessions, the signature of a roughly 1-for-100 reverse split, and no row in GapperStudySplits), so an unrecorded reverse split on D reads as a gain of thousands of percent. The frozen guard reads bars through S only (amendment 13 reads nothing after S for eligibility), so it cannot see them. Reported only, never used for the verdict: the same guard bounds (> +{cfg['guardUp']:.0%} or < {cfg['guardDown']:.2%}) applied to the S close -> D open move, the trade dropped from its list (no replacement). Over all eligible symbol-days this flags {n_artifact_all:,} ({n_artifact_up:,} upward). Base rate without them: train {P(sens_base_train[0], 3)} [{P(sens_base_train[1], 3)}, {P(sens_base_train[2], 3)}], evaluation windows {P(sens_base_eval[0], 3)} [{P(sens_base_eval[1], 3)}, {P(sens_base_eval[2], 3)}].")
    md.append("")
    md.append("| config | dropped (up) | trades | days | gross | mean cost | net [95%] | windows 1/2/3/4 | LOMO low | DSR | net flat 3% |")
    md.append("|---|---|---|---|---|---|---|---|---|---|---|")
    for r in sens:
        if r["trades"] == 0:
            md.append(f"| {r['name']} | {r['dropped']} ({r['droppedUp']}) | 0 | 0 | | | | | | | |")
            continue
        md.append(f"| {r['name']} | {r['dropped']} ({r['droppedUp']}) | {r['trades']:,} | {r['days']} | {P(r['meanGross'])} | {P(r['meanCost'])} | {P(r['meanNet'])} [{P(r['low'])}, {P(r['high'])}] | {' / '.join(P(x) for x in r['windows'])} | {P(r['lomoLow'])} | {F3(r.get('dsr', math.nan))} | {P(r['netFlat3'])} |")
    md.append("")
    md.append("## S1 and S2 over all of train (descriptive, 2021-11-01..2024-10-30; never used to choose)")
    md.append("")
    md.append("| config | trades | days | gross = break-even | mean cost | net [95%] | net 2x cost | net flat 3% | -100% exits |")
    md.append("|---|---|---|---|---|---|---|---|---|")
    for r in train_desc:
        if r["trades"] == 0:
            md.append(f"| {r['name']} | 0 | 0 | | | | | | |")
            continue
        md.append(f"| {r['name']} | {r['trades']:,} | {r['days']} | {P(r['meanGross'])} | {P(r['meanCost'])} | {P(r['meanNet'])} [{P(r['low'])}, {P(r['high'])}] | {P(r['net2x'])} | {P(r['netFlat3'])} | {r['exitedMinus100']} |")
    md.append("")
    md.append("## H32: where the overnight return comes from (descriptive, evaluation windows; no verdict)")
    md.append("")
    md.append("Buckets on the gross overnight return (S close to D open, split-adjusted): share of trades and the bucket's mean. Concentration: the top 1% / 5% of trades' share of the summed P&L (net and gross; a share above 100% means the rest sum to a loss, a negative share means the total is negative).")
    md.append("")
    md.append("| population | n | mean gross | median gross | >= +10% share (mean) | >= +20% share (mean) | <= -10% share (mean) | <= -20% share (mean) | top 1% / 5% of net P&L | top 1% / 5% of gross P&L |")
    md.append("|---|---|---|---|---|---|---|---|---|---|")

    def arow(nm, an):
        b = lambda k: f"{SH(an[k]['share'])} ({P(an[k]['meanGross'], 1)})"
        conc_n = f"{SH(an.get('top1_net'))} / {SH(an.get('top5_net'))}" if "top1_net" in an else ""
        conc_g = f"{SH(an.get('top1_gross'))} / {SH(an.get('top5_gross'))}" if "top1_gross" in an else ""
        return f"| {nm} | {an['n']:,} | {P(an['meanGross'])} | {P(an['medianGross'])} | {b('ge10')} | {b('ge20')} | {b('le-10')} | {b('le-20')} | {conc_n} | {conc_g} |"

    for an in h32:
        md.append(arow(an["name"], an))
    md.append(arow("base rate: every eligible symbol-day, evaluation windows", base_eval))
    md.append(arow("base rate: every eligible symbol-day, all of train", base_train))
    md.append("")
    md.append("## Sanity checks")
    md.append("")
    md.append(f"- Split direction: `SplitEvent.PriceFactor` = OldRate / NewRate, and an old-basis price times it is on the new basis. Checked on the bars: AYTU 1-for-20 reverse split ex 2023-01-06 (OldRate 20, NewRate 1) closes 0.2151 on 2023-01-05 and opens 3.49 on 2023-01-06 (0.2151 x 20 = 4.30); AAON 3-for-2 ex 2023-08-17 (OldRate 2, NewRate 3) closes 94.57 then opens 62.92 (94.57 x 2/3 = 63.05). The script uses this convention.")
    md.append(f"- Trades where the split factor S->D is not 1 among eligible symbol-days: {adj_nontrivial} (expected 0: the frozen exclusion removes every split from S-25 through D). List trades with a gross overnight return above +500%: {absurd}. List trades closed at -100%: {n_minus100_lists}. List trades whose exit bar is after {train['to']} (a halt held past the train end): {late_exit}.")
    md.append("")
    md.append("The 20 largest overnight returns among every list trade (all nine configurations plus the train-wide S1/S2 lists), with the raw S and D bars they were computed from:")
    md.append("")
    md.extend(rows_table(top20))
    md.append("")
    md.append("The 20 smallest:")
    md.append("")
    md.extend(rows_table(bot20))
    md.append("")
    md.append("Every row above is printed from the raw bars it was computed from (S bar, prior close, D bar open, and the S->D split factor), so an unhandled split would show as a split factor of 1 next to a price that jumps by a split ratio.")
    md.append("")
    md.append("## Implementation notes and interpretations")
    md.append("")
    md.append(NOTES)
    md.append("")
    md.append(f"Ledger: the nine trials are in `runs/explore/a13-ledger-pending.jsonl` (track a13-overnight-hold), not yet in ledger.jsonl. Runtime {time.time() - t0:,.0f} s.")

    out_json = {
        "amendment": "A13", "ticket": "IP-T632", "computedUtc": utc_iso, "gitHead": git, "dailyCsvSha256": daily_sha,
        "signalDays": {"from": sig_from, "to": sig_to}, "evalWindows": [fo["eval"] for fo in folds],
        "population": {"fileRows": total_rows, "zeroVolumeRowsDropped": zero_vol, "sessions": len(sess_days), **agg, "eligible": len(df),
                       "perYear": {k: {"rows": int(v_["rows"]), "days": int(v_["days"])} for k, v_ in per_year.iterrows()},
                       "minus100All": n_minus100_all},
        "baseRate": {"trainMeanPct": base_train_ci, "evalMeanPct": base_eval_ci, "meanCostPct": base_cost},
        "s3Folds": fold_info,
        "configs": [{"name": cf["name"], **cf["res"]} for cf in configs],
        "spa": spa, "dsr": {"trials": a.trials, "varianceOfSharpes": var_sr, "expectedMaxSharpe": emax},
        "selected": selected["name"] if selected else None, "h31Pass": h31_pass, "failures": failures,
        "sensitivityOvernightSplitGuard": {"flaggedAll": n_artifact_all, "flaggedUp": n_artifact_up, "baseTrainPct": sens_base_train, "baseEvalPct": sens_base_eval, "configs": sens},
        "trainDescriptive": train_desc, "h32": h32, "h32BaseEval": base_eval, "h32BaseTrain": base_train,
        "sanity": {"adjFactorNotOne": adj_nontrivial, "listTradesAbove500pct": absurd, "listMinus100": n_minus100_lists, "listExitAfterTrain": late_exit,
                   "top20": json.loads(top20.assign(sym=top20["sym"].astype(str), S=top20["S"].map(day_str), Dexit=top20["Dexit"].map(day_str)).to_json(orient="records")),
                   "bottom20": json.loads(bot20.assign(sym=bot20["sym"].astype(str), S=bot20["S"].map(day_str), Dexit=bot20["Dexit"].map(day_str)).to_json(orient="records"))},
    }
    with open(os.path.join(runs, f"a13-overnight-{stamp}.json"), "w", encoding="utf-8") as f:
        json.dump(out_json, f, indent=1, default=lambda o: o.item() if hasattr(o, "item") else str(o))
    with open(os.path.join(runs, f"a13-overnight-{stamp}.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(md) + "\n")
    log(f"wrote runs/explore/a13-overnight-{stamp}.md/.json and a13-ledger-pending.jsonl; H31 {'PASS' if h31_pass else 'FAIL'}; {time.time() - t0:,.0f} s")


def auc_score(y, p):
    y = np.asarray(y)
    p = np.asarray(p)
    npos, nneg = int(y.sum()), int(len(y) - y.sum())
    if npos == 0 or nneg == 0:
        return math.nan
    r = pd.Series(p).rank().values
    return float((r[y == 1].sum() - npos * (npos + 1) / 2) / (npos * nneg))


def what_this_says(selected, configs, h31_pass, failures, spa, base_train, base_eval, base_cost, h32, P, SH, sens, absurd):
    lines = []
    if selected is None:
        return "No configuration had enough trades to be selected; the test cannot pass."
    r = selected["res"]
    an = next(a for a in h32 if a["name"] == selected["name"])
    best_gross = max((c for c in configs if c["res"]["trades"] > 0), key=lambda c: c["res"]["meanGross"])
    lines.append(f"- Holding the evening list from the close to the next open earns {P(r['meanGross'])} per trade before costs for the selected list ({selected['name']}); the A13 cost takes {P(r['meanCost'])} of it, leaving {P(r['meanNet'])} [{P(r['low'])}, {P(r['high'])}]. "
                 f"The best gross mean of the nine is {best_gross['name']} at {P(best_gross['res']['meanGross'])} (its break-even round-trip cost).")
    lines.append(f"- For comparison, the average eligible stock earns {P(base_train[0], 3)} overnight on train ({P(base_eval[0], 3)} in the evaluation windows) and would pay {P(base_cost)} under this cost model.")
    lines.append(f"- Where the list's overnight return comes from: {SH(an['ge10']['share'])} of the selected list's trades open at least +10% up (mean {P(an['ge10']['meanGross'], 1)}), {SH(an['le-10']['share'])} open at least 10% down (mean {P(an['le-10']['meanGross'], 1)}); "
                 f"the median trade is {P(an['medianGross'])}.")
    sr = next(x for x in sens if x["name"] == selected["name"])
    lines.append(f"- The positive gross means rest on a data hole, not on overnight drift: {absurd} list trades 'return' more than +500% overnight, and the ones checked are reverse splits missing from the split table (recorded, if at all, under the company's earlier ticker). Dropping list trades whose overnight move breaks the frozen guard bounds (sensitivity below, {sr['dropped']} trades for the selected list) takes {selected['name']} from {P(r['meanNet'])} to {P(sr.get('meanNet'))} net [{P(sr.get('low'))}, {P(sr.get('high'))}], gross {P(sr.get('meanGross'))}.")
    lines.append(f"- Across all nine configurations SPA p = {spa['spaP']:.3f}: " + ("the best is unlikely to be luck." if spa["spaP"] < 0.05 else "the best of the nine is not distinguishable from zero once the search over nine is accounted for."))
    if h31_pass:
        lines.append("- Every H31 criterion holds on train. Because the test lets the rule see the full S bar and fill at that same close (the stated look-ahead, in the rule's favour), this is not believed until validation and the minute-level replication.")
    else:
        lines.append(f"- H31 fails ({'; '.join(failures)}). The stated look-ahead favours the rule (it sees the full S bar and fills at that same close), so this fail is decisive under the amendment: the close-to-open leg of these evening lists does not pay after a conservative cost. Validation stays unread.")
    return "\n".join(lines)


NOTES = "\n".join([
    "- Zero-volume daily rows (they exist in the export, e.g. APRE on its 2023-02-13 split ex-date at the old-basis price) are treated as no bar: a fill at their open is impossible. A symbol whose next real bar is within 5 sessions exits at that bar's open; none within 5 sessions = -100%, as frozen.",
    "- Sessions are the market calendar derived from the export (dates with >= 500 traded symbols). '20 sessions through S' = the 20 market sessions ending at S, S included; 'traded' = a bar with volume > 0 in that window (the universe builder's count). ADV (dollars and shares) = the mean over the traded bars in the window (as `GapperUniverseBuilder`): dollars from raw close x raw volume, shares on the S basis.",
    "- S1's volume rule uses that same 20-session average share volume through S (S included), the one average amendment 13 defines; the frozen candidate rule (params.candidate) divides by the 20 sessions before the day instead. volRatioS/volRatioS1 use the same definition (S-1's ratio against S-1's own window).",
    "- Unadjusted-split guard (params.universe.unadjustedSplitGuard: up > +200% or down < -66.67%, 25 calendar days): applied to raw close-to-close moves between consecutive bars whose later bar is dated S-25..S, skipping a move with a split ex-date between the two bars, exactly as `GapperUniverseBuilder.HasUnexplainedMove` does for the universe day D (whose bars before D end at S). S's own move is therefore inside the guard; the count it removes is reported above.",
    "- Split exclusion: no split ex-date from S-25 calendar days through D (D = the exit bar, or S + 5 sessions when there is none). Prices are back-adjusted with OldRate/NewRate; any feature window crossing an older split uses adjusted prices; the price screen and logPrice use the raw S close.",
    "- sigma20 = `GapperCostModel.Sigma`: sample stdev of the log returns of the last 21 closes through S (split-adjusted), at least 3 closes; corwinSchultz20 = `TradingCosts.CorwinSchultzSpread` over consecutive bar pairs within the 20-session window. Square-root impact = impact constant x sigma x sqrt(shares / ADV shares), charged twice.",
    "- Feature definitions the registration names but does not spell out: ret1 = S close / the prior bar's close - 1 (S1's ranking return); ret2/5/20 = S close over the last close at or before session S-k; rangeS = high/low - 1; closeLocationS = (close - low)/(high - low), 0.5 when high = low; maxRet10/60 = the largest close-to-close return among bars in the last 10/60 sessions; sessionsSinceUp50 = sessions since the last bar with a close-to-close gain >= +50% (none or more = 250); dayOfWeek 0 = Monday; daysSinceReverseSplit = calendar days since the last split with OldRate/NewRate > 1 at or before S (cap 365, none 366), lastReverseSplitRatio = its OldRate/NewRate (none = 0); sessionsSinceFirstBar counts from the first bar in the export (which begins 2021-09-01, so early values are truncated); eligibleSymbolsUp20OnS counts eligible symbols with ret1 >= +20% on S.",
    "- S3: LightGBM 4.7 `LGBMClassifier` with exactly the frozen parameters (everything else at its default); fit rows = eligible symbol-days with S in the fold's fit range and more than 5 sessions before the first evaluation day (the embargo); label = D open / S close - 1 >= +10% (a -100% exit is 0). Ties in any ranking are broken by symbol.",
    "- Statistics: the day-clustered bootstrap and SPA are ports of `WeightedPopulation.Mean` (unit weights) and `RealityCheck.Run` with numpy's generator seeded 1337, so the random draws differ from .NET's `Random(1337)`; SPA uses 10,000 resamples (params.statistics.bootstrapDraws) and mean block length 5 (RealityCheck's default and the study's earlier use). LOMO uses the same day-clustered bootstrap with each evaluation month dropped in turn. DSR = `MultipleTesting.DeflatedSharpe` on the per-trade net series (population moments) with the sample variance of the nine trials' Sharpes, at N = 5,676 (ledger 5,667 + these 9; amendment 14's trials, if any, are not counted).",
])


if __name__ == "__main__":
    main()
