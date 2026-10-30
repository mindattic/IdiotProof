using System.Text.Json.Nodes;
using IdiotProof.Mcp;
using IdiotProof.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ── IdiotProof.Mcp ───────────────────────────────────────────────────────────
//
// stdio MCP server for Claude Code (and any MCP host). Every Hub operation is one tool named
// <noun>_<verb>; calls are forwarded to the running Hub (POST /api/op) with the shared key, so
// the MCP server holds no state, no database and no broker — the Hub does all of that.
//
// Register (once):   powershell -File tools\register-mcp.ps1
// Docs:              dotnet run --project IdiotProof.Mcp -- --export-tools [docs/MCP_TOOLS.md]
//
// stdout is the protocol wire; every log line goes to stderr.

var offlineCatalog = OperationCatalog.FromAssemblies(typeof(OperationCatalog).Assembly);

if (args.Length > 0 && args[0] == "--export-tools")
{
    var outPath = args.Length > 1 ? args[1] : Path.Combine("docs", "MCP_TOOLS.md");
    var tools = ToolSpec.FromCatalog(offlineCatalog.ToJsonNode());
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    File.WriteAllText(outPath, ToolDocs.Render(tools));
    Console.Error.WriteLine($"[export-tools] wrote {tools.Count} tools to {Path.GetFullPath(outPath)}");
    return 0;
}

var hub = new HubInvoker(HubInvoker.CreateClient(HubWire.ResolveBaseUrl()));

// The Hub's live catalog includes every operation it hosts; the reflected one is the fallback
// when the Hub is down at startup (calls then report hub_unreachable until it is started).
var catalogJson = await hub.TryGetCatalogAsync(CancellationToken.None) ?? offlineCatalog.ToJsonNode();
var specs = ToolSpec.FromCatalog(catalogJson);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(hub);
builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "idiotproof", Version = typeof(ToolSpec).Assembly.GetName().Version?.ToString() ?? "1.0.0" })
    .WithStdioServerTransport()
    .WithTools(specs.Select(s => (ModelContextProtocol.Server.McpServerTool)new HubTool(s, hub)));

await builder.Build().RunAsync();
return 0;
