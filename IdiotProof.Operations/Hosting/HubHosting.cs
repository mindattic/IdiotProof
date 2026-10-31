namespace IdiotProof.Operations.Hosting;

/// <summary>
/// Host-level defaults the Hub decides before it builds (IP-T76/IP-T78), kept here so they are
/// testable without starting a Hub.
/// </summary>
public static class HubHosting
{
    /// <summary>The Windows service name the installer registers and the Hub reports.</summary>
    public const string ServiceName = "IdiotProof.Hub";

    /// <summary>MindAttic.Log application id: <c>%APPDATA%\MindAttic\Logging\IdiotProof.Hub\MindAttic.Log.db</c>.</summary>
    public const string LogApplication = "IdiotProof.Hub";

    /// <summary>
    /// The content root: the exe's folder when running as a service (the service control manager
    /// starts services in C:\Windows\System32, where appsettings.json and legion.json are not),
    /// else null to keep the default (the working directory, as with <c>dotnet run</c>).
    /// </summary>
    public static string? ContentRoot(bool isWindowsService, string baseDirectory) =>
        isWindowsService ? baseDirectory : null;

    /// <summary>
    /// Where the window mirror goes. An explicit <c>IDIOTPROOF_HUB_LOG</c> wins; a service has no
    /// window anyone sees, so it defaults to <c>…\Logs\hub-service.log</c> under the user's local
    /// app data (the service runs as the user); an interactive Hub with none set mirrors nothing.
    /// </summary>
    public static string? WindowLogPath(bool isWindowsService, string? configured, string localAppData) =>
        !string.IsNullOrWhiteSpace(configured) ? configured
        : isWindowsService ? Path.Combine(localAppData, "MindAttic", "IdiotProof", "Logs", "hub-service.log")
        : null;

    /// <summary>The Hub's MindAttic.Log database (<c>%APPDATA%\MindAttic\Logging\IdiotProof.Hub\MindAttic.Log.db</c>).</summary>
    public static string LogDatabasePath(string roamingAppData) =>
        Path.Combine(roamingAppData, "MindAttic", "Logging", LogApplication, "MindAttic.Log.db");
}
