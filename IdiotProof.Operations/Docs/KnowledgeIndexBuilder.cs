namespace IdiotProof.Operations.Docs;

/// <summary>
/// Builds a <see cref="KnowledgeIndex"/> from a repo checkout. Every source is optional: a missing
/// file or tool becomes a warning on the index, never an exception, so <c>docs</c> operations keep
/// answering from whatever was found.
/// </summary>
public sealed partial class KnowledgeIndexBuilder(string repoRoot)
{
    /// <summary>Points the Hub at a checkout explicitly (a published Hub has none next to it).</summary>
    public const string RepoEnvVar = "IDIOTPROOF_REPO";

    /// <summary>The BIBLE, indexed by section (<c>IP-§n</c>) and law (<c>IP-LAW-n</c>).</summary>
    public const string BibleFile = "docs/BIBLE.md";

    /// <summary>Other prose indexed heading by heading (IP-T260).</summary>
    public static IReadOnlyList<string> DocFiles { get; } =
    [
        "docs/BIBLE.digest.md",
        "docs/AMENDMENTS.md",
        "README.md",
        "docs/agent/IDIOTPROOF_PROTOCOL.md",
        "docs/ALPACA_COVERAGE.md",
    ];

    public string RepoRoot { get; } = repoRoot;

    /// <summary>The <c>tickets</c> CLI for ticket history (IP-T264); null indexes the USER_STORIES ticket table only.</summary>
    public ITicketCli? Tickets { get; init; }

    /// <summary>
    /// The checkout to index: <see cref="RepoEnvVar"/> when set, else the nearest folder at or above
    /// each start directory that holds <c>IdiotProof.slnx</c> and <c>docs/BIBLE.md</c>; null when none.
    /// </summary>
    public static string? FindRepoRoot(params string?[] starts)
    {
        var fromEnv = Environment.GetEnvironmentVariable(RepoEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv)) return Path.GetFullPath(fromEnv);

        foreach (var start in starts)
        {
            if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start)) continue;
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "IdiotProof.slnx")) && File.Exists(Path.Combine(dir.FullName, "docs", "BIBLE.md")))
                    return dir.FullName;
            }
        }
        return null;
    }

    /// <summary>Reads every source into a fresh index.</summary>
    public KnowledgeIndex Build(CancellationToken ct = default)
    {
        var index = new KnowledgeIndex { RepoRoot = RepoRoot };
        IndexDocs(index);
        IndexStories(index);
        ct.ThrowIfCancellationRequested();
        index.Sources = ScanSources(index, ct);
        IndexXmlDocs(index);
        IndexCatalogs(index);
        ct.ThrowIfCancellationRequested();
        if (Tickets is not null) new TicketHistory(Tickets).IndexIntoAsync(index, ct).GetAwaiter().GetResult();
        return index;
    }

    /// <summary>IP-T260: the BIBLE by section and law, then the digest, amendments, README and agent docs by heading.</summary>
    internal void IndexDocs(KnowledgeIndex index)
    {
        if (ReadText(index, BibleFile) is { } bible) index.AddMarkdown(BibleFile, bible, bible: true);
        foreach (var file in DocFiles)
        {
            if (ReadText(index, file, optional: true) is { } text) index.AddMarkdown(file, text);
        }
    }

    /// <summary>The file's text, or null with a warning (silently for <paramref name="optional"/> files).</summary>
    internal string? ReadText(KnowledgeIndex index, string relative, bool optional = false)
    {
        var path = FullPath(relative);
        if (File.Exists(path)) return File.ReadAllText(path);
        if (!optional) index.Warn($"{relative} not found under {RepoRoot}");
        return null;
    }

    internal string FullPath(string relative) => Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A path under the repo as <c>a/b/c.cs</c>.</summary>
    internal string Relative(string fullPath) => Path.GetRelativePath(RepoRoot, fullPath).Replace('\\', '/');
}
