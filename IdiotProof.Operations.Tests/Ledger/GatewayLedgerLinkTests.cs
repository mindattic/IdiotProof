using IdiotProof.Core.Data;
using IdiotProof.Brokers;
using IdiotProof.Core.Ledger;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Ledger;

/// <summary>
/// IP-T93 end to end, the way the Hub runs it: the dispatcher opens a CommandScope, the operation's
/// gateway call writes its audit row into it, and the ledger row appended afterwards (same async
/// flow, after the operation returned) is stamped onto that audit row as command_ledger_id.
/// </summary>
public class GatewayLedgerLinkTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    [Test]
    public async Task HubFlow_TheLedgerRowIsStampedOntoTheGatewaysAuditRow()
    {
        var (dispatcher, ledger, db) = Build();

        var result = await dispatcher.DispatchAsync("test exit", Array.Empty<string>(), new OperationInvocation { OwnerId = Owner });
        var ledgerId = await ledger.AppendAsync(new CommandLedgerEntry { OwnerUserId = Owner, Operation = "test exit", Caller = "cli", Status = result.Status.ToString() });

        Assert.That(result.IsOk, Is.True, result.Message);
        await using var ctx = await db.CreateDbContextAsync();
        var audit = ctx.AuditLogs.Single(a => a.Category == "order");
        Assert.That(audit.DataJson, Does.StartWith($"{{\"command_ledger_id\":{ledgerId},"));
        Assert.That(audit.DataJson, Does.Contain("\"operation\":\"test exit\"").And.Contain("\"caller\":\"cli\""));
        Assert.That(System.Text.Json.JsonDocument.Parse(audit.DataJson!).RootElement.GetProperty("command_ledger_id").GetInt64(), Is.EqualTo(ledgerId));
    }

    [Test]
    public async Task ALedgerRowForAnotherOperation_DoesNotClaimTheAuditRows()
    {
        var (dispatcher, ledger, db) = Build();

        await dispatcher.DispatchAsync("test exit", Array.Empty<string>(), new OperationInvocation { OwnerId = Owner });
        await ledger.AppendAsync(new CommandLedgerEntry { OwnerUserId = Owner, Operation = "orders list", Caller = "cli", Status = "Ok" });

        await using var ctx = await db.CreateDbContextAsync();
        Assert.That(ctx.AuditLogs.Single(a => a.Category == "order").DataJson, Does.Not.Contain("command_ledger_id"));
    }

    [TestCase(null, "{\"command_ledger_id\":7}")]
    [TestCase("{}", "{\"command_ledger_id\":7}")]
    [TestCase("{\"a\":1}", "{\"command_ledger_id\":7,\"a\":1}")]
    [TestCase("[full message] x", "{\"command_ledger_id\":7,\"text\":\"[full message] x\"}")]
    public void WithLedgerId_KeepsTheRowValidJson(string? before, string after) =>
        Assert.That(CommandLedgerRepository.WithLedgerId(before, 7), Is.EqualTo(after));

    private static (OperationDispatcher, CommandLedgerRepository, IDbContextFactory<AppDbContext>) Build()
    {
        var db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"ledger-link-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        var audit = new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance);
        var gateway = new TransactionGateway(new SandboxOnly(), new SwitchableMarketDataFeed(), new RiskGuardianService(db),
            new ConfirmationStore(db, TimeProvider.System), audit, NullLogger<TransactionGateway>.Instance);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(gateway)
            .AddIdiotProofOperations([typeof(GatewayExitOperation)])
            .BuildServiceProvider();
        var ledger = new CommandLedgerRepository(db, NullLogger<CommandLedgerRepository>.Instance);
        return (services.GetRequiredService<OperationDispatcher>(), ledger, db);
    }

    private sealed class SandboxOnly : IOrderRouting
    {
        private readonly SandboxBrokerClient sandbox = new();
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) => Task.FromResult<IBrokerClient>(sandbox);
    }
}

/// <summary>Fixture: one Sandbox exit through the gateway (handed to the catalog by type, never discovered).</summary>
[Operation("test exit", Summary = "Sell 1 NVDA in the Sandbox through the gateway.", Risk = OperationRisk.Money)]
[Example("idiotproof test exit")]
public sealed class GatewayExitOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var outcome = await ctx.Get<TransactionGateway>().SubmitAsync(new OrderIntent
        {
            OwnerUserId = ctx.RequireOwner(), Mode = BrokerMode.Sandbox, Purpose = OrderPurpose.Exit, Origin = OrderOrigin.Manual,
            Order = new OrderRequest { Symbol = "NVDA", Quantity = 1, Side = OrderSide.Sell, Type = OrderType.Market, TimeInForce = "DAY" },
        }, new GatewayOptions { Caller = "cli" }, ctx.CancellationToken);
        return outcome.Status == GatewayStatus.Placed ? OperationResult.Ok(new { outcome.Message }) : OperationResult.Fail(outcome.Message);
    }
}
