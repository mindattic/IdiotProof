using System.Text.Json;
using IdiotProof.Engine.Storage;
using IdiotProof.Models;

namespace IdiotProof.Engine.Settings;

/// <summary>
/// Global application settings. Instance-based, registered in DI.
/// </summary>
public sealed class AppSettings
{
    // Connection
    public string IbkrHost { get; set; } = "127.0.0.1";
    public int IbkrLivePort { get; set; } = 4001;
    public int IbkrPaperPort { get; set; } = 4002;
    public int IbkrClientId { get; set; } = 99;
    public bool IbkrUsePaper { get; set; } = true;

    // Alpaca
    public string AlpacaApiKeyId { get; set; } = "";
    public string AlpacaApiSecretKey { get; set; } = "";
    public bool AlpacaIsPaper { get; set; } = true;

    // Polygon
    public string PolygonApiKey { get; set; } = "";

    // Defaults
    public string DefaultBroker { get; set; } = "Sandbox";
    public string DefaultDataFeed { get; set; } = "Polygon";
    public string Timezone { get; set; } = "Central Standard Time";

    // Display
    public bool ShowConnectionMessages { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppSettings Load(IStorageProvider storage)
    {
        storage.EnsureDirectories();
        var path = Path.Combine(storage.SettingsPath, "app-settings.json");
        if (!File.Exists(path)) return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(IStorageProvider storage)
    {
        storage.EnsureDirectories();
        var path = Path.Combine(storage.SettingsPath, "app-settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
