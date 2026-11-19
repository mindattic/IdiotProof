using System.Globalization;
using System.Net;
using System.Text;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    private const int AuctionSymbolsPerRequest = 100;

    /// <summary>The paper gapper strategies share the Alpaca data API through the Hub in premarket; study pulls wait out 03:55-09:35 ET on weekdays.</summary>
    public static readonly TimeSpan PullQuietFromEt = new(3, 55, 0);
    public static readonly TimeSpan PullQuietToEt = new(9, 35, 0);

    public string AuctionDir() => Path.Combine(Path.GetDirectoryName(CacheDir().TrimEnd('\\', '/'))!, "auctions");

    private string AuctionFile(DateOnly day) => Path.Combine(AuctionDir(), day.Year.ToString(CultureInfo.InvariantCulture), $"{day:yyyy-MM-dd}.csv");

    /// <summary>Train gapper days (Tier20 and up) and their matched controls: the auction-print scope (strategy-refined.md §3.2 P1).</summary>
    private async Task<List<(DateOnly Day, List<(string Symbol, string Role)> Symbols)>> AuctionScopeAsync(ExploreRequest req, CancellationToken ct) =>
        (await s.StudyDaysAsync("train", req.From, req.To, ct))
            .Where(u => (u.IsGapper && u.GapTier is not null) || u.ControlForSymbol is not null)
            .GroupBy(u => u.DateEt).OrderBy(g => g.Key)
            .Select(g => (g.Key, g.GroupBy(u => u.Symbol, StringComparer.Ordinal)
                .Select(x => x.First()).Select(u => (u.Symbol.ToUpperInvariant(), u.IsGapper && u.GapTier is not null ? u.GapTier! : "control")).ToList()))
            .ToList();

    /// <summary>Waits until the premarket window is over when a pull would otherwise run inside it (weekdays only; holidays are not special-cased).</summary>
    private async Task WaitOutPremarketAsync(string what, CancellationToken ct)
    {
        while (true)
        {
            var et = EasternTime.FromUtc(s.UtcNow);
            if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || et.TimeOfDay < PullQuietFromEt || et.TimeOfDay >= PullQuietToEt) return;
            var wait = et.Date + PullQuietToEt - et;
            s.Logger.LogInformation("[{Now}] {What}: paused through premarket (paper gappers share the Alpaca data API), resuming in {Minutes:0} min", GapperStudyServices.Et(s.UtcNow), what, wait.TotalMinutes);
            await Task.Delay(wait, ct);
        }
    }

    /// <summary>
    /// `explore auctions-pull` (IP-T472): Alpaca opening and closing auction prints for every train gapper day (Tier20,
    /// Tier50, Tier100) and its matched control, one file per day under auctions/yyyy/, every symbol asked recorded
    /// (a symbol with no print gets a row of kind '-'). Resumable (a day on disk is skipped), paced by the study's
    /// Alpaca gate, and paused through premarket. Train only: validation and holdout days wait for amendment 04.
    /// </summary>
    private async Task<(List<string>, int, string?)> AuctionsPullAsync(ExploreRequest req, CancellationToken ct)
    {
        var scope = await AuctionScopeAsync(req, ct);
        var todo = scope.Where(d => req.Force || !File.Exists(AuctionFile(d.Day))).ToList();
        s.Logger.LogInformation("[{Now}] explore auctions-pull: {Todo} of {Days} train days to fetch ({Symbols:N0} symbol-days)", GapperStudyServices.Et(s.UtcNow), todo.Count, scope.Count, todo.Sum(d => d.Symbols.Count));
        var keys = await s.ResolveKeysAsync(req.Owner, ct);
        using var source = new AlpacaAuctionSource(keys, s.Params.DataTiers.AlpacaFeed, s.RateGate);
        int written = 0, failed = 0, prints = 0;
        foreach (var (day, symbols) in todo)
        {
            await WaitOutPremarketAsync("explore auctions-pull", ct);
            var role = symbols.ToDictionary(x => x.Symbol, x => x.Role, StringComparer.Ordinal);
            var rows = new List<GapperAuctionPrint>();
            try
            {
                foreach (var chunk in symbols.Select(x => x.Symbol).Chunk(AuctionSymbolsPerRequest))
                    rows.AddRange(await source.GetAsync(chunk, MinuteBarMath.ToUtc(day, TimeSpan.Zero), MinuteBarMath.ToUtc(day, new TimeSpan(23, 59, 0)), ct));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                failed++;
                s.Logger.LogWarning("explore auctions-pull: {Day} failed ({Message}); the day is left for a rerun", day, ex.Message);
                if (ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }) throw;
                continue;
            }
            var kept = rows.Where(r => role.ContainsKey(r.Symbol)).Select(r => r with { Role = role[r.Symbol] }).ToList();
            var have = kept.Select(r => r.Symbol).ToHashSet(StringComparer.Ordinal);
            kept.AddRange(symbols.Where(x => !have.Contains(x.Symbol)).Select(x => new GapperAuctionPrint(x.Symbol, x.Role, '-', null, 0, 0, "", "")));
            var file = AuctionFile(day);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // Write then rename: a day on disk is skipped forever, so a crash mid-write must not leave a truncated file.
            await File.WriteAllTextAsync(file + ".tmp", GapperAuctionPrints.ToCsv(kept.OrderBy(r => r.Symbol, StringComparer.Ordinal).ThenBy(r => r.Kind).ThenBy(r => r.TimeUtc)), ct);
            File.Move(file + ".tmp", file, true);
            written++;
            prints += kept.Count(r => r.Kind != '-');
            if (written % 25 == 0 || written == todo.Count)
                s.Logger.LogInformation("[{Now}] explore auctions-pull: {Done}/{Total} days ({Day}), {Prints:N0} prints, {Requests:N0} requests", GapperStudyServices.Et(s.UtcNow), written, todo.Count, day, prints, source.Requests);
        }
        return ([$"{written:N0} train days written to {AuctionDir()} ({prints:N0} prints, {source.Requests:N0} requests); {failed:N0} days failed and are left for a rerun; {scope.Count - todo.Count:N0} were already on disk"], 0, AuctionDir());
    }

    /// <summary>
    /// `explore auctions` (IP-T472, descriptive): how far the official opening cross is from the 09:30 minute bar's
    /// open (the price the exit-route simulator's opg route uses until now) and the official close from the 15:59
    /// bar's close, for gapper days by tier and their controls; the opening cross's size against the 09:30 minute's
    /// volume and the premarket volume, and the cross against the premarket high on gapper days. No trial is logged.
    /// </summary>
    private async Task<(List<string>, int, string?)> AuctionsAsync(ExploreRequest req, CancellationToken ct)
    {
        var scope = (await AuctionScopeAsync(req, ct)).Where(d => File.Exists(AuctionFile(d.Day))).ToList();
        if (scope.Count == 0) return ([$"no auction files under {AuctionDir()}; run `explore auctions-pull` first"], 0, null);
        var store = await s.StoreAsync(ct);
        var rows = new List<(string Group, double? OpenDiff, double? CloseDiff, double? CrossShareOfMinute, double? CrossShareOfPm, double? CrossVsPmHigh, bool HasOpen, bool HasClose)>();
        foreach (var (day, _) in scope)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var g in GapperAuctionPrints.FromCsv(File.ReadLines(AuctionFile(day))).GroupBy(p => p.Symbol, StringComparer.Ordinal))
            {
                var group = g.First().Role;
                var open = GapperAuctionPrints.Official(g, 'O');
                var close = GapperAuctionPrints.Official(g, 'C');
                var bars = store.ReadDay(g.Key, day);
                var rthOpen = MinuteBarMath.ToUtc(day, MinuteBarMath.RthOpenEt);
                var bar0930 = bars.FirstOrDefault(b => b.StartUtc == rthOpen && b.Volume > 0 && b.Open > 0);
                var bar1559 = bars.FirstOrDefault(b => b.StartUtc == MinuteBarMath.ToUtc(day, new TimeSpan(15, 59, 0)) && b.Volume > 0 && b.Close > 0);
                var pm = bars.Where(b => b.StartUtc >= MinuteBarMath.ToUtc(day, MinuteBarMath.SessionStartEt) && b.StartUtc < rthOpen).ToList();
                double pmVol = pm.Sum(b => (double)b.Volume), pmHigh = pm.Count > 0 ? pm.Max(b => (double)b.High) : 0;
                rows.Add((group,
                    open is not null && bar0930 is not null ? open.Price / (double)bar0930.Open - 1 : null,
                    close is not null && bar1559 is not null ? close.Price / (double)bar1559.Close - 1 : null,
                    open is not null && bar0930 is not null ? open.Size / (double)bar0930.Volume : null,
                    open is not null && pmVol > 0 ? open.Size / pmVol : null,
                    open is not null && pmHigh > 0 && group != "control" ? open.Price / pmHigh - 1 : null,
                    open is not null, close is not null));
            }
        }

        static double Med(IEnumerable<double?> v) { var l = v.Where(x => x is { } d && double.IsFinite(d)).Select(x => x!.Value).OrderBy(x => x).ToList(); return l.Count == 0 ? double.NaN : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; }
        static double Share(IEnumerable<double?> v, Func<double, bool> pred) { var l = v.Where(x => x is { } d && double.IsFinite(d)).Select(x => x!.Value).ToList(); return l.Count == 0 ? double.NaN : (double)l.Count(pred) / l.Count; }
        static string P(double v, string fmt = "+0.00%;-0.00%") => double.IsNaN(v) ? "n/a" : v.ToString(fmt, CultureInfo.InvariantCulture);

        var groups = new[] { "Tier20", "Tier50", "Tier100", "control" }.Where(g => rows.Any(r => r.Group == g)).ToList();
        var md = new StringBuilder("# Explore: opening and closing auction prints (train gapper days and controls, descriptive)\n\n");
        md.AppendLine($"Days with auction files: {scope.Count:N0}; symbol-days: {rows.Count:N0}. Official print: Q (open) / M (close) first, then O / 6, else the largest print. Bars are the study's raw SIP minute bars.");
        md.AppendLine();
        md.AppendLine("| group | symbol-days | with an open cross | with a close cross | median open cross / 09:30 bar open - 1 | median abs | abs > 0.5% | abs > 2% | median close cross / 15:59 bar close - 1 | median abs | abs > 0.5% | open cross size / 09:30 minute volume | open cross size / premarket volume | open cross / premarket high - 1 |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var json = new List<object>();
        foreach (var g in groups)
        {
            var r = rows.Where(x => x.Group == g).ToList();
            var o = r.Select(x => x.OpenDiff).ToList();
            var c = r.Select(x => x.CloseDiff).ToList();
            var oAbs = o.Select(x => x is { } d ? Math.Abs(d) : (double?)null).ToList();
            var cAbs = c.Select(x => x is { } d ? Math.Abs(d) : (double?)null).ToList();
            md.AppendLine($"| {g} | {r.Count:N0} | {r.Count(x => x.HasOpen):N0} | {r.Count(x => x.HasClose):N0} | {P(Med(o))} | {P(Med(oAbs), "0.00%")} | {P(Share(oAbs, d => d > 0.005), "0.0%")} | {P(Share(oAbs, d => d > 0.02), "0.0%")} | {P(Med(c))} | {P(Med(cAbs), "0.00%")} | {P(Share(cAbs, d => d > 0.005), "0.0%")} | {P(Med(r.Select(x => x.CrossShareOfMinute)), "0.0%")} | {P(Med(r.Select(x => x.CrossShareOfPm)), "0.0%")} | {(g == "control" ? "-" : P(Med(r.Select(x => x.CrossVsPmHigh))))} |");
            json.Add(new
            {
                group = g, n = r.Count, withOpen = r.Count(x => x.HasOpen), withClose = r.Count(x => x.HasClose),
                openDiffMedian = Med(o), openAbsMedian = Med(oAbs), openAbsOver05 = Share(oAbs, d => d > 0.005), openAbsOver2 = Share(oAbs, d => d > 0.02),
                closeDiffMedian = Med(c), closeAbsMedian = Med(cAbs), closeAbsOver05 = Share(cAbs, d => d > 0.005),
                crossShareOfMinute = Med(r.Select(x => x.CrossShareOfMinute)), crossShareOfPm = Med(r.Select(x => x.CrossShareOfPm)), crossVsPmHigh = Med(r.Select(x => x.CrossVsPmHigh)),
            });
        }
        md.AppendLine();
        md.AppendLine("Read the open columns as the error of the opg route's 09:30-bar-open approximation in `GapperExitRouteSimulator`: a cross below the bar open makes the simulated auction sell too generous. Descriptive; no trial is logged.");
        return Write("auctions", md, [], json);
    }
}
