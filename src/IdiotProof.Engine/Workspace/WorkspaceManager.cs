using System.Text.Json;
using IdiotProof.Engine.Storage;

namespace IdiotProof.Engine.Workspace;

/// <summary>
/// Manages the lifecycle of all workspace tabs — load, save, create, delete.
/// </summary>
public sealed class WorkspaceManager
{
    private readonly IStorageProvider storage;
    private readonly List<WorkspaceTab> tabs = [];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<WorkspaceTab> Tabs => tabs;

    public WorkspaceManager(IStorageProvider storage)
    {
        this.storage = storage;
        storage.EnsureDirectories();
    }

    /// <summary>
    /// Load all workspace tabs from disk.
    /// </summary>
    public void LoadAll()
    {
        tabs.Clear();
        var dir = storage.WorkspacesPath;
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var tab = JsonSerializer.Deserialize<WorkspaceTab>(json, JsonOptions);
                if (tab != null) tabs.Add(tab);
            }
            catch { /* skip corrupt files */ }
        }

        tabs.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));

        // Create a default tab if none exist
        if (tabs.Count == 0)
        {
            var defaultTab = new WorkspaceTab
            {
                Name = "Default",
                DisplayOrder = 0,
                Strategies = [new StrategyBinding { StrategyName = "ITI" }]
            };
            tabs.Add(defaultTab);
            Save(defaultTab);
        }
    }

    /// <summary>
    /// Save a workspace tab to disk.
    /// </summary>
    public void Save(WorkspaceTab tab)
    {
        storage.EnsureDirectories();
        var path = Path.Combine(storage.WorkspacesPath, $"{tab.TabId}.json");
        var json = JsonSerializer.Serialize(tab, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Create a new workspace tab.
    /// </summary>
    public WorkspaceTab Create(string name)
    {
        var tab = new WorkspaceTab
        {
            Name = name,
            DisplayOrder = tabs.Count,
            Strategies = [new StrategyBinding { StrategyName = "ITI" }]
        };
        tabs.Add(tab);
        Save(tab);
        return tab;
    }

    /// <summary>
    /// Delete a workspace tab.
    /// </summary>
    public bool Delete(string tabId)
    {
        var tab = tabs.FirstOrDefault(t => t.TabId == tabId);
        if (tab == null) return false;

        tabs.Remove(tab);
        var path = Path.Combine(storage.WorkspacesPath, $"{tabId}.json");
        if (File.Exists(path)) File.Delete(path);
        return true;
    }

    /// <summary>
    /// Get a workspace tab by ID.
    /// </summary>
    public WorkspaceTab? Get(string tabId) => tabs.FirstOrDefault(t => t.TabId == tabId);
}
