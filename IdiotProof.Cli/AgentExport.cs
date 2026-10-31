using IdiotProof.Mcp;
using IdiotProof.Operations;

namespace IdiotProof.Cli;

/// <summary>One generated file: where it went, how many operations it covers, and whether it changed.</summary>
public sealed record ExportedFile(string RelativePath, string FullPath, int Operations, bool Changed);

/// <summary>
/// <c>idiotproof agent export</c> (IP-T272): regenerates the three agent-facing references from
/// the operation catalog built into the CLI, so they can never drift from the code:
/// <c>docs/CLI_COMMANDS.md</c> (every command's help), <c>docs/MCP_TOOLS.md</c> (every MCP tool
/// with its input schema, rendered by the MCP server's own <see cref="ToolDocs"/>) and
/// <c>docs/agent/operation-catalog.json</c> (the catalog as <c>/api/catalog</c> serves it).
/// </summary>
public static class AgentExport
{
    public const string CommandsFile = "docs/CLI_COMMANDS.md";
    public const string ToolsFile = "docs/MCP_TOOLS.md";
    public const string CatalogFile = "docs/agent/operation-catalog.json";

    /// <summary>The three files' contents, keyed by repo-relative path (LF line endings).</summary>
    public static IReadOnlyDictionary<string, (string Text, int Operations)> Render(OperationCatalog catalog)
    {
        var tools = ToolSpec.FromCatalog(catalog.ToJsonNode());
        return new Dictionary<string, (string, int)>
        {
            [CommandsFile] = (CommandDocs.Render(catalog), catalog.Operations.Count),
            [ToolsFile] = (ToolDocs.Render(tools), tools.Count),
            [CatalogFile] = (catalog.ToJson(indented: true).ReplaceLineEndings("\n") + "\n", catalog.Operations.Count),
        };
    }

    /// <summary>
    /// Writes the files under <paramref name="root"/> (only those whose text changed unless
    /// <paramref name="checkOnly"/>, which writes nothing and just reports what is stale).
    /// </summary>
    public static IReadOnlyList<ExportedFile> Write(OperationCatalog catalog, string root, bool checkOnly = false)
    {
        var results = new List<ExportedFile>();
        foreach (var (relative, (text, count)) in Render(catalog))
        {
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            var existing = File.Exists(full) ? File.ReadAllText(full).ReplaceLineEndings("\n") : null;
            var changed = existing != text;
            if (changed && !checkOnly)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
            }
            results.Add(new ExportedFile(relative, full, count, changed));
        }
        return results;
    }
}
