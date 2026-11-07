using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

public sealed record ReportStageRequest(bool Export);

public sealed record ReportStageResult(string ReportFile, int RunsUsed, int HoldoutRunsWithheld, int PostHocHypotheses, List<ExportedFile> Exports, double Minutes);

/// <summary>
/// <c>research gappers report</c>: gathers the sample, the status, the latest run file of every
/// hypothesis per split, the holdout lock state, post-unlock commits, the spread multiplier and the
/// post-registration summaries, exports the panel CSVs with a hashed manifest, and renders
/// <c>docs/research/gapper-study-2026-11.md</c>. Holdout numbers are withheld unless
/// <c>holdout-run.json</c> exists.
/// </summary>
public sealed class GapperReportStage(GapperStudyServices s, GapperStatusStage status, GapperStudyPanel panel)
{
    public string ReportFile => Path.Combine(Path.GetDirectoryName(s.Paths.StudyDir)!, Path.GetFileName(s.Paths.StudyDir) + ".md");

    public async Task<ReportStageResult> RunAsync(ReportStageRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var p = s.Params;
        var set = GapperHypothesisSet.Load(s.Paths);
        bool holdoutRunExists = File.Exists(s.Paths.HoldoutRunFile(p));
        var runs = LoadLatestRuns(s.Paths.RunsDir);
        int withheld = runs.Count(r => r.Split == "holdout" && !holdoutRunExists);
        if (!holdoutRunExists) runs = runs.Where(r => r.Split != "holdout").ToList();
        foreach (var split in runs.GroupBy(r => r.Split))
            HypothesisCorrections.Apply(split.ToList(), p, $"latest run of every hypothesis on {split.Key}");

        DateTime? runStart = null;
        var commits = new List<(string, string, string)>();
        var postHoc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (holdoutRunExists)
        {
            var doc = JsonNode.Parse(File.ReadAllText(s.Paths.HoldoutRunFile(p))) as JsonObject;
            if (DateTime.TryParse(doc?["runStartUtc"]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var start))
            {
                runStart = start;
                commits = HoldoutLock.GitCommitsSince(s.Paths, start, p.HoldoutLock.PostHocFlagFiles);
                var head = doc?["head"]?.GetValue<string>();
                if (commits.Count > 0 && head is not null)
                    foreach (var id in ChangedHypotheses(HoldoutLock.GitShow(s.Paths, head, "hypotheses.json"), File.ReadAllText(s.Paths.HypothesesFile), HoldoutLock.GitShow(s.Paths, head, "params.json"), File.ReadAllText(s.Paths.ParamsFile), set))
                        postHoc.Add(id);
            }
        }

        var sampleFile = s.Paths.SampleFile(false);
        var sample = File.Exists(sampleFile) ? JsonNode.Parse(File.ReadAllText(sampleFile)) as JsonObject : null;
        var st = await status.RunAsync(ct);
        var sipShare = await panel.SipShareAsync(["train", "validation"], ct);
        var (multiplier, source) = GapperSpreadMultiplier.Resolve(s.Paths, p);
        var registry = GapperSnapshotStage.Registry(p);
        var summaries = await PostRegistrationSummariesAsync(registry.PostRegistrationNames, holdoutRunExists, ct);

        var exports = new List<ExportedFile>();
        string? exportNote = null;
        if (req.Export)
        {
            exports = await ExportAsync(holdoutRunExists, ct);
            exportNote = holdoutRunExists ? null : "Holdout rows are excluded from every export until holdout-run.json exists.";
        }

        var model = new GapperReportModel
        {
            Params = p, Hypotheses = set, GeneratedUtc = s.UtcNow, Head = HoldoutLock.GitHead(s.Paths), FrozenCommit = HoldoutLock.GitFrozenCommit(s.Paths),
            StudyDirName = Path.GetFileName(s.Paths.StudyDir), Sample = sample, Status = st, Runs = runs, HoldoutRunExists = holdoutRunExists, HoldoutRunsWithheld = withheld,
            HoldoutRunStartUtc = runStart, PostUnlockCommits = commits, PostHocHypotheses = postHoc, SpreadMultiplier = multiplier, SpreadMultiplierSource = source,
            PostRegistrationFeatures = registry.PostRegistrationNames, PostRegistrationSummaries = summaries, Exports = exports, ExportNote = exportNote,
            LiveSipSharePct = sipShare,
        };
        File.WriteAllText(ReportFile, GapperReportBuilder.Build(model));
        return new ReportStageResult(ReportFile, runs.Count, withheld, postHoc.Count, exports, Math.Round(sw.Elapsed.TotalMinutes, 2));
    }

    /// <summary>The newest run file per (hypothesis, split), smoke runs excluded.</summary>
    public static List<HypothesisRun> LoadLatestRuns(string runsDir)
    {
        if (!Directory.Exists(runsDir)) return [];
        var runs = new List<HypothesisRun>();
        foreach (var file in Directory.GetFiles(runsDir, "test-*.json"))
        {
            var run = HypothesisRun.FromJson(File.ReadAllText(file));
            if (run is null || string.IsNullOrEmpty(run.Id) || run.Smoke || run.Split == GapperStudyServices.SmokeSplit) continue;
            run.File ??= file;
            runs.Add(run);
        }
        return runs.GroupBy(r => (r.Id.ToUpperInvariant(), r.Split)).Select(g => g.OrderByDescending(r => r.ComputedUtc).ThenByDescending(r => r.File, StringComparer.Ordinal).First()).ToList();
    }

    /// <summary>Hypotheses whose JSON definition differs between the unlock-time files and the current ones; every hypothesis when params changed.</summary>
    public static IEnumerable<string> ChangedHypotheses(string? hypothesesThen, string hypothesesNow, string? paramsThen, string paramsNow, GapperHypothesisSet set)
    {
        if (paramsThen is not null && paramsThen != paramsNow) return set.Hypotheses.Select(h => h.Id);
        if (hypothesesThen is null) return [];
        try
        {
            var then = GapperHypothesisSet.Parse(hypothesesThen).Hypotheses.ToDictionary(h => h.Id, h => JsonSerializer.Serialize(h), StringComparer.OrdinalIgnoreCase);
            var now = GapperHypothesisSet.Parse(hypothesesNow).Hypotheses.ToDictionary(h => h.Id, h => JsonSerializer.Serialize(h), StringComparer.OrdinalIgnoreCase);
            return now.Where(kv => !then.TryGetValue(kv.Key, out var old) || old != kv.Value).Select(kv => kv.Key).ToList();
        }
        catch (JsonException) { return set.Hypotheses.Select(h => h.Id); }
    }

    private async Task<List<PostRegistrationSummary>> PostRegistrationSummariesAsync(IReadOnlyCollection<string> features, bool holdoutRunExists, CancellationToken ct)
    {
        var result = new List<PostRegistrationSummary>();
        var lastDecision = s.Params.DecisionTimes().Where(d => d.DayOffset == 0).OrderBy(d => d.TimeEt).Last().Label;
        foreach (var split in new[] { "train", "validation", "holdout" })
        {
            if (split == "holdout" && !holdoutRunExists) continue;
            var rows = (await panel.LoadAsync(split, lastDecision, ct)).Where(r => r.IsGapper).ToList();
            if (rows.Count == 0) continue;
            foreach (var feature in features)
            {
                var values = rows.Select(r => r.Extras.Number(feature)).Where(v => v is not null).Select(v => v!.Value).OrderBy(v => v).ToList();
                double? median = values.Count == 0 ? null : values.Count % 2 == 1 ? values[values.Count / 2] : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2;
                result.Add(new PostRegistrationSummary(split, feature, rows.Count, values.Count, median, GapperSnapshotStage.PostRegistrationDeathWindowMinutes));
            }
        }
        return result;
    }

    private async Task<List<ExportedFile>> ExportAsync(bool holdoutRunExists, CancellationToken ct)
    {
        var dir = s.Paths.DataDir;
        Directory.CreateDirectory(dir);
        var files = new List<ExportedFile>();
        bool Allowed(string split) => split != GapperStudyServices.SmokeSplit && (split != "holdout" || holdoutRunExists);

        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var events = db.GapperStudyEvents.AsNoTracking().OrderBy(e => e.DateEt).ThenBy(e => e.Symbol).AsAsyncEnumerable();
            files.Add(await GapperStudyExport.WriteCsvAsync(Path.Combine(dir, "events.csv"),
                ["symbol", "dateEt", "split", "adjPrevClose", "gapMax", "tier20", "tier50", "tier100", "tier20Touched", "tier20TouchTimeEt", "subType", "pmHigh", "pmHighTimeEt", "rthHigh", "hodTimeEt", "pmDollarVolume", "minuteBars", "sipShare"],
                Filter(events, e => Allowed(e.Split), e => [e.Symbol, e.DateEt, e.Split, e.AdjPrevClose, e.GapMax, e.Tier20, e.Tier50, e.Tier100, e.Tier20Touched, e.Tier20TouchTimeEt, e.SubType, e.PmHigh, e.PmHighTimeEt, e.RthHigh, e.HodTimeEt, e.PmDollarVolume, e.MinuteBars, e.SipShare]), ct));
        }
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var snapshots = db.GapperFormationSnapshots.AsNoTracking().Where(r => r.Source == GapperSnapshotStage.Source).OrderBy(r => r.DateEt).ThenBy(r => r.Symbol).ThenBy(r => r.DecisionTimeEt).AsAsyncEnumerable();
            files.Add(await GapperStudyExport.WriteCsvAsync(Path.Combine(dir, "snapshots.csv"),
                ["symbol", "dateEt", "split", "decisionLabel", "decisionUtc", "isCandidate", "isControl", "gapPct", "pmCumVol", "volRatioTod", "ownProfileRatio", "velocity1m", "velocity3m", "velocity5m", "velocity15m", "acceleration", "volumeRoc", "tradesProxy", "spreadProxy", "distToPmHighPct", "minsSinceFirstAbnormal", "sessionVwap",
                 "abnormalReturnD1", "abnormalReturnD2", "abnormalReturnD3", "abnormalReturnD4", "abnormalReturnD5", "volumeRatioD1", "volumeRatioD2", "volumeRatioD3", "volumeRatioD4", "volumeRatioD5", "daysSinceReverseSplit", "daysSince424B", "daysSinceS1",
                 "catalystScore", "catalystAgeMinutes", "catalystDirection", "catalystPreClass", "catalystStampIsUpperBound", "halted", "extraJson"],
                Filter(snapshots, r => Allowed(r.Split), r => [r.Symbol, r.DateEt, r.Split, r.DecisionLabel, r.DecisionUtc, r.IsCandidate, r.IsControl, r.GapPct, r.PmCumVol, r.VolRatioTod, r.OwnProfileRatio, r.Velocity1m, r.Velocity3m, r.Velocity5m, r.Velocity15m, r.Acceleration, r.VolumeRoc, r.TradesProxy, r.SpreadProxy, r.DistToPmHighPct, r.MinsSinceFirstAbnormal, r.SessionVwap,
                    r.AbnormalReturnD1, r.AbnormalReturnD2, r.AbnormalReturnD3, r.AbnormalReturnD4, r.AbnormalReturnD5, r.VolumeRatioD1, r.VolumeRatioD2, r.VolumeRatioD3, r.VolumeRatioD4, r.VolumeRatioD5, r.DaysSinceReverseSplit, r.DaysSince424B, r.DaysSinceS1,
                    r.CatalystScore, r.CatalystAgeMinutes, r.CatalystDirection, r.CatalystPreClass, r.CatalystStampIsUpperBound, r.Halted, r.ExtraJson]), ct));
        }
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var labels = db.GapperStudyLabels.AsNoTracking().OrderBy(r => r.DateEt).ThenBy(r => r.Symbol).ThenBy(r => r.DecisionTimeEt).AsAsyncEnumerable();
            files.Add(await GapperStudyExport.WriteCsvAsync(Path.Combine(dir, "labels.csv"),
                ["symbol", "dateEt", "split", "decisionLabel", "decisionUtc", "entryRef", "entryUtc", "fwdMax0930", "fwdMax0945", "fwdMax1030", "fwdMax1600", "fwdClose0930", "fwdClose0945", "fwdClose1030", "fwdClose1600", "reach20By0945", "pmHigh", "hodTimePmEt", "hodTimeRthEt", "hodTimeEt", "hodBucket", "fade", "fadeFlag30", "closeBelowOpen", "extensionFromOpen0945", "velocityDeathTimeEt", "deathToHodMin", "haltFlag", "vwapLossTimeEt"],
                Filter(labels, r => Allowed(r.Split), r => [r.Symbol, r.DateEt, r.Split, r.DecisionLabel, r.DecisionUtc, r.EntryRef, r.EntryUtc, r.FwdMax0930, r.FwdMax0945, r.FwdMax1030, r.FwdMax1600, r.FwdClose0930, r.FwdClose0945, r.FwdClose1030, r.FwdClose1600, r.Reach20By0945, r.PmHigh, r.HodTimePmEt, r.HodTimeRthEt, r.HodTimeEt, r.HodBucket, r.Fade, r.FadeFlag30, r.CloseBelowOpen, r.ExtensionFromOpen0945, r.VelocityDeathTimeEt, r.DeathToHodMin, r.HaltFlag, r.VwapLossTimeEt]), ct));
        }
        GapperStudyExport.WriteManifest(dir, files, new Dictionary<string, object?>
        {
            ["writtenUtc"] = s.UtcNow.ToString("O"),
            ["head"] = HoldoutLock.GitHead(s.Paths),
            ["frozenCommit"] = HoldoutLock.GitFrozenCommit(s.Paths),
            ["hypothesesSha256"] = s.Params.HypothesesSha256,
            ["paramsSha256"] = s.Params.ParamsSha256,
            ["holdoutIncluded"] = holdoutRunExists,
            ["hash"] = "SHA-256 over the data rows (header excluded), one row per line with LF",
        });
        return files;
    }

    private static async IAsyncEnumerable<IReadOnlyList<object?>> Filter<T>(IAsyncEnumerable<T> rows, Func<T, bool> keep, Func<T, object?[]> project)
    {
        await foreach (var r in rows)
            if (keep(r)) yield return project(r);
    }
}
