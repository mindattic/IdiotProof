namespace IdiotProof.Engine.Storage;

/// <summary>
/// Abstracts file paths for Web vs Desktop storage.
/// </summary>
public interface IStorageProvider
{
    string SettingsPath { get; }
    string WorkspacesPath { get; }
    string DataPath { get; }
    string LogsPath { get; }

    /// <summary>
    /// Ensures all required directories exist.
    /// </summary>
    void EnsureDirectories();
}

/// <summary>
/// Stores data in the application directory (for Blazor Server).
/// </summary>
public sealed class WebStorageProvider : IStorageProvider
{
    private readonly string basePath;

    public WebStorageProvider(string? basePath = null)
    {
        basePath = basePath ?? Path.Combine(AppContext.BaseDirectory, "AppData");
    }

    public string SettingsPath => Path.Combine(basePath, "Settings");
    public string WorkspacesPath => Path.Combine(basePath, "Workspaces");
    public string DataPath => Path.Combine(basePath, "Data");
    public string LogsPath => Path.Combine(basePath, "Logs");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(SettingsPath);
        Directory.CreateDirectory(WorkspacesPath);
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(LogsPath);
    }
}

/// <summary>
/// Stores data in %LOCALAPPDATA%\MindAttic (for MAUI Desktop).
/// </summary>
public sealed class DesktopStorageProvider : IStorageProvider
{
    private readonly string basePath;

    public DesktopStorageProvider()
    {
        basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MindAttic");
    }

    public string SettingsPath => Path.Combine(basePath, "Settings");
    public string WorkspacesPath => Path.Combine(basePath, "Workspaces");
    public string DataPath => Path.Combine(basePath, "Data");
    public string LogsPath => Path.Combine(basePath, "Logs");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(SettingsPath);
        Directory.CreateDirectory(WorkspacesPath);
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(LogsPath);
    }
}
