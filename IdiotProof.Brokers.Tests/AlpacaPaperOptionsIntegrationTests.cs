using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Models;

namespace IdiotProof.Brokers.Tests;

/// <summary>
/// IP-US-U10 harness: talks to the REAL Alpaca PAPER account through the same
/// <see cref="AlpacaBrokerClient"/> the Options page uses. <c>[Explicit]</c> so a plain
/// <c>dotnet test</c> never touches the network; run it by name:
/// <code>dotnet test IdiotProof.Brokers.Tests --filter FullyQualifiedName~AlpacaPaperOptionsIntegrationTests</code>
/// Keys come from the MindAttic broker keyring (<c>%APPDATA%\MindAttic\Brokers\providers.json</c>,
/// entry <c>alpaca-paper</c>); the fixture is skipped when that file is absent and REFUSES to run
/// against anything but <c>paper-api.alpaca.markets</c>. The order test buys to open one contract
/// at $0.01 — far below any real premium — so it can never fill, then cancels it. The fill-and-close
/// half of U10 stays a deliberate, market-hours, user-initiated action on <c>/options</c>.
/// </summary>
[TestFixture]
[Explicit("Hits the real Alpaca PAPER account (places + cancels a never-fillable options order).")]
public class AlpacaPaperOptionsIntegrationTests
{
    private const string Underlying = "BE";
    private AlpacaBrokerClient broker = null!;

    [OneTimeSetUp]
    public void ResolvePaperKeys()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic", "Brokers", "providers.json");
        if (!File.Exists(path))
            Assert.Ignore($"No broker keyring at {path}.");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("alpaca-paper", out var paper))
            Assert.Ignore("Keyring has no 'alpaca-paper' entry.");

        var baseUrl = paper.TryGetProperty("baseUrl", out var b) ? b.GetString() : null;
        if (baseUrl is not null && !baseUrl.Contains("paper-api.alpaca.markets", StringComparison.OrdinalIgnoreCase))
            Assert.Fail($"Refusing: 'alpaca-paper' keyring entry points at {baseUrl}, not the paper host.");

        var key = paper.GetProperty("apiKey").GetString();
        var secret = paper.GetProperty("secret").GetString();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
            Assert.Ignore("Keyring 'alpaca-paper' entry is missing apiKey/secret.");

        broker = new AlpacaBrokerClient(key, secret, isPaper: true);
        Assert.That(broker.IsPaper, Is.True, "Client must be pointed at the paper host.");
    }

    [OneTimeTearDown]
    public async Task Dispose() { if (broker is not null) await broker.DisposeAsync(); }

    [Test, Order(1)]
    public async Task Account_ReportsOptionsApproval_ViaPluralFields()
    {
        var info = await broker.GetOptionsAccountAsync();
        TestContext.Out.WriteLine($"options_trading_level={info.TradingLevel} approved={info.ApprovedLevel} options_buying_power={info.OptionsBuyingPower}");
        Assert.Multiple(() =>
        {
            Assert.That(info.TradingLevel, Is.GreaterThanOrEqualTo(2), "buy-to-open needs level 2+");
            Assert.That(info.ApprovedLevel, Is.GreaterThanOrEqualTo(info.TradingLevel));
            Assert.That(info.OptionsBuyingPower, Is.Not.Null.And.GreaterThan(0m));
        });
    }

    [Test, Order(2)]
    public async Task Chain_And_Quotes_ComeBackForARealUnderlying()
    {
        var chain = await broker.GetOptionChainAsync(Underlying);
        var quotes = await broker.GetOptionQuotesAsync(Underlying);
        TestContext.Out.WriteLine($"{Underlying}: {chain.Count} contracts, {quotes.Count} snapshots, expirations={string.Join(",", chain.Select(c => c.Expiration).Distinct().OrderBy(d => d).Take(6))}");

        Assert.Multiple(() =>
        {
            Assert.That(chain, Is.Not.Empty);
            Assert.That(chain.All(c => c.UnderlyingSymbol == Underlying), Is.True);
            Assert.That(chain.All(c => OptionContract.ParseOcc(c.OccSymbol) is not null), Is.True, "every OCC symbol decodes");
            Assert.That(quotes, Is.Not.Empty);
            Assert.That(quotes.Select(q => q.OccSymbol).Intersect(chain.Select(c => c.OccSymbol)).Any(), Is.True, "quotes join to the chain by OCC symbol");
        });
    }

    [Test, Order(3)]
    public async Task BuyToOpen_UnfillableLimit_PlacesAndCancels()
    {
        var contract = await PickLiquidCallAsync();
        TestContext.Out.WriteLine($"Contract: {contract.DisplayName} ({contract.OccSymbol})");

        var placed = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol = contract.OccSymbol,
            AssetClass = AssetClass.Option,
            Option = contract,
            Quantity = 1,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 0.01m,          // no real premium is a penny — cannot fill
            TimeInForce = "DAY",
            PositionIntent = "buy_to_open",
        });
        TestContext.Out.WriteLine($"Place: success={placed.IsSuccess} id={placed.BrokerOrderId} msg={placed.Message}");
        Assert.That(placed.IsSuccess, Is.True, placed.Message);
        Assert.That(placed.BrokerOrderId, Is.Not.Empty);

        try
        {
            var positions = await broker.GetPositionsAsync();
            Assert.That(positions.Any(p => p.Symbol == contract.OccSymbol), Is.False, "a $0.01 limit must not have filled");
        }
        finally
        {
            var cancel = await broker.CancelOrderAsync(placed.BrokerOrderId);
            TestContext.Out.WriteLine($"Cancel: success={cancel.IsSuccess} msg={cancel.Message}");
            Assert.That(cancel.IsSuccess, Is.True, cancel.Message);
        }
    }

    /// <summary>Nearest expiration at least three weeks out, strike closest to the quoted market (highest OI as tiebreak).</summary>
    private async Task<OptionContract> PickLiquidCallAsync()
    {
        var chain = await broker.GetOptionChainAsync(Underlying);
        var quotes = (await broker.GetOptionQuotesAsync(Underlying)).ToDictionary(q => q.OccSymbol, q => q);

        var expiration = chain.Select(c => c.Expiration).Distinct()
            .Where(d => d >= DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(21)))
            .OrderBy(d => d).First();

        var calls = chain.Where(c => c.Right == OptionRight.Call && c.Expiration == expiration && c.Tradable && quotes.ContainsKey(c.OccSymbol)).ToList();
        Assert.That(calls, Is.Not.Empty, $"no quoted calls for {Underlying} {expiration}");

        // Quoted mid nearest to a "sensible" premium: the ATM call is where bid/ask is tightest.
        return calls
            .OrderByDescending(c => quotes[c.OccSymbol].Bid > 0m && quotes[c.OccSymbol].Ask > 0m)
            .ThenBy(c => Math.Abs(quotes[c.OccSymbol].Ask - quotes[c.OccSymbol].Bid))
            .ThenByDescending(c => c.OpenInterest ?? 0)
            .First();
    }
}
