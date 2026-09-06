using System.Net;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Models;

namespace IdiotProof.Brokers.Tests;

/// <summary>Sandbox synthetic options chain + fills (Phase 1 manual options section).</summary>
public class SandboxOptionsTests
{
    private static OptionContract Contract(string underlying = "BE", decimal strike = 38m, OptionRight right = OptionRight.Call)
    {
        var exp = new DateOnly(2026, 12, 18);
        return new OptionContract
        {
            OccSymbol = OptionContract.BuildOcc(underlying, exp, right, strike),
            UnderlyingSymbol = underlying, Expiration = exp, Strike = strike, Right = right,
        };
    }

    [Test]
    public async Task Chain_IsCenteredOnReferencePrice_WithCallsAndPuts()
    {
        var broker = new SandboxBrokerClient();
        broker.SetReferencePrice("BE", 45m);

        var chain = await broker.GetOptionChainAsync("BE");

        Assert.Multiple(() =>
        {
            Assert.That(chain, Is.Not.Empty);
            Assert.That(chain.Select(c => c.Expiration).Distinct().Count(), Is.EqualTo(4));
            Assert.That(chain.Count(c => c.Right == OptionRight.Call), Is.EqualTo(chain.Count(c => c.Right == OptionRight.Put)));
            Assert.That(chain.Any(c => c.Strike == 45m), Is.True, "ATM strike present");
            Assert.That(chain.All(c => c.UnderlyingSymbol == "BE"), Is.True);
            Assert.That(chain.All(c => OptionContract.ParseOcc(c.OccSymbol)?.Strike == c.Strike), Is.True, "OCC symbols round-trip");
        });
    }

    [Test]
    public async Task Chain_FilteredToOneExpiration()
    {
        var broker = new SandboxBrokerClient();
        var exp = new DateOnly(2026, 12, 18);
        var chain = await broker.GetOptionChainAsync("NVDA", exp);
        Assert.That(chain.All(c => c.Expiration == exp), Is.True);
    }

    [Test]
    public async Task Quotes_CoverEveryContract_AndRespectParity()
    {
        var broker = new SandboxBrokerClient();
        broker.SetReferencePrice("BE", 45m);
        var chain = await broker.GetOptionChainAsync("BE");
        var quotes = await broker.GetOptionQuotesAsync("BE");

        var bySymbol = quotes.ToDictionary(q => q.OccSymbol);
        Assert.That(bySymbol, Has.Count.EqualTo(chain.Count));

        foreach (var c in chain)
        {
            var q = bySymbol[c.OccSymbol];
            var intrinsic = c.Right == OptionRight.Call ? Math.Max(0m, 45m - c.Strike) : Math.Max(0m, c.Strike - 45m);
            Assert.That(q.Ask, Is.GreaterThan(q.Bid), c.OccSymbol);
            Assert.That(q.Mid, Is.GreaterThanOrEqualTo(intrinsic), $"{c.OccSymbol} never quotes below parity");
            Assert.That(q.ImpliedVolatility, Is.Null, "sandbox omits IV so the local model path is exercised");
        }
    }

    [Test]
    public async Task BuyCall_BooksAnOptionPosition_InContracts()
    {
        var broker = new SandboxBrokerClient();
        var contract = Contract();
        var result = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol = contract.OccSymbol, AssetClass = AssetClass.Option, Option = contract,
            Quantity = 2, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 9.50m,
        });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var positions = await broker.GetPositionsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(positions, Has.Count.EqualTo(1));
            Assert.That(positions[0].AssetClass, Is.EqualTo(AssetClass.Option));
            Assert.That(positions[0].Quantity, Is.EqualTo(2m));
            Assert.That(positions[0].AveragePrice, Is.EqualTo(9.50m));
            Assert.That(positions[0].MarketValue, Is.EqualTo(2m * 9.50m * 100m), "market value is × multiplier");
            Assert.That(positions[0].Option?.Strike, Is.EqualTo(38m));
        });
    }

    [Test]
    public async Task SellToClose_FlattensTheOptionPosition()
    {
        var broker = new SandboxBrokerClient();
        var contract = Contract();
        await broker.PlaceOrderAsync(new OrderRequest { Symbol = contract.OccSymbol, AssetClass = AssetClass.Option, Option = contract, Quantity = 2, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 9.50m });
        await broker.PlaceOrderAsync(new OrderRequest { Symbol = contract.OccSymbol, AssetClass = AssetClass.Option, Option = contract, Quantity = 2, Side = OrderSide.Sell, Type = OrderType.Limit, LimitPrice = 12.35m, PositionIntent = "sell_to_close" });

        Assert.That(await broker.GetPositionsAsync(), Is.Empty);
    }

    private static OrderRequest Order(OptionContract c, OrderSide side, int qty, decimal price) => new()
    {
        Symbol = c.OccSymbol, AssetClass = AssetClass.Option, Option = c, Quantity = qty, Side = side, Type = OrderType.Limit, LimitPrice = price,
    };

    [Test]
    public async Task AddingToAShort_BlendsTheBasis()
    {
        var broker = new SandboxBrokerClient();
        var c = Contract();
        await broker.PlaceOrderAsync(Order(c, OrderSide.Sell, 5, 10m));
        await broker.PlaceOrderAsync(Order(c, OrderSide.Sell, 3, 14m));

        var p = (await broker.GetPositionsAsync()).Single();
        Assert.That(p.Quantity, Is.EqualTo(-8m));
        Assert.That(p.AveragePrice, Is.EqualTo(11.5m), "(10×5 + 14×3) / 8 — blended by |size|, not stuck at the first fill");
    }

    [Test]
    public async Task PartialClose_KeepsTheOriginalBasis()
    {
        var broker = new SandboxBrokerClient();
        var c = Contract();
        await broker.PlaceOrderAsync(Order(c, OrderSide.Buy, 5, 10m));
        await broker.PlaceOrderAsync(Order(c, OrderSide.Sell, 2, 13m));

        var p = (await broker.GetPositionsAsync()).Single();
        Assert.That(p.Quantity, Is.EqualTo(3m));
        Assert.That(p.AveragePrice, Is.EqualTo(10m));
    }

    [Test]
    public async Task FlippingThroughZero_StartsAFreshBasisAtTheFill()
    {
        var broker = new SandboxBrokerClient();
        var c = Contract();
        await broker.PlaceOrderAsync(Order(c, OrderSide.Buy, 5, 10m));
        await broker.PlaceOrderAsync(Order(c, OrderSide.Sell, 8, 12m)); // closes 5, opens a 3-contract short at 12

        var p = (await broker.GetPositionsAsync()).Single();
        Assert.That(p.Quantity, Is.EqualTo(-3m));
        Assert.That(p.AveragePrice, Is.EqualTo(12m), "the old long's basis has nothing to do with the new short");
    }

    [Test]
    public async Task ReopeningAFlatRow_StartsAFreshBasis()
    {
        var broker = new SandboxBrokerClient();
        var c = Contract();
        await broker.PlaceOrderAsync(Order(c, OrderSide.Buy, 2, 10m));
        await broker.PlaceOrderAsync(Order(c, OrderSide.Sell, 2, 15m)); // flat (row retained at qty 0)
        await broker.PlaceOrderAsync(Order(c, OrderSide.Buy, 1, 7m));

        var p = (await broker.GetPositionsAsync()).Single();
        Assert.That(p.Quantity, Is.EqualTo(1m));
        Assert.That(p.AveragePrice, Is.EqualTo(7m));
    }

    [Test]
    public async Task OptionOrder_WithoutContract_IsRejected()
    {
        var broker = new SandboxBrokerClient();
        var result = await broker.PlaceOrderAsync(new OrderRequest { Symbol = "BE", AssetClass = AssetClass.Option, Quantity = 1, Side = OrderSide.Buy });
        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void DefaultInterface_ReportsNoOptions_ForBrokersThatDontOptIn()
    {
        IBrokerClient dumb = new NoOptionsBroker();
        Assert.That(dumb.SupportsOptions, Is.False);
        Assert.That(dumb.GetOptionChainAsync("BE").Result, Is.Empty);
        Assert.That(dumb.GetOptionTradingLevelAsync().Result, Is.EqualTo(0));
    }

    private sealed class NoOptionsBroker : IBrokerClient
    {
        public BrokerType BrokerType => BrokerType.Sandbox;
        public bool IsPaper => true;
        public bool IsConnected => true;
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Position>>([]);
    }
}

/// <summary>
/// Alpaca options wire format — asserted against canned request/response shapes from Alpaca's
/// docs. Field casing/nullability should be re-checked against a real paper response once the
/// account is options-approved (see docs/rfc/0004).
/// </summary>
public class AlpacaOptionsWireTests
{
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static OptionContract BeDecCall() => OptionContract.ParseOcc("BE251219C00038000")!;

    [Test]
    public async Task OptionOrder_SendsOccSymbol_QtyOnly_Day_NoExtendedHours()
    {
        var handler = new RecordingHandler(_ => Json("""{"id":"abc-123","status":"accepted"}"""));
        var broker = new AlpacaBrokerClient(handler);

        var result = await broker.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "BE251219C00038000", AssetClass = AssetClass.Option, Option = BeDecCall(),
            Quantity = 2, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 9.50m, PositionIntent = "buy_to_open",
        });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.BrokerOrderId, Is.EqualTo("abc-123"));
        Assert.That(handler.Requests[0].RequestUri!.PathAndQuery, Is.EqualTo("/v2/orders"));

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("symbol").GetString(), Is.EqualTo("BE251219C00038000"));
            Assert.That(root.GetProperty("qty").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("side").GetString(), Is.EqualTo("buy"));
            Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("limit"));
            Assert.That(root.GetProperty("time_in_force").GetString(), Is.EqualTo("day"));
            Assert.That(root.GetProperty("limit_price").GetDecimal(), Is.EqualTo(9.50m));
            Assert.That(root.GetProperty("position_intent").GetString(), Is.EqualTo("buy_to_open"));
            Assert.That(root.TryGetProperty("notional", out _), Is.False);
            Assert.That(root.TryGetProperty("extended_hours", out _), Is.False);
        });
    }

    [Test]
    public async Task OptionOrder_RejectsNotional_ExtendedHours_Gtc_AndStopTypes_Locally()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not hit the wire"));
        var broker = new AlpacaBrokerClient(handler);
        var c = BeDecCall();

        var notional = await broker.PlaceOrderAsync(new OrderRequest { Symbol = c.OccSymbol, AssetClass = AssetClass.Option, Option = c, Notional = 500m, Side = OrderSide.Buy });
        var ext = await broker.PlaceOrderAsync(new OrderRequest { Symbol = c.OccSymbol, AssetClass = AssetClass.Option, Option = c, Quantity = 1, ExtendedHours = true, Side = OrderSide.Buy });
        var gtc = await broker.PlaceOrderAsync(new OrderRequest { Symbol = c.OccSymbol, AssetClass = AssetClass.Option, Option = c, Quantity = 1, TimeInForce = "GTC", Side = OrderSide.Buy });
        var stop = await broker.PlaceOrderAsync(new OrderRequest { Symbol = c.OccSymbol, AssetClass = AssetClass.Option, Option = c, Quantity = 1, Type = OrderType.TrailingStop, Side = OrderSide.Sell });

        Assert.Multiple(() =>
        {
            Assert.That(notional.IsSuccess, Is.False); Assert.That(notional.Message, Does.Contain("notional"));
            Assert.That(ext.IsSuccess, Is.False); Assert.That(ext.Message, Does.Contain("extended hours"));
            Assert.That(gtc.IsSuccess, Is.False); Assert.That(gtc.Message, Does.Contain("DAY"));
            Assert.That(stop.IsSuccess, Is.False); Assert.That(stop.Message, Does.Contain("Market or Limit"));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task Positions_DecodeUsOptionAssetClass_IntoContract()
    {
        var handler = new RecordingHandler(_ => Json("""
            [
              {"symbol":"AAPL","asset_class":"us_equity","qty":"10","avg_entry_price":"180.5","market_value":"1900","unrealized_pl":"95"},
              {"symbol":"BE251219C00038000","asset_class":"us_option","qty":"2","avg_entry_price":"9.5","market_value":"2470","unrealized_pl":"570"}
            ]
            """));
        var broker = new AlpacaBrokerClient(handler);

        var positions = await broker.GetPositionsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(positions, Has.Count.EqualTo(2));
            Assert.That(positions[0].AssetClass, Is.EqualTo(AssetClass.Equity));
            Assert.That(positions[0].Option, Is.Null);
            Assert.That(positions[1].AssetClass, Is.EqualTo(AssetClass.Option));
            Assert.That(positions[1].Quantity, Is.EqualTo(2m), "contracts, not shares");
            Assert.That(positions[1].Option!.UnderlyingSymbol, Is.EqualTo("BE"));
            Assert.That(positions[1].Option!.Strike, Is.EqualTo(38m));
            Assert.That(positions[1].Option!.Right, Is.EqualTo(OptionRight.Call));
            Assert.That(positions[1].Option!.Expiration, Is.EqualTo(new DateOnly(2025, 12, 19)));
        });
    }

    [Test]
    public async Task Chain_ParsesContractCatalog_AndFollowsPageToken()
    {
        var page = 0;
        var handler = new RecordingHandler(req =>
        {
            page++;
            return page == 1
                ? Json("""
                    {"option_contracts":[
                      {"symbol":"BE251219C00038000","underlying_symbol":"BE","expiration_date":"2025-12-19","strike_price":"38","type":"call","open_interest":"1520","size":"100","tradable":true},
                      {"symbol":"BE251219P00038000","underlying_symbol":"BE","expiration_date":"2025-12-19","strike_price":"38","type":"put","open_interest":"880","size":"100","tradable":true}
                    ],"next_page_token":"tok2"}
                    """)
                : Json("""
                    {"option_contracts":[
                      {"symbol":"BE251219C00040000","underlying_symbol":"BE","expiration_date":"2025-12-19","strike_price":"40","type":"call","open_interest":"2010","size":"100","tradable":true}
                    ],"next_page_token":null}
                    """);
        });
        var broker = new AlpacaBrokerClient(handler);

        var chain = await broker.GetOptionChainAsync("be", new DateOnly(2025, 12, 19));

        Assert.Multiple(() =>
        {
            Assert.That(chain, Has.Count.EqualTo(3));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[0].RequestUri!.Host, Is.EqualTo("paper-api.alpaca.markets"));
            Assert.That(handler.Requests[0].RequestUri!.PathAndQuery, Does.StartWith("/v2/options/contracts?underlying_symbols=BE").And.Contain("expiration_date=2025-12-19"));
            Assert.That(handler.Requests[1].RequestUri!.Query, Does.Contain("page_token=tok2"));
            Assert.That(chain[1].Right, Is.EqualTo(OptionRight.Put));
            Assert.That(chain[1].OpenInterest, Is.EqualTo(880));
            Assert.That(chain[2].Strike, Is.EqualTo(40m));
        });
    }

    [Test]
    public async Task Snapshots_HitDataHost_AndParseGreeksWhenPresent()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"snapshots":{
              "BE251219C00038000":{
                "latestQuote":{"ap":9.7,"as":12,"bp":9.3,"bs":8,"t":"2025-08-20T15:59:58.123Z"},
                "latestTrade":{"p":9.5,"s":1,"t":"2025-08-20T15:59:50Z"},
                "impliedVolatility":0.8123,
                "greeks":{"delta":0.71,"gamma":0.021,"theta":-0.045,"vega":0.11,"rho":0.06}
              },
              "BE251219P00038000":{
                "latestQuote":{"ap":2.1,"as":5,"bp":1.9,"bs":5,"t":"2025-08-20T15:59:58.123Z"}
              }
            },"next_page_token":null}
            """));
        var broker = new AlpacaBrokerClient(handler);

        var quotes = await broker.GetOptionQuotesAsync("BE");

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests[0].RequestUri!.Host, Is.EqualTo("data.alpaca.markets"));
            Assert.That(handler.Requests[0].RequestUri!.PathAndQuery, Does.StartWith("/v1beta1/options/snapshots/BE?feed=indicative"));
            Assert.That(quotes, Has.Count.EqualTo(2));

            var call = quotes.Single(q => q.OccSymbol.EndsWith("C00038000"));
            Assert.That(call.Bid, Is.EqualTo(9.3m));
            Assert.That(call.Ask, Is.EqualTo(9.7m));
            Assert.That(call.Mid, Is.EqualTo(9.5m));
            Assert.That(call.LastTrade, Is.EqualTo(9.5m));
            Assert.That(call.ImpliedVolatility, Is.EqualTo(0.8123m));
            Assert.That(call.Greeks!.Delta, Is.EqualTo(0.71m));
            Assert.That(call.TimestampUtc.Kind, Is.EqualTo(DateTimeKind.Utc));

            var put = quotes.Single(q => q.OccSymbol.EndsWith("P00038000"));
            Assert.That(put.ImpliedVolatility, Is.Null, "omitted greeks stay null — no guessing");
            Assert.That(put.Greeks, Is.Null);
            Assert.That(put.Mid, Is.EqualTo(2.0m));
        });
    }

    [Test]
    public async Task TradingLevel_ReadsAlpacasPluralFieldNames()
    {
        // Real /v2/account shape: options_trading_level (effective), options_approved_level,
        // options_buying_power — plural "options_". The first cut read the singular spelling,
        // which Alpaca never sends, so every account looked unapproved.
        var real = new AlpacaBrokerClient(new RecordingHandler(_ => Json("""{"id":"x","options_approved_level":3,"options_trading_level":2,"options_buying_power":"12345.67"}""")));

        var info = await real.GetOptionsAccountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(info.TradingLevel, Is.EqualTo(2));
            Assert.That(info.ApprovedLevel, Is.EqualTo(3));
            Assert.That(info.OptionsBuyingPower, Is.EqualTo(12345.67m));
        });
        Assert.That(await real.GetOptionTradingLevelAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task TradingLevel_FallsBackToSingularSpelling_AndZeroWhenAbsent()
    {
        var singular = new AlpacaBrokerClient(new RecordingHandler(_ => Json("""{"id":"x","option_approved_level":3,"option_trading_level":2}""")));
        var legacy = new AlpacaBrokerClient(new RecordingHandler(_ => Json("""{"id":"x","status":"ACTIVE"}""")));

        Assert.That(await singular.GetOptionTradingLevelAsync(), Is.EqualTo(2));
        var none = await legacy.GetOptionsAccountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(none.TradingLevel, Is.EqualTo(0));
            Assert.That(none.ApprovedLevel, Is.EqualTo(0));
            Assert.That(none.OptionsBuyingPower, Is.Null);
        });
    }

    [Test]
    public void Chain_FailsLoud_OnHttpError()
    {
        var broker = new AlpacaBrokerClient(new RecordingHandler(_ => Json("""{"message":"forbidden"}""", HttpStatusCode.Forbidden)));
        Assert.ThrowsAsync<HttpRequestException>(() => broker.GetOptionChainAsync("BE"));
    }
}
