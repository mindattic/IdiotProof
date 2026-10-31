using System.Net;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Reference;

namespace IdiotProof.Operations.Tests.Reference;

/// <summary>watchlists list|show|create|rename|add|remove|delete (IP-T314) over a scripted /v2/watchlists.</summary>
public class WatchlistOperationsTests
{
    private const string Id = "3174d6df-7726-44b4-a5bd-7fda5ae6e009";

    private const string Tech = $$"""
        {"id":"{{Id}}","name":"tech","created_at":"2026-10-01T14:00:00Z","updated_at":"2026-10-30T13:31:00Z",
         "assets":[{"symbol":"AAPL","name":"Apple Inc.","exchange":"NASDAQ","class":"us_equity","tradable":true},
                   {"symbol":"MSFT","name":"Microsoft","exchange":"NASDAQ","class":"us_equity","tradable":true}]}
        """;

    [Test]
    public async Task List_ShowsNamesAndEtTimes()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/watchlists", $$"""[{"id":"{{Id}}","name":"tech","created_at":"2026-10-01T14:00:00Z","updated_at":"2026-10-30T13:31:00Z"}]""");

        var result = await new WatchlistsListOperation().ExecuteAsync(new WatchlistsListArgs(), ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("tech").And.Contain(Id).And.Contain("9:31 AM ET"));
    }

    [Test]
    public async Task Show_ByName_UsesByNameEndpoint_ById_UsesIdPath()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/watchlists", Tech);
        var ctx = ReferenceTestKit.Context(alpaca, out var factory);

        var byName = await new WatchlistsShowOperation().ExecuteAsync(new WatchlistRefArgs { Watchlist = "tech" }, ctx);
        var byId = await new WatchlistsShowOperation().ExecuteAsync(new WatchlistRefArgs { Watchlist = Id, Mode = AlpacaAccount.Live }, ctx);

        Assert.That(byName.IsOk && byId.IsOk, Is.True);
        Assert.That(alpaca.Requests[0].Uri.PathAndQuery, Is.EqualTo("/v2/watchlists:by_name?name=tech"));
        Assert.That(alpaca.Requests[1].Uri.AbsoluteUri, Is.EqualTo($"https://api.alpaca.markets/v2/watchlists/{Id}"));
        Assert.That(factory.Modes, Is.EqualTo(new[] { BrokerMode.Paper, BrokerMode.Live }));
        Assert.That(byName.Human, Does.Contain("AAPL").And.Contain("Microsoft"));
    }

    [Test]
    public async Task Show_UnknownName_Fails()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/watchlists", """{"code":40410000,"message":"watchlist not found"}""", HttpStatusCode.NotFound);

        var result = await new OperationDispatcherShim().RunAsync(() =>
            new WatchlistsShowOperation().ExecuteAsync(new WatchlistRefArgs { Watchlist = "nope" }, ReferenceTestKit.Context(alpaca)));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("'nope'").And.Contain("watchlist not found"));
    }

    [Test]
    public async Task Create_PostsNameAndNormalizedSymbols()
    {
        var alpaca = new RoutedAlpaca().On("POST", "/v2/watchlists", Tech);

        var result = await new WatchlistsCreateOperation().ExecuteAsync(new WatchlistsCreateArgs { Name = "tech", Symbols = ["aapl,msft", "AAPL"] }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Requests.Single().Body, Is.EqualTo("""{"name":"tech","symbols":["AAPL","MSFT"]}"""));
    }

    [Test]
    public async Task Rename_PutsTheNewNameWithTheExistingSymbols()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/watchlists", Tech).On("PUT", "/v2/watchlists/", Tech.Replace("\"tech\"", "\"megacaps\""));

        var result = await new WatchlistsRenameOperation().ExecuteAsync(new WatchlistsRenameArgs { Watchlist = "tech", NewName = "megacaps" }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        var put = alpaca.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.That(put.Uri.AbsolutePath, Is.EqualTo($"/v2/watchlists/{Id}"));
        Assert.That(put.Body, Is.EqualTo("""{"name":"megacaps","symbols":["AAPL","MSFT"]}"""));
        Assert.That(result.Human, Does.Contain("Renamed 'tech' to 'megacaps'"));
    }

    [Test]
    public async Task Add_PostsOnlyMissingSymbols()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/watchlists", Tech).On("POST", "/v2/watchlists/", Tech);

        var result = await new WatchlistsAddOperation().ExecuteAsync(new WatchlistSymbolsArgs { Watchlist = "tech", Symbols = ["amd", "AAPL"] }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        var post = alpaca.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.That(post.Uri.AbsolutePath, Is.EqualTo($"/v2/watchlists/{Id}"));
        Assert.That(post.Body, Is.EqualTo("""{"symbol":"AMD"}"""));
        Assert.That(result.Human, Does.Contain("Added AMD (already there: AAPL)."));
    }

    [Test]
    public async Task Remove_DeletesEachPresentSymbol()
    {
        var after = Tech.Replace("""{"symbol":"AAPL","name":"Apple Inc.","exchange":"NASDAQ","class":"us_equity","tradable":true},""", "");
        var alpaca = new RoutedAlpaca().On("GET", "/v2/watchlists", Tech).On("DELETE", "/v2/watchlists/", after);

        var result = await new WatchlistsRemoveOperation().ExecuteAsync(new WatchlistSymbolsArgs { Watchlist = "tech", Symbols = ["AAPL", "TSLA"] }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Requests.Single(r => r.Method == HttpMethod.Delete).Uri.AbsolutePath, Is.EqualTo($"/v2/watchlists/{Id}/AAPL"));
        Assert.That(result.Human, Does.Contain("Removed AAPL (not on the list: TSLA)."));
    }

    [Test]
    public async Task Delete_ResolvesByNameThenDeletesById()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/watchlists", Tech).On("DELETE", "/v2/watchlists/", "", HttpStatusCode.NoContent);

        var result = await new WatchlistsDeleteOperation().ExecuteAsync(new WatchlistRefArgs { Watchlist = "tech" }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Requests.Single(r => r.Method == HttpMethod.Delete).Uri.AbsolutePath, Is.EqualTo($"/v2/watchlists/{Id}"));
        Assert.That(result.Human, Does.Contain("Deleted Alpaca watchlist 'tech' (2 symbols: AAPL, MSFT)."));
    }

    /// <summary>What the dispatcher does with an <see cref="OperationException"/> thrown from a handler.</summary>
    private sealed class OperationDispatcherShim
    {
        public async Task<OperationResult> RunAsync(Func<Task<OperationResult>> run)
        {
            try { return await run(); }
            catch (OperationException ex) { return ex.ToResult(); }
        }
    }
}
