using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options signals (IP-T198): the Options page's sell-the-hype nudge for every open long option.</summary>
public class OptionsSignalsOperationTests
{
    private const string Positions = """
        [
          {"symbol":"AAPL","asset_class":"us_equity","qty":"10","current_price":"250"},
          {"symbol":"AAPL261120C00250000","asset_class":"us_option","side":"long","qty":"2","avg_entry_price":"4.00","current_price":"5.20"},
          {"symbol":"AAPL261120P00240000","asset_class":"us_option","side":"short","qty":"-1","avg_entry_price":"3.00","current_price":"2.00"}
        ]
        """;

    /// <summary>Today's 5-minute option bars: the hype climbs 4.00 → 4.60 → 5.25 with the stock flat at the strike.</summary>
    private const string OptionBars = """
        {"bars":{"AAPL261120C00250000":[
          {"t":"2026-10-30T16:00:00Z","o":3.9,"h":4.1,"l":3.8,"c":4.00,"v":10},
          {"t":"2026-10-30T16:05:00Z","o":4.0,"h":4.7,"l":4.0,"c":4.60,"v":12},
          {"t":"2026-10-30T16:10:00Z","o":4.6,"h":5.3,"l":4.6,"c":5.25,"v":20}
        ]},"next_page_token":null}
        """;

    private const string StockBars = """
        {"bars":{"AAPL":[
          {"t":"2026-10-30T16:00:00Z","o":250,"h":250,"l":250,"c":250,"v":1000},
          {"t":"2026-10-30T16:05:00Z","o":250,"h":250,"l":250,"c":250,"v":1000},
          {"t":"2026-10-30T16:10:00Z","o":250,"h":250,"l":250,"c":250,"v":1000}
        ]},"next_page_token":null}
        """;

    private static async Task<OperationContext> ContextAsync(OptionsDataAlpaca paper, bool bullishNews)
    {
        var services = new ServiceCollection()
            .AddSingleton<AlpacaApiFactory>(new OptionsDataFactory(paper))
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"signals-{Guid.NewGuid():N}"))
            .BuildServiceProvider();
        if (bullishNews)
        {
            await using var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            db.ResearchClaims.Add(new ResearchClaim
            {
                Ticker = "AAPL", Sentiment = "Bullish", ArticleDate = new DateOnly(2026, 10, 29),
                SignificanceScore = 82, ClaimSummary = "Apple lands a huge data-center deal", SourceName = "test",
            });
            await db.SaveChangesAsync();
        }
        return new OperationContext
        {
            Services = services,
            OwnerId = Guid.NewGuid(),
            Clock = new Fixtures.FixedClock(OptionsDataKit.Now),
        };
    }

    private static OptionsDataAlpaca Paper() => new OptionsDataAlpaca()
        .On("/v2/positions", Positions)
        .On("/v1beta1/options/bars", OptionBars)
        .On("/v2/stocks/bars", StockBars)
        .On("/v2/stocks/AAPL/trades/latest", OptionsDataKit.Spot);

    [Test]
    public async Task HypeNearItsHigh_PlusBullishNews_Nudges_OnlyTheLongContract()
    {
        var paper = Paper();
        var result = await new OptionsSignalsOperation().ExecuteAsync(new AccountScopeArgs { Mode = AccountScope.Paper }, await ContextAsync(paper, bullishNews: true));

        Assert.That(result.IsOk, Is.True, result.Message);
        var rows = OptionsDataKit.Data(result).GetProperty("signals");
        Assert.That(rows.GetArrayLength(), Is.EqualTo(1), "equities and the written put are not nudged");
        var row = rows[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("symbol").GetString(), Is.EqualTo("AAPL261120C00250000"));
            Assert.That(row.GetProperty("nudge").GetBoolean(), Is.True);
            Assert.That(row.GetProperty("extrinsic_per_share").GetDecimal(), Is.EqualTo(5.20m));
            Assert.That(row.GetProperty("extrinsic_high").GetDecimal(), Is.EqualTo(5.25m));
            Assert.That(row.GetProperty("samples").GetInt32(), Is.EqualTo(3));
            Assert.That(result.Human, Does.Contain("NUDGE").And.Contain("consider taking profit").And.Contain("nothing is ever placed"));
        });
        Assert.That(paper.Urls.Any(u => u.Contains("/v1beta1/options/bars") && u.Contains("timeframe=5Min")), Is.True);
    }

    [Test]
    public async Task WithoutBullishNews_StaysQuiet_AndSaysWhy()
    {
        var result = await new OptionsSignalsOperation().ExecuteAsync(new AccountScopeArgs { Mode = AccountScope.Paper }, await ContextAsync(Paper(), bullishNews: false));

        Assert.That(result.IsOk, Is.True, result.Message);
        var row = OptionsDataKit.Data(result).GetProperty("signals")[0];
        Assert.That(row.GetProperty("nudge").GetBoolean(), Is.False);
        Assert.That(row.GetProperty("verdict").GetString(), Is.EqualTo("no bullish news in 7d"));
    }

    [Test]
    public async Task NoLongOptions_SaysSo()
    {
        var paper = new OptionsDataAlpaca().On("/v2/positions", "[]");
        var result = await new OptionsSignalsOperation().ExecuteAsync(new AccountScopeArgs { Mode = AccountScope.Paper }, await ContextAsync(paper, bullishNews: false));

        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Human, Does.Contain("No open long option positions"));
    }
}
