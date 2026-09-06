using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Turns the hand-maintained <c>data/sp-index-events.json</c> (announced S&amp;P 500 / S&amp;P 100
/// additions and deletions) into <see cref="ResearchClaim"/> rows with
/// <c>ClaimType = "IndexEvent"</c>. Index inclusion is a mechanical catalyst — passive funds
/// must buy the joiner and sell the leaver around the effective date — which is exactly the
/// "pending portent" shape <see cref="ResearchClaim.HasHappenedAlready"/> already models.
/// <para>
/// There is no free live feed for index membership, so the JSON file IS the source and the user
/// edits it when S&amp;P announces a change. Idempotent: an entry that already has its claim is
/// skipped; a Pending claim whose effective date has passed is flipped to Realized. Never throws
/// — failures log and count as zero new claims so one bad file doesn't abort the scan pass.
/// </para>
/// </summary>
public sealed class IndexEventScanner(IDbContextFactory<AppDbContext> dbFactory, ILogger<IndexEventScanner> logger)
{
    public const string ClaimType = "IndexEvent";
    public const string EnvVar = "IDIOTPROOF_INDEX_EVENTS_FILE";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Path to the events file. Resolution order: explicit assignment, <see cref="EnvVar"/>,
    /// <c>{app}/data/sp-index-events.json</c> (console host, linked Content), then
    /// <c>{app}/wwwroot/data/sp-index-events.json</c> (web host).
    /// </summary>
    public string? FilePath { get; set; }

    public sealed record IndexEvent(
        string Ticker,
        string Index,
        string Action,
        DateOnly AnnouncedDate,
        DateOnly? EffectiveDate,
        string? SourceUrl,
        string? Note)
    {
        public bool IsAdd => Action.Equals("Add", StringComparison.OrdinalIgnoreCase);
        public string IndexLabel => Index.Equals("SP100", StringComparison.OrdinalIgnoreCase) ? "S&P 100" : "S&P 500";

        /// <summary>Stable text used both for display and as the idempotency key (with Ticker).</summary>
        public string Summary =>
            $"{(IsAdd ? "Added to" : "Removed from")} the {IndexLabel}" +
            (EffectiveDate is { } e ? $" (effective {e:yyyy-MM-dd})" : " (effective date TBA)");
    }

    private sealed class EventsFile
    {
        [JsonPropertyName("events")] public List<RawEvent> Events { get; set; } = [];
    }

    private sealed class RawEvent
    {
        public string? Ticker { get; set; }
        public string? Index { get; set; }
        public string? Action { get; set; }
        public string? AnnouncedDate { get; set; }
        public string? EffectiveDate { get; set; }
        public string? SourceUrl { get; set; }
        public string? Note { get; set; }
    }

    public static string ResolveDefaultPath()
    {
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var baseDir = AppContext.BaseDirectory;
        var console = Path.Combine(baseDir, "data", "sp-index-events.json");
        if (File.Exists(console)) return console;
        return Path.Combine(baseDir, "wwwroot", "data", "sp-index-events.json");
    }

    /// <summary>Parses the file; malformed entries are logged and skipped, never fatal.</summary>
    public IReadOnlyList<IndexEvent> LoadEvents(string path)
    {
        if (!File.Exists(path))
        {
            logger.LogInformation("IndexEventScanner: no events file at {Path} — nothing to do.", path);
            return [];
        }

        EventsFile? file;
        try { file = JsonSerializer.Deserialize<EventsFile>(File.ReadAllText(path), JsonOpts); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IndexEventScanner: could not parse {Path}", path);
            return [];
        }
        if (file is null) return [];

        var events = new List<IndexEvent>();
        foreach (var raw in file.Events)
        {
            if (string.IsNullOrWhiteSpace(raw.Ticker) || string.IsNullOrWhiteSpace(raw.Index) || string.IsNullOrWhiteSpace(raw.Action)
                || !DateOnly.TryParse(raw.AnnouncedDate, System.Globalization.CultureInfo.InvariantCulture, out var announced))
            {
                logger.LogWarning("IndexEventScanner: skipping malformed entry {Entry}", JsonSerializer.Serialize(raw));
                continue;
            }
            if (!raw.Index.Equals("SP500", StringComparison.OrdinalIgnoreCase) && !raw.Index.Equals("SP100", StringComparison.OrdinalIgnoreCase)
                || !raw.Action.Equals("Add", StringComparison.OrdinalIgnoreCase) && !raw.Action.Equals("Remove", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("IndexEventScanner: skipping entry with unknown index/action {Ticker} {Index} {Action}", raw.Ticker, raw.Index, raw.Action);
                continue;
            }

            DateOnly? effective = DateOnly.TryParse(raw.EffectiveDate, System.Globalization.CultureInfo.InvariantCulture, out var eff) ? eff : null;
            events.Add(new IndexEvent(raw.Ticker.Trim().ToUpperInvariant(), raw.Index.Trim().ToUpperInvariant(), raw.Action.Trim(), announced, effective, raw.SourceUrl, raw.Note));
        }
        return events;
    }

    /// <summary>Returns the number of NEW claims persisted (Pending→Realized flips are not counted).</summary>
    public async Task<int> ScanAsync(CancellationToken ct = default) =>
        await ScanAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct);

    public async Task<int> ScanAsync(DateOnly today, CancellationToken ct = default)
    {
        var path = FilePath ?? ResolveDefaultPath();
        var events = LoadEvents(path);
        if (events.Count == 0) return 0;

        var persisted = 0;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await db.ResearchClaims.Where(c => c.ClaimType == ClaimType).ToListAsync(ct);

            foreach (var ev in events)
            {
                var happened = ev.EffectiveDate is { } e && e <= today;
                var match = existing.FirstOrDefault(c =>
                    c.Ticker.Equals(ev.Ticker, StringComparison.OrdinalIgnoreCase) && c.ClaimSummary == ev.Summary);

                if (match is not null)
                {
                    if (happened && !match.HasHappenedAlready)
                    {
                        match.HasHappenedAlready = true;
                        match.Status = "Realized";
                        match.OutcomeDate ??= ev.EffectiveDate;
                    }
                    continue;
                }

                db.ResearchClaims.Add(ToClaim(ev, happened));
                persisted++;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IndexEventScanner: persisting claims failed");
            return 0;
        }

        if (persisted > 0) logger.LogInformation("IndexEventScanner: {Count} new index-event claims from {Path}", persisted, path);
        return persisted;
    }

    private static ResearchClaim ToClaim(IndexEvent ev, bool happened)
    {
        var isSp500 = ev.Index.Equals("SP500", StringComparison.OrdinalIgnoreCase);
        var mechanism = ev.IsAdd
            ? $"index funds tracking the {ev.IndexLabel} must buy {ev.Ticker} around the effective date (forced passive demand), and the listing raises analyst/ETF visibility"
            : $"index funds tracking the {ev.IndexLabel} must sell {ev.Ticker} around the effective date (forced passive supply)";
        var timing = ev.EffectiveDate is { } e ? $"around {e:MMM d, yyyy}" : "effective date not yet announced";

        return new ResearchClaim
        {
            Ticker = ev.Ticker,
            IsMacro = false,
            SourceName = "S&P Dow Jones Indices (user-logged)",
            SourceUrl = ev.SourceUrl,
            SourceTier = string.IsNullOrWhiteSpace(ev.SourceUrl) ? 3 : 1,
            ArticleDate = ev.AnnouncedDate,
            ClaimSummary = ev.Summary,
            ClaimType = ClaimType,
            Sentiment = ev.IsAdd ? "Bullish" : "Bearish",
            Magnitude = isSp500 ? "High" : "Medium",
            HasHappenedAlready = happened,
            PendingTrigger = happened ? null : "Index rebalance effective date",
            ExpectedTimeline = ev.EffectiveDate?.ToString("yyyy-MM-dd") ?? "TBA",
            TriggerConfidence = "High",
            Status = happened ? "Realized" : "Pending",
            OutcomeDate = happened ? ev.EffectiveDate : null,
            LlmAnswer = $"{ev.Summary}. Expected impact {timing}: {mechanism}.{(string.IsNullOrWhiteSpace(ev.Note) ? "" : $" Note: {ev.Note}")}",
            RawArticleSnippet = ev.Note,
        };
    }
}
