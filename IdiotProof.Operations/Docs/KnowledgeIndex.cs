using System.Text.RegularExpressions;

namespace IdiotProof.Operations.Docs;

/// <summary>
/// Everything IdiotProof knows about itself, in one queryable list (IP-T259): BIBLE sections and
/// laws, other docs' headings, user stories, tickets, documented symbols and static catalog
/// entries. Built once (see <see cref="KnowledgeIndexBuilder"/>) and then only read, so it is safe
/// to share across calls. Ids are case-insensitive; the first entry added under an id wins.
/// </summary>
public sealed partial class KnowledgeIndex
{
    private readonly List<KnowledgeEntry> entries = [];
    private readonly Dictionary<string, KnowledgeEntry> byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> warnings = [];

    /// <summary>The repo checkout the index was built from, when there was one.</summary>
    public string? RepoRoot { get; init; }

    /// <summary>When the index was built (UTC).</summary>
    public DateTime BuiltUtc { get; init; } = DateTime.UtcNow;

    public IReadOnlyList<KnowledgeEntry> Entries => entries;

    /// <summary>Symbol → <c>path:line</c> and the source text for callers (IP-T263); null when no checkout was scanned.</summary>
    public SourceMap? Sources { get; set; }

    /// <summary>Sources that could not be read (missing file, tickets CLI absent); never fatal.</summary>
    public IReadOnlyList<string> Warnings => warnings;

    public int Count => entries.Count;

    public void Add(KnowledgeEntry entry)
    {
        entries.Add(entry);
        byId.TryAdd(entry.Id, entry);
    }

    public void Warn(string message) => warnings.Add(message);

    /// <summary>The entry with this exact id (case-insensitive), or null.</summary>
    public KnowledgeEntry? Get(string id) => byId.GetValueOrDefault(id.Trim());

    public IEnumerable<KnowledgeEntry> OfKind(KnowledgeKind kind) => entries.Where(e => e.Kind == kind);

    /// <summary>
    /// Indexes one markdown file. With <paramref name="bible"/> set, numbered headings become
    /// <see cref="KnowledgeKind.Section"/> entries (<c>IP-§4</c>, <c>IP-§4.4</c>) and
    /// <c>{#IP-LAW-n}</c> headings become <see cref="KnowledgeKind.Law"/>; every other heading is a
    /// <see cref="KnowledgeKind.Doc"/> entry with id <c>source#anchor</c>. Returns how many were added.
    /// </summary>
    public int AddMarkdown(string source, string text, bool bible = false)
    {
        var added = 0;
        string? parent = null;
        foreach (var s in MarkdownSections.Parse(text))
        {
            KnowledgeEntry entry;
            if (bible && s.ExplicitAnchor && LawAnchor().IsMatch(s.Anchor))
            {
                entry = new KnowledgeEntry(KnowledgeKind.Law, s.Anchor, s.Title, s.Body, source, s.Line) { Parent = "IP-§5" };
            }
            else if (bible && MarkdownSections.SectionNumber(s) is { } number)
            {
                var id = "IP-§" + number;
                var dot = number.IndexOf('.');
                entry = new KnowledgeEntry(KnowledgeKind.Section, id, s.Title, s.Body, source, s.Line)
                {
                    Parent = dot > 0 ? "IP-§" + number[..dot] : null,
                };
                if (dot < 0) parent = id;
            }
            else
            {
                entry = new KnowledgeEntry(KnowledgeKind.Doc, $"{source}#{s.Anchor}", s.Title, s.Body, source, s.Line)
                {
                    Parent = s.Level > 2 ? parent : null,
                };
            }
            Add(entry);
            added++;
        }
        return added;
    }

    /// <summary>
    /// <c>4</c>, <c>§4</c>, <c>IP-§4</c>, <c>4.4</c>, <c>#IP-§4</c> → <c>IP-§4</c> / <c>IP-§4.4</c>; null when it is not a section reference.
    /// </summary>
    public static string? SectionId(string input)
    {
        var m = SectionInput().Match(input.Trim());
        return m.Success ? "IP-§" + m.Groups[1].Value : null;
    }

    /// <summary><c>3</c>, <c>LAW-3</c>, <c>IP-LAW-3</c>, <c>law3</c> → <c>IP-LAW-3</c>; null otherwise.</summary>
    public static string? LawId(string input)
    {
        var m = LawInput().Match(input.Trim());
        return m.Success ? "IP-LAW-" + int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary><c>K1</c>, <c>US-K1</c>, <c>ip-us-k1</c> → <c>IP-US-K1</c>; null otherwise.</summary>
    public static string? StoryId(string input)
    {
        var m = StoryInput().Match(input.Trim());
        return m.Success ? "IP-US-" + m.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary><c>259</c>, <c>T259</c>, <c>IP-T259</c> → <c>IP-T259</c>; null otherwise.</summary>
    public static string? TicketId(string input)
    {
        var m = TicketInput().Match(input.Trim());
        return m.Success ? "IP-T" + int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"^IP-LAW-\d+$")]
    private static partial Regex LawAnchor();

    [GeneratedRegex(@"^#?(?:IP-)?§?\s*(\d+(?:\.\d+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SectionInput();

    [GeneratedRegex(@"^#?(?:IP-)?(?:LAW-?)?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex LawInput();

    [GeneratedRegex(@"^(?:IP-)?(?:US-)?([A-Z]\d+[A-Z]?)$", RegexOptions.IgnoreCase)]
    private static partial Regex StoryInput();

    [GeneratedRegex(@"^(?:IP-)?T?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TicketInput();
}
