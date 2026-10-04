using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Models;
using IdiotProof.Monitor;

namespace IdiotProof.Monitor.Tests;

/// <summary>
/// Opt-in check of the Monitor's short-entry order against the REAL Alpaca PAPER account.
/// <c>[Explicit]</c> so a plain <c>dotnet test</c> never touches the network; run it by name:
/// <code>dotnet test IdiotProof.Monitor.Tests --filter FullyQualifiedName~AlpacaPaperShortIntegrationTests</code>
/// Keys come from the MindAttic broker keyring (<c>%APPDATA%\MindAttic\Brokers\providers.json</c>,
/// entry <c>alpaca-paper</c>). The fixture is skipped when that file or entry is absent, and fails
/// rather than run when the entry points anywhere but <c>https://paper-api.alpaca.markets</c>.
/// <para>
/// The test builds the order with <see cref="DirectionalOrders.Entry"/> (side <c>sell</c> +
/// <c>position_intent: sell_to_open</c>, limit, DAY) and sends it through
/// <see cref="AlpacaBrokerClient.PlaceOrderAsync"/>, the same code the Monitor runs. It shorts
/// 1 share of a shortable, easy-to-borrow large-cap at a limit about twice the last trade, so it
/// cannot fill, reads the order back, then cancels it and confirms the cancel.
/// </para>
/// <para>
/// Alpaca opens a short only on a margin account with shorting enabled (account equity of at least
/// $2,000). When the paper account is not eligible the test still sends the order, because Alpaca's
/// answer shows whether the order shape is valid, then reports the result as Inconclusive.
/// </para>
/// </summary>
[TestFixture]
[Explicit("Hits the real Alpaca PAPER account (places + cancels a never-fillable 1-share short).")]
public sealed class AlpacaPaperShortIntegrationTests
{
    private const string PaperHost = "https://paper-api.alpaca.markets";
    private const string DataHost = "https://data.alpaca.markets";
    private const string Symbol = "AAPL";

    private AlpacaBrokerClient broker = null!;
    private HttpClient trading = null!;
    private HttpClient data = null!;

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
        if (!string.Equals(baseUrl?.TrimEnd('/'), PaperHost, StringComparison.OrdinalIgnoreCase))
            Assert.Fail($"Refusing: 'alpaca-paper' keyring entry points at '{baseUrl}', not {PaperHost}.");

        var key = paper.TryGetProperty("apiKey", out var k) ? k.GetString() : null;
        var secret = paper.TryGetProperty("secret", out var s) ? s.GetString() : null;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
            Assert.Ignore("Keyring 'alpaca-paper' entry is missing apiKey/secret.");

        broker = new AlpacaBrokerClient(key!, secret!, isPaper: true);
        Assert.That(broker.IsPaper, Is.True, "Client must be pointed at the paper host.");

        trading = Client(PaperHost, key!, secret!);
        data = Client(DataHost, key!, secret!);
    }

    [OneTimeTearDown]
    public async Task Dispose()
    {
        if (broker is not null) await broker.DisposeAsync();
        trading?.Dispose();
        data?.Dispose();
    }

    [Test]
    public async Task ShortEntry_SellToOpen_UnfillableLimit_IsAcceptedThenCancelled()
    {
        // 1. The asset must be shortable and easy to borrow; Alpaca opens shorts only in ETB names.
        var asset = await GetJsonAsync(trading, $"/v2/assets/{Symbol}");
        var shortable = asset.GetProperty("shortable").GetBoolean();
        var etb = asset.GetProperty("easy_to_borrow").GetBoolean();
        TestContext.Out.WriteLine($"{Symbol}: shortable={shortable} easy_to_borrow={etb}");
        Assert.That(shortable && etb, Is.True, $"{Symbol} must be shortable and easy to borrow.");

        var account = await GetJsonAsync(trading, "/v2/account");
        var shortingEnabled = account.TryGetProperty("shorting_enabled", out var se) && se.ValueKind == JsonValueKind.True;
        TestContext.Out.WriteLine($"account: shorting_enabled={shortingEnabled} equity={Str(account, "equity")} multiplier={Str(account, "multiplier")}");

        // 2. Price the short far above the market so the sell limit cannot fill.
        var trade = await GetJsonAsync(data, $"/v2/stocks/{Symbol}/trades/latest");
        var last = trade.GetProperty("trade").GetProperty("p").GetDecimal();
        var order = DirectionalOrders.Entry(Symbol, TradeDirection.Short, 1, Math.Round(last * 2m, 2), extendedHours: false);
        TestContext.Out.WriteLine($"last={last} → side={order.Side} intent={order.PositionIntent} limit={order.LimitPrice} tif={order.TimeInForce}");
        Assert.That(order.LimitPrice, Is.GreaterThan(last * 1.5m), "the short limit must sit far above the market");

        // 3. Send it through the adapter the Monitor uses.
        var placed = await broker.PlaceOrderAsync(order);
        TestContext.Out.WriteLine($"Place: success={placed.IsSuccess} id={placed.BrokerOrderId} msg={placed.Message}");

        if (!placed.IsSuccess)
        {
            // A malformed side/position_intent is a 422 (code 40010001 "invalid position_intent");
            // an ineligible account is a 403 (code 40310000 "account is not allowed to short").
            Assert.That(placed.Message, Does.Not.Contain("HTTP 422"),
                "Alpaca rejected the order shape itself.");
            if (!shortingEnabled)
                Assert.Inconclusive($"The paper account cannot short (shorting_enabled=false); Alpaca answered: {placed.Message}");
            Assert.Fail(placed.Message);
        }

        await ReadBackThenCancelAsync(placed.BrokerOrderId, "sell", "sell_to_open");
    }

    /// <summary>
    /// Shows that Alpaca accepts <c>side</c> + <c>position_intent</c> on an equity order without needing a
    /// short-enabled account: the Monitor's long exit (<c>sell_to_close</c>) for 1 share of a long the paper
    /// account already holds, at a limit twice the market so it cannot fill, then cancelled. Skipped when
    /// the paper account holds no long equity position.
    /// </summary>
    [Test]
    public async Task LongExit_SellToClose_UnfillableLimit_IsAcceptedThenCancelled()
    {
        var held = (await broker.GetPositionsAsync())
            .FirstOrDefault(p => p.AssetClass == AssetClass.Equity && p.Quantity >= 1m);
        if (held is null)
            Assert.Ignore("The paper account holds no long equity position to close.");

        var trade = await GetJsonAsync(data, $"/v2/stocks/{held!.Symbol}/trades/latest");
        var last = trade.GetProperty("trade").GetProperty("p").GetDecimal();
        var order = DirectionalOrders.Exit(held.Symbol, TradeDirection.Long, 1, Math.Round(last * 2m, 2), extendedHours: false);
        TestContext.Out.WriteLine($"{held.Symbol}: held={held.Quantity} last={last} → side={order.Side} intent={order.PositionIntent} limit={order.LimitPrice}");
        Assert.That(order.LimitPrice, Is.GreaterThan(last * 1.5m), "the sell limit must sit far above the market");

        var placed = await broker.PlaceOrderAsync(order);
        TestContext.Out.WriteLine($"Place: success={placed.IsSuccess} id={placed.BrokerOrderId} msg={placed.Message}");
        Assert.That(placed.IsSuccess, Is.True, placed.Message);

        await ReadBackThenCancelAsync(placed.BrokerOrderId, "sell", "sell_to_close");
    }

    private async Task ReadBackThenCancelAsync(string orderId, string side, string intent)
    {
        try
        {
            var live = await GetJsonAsync(trading, $"/v2/orders/{orderId}");
            TestContext.Out.WriteLine($"Order: status={Str(live, "status")} side={Str(live, "side")} position_intent={Str(live, "position_intent")} qty={Str(live, "qty")} limit={Str(live, "limit_price")}");
            Assert.Multiple(() =>
            {
                Assert.That(Str(live, "side"), Is.EqualTo(side));
                Assert.That(Str(live, "position_intent"), Is.EqualTo(intent));
                Assert.That(Str(live, "qty"), Is.EqualTo("1"), "Alpaca keeps qty positive; the side carries the direction");
                Assert.That(Str(live, "filled_qty"), Is.EqualTo("0"), "a limit twice the market must not have filled");
            });
        }
        finally
        {
            var cancel = await broker.CancelOrderAsync(orderId);
            TestContext.Out.WriteLine($"Cancel: success={cancel.IsSuccess} msg={cancel.Message}");
            Assert.That(cancel.IsSuccess, Is.True, cancel.Message);
        }

        var after = await GetJsonAsync(trading, $"/v2/orders/{orderId}");
        TestContext.Out.WriteLine($"After cancel: status={Str(after, "status")} canceled_at={Str(after, "canceled_at")}");
        Assert.That(Str(after, "status"), Is.AnyOf("canceled", "pending_cancel"));
    }

    private static HttpClient Client(string host, string key, string secret)
    {
        var client = new HttpClient { BaseAddress = new Uri(host), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", key);
        client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", secret);
        return client;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"GET {url} → {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) ? p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString() : null;
}
