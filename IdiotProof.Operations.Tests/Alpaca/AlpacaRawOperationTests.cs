using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Alpaca;
using IdiotProof.Operations.Tests.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Alpaca;

/// <summary>alpaca raw (IP-T327): any endpoint through the owner's client; money-path mutations refused; Live mutations need a code.</summary>
public class AlpacaRawOperationTests
{
    private ServiceProvider db = null!;

    [OneTimeSetUp]
    public void CreateDb() => db = new ServiceCollection()
        .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"alpaca-raw-{Guid.NewGuid():N}"))
        .BuildServiceProvider();

    [OneTimeTearDown]
    public void DisposeDb() => db.Dispose();

    private void WithStore(IServiceCollection s) =>
        s.AddSingleton(new ConfirmationStore(db.GetRequiredService<IDbContextFactory<AppDbContext>>(), TimeProvider.System));

    private static Task<OperationResult> Run(AlpacaRawArgs args, OperationContext ctx) => new AlpacaRawOperation().ExecuteAsync(args, ctx);

    [Test]
    public async Task Get_ReachesTheTradingApi_WithTheQueryString()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/assets", """[{"symbol":"AAPL","tradable":true}]""");

        var result = await Run(new AlpacaRawArgs { Method = "get", Path = "/v2/assets?status=active" }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.StartWith("GET /v2/assets?status=active (paper)").And.Contain("\"symbol\": \"AAPL\""));
        Assert.That(f.Paper.Requests.Single().Uri.Query, Is.EqualTo("?status=active"));
        Assert.That(f.Paper.Requests.Single().Uri.Host, Is.EqualTo("paper-api.alpaca.markets"));
    }

    [Test]
    public async Task Data_GoesToTheMarketDataHost()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/stocks/AAPL/snapshot", """{"latestTrade":{"p":187.2}}""");

        var result = await Run(new AlpacaRawArgs { Method = "GET", Path = "/v2/stocks/AAPL/snapshot", Data = true }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(f.Paper.Requests.Single().Uri.Host, Is.EqualTo("data.alpaca.markets"));
    }

    [Test]
    public async Task Data_RefusesAMutation()
    {
        var result = await Run(new AlpacaRawArgs { Method = "POST", Path = "/v2/stocks/x", Data = true, Body = "{}" }, AccountTestKit.Context(new ScriptedAlpacaFactory()));
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [TestCase("POST", "/v2/orders")]
    [TestCase("DELETE", "/v2/orders")]
    [TestCase("PATCH", "/v2/orders/abc")]
    [TestCase("DELETE", "/V2//Orders/abc")]
    [TestCase("DELETE", "/v2/%6Frders/abc")]
    [TestCase("DELETE", "/v2/./orders/abc")]
    [TestCase("DELETE", "/v2/orders:by_client_order_id")]
    [TestCase("DELETE", "/v2/positions")]
    [TestCase("DELETE", "/v2/positions/AAPL?qty=1")]
    [TestCase("POST", "/v2/positions/AAPL241220C00200000/exercise")]
    [TestCase("POST", "/v2/positions/AAPL241220C00200000/do-not-exercise")]
    [TestCase("POST", "/v2/wallets/transfers")]
    [TestCase("POST", "/v2/wallets/whitelists")]
    // A fragment is never sent, so these reach Alpaca as the gated paths themselves.
    [TestCase("POST", "/v2/orders" + "#")]
    [TestCase("POST", "/v2/orders" + "%23x")]
    [TestCase("DELETE", "/v2/positions" + "#")]
    [TestCase("POST", "/v2/wallets/transfers" + "#")]
    [TestCase("POST", "/v2/orders;x")]
    public async Task MoneyPathMutations_AreRefused_WithTheGatedOperation(string method, string path)
    {
        var f = new ScriptedAlpacaFactory();
        foreach (var mode in new[] { BrokerMode.Paper, BrokerMode.Live })
        {
            var result = await Run(new AlpacaRawArgs { Method = method, Path = path, Mode = mode, Body = method == "DELETE" ? null : "{}" },
                AccountTestKit.Context(f, WithStore));

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked), $"{method} {path} {mode}");
            Assert.That(result.Message, Does.Contain("Use `"));
        }
        Assert.That(f.Paper.Requests, Is.Empty);
        Assert.That(f.Live.Requests, Is.Empty);
    }

    [TestCase("/v2/../v2/orders")]
    [TestCase("https://api.alpaca.markets/v2/account")]
    [TestCase("v2/account")]
    [TestCase("/v2\\orders")]
    public async Task BadPaths_AreUsageErrors(string path)
    {
        var result = await Run(new AlpacaRawArgs { Method = "DELETE", Path = path }, AccountTestKit.Context(new ScriptedAlpacaFactory()));
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError), path);
    }

    [Test]
    public async Task ReadsOfTheGatedPaths_AreAllowed()
    {
        var f = new ScriptedAlpacaFactory();
        f.Live.On("GET /v2/orders", "[]");

        var result = await Run(new AlpacaRawArgs { Method = "GET", Path = "/v2/orders?status=open", Mode = BrokerMode.Live }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(f.Live.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task PaperMutation_RunsWithoutACode()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("POST /v2/watchlists", """{"id":"w1","name":"gappers"}""");

        var result = await Run(new AlpacaRawArgs { Method = "POST", Path = "/v2/watchlists", Body = """{"name":"gappers","symbols":["AAPL"]}""" },
            AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(f.Paper.Requests.Single().Body, Is.EqualTo("""{"name":"gappers","symbols":["AAPL"]}"""));
    }

    [Test]
    public async Task LiveMutation_NeedsACodeBoundToTheExactRequest_UsedOnce()
    {
        var f = new ScriptedAlpacaFactory();
        f.Live.On("DELETE /v2/watchlists/w1", "");
        var args = new AlpacaRawArgs { Method = "DELETE", Path = "/v2/watchlists/w1", Mode = BrokerMode.Live };

        var first = await Run(args, AccountTestKit.Context(f, WithStore));
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);
        Assert.That(first.Confirmation!.Preview, Does.Contain("LIVE DELETE /v2/watchlists/w1"));
        Assert.That(f.Live.Requests, Is.Empty, "nothing is sent before the code");

        var other = await Run(args with { Path = "/v2/watchlists/w2" }, AccountTestKit.Context(f, WithStore, confirm: first.Confirmation.Code));
        Assert.That(other.Status, Is.EqualTo(OperationStatus.Blocked), "a code for another request is refused");

        var second = await Run(args, AccountTestKit.Context(f, WithStore, confirm: first.Confirmation.Code));
        Assert.That(second.IsOk, Is.True, second.Message);
        Assert.That(second.Human, Does.Contain("(no content)"));
        Assert.That(f.Live.Requests, Has.Count.EqualTo(1));

        var replay = await Run(args, AccountTestKit.Context(f, WithStore, confirm: first.Confirmation.Code));
        Assert.That(replay.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(f.Live.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AlpacaErrors_ComeBackAsFailedWithTheStatus()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/assets/NOPE", """{"code":40410000,"message":"asset not found"}""", System.Net.HttpStatusCode.NotFound);

        var result = await Run(new AlpacaRawArgs { Method = "GET", Path = "/v2/assets/NOPE" }, AccountTestKit.Context(f));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("404").And.Contain("asset not found"));
    }
}
