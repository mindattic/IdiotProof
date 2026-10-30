using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Operations;

namespace IdiotProof.Mcp;

/// <summary>
/// One MCP tool, derived from one catalog operation (<c>OperationDescriptor.ToJson()</c>, whether it
/// came from the Hub's <c>/api/catalog</c> or the reflected offline catalog). Pure data so the
/// tool list, its schemas and the generated docs can be tested without a running server.
/// </summary>
public sealed record ToolSpec(
    string ToolName,
    string Operation,
    string Risk,
    string Description,
    JsonElement InputSchema)
{
    /// <summary>Extra input every Money/Destructive tool accepts: the code from an earlier preview.</summary>
    public const string ConfirmationProperty = "confirmation_code";

    public bool ReadOnly => Risk == "read";
    public bool Destructive => Risk is "money" or "destructive";
    public bool NeedsConfirmationInput => Destructive;

    public static IReadOnlyList<ToolSpec> FromCatalog(JsonNode catalog) =>
        (catalog["operations"]?.AsArray() ?? [])
            .Where(o => o is not null)
            .Select(o => FromOperation(o!))
            .OrderBy(t => t.ToolName, StringComparer.Ordinal)
            .ToList();

    public static ToolSpec FromOperation(JsonNode op)
    {
        var name = (string)op["name"]!;
        var risk = ((string?)op["risk"] ?? "read").ToLowerInvariant();
        var schema = op["input_schema"]?.DeepClone() as JsonObject ?? new JsonObject { ["type"] = "object" };

        if (risk is "money" or "destructive")
        {
            var props = schema["properties"] as JsonObject ?? (JsonObject)(schema["properties"] = new JsonObject());
            props[ConfirmationProperty] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Only after the user explicitly approves the preview: the code a previous call of this tool returned.",
            };
        }

        return new ToolSpec(
            (string)op["tool_name"]!,
            name,
            risk,
            BuildDescription(op, name, risk),
            JsonSerializer.SerializeToElement(schema));
    }

    private static string BuildDescription(JsonNode op, string name, string risk)
    {
        var sb = new StringBuilder();
        sb.Append((string?)op["summary"] ?? name);
        if ((string?)op["description"] is { Length: > 0 } detail) sb.Append("\n\n").Append(detail);
        sb.Append("\n\nCLI equivalent: ").Append((string?)op["usage"] ?? $"{OperationDescriptor.ProgramName} {name}");

        var examples = op["examples"]?.AsArray().Select(e => (string?)e?["command_line"]).Where(e => !string.IsNullOrEmpty(e)).Take(3).ToList();
        if (examples is { Count: > 0 }) sb.Append("\nExamples:\n  ").Append(string.Join("\n  ", examples));

        sb.Append(risk switch
        {
            "money" =>
                "\n\nMOVES MONEY. Sandbox and Paper run immediately. A Live action returns status confirmation-required with a " +
                "preview and a code instead of executing: show the user the preview verbatim, ask them, and only if they say yes " +
                $"call this tool again with the identical arguments plus {ConfirmationProperty}. Never invent or reuse a code.",
            "destructive" =>
                $"\n\nDESTRUCTIVE. If it returns confirmation-required, show the preview and call again with {ConfirmationProperty} only after the user agrees.",
            "write" => "\n\nChanges saved state.",
            _ => "\n\nRead-only.",
        });
        sb.Append(" Times are US Eastern.");
        return sb.ToString();
    }
}
