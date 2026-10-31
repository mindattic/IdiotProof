using IdiotProof.Operations.Account;

namespace IdiotProof.Operations.Tests.Account;

/// <summary>account activities (IP-T102): GET /v2/account/activities[/{type}], paged by the last id, rendered in ET.</summary>
public class AccountActivitiesOperationTests
{
    private const string Fills = """
        [{"id":"20261030133100000::a","activity_type":"FILL","transaction_time":"2026-10-30T13:31:00Z","type":"fill",
          "price":"131.2","qty":"10","side":"buy","symbol":"NVDA","order_id":"o1"},
         {"id":"20261029150000000::b","activity_type":"FILL","transaction_time":"2026-10-29T15:00:00Z","type":"partial_fill",
          "price":"20.5","qty":"4","side":"sell","symbol":"INTC","order_id":"o2"}]
        """;

    private const string Dividend = """
        [{"id":"20261028000000000::c","activity_type":"DIV","date":"2026-10-28","net_amount":"1.23","symbol":"MSFT",
          "qty":"3","per_share_amount":"0.41","description":"Cash DIV @ 0.41"}]
        """;

    [Test]
    public async Task OneType_UsesTheTypePath_AndRendersFillsInEt()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/account/activities/FILL", Fills);

        var result = await new AccountActivitiesOperation().ExecuteAsync(
            new AccountActivitiesArgs { Type = ["fill"], Mode = AccountScope.Paper, Since = new DateOnly(2026, 10, 29) },
            AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("2026-10-30 9:31 AM ET").And.Contain("NVDA").And.Contain("-$1,312.00"));
        Assert.That(result.Human, Does.Contain("+$82.00").And.Contain("partial fill"));
        var query = f.Paper.Requests.Single().Uri.Query;
        Assert.That(query, Does.Contain("after=2026-10-29T04%3A00%3A00Z").And.Contain("direction=desc"));
        Assert.That(query, Does.Not.Contain("activity_types"));
    }

    [Test]
    public async Task SeveralTypes_FilterByQuery_BothAccountsMergedNewestFirst()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/account/activities", Fills);
        f.Live.On("GET /v2/account/activities", Dividend);

        var result = await new AccountActivitiesOperation().ExecuteAsync(
            new AccountActivitiesArgs { Type = ["FILL", "DIV"] }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(f.Live.Requests.Single().Uri.Query, Does.Contain("activity_types=FILL%2CDIV"));
        var human = result.Human!;
        Assert.That(human.IndexOf("NVDA"), Is.LessThan(human.IndexOf("INTC")));
        Assert.That(human.IndexOf("INTC"), Is.LessThan(human.IndexOf("MSFT")));
        Assert.That(human, Does.Contain("2026-10-28").And.Contain("+$1.23"));
    }

    [Test]
    public async Task FullPage_FollowsTheLastIdAsPageToken_ShortPageEnds()
    {
        var hundred = "[" + string.Join(",", Enumerable.Range(0, 100).Select(i =>
            $$"""{"id":"id{{i:000}}","activity_type":"FILL","transaction_time":"2026-10-29T15:00:00Z","price":"1","qty":"1","side":"buy","symbol":"X"}""")) + "]";
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/account/activities", hundred).On("GET /v2/account/activities", Dividend);

        var result = await new AccountActivitiesOperation().ExecuteAsync(
            new AccountActivitiesArgs { Mode = AccountScope.Paper, Limit = 1000 }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(f.Paper.Requests, Has.Count.EqualTo(2));
        Assert.That(f.Paper.Requests[1].Uri.Query, Does.Contain("page_token=id099"));
        Assert.That(result.Human, Does.Contain("MSFT"));
    }

    [Test]
    public async Task Limit_CapsThePageSize_AndStopsPaging()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/account/activities", Fills);

        var result = await new AccountActivitiesOperation().ExecuteAsync(
            new AccountActivitiesArgs { Mode = AccountScope.Paper, Limit = 2 }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(f.Paper.Requests.Single().Uri.Query, Does.Contain("page_size=2"));
        Assert.That(result.Human, Does.Contain("showing the newest 2"));
    }

    [Test]
    public async Task BadLimit_IsUsage()
    {
        var result = await new AccountActivitiesOperation().ExecuteAsync(new AccountActivitiesArgs { Limit = 0 }, AccountTestKit.Context(new ScriptedAlpacaFactory()));
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
