using System.Diagnostics;
using System.Globalization;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// IP-T458, exploration only (avenues.md A2): the widened EDGAR pull. Neither tier is part of
/// <c>pull all</c>, neither reads or writes the frozen <c>EdgarFilingIndex</c> table, and params.json's
/// <c>edgarForms</c> is untouched: rows land in files under <see cref="DilutionDir"/> that only the
/// explore stage reads. Every SEC request goes through the shared <see cref="EdgarRateGate"/>.
/// <list type="bullet">
/// <item><c>edgar-dilution</c>: the quarterly form.idx filtered to <see cref="DilutionForms.Pulled"/> for
/// mapped CIKs (filings.csv, quarters.txt), then the acceptance-time headers for filings inside the
/// frozen acceptance window of candidate or population-sample days (headers.csv, ET).</item>
/// <item><c>edgar-dilution-text</c>: the submission text of each 424B5 (365 days before a target day) and
/// S-3/F-3 shelf (three years before) run through <see cref="AtmShelfDetector"/> (classified.csv).</item>
/// </list>
/// </summary>
public sealed partial class GapperPullStage
{
    public const string EdgarDilutionTier = "edgar-dilution";
    public const string EdgarDilutionTextTier = "edgar-dilution-text";

    /// <summary>424B5 text is read this far back from a target day (the ATM-active window).</summary>
    internal const int Dilution424B5LookbackDays = 365;

    /// <summary>Shelf (S-3/F-3) text is read this far back: a shelf registration lasts three years.</summary>
    internal const int DilutionShelfLookbackDays = 3 * 365;

    /// <summary>Characters read from a submission's text; the cover and plan of distribution sit well inside it.</summary>
    internal const int DilutionMaxTextChars = 600_000;

    /// <summary>The widened filings folder; GAPPER_EDGAR_DILUTION_DIR overrides, default D:\IdiotProofData\edgar-dilution beside the study folder.</summary>
    public static string DilutionDir(GapperStudyParams p) => Environment.GetEnvironmentVariable("GAPPER_EDGAR_DILUTION_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(p.DataTiers.MinuteBarRoot().TrimEnd('\\', '/'))!)!, "edgar-dilution");

    private async Task<TierSummary> PullDilutionIndexAsync(PullStageRequest req, CallBudget calls, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var p = s.Params;
        var dir = DilutionDir(p);
        Directory.CreateDirectory(dir);
        var forms = DilutionForms.Pulled.ToHashSet(StringComparer.Ordinal);
        var candidates = await CandidateDaysAsync(req, ct);
        var span = p.DataTiers.DailyBars;
        var acceptance = p.DataTiers.EdgarAcceptanceWindowDays;
        DateOnly from = req.Smoke && candidates.Count > 0 ? candidates.Min(c => c.DateEt).AddDays(-DilutionShelfLookbackDays) : span.From.AddDays(-DilutionShelfLookbackDays);
        DateOnly to = req.Smoke && candidates.Count > 0 ? candidates.Max(c => c.DateEt).AddDays(acceptance.After) : span.To;

        HashSet<string> mapped;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
            mapped = (await db.GapperStudyCikMap.AsNoTracking().Where(r => r.Cik != null).Select(r => r.Cik!).ToListAsync(ct))
                .Select(EdgarTickerMap.Normalize).ToHashSet(StringComparer.Ordinal);
        if (mapped.Count == 0) return new TierSummary(EdgarDilutionTier, 0, 0, 0, null, "no CIK mappings yet: run `pull --tier cik` first");

        var quartersFile = Path.Combine(dir, "quarters.txt");
        var done = File.Exists(quartersFile) ? File.ReadAllLines(quartersFile).ToHashSet(StringComparer.Ordinal) : [];
        var filingsFile = Path.Combine(dir, DilutionHistory.FilingsFile);
        if (!File.Exists(filingsFile)) File.WriteAllLines(filingsFile, [DilutionHistory.FilingsHeader]);
        var present = DilutionHistory.ReadFilings(dir).Select(f => (f.Cik, f.AccessionNumber)).ToHashSet();
        var todayEt = DateOnly.FromDateTime(EasternTime.FromUtc(s.UtcNow));
        int quarterCalls = 0, indexRows = 0;
        foreach (var (year, quarter) in EdgarFormIndex.Quarters(from, to))
        {
            var key = $"{year}/QTR{quarter}";
            if (done.Contains(key)) continue;
            if (!calls.Allow()) { s.Logger.LogWarning("{Tier}: --max-calls reached", EdgarDilutionTier); break; }
            quarterCalls++;
            var rows = await edgarGate.RunAsync(async c =>
            {
                var http = httpFactory.CreateClient(p.DataTiers.EdgarHttpClientName);
                http.Timeout = TimeSpan.FromMinutes(10);
                using var resp = await http.GetAsync(EdgarFormIndex.Url(year, quarter), HttpCompletionOption.ResponseHeadersRead, c);
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync(c);
                using var reader = new StreamReader(stream);
                return EdgarFormIndex.Parse(reader, forms).Where(e => mapped.Contains(EdgarTickerMap.Normalize(e.Cik))).ToList();
            }, ct);
            var lines = new List<string>();
            foreach (var e in rows)
            {
                var cik = int.Parse(EdgarTickerMap.Normalize(e.Cik), CultureInfo.InvariantCulture);
                if (!present.Add((cik, e.AccessionNumber))) continue;
                lines.Add(DilutionHistory.FilingLine(new DilutionFiling(cik, e.FormType, e.AccessionNumber, e.FiledDate, null)));
            }
            File.AppendAllLines(filingsFile, lines);
            indexRows += lines.Count;
            // A quarter still in progress is read again next run.
            var quarterEnd = new DateOnly(year, quarter * 3, 1).AddMonths(1).AddDays(-1);
            if (quarterEnd < todayEt) File.AppendAllLines(quartersFile, [key]);
            s.Logger.LogInformation("[{Now}] {Tier}: {Quarter} -> {Rows} widened-form rows for mapped CIKs ({Total} new so far)", GapperStudyServices.Et(s.UtcNow), EdgarDilutionTier, key, lines.Count, indexRows);
        }

        // Acceptance times for filings inside the frozen acceptance window of a target day.
        var targets = await TargetDaysAsync(req, ct);
        var cikBySymbol = await CikBySymbolAsync(ct);
        var windows = WindowsByCik(targets, cikBySymbol, acceptance.Before, acceptance.After);
        var answered = DilutionHistory.ReadHeaders(dir);
        var pending = DilutionHistory.ReadFilings(dir)
            .Where(f => !answered.ContainsKey(f.AccessionNumber) && windows.TryGetValue(f.Cik, out var w) && w.Any(x => f.Filed >= x.From && f.Filed <= x.To))
            .OrderBy(f => f.Filed).ToList();
        s.Logger.LogInformation("[{Now}] {Tier}: {Count} widened filings inside [D-{B}, D+{A}] of target days need acceptance times", GapperStudyServices.Et(s.UtcNow), EdgarDilutionTier, pending.Count, acceptance.Before, acceptance.After);
        var headersFile = Path.Combine(dir, DilutionHistory.HeadersFile);
        if (!File.Exists(headersFile)) File.WriteAllLines(headersFile, [DilutionHistory.HeadersHeader]);
        int headerCalls = 0, filled = 0;
        foreach (var chunk in pending.Chunk(HeaderChunk))
        {
            var allowed = new List<DilutionFiling>(chunk.Length);
            var budgetHit = false;
            foreach (var f in chunk)
            {
                if (!calls.Allow()) { budgetHit = true; break; }
                allowed.Add(f);
            }
            var results = new System.Collections.Concurrent.ConcurrentDictionary<string, EdgarFilingHeader?>(StringComparer.Ordinal);
            await Parallel.ForEachAsync(allowed, new ParallelOptions { MaxDegreeOfParallelism = HeaderConcurrency, CancellationToken = ct },
                async (f, token) => results[f.AccessionNumber] = await headers.FetchAsync(f.Cik.ToString(CultureInfo.InvariantCulture), f.AccessionNumber, token));
            // Only an answered fetch is written; a refused one stays pending for the next run.
            var lines = results.Where(kv => kv.Value is not null)
                .Select(kv => DilutionHistory.HeaderLine(kv.Key, kv.Value!.AcceptanceUtc is { } u ? EasternTime.FromUtc(u) : null)).ToList();
            File.AppendAllLines(headersFile, lines);
            headerCalls += results.Count;
            filled += results.Count(kv => kv.Value?.AcceptanceUtc is not null);
            var failed = results.Count(kv => kv.Value is null);
            s.Logger.LogInformation("[{Now}] {Tier} headers: {Calls} of {Total} fetched, {Filled} with acceptance time, {Failed} failed this chunk", GapperStudyServices.Et(s.UtcNow), EdgarDilutionTier, headerCalls, pending.Count, filled, failed);
            if (budgetHit) { s.Logger.LogWarning("{Tier} headers: --max-calls reached", EdgarDilutionTier); break; }
            if (results.Count > 0 && failed * 2 > results.Count)
            {
                s.Logger.LogWarning("[{Now}] {Tier} headers: {Failed} of {Count} failed, backing off 2 minutes", GapperStudyServices.Et(s.UtcNow), EdgarDilutionTier, failed, results.Count);
                await Task.Delay(TimeSpan.FromMinutes(2), ct);
            }
        }
        return new TierSummary(EdgarDilutionTier, indexRows, quarterCalls + headerCalls, Math.Round(sw.Elapsed.TotalMinutes, 2), null,
            $"{quarterCalls} quarter files, {indexRows} new widened-form rows, {headerCalls} header fetches, {filled} acceptance times -> {dir}");
    }

    private async Task<TierSummary> PullDilutionTextAsync(PullStageRequest req, CallBudget calls, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var p = s.Params;
        var dir = DilutionDir(p);
        var filings = DilutionHistory.ReadFilings(dir);
        if (filings.Count == 0) return new TierSummary(EdgarDilutionTextTier, 0, 0, 0, null, $"no widened filings in {dir}: run `pull --tier {EdgarDilutionTier}` first");
        var classified = DilutionHistory.ReadClassified(dir);
        var targets = await TargetDaysAsync(req, ct);
        var cikBySymbol = await CikBySymbolAsync(ct);
        var w424 = WindowsByCik(targets, cikBySymbol, Dilution424B5LookbackDays, 0);
        var wShelf = WindowsByCik(targets, cikBySymbol, DilutionShelfLookbackDays, 0);
        bool InWindow(DilutionFiling f) => (f.FormType == "424B5" ? w424 : DilutionForms.IsShelf(f.FormType) ? wShelf : null) is { } w
            && w.TryGetValue(f.Cik, out var list) && list.Any(x => f.Filed >= x.From && f.Filed <= x.To);
        var pending = filings.Where(f => DilutionForms.TextForms.Contains(f.FormType) && !classified.ContainsKey(f.AccessionNumber) && InWindow(f))
            .DistinctBy(f => f.AccessionNumber).OrderBy(f => f.Filed).ToList();
        s.Logger.LogInformation("[{Now}] {Tier}: {Count} 424B5/S-3/F-3 documents to classify", GapperStudyServices.Et(s.UtcNow), EdgarDilutionTextTier, pending.Count);

        var file = Path.Combine(dir, DilutionHistory.ClassifiedFile);
        if (!File.Exists(file)) File.WriteAllLines(file, [DilutionHistory.ClassifiedHeader]);
        int fetched = 0, atm = 0, baby = 0, failedTotal = 0;
        foreach (var chunk in pending.Chunk(HeaderChunk))
        {
            var allowed = new List<DilutionFiling>(chunk.Length);
            var budgetHit = false;
            foreach (var f in chunk)
            {
                if (!calls.Allow()) { budgetHit = true; break; }
                allowed.Add(f);
            }
            var results = new System.Collections.Concurrent.ConcurrentDictionary<string, AtmShelfFacts?>(StringComparer.Ordinal);
            await Parallel.ForEachAsync(allowed, new ParallelOptions { MaxDegreeOfParallelism = HeaderConcurrency, CancellationToken = ct }, async (f, token) =>
            {
                var text = await FetchSubmissionTextAsync(f.Cik.ToString(CultureInfo.InvariantCulture), f.AccessionNumber, token);
                results[f.AccessionNumber] = text is null ? null : AtmShelfDetector.Detect(text);
            });
            var lines = results.Where(kv => kv.Value is not null).Select(kv => DilutionHistory.ClassifiedLine(kv.Key, kv.Value!)).ToList();
            File.AppendAllLines(file, lines);
            fetched += lines.Count;
            atm += results.Count(kv => kv.Value?.IsAtm == true);
            baby += results.Count(kv => kv.Value?.BabyShelf == true);
            var failed = results.Count(kv => kv.Value is null);
            failedTotal += failed;
            s.Logger.LogInformation("[{Now}] {Tier}: {Done} of {Total} classified, {Atm} ATM, {Baby} baby shelf, {Failed} failed this chunk", GapperStudyServices.Et(s.UtcNow), EdgarDilutionTextTier, fetched, pending.Count, atm, baby, failed);
            if (budgetHit) { s.Logger.LogWarning("{Tier}: --max-calls reached", EdgarDilutionTextTier); break; }
            if (results.Count > 0 && failed * 2 > results.Count)
            {
                s.Logger.LogWarning("[{Now}] {Tier}: {Failed} of {Count} failed, backing off 2 minutes", GapperStudyServices.Et(s.UtcNow), EdgarDilutionTextTier, failed, results.Count);
                await Task.Delay(TimeSpan.FromMinutes(2), ct);
            }
        }
        return new TierSummary(EdgarDilutionTextTier, fetched, fetched + failedTotal, Math.Round(sw.Elapsed.TotalMinutes, 2), pending.Count == 0 ? null : Math.Round(100.0 * fetched / pending.Count, 2),
            $"{fetched} documents classified ({atm} ATM, {baby} citing I.B.6), {failedTotal} failed -> {file}");
    }

    /// <summary>The first <see cref="DilutionMaxTextChars"/> characters of a filing's complete submission text, through the EDGAR gate; null when refused or failed.</summary>
    private async Task<string?> FetchSubmissionTextAsync(string cik, string accession, CancellationToken ct)
    {
        var url = Form4Submission.Url(cik, accession);
        try
        {
            return await edgarGate.RunAsync(async c =>
            {
                var http = httpFactory.CreateClient(s.Params.DataTiers.EdgarHttpClientName);
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, c);
                if (!resp.IsSuccessStatusCode) return null;
                using var stream = await resp.Content.ReadAsStreamAsync(c);
                using var reader = new StreamReader(stream);
                var buffer = new char[DilutionMaxTextChars];
                int read = 0, n;
                while (read < buffer.Length && (n = await reader.ReadAsync(buffer.AsMemory(read), c)) > 0) read += n;
                return new string(buffer, 0, read);
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            s.Logger.LogDebug(ex, "{Tier}: {Url} not fetched", EdgarDilutionTextTier, url);
            return null;
        }
    }

    private async Task<Dictionary<string, int>> CikBySymbolAsync(CancellationToken ct)
    {
        await using var db = await s.Db.CreateDbContextAsync(ct);
        return (await db.GapperStudyCikMap.AsNoTracking().Where(r => r.Cik != null).Select(r => new { r.Symbol, r.Cik }).ToListAsync(ct))
            .Where(r => int.TryParse(EdgarTickerMap.Normalize(r.Cik!), CultureInfo.InvariantCulture, out _))
            .ToDictionary(r => r.Symbol, r => int.Parse(EdgarTickerMap.Normalize(r.Cik!), CultureInfo.InvariantCulture), StringComparer.Ordinal);
    }

    private static Dictionary<int, List<(DateOnly From, DateOnly To)>> WindowsByCik(List<(string Symbol, DateOnly DateEt)> targets, Dictionary<string, int> cikBySymbol, int before, int after) =>
        targets.Where(t => cikBySymbol.ContainsKey(t.Symbol)).GroupBy(t => cikBySymbol[t.Symbol])
            .ToDictionary(g => g.Key, g => g.Select(t => (t.DateEt.AddDays(-before), t.DateEt.AddDays(after))).ToList());
}
