using IdiotProof.Engine.Storage;
using IdiotProof.Engine.Workspace;

namespace IdiotProof.Engine.Tests;

/// <summary>
/// IP-T14: the on-disk <see cref="JsonFileWorkspaceStore"/> in a temp <c>WorkspacesPath</c>: save/load
/// round trip, DisplayOrder sort, corrupt files skipped, Delete of a missing tab, and the
/// <c>__global__</c> bucket reading flat files at the root and staying out of <c>EnumerateUserIds</c>.
/// </summary>
[TestFixture]
public sealed class JsonFileWorkspaceStoreTests
{
    private string root = null!;
    private WebStorageProvider storage = null!;
    private JsonFileWorkspaceStore store = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), $"ip-ws-{Guid.NewGuid():N}");
        storage = new WebStorageProvider(root);
        store = new JsonFileWorkspaceStore(storage);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
    }

    private static WorkspaceTab Tab(string id, int order, string name = "") => new()
    {
        TabId = id, Name = name == "" ? id : name, DisplayOrder = order,
        CreatedUtc = new DateTime(2026, 10, 1, 13, 30, 0, DateTimeKind.Utc),
    };

    [Test]
    public void Load_UnknownUserOrMissingRoot_IsEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(store.Load("nobody"), Is.Empty);
            Assert.That(store.EnumerateUserIds(), Is.Empty);
        });
    }

    [Test]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var tab = Tab("abc", 2, "Gappers");
        tab.Watchlist = ["AAPL", "NVDA"];
        tab.BrokerOverride = "Sandbox";
        tab.DataFeedOverride = "Mock";
        store.Save("user-1", tab);

        var loaded = store.Load("user-1").Single();
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(storage.WorkspacesPath, "user-1", "abc.json")), Is.True, "Workspaces/{user}/{tab}.json layout");
            Assert.That(loaded.TabId, Is.EqualTo("abc"));
            Assert.That(loaded.Name, Is.EqualTo("Gappers"));
            Assert.That(loaded.DisplayOrder, Is.EqualTo(2));
            Assert.That(loaded.CreatedUtc, Is.EqualTo(tab.CreatedUtc));
            Assert.That(loaded.Watchlist, Is.EqualTo(new[] { "AAPL", "NVDA" }));
            Assert.That(loaded.BrokerOverride, Is.EqualTo("Sandbox"));
            Assert.That(loaded.DataFeedOverride, Is.EqualTo("Mock"));
        });
    }

    [Test]
    public void Save_SameTabIdOverwrites()
    {
        store.Save("u", Tab("t1", 0, "old"));
        store.Save("u", Tab("t1", 0, "new"));
        Assert.That(store.Load("u").Single().Name, Is.EqualTo("new"));
    }

    [Test]
    public void Load_SortsByDisplayOrder()
    {
        store.Save("u", Tab("c", 3));
        store.Save("u", Tab("a", 1));
        store.Save("u", Tab("b", 2));
        store.Save("u", Tab("z", 0));
        Assert.That(store.Load("u").Select(t => t.TabId), Is.EqualTo(new[] { "z", "a", "b", "c" }));
    }

    [Test]
    public void Load_SkipsCorruptFiles_AndNonJsonFiles()
    {
        store.Save("u", Tab("good", 0));
        var dir = Path.Combine(storage.WorkspacesPath, "u");
        File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(dir, "null.json"), "null");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "{}");

        Assert.That(store.Load("u").Select(t => t.TabId), Is.EqualTo(new[] { "good" }));
    }

    [Test]
    public void Delete_RemovesTheFile_AndReturnsFalseWhenMissing()
    {
        store.Save("u", Tab("t1", 0));
        store.Save("u", Tab("t2", 1));
        Assert.Multiple(() =>
        {
            Assert.That(store.Delete("u", "t1"), Is.True);
            Assert.That(store.Delete("u", "t1"), Is.False, "already gone");
            Assert.That(store.Delete("u", "never"), Is.False);
            Assert.That(store.Delete("nobody", "t2"), Is.False, "another user's tab id");
            Assert.That(store.Load("u").Select(t => t.TabId), Is.EqualTo(new[] { "t2" }));
        });
    }

    [Test]
    public void UsersAreIsolated()
    {
        store.Save("alice", Tab("a1", 0));
        store.Save("bob", Tab("b1", 0));
        Assert.Multiple(() =>
        {
            Assert.That(store.Load("alice").Select(t => t.TabId), Is.EqualTo(new[] { "a1" }));
            Assert.That(store.Load("bob").Select(t => t.TabId), Is.EqualTo(new[] { "b1" }));
        });
    }

    [Test]
    public void GlobalBucket_ReadsAndWritesFlatFilesAtTheRoot_AndIsNotAUserId()
    {
        store.Save("__global__", Tab("g1", 1));
        store.Save("__global__", Tab("g0", 0));
        store.Save("alice", Tab("a1", 0));
        // A stray "__global__" folder (e.g. from an old build) must not surface as a user either.
        Directory.CreateDirectory(Path.Combine(storage.WorkspacesPath, "__global__"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(storage.WorkspacesPath, "g1.json")), Is.True, "flat at the root");
            Assert.That(store.Load("__global__").Select(t => t.TabId), Is.EqualTo(new[] { "g0", "g1" }),
                "root files only, not the users' subfolders");
            Assert.That(store.EnumerateUserIds(), Is.EquivalentTo(new[] { "alice" }));
            Assert.That(store.Delete("__global__", "g1"), Is.True);
            Assert.That(store.Load("__global__").Select(t => t.TabId), Is.EqualTo(new[] { "g0" }));
        });
    }

    [Test]
    public void EnumerateUserIds_ListsEveryUserFolder()
    {
        store.Save("alice", Tab("a", 0));
        store.Save("bob", Tab("b", 0));
        store.Save("3f2c1d7e-0000-0000-0000-000000000001", Tab("c", 0));
        Assert.That(store.EnumerateUserIds(), Is.EquivalentTo(new[] { "alice", "bob", "3f2c1d7e-0000-0000-0000-000000000001" }));
    }
}
