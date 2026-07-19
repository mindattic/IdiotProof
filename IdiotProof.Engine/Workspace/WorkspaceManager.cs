using System.Collections.Concurrent;

namespace IdiotProof.Engine.Workspace;

/// <summary>
/// In-memory cache + default-seeding layer over an <see cref="IWorkspaceStore"/>.
/// All persistence I/O delegates to the store; this class adds:
///
///   â€¢ A per-user list cache so repeated reads don't hit disk/SQL.
///   â€¢ A "first read seeds a Default tab" rule so a new user lands on a working
///     workspace without an empty-state branch in every consumer.
///   â€¢ The legacy global-bucket API (<see cref="Tabs"/>, <see cref="LoadAll"/>)
///     used by the CLI and any single-user code path that hasn't been
///     user-scoped yet.
///
/// Constructed with the legacy <c>(IStorageProvider)</c> ctor for backward
/// compatibility â€” that path wraps the storage provider in a
/// <see cref="JsonFileWorkspaceStore"/> so existing callers keep their disk-based
/// behavior. New consumers should inject <see cref="IWorkspaceStore"/> directly
/// (the Blazor host registers a SQL-backed implementation).
/// </summary>
public sealed class WorkspaceManager
{
    private const string GlobalBucket = "__global__";

    private readonly IWorkspaceStore store;
    private readonly ConcurrentDictionary<string, List<WorkspaceTab>> tabsByUser = new(StringComparer.Ordinal);

    /// <summary>Legacy global-bucket accessor â€” used by the CLI and the standalone Engine path.</summary>
    public IReadOnlyList<WorkspaceTab> Tabs => GetTabsForUser(GlobalBucket);

    /// <summary>
    /// Preferred constructor. Used by Blazor (SQL-backed) + tests (in-memory store).
    /// </summary>
    public WorkspaceManager(IWorkspaceStore store)
    {
        this.store = store;
    }

    /// <summary>
    /// Backward-compat constructor for callers that still pass a storage provider
    /// (CLI, design-time, integration tests). Wraps the provider in a
    /// JsonFileWorkspaceStore so existing on-disk data continues to work.
    /// </summary>
    public WorkspaceManager(Storage.IStorageProvider storage)
    {
        storage.EnsureDirectories();
        store = new JsonFileWorkspaceStore(storage);
    }

    // â”€â”€ Per-user API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public IReadOnlyList<WorkspaceTab> GetTabsForUser(string userId)
    {
        if (tabsByUser.TryGetValue(userId, out var cached)) return cached;
        LoadForUser(userId);
        return tabsByUser.TryGetValue(userId, out var loaded) ? loaded : [];
    }

    public void LoadForUser(string userId)
    {
        var list = store.Load(userId).ToList();
        list.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));

        if (list.Count == 0 && userId != GlobalBucket)
        {
            var def = new WorkspaceTab
            {
                Name = "Default",
                DisplayOrder = 0,
            };
            list.Add(def);
            store.Save(userId, def);
        }

        tabsByUser[userId] = list;
    }

    public void Save(string userId, WorkspaceTab tab)
    {
        store.Save(userId, tab);
        var list = tabsByUser.GetOrAdd(userId, _ => []);
        var existing = list.FindIndex(t => t.TabId == tab.TabId);
        if (existing >= 0) list[existing] = tab;
        else               list.Add(tab);
    }

    public WorkspaceTab Create(string userId, string name)
    {
        var tabs = GetTabsForUser(userId);
        var tab = new WorkspaceTab
        {
            Name = name,
            DisplayOrder = tabs.Count,
        };
        Save(userId, tab);
        return tab;
    }

    public bool Delete(string userId, string tabId)
    {
        var ok = store.Delete(userId, tabId);
        if (tabsByUser.TryGetValue(userId, out var list))
        {
            var tab = list.FirstOrDefault(t => t.TabId == tabId);
            if (tab != null) list.Remove(tab);
        }
        return ok;
    }

    public WorkspaceTab? Get(string userId, string tabId) =>
        GetTabsForUser(userId).FirstOrDefault(t => t.TabId == tabId);

    /// <summary>
    /// Returns every (userId, tabs) pair the underlying store knows about.
    /// Refreshes the cache for each user as it iterates so consumers never
    /// see a stale list.
    /// </summary>
    public IEnumerable<(string UserId, IReadOnlyList<WorkspaceTab> Tabs)> GetAllUsers()
    {
        foreach (var userId in store.EnumerateUserIds())
        {
            LoadForUser(userId);
            yield return (userId, GetTabsForUser(userId));
        }
    }

    // â”€â”€ Legacy global-bucket API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Loads the legacy global bucket. Single-user / CLI fallback.</summary>
    public void LoadAll()
    {
        var list = store.Load(GlobalBucket).ToList();
        list.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));

        if (list.Count == 0)
        {
            var def = new WorkspaceTab
            {
                Name = "Default",
                DisplayOrder = 0,
            };
            list.Add(def);
            store.Save(GlobalBucket, def);
        }

        tabsByUser[GlobalBucket] = list;
    }

    public void Save(WorkspaceTab tab) => Save(GlobalBucket, tab);
    public WorkspaceTab Create(string name) => Create(GlobalBucket, name);
    public bool Delete(string tabId) => Delete(GlobalBucket, tabId);
    public WorkspaceTab? Get(string tabId) => Get(GlobalBucket, tabId);
}
