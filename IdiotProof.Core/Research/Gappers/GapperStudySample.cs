using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// Writes <c>sample.json</c> (protocol §Reproducibility): per split the universe days, candidates,
/// gappers per tier and sub-type, controls, exclusions by reason, symbol counts and date range, with
/// the frozen-files commit hash. Re-written after each stage that changes the sample (universe
/// build, gapper classification, CIK mapping) and never after a snapshot exists for that split.
/// </summary>
public static class GapperStudySample
{
    public static async Task<JsonObject> WriteAsync(GapperStudyServices s, bool smoke, IDictionary<string, object?>? extra, CancellationToken ct)
    {
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var days = db.GapperStudyUniverseDays.AsNoTracking();
        if (smoke) days = days.Where(d => d.Split == GapperStudyServices.SmokeSplit);
        else days = days.Where(d => d.Split != GapperStudyServices.SmokeSplit);

        var perSplit = await days
            .GroupBy(d => d.Split)
            .Select(g => new
            {
                Split = g.Key,
                UniverseDays = g.Count(d => d.ExcludedReason == null),
                Excluded = g.Count(d => d.ExcludedReason != null),
                Candidates = g.Count(d => d.IsCandidate),
                Tier20 = g.Count(d => d.IsGapper),
                Tier50 = g.Count(d => d.GapTier == "Tier50" || d.GapTier == "Tier100"),
                Tier100 = g.Count(d => d.GapTier == "Tier100"),
                Premarket = g.Count(d => d.IsGapper && d.GapperSubType == GapperEventClassifier.PremarketSubType),
                Open = g.Count(d => d.IsGapper && d.GapperSubType == GapperEventClassifier.OpenSubType),
                Controls = g.Count(d => d.ControlForSymbol != null),
                Symbols = g.Select(d => d.Symbol).Distinct().Count(),
                CandidateSymbols = g.Where(d => d.IsCandidate).Select(d => d.Symbol).Distinct().Count(),
                First = g.Min(d => d.DateEt),
                Last = g.Max(d => d.DateEt),
            })
            .ToListAsync(ct);
        var excludedByReason = await days.Where(d => d.ExcludedReason != null)
            .GroupBy(d => new { d.Split, d.ExcludedReason })
            .Select(g => new { g.Key.Split, Reason = g.Key.ExcludedReason!, Count = g.Count() })
            .ToListAsync(ct);
        var touchedNoVolume = await db.GapperStudyEvents.AsNoTracking()
            .Where(e => smoke ? e.Split == GapperStudyServices.SmokeSplit : e.Split != GapperStudyServices.SmokeSplit)
            .GroupBy(e => e.Split)
            .Select(g => new { Split = g.Key, Tier20TouchedNotQualified = g.Count(e => e.Tier20Touched && !e.Tier20) })
            .ToListAsync(ct);

        var splits = new JsonObject();
        foreach (var r in perSplit.OrderBy(r => r.Split, StringComparer.Ordinal))
        {
            var reasons = new JsonObject();
            foreach (var e in excludedByReason.Where(e => e.Split == r.Split)) reasons[e.Reason] = e.Count;
            splits[r.Split] = new JsonObject
            {
                ["universeDays"] = r.UniverseDays,
                ["candidates"] = r.Candidates,
                ["gappers"] = new JsonObject
                {
                    ["tier20"] = r.Tier20, ["tier50"] = r.Tier50, ["tier100"] = r.Tier100,
                    ["premarket"] = r.Premarket, ["open"] = r.Open,
                    ["tier20TouchedBelowFloor"] = touchedNoVolume.FirstOrDefault(t => t.Split == r.Split)?.Tier20TouchedNotQualified ?? 0,
                },
                ["controls"] = r.Controls,
                ["excluded"] = r.Excluded,
                ["excludedByReason"] = reasons,
                ["symbols"] = r.Symbols,
                ["candidateSymbols"] = r.CandidateSymbols,
                ["firstDate"] = r.First.ToString("yyyy-MM-dd"),
                ["lastDate"] = r.Last.ToString("yyyy-MM-dd"),
            };
        }

        var doc = new JsonObject
        {
            ["writtenUtc"] = s.UtcNow.ToString("O"),
            ["smoke"] = smoke,
            ["frozenCommit"] = HoldoutLock.GitFrozenCommit(s.Paths),
            ["head"] = HoldoutLock.GitHead(s.Paths),
            ["hypothesesSha256"] = s.Params.HypothesesSha256,
            ["paramsSha256"] = s.Params.ParamsSha256,
            ["splits"] = splits,
        };
        if (extra is not null)
            foreach (var (k, v) in extra) doc[k] = v is null ? null : JsonSerializer.SerializeToNode(v);
        var prior = s.Paths.SampleFile(smoke);
        if (File.Exists(prior) && JsonNode.Parse(File.ReadAllText(prior)) is JsonObject old)
            foreach (var key in new[] { "universeBuild", "cikCoverage", "pull" })
                if (!doc.ContainsKey(key) && old[key] is { } kept) doc[key] = kept.DeepClone();
        File.WriteAllText(prior, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return doc;
    }
}
