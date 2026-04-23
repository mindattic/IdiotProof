namespace IdiotProof.Engine.Storage;

/// <summary>
/// Resolves the shared on-disk location for IdiotProof state (workspaces, settings, logs, db).
/// All processes — Blazor server, CLI runner, future supervisors — must point at the same
/// directory so a strategy authored in one is visible to the others.
///
/// Resolution order:
///   1. IDIOTPROOF_DATA_DIR environment variable (absolute path)
///   2. %LOCALAPPDATA%\IdiotProof on Windows
///   3. ~/.idiotproof on other platforms
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
            return Path.Combine(localAppData, "IdiotProof");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".idiotproof");
    }
}
