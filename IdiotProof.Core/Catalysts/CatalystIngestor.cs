using System.Text.Json;
using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

public enum CatalystIngestOutcome
{
    Inserted,
    Duplicate,
    NoSymbol,
}

public sealed record CatalystIngestResult(CatalystIngestOutcome Outcome, Guid? Id, CatalystPreClassification PreClass, double Score);

/// <summary>
/// Turns one <see cref="CatalystIntake"/> into a <see cref="CatalystEvent"/> row (IP-T443): dedupes on
/// (Source, ExternalId), pre-classifies with the rule tables, counts the symbol's recent events for
/// novelty, scores, writes the row plus one <see cref="CatalystEventSymbol"/> per symbol, and hands the
/// id to the LLM queue when <c>catalyst.intake.llmClassify</c> is on. Live and backfill both come through here.
/// </summary>
public sealed class CatalystIngestor(
    IDbContextFactory<AppDbContext> dbFactory,
    CatalystIntakeSettings intakeSettings,
    CatalystClassifier classifier,
    ILogger<CatalystIngestor> logger)
{
    public CatalystPreClassRules Rules { get; init; } = CatalystCatalogs.PreClassRules;

    public CatalystScoringWeights Weights { get; init; } = CatalystCatalogs.ScoringWeights;

    public async Task<CatalystIngestResult> IngestAsync(CatalystIntake item, CancellationToken ct = default)
    {
        var symbols = item.Symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length is > 0 and <= 16).Distinct().ToList();
        var pre = CatalystPreClassifier.Classify(Rules, item.Headline, item.Summary, item.FormType, item.Items, item.SourceName);
        if (symbols.Count == 0) return new(CatalystIngestOutcome.NoSymbol, null, pre, 0);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.CatalystEvents.AnyAsync(e => e.Source == item.Source && e.ExternalId == item.ExternalId, ct))
            return new(CatalystIngestOutcome.Duplicate, null, pre, 0);

        var primary = symbols[0];
        var isEdgar = item.Source == CatalystIntake.Edgar;
        var tier = CatalystPreClassifier.SourceTier(Rules, item.SourceName, isEdgar);
        var windowStart = item.PublishedUtc.AddDays(-Weights.NoveltyWindowDays);
        var novelty = await db.CatalystEvents.CountAsync(
            e => e.PrimarySymbol == primary && e.PublishedUtc >= windowStart && e.PublishedUtc < item.PublishedUtc, ct);
        var age = Math.Max(0, (item.ReceivedUtc - item.PublishedUtc).TotalMinutes);
        var (score, direction) = CatalystScorer.Score(Weights, pre.Class, pre.Direction, null, tier, novelty, age);

        var row = new CatalystEvent
        {
            Source = item.Source,
            ExternalId = Clip(item.ExternalId, 64),
            PublishedUtc = item.PublishedUtc,
            ReceivedUtc = item.ReceivedUtc,
            PrimarySymbol = primary,
            SymbolsJson = Clip(JsonSerializer.Serialize(symbols), 2000),
            Headline = Clip(item.Headline, 500),
            Url = item.Url is null ? null : Clip(item.Url, 1000),
            FormType = item.FormType is null ? null : Clip(item.FormType, 16),
            ItemsJson = item.Items is null ? null : Clip(JsonSerializer.Serialize(item.Items), 200),
            SourceTier = tier,
            PreClass = pre.Class.ToString(),
            PreDirection = pre.Direction,
            PreConfidence = pre.Confidence,
            NoveltyCount = novelty,
            CatalystScore = score,
            Direction = direction,
            ScoreComputedUtc = item.ReceivedUtc,
            RawSnippet = item.Summary is { Length: > 0 } s ? Clip(s, 8000) : null,
            Backfilled = item.Backfilled,
        };
        db.CatalystEvents.Add(row);
        foreach (var symbol in symbols) db.CatalystEventSymbols.Add(new CatalystEventSymbol { EventId = row.Id, Symbol = symbol });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogDebug(ex, "Catalyst {Source}/{Id} already stored (concurrent insert).", item.Source, item.ExternalId);
            return new(CatalystIngestOutcome.Duplicate, null, pre, 0);
        }

        if ((await intakeSettings.ReadAsync(ct)).LlmClassify) classifier.Enqueue(row.Id);
        return new(CatalystIngestOutcome.Inserted, row.Id, pre, score);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
