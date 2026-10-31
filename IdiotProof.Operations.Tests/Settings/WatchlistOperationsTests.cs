using IdiotProof.Engine.Workspace;
using IdiotProof.Operations.Settings;
using IdiotProof.Operations.Tests.Keys;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Settings;

/// <summary>`watchlist list|add|remove` (IP-T252) over an in-memory workspace store.</summary>
public class WatchlistOperationsTests
{
    private KeysTestKit kit = null!;
    private MemoryWorkspaceStore store = null!;

    [SetUp]
    public void SetUp()
    {
        kit = new KeysTestKit();
        store = new MemoryWorkspaceStore();
        kit.Services.AddSingleton<IWorkspaceStore>(store);
    }

    [TearDown]
    public void TearDown() => kit.Dispose();

    private string Owner => KeysTestKit.Owner.ToString();

    [Test]
    public async Task Add_CreatesMain_Dedupes_AndListShowsIt()
    {
        var add = await new WatchlistAddOperation().ExecuteAsync(new WatchlistChangeArgs { Symbols = ["nvda", "AAPL,nvda"] }, kit.Context());
        var again = await new WatchlistAddOperation().ExecuteAsync(new WatchlistChangeArgs { Symbols = ["AAPL", "TSLA"] }, kit.Context());

        Assert.That(add.Human, Does.StartWith("Added NVDA AAPL to 'Main'."));
        Assert.That(again.Human, Does.Contain("Added TSLA").And.Contain("Already there: AAPL"));
        Assert.That(store.Load(Owner).Single().Watchlist, Is.EqualTo(new[] { "NVDA", "AAPL", "TSLA" }));

        var list = await new WatchlistListOperation().ExecuteAsync(new WatchlistListArgs(), kit.Context());
        Assert.That(list.Human, Does.Contain("Main").And.Contain("NVDA AAPL TSLA"));
    }

    [Test]
    public async Task Remove_TakesSymbolsOff_AndReportsMissingOnes()
    {
        store.Save(Owner, new WorkspaceTab { Name = "Main", Watchlist = ["NVDA", "AAPL"] });

        var result = await new WatchlistRemoveOperation().ExecuteAsync(new WatchlistChangeArgs { Symbols = ["aapl", "MSFT"] }, kit.Context());

        Assert.That(result.Human, Does.Contain("Removed AAPL").And.Contain("Not on it: MSFT"));
        Assert.That(store.Load(Owner).Single().Watchlist, Is.EqualTo(new[] { "NVDA" }));
    }

    [Test]
    public void SeveralWorkspaces_NeedAName_AndJunkSymbolsAreRefused()
    {
        store.Save(Owner, new WorkspaceTab { Name = "Main" });
        store.Save(Owner, new WorkspaceTab { Name = "Swing" });

        var ambiguous = Assert.ThrowsAsync<OperationException>(() =>
            new WatchlistAddOperation().ExecuteAsync(new WatchlistChangeArgs { Symbols = ["NVDA"] }, kit.Context()));
        var junk = Assert.ThrowsAsync<OperationException>(() =>
            new WatchlistAddOperation().ExecuteAsync(new WatchlistChangeArgs { Symbols = ["$$$"], Workspace = "Swing" }, kit.Context()));

        Assert.That(ambiguous!.Message, Does.Contain("--workspace").And.Contain("'Swing'"));
        Assert.That(junk!.Message, Does.Contain("not a ticker"));
    }

    [Test]
    public async Task OtherUsersWatchlists_AreInvisible()
    {
        store.Save(Guid.NewGuid().ToString(), new WorkspaceTab { Name = "Theirs", Watchlist = ["GME"] });

        var list = await new WatchlistListOperation().ExecuteAsync(new WatchlistListArgs(), kit.Context());

        Assert.That(list.Human, Does.Contain("empty"));
    }
}

/// <summary>An <see cref="IWorkspaceStore"/> in a dictionary, keyed by user then tab id.</summary>
internal sealed class MemoryWorkspaceStore : IWorkspaceStore
{
    private readonly Dictionary<string, Dictionary<string, WorkspaceTab>> tabs = [];

    public IReadOnlyList<WorkspaceTab> Load(string userId) =>
        tabs.TryGetValue(userId, out var mine) ? mine.Values.ToList() : [];

    public void Save(string userId, WorkspaceTab tab)
    {
        if (!tabs.TryGetValue(userId, out var mine)) tabs[userId] = mine = [];
        mine[tab.TabId] = tab;
    }

    public bool Delete(string userId, string tabId) => tabs.TryGetValue(userId, out var mine) && mine.Remove(tabId);

    public IEnumerable<string> EnumerateUserIds() => tabs.Keys;
}
