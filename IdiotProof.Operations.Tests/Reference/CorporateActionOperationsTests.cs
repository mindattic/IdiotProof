using System.Net;
using IdiotProof.Operations.Reference;

namespace IdiotProof.Operations.Tests.Reference;

/// <summary>corporate-actions list / show (IP-T313) over scripted Market Data and announcements.</summary>
public class CorporateActionOperationsTests
{
    [Test]
    public async Task List_FromMarketData_PagesFlattensAndDefaultsTheWindowInEt()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v1/corporate-actions", """
            {"corporate_actions":{
               "cash_dividends":[{"id":"cd1","symbol":"AAPL","rate":0.26,"special":false,"ex_date":"2026-11-10","record_date":"2026-11-10","payable_date":"2026-11-13"}],
               "forward_splits":[{"id":"fs1","symbol":"NVDA","new_rate":10,"old_rate":1,"ex_date":"2026-10-15","record_date":"2026-10-10","payable_date":"2026-10-14"}]
             },"next_page_token":null}
            """);

        var result = await new CorporateActionsListOperation().ExecuteAsync(
            new CorporateActionsListArgs { Symbol = ["aapl,nvda"], Types = ["cash_dividend,forward_split"] }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        var uri = alpaca.Requests.Single().Uri;
        Assert.That(uri.Host, Is.EqualTo("data.alpaca.markets"));
        Assert.That(uri.Query, Does.Contain("symbols=AAPL%2CNVDA").And.Contain("types=cash_dividend%2Cforward_split")
            .And.Contain("start=2026-09-30").And.Contain("end=2026-11-29"));
        var human = result.Human!;
        Assert.That(human.IndexOf("NVDA", StringComparison.Ordinal), Is.LessThan(human.IndexOf("AAPL", StringComparison.Ordinal)), "sorted by date");
        Assert.That(human, Does.Contain("forward_split").And.Contain("10:1").And.Contain("$0.2600/sh").And.Contain("Showing 2 of 2"));
    }

    [Test]
    public async Task List_FromAnnouncements_MapsTypesAndCallsPerSymbol()
    {
        var alpaca = new RoutedAlpaca().On("GET", "/v2/corporate_actions/announcements", """
            [{"id":"an1","corporate_action_id":"X","ca_type":"Split","ca_sub_type":"stock_split","initiating_symbol":"NVDA","ex_date":"2026-11-02","old_rate":"1","new_rate":"4","cash":"0"}]
            """);

        var result = await new CorporateActionsListOperation().ExecuteAsync(new CorporateActionsListArgs
        {
            Source = CorporateActionSource.Announcements, Types = ["split"], Symbol = ["NVDA"],
            Since = new DateOnly(2026, 10, 1), Until = new DateOnly(2026, 11, 30),
        }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        var uri = alpaca.Requests.Single().Uri;
        Assert.That(uri.Host, Is.EqualTo("paper-api.alpaca.markets"));
        Assert.That(uri.Query, Does.Contain("ca_types=Split").And.Contain("symbol=NVDA").And.Contain("since=2026-10-01"));
        Assert.That(result.Human, Does.Contain("Split/stock_split").And.Contain("4:1"));
    }

    [Test]
    public async Task List_Announcements_RejectsAWindowOver90Days()
    {
        var result = await new CorporateActionsListOperation().ExecuteAsync(new CorporateActionsListArgs
        {
            Source = CorporateActionSource.Announcements, Since = new DateOnly(2026, 1, 1), Until = new DateOnly(2026, 6, 1),
        }, ReferenceTestKit.Context(new RoutedAlpaca()));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task Show_FallsBackToAnnouncements_WhenMarketDataHasNoSuchId()
    {
        var alpaca = new RoutedAlpaca()
            .On("GET", "/v1/corporate-actions", """{"corporate_actions":{},"next_page_token":null}""")
            .On("GET", "/v2/corporate_actions/announcements/", """{"id":"an1","ca_type":"Dividend","initiating_symbol":"KO","ex_date":"2026-11-27","cash":"0.51","record_date":"2026-11-27"}""");

        var result = await new CorporateActionsShowOperation().ExecuteAsync(new CorporateActionsShowArgs { Id = "an1" }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Requests[0].Uri.Query, Does.Contain("ids=an1"));
        Assert.That(result.Human, Does.Contain("Dividend  KO  2026-11-27  $0.5100/sh  (announcements)").And.Match(@"Record date: +2026-11-27"));
    }

    [Test]
    public async Task Show_FailsWhenNeitherFeedKnowsTheId()
    {
        var alpaca = new RoutedAlpaca()
            .On("GET", "/v1/corporate-actions", """{"code":400,"message":"invalid ids"}""", HttpStatusCode.BadRequest)
            .On("GET", "/v2/corporate_actions/announcements/", """{"code":40410000,"message":"not found"}""", HttpStatusCode.NotFound);

        var result = await new CorporateActionsShowOperation().ExecuteAsync(new CorporateActionsShowArgs { Id = "nope" }, ReferenceTestKit.Context(alpaca));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("nope").And.Contain("not found"));
    }
}
