using System.Text.Json;

namespace IdiotProof.Operations;

/// <summary>
/// The Hub's HTTP contract, shared by the Hub and its clients (the <c>idiotproof</c> CLI and the
/// MCP server) so the two sides cannot drift. The Hub listens on loopback only; every mutating
/// endpoint requires <see cref="KeyHeader"/>.
/// </summary>
public static class HubWire
{
    public const int DefaultPort = 5910;
    public const string PortEnvVar = "IDIOTPROOF_HUB_PORT";
    public const string UrlEnvVar = "IDIOTPROOF_HUB_URL";
    public const string KeyHeader = "X-IdiotProof-Key";

    /// <summary>Vault Tokens bucket entry holding the shared key (%APPDATA%\MindAttic\Tokens\tokens.json).</summary>
    public const string KeyTokenName = "idiotproof-hub";
    public const string KeyEnvVar = "IDIOTPROOF_HUB_KEY";

    public const string HealthPath = "/api/health";
    public const string CatalogPath = "/api/catalog";
    public const string OperationPath = "/api/op";

    /// <summary>Key-guarded POST that stops the Hub gracefully (drains, stops the Monitor, releases the lease).</summary>
    public const string ShutdownPath = "/api/shutdown";

    public static int ResolvePort() =>
        int.TryParse(Environment.GetEnvironmentVariable(PortEnvVar), out var p) && p is > 0 and < 65536 ? p : DefaultPort;

    public static Uri ResolveBaseUrl(string? overrideUrl = null) =>
        new(overrideUrl ?? Environment.GetEnvironmentVariable(UrlEnvVar) ?? $"http://127.0.0.1:{ResolvePort()}/");
}

/// <summary>
/// One operation call. Either <see cref="Argv"/> (the CLI: operation name + its tokens, globals
/// already removed) or <see cref="Operation"/> + <see cref="Args"/> (the MCP server: a tool name
/// and its JSON arguments).
/// </summary>
public sealed record HubOperationRequest
{
    public IReadOnlyList<string>? Argv { get; init; }
    public string? Operation { get; init; }
    public JsonElement? Args { get; init; }
    public OperationCaller Caller { get; init; } = OperationCaller.Cli;
    public OutputMode Output { get; init; } = OutputMode.Human;
    public Guid? OwnerId { get; init; }
    public string? ConfirmationCode { get; init; }
    public bool Color { get; init; }
    public int? Width { get; init; }
}

/// <summary>What the Hub returns for every call: the result envelope plus the rendered text.</summary>
public sealed record HubOperationResponse
{
    public required string Operation { get; init; }
    public required string Status { get; init; }
    public required int ExitCode { get; init; }
    /// <summary>The text to print (human render, or the JSON envelope when Output = Json).</summary>
    public required string Text { get; init; }
    /// <summary>The structured envelope ({status, exit_code, message, data, confirmation}).</summary>
    public JsonElement? Envelope { get; init; }
}

/// <summary><c>GET /api/health</c>.</summary>
public sealed record HubHealth
{
    public required string Status { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset StartedEt { get; init; }
    public required bool MonitorHosted { get; init; }
    public required string Database { get; init; }
    public required int Operations { get; init; }
    public int ProcessId { get; init; }
}
