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

    // Serializes first-load seeding and cache mutation. Without it, two
    // concurrent first reads for the same new user both see an empty store
    // and both persist their own "Default" tab (duplicate seeds).
    private readonly Lock gate = new();

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
        // Return a snapshot so callers can iterate safely while Save/Delete
        // mutate the underlying list under the gate lock.
        lock (gate)
        {
            if (tabsByUser.TryGetValue(userId, out var cached)) return cached.ToList();
        }
        LoadForUser(userId);
        lock (gate)
        {
            return tabsByUser.TryGetValue(userId, out var loaded) ? loaded.ToList() : [];
        }
    }

    public void LoadForUser(string userId)
    {
        lock (gate)
        {
            // Load under the lock: a concurrent first read that seeded this
            // user has already persisted its Default tab, so this re-load
            // sees it and does not seed a duplicate. (LoadForUser is also the
            // forced-refresh path for GetAllUsers, so no cached early-return.)
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
    }

    public void Save(string userId, WorkspaceTab tab)
    {
        // Hydrate the cache from the store BEFORE mutating it. GetOrAdd with an
        // empty list would poison the cache for a user whose tabs were never
        // loaded — subsequent reads would see only the just-saved tab and
        // silently hide the rest until process restart.
        GetTabsForUser(userId);

        store.Save(userId, tab);
        lock (gate)
        {
            var list = tabsByUser.GetOrAdd(userId, _ => []);
            var existing = list.FindIndex(t => t.TabId == tab.TabId);
            if (existing >= 0) list[existing] = tab;
            else               list.Add(tab);
        }
    }

    public WorkspaceTab Create(string userId, string name)
    {
        var tabs = GetTabsForUser(userId);
        var tab = new WorkspaceTab
        {
            Name = name,
            // tabs.Count would assign duplicate orders after any deletion.
            // Max+1 always produces a unique, monotonically increasing order.
            DisplayOrder = tabs.Count > 0 ? tabs.Max(t => t.DisplayOrder) + 1 : 0,
        };
        Save(userId, tab);
        return tab;
    }

    public bool Delete(string userId, string tabId)
    {
        var ok = store.Delete(userId, tabId);
        // Same gate as Save/LoadForUser — removing from the shared List while
        // another thread sorts/mutates it under the lock is a torn-state race.
        lock (gate)
        {
            if (tabsByUser.TryGetValue(userId, out var list))
            {
                var tab = list.FirstOrDefault(t => t.TabId == tabId);
                if (tab != null) list.Remove(tab);
            }
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
        lock (gate)
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
    }

    public void Save(WorkspaceTab tab) => Save(GlobalBucket, tab);
    public WorkspaceTab Create(string name) => Create(GlobalBucket, name);
    public bool Delete(string tabId) => Delete(GlobalBucket, tabId);
    public WorkspaceTab? Get(string tabId) => Get(GlobalBucket, tabId);
}
