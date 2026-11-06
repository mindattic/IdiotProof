using System.Diagnostics;
using System.Text.Json;
using IdiotProof.Core.Catalysts;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

public sealed record CatalystStageResult(int Rows, int Reclassified, int LlmClassified, int LlmSkipped, double Minutes);

/// <summary>
/// <c>research gappers catalysts</c>: re-runs the deterministic <see cref="CatalystPreClassifier"/>
/// over every backfilled row (idempotent: a row whose class, direction, confidence or tier already
/// match is untouched, so the frozen rules are applied exactly once per item), then spends an
/// optional Haiku budget on rows never classified (the classifier skips any row with an LLM stamp,
/// so the cache is the row itself).
/// </summary>
public sealed class GapperCatalystStage(GapperStudyServices s, CatalystClassifier classifier)
{
    public async Task<CatalystStageResult> RunAsync(int llmBudget, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var rules = CatalystCatalogs.PreClassRules;
        var weights = CatalystCatalogs.ScoringWeights;
        int rows = 0, changed = 0;
        var unclassified = new List<Guid>();
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var pageSize = 2000;
            Guid? after = null;
            while (true)
            {
                var query = db.CatalystEvents.Where(e => e.Backfilled);
                if (after is { } a) query = query.Where(e => e.Id.CompareTo(a) > 0);
                var page = await query.OrderBy(e => e.Id).Take(pageSize).ToListAsync(ct);
                if (page.Count == 0) break;
                foreach (var row in page)
                {
                    rows++;
                    var items = row.ItemsJson is null ? null : JsonSerializer.Deserialize<List<string>>(row.ItemsJson);
                    var isEdgar = row.Source == CatalystIntake.Edgar;
                    var pre = CatalystPreClassifier.Classify(rules, row.Headline, row.RawSnippet, row.FormType, items, isEdgar ? "edgar" : null);
                    var tier = CatalystPreClassifier.SourceTier(rules, isEdgar ? "edgar" : null, isEdgar);
                    if (!isEdgar) tier = row.SourceTier;
                    var preClass = pre.Class.ToString();
                    var stamp = CatalystStampKind.Classify(row.Source, row.SourceName, row.Url);
                    if (row.StampKind != stamp && !(stamp == CatalystStampKind.Unknown && row.StampKind != CatalystStampKind.Unknown))
                    {
                        row.StampKind = stamp;
                        changed++;
                    }
                    if (row.PreClass != preClass || row.PreDirection != pre.Direction || Math.Abs(row.PreConfidence - pre.Confidence) > 1e-9 || row.SourceTier != tier)
                    {
                        row.PreClass = preClass;
                        row.PreDirection = pre.Direction;
                        row.PreConfidence = pre.Confidence;
                        row.SourceTier = tier;
                        var direction = row.LlmSentiment switch { "Bullish" => 1, "Bearish" => -1, _ => pre.Direction };
                        (row.CatalystScore, row.Direction) = CatalystScorer.Score(weights, pre.Class, direction, row.LlmMagnitude, tier, row.NoveltyCount, 0);
                        row.ScoreComputedUtc = s.UtcNow;
                        changed++;
                    }
                    if (row.LlmClassifiedUtc is null) unclassified.Add(row.Id);
                }
                await db.SaveChangesAsync(ct);
                after = page[^1].Id;
                s.Logger.LogInformation("[{Now}] catalysts: {Rows} rows checked, {Changed} re-classified", GapperStudyServices.Et(s.UtcNow), rows, changed);
            }
        }

        int llm = 0;
        foreach (var id in unclassified.Take(Math.Max(0, llmBudget)))
        {
            try { if (await classifier.ClassifyAsync(id, ct)) llm++; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { s.Logger.LogWarning(ex, "catalysts: LLM classification failed for {Id}", id); }
        }
        return new CatalystStageResult(rows, changed, llm, Math.Max(0, unclassified.Count - llm), Math.Round(sw.Elapsed.TotalMinutes, 2));
    }
}
