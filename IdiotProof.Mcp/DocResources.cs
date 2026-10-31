using System.Text.Json.Nodes;
using IdiotProof.Operations;
using IdiotProof.Operations.Docs;
using ModelContextProtocol.Server;

namespace IdiotProof.Mcp;

/// <summary>One document the MCP server offers as a resource.</summary>
/// <param name="Uri">The resource URI (<c>idiotproof://docs/bible</c>).</param>
/// <param name="Read">Produces the current text; never throws for a missing file (it says so instead).</param>
public sealed record DocResource(string Uri, string Name, string Title, string Description, string MimeType, Func<CancellationToken, Task<string>> Read);

/// <summary>
/// The MCP resources (IP-T122): the BIBLE, the user stories, the agent protocol and the live
/// operation catalog, so an MCP host can load the project's law and surface without a shell.
/// Docs are read from the repo checkout when the server runs inside one (always current), else
/// from the copies shipped next to the server (<c>docs/</c> in its output folder). The catalog
/// comes from the running Hub, falling back to the catalog built into the server.
/// </summary>
public static class DocResources
{
    public const string BibleUri = "idiotproof://docs/bible";
    public const string StoriesUri = "idiotproof://docs/user-stories";
    public const string ProtocolUri = "idiotproof://docs/protocol";
    public const string CatalogUri = "idiotproof://catalog";

    public static IReadOnlyList<DocResource> All(string? repoRoot, string shippedRoot, Func<CancellationToken, Task<JsonNode?>> liveCatalog, JsonNode offlineCatalog) =>
    [
        File(BibleUri, "bible", "IdiotProof BIBLE", "The project bible: what IdiotProof is, its architecture and the IP-LAWs every change must keep.",
            "docs/BIBLE.md", repoRoot, shippedRoot),
        File(StoriesUri, "user-stories", "IdiotProof user stories", "Every user story by epic with its status and the tests that prove it, plus the ticket log.",
            "docs/USER_STORIES.md", repoRoot, shippedRoot),
        File(ProtocolUri, "protocol", "IdiotProof agent protocol", "How an assistant drives IdiotProof: bootstrap, Hub-only, exit codes, Live confirmation etiquette, ET times.",
            "docs/agent/IDIOTPROOF_PROTOCOL.md", repoRoot, shippedRoot),
        new DocResource(CatalogUri, "operation-catalog", "IdiotProof operation catalog",
            "Every operation (= CLI command = MCP tool) with its parameters, risk and JSON input schema, from the running Hub when it is up.",
            "application/json",
            async ct => ((await liveCatalog(ct)) ?? offlineCatalog).ToJsonString(OperationJson.Indented)),
    ];

    /// <summary>The resources as MCP server resources.</summary>
    public static IEnumerable<McpServerResource> ToMcp(IEnumerable<DocResource> resources) =>
        resources.Select(r => McpServerResource.Create(r.Read, new McpServerResourceCreateOptions
        {
            UriTemplate = r.Uri,
            Name = r.Name,
            Title = r.Title,
            Description = r.Description,
            MimeType = r.MimeType,
        }));

    /// <summary>The checkout the server runs in (or <see cref="KnowledgeIndexBuilder.RepoEnvVar"/>), else null.</summary>
    public static string? FindRepoRoot() => KnowledgeIndexBuilder.FindRepoRoot(AppContext.BaseDirectory, Environment.CurrentDirectory);

    private static DocResource File(string uri, string name, string title, string description, string relative, string? repoRoot, string shippedRoot) =>
        new(uri, name, title, description, "text/markdown", async ct =>
        {
            foreach (var root in new[] { repoRoot, shippedRoot })
            {
                if (root is null) continue;
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(path)) return await System.IO.File.ReadAllTextAsync(path, ct);
            }
            return $"{relative} was not found. Run the MCP server from the IdiotProof checkout or set {KnowledgeIndexBuilder.RepoEnvVar}.";
        });
}
