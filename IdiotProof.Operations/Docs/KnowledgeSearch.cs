using System.Text.RegularExpressions;

namespace IdiotProof.Operations.Docs;

/// <summary>One ranked search result with a short excerpt around the first hit.</summary>
public sealed record KnowledgeHit(KnowledgeEntry Entry, double Score, string Snippet);

/// <summary>
/// Ranked full-text search across every index (IP-T266). An exact id (<c>IP-LAW-3</c>,
/// <c>IP-US-K1</c>, <c>IP-T259</c>, <c>§4</c>) comes first; then entries holding every term,
/// weighted by where the terms appear (id and title over body), how often (capped, so one long
/// section cannot drown the rest) and the kind (laws and sections over symbols). When nothing holds
/// every term, entries holding most of them are returned instead.
/// </summary>
public static partial class KnowledgeSearch
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "the", "of", "to", "in", "is", "it", "for", "on", "or", "be", "by", "as", "at", "how", "what", "does", "do", "i",
    };

    public static IReadOnlyList<KnowledgeHit> Search(KnowledgeIndex index, string query, int limit = 15, KnowledgeKind? kind = null)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];
        var terms = Terms(q);
        var exactIds = new[] { q, KnowledgeIndex.SectionId(q), KnowledgeIndex.LawId(q), KnowledgeIndex.StoryId(q), KnowledgeIndex.TicketId(q) }
            .Where(id => id is not null)
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A bare number is a section, a law and a ticket at once; only exact text counts as an id there.
        if (int.TryParse(q, out _)) exactIds = [q];

        var scored = new List<KnowledgeHit>();
        var partial = new List<KnowledgeHit>();
        foreach (var e in index.Entries)
        {
            if (kind is { } k && e.Kind != k) continue;
            var exact = exactIds.Contains(e.Id) || e.Id.EndsWith(":" + q, StringComparison.OrdinalIgnoreCase)
                        || (e.Kind == KnowledgeKind.Symbol && string.Equals(e.Title, q, StringComparison.OrdinalIgnoreCase));
            var (score, matched) = Score(e, q, terms);
            if (exact) score += 1000;
            if (score <= 0) continue;
            var hit = new KnowledgeHit(e, Math.Round(score, 2), Snippet(e.Text, terms));
            if (exact || matched == terms.Count) scored.Add(hit);
            else if (terms.Count > 1 && matched * 2 >= terms.Count) partial.Add(hit);
        }

        var results = scored.Count > 0 ? scored : partial;
        return [.. results
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Entry.Kind)
            .ThenBy(h => h.Entry.Id, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))];
    }

    /// <summary>Lower-cased words of two or more characters, stop words dropped (all kept if that leaves none).</summary>
    public static IReadOnlyList<string> Terms(string query)
    {
        var words = WordPattern().Matches(query).Select(m => m.Value.Trim('.', '-', '_').ToLowerInvariant()).Where(w => w.Length >= 2).Distinct().ToList();
        var kept = words.Where(w => !StopWords.Contains(w)).ToList();
        return kept.Count > 0 ? kept : words;
    }

    private static (double Score, int Matched) Score(KnowledgeEntry e, string phrase, IReadOnlyList<string> terms)
    {
        var id = e.Id.ToLowerInvariant();
        var title = e.Title.ToLowerInvariant();
        var text = e.Text.ToLowerInvariant();
        double score = 0;
        var matched = 0;
        foreach (var t in terms)
        {
            var inId = id.Contains(t, StringComparison.Ordinal);
            var inTitle = title.Contains(t, StringComparison.Ordinal);
            var inText = Count(text, t, cap: 5);
            if (!inId && !inTitle && inText == 0) continue;
            matched++;
            score += (inId ? 6 : 0) + (inTitle ? 8 : 0) + inText;
        }
        if (matched == 0) return (0, 0);
        if (terms.Count > 1)
        {
            var p = phrase.ToLowerInvariant();
            if (title.Contains(p, StringComparison.Ordinal)) score += 25;
            else if (text.Contains(p, StringComparison.Ordinal)) score += 10;
        }
        // Long bodies collect hits by sheer size; damp them gently.
        score /= 1 + Math.Log10(1 + text.Length / 2000.0);
        return (score * Weight(e.Kind), matched);
    }

    private static double Weight(KnowledgeKind kind) => kind switch
    {
        KnowledgeKind.Law => 1.4,
        KnowledgeKind.Section => 1.25,
        KnowledgeKind.Story => 1.15,
        KnowledgeKind.Doc => 1.0,
        KnowledgeKind.Catalog => 1.0,
        KnowledgeKind.Ticket => 0.9,
        KnowledgeKind.Symbol => 0.8,
        _ => 1.0,
    };

    private static int Count(string text, string term, int cap)
    {
        var n = 0;
        for (var at = text.IndexOf(term, StringComparison.Ordinal); at >= 0 && n < cap; at = text.IndexOf(term, at + term.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    /// <summary>About 160 characters of <paramref name="text"/> around the first term, on one line.</summary>
    public static string Snippet(string text, IReadOnlyList<string> terms, int width = 160)
    {
        var flat = Whitespace().Replace(text, " ").Trim();
        if (flat.Length <= width) return flat;
        var first = terms.Select(t => flat.IndexOf(t, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, first - width / 3);
        var end = Math.Min(flat.Length, start + width);
        start = Math.Max(0, end - width);
        return (start > 0 ? "…" : "") + flat[start..end].Trim() + (end < flat.Length ? "…" : "");
    }

    [GeneratedRegex(@"[\p{L}\p{N}§#._-]+")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
