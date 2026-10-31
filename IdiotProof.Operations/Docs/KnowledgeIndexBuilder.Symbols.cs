namespace IdiotProof.Operations.Docs;

public sealed partial class KnowledgeIndexBuilder
{
    /// <summary>
    /// Where compiled XML doc files are read from, most current first. Defaults to the host's own
    /// folder (the Hub ships every referenced library's doc file next to its DLLs) and then each
    /// project's <c>bin/*/net10.0*</c> folder in the checkout (so front doors the Hub does not
    /// reference, like Blazor and the CLI, are covered when built).
    /// </summary>
    public IReadOnlyList<string>? XmlDocDirectories { get; init; }

    /// <summary>IP-T262: every documented type and member of every IdiotProof library.</summary>
    internal void IndexXmlDocs(KnowledgeIndex index)
    {
        var members = XmlDocs.Load(XmlDocDirectories ?? DefaultXmlDocDirectories());
        if (members.Count == 0)
        {
            index.Warn("no IdiotProof XML doc files found (build the solution to produce them)");
            return;
        }
        foreach (var m in members)
        {
            var text = m.Remarks.Length == 0 ? m.Summary : $"{m.Summary}\n\n{m.Remarks}";
            index.Add(new KnowledgeEntry(KnowledgeKind.Symbol, m.MemberId, m.DisplayName, text, m.Assembly + ".xml", 0)
            {
                Parent = m.KindLetter == 'T' ? null : "T:" + m.FullName[..Math.Max(0, m.FullName.LastIndexOf('.'))],
            });
        }
    }

    private IEnumerable<string> DefaultXmlDocDirectories()
    {
        yield return AppContext.BaseDirectory;
        foreach (var project in SafeDirectories(RepoRoot, "IdiotProof.*"))
        {
            if (project.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var config in SafeDirectories(Path.Combine(project, "bin"), "*"))
                foreach (var tfm in SafeDirectories(config, "net10.0*"))
                    yield return tfm;
        }
    }

    private static IEnumerable<string> SafeDirectories(string path, string pattern) =>
        Directory.Exists(path) ? Directory.EnumerateDirectories(path, pattern) : [];
}
