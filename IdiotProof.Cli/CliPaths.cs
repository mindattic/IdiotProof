namespace IdiotProof.Cli;

/// <summary>
/// Where the CLI keeps its few files. Injectable so tests point everything at a temp folder.
/// </summary>
public sealed record CliPaths
{
    /// <summary><c>%APPDATA%\MindAttic\IdiotProof\cli.json</c> — the persisted default owner (IP-T63).</summary>
    public required string ConfigFile { get; init; }

    /// <summary><c>%LOCALAPPDATA%\MindAttic\IdiotProof\cli-catalog.json</c> — last catalog fetched from the Hub (IP-T60).</summary>
    public required string CatalogCache { get; init; }

    /// <summary><c>%LOCALAPPDATA%\MindAttic\IdiotProof\Logs</c> — where <c>hub start</c> writes the Hub's console log.</summary>
    public required string LogsDirectory { get; init; }

    /// <summary>The published Hub (<c>C:\Apps\IdiotProof\Hub\idiotproof-hub.exe</c>), preferred by <c>hub start</c> when present.</summary>
    public required string PublishedHubExe { get; init; }

    public static CliPaths Default { get; } = Create(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"C:\Apps\IdiotProof");

    /// <summary>Paths under the given roaming/local app-data roots and deploy folder.</summary>
    public static CliPaths Create(string roamingRoot, string localRoot, string deployRoot) => new()
    {
        ConfigFile = Path.Combine(roamingRoot, "MindAttic", "IdiotProof", "cli.json"),
        CatalogCache = Path.Combine(localRoot, "MindAttic", "IdiotProof", "cli-catalog.json"),
        LogsDirectory = Path.Combine(localRoot, "MindAttic", "IdiotProof", "Logs"),
        PublishedHubExe = Path.Combine(deployRoot, "Hub", "idiotproof-hub.exe"),
    };

    /// <summary>Everything under one folder (tests).</summary>
    public static CliPaths UnderRoot(string root) =>
        Create(Path.Combine(root, "roaming"), Path.Combine(root, "local"), Path.Combine(root, "deploy"));
}
