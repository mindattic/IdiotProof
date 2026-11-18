using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// The two helpers <see cref="StrategyJsonSchemaTests"/> needs (IP-T526), with no test-framework
/// dependency: a validator for the subset of JSON Schema that <c>StrategyJson.Schema()</c> uses,
/// and a generator that builds, from the schema alone, a document that uses every key of every
/// object and every condition type.
/// </summary>
public static class StrategyJsonSchemaSupport
{
    /// <summary>The symbol the generated document trades; also the string every string mutation uses.</summary>
    public const string Symbol = "NVDA";

    // ── validate ──

    /// <summary>
    /// True when <paramref name="value"/> satisfies <paramref name="schema"/>. Understands $ref (into
    /// the root's $defs), type (a name or a list; integer, number, string, boolean, array, object,
    /// null), const, enum, pattern, minimum, maximum, properties, required,
    /// additionalProperties: false, items, oneOf and anyOf. Any other keyword throws, so the
    /// schema cannot start using one this validator would silently ignore.
    /// </summary>
    public static bool Accepts(JsonObject root, JsonNode schema, JsonNode? value)
    {
        var s = schema.AsObject();
        foreach (var (keyword, constraint) in s)
        {
            switch (keyword)
            {
                case "$schema": case "$id": case "$comment": case "$defs": case "title": case "description": case "examples":
                    break;
                case "$ref":
                    if (!Accepts(root, Resolve(root, constraint!.GetValue<string>()), value)) return false;
                    break;
                case "type":
                    var types = constraint is JsonArray list ? list.Select(t => t!.GetValue<string>()).ToArray() : [constraint!.GetValue<string>()];
                    if (!types.Any(t => IsType(t, value))) return false;
                    break;
                case "const":
                    if (!JsonNode.DeepEquals(constraint, value)) return false;
                    break;
                case "enum":
                    if (!constraint!.AsArray().Any(option => JsonNode.DeepEquals(option, value))) return false;
                    break;
                case "pattern":
                    if (value is JsonValue text && text.TryGetValue<string>(out var str) && !Regex.IsMatch(str, constraint!.GetValue<string>())) return false;
                    break;
                case "minimum":
                    if (Number(value) is { } low && low < Number(constraint)) return false;
                    break;
                case "maximum":
                    if (Number(value) is { } high && high > Number(constraint)) return false;
                    break;
                case "properties":
                    if (value is JsonObject withProperties)
                        foreach (var (name, propertySchema) in constraint!.AsObject())
                            if (withProperties.TryGetPropertyValue(name, out var propertyValue) && !Accepts(root, propertySchema!, propertyValue)) return false;
                    break;
                case "required":
                    if (value is JsonObject withRequired && constraint!.AsArray().Any(name => !withRequired.ContainsKey(name!.GetValue<string>()))) return false;
                    break;
                case "additionalProperties":
                    if (constraint!.GetValue<bool>()) throw new NotSupportedException("additionalProperties: true");
                    var known = s["properties"]?.AsObject().Select(p => p.Key).ToHashSet() ?? [];
                    if (value is JsonObject closed && closed.Any(p => !known.Contains(p.Key))) return false;
                    break;
                case "items":
                    if (value is JsonArray items && items.Any(item => !Accepts(root, constraint!, item))) return false;
                    break;
                case "oneOf":
                    if (constraint!.AsArray().Count(option => Accepts(root, option!, value)) != 1) return false;
                    break;
                case "anyOf":
                    if (!constraint!.AsArray().Any(option => Accepts(root, option!, value))) return false;
                    break;
                default:
                    throw new NotSupportedException($"Schema keyword '{keyword}' is not understood by the test validator.");
            }
        }
        return true;
    }

    public static JsonObject Resolve(JsonObject root, string reference) =>
        reference.StartsWith("#/$defs/", StringComparison.Ordinal)
            ? root["$defs"]![reference["#/$defs/".Length..]]!.AsObject()
            : throw new NotSupportedException(reference);

    private static decimal? Number(JsonNode? value) =>
        value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number ? decimal.Parse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) : null;

    private static bool IsType(string type, JsonNode? value) => type switch
    {
        "null" => value is null,
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "string" => value is JsonValue s && s.GetValueKind() == System.Text.Json.JsonValueKind.String,
        "boolean" => value is JsonValue b && b.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
        "number" => Number(value) is not null,
        "integer" => Number(value) is { } n && n == decimal.Truncate(n),
        _ => throw new NotSupportedException("type " + type),
    };

    // ── generate ──

    /// <summary>
    /// A document built from the schema alone that sets every property of every object: each list of
    /// conditions holds one of every condition type, and the conditions nested inside and/or/not are
    /// the first type that nests nothing.
    /// </summary>
    public static JsonObject FullDocument(JsonObject root) => (JsonObject)Generate(root, root, nested: false)!;

    private static JsonNode? Generate(JsonObject root, JsonObject schema, bool nested)
    {
        if (schema["$ref"] is { } reference) return Generate(root, Resolve(root, reference.GetValue<string>()), nested);
        if (schema["const"] is { } constant) return constant.DeepClone();
        if (schema["examples"] is JsonArray examples) return examples[0]!.DeepClone(); // a value only an example can get right (a step reference)
        if (schema["enum"] is JsonArray options) return options.First(o => o is not null)!.DeepClone();
        if (schema["anyOf"] is JsonArray any) return Generate(root, any[0]!.AsObject(), nested);
        if (schema["oneOf"] is JsonArray alternatives)
        {
            // A single condition: the first type whose own properties hold no further condition.
            var leaf = alternatives.Select(a => a!.AsObject()).First(a => !Resolve(root, a["$ref"]!.GetValue<string>()).ToJsonString().Contains("#/$defs/condition\""));
            return Generate(root, leaf, nested: true);
        }

        var type = schema["type"] is JsonArray types ? types.Select(t => t!.GetValue<string>()).First(t => t != "null") : schema["type"]?.GetValue<string>();
        switch (type)
        {
            case "object":
                var result = new JsonObject();
                foreach (var (name, propertySchema) in schema["properties"]!.AsObject())
                    result[name] = Generate(root, propertySchema!.AsObject(), nested);
                return result;
            case "array":
                var items = schema["items"]!.AsObject();
                var target = items["$ref"] is { } itemRef ? Resolve(root, itemRef.GetValue<string>()) : items;
                if (target["oneOf"] is JsonArray each && !nested)
                    return new JsonArray([.. each.Select(a => Generate(root, a!.AsObject(), nested: true))]);
                return new JsonArray(Generate(root, items, nested));
            case "string":
                return schema["pattern"] is { } pattern && pattern.GetValue<string>().Contains(':') ? "09:30" : Symbol;
            case "boolean":
                return true;
            case "integer":
                return (long)Math.Max(1m, Number(schema["minimum"]) ?? 1m);
            case "number":
                return 1.5;
            default:
                throw new NotSupportedException("Cannot generate a value for " + schema.ToJsonString());
        }
    }

    // ── mutate ──

    /// <summary>
    /// Every single-change variant of a document: each property of each object removed, replaced by
    /// null, a string, a fraction, two whole numbers, a boolean, an empty array and an empty object,
    /// and one unknown key added to each object. Each comes with a label for the failure message.
    /// <paramref name="references"/> are keys whose string value refers to something else in the
    /// document (a route names a step): another string there is refused for a reason no schema can
    /// state, so that one replacement is left out for them.
    /// </summary>
    public static IEnumerable<(string Label, JsonObject Document)> Mutations(JsonObject document, IReadOnlySet<string> references)
    {
        JsonNode?[] replacements = [null, Symbol, 1.5, 7, 0, true, new JsonArray(), new JsonObject()];
        foreach (var path in ObjectPaths(document, []))
        {
            var where = "/" + string.Join('/', path);
            var names = ((JsonObject)Navigate(document, path)).Select(p => p.Key).ToList();
            foreach (var name in names)
            {
                var removed = (JsonObject)document.DeepClone();
                ((JsonObject)Navigate(removed, path)).Remove(name);
                yield return ($"{where}: '{name}' removed", removed);

                foreach (var replacement in replacements)
                {
                    if (replacement is JsonValue text && text.GetValueKind() == System.Text.Json.JsonValueKind.String && references.Contains(name)) continue;
                    var replaced = (JsonObject)document.DeepClone();
                    ((JsonObject)Navigate(replaced, path))[name] = replacement?.DeepClone();
                    yield return ($"{where}: '{name}' = {replacement?.ToJsonString() ?? "null"}", replaced);
                }
            }
            var extra = (JsonObject)document.DeepClone();
            ((JsonObject)Navigate(extra, path))["notAKey"] = 1;
            yield return ($"{where}: unknown key added", extra);
        }
    }

    private static IEnumerable<List<object>> ObjectPaths(JsonNode? node, List<object> path)
    {
        switch (node)
        {
            case JsonObject o:
                yield return path;
                foreach (var (name, child) in o)
                    foreach (var found in ObjectPaths(child, [.. path, name])) yield return found;
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                    foreach (var found in ObjectPaths(a[i], [.. path, i])) yield return found;
                break;
        }
    }

    private static JsonNode Navigate(JsonNode node, List<object> path)
    {
        foreach (var step in path) node = step is int index ? node[index]! : node[(string)step]!;
        return node;
    }
}
