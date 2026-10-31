using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Research;

/// <summary>
/// One research claim as the research operations list it: a flat, serializable projection of
/// <see cref="ResearchClaim"/> (the raw snippet and LLM text stay on <c>research claim</c>).
/// </summary>
public sealed record ResearchClaimRow(
    Guid Id,
    string Short,
    string Ticker,
    DateOnly ArticleDate,
    string Type,
    string Sentiment,
    string Magnitude,
    bool Portent,
    string Status,
    double? Score,
    string Summary,
    string Source,
    int SourceTier,
    bool IsMacro,
    IReadOnlyList<string> AffectedTickers,
    decimal? OutcomePct,
    DateTime CreatedUtc);

/// <summary>
/// Shared plumbing for the <c>research</c> operations: the SQL context, claim-id resolution
/// (full id or a short prefix), the list projection and the claims table. Research rows are
/// global (the scanner writes one shared feed), so these reads are not owner-scoped.
/// </summary>
public static class ResearchSupport
{
    /// <summary>The shared SQL context factory.</summary>
    public static IDbContextFactory<AppDbContext> Db(OperationContext ctx) => ctx.Get<IDbContextFactory<AppDbContext>>();

    /// <summary>Midnight ET at the start of an ET date, as UTC; null passes through.</summary>
    public static DateTime? SinceUtc(DateOnly? etDate) => etDate is { } d ? AccountApis.EtMidnightUtc(d) : null;

    /// <summary>The first 8 hex digits of an id: what tables show and what <see cref="ResolveClaimAsync"/> accepts.</summary>
    public static string Short(Guid id) => id.ToString("N")[..8];

    /// <summary>
    /// Finds exactly one claim by full id or by an id prefix of at least 4 hex digits, or throws
    /// a usage error naming the candidates.
    /// </summary>
    public static async Task<ResearchClaim> ResolveClaimAsync(OperationContext ctx, string reference)
    {
        var r = (reference ?? "").Trim();
        if (r.Length == 0) throw OperationException.Usage("Name a claim: its id or the 8-character prefix `research claims` shows.");

        await using var db = await Db(ctx).CreateDbContextAsync(ctx.CancellationToken);
        if (Guid.TryParse(r, out var id))
            return await db.ResearchClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ctx.CancellationToken)
                ?? throw OperationException.Usage($"No research claim has id {id}.");

        var compact = r.Replace("-", "").ToLowerInvariant();
        if (compact.Length < 4 || !compact.All(Uri.IsHexDigit))
            throw OperationException.Usage($"'{r}' is not a claim id. Use the id (or its first 8 characters) from `research claims`.");

        // The first 8 hex digits are the same in the "D" and "N" forms, so filter on them in SQL
        // and finish a longer prefix in memory.
        var head = compact.Length > 8 ? compact[..8] : compact;
        var candidates = await db.ResearchClaims.AsNoTracking()
            .Where(c => c.Id.ToString().StartsWith(head))
            .Take(50)
            .ToListAsync(ctx.CancellationToken);
        var matches = candidates.Where(c => c.Id.ToString("N").StartsWith(compact, StringComparison.OrdinalIgnoreCase)).ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw OperationException.Usage($"No research claim id starts with '{r}'. `idiotproof research claims` lists them."),
            _ => throw OperationException.Usage($"'{r}' matches {matches.Count} claims — use more of the id:\n" +
                string.Join("\n", matches.Take(10).Select(m => $"  {m.Id}  {m.Ticker,-6} {m.ClaimSummary}"))),
        };
    }

    /// <summary>The tickers a macro claim names (its JSON array), or empty for a descriptive count / malformed text.</summary>
    public static IReadOnlyList<string> AffectedTickers(string? affectedTickersJson)
    {
        if (string.IsNullOrWhiteSpace(affectedTickersJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(affectedTickersJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static ResearchClaimRow ToRow(ResearchClaim c) => new(
        c.Id, Short(c.Id), c.Ticker, c.ArticleDate, c.ClaimType, c.Sentiment, c.Magnitude, !c.HasHappenedAlready,
        c.Status, c.SignificanceScore is { } s ? Math.Round(s, 1) : null, c.ClaimSummary, c.SourceName, c.SourceTier,
        c.IsMacro, AffectedTickers(c.AffectedTickersJson), c.OutcomePctChange is { } o ? Math.Round(o, 2) : null, c.CreatedUtc);

    /// <summary>The ranked claims table every list view prints.</summary>
    public static string ClaimsTable(IEnumerable<ResearchClaimRow> rows, int? width)
    {
        var table = new TextTable { MaxWidth = width }
            .Column("id").Column("score", Align.Right).Column("date").Column("ticker").Column("type")
            .Column("sent").Column("mag").Column("kind").Column("status").Column("outcome", Align.Right)
            .Column("summary", maxWidth: 70);
        foreach (var r in rows)
            table.Row(r.Short, Score(r.Score), r.ArticleDate.ToString("yyyy-MM-dd"), TickerCell(r), r.Type,
                r.Sentiment, r.Magnitude, r.Portent ? "portent" : "event", r.Status,
                r.OutcomePct is { } o ? Fmt.PercentPoints(o, 1, signed: true) : "", r.Summary);
        return table.Render();
    }

    /// <summary>A score to one decimal, or "-" while the scanner has not scored the claim.</summary>
    public static string Score(double? score) => score is { } s ? N(s) : "-";

    /// <summary>A double in a fixed, culture-invariant format.</summary>
    public static string N(double value, string format = "0.0") => value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    private static string TickerCell(ResearchClaimRow r)
    {
        if (!r.IsMacro) return r.Ticker;
        return r.AffectedTickers.Count switch
        {
            0 => "(macro)",
            1 => $"(macro) {r.AffectedTickers[0]}",
            _ => $"(macro) {r.AffectedTickers.Count} tickers",
        };
    }
}
