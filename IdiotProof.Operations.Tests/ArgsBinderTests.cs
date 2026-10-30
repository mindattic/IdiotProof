using System.Text.Json;
using IdiotProof.Operations;

namespace IdiotProof.Operations.Tests;

/// <summary>JSON + argv binding and the argv helpers (IP-T55).</summary>
public class ArgsBinderTests
{
    private static EverythingArgs BindArgv(params string[] argv)
    {
        var result = ArgsBinder.Bind(Fixtures.Everything, argv);
        Assert.That(result.Errors, Is.Empty);
        return (EverythingArgs)result.Args!;
    }

    private static IReadOnlyList<string> ArgvErrors(params string[] argv) => ArgsBinder.Bind(Fixtures.Everything, argv).Errors;

    private static IReadOnlyList<string> JsonErrors(string json) => ArgsBinder.BindJson(Fixtures.Everything, json).Errors;

    [Test]
    public void Argv_BindsEveryType()
    {
        var id = Guid.NewGuid();
        var a = BindArgv("AAPL", "x", "y", "--active", "-n", "5", "--big=9000000000", "--max-price", "$1,250.50",
            "--ratio", "-0.25", "--id", id.ToString(), "--on", "2026-10-30", "--at", "9:30", "--side", "short-sale",
            "--maybe-side", "SHORT_SALE", "--symbols", "MSFT,NVDA", "--symbols", "TSLA", "--note", "hello world");
        Assert.Multiple(() =>
        {
            Assert.That(a.Ticker, Is.EqualTo("AAPL"));
            Assert.That(a.Tags, Is.EqualTo(new[] { "x", "y" }));
            Assert.That(a.Active, Is.True);
            Assert.That(a.Limit, Is.EqualTo(5));
            Assert.That(a.Big, Is.EqualTo(9_000_000_000L));
            Assert.That(a.MaxPrice, Is.EqualTo(1250.50m));
            Assert.That(a.Ratio, Is.EqualTo(-0.25));
            Assert.That(a.Id, Is.EqualTo(id));
            Assert.That(a.On, Is.EqualTo(new DateOnly(2026, 10, 30)));
            Assert.That(a.At, Is.EqualTo(new TimeOnly(9, 30)));
            Assert.That(a.Side, Is.EqualTo(TradeSide.ShortSale));
            Assert.That(a.MaybeSide, Is.EqualTo(TradeSide.ShortSale));
            Assert.That(a.Symbols, Is.EqualTo(new[] { "MSFT", "NVDA", "TSLA" }));
            Assert.That(a.Note, Is.EqualTo("hello world"));
        });
    }

    [Test]
    public void Argv_UnsuppliedKeepsRecordDefaults()
    {
        var a = BindArgv("AAPL");
        Assert.Multiple(() =>
        {
            Assert.That(a.Limit, Is.EqualTo(50));
            Assert.That(a.Ratio, Is.EqualTo(0.5));
            Assert.That(a.Side, Is.EqualTo(TradeSide.Buy));
            Assert.That(a.Tags, Is.Empty);
            Assert.That(a.MaxPrice, Is.Null);
        });
    }

    [Test]
    public void Argv_FlagForms()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BindArgv("A", "-a").Active, Is.True);
            Assert.That(BindArgv("A", "--active=false").Active, Is.False);
            Assert.That(BindArgv("A", "--no-active").Active, Is.False);
            Assert.That(BindArgv("--", "-weird-ticker").Ticker, Is.EqualTo("-weird-ticker"));
            Assert.That(BindArgv("A", "--max_price", "3").MaxPrice, Is.EqualTo(3m), "snake spelling is accepted on the CLI too");
        });
    }

    [Test]
    public void Argv_Errors_ArePrecise()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ArgvErrors(), Has.Some.EqualTo("Missing required argument <ticker>."));
            Assert.That(ArgvErrors("A", "--activ"), Has.Some.Contains("Unknown option '--activ'. Did you mean --active?"));
            Assert.That(ArgvErrors("A", "--zzzzzzzz"), Has.Some.Contains("Known: --active"));
            Assert.That(ArgvErrors("A", "--limit", "ten"), Has.Some.EqualTo("--limit expects a whole number, got 'ten'."));
            Assert.That(ArgvErrors("A", "--side", "long"), Has.Some.EqualTo("--side must be one of buy, short-sale; got 'long'."));
            Assert.That(ArgvErrors("A", "--limit"), Has.Some.Contains("--limit needs a value"));
            Assert.That(ArgvErrors("A", "--limit", "--active"), Has.Some.Contains("--limit needs a value"));
            Assert.That(ArgvErrors("A", "--limit", "1", "--limit", "2"), Has.Some.Contains("more than once"));
            Assert.That(ArgvErrors("A", "-xyz"), Has.Some.Contains("Unknown option '-xyz'"));
            Assert.That(ArgvErrors("A", "--at", "25:00"), Has.Some.Contains("ET time of day"));
            Assert.That(ArgvErrors("A", "--on", "10/30/2026"), Has.Some.Contains("yyyy-MM-dd"));
        });
    }

    [Test]
    public void Argv_TooManyPositionals()
    {
        var errors = ArgsBinder.Bind(Fixtures.Order, ["AAPL", "MSFT", "--qty", "1"]).Errors;
        Assert.That(errors, Has.Some.Contains("Unexpected argument 'MSFT'"));
    }

    [Test]
    public void Argv_PositionalRecord_UsesConstructorDefaults()
    {
        var result = ArgsBinder.Bind(Fixtures.Order, ["AAPL", "--qty", "3"]);
        Assert.That(result.Success, Is.True, string.Join("; ", result.Errors));
        Assert.That(result.Args, Is.EqualTo(new OrderArgs("AAPL", 3, null, TradeSide.ShortSale)));
    }

    [Test]
    public void Json_BindsNativeAndStringifiedValues_ByAnySpelling()
    {
        var result = ArgsBinder.BindJson(Fixtures.Everything,
            """{"ticker":"AAPL","tags":["x"],"active":true,"limit":"7","MaxPrice":12.5,"maybeSide":"short_sale","symbols":"A,B","at":"15:55","note":null}""");
        Assert.That(result.Success, Is.True, string.Join("; ", result.Errors));
        var a = (EverythingArgs)result.Args!;
        Assert.Multiple(() =>
        {
            Assert.That(a.Limit, Is.EqualTo(7));
            Assert.That(a.MaxPrice, Is.EqualTo(12.5m));
            Assert.That(a.MaybeSide, Is.EqualTo(TradeSide.ShortSale));
            Assert.That(a.Symbols, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(a.At, Is.EqualTo(new TimeOnly(15, 55)));
            Assert.That(a.Note, Is.Null);
            Assert.That(a.Tags, Is.EqualTo(new[] { "x" }));
        });
    }

    [Test]
    public void Json_Errors_ArePrecise()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JsonErrors("{}"), Has.Some.EqualTo("Missing required 'ticker' (string)."));
            Assert.That(JsonErrors("""{"ticker":"A","limt":1}"""), Has.Some.Contains("Unknown option 'limt'. Did you mean limit?"));
            Assert.That(JsonErrors("""{"ticker":"A","limit":1.5}"""), Has.Some.EqualTo("'limit' expects a whole number, got '1.5'."));
            Assert.That(JsonErrors("""{"ticker":"A","active":"maybe"}"""), Has.Some.EqualTo("'active' expects true or false, got 'maybe'."));
            Assert.That(JsonErrors("""{"ticker":"A","side":"sell"}"""), Has.Some.EqualTo("'side' must be one of buy, short-sale; got 'sell'."));
            Assert.That(JsonErrors("""{"ticker":"A","tags":[1]}"""), Has.Some.Contains("array of strings"));
            Assert.That(JsonErrors("""{"ticker":"A","limit":true}"""), Has.Some.Contains("got true"));
            Assert.That(JsonErrors("[1]"), Has.Some.Contains("must be a JSON object"));
            Assert.That(JsonErrors("{nope"), Has.Some.Contains("not valid JSON"));
        });
    }

    [Test]
    public void Json_NullOrEmpty_IsNoArgs()
    {
        Assert.That(ArgsBinder.Bind(Fixtures.Catalog().Find("test whoami")!, default(JsonElement)).Success, Is.True);
        Assert.That(ArgsBinder.BindJson(Fixtures.Catalog().Find("test whoami")!, "").Success, Is.True);
    }

    [Test]
    public void UsageResult_IsExit2WithSynopsis()
    {
        var result = ArgsBinder.Bind(Fixtures.Order, []).ToUsageResult(Fixtures.Order);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Message, Does.Contain("Missing required option --qty <int>.").And.Contain("Usage: idiotproof test order"));
    }

    [Test]
    public void Split_HandlesQuotesAndEscapes()
    {
        Assert.That(ArgvParser.Split("strategy add \"Gap and go\" --note 'it''s' --x \"a \\\"b\\\"\""),
            Is.EqualTo(new[] { "strategy", "add", "Gap and go", "--note", "its", "--x", "a \"b\"" }));
        Assert.Throws<FormatException>(() => ArgvParser.Split("a \"b"));
    }

    [Test]
    public void ExtractGlobals_PeelsFrontDoorFlags()
    {
        var user = Guid.NewGuid();
        var x = ArgvParser.ExtractGlobals(["--json", "test", "order", "AAPL", "--user", user.ToString(), "--confirm=K1", "-h", "--qty", "1"]);
        Assert.Multiple(() =>
        {
            Assert.That(x.Error, Is.Null);
            Assert.That(x.Flags.Json, Is.True);
            Assert.That(x.Flags.Help, Is.True);
            Assert.That(x.Flags.User, Is.EqualTo(user));
            Assert.That(x.Flags.Confirm, Is.EqualTo("K1"));
            Assert.That(x.Remaining, Is.EqualTo(new[] { "test", "order", "AAPL", "--qty", "1" }));
        });
        Assert.That(ArgvParser.ExtractGlobals(["--user", "bob"]).Error, Does.Contain("GUID"));
        Assert.That(ArgvParser.ExtractGlobals(["--", "--json"]).Flags.Json, Is.False);
    }
}
