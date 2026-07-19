using IdiotProof.Engine.Workspace;

namespace IdiotProof.Engine.Tests;

/// <summary>
/// WorkspaceManager's cache layer must never hide store rows. Regressions
/// covered: Save() for a never-loaded user used to seed the cache with an
/// EMPTY list plus the just-saved tab, silently masking that user's other
/// persisted tabs until process restart; and concurrent first reads could
/// each persist their own "Default" seed.
/// </summary>
public class WorkspaceManagerTests
{
    private sealed class InMemoryStore : IWorkspaceStore
    {
        private readonly object sync = new();
        private readonly Dictionary<string, Dictionary<string, WorkspaceTab>> data = new(StringComparer.Ordinal);

        public int SaveCount;

        public IReadOnlyList<WorkspaceTab> Load(string userId)
        {
            lock (sync)
                return data.TryGetValue(userId, out var tabs) ? tabs.Values.ToList() : [];
        }

        public void Save(string userId, WorkspaceTab tab)
        {
            lock (sync)
            {
                Interlocked.Increment(ref SaveCount);
                if (!data.TryGetValue(userId, out var tabs))
                    data[userId] = tabs = new(StringComparer.Ordinal);
                tabs[tab.TabId] = tab;
            }
        }

        public bool Delete(string userId, string tabId)
        {
            lock (sync)
                return data.TryGetValue(userId, out var tabs) && tabs.Remove(tabId);
        }

        public IEnumerable<string> EnumerateUserIds()
        {
            lock (sync)
                return data.Keys.ToList();
        }
    }

    [Test]
    public void Save_ForNeverLoadedUser_DoesNotHideExistingStoreTabs()
    {
        var store = new InMemoryStore();
        store.Save("user-1", new WorkspaceTab { Name = "Alpha" });
        store.Save("user-1", new WorkspaceTab { Name = "Beta" });

        var manager = new WorkspaceManager(store);
        manager.Save("user-1", new WorkspaceTab { Name = "Gamma" });

        var tabs = manager.GetTabsForUser("user-1");
        Assert.That(tabs.Select(t => t.Name), Is.EquivalentTo(new[] { "Alpha", "Beta", "Gamma" }),
            "the cache must hydrate from the store before absorbing a save");
    }

    [Test]
    public void FirstRead_ForNewUser_SeedsExactlyOnePersistedDefaultTab()
    {
        var store = new InMemoryStore();
        var manager = new WorkspaceManager(store);

        var tabs = manager.GetTabsForUser("new-user");

        Assert.Multiple(() =>
        {
            Assert.That(tabs, Has.Count.EqualTo(1));
            Assert.That(tabs[0].Name, Is.EqualTo("Default"));
            Assert.That(store.Load("new-user"), Has.Count.EqualTo(1), "the seed must be persisted, not fabricated in memory");
        });
    }

    [Test]
    public void ConcurrentFirstReads_SeedOnlyOneDefaultTab()
    {
        var store = new InMemoryStore();
        var manager = new WorkspaceManager(store);

        Parallel.For(0, 8, _ => manager.GetTabsForUser("racy-user"));

        Assert.Multiple(() =>
        {
            Assert.That(store.Load("racy-user"), Has.Count.EqualTo(1), "exactly one Default seed may be persisted");
            Assert.That(manager.GetTabsForUser("racy-user"), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Save_UpdatesExistingTabInPlace()
    {
        var store = new InMemoryStore();
        var manager = new WorkspaceManager(store);
        var tab = manager.Create("user-2", "Scalps");

        tab.Name = "Scalps v2";
        manager.Save("user-2", tab);

        var tabs = manager.GetTabsForUser("user-2");
        Assert.That(tabs.Single(t => t.TabId == tab.TabId).Name, Is.EqualTo("Scalps v2"));
    }
}
