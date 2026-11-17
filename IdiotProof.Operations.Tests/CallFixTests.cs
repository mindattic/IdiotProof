using System.Text.Json;
using IdiotProof.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests;

/// <summary>
/// IP-T523: a binding failure carries the corrected call as data (command line, argv and args
/// object) when every problem has one determinable correction; otherwise it carries none.
/// </summary>
public class CallFixTests
{
    private static BindResult Argv(params string[] argv) => ArgsBinder.Bind(Fixtures.Everything, argv);

    private static BindResult Json(string json) => ArgsBinder.BindJson(Fixtures.Everything, json);

    private static string ArgsJson(CallFix fix) => fix.Args.ToJsonString();

    [Test]
    public void Argv_MisspelledOption_CarriesTheCorrectedCall()
    {
        var r = Argv("A", "--activ");
        Assert.Multiple(() =>
        {
            Assert.That(r.Success, Is.False, "the call as sent is still refused");
            Assert.That(r.Problems.Single().Fix, Is.EqualTo(new CallChange("option", "active", "--activ", "--active")));
            Assert.That(r.Fix!.Operation, Is.EqualTo("test everything"));
            Assert.That(r.Fix.CommandLine, Is.EqualTo("idiotproof test everything A --active"));
            Assert.That(r.Fix.Argv, Is.EqualTo(new[] { "test", "everything", "A", "--active" }));
            Assert.That(ArgsJson(r.Fix), Is.EqualTo("""{"ticker":"A","active":true}"""));
        });
    }

    [Test]
    public void Argv_ValuesWithOneReading_AreCorrected()
    {
        var r = Argv("A", "--limit", "5.0", "--big", "1_000", "--on", "10/30/2026", "--at", "0930", "--side", "shrt-sale", "--active=y");
        Assert.Multiple(() =>
        {
            Assert.That(r.Fix, Is.Not.Null, string.Join("; ", r.Errors));
            Assert.That(r.Fix!.CommandLine, Is.EqualTo(
                "idiotproof test everything A --active --limit 5 --big 1000 --on 2026-10-30 --at 09:30 --side short-sale"));
            Assert.That(ArgsJson(r.Fix), Is.EqualTo(
                """{"ticker":"A","active":true,"limit":5,"big":1000,"on":"2026-10-30","at":"09:30","side":"short-sale"}"""));
            Assert.That(r.Fix.Changes.Select(c => (c.Kind, c.Parameter, c.From, c.To)), Is.EquivalentTo(new[]
            {
                ("value", (string?)"limit", (string?)"5.0", (string?)"5"),
                ("value", "big", "1_000", "1000"),
                ("value", "on", "10/30/2026", "2026-10-30"),
                ("value", "at", "0930", "09:30"),
                ("value", "side", "shrt-sale", "short-sale"),
                ("value", "active", "y", "true"),
            }));
        });
    }

    [TestCase("2026/10/30")]
    [TestCase("20261030")]
    [TestCase("Oct 30 2026")]
    [TestCase("October 30, 2026")]
    [TestCase("30/10/2026")]
    [TestCase("2026-10-30T09:31:00-04:00")]
    public void Argv_UnambiguousDates_AreCorrectedToIso(string typed) =>
        Assert.That(Argv("A", "--on", typed).Fix?.Args["on"]?.GetValue<string>(), Is.EqualTo("2026-10-30"));

    [Test]
    public void Argv_PositionalPassedByName_BecomesThePositional()
    {
        var r = Argv("--ticker", "AAPL", "--limit", "3");
        Assert.Multiple(() =>
        {
            Assert.That(r.Errors.Single(), Does.Contain("<ticker> is a positional argument"));
            Assert.That(r.Fix!.CommandLine, Is.EqualTo("idiotproof test everything AAPL --limit 3"));
            Assert.That(r.Fix.Changes.Single().Kind, Is.EqualTo("argument"));
        });
    }

    [Test]
    public void Json_MisspelledKeyAndStringifiedWholeNumber_AreCorrected()
    {
        var r = Json("""{"ticker":"A","limt":"7.0","note":"two words"}""");
        Assert.Multiple(() =>
        {
            Assert.That(r.Problems.Select(p => p.Fix!.Kind), Is.EqualTo(new[] { "option", "value" }));
            Assert.That(ArgsJson(r.Fix!), Is.EqualTo("""{"ticker":"A","limit":7,"note":"two words"}"""));
            Assert.That(r.Fix!.CommandLine, Is.EqualTo("idiotproof test everything A --limit 7 --note \"two words\""));
            Assert.That(ArgvParser.Split(r.Fix.CommandLine!).Skip(1), Is.EqualTo(r.Fix.Argv), "the command line splits back to the argv");
        });
    }

    [Test]
    public void TheCorrectedCall_BindsCleanly()
    {
        var fix = Argv("A", "--activ", "--limit", "5.0").Fix!;
        var again = ArgsBinder.Bind(Fixtures.Everything, JsonSerializer.SerializeToElement(fix.Args));
        Assert.That(again.Success, Is.True, string.Join("; ", again.Errors));
        Assert.That(((EverythingArgs)again.Args!).Limit, Is.EqualTo(5));
        Assert.That(ArgsBinder.Bind(Fixtures.Everything, fix.Argv!.Skip(2).ToArray()).Success, Is.True);
    }

    // The refusals stay refusals: no correction that picks a meaning.
    [TestCase("--max-price", "1,005", TestName = "A bare comma group is 1005 to some and 1.005 to others")]
    [TestCase("--max-price", "0,25", TestName = "A comma decimal")]
    [TestCase("--limit", "1,000", TestName = "A comma in a whole number")]
    [TestCase("--limit", "ten", TestName = "A word where a number belongs")]
    [TestCase("--limit", "1.5", TestName = "A fraction for a whole number")]
    [TestCase("--on", "03/04/2026", TestName = "A date that is March 4 or April 3")]
    [TestCase("--at", "25:00", TestName = "A time that does not exist")]
    [TestCase("--side", "long", TestName = "An enum value that is not a typo of one accepted value")]
    [TestCase("--id", "not-a-guid", TestName = "A GUID")]
    public void Argv_ValuesWithNoSingleReading_CarryNoFix(string option, string value)
    {
        var r = Argv("A", option, value);
        Assert.Multiple(() =>
        {
            Assert.That(r.Success, Is.False);
            Assert.That(r.Problems.Single().Fix, Is.Null);
            Assert.That(r.Fix, Is.Null);
        });
    }

    [Test]
    public void OneUncorrectableProblem_MeansNoCorrectedCall_ButTheOthersKeepTheirFix()
    {
        var r = Argv("A", "--activ", "--max-price", "1,005");
        Assert.Multiple(() =>
        {
            Assert.That(r.Problems.Count(p => p.Fix is not null), Is.EqualTo(1));
            Assert.That(r.Fix, Is.Null);
        });
        Assert.That(Argv().Fix, Is.Null, "a missing required argument has no value to supply");
        Assert.That(Argv("A", "--zzzzzzzz").Fix, Is.Null, "no option is close to that");
        Assert.That(Json("""{"limit":5}""").Fix, Is.Null);
    }

    [Test]
    public void UsageResult_CarriesTheFixAsData_AndPrintsItOutsideTheLedgeredMessage()
    {
        var result = ArgsBinder.Bind(Fixtures.Order, ["AAPL", "--qty", "3.0", "--side", "by"]).ToUsageResult(Fixtures.Order);
        var data = (UsageErrorData)result.Data!;
        var envelope = JsonDocument.Parse(result.ToJson()).RootElement.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(data.Operation, Is.EqualTo("test order"));
            Assert.That(data.Errors, Has.Count.EqualTo(2));
            Assert.That(data.Fix!.CommandLine, Is.EqualTo("idiotproof test order AAPL --qty 3 --side buy"));
            Assert.That(envelope.GetProperty("fix").GetProperty("args").GetRawText(), Is.EqualTo("""{"ticker":"AAPL","qty":3,"side":"buy"}"""));
            Assert.That(envelope.GetProperty("errors")[0].GetProperty("fix").GetProperty("to").GetString(), Is.EqualTo("3"));
            Assert.That(result.Message, Does.Not.Contain("Corrected call"), "the ledger keeps the message; the corrected call repeats every value");
            Assert.That(result.Human, Does.Contain("Corrected call (not run").And.Contain("idiotproof test order AAPL --qty 3 --side buy"));
            Assert.That(result.Render(OutputMode.Human), Does.Contain("Usage error: --qty expects a whole number, got '3.0'."));
        });
    }

    // ── the dispatcher: unknown operation names ──

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new Fixtures.FixedClock(Fixtures.FixedClock.MarketOpen));
        services.AddIdiotProofOperations([.. Fixtures.Valid, typeof(BuiltIn.HubPingOperation), typeof(BuiltIn.AgentCatalogOperation)]);
        return services.BuildServiceProvider();
    }

    private static readonly OperationInvocation Cli = new() { Caller = OperationCaller.Test };

    [Test]
    public async Task UnknownOperation_Argv_CarriesTheCorrectedCall_AndDoesNotRunIt()
    {
        using var sp = Provider();
        var result = await sp.GetRequiredService<OperationDispatcher>().DispatchArgvAsync(["test", "everythin", "MSFT", "-n", "9"], Cli);
        var data = (UsageErrorData)result.Data!;
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), "the corrected call is data, not something the dispatcher ran");
            Assert.That(result.Message, Is.EqualTo("Unknown operation 'test everythin'. Did you mean 'test everything'?"));
            Assert.That(data.Errors.Single().Fix, Is.EqualTo(new CallChange("operation", null, "test everythin", "test everything")));
            Assert.That(data.Fix!.CommandLine, Is.EqualTo("idiotproof test everything MSFT --limit 9"));
            Assert.That(data.Fix.Args.ToJsonString(), Is.EqualTo("""{"ticker":"MSFT","limit":9}"""));
            Assert.That(result.Human, Does.Contain("idiotproof test everything MSFT --limit 9"));
        });
    }

    [Test]
    public async Task UnknownOperation_Json_CarriesTheCorrectedCall_WithItsArgumentCorrections()
    {
        using var sp = Provider();
        var args = JsonDocument.Parse("""{"ticker":"AAPL","limt":3}""").RootElement.Clone();
        var result = await sp.GetRequiredService<OperationDispatcher>().DispatchAsync("test_everythin", args, Cli);
        var fix = ((UsageErrorData)result.Data!).Fix!;
        Assert.Multiple(() =>
        {
            Assert.That(fix.Operation, Is.EqualTo("test everything"));
            Assert.That(fix.Args.ToJsonString(), Is.EqualTo("""{"ticker":"AAPL","limit":3}"""));
            Assert.That(fix.Changes.Select(c => c.Kind), Is.EqualTo(new[] { "operation", "option" }));
        });
    }

    [Test]
    public async Task UnknownOperation_WhoseArgumentsDoNotBind_OrWithNoCloseName_CarriesNoCorrectedCall()
    {
        using var sp = Provider();
        var dispatcher = sp.GetRequiredService<OperationDispatcher>();

        var unbound = await dispatcher.DispatchArgvAsync(["test", "ordr", "AAPL"], Cli); // --qty is required
        Assert.That(unbound.Message, Does.Contain("Did you mean 'test order'?"));
        Assert.That(((UsageErrorData)unbound.Data!).Fix, Is.Null);

        var unknown = await dispatcher.DispatchArgvAsync(["zzz"], Cli);
        Assert.That(unknown.ExitCode, Is.EqualTo(2));
        Assert.That(((UsageErrorData)unknown.Data!).Fix, Is.Null);
        Assert.That(unknown.Human, Is.Null);
    }
}
