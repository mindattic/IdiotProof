using System.Text.Json;
using System.Text.Json.Nodes;

namespace IdiotProof.Cli;

/// <summary>Where the owner a call acts for came from.</summary>
public enum OwnerSource
{
    /// <summary><c>--user &lt;guid&gt;</c> on this command line.</summary>
    Flag,

    /// <summary><c>idiotproof users use &lt;guid&gt;</c>, saved in cli.json.</summary>
    Saved,

    /// <summary>Nothing sent: the Hub decides (its <c>IDIOTPROOF_OWNER</c>, else the lone user).</summary>
    HubDefault,
}

/// <summary>
/// The persisted default owner (IP-T63): <c>{"owner": "&lt;guid&gt;"}</c> in
/// <c>%APPDATA%\MindAttic\IdiotProof\cli.json</c>. Every Hub call sends
/// <c>--user</c> ?? saved owner ?? nothing. Client-side on purpose: if the Hub later grows a
/// <c>users use</c> operation, this local one still wins (the CLI handles the command before
/// forwarding), so a person's saved choice never depends on which Hub they reach.
/// </summary>
public sealed class OwnerStore(string configFile)
{
    public string ConfigFile { get; } = configFile;

    /// <summary>The saved owner, or null when none is saved (or the file is unreadable).</summary>
    public Guid? Load()
    {
        try
        {
            if (!File.Exists(ConfigFile)) return null;
            var node = JsonNode.Parse(File.ReadAllText(ConfigFile)) as JsonObject;
            return Guid.TryParse(node?["owner"]?.GetValue<string>(), out var owner) ? owner : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves (or, with null, removes) the default owner, keeping any other keys in the file.</summary>
    public void Save(Guid? owner)
    {
        JsonObject root;
        try
        {
            root = File.Exists(ConfigFile) ? JsonNode.Parse(File.ReadAllText(ConfigFile)) as JsonObject ?? [] : [];
        }
        catch (JsonException)
        {
            root = [];
        }
        if (owner is { } g) root["owner"] = g.ToString("D");
        else root.Remove("owner");

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
        File.WriteAllText(ConfigFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The owner to send and why: the flag, else the saved default, else none (Hub default).</summary>
    public (Guid? Owner, OwnerSource Source) Resolve(Guid? flag) =>
        flag is { } f ? (f, OwnerSource.Flag)
        : Load() is { } saved ? (saved, OwnerSource.Saved)
        : (null, OwnerSource.HubDefault);
}
