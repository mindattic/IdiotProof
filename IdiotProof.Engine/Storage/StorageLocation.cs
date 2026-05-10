namespace IdiotProof.Engine.Storage;

/// <summary>
/// Resolves the shared on-disk location for IdiotProof state (workspaces, settings, logs, db).
/// All processes — Blazor server, CLI runner, future supervisors — must point at the same
/// directory so a strategy authored in one is visible to the others.
///
/// Sits under the MindAttic family root, parallel to ThinkTank's
/// <c>%LOCALAPPDATA%\MindAttic\ThinkTank\</c>. Sensitive credentials live elsewhere
/// (Roaming) under <c>%APPDATA%\MindAttic\Brokers\</c> and <c>%APPDATA%\MindAttic\LLM\</c>.
///
/// Resolution order:
///   1. IDIOTPROOF_DATA_DIR environment variable (absolute path)
///   2. %LOCALAPPDATA%\MindAttic\IdiotProof on Windows
///   3. ~/.local/share/MindAttic/IdiotProof on other platforms
/// </summary>
public static class StorageLocation
{
    public const string EnvVarName = "IDIOTPROOF_DATA_DIR";

    public static string Resolve()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return Path.GetFullPath(fromEnv);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            return Path.Combine(localAppData, "MindAttic", "IdiotProof");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "MindAttic", "IdiotProof");
    }
}
