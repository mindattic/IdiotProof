using System.Net;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    public static readonly DateOnly RegShoPullFrom = new(2021, 10, 1);
    public static readonly DateOnly RegShoPullTo = new(2026, 11, 6);
    /// <summary>Pause after each request: about two requests a second across all three hosts.</summary>
    private static readonly TimeSpan RegShoPullDelay = TimeSpan.FromMilliseconds(320);
    /// <summary>A source given up for the rest of a run after this many failed days in a row (e.g. a persistent 429).</summary>
    private const int RegShoMaxConsecutiveFailures = 5;
    private const string RegShoUserAgent = "IdiotProof-GapperStudy/1.0 (exploratory research; Reg SHO threshold lists and daily short volume)";

    /// <summary>The Reg SHO threshold-list day files (IP-T469); GAPPER_REGSHO_DIR overrides, default D:\IdiotProofData\regsho-threshold.</summary>
    public string RegShoDir() => Environment.GetEnvironmentVariable("GAPPER_REGSHO_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetDirectoryName(HaltsDir().TrimEnd('\\', '/'))!, "regsho-threshold");

    /// <summary>The FINRA daily short-volume files (IP-T469); GAPPER_SHORT_VOLUME_DIR overrides, default D:\IdiotProofData\finra-short-volume.</summary>
    public string ShortVolumeDir() => Environment.GetEnvironmentVariable("GAPPER_SHORT_VOLUME_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetDirectoryName(HaltsDir().TrimEnd('\\', '/'))!, "finra-short-volume");

    /// <summary>One download source; <paramref name="MinInterval"/> spaces its own requests (NYSE answers 429 at about one a second).</summary>
    private sealed record RegShoSource(string Name, Func<DateOnly, string> Url, Func<GapperExploreStage, DateOnly, string> File, Func<string, bool> Valid, TimeSpan MinInterval = default);

    private static readonly RegShoSource[] RegShoSources =
    [
        new("nasdaq threshold", d => $"https://www.nasdaqtrader.com/dynamic/symdir/regsho/nasdaqth{d:yyyyMMdd}.txt",
            (st, d) => Path.Combine(st.RegShoDir(), $"nasdaqth{d:yyyyMMdd}.txt"), RegShoThresholdHistory.LooksValid),
        // NYSE, NYSE American, NYSE Arca, NYSE Chicago and NYSE National in one list when no market is named.
        new("nyse threshold", d => $"https://www.nyse.com/api/regulatory/threshold-securities/download?selectedDate={d:yyyy-MM-dd}",
            (st, d) => Path.Combine(st.RegShoDir(), $"nyse{d:yyyyMMdd}.txt"), RegShoThresholdHistory.LooksValid, TimeSpan.FromMilliseconds(1500)),
        new("finra short volume", d => $"https://cdn.finra.org/equity/regsho/daily/CNMSshvol{d:yyyyMMdd}.txt",
            (st, d) => Path.Combine(st.ShortVolumeDir(), $"CNMSshvol{d:yyyyMMdd}.txt"), ShortVolumeHistory.LooksValid),
    ];

    /// <summary>
    /// `explore regsho-pull` (IP-T469): the Nasdaq and NYSE Reg SHO threshold lists and the FINRA consolidated
    /// daily short-volume file for every trading day in range (default 2021-10-01..2026-11-06, --from/--to
    /// override). Resumable: a file already on disk is skipped; a missing day (404, Nasdaq's redirect to its
    /// not-found page, FINRA's S3 403) is counted and left for the next run. About two requests a second; NYSE at most one every 1.5 s.
    /// </summary>
    private async Task<(List<string>, int, string?)> RegShoPullAsync(ExploreRequest req, CancellationToken ct)
    {
        Directory.CreateDirectory(RegShoDir());
        Directory.CreateDirectory(ShortVolumeDir());
        var days = MarketCalendar.TradingDays(req.From ?? RegShoPullFrom, req.To ?? RegShoPullTo);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(RegShoUserAgent);
        var fetched = RegShoSources.ToDictionary(x => x.Name, _ => 0);
        var onDisk = RegShoSources.ToDictionary(x => x.Name, _ => 0);
        var missing = RegShoSources.ToDictionary(x => x.Name, _ => new List<DateOnly>());
        var failed = RegShoSources.ToDictionary(x => x.Name, _ => 0);
        var streak = RegShoSources.ToDictionary(x => x.Name, _ => 0);
        var lastRequest = RegShoSources.ToDictionary(x => x.Name, _ => DateTime.MinValue);
        int done = 0;
        foreach (var day in days)
        {
            foreach (var src in RegShoSources)
            {
                var file = src.File(this, day);
                if (File.Exists(file)) { onDisk[src.Name]++; continue; }
                if (streak[src.Name] >= RegShoMaxConsecutiveFailures) { failed[src.Name]++; continue; }
                var wait = lastRequest[src.Name] + src.MinInterval - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                var (text, status) = await RegShoFetchAsync(http, src.Url(day), ct);
                lastRequest[src.Name] = DateTime.UtcNow;
                await Task.Delay(RegShoPullDelay, ct);
                if (text is not null && src.Valid(text))
                {
                    var tmp = file + ".tmp";
                    await File.WriteAllTextAsync(tmp, text, ct);
                    File.Move(tmp, file, overwrite: true);
                    fetched[src.Name]++;
                    streak[src.Name] = 0;
                }
                else if (status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Found or HttpStatusCode.Moved || (text is not null && !src.Valid(text)))
                {
                    missing[src.Name].Add(day);
                    streak[src.Name] = 0;
                }
                else
                {
                    failed[src.Name]++;
                    if (++streak[src.Name] == RegShoMaxConsecutiveFailures)
                        s.Logger.LogWarning("[{Now}] explore regsho-pull: {Source} failed {Count} days in a row; skipped for the rest of this run", GapperStudyServices.Et(s.UtcNow), src.Name, RegShoMaxConsecutiveFailures);
                    s.Logger.LogWarning("[{Now}] explore regsho-pull: {Source} {Day} failed ({Status}); left for the next run", GapperStudyServices.Et(s.UtcNow), src.Name, day, status);
                }
            }
            if (++done % 25 == 0 || done == days.Count)
                s.Logger.LogInformation("[{Now}] explore regsho-pull: {Day} done, {Done}/{Total} trading days; fetched {Fetched}; missing {Missing}",
                    GapperStudyServices.Et(s.UtcNow), day, done, days.Count, string.Join(", ", fetched.Select(kv => $"{kv.Key} {kv.Value}")), string.Join(", ", missing.Select(kv => $"{kv.Key} {kv.Value.Count}")));
        }
        var lines = new List<string> { $"{days.Count:N0} trading days {days.FirstOrDefault():yyyy-MM-dd}..{days.LastOrDefault():yyyy-MM-dd}" };
        foreach (var src in RegShoSources)
        {
            var m = missing[src.Name];
            lines.Add($"{src.Name}: fetched {fetched[src.Name]:N0}, already on disk {onDisk[src.Name]:N0}, missing {m.Count:N0}{(m.Count > 0 ? " (" + string.Join(", ", m.Take(12).Select(d => d.ToString("yyyy-MM-dd"))) + (m.Count > 12 ? ", ..." : "") + ")" : "")}, failed {failed[src.Name]:N0}");
        }
        lines.Add($"threshold lists in {RegShoDir()}, short volume in {ShortVolumeDir()}");
        return (lines, 0, RegShoDir());
    }

    /// <summary>One GET with three retries on a timeout, network error or 5xx (2, 10, 30 s) or a 429 (Retry-After, else 30, 60, 120 s); returns the body only on 200.</summary>
    private async Task<(string? Text, HttpStatusCode? Status)> RegShoFetchAsync(HttpClient http, string url, CancellationToken ct)
    {
        int[] backoff = [2, 10, 30];
        for (int attempt = 0; ; attempt++)
        {
            TimeSpan pause = TimeSpan.FromSeconds(backoff[Math.Min(attempt, backoff.Length - 1)]);
            try
            {
                using var resp = await http.GetAsync(url, ct);
                if (resp.StatusCode == HttpStatusCode.OK) return (await resp.Content.ReadAsStringAsync(ct), resp.StatusCode);
                bool transient = (int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.TooManyRequests;
                if (!transient || attempt >= backoff.Length) return (null, resp.StatusCode);
                if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                    pause = resp.Headers.RetryAfter?.Delta is { } ra && ra > TimeSpan.Zero && ra < TimeSpan.FromMinutes(10) ? ra : TimeSpan.FromSeconds(30 << attempt);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                if (attempt >= backoff.Length) return (null, null);
            }
            await Task.Delay(pause, ct);
        }
    }

    private RegShoThresholdHistory? regSho;
    private ShortVolumeHistory? shortVolume;
    private HashSet<string>? shortVolumeSymbols;

    /// <summary>
    /// Adds the Reg SHO block when its files exist: on the threshold list dated D-1 and the consecutive listed
    /// days ending D-1; the FINRA short-volume share on D-1, its D-1..D-5 mean, and D-1 against D-2..D-21.
    /// Every input is published by the evening of D-1, so it is known before any decision time on D.
    /// </summary>
    private ExploreTable AddRegSho(ExploreTable t)
    {
        if (t.Feature(RegShoFeatures.ListedD1) >= 0 || t.Feature(RegShoFeatures.ShortVolShareD1) >= 0) return t;
        regSho ??= RegShoThresholdHistory.Load(RegShoDir()) is { Days: > 0 } h ? h : null;
        var symbols = t.Symbols.Select(x => x.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (shortVolume is null || shortVolumeSymbols is null || !symbols.IsSubsetOf(shortVolumeSymbols))
        {
            shortVolume = ShortVolumeHistory.Load(ShortVolumeDir(), symbols) is { Count: > 0 } v ? v : null;
            shortVolumeSymbols = symbols;
        }
        var names = new List<string>();
        if (regSho is not null) names.AddRange([RegShoFeatures.ListedD1, RegShoFeatures.DaysListed]);
        if (shortVolume is not null) names.AddRange([RegShoFeatures.ShortVolShareD1, RegShoFeatures.ShortVolShareAvg5, RegShoFeatures.ShortVolShareZ]);
        if (names.Count == 0) return t;
        return ExploreDerived.AddColumns(t, names, r =>
        {
            var row = new List<double>(5);
            if (regSho is not null)
            {
                var (listed, run) = regSho.KnownBefore(t.Symbols[r], t.Days[r], MarketCalendar.PreviousTradingDay);
                row.Add(listed is null ? double.NaN : listed.Value ? 1 : 0);
                row.Add(run ?? double.NaN);
            }
            if (shortVolume is not null)
            {
                var (d1, avg, z) = shortVolume.KnownBefore(t.Symbols[r], t.Days[r], MarketCalendar.PreviousTradingDay);
                row.Add(d1 ?? double.NaN);
                row.Add(avg ?? double.NaN);
                row.Add(z ?? double.NaN);
            }
            return row;
        });
    }
}
