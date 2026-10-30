using System.Text.Json;
using IdiotProof.Core.Identity;
using IdiotProof.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests;

/// <summary>Dispatch: scope, binding, owner, and mapping every outcome to a result (IP-T55).</summary>
public class OperationDispatcherTests
{
    private static ServiceProvider Provider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new Fixtures.FixedClock(Fixtures.FixedClock.MarketOpen));
        services.AddIdiotProofOperations([.. Fixtures.Valid, typeof(BuiltIn.HubPingOperation), typeof(BuiltIn.AgentCatalogOperation)]);
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static readonly OperationInvocation Cli = new() { Caller = OperationCaller.Test };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Test]
    public async Task Dispatch_Json_RunsTheOperation()
    {
        using var sp = Provider();
        var result = await sp.GetRequiredService<OperationDispatcher>()
            .DispatchAsync("test_everything", Json("""{"ticker":"AAPL","limit":3}"""), Cli);
        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.That(result.Human, Is.EqualTo("AAPL:3"));
    }

    [Test]
    public async Task Dispatch_Argv_ResolvesNameFromTokens()
    {
        using var sp = Provider();
        var result = await sp.GetRequiredService<OperationDispatcher>()
            .DispatchArgvAsync(["test", "everything", "MSFT", "-n", "9"], Cli);
        Assert.That(result.Human, Is.EqualTo("MSFT:9"));
    }

    [Test]
    public async Task Dispatch_UnknownOperation_IsUsageWithSuggestion()
    {
        using var sp = Provider();
        var dispatcher = sp.GetRequiredService<OperationDispatcher>();
        var result = await dispatcher.DispatchAsync("test everythin", Array.Empty<string>(), Cli);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Message, Does.Contain("Did you mean 'test everything'?"));
        Assert.That((await dispatcher.DispatchArgvAsync(["zzz"], Cli)).ExitCode, Is.EqualTo(2));
    }

    [Test]
    public async Task Dispatch_BindError_IsUsage()
    {
        using var sp = Provider();
        var result = await sp.GetRequiredService<OperationDispatcher>().DispatchAsync("test everything", ["A", "--limit", "x"], Cli);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Message, Does.Contain("--limit expects a whole number"));
    }

    [TestCase("crash", OperationStatus.Failed, "test fail failed: kaboom")]
    [TestCase("usage", OperationStatus.UsageError, "bad usage")]
    [TestCase("blocked", OperationStatus.Blocked, "market closed")]
    [TestCase("soft", OperationStatus.Failed, "failed softly")]
    [TestCase("null", OperationStatus.Failed, "test fail returned no result.")]
    public async Task Dispatch_MapsOutcomes(string mode, OperationStatus status, string message)
    {
        using var sp = Provider();
        var result = await sp.GetRequiredService<OperationDispatcher>().DispatchAsync("test fail", ["--mode", mode], Cli);
        Assert.That(result.Status, Is.EqualTo(status));
        Assert.That(result.Message, Is.EqualTo(message));
        Assert.That(result.Render(OutputMode.Human), Does.Not.Contain("Secret.Stack"), "no stack trace in human output");
    }

    [Test]
    public async Task Dispatch_Cancelled_IsFailed()
    {
        using var sp = Provider();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var result = await sp.GetRequiredService<OperationDispatcher>().DispatchAsync("test fail", ["--mode", "cancel"], Cli, cts.Token);
        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("cancelled"));
    }

    [Test]
    public async Task Dispatch_Confirmation_RoundTrip()
    {
        using var sp = Provider();
        var dispatcher = sp.GetRequiredService<OperationDispatcher>();
        var first = await dispatcher.DispatchAsync("test order", ["AAPL", "--qty", "1"], Cli);
        Assert.That(first.ExitCode, Is.EqualTo(4));
        Assert.That(first.Confirmation!.ExpiresAtUtc, Is.EqualTo(new DateTime(2026, 10, 30, 13, 36, 0, DateTimeKind.Utc)));

        var second = await dispatcher.DispatchAsync("test order", ["AAPL", "--qty", "1"], Cli with { ConfirmationCode = first.Confirmation.Code });
        Assert.That(second.IsOk, Is.True);
    }

    [Test]
    public async Task Owner_ExplicitWins_ThenHostOwnerContext_ElseUsage()
    {
        var hostOwner = Guid.NewGuid();
        var explicitOwner = Guid.NewGuid();

        using (var sp = Provider())
        {
            var d = sp.GetRequiredService<OperationDispatcher>();
            var none = await d.DispatchAsync("test whoami", [], Cli with { OwnerError = OwnerResolver.AmbiguousMessage });
            Assert.That(none.ExitCode, Is.EqualTo(2));
            Assert.That(none.Message, Is.EqualTo(OwnerResolver.AmbiguousMessage));
            Assert.That((await d.DispatchAsync("test whoami", [], Cli with { OwnerId = explicitOwner })).Human, Is.EqualTo(explicitOwner.ToString()));
        }

        using (var sp = Provider(s => s.AddSingleton<IOwnerContext>(new FixedOwnerContext(hostOwner))))
        {
            var d = sp.GetRequiredService<OperationDispatcher>();
            Assert.That((await d.DispatchAsync("test whoami", [], Cli)).Human, Is.EqualTo(hostOwner.ToString()));
            Assert.That((await d.DispatchAsync("test whoami", [], Cli with { OwnerId = explicitOwner })).Human, Is.EqualTo(explicitOwner.ToString()));
        }
    }

    [Test]
    public async Task OperationOwnerContext_SeesTheDispatchedOwner()
    {
        var owner = Guid.NewGuid();
        using var sp = Provider(s => s.AddScoped<IOwnerContext, OperationOwnerContext>());
        var result = await sp.GetRequiredService<OperationDispatcher>().DispatchAsync("test whoami", [], Cli with { OwnerId = owner });
        Assert.That(result.Human, Is.EqualTo(owner.ToString()));

        var none = await sp.GetRequiredService<OperationDispatcher>().DispatchAsync("test whoami", [], Cli);
        Assert.That(none.ExitCode, Is.EqualTo(2), "the holder never resolves itself into an owner");
    }

    [Test]
    public async Task HubPing_ReportsEtClock()
    {
        using var sp = Provider();
        var result = await sp.GetRequiredService<OperationDispatcher>().DispatchAsync("hub ping", [], Cli);
        Assert.That(result.Human, Is.EqualTo("pong — 2026-10-30 9:31 AM ET"));
        Assert.That(result.ToJson(), Does.Contain("\"now\":\"2026-10-30T09:31:00-04:00\"").And.Contain("\"caller\":\"test\""));
    }

    [Test]
    public async Task AgentCatalog_ListsOperations_AndOneOperationHelp()
    {
        using var sp = Provider();
        var d = sp.GetRequiredService<OperationDispatcher>();
        var all = await d.DispatchAsync("agent catalog", [], Cli);
        Assert.That(all.Human, Does.Contain("hub ping").And.Contain("test order"));
        Assert.That(all.ToJson(), Does.Contain("\"tool_name\":\"test_order\""));

        var one = await d.DispatchAsync("agent catalog", ["-o", "test_order"], Cli);
        Assert.That(one.Human, Does.Contain("Usage: idiotproof test order"));

        var bad = await d.DispatchAsync("agent catalog", ["--operation", "test ordr"], Cli);
        Assert.That(bad.ExitCode, Is.EqualTo(2));
        Assert.That(bad.Message, Does.Contain("Did you mean 'test order'?"));
    }

    [Test]
    public void AddIdiotProofOperations_FromAssembly_IncludesBuiltIns()
    {
        using var sp = new ServiceCollection().AddIdiotProofOperations().BuildServiceProvider();
        var catalog = sp.GetRequiredService<OperationCatalog>();
        Assert.That(catalog.Find("hub ping"), Is.Not.Null);
        Assert.That(catalog.Find("agent catalog"), Is.Not.Null);
    }
}
