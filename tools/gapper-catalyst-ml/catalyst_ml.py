"""IP-T488: machine-learning catalyst discovery on the gapper study's TRAIN event table.

Reads the events.csv written by `GapperStudy explore catalyst-events` (train rows only; the C# stage refuses any
other split) and writes, beside it or into --out:
  assign.csv   row_id, cluster (global topic or filing class), cluster_f1..cluster_f4 (topics refit on each fold's
               fit window and applied to that fold's fit and eval rows; -2 = row outside the fold)
  clusters.json  id, kind, name, size, top terms and example headlines per cluster
  models.json  per-fold AUC / R^2 of the text, controls-only and text+controls linear models, top terms
  summary.md   a readable dump for labelling the topics

Method: TF-IDF on word 1-2-grams of headline + stored snippet (numbers, dollar amounts and percentages folded into
tokens, exchange tickers stripped), NMF (scikit-learn, nndsvda init, fixed seed) into --k topics on the unique news
items; a row's topic is its largest NMF weight (-1 when every weight is 0). Filings are clustered by form and 8-K
item, not by text. Supervised: weighted logistic regression (target a) and ridge (b, c) on sparse TF-IDF plus
standardized controls, fitted on each fold's fit window and scored on its eval window, weight 1 / items on the
symbol-day. No external model or API. Run isolated: python -I catalyst_ml.py --events events.csv --out dir
"""
import argparse
import json
import re
import sys
import time

import numpy as np
import pandas as pd
import scipy.sparse as sp
from sklearn.decomposition import NMF
from sklearn.feature_extraction.text import TfidfVectorizer, ENGLISH_STOP_WORDS
from sklearn.linear_model import LogisticRegression, Ridge
from sklearn.metrics import roc_auc_score, r2_score

SEED = 0
FOLDS = 4

TICKER = re.compile(r"\(?\b(nasdaq|nyse(\s+american|\s+mkt|\s+arca)?|otc(qb|qx|\s+markets)?|tsx(v|-v)?|cse|asx|amex)\s*:\s*[a-z.\-]{1,6}\)?", re.I)
CASHTAG = re.compile(r"\$[A-Za-z]{1,5}\b")
USD = re.compile(r"(us)?\$\s?\d[\d,.]*\s*(million|billion|thousand|mln|bln|[mbk]\b)?", re.I)
PCT = re.compile(r"\d[\d,.]*\s?(%|percent\b)", re.I)
NUM = re.compile(r"\b\d[\d,.]*\b")
URL = re.compile(r"https?://\S+")
ENTITY = re.compile(r"&#?\w+;")

STOP = sorted(set(ENGLISH_STOP_WORDS) | {"inc", "corp", "corporation", "ltd", "llc", "co", "company", "announces", "announced", "nbsp", "amp", "quot", "said"})

CONTROLS = ["log_price", "log_adv_dollars", "adv_decile", "is_adr", "log_shares", "top20", "log_top_rank", "log_days_since_rs", "rs_log_ratio",
            "sigma_daily", "gap_at_pub", "pre30", "pre60", "log_pre_vol60", "start_min", "pub_min_et", "overnight", "after_close", "premarket", "regular",
            "log_n_symbols", "log_prior_items", "is_filing", "weekday"]

FILING_ITEMS = [("1.01", "8-K 1.01 material agreement"), ("2.01", "8-K 2.01 completed acquisition/disposition"), ("1.02", "8-K 1.02 agreement terminated"),
                ("2.02", "8-K 2.02 results of operations"), ("3.01", "8-K 3.01 delisting / listing-rule notice"), ("3.02", "8-K 3.02 unregistered equity sale"),
                ("3.03", "8-K 3.03 holder rights modified (reverse split)"), ("5.03", "8-K 5.03 charter / bylaw amendment"), ("5.07", "8-K 5.07 shareholder vote"),
                ("5.02", "8-K 5.02 officer / director change"), ("7.01", "8-K 7.01 Reg FD"), ("8.01", "8-K 8.01 other events")]


def clean(text):
    t = URL.sub(" ", text)
    t = ENTITY.sub(" ", t)
    t = TICKER.sub(" ", t)
    t = CASHTAG.sub(" ", t)
    t = USD.sub(" usdamt ", t)
    t = PCT.sub(" pctnum ", t)
    t = NUM.sub(" num ", t)
    return t.lower()


def filing_class(form, items):
    f = (form or "").upper()
    its = [i for i in str(items or "").split(";") if i]
    if f == "8-K":
        for code, name in FILING_ITEMS:
            if code in its:
                return name
        return "8-K other items"
    if f == "8-K/A":
        return "8-K/A amendment"
    if f.startswith("S-1") or f.startswith("F-1"):
        return "S-1/F-1 registration"
    if f.startswith("S-3") or f.startswith("F-3"):
        return "S-3/F-3 shelf"
    if f == "424B4":
        return "424B4 priced offering"
    if f == "424B5":
        return "424B5 shelf takedown"
    if f.startswith("424B"):
        return "424B other prospectus"
    if f.startswith("SC 13D"):
        return "SC 13D stake"
    if f.startswith("SC 13G"):
        return "SC 13G passive stake"
    if f.startswith("25"):
        return "Form 25 delisting"
    if f.startswith("15"):
        return "Form 15 deregistration"
    return "other filing " + f


def filing_text(form, items):
    toks = ["form_" + re.sub(r"[^a-z0-9]", "", (form or "").lower())]
    toks += ["item_" + i.replace(".", "_") for i in str(items or "").split(";") if i]
    return " ".join(toks)


def controls(df):
    c = pd.DataFrame(index=df.index)
    for k in ["log_price", "log_adv_dollars", "adv_decile", "is_adr", "log_shares", "rs_log_ratio", "sigma_daily", "gap_at_pub", "pre30", "pre60", "start_min", "pub_min_et", "weekday"]:
        c[k] = df[k]
    rank = df["top_mover_rank"].fillna(0)
    c["top20"] = ((rank > 0) & (rank <= 20)).astype(float)
    c["log_top_rank"] = np.where(rank > 0, np.log(rank.clip(lower=1)), np.nan)
    c["log_days_since_rs"] = np.log1p(df["days_since_rs"])
    c["log_pre_vol60"] = np.log1p(df["pre_vol60"].fillna(0))
    m = df["pub_min_et"]
    pub_day = df["pub_et"].str[:10]
    same = pub_day == df["day"]
    c["overnight"] = (~same | (m < 240)).astype(float)
    c["after_close"] = (~same & (m >= 960)).astype(float)
    c["premarket"] = (same & (m >= 240) & (m < 570)).astype(float)
    c["regular"] = (same & (m >= 570) & (m < 960)).astype(float)
    c["log_n_symbols"] = np.log(df["n_symbols"].clip(lower=1))
    c["log_prior_items"] = np.log1p(df["prior_items"])
    c["is_filing"] = (df["kind"] == "filing").astype(float)
    return c[CONTROLS]


def design(ctrl, fit_idx):
    """Median-impute with missing flags and standardize on the fit rows; returns a function of row indices."""
    med = ctrl.loc[fit_idx].median()
    miss_cols = [k for k in CONTROLS if ctrl[k].isna().any()]
    filled = ctrl.fillna(med).fillna(0)
    flags = ctrl[miss_cols].isna().astype(float).add_suffix("_missing")
    full = pd.concat([filled, flags], axis=1)
    mu = full.loc[fit_idx].mean()
    sd = full.loc[fit_idx].std().replace(0, 1).fillna(1)
    z = ((full - mu) / sd).clip(-8, 8)
    return z, list(full.columns)


def wauc(y, s, w):
    if len(np.unique(y)) < 2:
        return None
    return float(roc_auc_score(y, s, sample_weight=w))


# (topics, split a topic holding more than this share of ALL items, id base) per depth of the topic tree.
LEVELS = [(None, 0.15, 0), (25, 0.04, 100), (15, None, 200)]


def vectorizer(min_df):
    return TfidfVectorizer(ngram_range=(1, 2), min_df=min_df, max_df=0.2, sublinear_tf=True, stop_words=STOP, token_pattern=r"(?u)\b[a-z][a-z0-9_\-]+\b", max_features=60000, dtype=np.float32)


class Topics:
    """A small NMF topic tree: k topics on every item; a topic holding more than LEVELS[0] share of all items (the
    catch-all of press releases once the wire templates have their own topics) is refit into 25 sub-topics (ids 100+),
    and a sub-topic still holding more than 4% of all items into 15 more (ids 200+). Deterministic (fixed seed)."""

    def __init__(self, texts, k, depth=0, total=None, base=0):
        total = total or len(texts)
        self.depth, self.base = depth, base
        self.vec = vectorizer(20 if depth == 0 else 10)
        x = self.vec.fit_transform(texts)
        self.k = k
        self.nmf = NMF(n_components=k, init="nndsvda", random_state=SEED, max_iter=400, tol=1e-4).fit(x)
        top = self._first(x)[0]
        counts = np.bincount(top[top >= 0], minlength=k)
        share = LEVELS[depth][1]
        self.sub = {}
        nxt = 0
        for t in range(k):
            if share is not None and depth + 1 < len(LEVELS) and counts[t] > share * total:
                k2, _, b2 = LEVELS[depth + 1]
                self.sub[t] = Topics(texts[top == t], k2, depth + 1, total, b2 + nxt)
                nxt += k2 + 1

    def _first(self, x):
        w = self.nmf.transform(x)
        top = w.argmax(axis=1)
        top[w.max(axis=1) <= 1e-9] = -1
        return top, w.max(axis=1)

    def assign(self, texts):
        """Final topic id per text and its NMF weight; an item with no weight in a split topic keeps base + k (its residual)."""
        top, weight = self._first(self.vec.transform(texts))
        out = np.where(top >= 0, self.base + top, -1 if self.depth == 0 else self.base + self.k)
        for t, child in self.sub.items():
            m = top == t
            if m.any():
                out[m], weight[m] = child.assign(texts[m])
        return out, weight

    def describe(self):
        terms = np.array(self.vec.get_feature_names_out())
        res = []
        for t in range(self.k):
            if t in self.sub:
                res += self.sub[t].describe()
            else:
                res.append((self.base + t, terms[np.argsort(-self.nmf.components_[t])[:15]].tolist()))
        if self.depth > 0:
            res.append((self.base + self.k, ["(no sub-topic weight)"]))
        return res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--events", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--k", type=int, default=30)
    ap.add_argument("--names", default=None, help="json {topic index: human name} for the global topics")
    ap.add_argument("--skip-models", action="store_true")
    a = ap.parse_args()
    t0 = time.time()
    log = lambda m: print(f"[{time.time() - t0:6.0f}s] {m}", flush=True)

    df = pd.read_csv(a.events, dtype={"item_id": str, "form": str, "items": str, "pre_class": str, "headline": str, "snippet": str, "fit_mask": str}, keep_default_na=False, na_values=[""])
    for col in ["form", "items", "pre_class", "headline", "snippet"]:
        df[col] = df[col].fillna("")
    df["fit_mask"] = df["fit_mask"].astype(str).str.zfill(FOLDS)
    df["prior_items"] = df.groupby(["symbol", "day"]).cumcount()
    df["w"] = 1.0 / df["n_on_day"].clip(lower=1)
    log(f"{len(df):,} rows, {df['item_id'].nunique():,} items, {(df['kind'] == 'news').sum():,} news rows")

    news = df["kind"] == "news"
    df["text"] = ""
    df.loc[news, "text"] = (df.loc[news, "headline"] + " . " + df.loc[news, "snippet"]).map(clean)
    df.loc[~news, "text"] = [filing_text(f, i) for f, i in zip(df.loc[~news, "form"], df.loc[~news, "items"])]

    # ── global topics on unique news items ──
    items = df[news].drop_duplicates("item_id")
    model = Topics(items["text"].values, a.k)
    item_topic, item_w = model.assign(items["text"].values)
    topic_of = dict(zip(items["item_id"], item_topic))
    k = a.k
    filing_names = sorted(set(filing_class(f, i) for f, i in zip(df.loc[~news, "form"], df.loc[~news, "items"])))
    filing_id = {n: 1000 + j for j, n in enumerate(filing_names)}
    cluster = np.full(len(df), -1, dtype=int)
    cluster[news.values] = df.loc[news, "item_id"].map(topic_of).values
    cluster[~news.values] = [filing_id[filing_class(f, i)] for f, i in zip(df.loc[~news, "form"], df.loc[~news, "items"])]
    df["cluster"] = cluster
    log(f"global NMF k={k} on {len(items):,} unique news items, topic tree split {sorted(model.sub)}")

    names = {}
    if a.names:
        with open(a.names, encoding="utf-8") as fh:
            names = {int(x): y for x, y in json.load(fh).items()}
    clusters = []
    for t, top_terms in model.describe():
        sel = item_topic == t
        members = items[sel].assign(wt=item_w[sel]).sort_values("wt", ascending=False)
        ex = members["headline"].head(8).tolist() + members["headline"].sample(min(4, len(members)), random_state=SEED).tolist() if len(members) else []
        clusters.append({"id": int(t), "kind": "news", "name": names.get(int(t), "topic %d: %s" % (t, ", ".join(top_terms[:4]))), "items": int(sel.sum()),
                         "rows": int((cluster == t).sum()), "terms": top_terms, "examples": ex})
    clusters.append({"id": -1, "kind": "news", "name": names.get(-1, "unassigned (no topic weight)"), "items": int((item_topic == -1).sum()), "rows": int((cluster == -1).sum()), "terms": [],
                     "examples": items[item_topic == -1]["headline"].head(5).tolist()})
    for n, cid in filing_id.items():
        clusters.append({"id": cid, "kind": "filing", "name": n, "items": int((cluster == cid).sum()), "rows": int((cluster == cid).sum()), "terms": [], "examples": []})

    # ── per-fold topics (refit on the fold's fit window) ──
    fold_cols = {}
    for f in range(1, FOLDS + 1):
        fit = (df["fit_mask"].str[f - 1] == "1").values
        ev = (df["eval_fold"] == f).values
        col = np.full(len(df), -2, dtype=int)
        fit_items = df[news.values & fit].drop_duplicates("item_id")
        if len(fit_items) > 1000:
            m_f = Topics(fit_items["text"].values, k)
            use = news.values & (fit | ev)
            u_items = df[use].drop_duplicates("item_id")
            tp, _ = m_f.assign(u_items["text"].values)
            m = dict(zip(u_items["item_id"], tp))
            col[use] = df.loc[use, "item_id"].map(m).values
        filing_rows = (~news.values) & (fit | ev)
        col[filing_rows] = cluster[filing_rows]
        fold_cols[f] = col
        log(f"fold {f}: topics refit on {len(fit_items):,} fit-window items")
    out = pd.DataFrame({"row_id": np.arange(len(df)), "cluster": cluster, **{f"cluster_f{f}": fold_cols[f] for f in range(1, FOLDS + 1)}})
    out.to_csv(f"{a.out}/assign.csv", index=False)
    with open(f"{a.out}/clusters.json", "w", encoding="utf-8") as fh:
        json.dump(clusters, fh, indent=1, ensure_ascii=False)

    with open(f"{a.out}/summary.md", "w", encoding="utf-8") as fh:
        for c in clusters:
            fh.write(f"## {c['id']} {c['name']} (items {c['items']}, rows {c['rows']})\n\nterms: {', '.join(c['terms'])}\n\n")
            for e in c["examples"]:
                fh.write(f"- {e}\n")
            fh.write("\n")
    log("clusters written")
    if a.skip_models:
        return

    # ── supervised, fold-honest ──
    ctrl = controls(df)
    df["y_run50"] = (df["run240"] >= 0.5).astype(float).where(df["run240"].notna())
    targets = {
        "run50_240": ("binary", "y_run50", "run >= +50% within 240 minutes of publication"),
        "ret_close": ("linear", "ret_close", "return from publication to the 16:00 close"),
        "fade": ("linear", "fade", "giveback: close over the post-publication high, minus 1"),
    }
    results = {"info": {"rows": int(len(df)), "k": k, "controls": CONTROLS, "method": "sklearn TfidfVectorizer(1-2 grams, min_df 5, max_df 0.3, sublinear) + LogisticRegression(liblinear, C=1) / Ridge(alpha=10); controls median-imputed with missing flags, standardized on the fit window; weight 1/items on the symbol-day; linear targets winsorized at the fit window's 1st/99th percentiles"}, "targets": {}, "terms": {}}
    for name, (kind, ycol, label) in targets.items():
        res = {"label": label, "kind": kind, "folds": []}
        for f in range(1, FOLDS + 1):
            fit = ((df["fit_mask"].str[f - 1] == "1") & df[ycol].notna()).values
            ev = ((df["eval_fold"] == f) & df[ycol].notna()).values
            fi, ei = np.where(fit)[0], np.where(ev)[0]
            y = df[ycol].values.astype(float)
            w = df["w"].values
            if kind == "linear":
                lo, hi = np.nanpercentile(y[fi], [1, 99])
                y = np.clip(y, lo, hi)
            z, _ = design(ctrl, df.index[fi])
            zc = sp.csr_matrix(z.values.astype(np.float32))
            tv = TfidfVectorizer(ngram_range=(1, 2), min_df=5, max_df=0.3, sublinear_tf=True, stop_words=STOP, token_pattern=r"(?u)\b[a-z][a-z0-9_\-]+\b", max_features=200000, dtype=np.float32)
            xt_fit = tv.fit_transform(df["text"].values[fi])
            xt_ev = tv.transform(df["text"].values[ei])
            sets = {"controls": (zc[fi], zc[ei]), "text": (xt_fit, xt_ev), "text+controls": (sp.hstack([xt_fit, zc[fi]]).tocsr(), sp.hstack([xt_ev, zc[ei]]).tocsr())}
            row = {"fold": f, "fit": int(len(fi)), "eval": int(len(ei)), "positives_eval": int(y[ei].sum()) if kind == "binary" else None}
            for sname, (xf, xe) in sets.items():
                if kind == "binary":
                    if y[fi].sum() < 5 or len(np.unique(y[ei])) < 2:
                        row[sname] = None
                        continue
                    mdl = LogisticRegression(C=1.0, solver="liblinear", max_iter=2000)
                    mdl.fit(xf, y[fi], sample_weight=w[fi])
                    row[sname] = wauc(y[ei], mdl.decision_function(xe), w[ei])
                else:
                    mdl = Ridge(alpha=10.0)
                    mdl.fit(xf, y[fi], sample_weight=w[fi])
                    row[sname] = float(r2_score(y[ei], mdl.predict(xe), sample_weight=w[ei]))
            res["folds"].append(row)
            log(f"{name} fold {f}: " + ", ".join(f"{s}={row[s]}" for s in sets))
        for sname in ["controls", "text", "text+controls"]:
            vals = [r[sname] for r in res["folds"] if r.get(sname) is not None]
            res[sname + "_mean"] = float(np.mean(vals)) if vals else None
        res["text_beats_controls_folds"] = sum(1 for r in res["folds"] if r.get("text+controls") is not None and r.get("controls") is not None and r["text+controls"] > r["controls"])
        results["targets"][name] = res

        # Top terms: one descriptive fit on every train row with the target (text + controls); min_df 50 keeps single-company names out.
        ok = df[ycol].notna().values
        idx = np.where(ok)[0]
        y = df[ycol].values.astype(float)
        if kind == "linear":
            lo, hi = np.nanpercentile(y[idx], [1, 99])
            y = np.clip(y, lo, hi)
        z, zcols = design(ctrl, df.index[idx])
        tv = TfidfVectorizer(ngram_range=(1, 2), min_df=50, max_df=0.3, sublinear_tf=True, stop_words=STOP, token_pattern=r"(?u)\b[a-z][a-z0-9_\-]+\b", max_features=200000, dtype=np.float32)
        xt = tv.fit_transform(df["text"].values[idx])
        x = sp.hstack([xt, sp.csr_matrix(z.values[idx].astype(np.float32))]).tocsr()
        mdl = LogisticRegression(C=1.0, solver="liblinear", max_iter=2000) if kind == "binary" else Ridge(alpha=10.0)
        mdl.fit(x, y[idx], sample_weight=df["w"].values[idx])
        coef = np.ravel(mdl.coef_)
        vocab = tv.get_feature_names_out()
        nt = len(vocab)
        df_count = np.asarray((xt > 0).sum(axis=0)).ravel()
        order = np.argsort(-coef[:nt])
        pos = [[vocab[i], float(coef[i]), int(df_count[i])] for i in order[:30]]
        neg = [[vocab[i], float(coef[i]), int(df_count[i])] for i in order[::-1][:30]]
        ctl = sorted([[zcols[j], float(coef[nt + j])] for j in range(len(zcols))], key=lambda r: -abs(r[1]))
        results["terms"][name] = {"positive": pos, "negative": neg, "controls": ctl}
        log(f"{name}: terms done")
        with open(f"{a.out}/models.json", "w", encoding="utf-8") as fh:
            json.dump(results, fh, indent=1)
    log("done")


if __name__ == "__main__":
    sys.exit(main())
