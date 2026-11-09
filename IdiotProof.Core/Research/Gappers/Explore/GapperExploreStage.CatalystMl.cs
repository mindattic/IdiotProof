using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Evidence.Search;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// <c>explore catalyst-ml</c> (IP-T488), TRAIN only: the report of the machine-learning catalyst discovery. Reads the
/// `catalyst-events` table and the outputs of tools/gapper-catalyst-ml/catalyst_ml.py (assign.csv: topic per row, refit per
/// fold; clusters.json: names, terms, examples; models.json: the fold-honest linear text models), and adds (1) the cluster
/// table (lift for a +50% run, median run, giveback, pre-publication drift), (2) boosted trees (GradientBoostedTrees) on the
/// fold-refit cluster ids plus controls against controls only, per fold, (3) the drift-then-news vs news-then-move split,
/// (4) a small long-only tradeability check of the clusters that hold their gains (first bar after publication or the next
/// 09:30 open; out at the close, the next open or the D+2 close; regular-hours and frozen costs; finalist bar and DSR), and
/// (5) the keyword classifier (`CatalystTypes`) checked against the clusters, before and after its fixes.
/// </summary>
public sealed partial class GapperExploreStage
{
    private sealed class MlRow
    {
        public string Symbol = "", Kind = "", Form = "", Items = "", PreClass = "", Headline = "", PubDate = "";
        public DateOnly Day;
        public int PubMin, Start, EvalFold, NOnDay, NSymbols, OldBits, NewBits, PriorItems, OpOffset;
        public string FitMask = "";
        public double Run240, RunClose, RetClose, Fade, D1, D2, DayGain, Pre30, Pre60, Pre60Z, PreVol60, GapAtPub;
        public double LogPrice, LogAdv, Decile, IsAdr, LogShares, TopRank, DaysSinceRs, RsLogRatio, Sigma, Weekday;
        public double FbPx, FbClose, FbD1o, FbD2c, CostRth, CostFrozenFb, OpPx, OpClose, OpD1o, OpD2c, CostRthOp, CostFrozenOp;
        public int Cluster;
        public readonly int[] FoldCluster = new int[4];
        public string Name = "";
        public double W => 1.0 / Math.Max(1, NOnDay);
        public bool InSession => PubDate == Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) && PubMin >= 270 && PubMin < 960 && PreVol60 > 0;
    }

    /// <summary>Clusters that describe coverage of a move, the market or analysts rather than an event at the company; not traded as catalysts.</summary>
    private static readonly HashSet<string> CoverageClusters = new(StringComparer.Ordinal)
    {
        "movers list (sector, session)", "why-shares-are-moving explainer", "post-results move explainer", "market wrap", "volatility halt / trading resumed",
        "unusual options activity (sweeps, whale alerts)", "short interest report", "stocks-to-watch / evergreen commentary", "analyst ratings roundup",
        "earnings preview / insights recap", "other company news",
    };

    public const int CatalystMlMinTradeEvents = 150;

    private static List<string> SplitCsv(string line)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q)
            {
                if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                else sb.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == ',') { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        res.Add(sb.ToString());
        return res;
    }

    private async Task<(List<string>, int, string?)> CatalystMlAsync(ExploreRequest req, CancellationToken ct)
    {
        var p = s.Params;
        var train = p.Split("train");
        var dir = CatalystMlDir;
        string ev = Path.Combine(dir, "events.csv"), asg = Path.Combine(dir, "assign.csv"), cj = Path.Combine(dir, "clusters.json"), mj = Path.Combine(dir, "models.json");
        foreach (var f in new[] { ev, asg, cj, mj })
            if (!File.Exists(f)) return ([$"{f} is missing: run `explore catalyst-events`, then python -I tools/gapper-catalyst-ml/catalyst_ml.py --events {ev} --out {dir} --names tools/gapper-catalyst-ml/names.json"], 0, null);

        // ── load ──
        var rows = new List<MlRow>();
        using (var r = new StreamReader(ev))
        {
            var head = SplitCsv(await r.ReadLineAsync(ct) ?? "");
            var ix = head.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i, StringComparer.Ordinal);
            string? line;
            while ((line = await r.ReadLineAsync(ct)) is not null)
            {
                var c = SplitCsv(line);
                double D(string k) => double.TryParse(c[ix[k]], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
                int N(string k) => int.TryParse(c[ix[k]], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
                var day = DateOnly.ParseExact(c[ix["day"]], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (day < train.From || day > train.To) throw new InvalidOperationException($"events.csv holds {day:yyyy-MM-dd}, outside train: refusing.");
                rows.Add(new MlRow
                {
                    Symbol = c[ix["symbol"]], Kind = c[ix["kind"]], Form = c[ix["form"]], Items = c[ix["items"]], PreClass = c[ix["pre_class"]], Headline = c[ix["headline"]],
                    PubDate = c[ix["pub_et"]].Length >= 10 ? c[ix["pub_et"]][..10] : "", Day = day, PubMin = N("pub_min_et"), Start = N("start_min"), EvalFold = N("eval_fold"),
                    FitMask = c[ix["fit_mask"]].PadLeft(4, '0'), NOnDay = N("n_on_day"), NSymbols = N("n_symbols"), OldBits = N("regex_bits"),
                    Run240 = D("run240"), RunClose = D("run_close"), RetClose = D("ret_close"), Fade = D("fade"), D1 = D("ret_d1"), D2 = D("ret_d2"), DayGain = D("day_gain"),
                    Pre30 = D("pre30"), Pre60 = D("pre60"), Pre60Z = D("pre60_z"), PreVol60 = D("pre_vol60"), GapAtPub = D("gap_at_pub"),
                    LogPrice = D("log_price"), LogAdv = D("log_adv_dollars"), Decile = D("adv_decile"), IsAdr = D("is_adr"), LogShares = D("log_shares"), TopRank = D("top_mover_rank"),
                    DaysSinceRs = D("days_since_rs"), RsLogRatio = D("rs_log_ratio"), Sigma = D("sigma_daily"), Weekday = D("weekday"),
                    FbPx = D("fb_px"), FbClose = D("fb_close"), FbD1o = D("fb_d1_open"), FbD2c = D("fb_d2_close"), CostRth = D("cost_rth"), CostFrozenFb = D("cost_frozen_fb"),
                    OpOffset = N("op_offset"), OpPx = D("op_px"), OpClose = D("op_close"), OpD1o = D("op_d1_open"), OpD2c = D("op_d2_close"), CostRthOp = D("cost_rth_op"), CostFrozenOp = D("cost_frozen_op"),
                });
            }
        }
        using (var r = new StreamReader(asg))
        {
            await r.ReadLineAsync(ct);
            string? line;
            int i = 0;
            while ((line = await r.ReadLineAsync(ct)) is not null)
            {
                var c = line.Split(',');
                var row = rows[int.Parse(c[0], CultureInfo.InvariantCulture)];
                row.Cluster = int.Parse(c[1], CultureInfo.InvariantCulture);
                for (int f = 0; f < 4; f++) row.FoldCluster[f] = int.Parse(c[2 + f], CultureInfo.InvariantCulture);
                i++;
            }
            if (i != rows.Count) throw new InvalidOperationException($"assign.csv has {i} rows, events.csv {rows.Count}: rerun the python step on this events.csv.");
        }
        using var clustersDoc = JsonDocument.Parse(await File.ReadAllTextAsync(cj, ct));
        using var modelsDoc = JsonDocument.Parse(await File.ReadAllTextAsync(mj, ct));
        var clusterInfo = clustersDoc.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetInt32(), e => e);
        foreach (var row in rows) row.Name = clusterInfo.TryGetValue(row.Cluster, out var ci) ? ci.GetProperty("name").GetString() ?? "?" : "?";
        // Prior items on the same symbol-day (rows are sorted by day, symbol and publication time).
        for (int i = 1; i < rows.Count; i++)
            if (rows[i].Day == rows[i - 1].Day && rows[i].Symbol == rows[i - 1].Symbol) rows[i].PriorItems = rows[i - 1].PriorItems + 1;
        foreach (var row in rows)
            row.NewBits = row.Kind == "news" ? CatalystTypes.FromHeadline(row.Headline, row.PreClass) : CatalystTypes.FromFiling(row.Form, row.Items.Split(';', StringSplitOptions.RemoveEmptyEntries));
        s.Logger.LogInformation("[{Now}] explore catalyst-ml: {Rows} rows loaded", GapperStudyServices.Et(s.UtcNow), rows.Count);

        var trainDays = MarketCalendar.TradingDays(train.From, train.To);
        var folds = ExploreFolds.Build(trainDays, foldMonths.Initial, foldMonths.Eval);
        var md = new StringBuilder(Header("machine-learning catalyst discovery (IP-T488): which news and filings move small caps", folds));
        var trials = new List<ExploreTrial>();
        var json = new Dictionary<string, object?>();
        string Pct(double? v) => DeepPct(v);
        string Sh(double? v) => DeepShare(v);
        string Num(double? v, string fmt = "0.000") => v is { } x && double.IsFinite(x) ? x.ToString(fmt, CultureInfo.InvariantCulture) : "n/a";
        static double Wins(double v, double lo, double hi) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : v;
        static double Q(List<double> v, double q) { if (v.Count == 0) return double.NaN; v.Sort(); return v[Math.Min(v.Count - 1, (int)Math.Floor(q * v.Count))]; }

        int nNews = rows.Count(r => r.Kind == "news"), nFil = rows.Count - nNews;
        double baseRun = rows.Where(r => double.IsFinite(r.Run240)).Average(r => r.Run240 >= 0.5 ? 1.0 : 0.0);
        double baseDay = rows.Average(r => r.DayGain >= 0.5 ? 1.0 : 0.0);
        var retAll = rows.Select(r => r.RetClose).Where(double.IsFinite).ToList();
        var d2All = rows.Select(r => r.D2).Where(double.IsFinite).ToList();
        double rLo = Q([.. retAll], 0.01), rHi = Q([.. retAll], 0.99), dLo = Q([.. d2All], 0.01), dHi = Q([.. d2All], 0.99);

        md.AppendLine("## 0. Data and method");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Event table (`explore catalyst-events`): one row per (symbol, item) for every stored news item (Alpaca/Benzinga, headline + stored snippet, {nNews:N0} rows) and EDGAR filing (forms 3/4/5 left out, {nFil:N0} rows) whose event session E is a train universe day with minute bars: {rows.Count:N0} rows, {rows.Select(r => (r.Symbol, r.Day)).Distinct().Count():N0} symbol-days, {rows.Select(r => r.Symbol).Distinct().Count():N0} symbols, {trainDays[0]:yyyy-MM-dd}..{trainDays[^1]:yyyy-MM-dd}. E = the publish day for an item out 00:00-15:59 ET on a trading day, else the next session. Outcomes start at the first full minute after publication (04:00 of E for an item from the night before) against the reference = the last trade that ended by publication (the prior session's after-hours count; else the prior daily close): max run in the next 60 / 240 minutes and to 16:00 (high over reference), return to the 16:00 close, D+1 / D+2 close (daily, raw; blank across a split or past train), giveback = close over the post-publication high minus 1, and whether the day ran >= +50% / +100% (04:00-16:00 high over the adjusted prior close). Pre-publication drift: log return over the 30 / 60 minutes before publication, and as a z-score against the stock's own 20-session daily volatility scaled to the window (sigma x sqrt(minutes / 390)). Controls: log prior close, log 20-day dollar volume, ADV decile, ADR, log shares outstanding known before E (float proxy), D-1 top-mover rank, days since and log ratio of the last reverse split, daily volatility, gap at publication, the pre-drift, premarket volume before publication, publish minute and session, symbols on the item, items earlier that symbol-day, filing flag, weekday. Base rates: run >= +50% within 240 minutes {Sh(baseRun)}; day >= +50% {Sh(baseDay)}. The news store holds the candidate windows (gapper days and the 5 sessions before, plus controls), so the population is small caps around a move, not every small cap."));
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Topics (tools/gapper-catalyst-ml/catalyst_ml.py, Python {modelsDoc.RootElement.GetProperty("info").GetProperty("method").GetString()?.Split(';')[0]}, scikit-learn, run isolated with `python -I`): TF-IDF on word 1-2-grams of headline + snippet (numbers, dollar amounts and percentages folded into tokens, exchange tickers and cashtags stripped), NMF on the {clustersDoc.RootElement.EnumerateArray().Where(e => e.GetProperty("kind").GetString() == "news").Sum(e => e.GetProperty("items").GetInt32()):N0} unique news items: 30 topics; the one topic holding over 15% of the items (the catch-all of press releases once the wire templates had their own topics) refit into 25 sub-topics, and a sub-topic over 4% into 15 more. A row's topic is its largest NMF weight. The {clusterInfo.Values.Count(e => e.GetProperty("kind").GetString() == "news")} topics were then named by hand from their top terms and examples (tools/gapper-catalyst-ml/names.json), several templated wires merging into one name, leaving {rows.Where(r => r.Kind == "news").Select(r => r.Name).Distinct().Count()} news clusters. Filings are clustered by form and 8-K item (priority 1.01 > 2.01 > 1.02 > 2.02 > 3.01 > 3.02 > 3.03 > 5.03 > 5.07 > 5.02 > 7.01 > 8.01). No 6-K is in the study's EDGAR index (the widened dilution files were never pulled on this host), so there is no 6-K exhibit cluster. No external model or API."));
        md.AppendLine();

        // ── 1. cluster table ──
        var byName = rows.GroupBy(r => r.Name).ToList();
        var stats = new Dictionary<string, (int N, int Days, double Run50, double Lift, double Day50, double MedRun, double MedRet, double MedFade, double MeanRet, double MeanD2, double MedD2,
            int NIn, double MedPre60, double ShareZ2, double ShareUp5, double DriftShare, double DriftThenNews, double NewsThenMove, double MeanPre, double MeanPost)>();
        foreach (var g in byName)
        {
            var l = g.ToList();
            var withRun = l.Where(r => double.IsFinite(r.Run240)).ToList();
            double run50 = withRun.Count == 0 ? double.NaN : withRun.Average(r => r.Run240 >= 0.5 ? 1.0 : 0.0);
            double day50 = l.Average(r => r.DayGain >= 0.5 ? 1.0 : 0.0);
            var inS = l.Where(r => r.InSession && double.IsFinite(r.Pre60)).ToList();
            double meanPre = inS.Count == 0 ? double.NaN : inS.Average(r => Wins(r.Pre60, -1, 1));
            var post = inS.Where(r => double.IsFinite(r.Run240)).Select(r => Wins(Math.Log(1 + r.Run240), -1, 1)).ToList();
            double meanPost = post.Count == 0 ? double.NaN : post.Average();
            double driftShare = meanPre > 0 && meanPre + meanPost > 0 ? meanPre / (meanPre + Math.Max(0, meanPost)) : (inS.Count > 0 ? 0 : double.NaN);
            double dtn = inS.Count == 0 ? double.NaN : inS.Average(r => r.Pre60 >= Math.Log(1.05) || r.Pre60Z >= 2 ? 1.0 : 0.0);
            double ntm = inS.Count == 0 ? double.NaN : inS.Average(r => r.Pre60Z < 1 && r.Pre60 < Math.Log(1.02) && r.Run240 >= 0.10 ? 1.0 : 0.0);
            stats[g.Key] = (l.Count, l.Select(r => (r.Symbol, r.Day)).Distinct().Count(), run50, run50 / baseRun, day50,
                Q(l.Select(r => r.RunClose).Where(double.IsFinite).ToList(), 0.5), Q(l.Select(r => r.RetClose).Where(double.IsFinite).ToList(), 0.5), Q(l.Select(r => r.Fade).Where(double.IsFinite).ToList(), 0.5),
                l.Select(r => Wins(r.RetClose, rLo, rHi)).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Average(), l.Select(r => Wins(r.D2, dLo, dHi)).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Average(),
                Q(l.Select(r => r.D2).Where(double.IsFinite).ToList(), 0.5),
                inS.Count, Q(inS.Select(r => r.Pre60).ToList(), 0.5), inS.Count == 0 ? double.NaN : inS.Average(r => r.Pre60Z >= 2 ? 1.0 : 0.0),
                inS.Count == 0 ? double.NaN : inS.Average(r => r.GapAtPub >= 0.05 ? 1.0 : 0.0), driftShare, dtn, ntm, meanPre, meanPost);
        }
        string Kind(string name) => byName.First(g => g.Key == name).First().Kind == "filing" ? "filing" : CoverageClusters.Contains(name) ? "coverage" : "news";
        md.AppendLine("## 1. Clusters: which catalyst types precede a run");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Every row counts once (a chatty symbol-day counts once per item). Lift = P(run >= +50% within 240 minutes of publication | cluster) / {Sh(baseRun)}. Run = median max run to 16:00; giveback = median close over the post-publication high minus 1; mean returns winsorized at the pooled 1st/99th percentiles. Drift share = mean pre-60-minute log drift / (that + mean log max run in the 240 minutes after), on items published 04:30-15:59 ET with premarket or regular-hours prints in the hour before (\"in-session\"). Kind: news = an event at the company; coverage = a wire about the move, the market or analysts' views of it (Benzinga templates); filing = EDGAR form / 8-K item. Sorted by lift."));
        md.AppendLine();
        md.AppendLine("| cluster | kind | rows | symbol-days | run >= +50% in 240 min | lift | day >= +50% | median run to close | median ret to close | mean ret to close | median giveback | mean D+2 | median D+2 | in-session | median pre-60 drift | pre z >= 2 | already +5% | drift share |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var clusterJson = new List<object>();
        foreach (var (name, st) in stats.OrderByDescending(kv => double.IsFinite(kv.Value.Lift) ? kv.Value.Lift : -1))
        {
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {Kind(name)} | {st.N:N0} | {st.Days:N0} | {Sh(st.Run50)} | {Num(st.Lift, "0.00")} | {Sh(st.Day50)} | {Pct(st.MedRun)} | {Pct(st.MedRet)} | {Pct(st.MeanRet)} | {Pct(st.MedFade)} | {Pct(st.MeanD2)} | {Pct(st.MedD2)} | {st.NIn:N0} | {Pct(st.MedPre60)} | {Sh(st.ShareZ2)} | {Sh(st.ShareUp5)} | {Sh(st.DriftShare)} |"));
            clusterJson.Add(new { name, kind = Kind(name), st.N, st.Days, run50 = st.Run50, lift = st.Lift, day50 = st.Day50, medianRun = st.MedRun, medianRet = st.MedRet, meanRet = st.MeanRet, medianFade = st.MedFade, meanD2 = st.MeanD2, medianD2 = st.MedD2, inSession = st.NIn, medianPre60 = st.MedPre60, shareZ2 = st.ShareZ2, shareUp5 = st.ShareUp5, driftShare = st.DriftShare, driftThenNews = st.DriftThenNews, newsThenMove = st.NewsThenMove });
        }
        md.AppendLine();
        md.AppendLine("Top terms and examples of the news clusters (NMF topics merged under one name are listed together):");
        md.AppendLine();
        foreach (var g in clusterInfo.Values.Where(e => e.GetProperty("kind").GetString() == "news").GroupBy(e => e.GetProperty("name").GetString() ?? "?").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var terms = g.SelectMany(e => e.GetProperty("terms").EnumerateArray().Select(x => x.GetString() ?? "")).Where(x => !x.StartsWith('(')).Distinct().Take(10);
            var ex = g.SelectMany(e => e.GetProperty("examples").EnumerateArray().Take(2).Select(x => x.GetString() ?? "")).Take(3).Select(Clean);
            md.AppendLine($"- **{g.Key}** (NMF topics {string.Join(", ", g.Select(e => e.GetProperty("id").GetInt32()))}): {string.Join(", ", terms)}. E.g. {string.Join(" / ", ex.Select(x => $"\"{x}\""))}");
        }
        md.AppendLine();
        json["clusters"] = clusterJson;

        // ── 2. does the text add information beyond the profile? ──
        md.AppendLine("## 2. Text vs the stock's profile, per fold");
        md.AppendLine();
        var targetsEl = modelsDoc.RootElement.GetProperty("targets");
        md.AppendLine("**Linear models on TF-IDF (Python, scikit-learn)**: weighted logistic regression for (a), ridge for (b) and (c) (winsorized at the fit window's 1st/99th percentiles), fitted on each fold's fit window (TF-IDF vocabulary too) and scored on its eval window; weight 1 / items on the symbol-day. Controls only vs text only vs text + controls. (a) weighted AUC, (b)/(c) weighted R^2 on the eval window.");
        md.AppendLine();
        md.AppendLine("| target | features | fold 1 | fold 2 | fold 3 | fold 4 | mean | folds text+controls > controls |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        var pyJson = new List<object>();
        foreach (var t in targetsEl.EnumerateObject())
        {
            var folds4 = t.Value.GetProperty("folds").EnumerateArray().ToList();
            foreach (var set in new[] { "controls", "text", "text+controls" })
            {
                var vals = folds4.Select(f => f.TryGetProperty(set, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null).ToList();
                double? mean = t.Value.TryGetProperty(set + "_mean", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetDouble() : null;
                md.AppendLine($"| {t.Value.GetProperty("label").GetString()} | {set} | {string.Join(" | ", vals.Select(v => Num(v)))} | {Num(mean)} | {(set == "text+controls" ? $"{t.Value.GetProperty("text_beats_controls_folds").GetInt32()}/4" : "")} |");
                bool binary = t.Value.GetProperty("kind").GetString() == "binary";
                trials.Add(new ExploreTrial
                {
                    Track = "catalyst-ml-model", Config = $"linear ({(binary ? "logistic" : "ridge")}) {set} -> {t.Name}", Metric = binary ? "weightedAuc (mean of folds)" : "weightedR2 (mean of folds)",
                    FoldValues = vals, Pooled = mean, Rows = folds4.Sum(f => f.GetProperty("eval").GetInt32()),
                });
                pyJson.Add(new { target = t.Name, set, folds = vals, mean });
            }
        }
        md.AppendLine();

        // Boosted trees on the fold-refit cluster ids plus controls.
        string[] ctrlNames = ["log_price", "log_adv_dollars", "adv_decile", "is_adr", "log_shares", "top20", "log_top_rank", "log_days_since_rs", "rs_log_ratio", "sigma_daily", "gap_at_pub", "pre30", "pre60",
            "log_pre_vol60", "start_min", "pub_min_et", "overnight_or_after_close", "premarket", "regular", "log_n_symbols", "log_prior_items", "is_filing", "weekday"];
        float[] Ctrl(MlRow r)
        {
            bool same = r.PubDate == r.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            double top = r.TopRank;
            return
            [
                (float)r.LogPrice, (float)r.LogAdv, (float)r.Decile, (float)r.IsAdr, (float)r.LogShares, top > 0 && top <= 20 ? 1 : 0, top > 0 ? (float)Math.Log(top) : float.NaN,
                (float)Math.Log(1 + r.DaysSinceRs), (float)r.RsLogRatio, (float)r.Sigma, (float)r.GapAtPub, (float)r.Pre30, (float)r.Pre60, (float)Math.Log(1 + (double.IsFinite(r.PreVol60) ? r.PreVol60 : 0)),
                r.Start, r.PubMin, !same || r.PubMin < 240 ? 1 : 0, same && r.PubMin >= 240 && r.PubMin < 570 ? 1 : 0, same && r.PubMin >= 570 && r.PubMin < 960 ? 1 : 0,
                (float)Math.Log(Math.Max(1, r.NSymbols)), (float)Math.Log(1 + r.PriorItems), r.Kind == "filing" ? 1 : 0, (float)r.Weekday,
            ];
        }
        var gbtTargets = new (string Name, string Label, Func<MlRow, bool?> Y)[]
        {
            ("run50_240", "run >= +50% within 240 minutes", r => double.IsFinite(r.Run240) ? r.Run240 >= 0.5 : null),
            ("up_close", "up at the 16:00 close (ret > 0)", r => double.IsFinite(r.RetClose) ? r.RetClose > 0 : null),
            ("giveback20", "gives back >= 20% from the post-publication high", r => double.IsFinite(r.Fade) ? r.Fade <= -0.20 : null),
        };
        var options = new BoostingOptions { Trees = 150, MaxDepth = 4, LearningRate = 0.05, MinChildHessian = 5 };
        md.AppendLine("**Boosted trees (`GradientBoostedTrees`)** on controls only vs controls + one-hot cluster ids, the topics refit by NMF on each fold's fit window (fit window and eval window rows; filings by form/item; ids seen at least 30 times in the fit window). Weighted AUC on the eval window; (b) and (c) as binary labels.");
        md.AppendLine();
        md.AppendLine("| target | eval positives (fold 1-4) | features | fold 1 | fold 2 | fold 3 | fold 4 | mean | folds clusters > controls |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        var gbtJson = new List<object>();
        var importance = new List<(string Feature, double Share)>();
        foreach (var (tname, tlabel, y) in gbtTargets)
        {
            var res = new double?[2, 4];
            var pos = new int[4];
            for (int f = 0; f < folds.Count; f++)
            {
                var fit = rows.Where(r => r.FitMask[f] == '1' && y(r) is not null && r.FoldCluster[f] != -2).ToList();
                var evl = rows.Where(r => r.EvalFold == f + 1 && y(r) is not null && r.FoldCluster[f] != -2).ToList();
                pos[f] = evl.Count(r => y(r) == true);
                if (fit.Count(r => y(r) == true) < 10 || pos[f] == 0 || pos[f] == evl.Count) continue;
                var ids = fit.GroupBy(r => r.FoldCluster[f]).Where(g => g.Count() >= 30).Select(g => g.Key).OrderBy(x => x).ToList();
                var col = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
                for (int set = 0; set < 2; set++)
                {
                    int d = ctrlNames.Length + (set == 1 ? ids.Count : 0);
                    var names = ctrlNames.Concat(set == 1 ? ids.Select(id => $"cluster={id}") : []).ToList();
                    float[] Row(MlRow r)
                    {
                        var v = new float[d];
                        Ctrl(r).CopyTo(v, 0);
                        if (set == 1 && col.TryGetValue(r.FoldCluster[f], out var k)) v[ctrlNames.Length + k] = 1;
                        return v;
                    }
                    var x = new float[fit.Count * d];
                    for (int i = 0; i < fit.Count; i++) Row(fit[i]).CopyTo(x, i * d);
                    var model = GradientBoostedTrees.Fit(names, x, fit.Select(r => y(r)!.Value).ToArray(), fit.Select(r => r.W).ToArray(), options);
                    var scores = evl.Select(r => model.Predict(Row(r))).ToList();
                    res[set, f] = WeightedAuc.Compute(scores, evl.Select(r => y(r)!.Value).ToList(), evl.Select(r => r.W).ToList());
                    if (set == 1 && f == folds.Count - 1 && tname == "run50_240") importance = model.Importance().Take(15).ToList();
                }
                s.Logger.LogInformation("[{Now}] explore catalyst-ml: boosted {Target} fold {Fold} controls {A:0.000} clusters {B:0.000}", GapperStudyServices.Et(s.UtcNow), tname, f + 1, res[0, f], res[1, f]);
            }
            for (int set = 0; set < 2; set++)
            {
                var vals = Enumerable.Range(0, 4).Select(f => res[set, f]).ToList();
                var ok = vals.Where(v => v is not null).Select(v => v!.Value).ToList();
                double? mean = ok.Count == 0 ? null : ok.Average();
                int wins = Enumerable.Range(0, 4).Count(f => res[1, f] is { } b && res[0, f] is { } a && b > a);
                md.AppendLine($"| {tlabel} | {string.Join("/", pos)} | {(set == 0 ? "controls" : "controls + clusters")} | {string.Join(" | ", vals.Select(v => Num(v)))} | {Num(mean)} | {(set == 1 ? $"{wins}/4" : "")} |");
                trials.Add(new ExploreTrial
                {
                    Track = "catalyst-ml-model", Config = $"boosted {(set == 0 ? "controls" : "controls + fold-refit clusters")} -> {tname}", Metric = "weightedAuc (mean of folds)", FoldValues = vals, Pooled = mean,
                    Rows = rows.Count(r => r.EvalFold > 0 && y(r) is not null),
                });
                gbtJson.Add(new { target = tname, set = set == 0 ? "controls" : "controls+clusters", folds = vals, mean });
            }
        }
        md.AppendLine();
        if (importance.Count > 0)
        {
            md.AppendLine("Top features by gain, boosted controls + clusters, run >= +50% in 240 minutes, fold 4 (descriptive; cluster ids are fold 4's own topics): " +
                string.Join(", ", importance.Select(x => $"{x.Feature} {x.Share:P1}")) + ".");
            md.AppendLine();
        }
        json["linear"] = pyJson;
        json["boosted"] = gbtJson;

        // ── 3. top terms ──
        md.AppendLine("## 3. Top terms (descriptive fit on every train row, text + controls)");
        md.AppendLine();
        var termsEl = modelsDoc.RootElement.GetProperty("terms");
        foreach (var t in termsEl.EnumerateObject())
        {
            string Fmt(JsonElement a) => string.Join(", ", a.EnumerateArray().Take(25).Select(x => $"{x[0].GetString()} ({x[1].GetDouble().ToString("+0.00;-0.00", CultureInfo.InvariantCulture)}, n={x[2].GetInt32()})"));
            md.AppendLine($"- **{targetsEl.GetProperty(t.Name).GetProperty("label").GetString()}**, positive: {Fmt(t.Value.GetProperty("positive"))}");
            md.AppendLine($"- **{targetsEl.GetProperty(t.Name).GetProperty("label").GetString()}**, negative: {Fmt(t.Value.GetProperty("negative"))}");
            md.AppendLine($"- controls (standardized coefficient): {string.Join(", ", t.Value.GetProperty("controls").EnumerateArray().Take(8).Select(x => $"{x[0].GetString()} {x[1].GetDouble().ToString("+0.000;-0.000", CultureInfo.InvariantCulture)}"))}");
        }
        md.AppendLine();
        md.AppendLine("Tokens: `usdamt` = a dollar amount, `pctnum` = a percentage, `num` = another number, `form_*` / `item_*` = a filing's form and 8-K items.");
        md.AppendLine();

        // ── 4. pre-publication drift ──
        md.AppendLine("## 4. Drift then news, or news then move?");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"In-session items only (published 04:30-15:59 ET on E with prints in the hour before). Drift then news = up >= 5% (log) or >= 2 sigma in the 60 minutes before publication; news then move = flat before (< 1 sigma and < +2%) and a >= +10% run within 240 minutes after. Drift share as in section 1. A cluster is flagged as a leakage / promotion signature when its drift share is >= 33% and its drift-then-news share is at least twice the pooled one ({Sh(rows.Where(r => r.InSession && double.IsFinite(r.Pre60)).Average(r => r.Pre60 >= Math.Log(1.05) || r.Pre60Z >= 2 ? 1.0 : 0.0))}) over at least 100 in-session items, and as \"watch\" at a drift share >= 30% with 1.5x the pooled drift-then-news share. Coverage wires (halt notices, movers lists, explainers) are written after the move by construction, so a high drift share there is a reaction, not a leak, and they are marked \"reaction\" instead. Sorted by drift share."));
        md.AppendLine();
        double pooledDtn = rows.Where(r => r.InSession && double.IsFinite(r.Pre60)).Average(r => r.Pre60 >= Math.Log(1.05) || r.Pre60Z >= 2 ? 1.0 : 0.0);
        md.AppendLine("| cluster | kind | in-session items | mean pre-60 log drift | mean log run 240 after | drift share | drift then news | news then move | already +5% at publication | flag |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        var flagged = new List<string>();
        foreach (var (name, st) in stats.Where(kv => kv.Value.NIn >= 30).OrderByDescending(kv => double.IsFinite(kv.Value.DriftShare) ? kv.Value.DriftShare : -1))
        {
            bool strong = st.NIn >= 100 && st.DriftShare >= 1.0 / 3 && st.DriftThenNews >= 2 * pooledDtn;
            bool watch = !strong && st.NIn >= 100 && st.DriftShare >= 0.30 && st.DriftThenNews >= 1.5 * pooledDtn;
            bool coverage = Kind(name) == "coverage";
            string flag = !(strong || watch) ? "" : coverage ? "reaction (coverage)" : strong ? "**leakage / promotion**" : "watch (leakage / promotion)";
            if ((strong || watch) && !coverage) flagged.Add(name + (strong ? "" : " (watch)"));
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {Kind(name)} | {st.NIn:N0} | {Pct(st.MeanPre)} | {Pct(st.MeanPost)} | {Sh(st.DriftShare)} | {Sh(st.DriftThenNews)} | {Sh(st.NewsThenMove)} | {Sh(st.ShareUp5)} | {flag} |"));
        }
        md.AppendLine();
        md.AppendLine($"Flagged: {(flagged.Count == 0 ? "none" : string.Join("; ", flagged))}.");
        md.AppendLine();
        json["drift"] = new { pooledDriftThenNews = pooledDtn, flagged };

        // ── 5. tradeability ──
        var candidates = stats.Where(kv => Kind(kv.Key) != "coverage" && kv.Value.N >= CatalystMlMinTradeEvents).ToList();
        var holds = candidates.Where(kv => kv.Value.MeanRet > 0 && kv.Value.MeanD2 > 0).OrderByDescending(kv => kv.Value.MedFade).Select(kv => kv.Key).Take(5).ToList();
        if (holds.Count < 3)
            holds.AddRange(candidates.Where(kv => !holds.Contains(kv.Key)).OrderByDescending(kv => kv.Value.MeanRet + kv.Value.MeanD2).Take(3 - holds.Count).Select(kv => kv.Key));
        var dayIndex = trainDays.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var months = trainDays.Select(d => d.Year * 12 + d.Month).Distinct().OrderBy(m => m).ToList();
        var dayMonth = trainDays.Select(d => months.IndexOf(d.Year * 12 + d.Month)).ToArray();
        var dayFold = trainDays.Select(d => folds.FindIndex(f => f.InEval(d))).ToArray();
        string[] entries = ["first bar after publication", "next 09:30 open"];
        string[] exits = ["close", "D+1 open", "D+2 close"];
        var configs = new List<(string Cluster, int Entry, int Exit, WlStats St, int Events)>();
        foreach (var name in holds)
            for (int en = 0; en < 2; en++)
            {
                var seen = new HashSet<(string, DateOnly)>();
                var sims = new List<(WlRow Row, WlSim Sim)>();
                foreach (var r in rows.Where(r => r.Name == name))
                {
                    double px = en == 0 ? r.FbPx : r.OpPx;
                    if (!(px > 0)) continue;
                    var entryDay = en == 1 && r.OpOffset == 1 ? MarketCalendar.NextTradingDay(r.Day) : r.Day;
                    if (!dayIndex.TryGetValue(entryDay, out int di) || !seen.Add((r.Symbol, entryDay))) continue;
                    double[] ex = en == 0 ? [r.FbClose, r.FbD1o, r.FbD2c] : [r.OpClose, r.OpD1o, r.OpD2c];
                    var sim = new WlSim { Fill = (float)px, Gross = ex.Select(v => v > 0 ? (float)(v / px - 1) : float.NaN).ToArray(), Cap1k = [float.NaN, float.NaN, float.NaN], Cap5k = [float.NaN, float.NaN, float.NaN], Full1k = new bool[3], Full5k = new bool[3] };
                    double rth = en == 0 ? r.CostRth : r.CostRthOp, frozen = en == 0 ? r.CostFrozenFb : r.CostFrozenOp;
                    sim.Cost[0] = (float)rth; sim.Cost[1] = (float)(double.IsFinite(frozen) ? frozen : rth); sim.Cost[2] = (float)(2 * OrbCommissionPerShare / px);
                    sims.Add((new WlRow { DayIdx = di, Weight = 1, Fold = dayFold[di] }, sim));
                }
                for (int x = 0; x < 3; x++)
                {
                    var list = sims.Where(z => float.IsFinite(z.Sim.Gross[x])).ToList();
                    configs.Add((name, en, x, TradeStats(list, x, trainDays.Count, dayFold, dayMonth, months.Count, folds.Count), list.Count));
                }
            }
        int nTrials = Ledger.Count + trials.Count + configs.Count;
        var sharpes = configs.Where(c => c.St.Trades >= 2).Select(c => c.St.Moments.Sharpe).Where(double.IsFinite).ToList();
        double varSr = sharpes.Count > 1 ? Math.Pow(EvidenceStats.StdDev(sharpes), 2) : 0;
        double Dsr(WlStats st) => st.Trades >= 2 ? MultipleTesting.DeflatedSharpe(st.Moments, nTrials, varSr) : double.NaN;
        double dsrMin = p.Statistics.DsrMin;
        md.AppendLine("## 5. Tradeability (long only, train, small)");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Clusters chosen by \"holds gains\" from section 1 (in-sample on train, which the DSR pays for): event clusters (not coverage) with >= {CatalystMlMinTradeEvents} rows, mean return to the close > 0 and mean D+2 > 0, ranked by median giveback (least first), top 5: {string.Join("; ", holds)}. One trade per symbol and entry day (the first item). Entry: the open of the first real bar from the first full minute after publication (within {CatalystMlEntryWindow} minutes, by 15:40; premarket fills count), or the next 09:30 open (E's when published by 09:30, else the next session's daily open). Exits relative to the entry day: the 15:55 bar's open (daily close for a next-session entry), the next session's open, the second session's close (daily bars, raw; dropped across a split or past train). No stop. Costs: (1) regular hours = Corwin-Schultz over E's 09:30-15:55 minute bars + ${OrbCommissionPerShare:0.0000}/share each side; (2) the frozen model (premarket multiplier, impact on the 04:00-to-entry volume, 20-session sigma, floor; a next-session entry uses E's spread and premarket volume); (3) commission only. Pooled over every train day; per fold = the four eval windows; day-clustered 95% bounds; LOMO = the lowest leave-one-month-out lower bound. DSR = per-trade deflated Sharpe of the regular-hours net at N = {nTrials:N0} (ledger total after this run), variance of this run's {sharpes.Count} trade-config Sharpes {varSr:0.0000}; frozen minimum {dsrMin:0.00}. Finalist bar (explore-plan.md): >= +1.0% net per trade with lower bound > 0, >= 150 trades over >= 40 days, > 0 in at least 3 of 4 folds, LOMO lower bound > 0, and DSR >= {dsrMin:0.00}. {configs.Count} configurations."));
        md.AppendLine();
        if (stats.TryGetValue("earnings beat (EPS/sales vs estimate)", out var eb) && !holds.Contains("earnings beat (EPS/sales vs estimate)"))
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"Not selected: earnings beat holds on the mean (close {Pct(eb.MeanRet)}, D+2 {Pct(eb.MeanD2)}) but gives back more from its high (median {Pct(eb.MedFade)}); its 09:30-open entry was already tested in IP-T492 (catalyst-split: +0.79% net RTH to the D+2 close). The selection rule was fixed before this run's trades were read; a dry run on a scratch copy of the ledger produced these same {configs.Count} configurations."));
        md.AppendLine();
        md.AppendLine("| cluster | entry | exit | trades | days | gross | RTH cost | net RTH [95%] | net frozen [95%] | net commission only | per fold (RTH) | folds > 0 | LOMO lo | win-rate proxy: worst 1% | DSR | finalist RTH/frozen/comm |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var tradeJson = new List<object>();
        foreach (var c in configs)
        {
            var st = c.St;
            string N(int l) => st.Net[l] is { } m ? DeepBounds(m.Mean, m.Lo, m.Hi) : "n/a";
            bool dsrOk = Dsr(st) >= dsrMin;
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {c.Cluster} | {entries[c.Entry]} | {exits[c.Exit]} | {st.Trades:N0} | {st.Days:N0} | {Pct(st.Gross)} | {Pct(st.MeanCost)} | {N(0)} | {N(1)} | {Pct(st.Net[2]?.Mean)} | {string.Join(" / ", st.Folds[0].Select(Pct))} | {st.Folds[0].Count(f => f > 0)}/{folds.Count} | {Pct(st.LomoLo[0])} | {Pct(st.Worst1)} | {Num(Dsr(st))} | {string.Join("/", st.Finalist.Select(f => f && dsrOk ? "Y" : f ? "y" : "-"))} |"));
            trials.Add(new ExploreTrial
            {
                Track = "catalyst-ml-trade", Config = $"long cluster={c.Cluster} entry={entries[c.Entry]} exit={exits[c.Exit]}", Metric = "meanNetReturn (long, regular-hours cost, all train days)",
                FoldValues = st.Folds[0], Pooled = st.Net[0]?.Mean, Rows = st.Trades,
                Note = string.Create(CultureInfo.InvariantCulture, $"lo={Pct(st.Net[0]?.Lo)} hi={Pct(st.Net[0]?.Hi)} days={st.Days} gross={Pct(st.Gross)} netFrozen={Pct(st.Net[1]?.Mean)} netComm={Pct(st.Net[2]?.Mean)} lomoLo={Pct(st.LomoLo[0])} dsr={Num(Dsr(st))}"),
            });
            tradeJson.Add(new { cluster = c.Cluster, entry = entries[c.Entry], exit = exits[c.Exit], st.Trades, st.Days, gross = st.Gross, meanRthCost = st.MeanCost, net = st.Net.Select(m => m is { } v ? new { v.Mean, v.Lo, v.Hi } : null).ToList(), folds = st.Folds[0], lomoLo = st.LomoLo[0], worst1 = st.Worst1, sharpe = st.Moments.Sharpe, dsr = Dsr(st), finalist = st.Finalist });
        }
        md.AppendLine();
        for (int l = 0; l < 3; l++)
        {
            var fin = configs.Where(c => c.St.Finalist[l]).ToList();
            md.AppendLine($"- Finalist bar at the {OrbCostNames[l]} cost: {fin.Count} configuration(s) before DSR, {fin.Count(c => Dsr(c.St) >= dsrMin)} after{(fin.Count == 0 ? "" : ": " + string.Join("; ", fin.Select(c => $"{c.Cluster} / {entries[c.Entry]} / {exits[c.Exit]} {Pct(c.St.Net[l]!.Value.Mean)}, DSR {Num(Dsr(c.St))}")))}.");
        }
        md.AppendLine($"- Positive net at the regular-hours cost: {configs.Count(c => c.St.Net[0]?.Mean > 0)} of {configs.Count}; lower bound above 0: {configs.Count(c => c.St.Net[0]?.Lo > 0)}.");
        md.AppendLine();
        json["trades"] = tradeJson;
        json["dsr"] = new { nTrials, varianceOfTrialSharpes = varSr, dsrMin };
        json["holdsGains"] = holds;

        // ── 6. the keyword classifier against the clusters ──
        md.AppendLine("## 6. The keyword classifier (`CatalystTypes`) against the clusters");
        md.AppendLine();
        md.AppendLine("Per type: rows it tags before (the classifier at export time) and after this ticket's fixes (`CatalystTypes` now), and the share of its tags that land in a cluster the cluster map (`CatalystClusterMap`) does not tie to that type (\"off-cluster\": a false-positive proxy; templated coverage such as movers lists and analyst notes is where most keyword misfires land). Then the cluster-to-type map.");
        md.AppendLine();
        md.AppendLine("| type | rows before | off-cluster before | rows after | off-cluster after | lift (+50% in 240 min) before | after |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        var typeJson = new List<object>();
        for (int k = 0; k < CatalystTypes.Keys.Length; k++)
        {
            int bit = 1 << k;
            var key = CatalystTypes.Keys[k];
            (int N, double Off, double Lift) Of(Func<MlRow, int> bits)
            {
                var l = rows.Where(r => (bits(r) & bit) != 0).ToList();
                if (l.Count == 0) return (0, double.NaN, double.NaN);
                double off = l.Average(r => CatalystClusterMap.TypesOf(r.Name).Contains(key) ? 0.0 : 1.0);
                var wr = l.Where(r => double.IsFinite(r.Run240)).ToList();
                return (l.Count, off, wr.Count == 0 ? double.NaN : wr.Average(r => r.Run240 >= 0.5 ? 1.0 : 0.0) / baseRun);
            }
            var before = Of(r => r.OldBits);
            var after = Of(r => r.NewBits);
            md.AppendLine($"| {CatalystTypes.Names[k]} | {before.N:N0} | {Sh(before.Off)} | {after.N:N0} | {Sh(after.Off)} | {Num(before.Lift, "0.00")} | {Num(after.Lift, "0.00")} |");
            typeJson.Add(new { key, before, after });
        }
        md.AppendLine();
        md.AppendLine("Cluster -> type map (`CatalystClusterMap`, IdiotProof.Evidence):");
        md.AppendLine();
        foreach (var name in stats.Keys.OrderBy(n => n, StringComparer.Ordinal))
            md.AppendLine($"- {name}: {(CatalystClusterMap.TypesOf(name) is { Count: > 0 } tl ? string.Join(", ", tl) : "(none: coverage or no catalyst type)")}");
        md.AppendLine();
        json["classifier"] = typeJson;
        md.AppendLine("Exploration only (train 2021-11-01..2024-10-31). Nothing after the last train day is read: news to its 16:00, daily bars to its close, and exits past it are blank. The python step reads only this train event table.");
        return Write("catalyst-ml", md, trials, json);
    }
}
