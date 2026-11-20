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

    /// <summary>A wire item another source already carried (IP-T602): stored as a sighting on that story, no new event.</summary>
    Merged,
}

public sealed record CatalystIngestResult(CatalystIngestOutcome Outcome, Guid? Id, CatalystPreClassification PreClass, double Score);

/// <summary>
/// Turns one <see cref="CatalystIntake"/> into a <see cref="CatalystEvent"/> row (IP-T443): dedupes on
/// (Source, ExternalId), classifies (the catalyst type, and the frozen version 1 class the score uses), counts the symbol's recent events for
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
        var externalId = Clip(item.ExternalId, 64);
        if (await db.CatalystEvents.AnyAsync(e => e.Source == item.Source && (e.ExternalId == item.ExternalId || e.ExternalId == externalId), ct)
            || await db.CatalystSightings.AnyAsync(s => s.Source == item.Source && s.ExternalId == externalId, ct))
            return new(CatalystIngestOutcome.Duplicate, null, pre, 0);

        // IP-T602: the same story from another source (same symbol, similar headline, inside the window). A wire
        // merges into it as a sighting; Alpaca news and EDGAR keep their own row, linked to the story.
        var story = item.Backfilled ? null : await FindStoryAsync(db, item, symbols, ct);
        if (story is not null && !CatalystIntake.KeepsOwnRow(item.Source))
        {
            db.CatalystSightings.Add(Sighting(item, story.Value.HeadId, story.Value.Similarity));
            await AdvanceFirstSeenAsync(db, story.Value.HeadId, item, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                logger.LogDebug(ex, "Catalyst sighting {Source}/{Id} already stored (concurrent insert).", item.Source, item.ExternalId);
                return new(CatalystIngestOutcome.Duplicate, null, pre, 0);
            }
            return new(CatalystIngestOutcome.Merged, story.Value.HeadId, pre, 0);
        }

        var primary = symbols[0];
        var isEdgar = item.Source == CatalystIntake.Edgar;
        var tier = CatalystPreClassifier.SourceTier(Rules, item.SourceName, isEdgar);
        var windowStart = item.PublishedUtc.AddDays(-Weights.NoveltyWindowDays);
        var noveltyQuery = db.CatalystEvents.Where(
            e => e.PrimarySymbol == primary && e.PublishedUtc >= windowStart && e.PublishedUtc < item.PublishedUtc);
        if (story is { HeadId: var head }) noveltyQuery = noveltyQuery.Where(e => e.Id != head && e.StoryId != head); // a copy of the same story is not news
        var novelty = await noveltyQuery.CountAsync(ct);
        var age = Math.Max(0, (item.ReceivedUtc - item.PublishedUtc).TotalMinutes);
        var (score, direction) = CatalystScorer.Score(Weights, pre.Legacy, pre.Direction, null, tier, novelty, age);

        var row = new CatalystEvent
        {
            Source = item.Source,
            ExternalId = externalId,
            PublishedUtc = item.PublishedUtc,
            ReceivedUtc = item.ReceivedUtc,
            PrimarySymbol = primary,
            SymbolsJson = Clip(JsonSerializer.Serialize(symbols), 2000),
            Headline = Clip(item.Headline, 500),
            Url = item.Url is null ? null : Clip(item.Url, 1000),
            FormType = item.FormType is null ? null : Clip(item.FormType, 16),
            ItemsJson = item.Items is null ? null : Clip(JsonSerializer.Serialize(item.Items), 200),
            SourceTier = tier,
            SourceName = item.SourceName is { Length: > 0 } sn ? Clip(sn, 64) : null,
            StampKind = CatalystStampKind.Classify(item.Source, item.SourceName, item.Url),
            PreClass = pre.Legacy.ToString(),
            CatalystType = pre.Class.ToString(),
            NonBindingLoi = pre.NonBindingLoi,
            PreDirection = pre.Direction,
            PreConfidence = pre.Confidence,
            NoveltyCount = novelty,
            CatalystScore = score,
            Direction = direction,
            ScoreComputedUtc = item.ReceivedUtc,
            RawSnippet = item.Summary is { Length: > 0 } s ? Clip(s, 8000) : null,
            Backfilled = item.Backfilled,
            FirstSeenUtc = item.Backfilled ? null : item.ReceivedUtc,
            FirstSeenSource = item.Backfilled ? null : Clip(item.Source, 32),
            StoryId = story?.HeadId,
        };
        db.CatalystEvents.Add(row);
        foreach (var symbol in symbols) db.CatalystEventSymbols.Add(new CatalystEventSymbol { EventId = row.Id, Symbol = symbol });
        if (!item.Backfilled)
        {
            db.CatalystSightings.Add(Sighting(item, story?.HeadId ?? row.Id, story?.Similarity ?? 1));
            if (story is { HeadId: var storyHead }) await AdvanceFirstSeenAsync(db, storyHead, item, ct);
        }

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

    /// <summary>How far apart two copies of one story may be published and still match (IP-T602).</summary>
    public static readonly TimeSpan StoryWindow = TimeSpan.FromHours(12);

    /// <summary>Headline similarity (token Jaccard, see <see cref="WireFeed.Similarity"/>) at or above which two items are one story.</summary>
    public const double StorySimilarity = 0.6;

    /// <summary>The story this item repeats: the best-matching earlier event naming one of its symbols, resolved to the story's first event.</summary>
    private static async Task<(Guid HeadId, double Similarity)?> FindStoryAsync(AppDbContext db, CatalystIntake item, List<string> symbols, CancellationToken ct)
    {
        var lo = item.PublishedUtc - StoryWindow;
        var hi = item.PublishedUtc + StoryWindow;
        var candidates = await (
                from s in db.CatalystEventSymbols
                join e in db.CatalystEvents on s.EventId equals e.Id
                where symbols.Contains(s.Symbol) && e.PublishedUtc >= lo && e.PublishedUtc <= hi && !e.Backfilled
                select new { e.Id, e.StoryId, e.Headline, e.ReceivedUtc, e.Source })
            .Distinct().OrderBy(e => e.ReceivedUtc).Take(200).ToListAsync(ct);
        // Alpaca news and EDGAR link only to a story a wire started, so their own scoring and novelty stay as before.
        var keepsOwnRow = CatalystIntake.KeepsOwnRow(item.Source);
        (Guid, double)? best = null;
        foreach (var c in candidates)
        {
            if (keepsOwnRow && !CatalystSources.IsWire(c.Source)) continue;
            if (string.Equals(c.Source, item.Source, StringComparison.OrdinalIgnoreCase)) continue; // one source's two releases are two stories
            var sim = WireFeed.Similarity(item.Headline, c.Headline);
            if (sim >= StorySimilarity && (best is null || sim > best.Value.Item2)) best = (c.StoryId ?? c.Id, sim);
        }
        return best;
    }

    private static CatalystSighting Sighting(CatalystIntake item, Guid eventId, double similarity) => new()
    {
        EventId = eventId,
        Source = Clip(item.Source, 32),
        ExternalId = Clip(item.ExternalId, 64),
        PublishedUtc = item.PublishedUtc,
        SeenUtc = item.ReceivedUtc,
        Headline = Clip(item.Headline, 300),
        Url = item.Url is null ? null : Clip(item.Url, 1000),
        Similarity = Math.Round(similarity, 3),
        Backfilled = item.Backfilled,
    };

    /// <summary>Keeps the story's first-seen at the EARLIEST sighting.</summary>
    private static async Task AdvanceFirstSeenAsync(AppDbContext db, Guid headId, CatalystIntake item, CancellationToken ct)
    {
        var head = await db.CatalystEvents.FirstOrDefaultAsync(e => e.Id == headId, ct);
        if (head is null) return;
        var first = head.FirstSeenUtc ?? head.ReceivedUtc;
        if (head.FirstSeenUtc is null)
        {
            head.FirstSeenUtc = first;
            head.FirstSeenSource ??= Clip(head.Source, 32);
        }
        if (item.ReceivedUtc < first)
        {
            head.FirstSeenUtc = item.ReceivedUtc;
            head.FirstSeenSource = Clip(item.Source, 32);
        }
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
