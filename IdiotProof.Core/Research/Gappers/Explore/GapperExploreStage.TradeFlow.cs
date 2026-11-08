using System.Globalization;
using System.Text;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    public static readonly TimeSpan TradeFlowFromEt = new(8, 0, 0);
    public static readonly TimeSpan TradeFlowToEt = new(10, 30, 0);
    private const int TradeFlowSymbolsPerRequest = 20;

    public string TradeFlowDir() => Path.Combine(Path.GetDirectoryName(CacheDir().TrimEnd('\\', '/'))!, "tradeflow");

    private string TradeFlowFile(string symbol, DateOnly day) => Path.Combine(TradeFlowDir(), symbol.ToUpperInvariant(), $"{day:yyyy-MM-dd}.csv");

    /// <summary>
    /// `explore tradeflow-pull` (IP-T460): SIP trade prints for every train gapper morning, 08:00-10:30 ET,
    /// aggregated to minutes (prints, shares, odd lots, off-exchange, sub-penny retail buy/sell) and kept per
    /// symbol-day under tradeflow/; resumable (a day already on disk is skipped), paced by the study's Alpaca gate.
    /// </summary>
    private async Task<(List<string>, int, string?)> TradeFlowPullAsync(ExploreRequest req, CancellationToken ct)
    {
        var days = (await s.StudyDaysAsync("train", req.From, req.To, ct)).Where(u => u.IsGapper && u.IsCandidate)
            .Where(u => !File.Exists(TradeFlowFile(u.Symbol, u.DateEt))).ToList();
        var keys = await s.ResolveKeysAsync(req.Owner, ct);
        await using var feed = s.Feed(keys);
        int pages = 0, written = 0;
        foreach (var date in days.GroupBy(u => u.DateEt).OrderBy(g => g.Key))
        {
            var from = MinuteBarMath.ToUtc(date.Key, TradeFlowFromEt);
            var to = MinuteBarMath.ToUtc(date.Key, TradeFlowToEt);
            foreach (var chunk in date.Select(u => u.Symbol).Distinct().Chunk(TradeFlowSymbolsPerRequest))
            {
                var r = await feed.GetTradesForSymbolsAsync(chunk, from, to, s.RateGate.WaitAsync, ct);
                pages += r.Pages;
                foreach (var symbol in chunk)
                {
                    var prints = r.Trades.TryGetValue(symbol, out var list) ? list : [];
                    var file = TradeFlowFile(symbol, date.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllTextAsync(file, TradeFlow.ToCsv(TradeFlow.Aggregate(prints.Select(p => (p.Utc, p.Price, p.Size, p.Exchange)))), ct);
                    written++;
                }
            }
            s.Logger.LogInformation("[{Now}] explore tradeflow-pull: {Day} done, {Written}/{Total} gapper mornings, {Pages} pages", GapperStudyServices.Et(s.UtcNow), date.Key, written, days.Count, pages);
        }
        return ([$"{written:N0} gapper mornings written to {TradeFlowDir()} ({pages:N0} pages)"], 0, TradeFlowDir());
    }

    /// <summary>
    /// `explore tradeflow` (IP-T460, the H17 footprint, descriptive): on train gapper mornings with trade flow,
    /// when the 5-minute sub-penny retail imbalance collapses (from >= +0.2 to below 0, armed from 08:30)
    /// against the time of the high, the price at the collapse against the high, and odd-lot / off-exchange /
    /// retail flow in the 15 minutes before against after the high.
    /// </summary>
    private async Task<(List<string>, int, string?)> TradeFlowAsync(ExploreRequest req, CancellationToken ct)
    {
        var store = await s.StoreAsync(ct);
        var days = (await s.StudyDaysAsync("train", req.From, req.To, ct)).Where(u => u.IsGapper && u.IsCandidate && File.Exists(TradeFlowFile(u.Symbol, u.DateEt))).ToList();
        var leads = new List<double>(); var captured = new List<double>();
        var before = new List<(double Odd, double Off, double Imb)>(); var after = new List<(double Odd, double Off, double Imb)>();
        int noCollapse = 0, noHigh = 0;
        foreach (var u in days)
        {
            ct.ThrowIfCancellationRequested();
            var minutes = TradeFlow.FromCsv(File.ReadLines(TradeFlowFile(u.Symbol, u.DateEt)));
            var bars = store.ReadDay(u.Symbol, u.DateEt).Where(b => b.Volume > 0 && b.StartUtc >= MinuteBarMath.ToUtc(u.DateEt, TradeFlowFromEt) && b.EndUtc <= MinuteBarMath.ToUtc(u.DateEt, TradeFlowToEt))
                .OrderBy(b => b.StartUtc).ToList();
            if (bars.Count == 0 || minutes.Count == 0) { noHigh++; continue; }
            var hodBar = bars.MaxBy(b => b.High)!;
            var hod = hodBar.StartUtc;
            (double, double, double) Flow(DateTime a, DateTime b)
            {
                var w = minutes.Where(m => m.StartUtc >= a && m.StartUtc < b).ToList();
                double sh = w.Sum(m => m.Shares);
                return (sh > 0 ? w.Sum(m => m.OddLotShares) / sh : double.NaN, sh > 0 ? w.Sum(m => m.OffExchangeShares) / sh : double.NaN, TradeFlow.Imbalance(minutes, a, b) ?? double.NaN);
            }
            before.Add(Flow(hod.AddMinutes(-15), hod));
            after.Add(Flow(hod.AddMinutes(1), hod.AddMinutes(16)));
            var collapse = TradeFlow.ImbalanceCollapse(minutes, MinuteBarMath.ToUtc(u.DateEt, new TimeSpan(8, 30, 0)), MinuteBarMath.ToUtc(u.DateEt, TradeFlowToEt));
            if (collapse is not { } c) { noCollapse++; continue; }
            leads.Add((hod - c).TotalMinutes);
            var at = bars.LastOrDefault(b => b.EndUtc <= c);
            if (at is not null && hodBar.High > 0) captured.Add((double)(at.Close / hodBar.High));
        }

        static string Pct(double v) => double.IsNaN(v) ? "n/a" : v.ToString("P1", CultureInfo.InvariantCulture);
        static double Med(IEnumerable<double> v) { var l = v.Where(double.IsFinite).OrderBy(x => x).ToList(); return l.Count == 0 ? double.NaN : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; }
        var md = new StringBuilder("# Explore: trade flow around the high (H17 footprint, train gapper mornings, descriptive)\n\n");
        md.AppendLine($"Mornings with trade flow: {days.Count:N0}; no bars or prints: {noHigh:N0}; imbalance never collapsed 08:30-10:30: {noCollapse:N0}; collapsed: {leads.Count:N0}.");
        md.AppendLine();
        md.AppendLine("| measure | value |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| median minutes from collapse to the high (positive = collapse first) | {Med(leads):0.0} |");
        md.AppendLine($"| collapse 0-15 min before the high | {Pct(leads.Count == 0 ? double.NaN : (double)leads.Count(x => x is >= 0 and <= 15) / leads.Count)} |");
        md.AppendLine($"| collapse after the high | {Pct(leads.Count == 0 ? double.NaN : (double)leads.Count(x => x < 0) / leads.Count)} |");
        md.AppendLine($"| median close at the collapse as a share of the high | {Pct(Med(captured))} |");
        md.AppendLine($"| odd-lot share, 15 min before / after the high | {Pct(Med(before.Select(b => b.Odd)))} / {Pct(Med(after.Select(b => b.Odd)))} |");
        md.AppendLine($"| off-exchange share, before / after | {Pct(Med(before.Select(b => b.Off)))} / {Pct(Med(after.Select(b => b.Off)))} |");
        md.AppendLine($"| retail imbalance, before / after | {Med(before.Select(b => b.Imb)):+0.00;-0.00} / {Med(after.Select(b => b.Imb)):+0.00;-0.00} |");
        md.AppendLine();
        md.AppendLine("Sub-penny signing (BJZZ 2021) is coarse (Barber et al. 2024); read the imbalance as a direction, not a size. No trial is logged: descriptive.");
        return Write("tradeflow", md, [], new { days = days.Count, collapsed = leads.Count, noCollapse, medianLead = Med(leads), medianCaptured = Med(captured) });
    }
}
