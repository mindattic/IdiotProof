using System.Globalization;
using System.Text;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Services;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// <c>explore catalyst-types</c> (IP-T487, gapper study amendment 08), TRAIN only, descriptive: run size, run duration and
/// fade by catalyst type. Table A reads the `explore catalyst-events` table (one row per symbol and stored news item or
/// filing on a train universe session with minute bars) and gives each item the one <see cref="CatalystPreClass"/> the
/// classifier stores. Table B takes every train gapper day of the frozen study and names its catalyst from the items
/// published after D-1 16:00 ET and at or before the first +20% touch. Long side only; one ledger trial per type and table.
/// </summary>
public sealed partial class GapperExploreStage
{
    private const string CoverageOnlyGroup = "coverage only (no event item)";
    private const string NoItemGroup = "no catalyst (no stored item)";
    private const string LoiGroup = "flag: non-binding LOI / MOU (any type)";

    private sealed record TypeItem(DateOnly Day, string Symbol, string Kind, CatalystPreClass Type, bool Loi, int Fold, double Run, double Minutes, double Fade, double Kept);

    private sealed record TypeDay(DateOnly Day, string Symbol, string Group, bool Loi, double Run, double Minutes, double HodMinute, double Fade, double Kept);

    private static double Quantile(IEnumerable<double> values, double q)
    {
        var v = values.Where(double.IsFinite).OrderBy(x => x).ToList();
        if (v.Count == 0) return double.NaN;
        double pos = q * (v.Count - 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        return v[lo] + (v[hi] - v[lo]) * (pos - lo);
    }

    private static double ShareAtLeast(IReadOnlyCollection<double> values, double level)
    {
        var v = values.Where(double.IsFinite).ToList();
        return v.Count == 0 ? double.NaN : (double)v.Count(x => x >= level) / v.Count;
    }

    private async Task<(List<string>, int, string?)> CatalystTypesAsync(ExploreRequest req, CancellationToken ct)
    {
        var train = s.Params.Split("train");
        var ev = Path.Combine(CatalystMlDir, "events.csv");
        if (!File.Exists(ev)) return ([$"{ev} is missing: run `explore catalyst-events` first"], 0, null);
        var trainDays = MarketCalendar.TradingDays(train.From, train.To);
        var folds = ExploreFolds.Build(trainDays, foldMonths.Initial, foldMonths.Eval);
        string Pct(double v) => DeepPct(double.IsFinite(v) ? v : null);
        string Sh(double v) => DeepShare(double.IsFinite(v) ? v : null);
        string Min(double v) => double.IsFinite(v) ? v.ToString("0", CultureInfo.InvariantCulture) : "n/a";

        // ── Table A: items ──
        var items = new List<TypeItem>();
        int excluded = 0, read = 0;
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
                read++;
                if (N("excluded") != 0) { excluded++; continue; }
                bool news = c[ix["kind"]] == "news";
                var type = news
                    ? CatalystPreClassifier.TypeOf(c[ix["headline"]], null, null, c[ix["pre_class"]])
                    : CatalystPreClassifier.TypeOf(null, c[ix["form"]], c[ix["items"]].Split(';', StringSplitOptions.RemoveEmptyEntries), null);
                int hi = N("hi_min"), start = N("start_min");
                items.Add(new TypeItem(day, c[ix["symbol"]], c[ix["kind"]], type.Class, type.NonBindingLoi, N("eval_fold"), D("run_close"),
                    c[ix["hi_min"]].Length > 0 && hi >= 0 ? Math.Max(0, hi - start) : double.NaN, D("fade"), D("ret_close")));
            }
        }
        s.Logger.LogInformation("[{Now}] explore catalyst-types: {Rows} item rows ({Excluded} on excluded universe days dropped)", GapperStudyServices.Et(s.UtcNow), items.Count, excluded);

        // Not the shared Header: that one says "Weighted (amendment 01 population)", and nothing here is weighted.
        var md = new StringBuilder();
        md.AppendLine("# Explore: run size, run duration and fade by catalyst type (IP-T487, amendment 08)");
        md.AppendLine();
        md.AppendLine("Train only. Not weighted: Table A counts every stored item once, Table B is a census of train gapper days. The folds give the ledger's per-fold values only:");
        foreach (var f in folds) md.AppendLine($"- {f.Label}");
        md.AppendLine();
        var trials = new List<ExploreTrial>();
        var json = new Dictionary<string, object?>();
        md.AppendLine("Descriptive only, long side only (amendment 08): a type that looks tradable still has to go through costs, the ledger, a registered amendment and validation.");
        md.AppendLine();
        md.AppendLine("## A. Per item: what the stock did after each kind of news item or filing");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Unit: one row of the `explore catalyst-events` table ({read:N0} rows; {excluded:N0} on excluded universe days dropped, {items.Count:N0} used: {items.Count(i => i.Kind == "news"):N0} news, {items.Count(i => i.Kind != "news"):N0} filings other than forms 3/4/5), event sessions {items.Min(i => i.Day):yyyy-MM-dd}..{items.Max(i => i.Day):yyyy-MM-dd}. Type: the stored catalyst type (`CatalystPreClassifier.TypeOf`: headline + version 1 class for news, form + 8-K items for filings; one label by the amendment's priority). Run size = highest high from the first full minute after publication to 16:00 over the last trade before publication, minus 1. Duration = minutes from that first minute to the minute of the high. Fade = the 16:00 close over that high, minus 1. Kept = the 16:00 close over the reference, minus 1, before costs; its mean has a day-clustered 95% interval. The sessions are the ones the study holds minute bars for (gapper candidate days, their neighbours, controls and the population sample), so the levels run higher than the market's: compare the types with each other. Sorted by median run size."));
        md.AppendLine();
        md.AppendLine("| catalyst type | items | news / filings | symbol-days | median run | p75 run | p90 run | run >= +20% | run >= +50% | median minutes to high | median fade | median kept | mean kept [95%] |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var aJson = new List<object>();
        void RowA(string name, List<TypeItem> l, bool trial)
        {
            if (l.Count == 0) return;
            var runs = l.Select(i => i.Run).Where(double.IsFinite).ToList();
            double med = Quantile(runs, 0.5);
            var kept = ExploreDerived.ClusteredMean(l.Select(i => (i.Day, i.Kept, 1.0)));
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {l.Count:N0} | {l.Count(i => i.Kind == "news"):N0} / {l.Count(i => i.Kind != "news"):N0} | {l.Select(i => (i.Symbol, i.Day)).Distinct().Count():N0} | {Pct(med)} | {Pct(Quantile(runs, 0.75))} | {Pct(Quantile(runs, 0.9))} | {Sh(ShareAtLeast(runs, 0.2))} | {Sh(ShareAtLeast(runs, 0.5))} | {Min(Quantile(l.Select(i => i.Minutes), 0.5))} | {Pct(Quantile(l.Select(i => i.Fade), 0.5))} | {Pct(Quantile(l.Select(i => i.Kept), 0.5))} | {(kept is { } k ? DeepBounds(k.Mean, k.Lo, k.Hi) : "n/a")} |"));
            aJson.Add(new
            {
                type = name, items = l.Count, news = l.Count(i => i.Kind == "news"), medianRun = med, p75Run = Quantile(runs, 0.75), p90Run = Quantile(runs, 0.9),
                run20 = ShareAtLeast(runs, 0.2), run50 = ShareAtLeast(runs, 0.5), medianMinutesToHigh = Quantile(l.Select(i => i.Minutes), 0.5),
                medianFade = Quantile(l.Select(i => i.Fade), 0.5), medianKept = Quantile(l.Select(i => i.Kept), 0.5), meanKept = kept?.Mean, meanKeptLo = kept?.Lo, meanKeptHi = kept?.Hi,
            });
            if (!trial) return;
            var perFold = folds.Select(f => (double?)Quantile(l.Where(i => i.Fold == f.Number).Select(i => i.Run), 0.5)).Select(v => v is { } x && double.IsFinite(x) ? v : null).ToList();
            trials.Add(new ExploreTrial
            {
                Track = "catalyst-types", Config = $"item type={name}", Metric = "median run to the 16:00 high over the publication reference (descriptive)", FoldValues = perFold,
                Pooled = double.IsFinite(med) ? med : null, Rows = l.Count,
                Note = string.Create(CultureInfo.InvariantCulture, $"minutesToHigh={Min(Quantile(l.Select(i => i.Minutes), 0.5))} fade={Pct(Quantile(l.Select(i => i.Fade), 0.5))} kept={Pct(Quantile(l.Select(i => i.Kept), 0.5))}"),
            });
        }
        RowA("all items", items, false);
        foreach (var g in items.GroupBy(i => i.Type).OrderByDescending(g => Quantile(g.Select(i => i.Run), 0.5)))
            RowA(CatalystPreClassNames.Label(g.Key), g.ToList(), true);
        RowA(LoiGroup, items.Where(i => i.Loi).ToList(), true);
        md.AppendLine();
        json["items"] = aJson;

        // ── Table B: gapper days ──
        var events = new List<(string Symbol, DateOnly Day, double Pc, double High, TimeOnly Touch, TimeOnly Hod)>();
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            foreach (var e in await db.GapperStudyEvents.AsNoTracking().Where(e => e.Split == "train" && e.DateEt >= train.From && e.DateEt <= train.To)
                         .Select(e => new { e.Symbol, e.DateEt, e.AdjPrevClose, e.PmHigh, e.RthHigh, e.Tier20TouchTimeEt, e.HodTimeEt }).ToListAsync(ct))
                if (e.Tier20TouchTimeEt is { } touch && e.HodTimeEt is { } hod && e.AdjPrevClose > 0)
                    events.Add((e.Symbol, e.DateEt, (double)e.AdjPrevClose, (double)Math.Max(e.PmHigh, e.RthHigh ?? 0), touch, hod));
        var symbols = events.Select(e => e.Symbol).ToHashSet(StringComparer.Ordinal);
        var needed = events.Select(e => (e.Symbol, e.Day)).ToHashSet();
        var closes = new Dictionary<(string, DateOnly), double>();
        var symbolList = symbols.OrderBy(x => x, StringComparer.Ordinal).ToList();
        for (int i = 0; i < symbolList.Count; i += 400)
        {
            var batch = symbolList.Skip(i).Take(400).ToList();
            await using var db = await s.Db.CreateDbContextAsync(ct);
            foreach (var b in await db.GapperStudyDailyBars.AsNoTracking().Where(b => batch.Contains(b.Symbol) && b.DateEt >= train.From && b.DateEt <= train.To)
                         .Select(b => new { b.Symbol, b.DateEt, b.Close }).ToListAsync(ct))
                if (needed.Contains((b.Symbol, b.DateEt))) closes[(b.Symbol, b.DateEt)] = (double)b.Close;
        }
        var fromUtc = MinuteBarMath.ToUtc(MarketCalendar.PreviousTradingDay(train.From), new TimeSpan(16, 0, 0));
        var toUtc = MinuteBarMath.ToUtc(train.To, new TimeSpan(16, 0, 0));
        var stored = await CatalystItemsAsync(symbols, fromUtc, toUtc, -1, ct);
        int coverageBit = CatalystTypes.Bit("coverage"), loiBit = CatalystTypes.Bit("loi");
        var days = new List<TypeDay>();
        foreach (var e in events)
        {
            var lo = MinuteBarMath.ToUtc(MarketCalendar.PreviousTradingDay(e.Day), new TimeSpan(16, 0, 0));
            var hi = MinuteBarMath.ToUtc(e.Day, e.Touch.ToTimeSpan());
            int bits = 0; bool any = false;
            if (stored.TryGetValue(e.Symbol, out var l))
                foreach (var (utc, b, _) in l)
                    if (utc > lo && utc <= hi) { any = true; bits |= b; }
            string group = !any ? NoItemGroup
                : bits == coverageBit ? CoverageOnlyGroup
                : CatalystPreClassNames.Label(CatalystPreClassifier.FromBits(bits & ~coverageBit).Type);
            double close = closes.TryGetValue((e.Symbol, e.Day), out var cl) ? cl : double.NaN;
            days.Add(new TypeDay(e.Day, e.Symbol, group, (bits & loiBit) != 0, e.High / e.Pc - 1, Math.Max(0, (e.Hod.ToTimeSpan() - e.Touch.ToTimeSpan()).TotalMinutes), e.Hod.ToTimeSpan().TotalMinutes,
                e.High > 0 ? close / e.High - 1 : double.NaN, close / e.Pc - 1));
        }

        md.AppendLine("## B. Per gapper day: what each kind of gapper did");
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Unit: one train gapper day of the frozen study (`GapperStudyEvents`, split train; {days.Count:N0} days, {days.Select(d => d.Symbol).Distinct().Count():N0} symbols, {(days.Count > 0 ? days.Min(d => d.Day) : train.From):yyyy-MM-dd}..{(days.Count > 0 ? days.Max(d => d.Day) : train.To):yyyy-MM-dd}; a census). Catalyst: the highest-priority type among the stored news (headline + version 1 class) and filings (8-K, 8-K/A, S-1, S-3, 424B3/4/5, SC 13D; a filing without a fetched acceptance time counts from 17:30 ET of its filing day) for the symbol published after D-1 16:00 ET and at or before the first +20% touch, so an item that only reports the move is not its driver. Coverage items are ignored when the day has any other item. Run size = the session high (premarket or regular hours, 04:00-16:00) over the split-adjusted prior close, minus 1. Duration = minutes from the first +20% touch to the high of day. Fade = the daily-bar close over the session high, minus 1. Kept = the daily-bar close over the prior close, minus 1. Conditional on the day having become a gapper: this says what each kind of gapper did, not how often a catalyst makes one. \"No catalyst\" = no item stored in Alpaca / Benzinga news or the EDGAR index in that window. Sorted by median run size."));
        md.AppendLine();
        md.AppendLine("| catalyst | gapper days | share of days | median run | p75 run | p90 run | run >= +50% | run >= +100% | median minutes, +20% touch to high | median high-of-day ET | median fade | median kept |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        var bJson = new List<object>();
        void RowB(string name, List<TypeDay> l, bool trial)
        {
            if (l.Count == 0) return;
            var runs = l.Select(d => d.Run).Where(double.IsFinite).ToList();
            double med = Quantile(runs, 0.5);
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {l.Count:N0} | {Sh((double)l.Count / Math.Max(1, days.Count))} | {Pct(med)} | {Pct(Quantile(runs, 0.75))} | {Pct(Quantile(runs, 0.9))} | {Sh(ShareAtLeast(runs, 0.5))} | {Sh(ShareAtLeast(runs, 1.0))} | {Min(Quantile(l.Select(d => d.Minutes), 0.5))} | {Clock(Quantile(l.Select(d => d.HodMinute), 0.5) is var h && double.IsFinite(h) ? h : null)} | {Pct(Quantile(l.Select(d => d.Fade), 0.5))} | {Pct(Quantile(l.Select(d => d.Kept), 0.5))} |"));
            bJson.Add(new
            {
                catalyst = name, days = l.Count, share = (double)l.Count / Math.Max(1, days.Count), medianRun = med, p75Run = Quantile(runs, 0.75), p90Run = Quantile(runs, 0.9),
                run50 = ShareAtLeast(runs, 0.5), run100 = ShareAtLeast(runs, 1.0), medianMinutesTouchToHigh = Quantile(l.Select(d => d.Minutes), 0.5),
                medianHodMinuteEt = Quantile(l.Select(d => d.HodMinute), 0.5), medianFade = Quantile(l.Select(d => d.Fade), 0.5), medianKept = Quantile(l.Select(d => d.Kept), 0.5),
            });
            if (!trial) return;
            var perFold = folds.Select(f => (double?)Quantile(l.Where(d => f.InEval(d.Day)).Select(d => d.Run), 0.5)).Select(v => v is { } x && double.IsFinite(x) ? v : null).ToList();
            trials.Add(new ExploreTrial
            {
                Track = "catalyst-types", Config = $"gapper-day catalyst={name}", Metric = "median session high over the prior close (descriptive)", FoldValues = perFold,
                Pooled = double.IsFinite(med) ? med : null, Rows = l.Count,
                Note = string.Create(CultureInfo.InvariantCulture, $"minutesTouchToHigh={Min(Quantile(l.Select(d => d.Minutes), 0.5))} fade={Pct(Quantile(l.Select(d => d.Fade), 0.5))} kept={Pct(Quantile(l.Select(d => d.Kept), 0.5))}"),
            });
        }
        RowB("all gapper days", days, false);
        foreach (var g in days.GroupBy(d => d.Group).OrderByDescending(g => Quantile(g.Select(d => d.Run), 0.5)))
            RowB(g.Key, g.ToList(), true);
        RowB(LoiGroup, days.Where(d => d.Loi).ToList(), true);
        md.AppendLine();
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Exploration only (train). Nothing after {train.To:yyyy-MM-dd} is read: the event table refuses a later session, and the gapper days, daily closes, news and filings are read for train dates only."));
        json["gapperDays"] = bJson;
        return Write("catalyst-types", md, trials, json);
    }
}
