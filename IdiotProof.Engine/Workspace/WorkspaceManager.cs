using System.Collections.Concurrent;
using System.Text.Json;
using IdiotProof.Engine.Storage;

namespace IdiotProof.Engine.Workspace;

/// <summary>
/// Manages workspace tabs per user. Each user's tabs live in Workspaces/{userId}/.
/// The legacy global API (Tabs, LoadAll, etc.) is retained for the CLI and system defaults.
/// </summary>
public sealed class WorkspaceManager
{
    private readonly IStorageProvider storage;
    private readonly ConcurrentDictionary<string, List<WorkspaceTab>> tabsByUser = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Legacy global access (used by CLI and StrategyExecutionService when iterating all users)
    public IReadOnlyList<WorkspaceTab> Tabs => GetTabsForUser("__global__");

    public WorkspaceManager(IStorageProvider storage)
    {
        this.storage = storage;
        storage.EnsureDirectories();
    }

    // ── Per-user API ────────────────────────────────────────────────────────────

    public IReadOnlyList<WorkspaceTab> GetTabsForUser(string userId)
    {
        if (tabsByUser.TryGetValue(userId, out var cached)) return cached;
        LoadForUser(userId);
        return tabsByUser.TryGetValue(userId, out var loaded) ? loaded : [];
    }

    public void LoadForUser(string userId)
    {
        var dir = UserDir(userId);
        var list = new List<WorkspaceTab>();

        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var tab = JsonSerializer.Deserialize<WorkspaceTab>(json, JsonOptions);
                    if (tab != null) list.Add(tab);
                }
                catch { /* skip corrupt files */ }
            }
        }

        list.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));

        if (list.Count == 0 && userId != "__global__")
        {
            var def = new WorkspaceTab { Name = "Default", DisplayOrder = 0, Strategies = [new StrategyBinding { StrategyName = "ITI" }] };
            list.Add(def);
            Save(userId, def);
        }

        tabsByUser[userId] = list;
    }

    public void Save(string userId, WorkspaceTab tab)
    {
        var dir = UserDir(userId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{tab.TabId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(tab, JsonOptions));

        var list = tabsByUser.GetOrAdd(userId, _ => []);
        var existing = list.FindIndex(t => t.TabId == tab.TabId);
        if (existing >= 0) list[existing] = tab; else list.Add(tab);
    }

    public WorkspaceTab Create(string userId, string name)
    {
        var tabs = tabsByUser.GetOrAdd(userId, _ => []);
        var tab = new WorkspaceTab { Name = name, DisplayOrder = tabs.Count, Strategies = [new StrategyBinding { StrategyName = "ITI" }] };
        Save(userId, tab);
        return tab;
    }

    public bool Delete(string userId, string tabId)
    {
        var dir = UserDir(userId);
        var path = Path.Combine(dir, $"{tabId}.json");
        if (File.Exists(path)) File.Delete(path);

        if (!tabsByUser.TryGetValue(userId, out var list)) return false;
        var tab = list.FirstOrDefault(t => t.TabId == tabId);
        if (tab == null) return false;
        list.Remove(tab);
        return true;
    }

    public WorkspaceTab? Get(string userId, string tabId) =>
        GetTabsForUser(userId).FirstOrDefault(t => t.TabId == tabId);

    /// <summary>Returns all (userId, tabs) pairs for the execution service to iterate.</summary>
    public IEnumerable<(string UserId, IReadOnlyList<WorkspaceTab> Tabs)> GetAllUsers()
    {
        // Refresh from disk for any user whose directory exists
        var workspacesRoot = storage.WorkspacesPath;
        if (!Directory.Exists(workspacesRoot)) yield break;

        foreach (var dir in Directory.GetDirectories(workspacesRoot))
        {
            var userId = Path.GetFileName(dir);
            if (userId == "__global__") continue;
            LoadForUser(userId);
            yield return (userId, GetTabsForUser(userId));
        }
    }

    // ── Legacy global API (for CLI / single-user fallback) ─────────────────────

    /// <summary>Loads global (non-user-scoped) workspaces from the root Workspaces directory.</summary>
    public void LoadAll()
    {
        var dir = storage.WorkspacesPath;
        var list = new List<WorkspaceTab>();
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var tab = JsonSerializer.Deserialize<WorkspaceTab>(json, JsonOptions);
                    if (tab != null) list.Add(tab);
                }
                catch { }
            }
        }
        list.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
        if (list.Count == 0)
        {
            var def = new WorkspaceTab { Name = "Default", DisplayOrder = 0, Strategies = [new StrategyBinding { StrategyName = "ITI" }] };
            list.Add(def);
            var path = Path.Combine(dir, $"{def.TabId}.json");
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(def, JsonOptions));
        }
        tabsByUser["__global__"] = list;
    }

    public void Save(WorkspaceTab tab) => Save("__global__", tab);
    public WorkspaceTab Create(string name) => Create("__global__", name);
    public bool Delete(string tabId) => Delete("__global__", tabId);
    public WorkspaceTab? Get(string tabId) => Get("__global__", tabId);

    private string UserDir(string userId) =>
        Path.Combine(storage.WorkspacesPath, userId);
}
