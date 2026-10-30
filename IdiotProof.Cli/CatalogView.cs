using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Operations;

namespace IdiotProof.Cli;

/// <summary>One command as help sees it, whichever catalog it came from.</summary>
public sealed record CatalogEntry
{
    /// <summary><c>strategy list</c> (or a one-word client command such as <c>version</c>).</summary>
    public required string Name { get; init; }
    public required string Noun { get; init; }
    public required string Verb { get; init; }
    public required string Summary { get; init; }
    public required string Risk { get; init; }
    public required string Usage { get; init; }

    /// <summary>The full <c>--help</c> text.</summary>
    public required string Help { get; init; }

    /// <summary>True for commands the CLI runs itself (no Hub needed).</summary>
    public bool Local { get; init; }
}

/// <summary>
/// The catalog help is rendered from (IP-T60), built from either source behind one shape:
/// the Hub's <c>GET /api/catalog</c> JSON (so Hub-only operations show up), a cached copy of it,
/// or the operations reflected from IdiotProof.Operations itself when the Hub is down and nothing
/// is cached. Client-local commands are merged in and win over a Hub operation of the same name.
/// </summary>
public sealed class CatalogView
{
    private readonly Dictionary<string, CatalogEntry> byName;

    private CatalogView(IEnumerable<CatalogEntry> entries, string source, string? rawJson)
    {
        byName = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries) byName[e.Name] = e;
        Entries = [.. byName.Values.OrderBy(e => e.Name, StringComparer.Ordinal)];
        Source = source;
        RawJson = rawJson;
    }

    /// <summary>All commands, sorted by name.</summary>
    public IReadOnlyList<CatalogEntry> Entries { get; }

    /// <summary>Where the operations came from, for the help footer ("Hub at …", "cached …", "built-in …").</summary>
    public string Source { get; }

    /// <summary>The catalog JSON this view was built from (null for a reflected catalog).</summary>
    public string? RawJson { get; }

    /// <summary>Operations only (not the CLI's own commands).</summary>
    public int OperationCount => Entries.Count(e => !e.Local);

    /// <summary>Distinct nouns, sorted.</summary>
    public IReadOnlyList<string> Nouns => [.. Entries.Select(e => e.Noun).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)];

    /// <summary>A view over a reflected catalog (the offline source).</summary>
    public static CatalogView FromCatalog(OperationCatalog catalog, string source) =>
        new(catalog.Operations.Select(o => new CatalogEntry
        {
            Name = o.Name,
            Noun = o.Noun,
            Verb = o.Verb,
            Summary = o.Summary,
            Risk = o.Risk.ToString().ToLowerInvariant(),
            Usage = o.Usage,
            Help = o.Help,
        }), source, null);

    /// <summary>A view over catalog JSON (<c>/api/catalog</c> or its cached copy). Throws <see cref="JsonException"/> on junk.</summary>
    public static CatalogView FromJson(string json, string source)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Catalog JSON is not an object.");
        var operations = root["operations"] as JsonArray ?? throw new JsonException("Catalog JSON has no 'operations' array.");
        var entries = new List<CatalogEntry>();
        foreach (var node in operations.OfType<JsonObject>())
        {
            var name = Str(node, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            entries.Add(new CatalogEntry
            {
                Name = name,
                Noun = Str(node, "noun") ?? words[0],
                Verb = Str(node, "verb") ?? string.Join(' ', words.Skip(1)),
                Summary = Str(node, "summary") ?? "",
                Risk = Str(node, "risk") ?? "read",
                Usage = Str(node, "usage") ?? $"{OperationDescriptor.ProgramName} {name}",
                Help = HelpFromJson(node),
            });
        }
        return new CatalogView(entries, source, json);
    }

    /// <summary>A view over explicit entries (the CLI's own commands).</summary>
    public static CatalogView Of(IEnumerable<CatalogEntry> entries, string source) => new(entries, source, null);

    /// <summary>This view plus the given entries; an added entry replaces one of the same name.</summary>
    public CatalogView With(IEnumerable<CatalogEntry> extra) => new(Entries.Concat(extra), Source, RawJson);

    public CatalogEntry? Find(string name) =>
        byName.GetValueOrDefault(string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    public bool HasNoun(string noun) => Entries.Any(e => string.Equals(e.Noun, noun, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<CatalogEntry> ForNoun(string noun) =>
        [.. Entries.Where(e => string.Equals(e.Noun, noun, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The longest command name at the start of argv (leading words only; flags end the name).</summary>
    public bool TryResolve(IReadOnlyList<string> argv, out CatalogEntry? entry, out IReadOnlyList<string> rest)
    {
        var words = argv.TakeWhile(t => !t.StartsWith('-')).Take(4).ToList();
        for (var take = words.Count; take >= 1; take--)
        {
            if (Find(string.Join(' ', words.Take(take))) is { } found)
            {
                entry = found;
                rest = [.. argv.Skip(take)];
                return true;
            }
        }
        entry = null;
        rest = argv;
        return false;
    }

    /// <summary>The closest command name (or noun) to what was typed.</summary>
    public string? Suggest(string typed) =>
        OperationNames.Suggest(typed, Entries.Select(e => e.Name).Concat(Nouns).Distinct());

    /// <summary>
    /// Rebuilds an operation's <c>--help</c> text from its catalog JSON entry, in exactly the
    /// layout of <see cref="OperationDescriptor.Help"/> (a test holds the two identical).
    /// </summary>
    internal static string HelpFromJson(JsonObject node)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Str(node, "summary") ?? "");
        if (Str(node, "description") is { Length: > 0 } description && !string.IsNullOrWhiteSpace(description))
            sb.AppendLine().AppendLine(description);
        sb.AppendLine().Append("Usage: ").AppendLine(Str(node, "usage") ?? "");
        sb.Append("Risk: ").AppendLine(Str(node, "risk") ?? "read");

        var parameters = (node["parameters"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        if (parameters.Count > 0)
        {
            sb.AppendLine().AppendLine("Parameters:");
            var labels = parameters.Select(p =>
            {
                var pname = Str(p, "name") ?? "?";
                if (Str(p, "kind") == "argument") return $"<{pname}>";
                var shortName = Str(p, "short");
                var isFlag = Str(p, "type") == "bool";
                return (string.IsNullOrEmpty(shortName) ? "    " : $"-{shortName}, ") + $"--{pname}" + (isFlag ? "" : $" <{Str(p, "type")}>");
            }).ToList();
            var width = labels.Max(l => l.Length);
            for (var i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                var required = p["required"] is JsonValue r && r.TryGetValue<bool>(out var req) && req;
                var def = p["default"];
                var extra = required ? " (required)" : def is not null ? $" (default: {def.ToJsonString().Trim('"')})" : "";
                sb.Append("  ").Append(labels[i].PadRight(width)).Append("  ").Append(Str(p, "description")).AppendLine(extra);
            }
        }

        var examples = (node["examples"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        if (examples.Count > 0)
        {
            sb.AppendLine().AppendLine("Examples:");
            foreach (var e in examples)
            {
                sb.Append("  ").AppendLine(Str(e, "command_line"));
                if (Str(e, "description") is { } d && !string.IsNullOrWhiteSpace(d)) sb.Append("      ").AppendLine(d);
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string? Str(JsonObject node, string key) =>
        node[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
