using System.Text.RegularExpressions;

namespace IdiotProof.Operations.Docs;

/// <summary>A place in the checkout: repo-relative path (forward slashes) and 1-based line.</summary>
public sealed record SourceLocation(string Path, int Line)
{
    public override string ToString() => $"{Path}:{Line}";
}

/// <summary>A line that mentions a symbol, for <c>docs explain</c>'s callers list.</summary>
public sealed record SourceReference(string Path, int Line, string Text)
{
    public override string ToString() => $"{Path}:{Line}";
}

/// <summary>
/// Symbol → <c>path:line</c> for every C# type and member declared in the checkout (IP-T263), built
/// by scanning the source text once (no Roslyn: it must be cheap enough to run at Hub start). Full
/// names match the XML doc ids without their prefix: <c>IdiotProof.Core.Trading.TransactionGateway</c>,
/// <c>IdiotProof.Core.Trading.TransactionGateway.SubmitAsync</c>, constructors as <c>.#ctor</c>.
/// Nested types are keyed by namespace plus their own name. The text is kept so callers can be
/// found on demand.
/// </summary>
public sealed partial class SourceMap
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", "node_modules", "Migrations", "deps", "TestResults", "cypress",
    };

    private readonly Dictionary<string, SourceLocation> types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SourceLocation>> members = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> typesBySimpleName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Path, string[] Lines)> files = [];

    public int FileCount => files.Count;

    public int TypeCount => types.Count;

    public IReadOnlyCollection<string> TypeNames => types.Keys;

    /// <summary>Scans every <c>.cs</c> file under <paramref name="repoRoot"/> except build output and migrations.</summary>
    public static SourceMap Scan(string repoRoot, CancellationToken ct = default)
    {
        var map = new SourceMap();
        foreach (var file in EnumerateSources(repoRoot))
        {
            ct.ThrowIfCancellationRequested();
            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch (IOException) { continue; }
            map.AddFile(Path.GetRelativePath(repoRoot, file).Replace('\\', '/'), lines);
        }
        return map;
    }

    /// <summary>Indexes one file's declarations (tests feed text directly).</summary>
    public void AddFile(string relativePath, string[] lines)
    {
        files.Add((relativePath, lines));
        var ns = "";
        string? currentType = null;
        var inBlockComment = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (inBlockComment)
            {
                if (trimmed.Contains("*/", StringComparison.Ordinal)) inBlockComment = false;
                continue;
            }
            if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                inBlockComment = !trimmed.Contains("*/", StringComparison.Ordinal);
                continue;
            }
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*') || trimmed.Length == 0) continue;

            var nsMatch = NamespacePattern().Match(line);
            if (nsMatch.Success)
            {
                ns = nsMatch.Groups[1].Value;
                continue;
            }

            var typeMatch = TypePattern().Match(line);
            if (typeMatch.Success)
            {
                currentType = typeMatch.Groups[2].Value;
                var full = ns.Length == 0 ? currentType : $"{ns}.{currentType}";
                var location = new SourceLocation(relativePath, i + 1);
                if (types.TryAdd(full, location))
                {
                    if (!typesBySimpleName.TryGetValue(currentType, out var list)) typesBySimpleName[currentType] = list = [];
                    list.Add(full);
                }
                continue;
            }

            if (currentType is null) continue;
            var memberMatch = MemberPattern().Match(line);
            if (!memberMatch.Success) continue;
            var name = memberMatch.Groups[1].Value;
            if (name is "if" or "for" or "foreach" or "while" or "switch" or "using" or "return" or "new" or "lock" or "catch") continue;
            if (name == currentType) name = "#ctor";
            var key = $"{(ns.Length == 0 ? "" : ns + ".")}{currentType}.{name}";
            if (!members.TryGetValue(key, out var at)) members[key] = at = [];
            at.Add(new SourceLocation(relativePath, i + 1));
        }
    }

    /// <summary>
    /// Where a type or member (full name without parameters, as in an XML doc id) is declared:
    /// the exact name first, then the same namespace with only the last type segment (nested types).
    /// </summary>
    public SourceLocation? Find(string fullName)
    {
        if (types.TryGetValue(fullName, out var type)) return type;
        if (members.TryGetValue(fullName, out var member)) return member[0];

        // Nested type or a member of one: Ns.Outer.Inner(.Member) was recorded as Ns.Inner(.Member).
        var parts = fullName.Split('.');
        for (var drop = 1; drop < parts.Length - 1; drop++)
        {
            var shorter = string.Join('.', parts.Take(parts.Length - 1 - drop).Concat(parts.Skip(parts.Length - 1)));
            if (types.TryGetValue(shorter, out type)) return type;
            var asMember = string.Join('.', parts.Take(parts.Length - 2 - drop).Concat(parts.Skip(parts.Length - 2)));
            if (members.TryGetValue(asMember, out member)) return member[0];
        }
        return null;
    }

    /// <summary>Full names of every type with this simple name (<c>OrderIntent</c> → <c>IdiotProof.Core.Trading.OrderIntent</c>).</summary>
    public IReadOnlyList<string> TypesNamed(string simpleName) =>
        typesBySimpleName.TryGetValue(simpleName, out var list) ? list : [];

    /// <summary>Every member full name ending in <c>.Name</c> (case-insensitive).</summary>
    public IEnumerable<string> MembersNamed(string name) =>
        members.Keys.Where(k => k.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Lines that mention <paramref name="name"/> as a whole word, excluding its own declaration
    /// lines and comments: a text-level approximation of "callers", newest-first order not implied.
    /// </summary>
    public IReadOnlyList<SourceReference> References(string name, int max = 20, SourceLocation? declaredAt = null)
    {
        var word = new Regex($@"\b{Regex.Escape(name)}\b", RegexOptions.CultureInvariant);
        var found = new List<SourceReference>();
        foreach (var (path, lines) in files)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.Contains(name, StringComparison.Ordinal) || !word.IsMatch(line)) continue;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*')) continue;
                if (declaredAt is not null && declaredAt.Path == path && declaredAt.Line == i + 1) continue;
                if (TypePattern().IsMatch(line) && TypePattern().Match(line).Groups[2].Value == name) continue;
                found.Add(new SourceReference(path, i + 1, trimmed.Length > 140 ? trimmed[..139] + "…" : trimmed));
                if (found.Count >= max) return found;
            }
        }
        return found;
    }

    private static IEnumerable<string> EnumerateSources(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> subdirs, csFiles;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir).ToList();
                csFiles = Directory.EnumerateFiles(dir, "*.cs").ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in csFiles) yield return f;
            foreach (var d in subdirs)
            {
                if (!SkippedFolders.Contains(Path.GetFileName(d))) pending.Push(d);
            }
        }
    }

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)")]
    private static partial Regex NamespacePattern();

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|ref|unsafe|file|new)\s+)*(class|record\s+struct|record\s+class|record|struct|interface|enum)\s+(\w+)")]
    private static partial Regex TypePattern();

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|static|override|virtual|async|sealed|abstract|readonly|required|partial|new|extern|unsafe|const|event)\s+)+(?:[\w.<>\[\]?,]+(?:\s*<[^>]*>)?\??\s+)?(\w+)\s*(?:<[^>()]*>)?\s*(?:\(|\{|=>|=|;)")]
    private static partial Regex MemberPattern();
}
