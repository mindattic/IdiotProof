using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using IdiotProof.Engine.Settings;

namespace IdiotProof.Operations.Docs;

public sealed partial class KnowledgeIndexBuilder
{
    /// <summary>The static catalogs (IP-LAW-7) as they sit in the checkout.</summary>
    public const string GapperProfilesFile = "IdiotProof.Core/Catalogs/gapper-profiles.json";

    public const string IndexEventsFile = "IdiotProof.Core/Catalogs/sp-index-events.json";

    public const string LegionFile = "legion.json";

    /// <summary>Settings whose names look like credentials: listed, never valued.</summary>
    private static readonly string[] SecretWords = ["Key", "Secret", "Password", "Token", "Hash", "Connection"];

    /// <summary>IP-T265: gapper profiles, S&amp;P index events, the Legion voter panel and AppSettings' settings.</summary>
    internal void IndexCatalogs(KnowledgeIndex index)
    {
        IndexGapperProfiles(index);
        IndexIndexEvents(index);
        IndexLegion(index);
        IndexSettings(index);
    }

    private void IndexGapperProfiles(KnowledgeIndex index)
    {
        if (ReadJson(index, GapperProfilesFile) is not { } doc) return;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles)) return;
            var text = ReadText(index, GapperProfilesFile, optional: true) ?? "";
            foreach (var p in profiles.EnumerateArray())
            {
                var id = Str(p, "id");
                var body = new StringBuilder(Str(p, "description"));
                body.Append('\n');
                foreach (var prop in p.EnumerateObject().Where(x => x.Name is not ("id" or "name" or "description")))
                    body.Append(prop.Name).Append(": ").Append(prop.Value.ValueKind == JsonValueKind.Null ? "—" : prop.Value.ToString()).Append("; ");
                index.Add(new KnowledgeEntry(KnowledgeKind.Catalog, "gapper-profile:" + id, $"Gapper profile {Str(p, "name")} ({id})",
                    body.ToString().TrimEnd(' ', ';'), GapperProfilesFile, LineOf(text, $"\"{id}\"")));
            }
        }
    }

    private void IndexIndexEvents(KnowledgeIndex index)
    {
        if (ReadJson(index, IndexEventsFile) is not { } doc) return;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("events", out var events)) return;
            var text = ReadText(index, IndexEventsFile, optional: true) ?? "";
            foreach (var e in events.EnumerateArray())
            {
                var ticker = Str(e, "ticker");
                var idx = Str(e, "index");
                var action = Str(e, "action");
                var effective = Str(e, "effectiveDate") is { Length: > 0 } d ? d : "TBA";
                var body = $"{ticker} {(action == "Remove" ? "leaves" : "joins")} {idx}: announced {Str(e, "announcedDate")}, effective {effective}. {Str(e, "note")}".Trim();
                index.Add(new KnowledgeEntry(KnowledgeKind.Catalog, $"index-event:{ticker}-{idx}-{action}".ToLowerInvariant(),
                    $"Index event {ticker} {action} {idx}", body, IndexEventsFile, LineOf(text, $"\"ticker\": \"{ticker}\"")));
            }
        }
    }

    private void IndexLegion(KnowledgeIndex index)
    {
        if (ReadJson(index, LegionFile) is not { } doc) return;
        using (doc)
        {
            var root = doc.RootElement;
            var voters = root.TryGetProperty("voters", out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                : [];
            var body = $"Voters: {string.Join(", ", voters)}. Judge: {Str(root, "judge")}. Tier: {Str(root, "tier")}.\n{Str(root, "_comment")}".Trim();
            index.Add(new KnowledgeEntry(KnowledgeKind.Catalog, "legion:panel", "Legion LLM voter panel", body, LegionFile, 1));
            foreach (var voter in voters)
            {
                index.Add(new KnowledgeEntry(KnowledgeKind.Catalog, "legion:voter:" + voter, $"Legion voter {voter}",
                    $"{voter} votes on every automated entry (IP-LAW-1){(voter == Str(root, "judge") ? " and judges the panel" : "")}.", LegionFile, 1)
                {
                    Parent = "legion:panel",
                });
            }
        }
    }

    /// <summary>Every settable <see cref="AppSettings"/> property with its type, default and doc comment.</summary>
    private static void IndexSettings(KnowledgeIndex index)
    {
        AppSettings defaults;
        try { defaults = new AppSettings(); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            index.Warn($"AppSettings could not be constructed for the settings catalog: {e.Message}");
            return;
        }
        foreach (var p in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite))
        {
            var secret = SecretWords.Any(w => p.Name.Contains(w, StringComparison.Ordinal));
            string shown;
            if (secret) shown = "(credential — never shown)";
            else
            {
                object? value;
                try { value = p.GetValue(defaults); }
                catch (TargetInvocationException) { value = null; }
                shown = value switch
                {
                    null => "—",
                    string s => s.Length == 0 ? "(empty)" : $"\"{s}\"",
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    System.Collections.IEnumerable => "(list)",
                    _ => value.ToString() ?? "—",
                };
            }
            var doc = index.Get($"P:{typeof(AppSettings).FullName}.{p.Name}")?.Text;
            var at = index.Get($"P:{typeof(AppSettings).FullName}.{p.Name}") is { Line: > 0 } sym ? sym : null;
            var body = $"Type: {FriendlyType(p.PropertyType)} · Default: {shown}{(string.IsNullOrEmpty(doc) ? "" : "\n" + doc)}";
            index.Add(new KnowledgeEntry(KnowledgeKind.Catalog, "setting:" + p.Name, $"Setting {p.Name}", body,
                at?.Source ?? "IdiotProof.Engine/Settings/AppSettings.cs", at?.Line ?? 0));
        }
    }

    private JsonDocument? ReadJson(KnowledgeIndex index, string relative)
    {
        if (ReadText(index, relative) is not { } text) return null;
        try { return JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e)
        {
            index.Warn($"{relative} is not valid JSON: {e.Message}");
            return null;
        }
    }

    private static int LineOf(string text, string needle)
    {
        var at = text.IndexOf(needle, StringComparison.Ordinal);
        return at < 0 ? 0 : text.AsSpan(0, at).Count('\n') + 1;
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string FriendlyType(Type t) =>
        Nullable.GetUnderlyingType(t) is { } inner ? FriendlyType(inner) + "?" :
        t == typeof(string) ? "string" : t == typeof(bool) ? "bool" : t == typeof(int) ? "int" :
        t == typeof(decimal) ? "decimal" : t == typeof(double) ? "double" : t.Name;
}
