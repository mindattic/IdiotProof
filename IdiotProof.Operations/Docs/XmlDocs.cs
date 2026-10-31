using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IdiotProof.Operations.Docs;

/// <summary>One documented type or member read from a compiler XML doc file.</summary>
/// <param name="MemberId">The compiler's id: <c>T:IdiotProof.Core.Trading.TransactionGateway</c>, <c>M:…SubmitAsync(…)</c>.</param>
/// <param name="Assembly">The assembly the doc file belongs to.</param>
/// <param name="Summary">The summary as plain text (<c>&lt;c&gt;</c> → backticks, crefs → short names).</param>
/// <param name="Remarks">The remarks, params and returns as plain text; empty when none.</param>
public sealed record XmlDocMember(string MemberId, string Assembly, string Summary, string Remarks)
{
    /// <summary>T (type), M (method or constructor), P (property), F (field), E (event).</summary>
    public char KindLetter => MemberId[0];

    /// <summary>The id without its kind prefix or parameter list: <c>IdiotProof.Core.Trading.TransactionGateway.SubmitAsync</c>.</summary>
    public string FullName => XmlDocs.StripParameters(MemberId[2..]);

    /// <summary><c>TransactionGateway</c> for a type, <c>TransactionGateway.SubmitAsync</c> for a member.</summary>
    public string DisplayName => XmlDocs.DisplayName(MemberId);
}

/// <summary>
/// Reads the XML doc comments every IdiotProof library ships (IP-T262; <c>GenerateDocumentationFile</c>
/// is on in Directory.Build.props) and turns their markup into plain text for the knowledge index.
/// </summary>
public static partial class XmlDocs
{
    /// <summary>
    /// Every member of every <c>IdiotProof*.xml</c> doc file in <paramref name="directories"/> (test
    /// assemblies skipped). The first file seen for an assembly wins, so list the most current
    /// directory first.
    /// </summary>
    public static IReadOnlyList<XmlDocMember> Load(IEnumerable<string> directories)
    {
        var seenAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var members = new List<XmlDocMember>();
        foreach (var dir in directories.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.xml"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!name.StartsWith("IdiotProof", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("idiotproof", StringComparison.Ordinal)) continue;
                if (name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)) continue;
                XDocument doc;
                try { doc = XDocument.Load(file); }
                catch (Exception e) when (e is System.Xml.XmlException or IOException) { continue; }
                var assembly = doc.Root?.Element("assembly")?.Element("name")?.Value;
                if (doc.Root?.Name != "doc" || assembly is null || !seenAssemblies.Add(assembly)) continue;
                members.AddRange(Parse(doc, assembly));
            }
        }
        return members;
    }

    /// <summary>The members of one doc file.</summary>
    public static IEnumerable<XmlDocMember> Parse(XDocument doc, string assembly)
    {
        foreach (var member in doc.Root?.Element("members")?.Elements("member") ?? [])
        {
            var id = (string?)member.Attribute("name");
            if (string.IsNullOrEmpty(id) || id.Length < 3 || id[1] != ':') continue;
            var summary = Text(member.Element("summary"));
            var extra = new StringBuilder();
            foreach (var p in member.Elements("param"))
                extra.Append("Parameter ").Append((string?)p.Attribute("name")).Append(": ").AppendLine(Text(p));
            if (member.Element("returns") is { } returns) extra.Append("Returns: ").AppendLine(Text(returns));
            if (member.Element("remarks") is { } remarks) extra.AppendLine(Text(remarks));
            if (member.Element("example") is { } example) extra.Append("Example: ").AppendLine(Text(example));
            yield return new XmlDocMember(id, assembly, summary, extra.ToString().Trim());
        }
    }

    /// <summary>Doc markup to text: code spans in backticks, crefs and paramrefs by short name, whitespace collapsed per paragraph.</summary>
    public static string Text(XElement? element)
    {
        if (element is null) return "";
        var sb = new StringBuilder();
        Append(element, sb);
        var lines = sb.ToString().Replace("\r\n", "\n").Split('\n').Select(l => Spaces().Replace(l, " ").Trim());
        return Blank().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    private static void Append(XNode node, StringBuilder sb)
    {
        switch (node)
        {
            case XText text:
                sb.Append(text.Value);
                break;
            case XElement e when e.Name == "c":
                sb.Append('`').Append(e.Value).Append('`');
                break;
            case XElement e when e.Name == "code":
                sb.Append('\n').Append(e.Value.Trim('\n')).Append('\n');
                break;
            case XElement e when e.Name is { LocalName: "see" or "seealso" }:
                var cref = (string?)e.Attribute("cref") ?? (string?)e.Attribute("langword") ?? (string?)e.Attribute("href");
                sb.Append(e.IsEmpty || e.Value.Length == 0 ? ShortCref(cref) : e.Value);
                break;
            case XElement e when e.Name is { LocalName: "paramref" or "typeparamref" }:
                sb.Append((string?)e.Attribute("name"));
                break;
            case XElement e when e.Name is { LocalName: "para" or "br" }:
                sb.Append("\n\n");
                foreach (var child in e.Nodes()) Append(child, sb);
                sb.Append("\n\n");
                break;
            case XElement e when e.Name == "item":
                sb.Append("\n- ");
                foreach (var child in e.Nodes()) Append(child, sb);
                break;
            case XElement e:
                foreach (var child in e.Nodes()) Append(child, sb);
                break;
        }
    }

    /// <summary><c>T:IdiotProof.Core.Trading.OrderIntent</c> → <c>OrderIntent</c>; <c>M:A.B.C(System.String)</c> → <c>B.C</c>.</summary>
    public static string ShortCref(string? cref)
    {
        if (string.IsNullOrEmpty(cref)) return "";
        var body = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        if (cref.StartsWith("T:", StringComparison.Ordinal) || !cref.Contains(':')) return LastPart(StripParameters(body));
        return DisplayName(cref);
    }

    /// <summary>The type name, or <c>Type.Member</c> for a member, without namespace, generic arity or parameters.</summary>
    public static string DisplayName(string memberId)
    {
        var body = StripParameters(memberId.Length > 2 && memberId[1] == ':' ? memberId[2..] : memberId);
        var parts = body.Split('.');
        var clean = parts.Select(p => Arity().Replace(p, "")).ToArray();
        if (memberId.StartsWith("T:", StringComparison.Ordinal) || clean.Length < 2) return clean[^1];
        var member = clean[^1] == "#ctor" ? "ctor" : clean[^1];
        return $"{clean[^2]}.{member}";
    }

    /// <summary>Drops a member id's parameter list: <c>A.B(System.String)</c> → <c>A.B</c>.</summary>
    public static string StripParameters(string id)
    {
        var paren = id.IndexOf('(');
        return paren < 0 ? id : id[..paren];
    }

    private static string LastPart(string dotted) => Arity().Replace(dotted[(dotted.LastIndexOf('.') + 1)..], "");

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex Blank();

    [GeneratedRegex(@"`\d+$")]
    private static partial Regex Arity();
}
