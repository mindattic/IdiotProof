using System.Net;
using System.Text;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Ledger;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>
/// options exercise / do-not-exercise (IP-T307) against a scripted Alpaca handler: Paper executes,
/// Live waits for a code bound to the exact request, Sandbox and shorts are refused, and every
/// attempt is audited. Nothing reaches a real account.
/// </summary>
public class OptionsExerciseOperationTests
{
    private const string Call = "AAPL261219C00150000";
    private const string PositionPath = "/v2/positions/" + Call;
    private static readonly Guid Owner = Guid.NewGuid();

    private ScriptedFactory alpaca = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private ConfirmationStore confirmations = null!;

    [SetUp]
    public void SetUp()
    {
        alpaca = new ScriptedFactory();
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"options-exercise-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        confirmations = new ConfirmationStore(db, TimeProvider.System);
        foreach (var h in new[] { alpaca.Paper, alpaca.Live })
        {
            h.Routes["GET " + PositionPath] = (HttpStatusCode.OK, $$"""{"symbol":"{{Call}}","qty":"2","asset_class":"us_option"}""");
            h.Routes["POST " + PositionPath + "/exercise"] = (HttpStatusCode.OK, "");
            h.Routes["POST " + PositionPath + "/do-not-exercise"] = (HttpStatusCode.OK, "");
        }
    }

    private OperationContext Context(string? code = null) => new()
    {
        Services = new ServiceCollection()
            .AddSingleton(confirmations)
            .AddSingleton(new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance))
            .AddSingleton<AlpacaApiFactory>(alpaca)
            // Instructions only need the confirmations, the audit log and Alpaca access (IP-T337).
            .AddSingleton(sp => new TransactionGateway(null!, null!, null!, confirmations,
                sp.GetRequiredService<AuditLogRepository>(), NullLogger<TransactionGateway>.Instance, null, alpaca))
            .BuildServiceProvider(),
        OwnerId = Owner,
        ConfirmationCode = code,
    };

    private async Task<List<string>> AuditAsync()
    {
        await using var ctx = await db.CreateDbContextAsync();
        return await ctx.AuditLogs.Select(a => a.Message).ToListAsync();
    }

    [Test]
    public async Task Exercise_Paper_PostsToThePosition_PreviewsTheDelivery_AndAudits()
    {
        var result = await new OptionsExerciseOperation().ExecuteAsync(new OptionsExerciseArgs { Occ = Call }, Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(alpaca.Paper.Calls, Is.EqualTo(new[] { "GET " + PositionPath, "POST " + PositionPath + "/exercise" }));
        Assert.That(result.Human, Does.Contain("EXERCISE 2 AAPL $150 Call").And.Contain("buys 200 AAPL at $150.00 ($30,000.00 cash out)"));
        Assert.That(await AuditAsync(), Has.One.StartsWith("Placed: EXERCISE 2"));
    }

    [Test]
    public async Task DoNotExercise_Live_NeedsACode_ThenTheSameCallWithTheCodeSendsIt()
    {
        var args = new OptionsExerciseArgs { Occ = Call, Mode = BrokerMode.Live };

        var first = await new OptionsDoNotExerciseOperation().ExecuteAsync(args, Context());
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);
        Assert.That(alpaca.Live.Calls, Has.None.StartsWith("POST"));

        var second = await new OptionsDoNotExerciseOperation().ExecuteAsync(args, Context(first.Confirmation!.Code));
        Assert.That(second.IsOk, Is.True, second.Message);
        Assert.That(alpaca.Live.Calls, Has.One.EqualTo("POST " + PositionPath + "/do-not-exercise"));

        var reuse = await new OptionsDoNotExerciseOperation().ExecuteAsync(args, Context(first.Confirmation!.Code));
        Assert.That(reuse.Status, Is.EqualTo(OperationStatus.Blocked));
    }

    [Test]
    public async Task ACodeIssuedForDoNotExercise_DoesNotAuthorizeAnExercise()
    {
        var args = new OptionsExerciseArgs { Occ = Call, Mode = BrokerMode.Live };
        var first = await new OptionsDoNotExerciseOperation().ExecuteAsync(args, Context());

        var swapped = await new OptionsExerciseOperation().ExecuteAsync(args, Context(first.Confirmation!.Code));

        Assert.That(swapped.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(alpaca.Live.Calls, Has.None.StartsWith("POST"));
    }

    [Test]
    public async Task Sandbox_AShort_AndNoPosition_AreRefusedWithoutAPost()
    {
        var sandbox = await new OptionsExerciseOperation().ExecuteAsync(new OptionsExerciseArgs { Occ = Call, Mode = BrokerMode.Sandbox }, Context());
        Assert.That(sandbox.Status, Is.EqualTo(OperationStatus.UsageError));

        alpaca.Paper.Routes["GET " + PositionPath] = (HttpStatusCode.OK, """{"qty":"-1"}""");
        var shortHeld = await new OptionsExerciseOperation().ExecuteAsync(new OptionsExerciseArgs { Occ = Call }, Context());
        Assert.That(shortHeld.Status, Is.EqualTo(OperationStatus.Blocked));

        alpaca.Paper.Routes["GET " + PositionPath] = (HttpStatusCode.NotFound, """{"code":40410000,"message":"position does not exist"}""");
        var none = await new OptionsExerciseOperation().ExecuteAsync(new OptionsExerciseArgs { Occ = Call }, Context());
        Assert.That(none.Status, Is.EqualTo(OperationStatus.Blocked));

        Assert.That(alpaca.Paper.Calls, Has.None.StartsWith("POST"));
    }

    [Test]
    public async Task AnAlpacaRefusal_IsAFailure_AndAudited()
    {
        alpaca.Paper.Routes["POST " + PositionPath + "/exercise"] = (HttpStatusCode.Forbidden, """{"code":40310000,"message":"option is out of the money"}""");

        var result = await new OptionsExerciseOperation().ExecuteAsync(new OptionsExerciseArgs { Occ = Call }, Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("out of the money"));
        Assert.That(await AuditAsync(), Has.One.StartsWith("Rejected:"));
    }

    [Test]
    public async Task TheExercise_GoesThroughTheGateway_AndItsAuditRowIsLinkedToTheCommandLedger()
    {
        CommandScope.Begin("options exercise", "cli");

        var result = await new OptionsExerciseOperation().ExecuteAsync(new OptionsExerciseArgs { Occ = Call, Note = "ITM at expiry" }, Context());
        var ledgerId = await new CommandLedgerRepository(db, NullLogger<CommandLedgerRepository>.Instance)
            .AppendAsync(new CommandLedgerEntry { OwnerUserId = Owner, Operation = "options exercise", Caller = "cli", Status = "Ok" });

        Assert.That(result.IsOk, Is.True, result.Message);
        await using var ctx = await db.CreateDbContextAsync();
        var row = ctx.AuditLogs.Single();
        Assert.That(row.Category, Is.EqualTo("order"));
        Assert.That(row.DataJson, Does.StartWith($"{{\"command_ledger_id\":{ledgerId},"));
        Assert.That(row.DataJson, Does.Contain("\"action\":\"exercise\"").And.Contain("ITM at expiry").And.Contain("\"operation\":\"options exercise\""));
    }

    /// <summary>Replies by "METHOD /path"; records every request. Nothing reaches the network.</summary>
    private sealed class Scripted : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, string Body)> Routes { get; } = [];
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            Calls.Add(key);
            var (status, body) = Routes.TryGetValue(key, out var r) ? r : throw new InvalidOperationException("No scripted reply for " + key);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ScriptedFactory() : AlpacaApiFactory(null!, null!, null!)
    {
        public Scripted Paper { get; } = new();
        public Scripted Live { get; } = new();

        public override Task<AlpacaApi> ForOwnerAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct = default) =>
            mode == BrokerMode.Sandbox
                ? throw new AlpacaAccessException("Sandbox is not Alpaca.")
                : Task.FromResult(new AlpacaApi("k", "s", mode == BrokerMode.Paper, mode == BrokerMode.Paper ? Paper : Live));
    }
}
