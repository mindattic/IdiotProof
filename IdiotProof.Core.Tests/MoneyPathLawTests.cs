using System.Text.RegularExpressions;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The two money-path laws (docs/BIBLE.md, IP-T99). IP-LAW-12: every order reaches a broker
/// through the TransactionGateway — no production code outside the broker adapters calls a
/// broker's place / replace / cancel / cancel-all itself, and nothing outside the gateway can
/// mark a Live order confirmed. IP-LAW-13: Live from the CLI or MCP needs a confirm code — every
/// person-initiated Live action is held for a code, and only the Monitor (whose Live strategy
/// activation was the confirmed act) and the web's password elevation are exempt.
/// </summary>
[TestFixture]
public sealed class MoneyPathLawTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IdiotProof.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (IdiotProof.slnx) not found.");
    }

    /// <summary>Production sources: every .cs/.razor outside test projects, bin/obj and the broker adapters themselves.</summary>
    private static IEnumerable<string> ProductionSources(string root)
    {
        var sep = Path.DirectorySeparatorChar;
        foreach (var project in Directory.EnumerateDirectories(root, "IdiotProof.*"))
        {
            var name = Path.GetFileName(project);
            if (name.EndsWith(".Tests", StringComparison.Ordinal) || name == "IdiotProof.Brokers") continue;
            foreach (var file in Directory.EnumerateFiles(project, "*.*", SearchOption.AllDirectories))
            {
                if (!(file.EndsWith(".cs") || file.EndsWith(".razor"))) continue;
                if (file.Contains($"{sep}bin{sep}") || file.Contains($"{sep}obj{sep}")) continue;
                yield return file;
            }
        }
    }

    private static bool IsComment(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("@*") || t.StartsWith("/*");
    }

    [Test]
    public void Law12_NoProductionCode_CallsABrokerMutator_ButTheGateway()
    {
        var root = RepoRoot();
        var gateway = Path.Combine(root, "IdiotProof.Core", "Trading", "TransactionGateway.cs");
        var mutator = new Regex(@"(?<receiver>\w+)\s*\.\s*(PlaceOrderAsync|ReplaceOrderAsync|CancelOrderAsync|CancelAllOrdersAsync)\s*\(");
        var offenders = new List<string>();
        foreach (var file in ProductionSources(root).Where(f => !string.Equals(f, gateway, StringComparison.OrdinalIgnoreCase)))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (IsComment(lines[i])) continue;
                foreach (Match m in mutator.Matches(lines[i]))
                    // OptionsTradingService.PlaceOrderAsync is itself the gateway path for the web ticket.
                    if (m.Groups["receiver"].Value is not ("OptionsSvc" or "OptionsTradingService"))
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.That(offenders, Is.Empty, "Send the order through TransactionGateway (IP-LAW-12).");
    }

    [Test]
    public void Law12_OnlyTheGateway_CanMarkLiveConfirmed()
    {
        var root = RepoRoot();
        var gateway = Path.Combine(root, "IdiotProof.Core", "Trading", "TransactionGateway.cs");
        var setter = new Regex(@"\b(LiveConfirmed|ConfirmedBy|ElevationOnly)\s*=");
        var offenders = ProductionSources(root)
            .Concat(Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => f.Contains(".Tests") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            .Where(f => !string.Equals(f, gateway, StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, i)))
            .Where(x => !IsComment(x.line) && setter.IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(root, x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();

        Assert.That(offenders, Is.Empty, "Use ConfirmAsync / ConfirmBatchAsync / SubmitElevatedAsync (IP-LAW-12).");
    }

    // ── IP-LAW-13 ────────────────────────────────────────────────────────────

    private Broker live = null!;
    private TransactionGateway gateway = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private static readonly Guid Owner = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"law13-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        live = new Broker();
        gateway = new TransactionGateway(new LiveRouting(live), new SwitchableMarketDataFeed(), new RiskGuardianService(db),
            new ConfirmationStore(db, TimeProvider.System), new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance),
            NullLogger<TransactionGateway>.Instance);
    }

    private static OrderIntent LiveIntent(OrderOrigin origin, OrderPurpose purpose, OrderAction action = OrderAction.Place) => new()
    {
        OwnerUserId = Owner, Mode = BrokerMode.Live, Origin = origin, Purpose = purpose, Action = action,
        TargetOrderId = action is OrderAction.Cancel or OrderAction.Replace ? "ord-9" : null,
        StopLossPrice = 98m, ReferencePrice = 100m,
        Order = action == OrderAction.Place
            ? new OrderRequest { Symbol = "NVDA", Quantity = 1, Side = purpose == OrderPurpose.Entry ? OrderSide.Buy : OrderSide.Sell, Type = OrderType.Limit, LimitPrice = 100m }
            : new OrderRequest { Symbol = "NVDA", LimitPrice = 101m, TimeInForce = "" },
    };

    [TestCase(OrderOrigin.Manual, OrderPurpose.Entry, OrderAction.Place)]
    [TestCase(OrderOrigin.Manual, OrderPurpose.Exit, OrderAction.Place)]
    [TestCase(OrderOrigin.Options, OrderPurpose.Entry, OrderAction.Place)]
    [TestCase(OrderOrigin.Flatten, OrderPurpose.Exit, OrderAction.Place)]
    [TestCase(OrderOrigin.Manual, OrderPurpose.Exit, OrderAction.Cancel)]
    [TestCase(OrderOrigin.Manual, OrderPurpose.Entry, OrderAction.Replace)]
    [TestCase(OrderOrigin.Flatten, OrderPurpose.Exit, OrderAction.CancelAll)]
    public async Task Law13_EveryPersonInitiatedLiveAction_IsHeldForACode(OrderOrigin origin, OrderPurpose purpose, OrderAction action)
    {
        foreach (var caller in new[] { "cli", "mcp" })
        {
            var outcome = await gateway.SubmitAsync(LiveIntent(origin, purpose, action), new GatewayOptions { Caller = caller });

            Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.ConfirmationRequired), $"{caller}: {outcome.Message}");
            Assert.That(outcome.ConfirmationCode, Has.Length.EqualTo(ConfirmationStore.CodeLength));
        }
        Assert.That(live.Calls, Is.Zero, "nothing reached the live account without its code");
    }

    [Test]
    public async Task Law13_OnlyTheMonitor_FiresLiveWithoutACode()
    {
        var outcome = await gateway.SubmitAsync(LiveIntent(OrderOrigin.Monitor, OrderPurpose.Entry),
            new GatewayOptions { Caller = "monitor", LlmVerdict = (true, "approved") });

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        Assert.That(live.Calls, Is.EqualTo(1));
    }

    private sealed class LiveRouting(IBrokerClient client) : IOrderRouting
    {
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) => Task.FromResult(client);
    }

    private sealed class Broker : IBrokerClient
    {
        public int Calls { get; private set; }
        public BrokerType BrokerType => BrokerType.Alpaca;
        public bool IsPaper => false;
        public bool IsConnected => true;
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = "live-1" });
        }
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId });
        }
        public Task<OrderResult> ReplaceOrderAsync(string orderId, OrderRequest changes, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId + "-r" });
        }
        public Task<OrderResult> CancelAllOrdersAsync(string? symbol = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new OrderResult { IsSuccess = true, Message = "0 cancelled" });
        }
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>([]);
    }
}
