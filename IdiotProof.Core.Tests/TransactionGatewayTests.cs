using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The transaction gateway is the one road to a broker. One test per rule: malformed orders never
/// leave, Live never silently degrades to paper, Mock data never opens a real position (IP-LAW-9),
/// entries need a stop and room under the Risk Guardian (IP-LAW-2), automated entries need the
/// voter panel (IP-LAW-1), exits always pass, and real money from a person waits for a code that
/// replays exactly what was previewed.
/// </summary>
[TestFixture]
public sealed class TransactionGatewayTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private FakeBroker sandbox = null!, paper = null!, live = null!;
    private FakeRouting routing = null!;
    private IDbContextFactory<AppDbContext> db = null!;
    private ConfirmationStore confirmations = null!;
    private FakeTimeProvider clock = null!;

    [SetUp]
    public void SetUp()
    {
        sandbox = new FakeBroker(BrokerType.Sandbox, isPaper: true);
        paper = new FakeBroker(BrokerType.Alpaca, isPaper: true);
        live = new FakeBroker(BrokerType.Alpaca, isPaper: false);
        routing = new FakeRouting { [BrokerMode.Sandbox] = sandbox, [BrokerMode.Paper] = paper, [BrokerMode.Live] = live };
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"gateway-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        confirmations = new ConfirmationStore(db, clock);
    }

    private TransactionGateway Gateway(IMarketDataFeed? feed = null) => new(
        routing,
        feed ?? new SwitchableMarketDataFeed(), // any feed that isn't MockDataFeed counts as real data
        new RiskGuardianService(db),
        confirmations,
        new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance),
        NullLogger<TransactionGateway>.Instance);

    private static OrderIntent Buy(BrokerMode mode, decimal? stop = 98m, int qty = 10, OrderOrigin origin = OrderOrigin.Manual,
        OrderPurpose purpose = OrderPurpose.Entry) => new()
    {
        OwnerUserId = Owner, Mode = mode, Purpose = purpose, Origin = origin,
        Order = new OrderRequest { Symbol = "NVDA", Quantity = qty, Side = purpose == OrderPurpose.Entry ? OrderSide.Buy : OrderSide.Sell, Type = OrderType.Limit, LimitPrice = 100m },
        StopLossPrice = stop, ReferencePrice = 100m,
    };

    [Test]
    public async Task Sandbox_ManualEntry_IsPlacedOnTheSimulator()
    {
        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Sandbox));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(sandbox.Placed, Has.Count.EqualTo(1));
        Assert.That(outcome.RoutedTo, Is.EqualTo("Sandbox (simulated)"));
    }

    [Test]
    public async Task MalformedOrder_NeverReachesABroker()
    {
        var both = Buy(BrokerMode.Sandbox) with { Order = new OrderRequest { Symbol = "NVDA", Quantity = 5, Notional = 500m, Side = OrderSide.Buy, Type = OrderType.Market } };

        var outcome = await Gateway().SubmitAsync(both);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(sandbox.Placed, Is.Empty);
    }

    [Test]
    public async Task Entry_WithoutAStop_IsVetoed()
    {
        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Sandbox, stop: null));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(outcome.Verdicts.Single(v => v.Gate == "risk").Passed, Is.False);
        Assert.That(sandbox.Placed, Is.Empty);
    }

    [Test]
    public async Task Entry_RiskingMoreThanTheCap_IsVetoed()
    {
        // $10 of risk per share × 50 shares = $500, five times the default $100 per-trade cap.
        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Sandbox, stop: 90m, qty: 50));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(outcome.Message, Does.StartWith("Risk Guardian veto"));
    }

    [Test]
    public async Task Exit_AlwaysPasses_EvenWithoutAStop()
    {
        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Sandbox, stop: null, purpose: OrderPurpose.Exit));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed));
    }

    [Test]
    public async Task MockFeed_RefusesRealEntries_ButAllowsRealExitsAndSandbox()
    {
        var gateway = Gateway(new MockDataFeed());

        var realEntry = await gateway.SubmitAsync(Buy(BrokerMode.Paper));
        var realExit = await gateway.SubmitAsync(Buy(BrokerMode.Paper, purpose: OrderPurpose.Exit));
        var simulated = await gateway.SubmitAsync(Buy(BrokerMode.Sandbox));

        Assert.Multiple(() =>
        {
            Assert.That(realEntry.Status, Is.EqualTo(GatewayStatus.Blocked));
            Assert.That(realExit.Status, Is.EqualTo(GatewayStatus.Placed));
            Assert.That(simulated.Status, Is.EqualTo(GatewayStatus.Placed));
            Assert.That(paper.Placed, Has.Count.EqualTo(1), "only the exit reached the paper account");
        });
    }

    [Test]
    public async Task Live_ThatResolvesToPaperOrSandbox_IsRefused_NeverDowngraded()
    {
        routing[BrokerMode.Live] = paper;

        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Live));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(paper.Placed, Is.Empty);
    }

    [Test]
    public async Task AutomatedEntry_NeedsAnApprovingVoterPanel()
    {
        var gateway = Gateway();

        var noVerdict = await gateway.SubmitAsync(Buy(BrokerMode.Sandbox, origin: OrderOrigin.Monitor));
        var rejected = await gateway.SubmitAsync(Buy(BrokerMode.Sandbox, origin: OrderOrigin.Monitor), new GatewayOptions { LlmVerdict = (false, "2 of 3 rejected") });
        var approved = await gateway.SubmitAsync(Buy(BrokerMode.Sandbox, origin: OrderOrigin.Monitor), new GatewayOptions { LlmVerdict = (true, "3 of 3 approved") });

        Assert.Multiple(() =>
        {
            Assert.That(noVerdict.Status, Is.EqualTo(GatewayStatus.Blocked));
            Assert.That(rejected.Status, Is.EqualTo(GatewayStatus.Blocked));
            Assert.That(approved.Status, Is.EqualTo(GatewayStatus.Placed));
        });
    }

    [Test]
    public async Task ManualLive_IssuesACode_ThenConfirmPlacesExactlyThatOrderOnce()
    {
        var gateway = Gateway();

        var pending = await gateway.SubmitAsync(Buy(BrokerMode.Live));
        Assert.That(pending.Status, Is.EqualTo(GatewayStatus.ConfirmationRequired));
        Assert.That(live.Placed, Is.Empty, "nothing moves before the yes");

        var confirmed = await gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "cli");
        Assert.That(confirmed!.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(live.Placed.Single().Quantity, Is.EqualTo(10));

        var again = Assert.ThrowsAsync<ConfirmationRefusedException>(() => gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "cli"));
        Assert.That(again!.Failure, Is.EqualTo(ConfirmationFailure.AlreadyUsed));
        Assert.That(live.Placed, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Confirm_WithADifferentOrderThanPreviewed_IsTampered_AndKeepsTheCode()
    {
        var gateway = Gateway();
        var pending = await gateway.SubmitAsync(Buy(BrokerMode.Live));

        var swapped = Assert.ThrowsAsync<ConfirmationRefusedException>(() =>
            gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "mcp", Buy(BrokerMode.Live, qty: 500).Hash()));
        var original = await gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "mcp", Buy(BrokerMode.Live).Hash());

        Assert.That(swapped!.Failure, Is.EqualTo(ConfirmationFailure.Tampered));
        Assert.That(original!.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(live.Placed.Single().Quantity, Is.EqualTo(10));
    }

    [Test]
    public async Task ManualLive_ExpiredCode_PlacesNothing()
    {
        var gateway = Gateway();
        var pending = await gateway.SubmitAsync(Buy(BrokerMode.Live));
        clock.Advance(ConfirmationStore.Lifetime + TimeSpan.FromSeconds(1));

        var refused = Assert.ThrowsAsync<ConfirmationRefusedException>(() => gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "cli"));

        Assert.That(refused!.Failure, Is.EqualTo(ConfirmationFailure.Expired));
        Assert.That(live.Placed, Is.Empty);
    }

    [Test]
    public async Task Preview_RunsEveryGate_PlacesNothing_IssuesNoCode()
    {
        var outcome = await Gateway().PreviewAsync(Buy(BrokerMode.Live));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Previewed));
        Assert.That(outcome.ConfirmationCode, Is.Null);
        Assert.That(outcome.Verdicts.Select(v => v.Gate), Is.EqualTo(new[] { "order", "route", "risk" }));
        Assert.That(outcome.Preview(), Does.Contain("Alpaca LIVE").And.Contain("worst case"));
        Assert.That(live.Placed, Is.Empty);
    }

    [Test]
    public async Task BrokerRefusal_IsRejected_AndAudited()
    {
        sandbox.Refuse = "insufficient buying power";

        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Sandbox));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Rejected));
        await using var ctx = await db.CreateDbContextAsync();
        Assert.That(ctx.AuditLogs.Any(a => a.Category == "order" && a.Message.Contains("Rejected")), Is.True);
    }

    [Test]
    public void Hash_ChangesWithEveryOrderField_ButNotTheNoteOrTrailingZeros()
    {
        var baseIntent = Buy(BrokerMode.Live);
        var o = baseIntent.Order;
        var variants = new[]
        {
            new OrderRequest { Symbol = o.Symbol, FractionalQuantity = 10.5m, Side = o.Side, Type = o.Type, LimitPrice = o.LimitPrice },
            new OrderRequest { Symbol = o.Symbol, Quantity = o.Quantity, Side = o.Side, Type = o.Type, LimitPrice = o.LimitPrice, ClientOrderId = "mine-1" },
            new OrderRequest { Symbol = o.Symbol, Quantity = o.Quantity, Side = o.Side, Type = o.Type, LimitPrice = o.LimitPrice, TimeInForce = "GTC" },
            new OrderRequest { Symbol = o.Symbol, Quantity = o.Quantity, Side = o.Side, Type = o.Type, LimitPrice = o.LimitPrice, OrderClass = OrderClass.Oto, StopLoss = new StopLossLeg { StopPrice = 95m } },
            new OrderRequest { Symbol = o.Symbol, Quantity = o.Quantity, Side = o.Side, Type = o.Type, LimitPrice = o.LimitPrice, OrderClass = OrderClass.Oto, StopLoss = new StopLossLeg { StopPrice = 95m, LimitPrice = 94m } },
            new OrderRequest { Symbol = o.Symbol, Quantity = o.Quantity, Side = o.Side, Type = o.Type, LimitPrice = o.LimitPrice, OrderClass = OrderClass.Bracket, StopLoss = new StopLossLeg { StopPrice = 95m }, TakeProfit = new TakeProfitLeg { LimitPrice = 110m } },
            new OrderRequest { Symbol = o.Symbol, Quantity = o.Quantity, Side = o.Side, Type = OrderType.TrailingStop, TrailPrice = 1m },
            new OrderRequest { Symbol = o.Symbol, Quantity = o.Quantity, Side = o.Side, Type = OrderType.TrailingStop, TrailPercent = 1m },
            new OrderRequest { Symbol = o.Symbol, Quantity = o.Quantity, Side = o.Side, Type = o.Type, LimitPrice = o.LimitPrice, PositionIntent = "buy_to_open" },
            new OrderRequest { Symbol = o.Symbol, Quantity = o.Quantity, Side = o.Side, Type = o.Type, LimitPrice = o.LimitPrice, OrderClass = OrderClass.Mleg, Legs = [new OrderLeg { Symbol = "A", Side = OrderSide.Buy }] },
            new OrderRequest { Symbol = o.Symbol, Quantity = o.Quantity, Side = o.Side, Type = o.Type, LimitPrice = o.LimitPrice, OrderClass = OrderClass.Mleg, Legs = [new OrderLeg { Symbol = "A", Side = OrderSide.Buy, RatioQuantity = 2 }] },
        };
        var hashes = variants.Select(v => (baseIntent with { Order = v }).Hash())
            .Append((baseIntent with { Action = OrderAction.Cancel, TargetOrderId = "a" }).Hash())
            .Append((baseIntent with { Action = OrderAction.Cancel, TargetOrderId = "b" }).Hash())
            .Append(baseIntent.Hash()).ToList();

        Assert.That(hashes.Distinct().Count(), Is.EqualTo(hashes.Count), "every field change is a different intent");
        Assert.That((baseIntent with { Note = "why" }).Hash(), Is.EqualTo(baseIntent.Hash()));
        var sameOrder = baseIntent with { Order = new OrderRequest { Symbol = "nvda", Quantity = 10, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 100.00m, TimeInForce = "day" } };
        Assert.That(sameOrder.Hash(), Is.EqualTo(baseIntent.Hash()), "case and trailing zeros don't make a different order");
    }

    [Test]
    public void Intent_RoundTripsEveryNewField()
    {
        var intent = Buy(BrokerMode.Live) with
        {
            Action = OrderAction.Replace, TargetOrderId = "ord-9",
            Order = new OrderRequest
            {
                Symbol = "NVDA", FractionalQuantity = 1.25m, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 100m,
                OrderClass = OrderClass.Mleg, ClientOrderId = "c-1", TrailPrice = 2m,
                Legs = [new OrderLeg { Symbol = "NVDA261120C00100000", Side = OrderSide.Buy, RatioQuantity = 1, PositionIntent = "buy_to_open" }],
                TakeProfit = new TakeProfitLeg { LimitPrice = 110m }, StopLoss = new StopLossLeg { StopPrice = 95m, LimitPrice = 94m },
            },
        };

        var back = OrderIntent.FromJson(intent.ToJson());

        Assert.That(back.Hash(), Is.EqualTo(intent.Hash()));
        Assert.That(back.Order.Legs!.Single().Symbol, Is.EqualTo("NVDA261120C00100000"));
    }

    [TestCase(OrderClass.Bracket, true, false, "both a take-profit and a stop-loss")]
    [TestCase(OrderClass.Oto, true, true, "exactly one child")]
    [TestCase(OrderClass.Simple, true, false, "no take-profit or stop-loss")]
    public async Task OrderClass_WithTheWrongChildren_IsBlocked(OrderClass cls, bool withTp, bool withSl, string reason)
    {
        var intent = Buy(BrokerMode.Sandbox) with
        {
            Order = new OrderRequest
            {
                Symbol = "NVDA", Quantity = 1, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 100m, OrderClass = cls,
                TakeProfit = withTp ? new TakeProfitLeg { LimitPrice = 110m } : null,
                StopLoss = withSl ? new StopLossLeg { StopPrice = 98m } : null,
            },
        };

        var outcome = await Gateway().SubmitAsync(intent);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(outcome.Message, Does.Contain(reason));
        Assert.That(sandbox.Placed, Is.Empty);
    }

    [Test]
    public async Task Bracket_ItsStopLossChild_IsTheProtectiveStopTheGuardianChecks()
    {
        var bracket = Buy(BrokerMode.Sandbox, stop: null) with
        {
            Order = new OrderRequest
            {
                Symbol = "NVDA", Quantity = 10, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = 100m, TimeInForce = "GTC",
                OrderClass = OrderClass.Bracket, TakeProfit = new TakeProfitLeg { LimitPrice = 110m }, StopLoss = new StopLossLeg { StopPrice = 98m },
            },
        };

        var outcome = await Gateway().SubmitAsync(bracket);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        Assert.That(sandbox.Placed.Single().OrderClass, Is.EqualTo(OrderClass.Bracket));
    }

    [Test]
    public async Task FractionalGtc_IsBlocked_FractionalDay_IsPlaced()
    {
        OrderIntent Frac(string tif) => Buy(BrokerMode.Sandbox, purpose: OrderPurpose.Exit) with
        {
            Order = new OrderRequest { Symbol = "NVDA", FractionalQuantity = 0.5m, Side = OrderSide.Sell, Type = OrderType.Market, TimeInForce = tif },
        };

        var gtc = await Gateway().SubmitAsync(Frac("GTC"));
        var day = await Gateway().SubmitAsync(Frac("DAY"));

        Assert.That(gtc.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(day.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(sandbox.Placed.Single().SizeQuantity, Is.EqualTo(0.5m));
    }

    [Test]
    public async Task LiveCancelAndReplace_NeedACode_ThenRunAtTheBroker()
    {
        var gateway = Gateway();
        var cancel = new OrderIntent
        {
            OwnerUserId = Owner, Mode = BrokerMode.Live, Purpose = OrderPurpose.Exit, Origin = OrderOrigin.Manual,
            Action = OrderAction.Cancel, TargetOrderId = "ord-7", Order = new OrderRequest { Symbol = "NVDA" },
        };
        var replace = cancel with
        {
            Action = OrderAction.Replace,
            Order = new OrderRequest { Symbol = "NVDA", Quantity = 5, LimitPrice = 101m, TimeInForce = "" },
        };

        var pendingCancel = await gateway.SubmitAsync(cancel);
        var pendingReplace = await gateway.SubmitAsync(replace);
        Assert.That(pendingCancel.Status, Is.EqualTo(GatewayStatus.ConfirmationRequired), pendingCancel.Message);
        Assert.That(pendingReplace.Status, Is.EqualTo(GatewayStatus.ConfirmationRequired), pendingReplace.Message);
        Assert.That(live.Cancelled, Is.Empty);
        Assert.That(live.Replaced, Is.Empty);

        var tampered = Assert.ThrowsAsync<ConfirmationRefusedException>(() =>
            gateway.ConfirmAsync(pendingCancel.ConfirmationCode!, Owner, "mcp", (cancel with { TargetOrderId = "ord-8" }).Hash()));
        Assert.That(tampered!.Failure, Is.EqualTo(ConfirmationFailure.Tampered));

        var cancelled = await gateway.ConfirmAsync(pendingCancel.ConfirmationCode!, Owner, "cli");
        var replaced = await gateway.ConfirmAsync(pendingReplace.ConfirmationCode!, Owner, "cli");

        Assert.That(cancelled!.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(live.Cancelled, Is.EqualTo(new[] { "ord-7" }));
        Assert.That(replaced!.Status, Is.EqualTo(GatewayStatus.Placed));
        Assert.That(live.Replaced.Single().Id, Is.EqualTo("ord-7"));
        Assert.That(live.Replaced.Single().Changes.LimitPrice, Is.EqualTo(101m));
        Assert.That(live.Placed, Is.Empty, "a cancel or replace never places a new order");
    }

    [Test]
    public async Task CancelAll_ForOneSymbol_PassesTheSymbol_EvenOnMockData()
    {
        var all = new OrderIntent
        {
            OwnerUserId = Owner, Mode = BrokerMode.Paper, Purpose = OrderPurpose.Exit, Origin = OrderOrigin.Manual,
            Action = OrderAction.CancelAll, Order = new OrderRequest { Symbol = "tsla" },
        };

        var outcome = await Gateway(new MockDataFeed()).SubmitAsync(all);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        Assert.That(paper.CancelAllFor, Is.EqualTo(new string?[] { "TSLA" }));
    }

    // ── IP-T98: one test per remaining law / step ─────────────────────────────

    private static OrderIntent ReplaceTo(BrokerMode mode, decimal qty, decimal? was, decimal? stop = null,
        OrderPurpose purpose = OrderPurpose.Entry) => new()
    {
        OwnerUserId = Owner, Mode = mode, Purpose = purpose, Origin = OrderOrigin.Manual,
        Action = OrderAction.Replace, TargetOrderId = "ord-1", ReplacedQuantity = was,
        ReferencePrice = 100m, StopLossPrice = stop,
        Order = new OrderRequest { Symbol = "NVDA", Quantity = (int)qty, Side = purpose == OrderPurpose.Entry ? OrderSide.Buy : OrderSide.Sell, TimeInForce = "" },
    };

    [Test]
    public async Task Replace_ThatRaisesAnEntry_IsRiskChecked_LikeANewEntry()
    {
        var gateway = Gateway();

        var noStop = await gateway.SubmitAsync(ReplaceTo(BrokerMode.Sandbox, 20, was: 10));
        var tooBig = await gateway.SubmitAsync(ReplaceTo(BrokerMode.Sandbox, 500, was: 10, stop: 98m));
        var fine = await gateway.SubmitAsync(ReplaceTo(BrokerMode.Sandbox, 20, was: 10, stop: 98m));

        Assert.Multiple(() =>
        {
            Assert.That(noStop.Status, Is.EqualTo(GatewayStatus.Blocked));
            Assert.That(tooBig.Status, Is.EqualTo(GatewayStatus.Blocked));
            Assert.That(tooBig.Message, Does.StartWith("Risk Guardian veto"));
            Assert.That(fine.Status, Is.EqualTo(GatewayStatus.Placed), fine.Message);
            Assert.That(fine.Verdicts.Single(v => v.Gate == "risk").Detail, Does.Contain("raises the size 10 → 20"));
            Assert.That(sandbox.Replaced, Has.Count.EqualTo(1), "only the checked raise reached the broker");
        });
    }

    [Test]
    public async Task Replace_WithAnUnknownOriginalSize_CountsAsARaise_FailClosed()
    {
        var outcome = await Gateway().SubmitAsync(ReplaceTo(BrokerMode.Sandbox, 5, was: null));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(sandbox.Replaced, Is.Empty);
    }

    [Test]
    public async Task Replace_ThatShrinksAnEntry_OrResizesAnExit_SkipsTheGuardian()
    {
        var gateway = Gateway();

        var smaller = await gateway.SubmitAsync(ReplaceTo(BrokerMode.Sandbox, 5, was: 10));
        var exit = await gateway.SubmitAsync(ReplaceTo(BrokerMode.Sandbox, 50, was: 10, purpose: OrderPurpose.Exit));

        Assert.That(smaller.Status, Is.EqualTo(GatewayStatus.Placed), smaller.Message);
        Assert.That(exit.Status, Is.EqualTo(GatewayStatus.Placed), exit.Message);
    }

    [Test]
    public async Task Replace_ThatRaisesARealEntry_OnMockData_IsRefused()
    {
        var outcome = await Gateway(new MockDataFeed()).SubmitAsync(ReplaceTo(BrokerMode.Paper, 20, was: 10, stop: 98m));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(outcome.Verdicts.Single(v => v.Gate == "data").Passed, Is.False);
        Assert.That(paper.Replaced, Is.Empty);
    }

    [Test]
    public async Task MockFeed_IsRecognisedByName_ThroughAWrapper()
    {
        var wrapped = new SwitchableMarketDataFeed("Mock");
        wrapped.Register(new MockDataFeed());

        var outcome = await Gateway(wrapped).SubmitAsync(Buy(BrokerMode.Paper));

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Blocked));
        Assert.That(paper.Placed, Is.Empty);
    }

    [Test]
    public async Task AutomatedExit_SkipsTheVoterPanel_AndPassesOnMockData()
    {
        var exit = Buy(BrokerMode.Paper, stop: null, origin: OrderOrigin.Monitor, purpose: OrderPurpose.Exit);

        var outcome = await Gateway(new MockDataFeed()).SubmitAsync(exit);

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        Assert.That(outcome.Verdicts.Any(v => v.Gate == "llm"), Is.False, "exits never wait on the LLM panel (IP-LAW-1)");
    }

    [Test]
    public async Task AutomatedLive_PlacesWithoutACode_TheStrategyActivationWasTheYes()
    {
        var outcome = await Gateway().SubmitAsync(Buy(BrokerMode.Live, origin: OrderOrigin.Monitor),
            new GatewayOptions { Caller = "monitor", LlmVerdict = (true, "3 of 3 approved") });

        Assert.That(outcome.Status, Is.EqualTo(GatewayStatus.Placed), outcome.Message);
        Assert.That(outcome.ConfirmationCode, Is.Null);
        Assert.That(live.Placed, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Confirm_AfterTheStoredIntentWasEdited_IsTampered_AndPlacesNothing()
    {
        var gateway = Gateway();
        var pending = await gateway.SubmitAsync(Buy(BrokerMode.Live));
        await using (var ctx = await db.CreateDbContextAsync())
        {
            var row = ctx.PendingConfirmations.Single(p => p.Code == pending.ConfirmationCode);
            row.PayloadJson = Buy(BrokerMode.Live, qty: 500).ToJson(); // edited behind the code's back
            await ctx.SaveChangesAsync();
        }

        var refused = Assert.ThrowsAsync<ConfirmationRefusedException>(() => gateway.ConfirmAsync(pending.ConfirmationCode!, Owner, "cli"));

        Assert.That(refused!.Failure, Is.EqualTo(ConfirmationFailure.Tampered));
        Assert.That(live.Placed, Is.Empty);
    }

    [Test]
    public async Task Confirm_ByAnotherUser_IsRefused()
    {
        var gateway = Gateway();
        var pending = await gateway.SubmitAsync(Buy(BrokerMode.Live));

        var refused = Assert.ThrowsAsync<ConfirmationRefusedException>(() => gateway.ConfirmAsync(pending.ConfirmationCode!, Guid.NewGuid(), "cli"));

        Assert.That(refused!.Failure, Is.EqualTo(ConfirmationFailure.WrongOwner));
        Assert.That(live.Placed, Is.Empty);
    }

    [Test]
    public async Task EveryOutcome_LeavesAnAuditRow_ButAPreviewDoesNot()
    {
        var gateway = Gateway();
        await gateway.PreviewAsync(Buy(BrokerMode.Sandbox));
        await using (var before = await db.CreateDbContextAsync())
            Assert.That(before.AuditLogs.Count(), Is.Zero, "a dry run is not an outcome");

        await gateway.SubmitAsync(Buy(BrokerMode.Sandbox));                 // Placed
        await gateway.SubmitAsync(Buy(BrokerMode.Sandbox, stop: null));     // Blocked
        await gateway.SubmitAsync(Buy(BrokerMode.Live));                    // ConfirmationRequired
        sandbox.Refuse = "no";
        await gateway.SubmitAsync(Buy(BrokerMode.Sandbox));                 // Rejected

        await using var ctx = await db.CreateDbContextAsync();
        var messages = ctx.AuditLogs.Select(a => a.Message).ToList();
        foreach (var status in new[] { "Placed", "Blocked", "ConfirmationRequired", "Rejected" })
            Assert.That(messages.Any(m => m.StartsWith(status + ":")), Is.True, status);
        Assert.That(ctx.AuditLogs.All(a => a.UserId == Owner && a.DataJson!.Contains("\"verdicts\"")), Is.True);
    }

    private static OrderIntent ExitOf(string symbol, BrokerMode mode = BrokerMode.Live) => new()
    {
        OwnerUserId = Owner, Mode = mode, Purpose = OrderPurpose.Exit, Origin = OrderOrigin.Flatten,
        Order = new OrderRequest { Symbol = symbol, Quantity = 5, Side = OrderSide.Sell, Type = OrderType.Market, TimeInForce = "DAY" },
    };

    [Test]
    public async Task Batch_OneCodeForTheExactSet_EachStillGated_ThenUsedUp()
    {
        var gateway = Gateway();
        var set = new[] { ExitOf("NVDA"), ExitOf("TSLA") };

        var (dry, pending) = await gateway.RequestBatchConfirmationAsync(set, "close all · Live", "preview", "cli");
        Assert.That(pending, Is.Not.Null);
        Assert.That(dry.All(o => o.Status == GatewayStatus.Previewed), Is.True);
        Assert.That(live.Placed, Is.Empty, "nothing moves before the yes");

        var other = Assert.ThrowsAsync<ConfirmationRefusedException>(() =>
            gateway.ConfirmBatchAsync(pending!.Code, [ExitOf("NVDA"), ExitOf("AAPL")], "close all · Live", "cli"));
        Assert.That(other!.Failure, Is.EqualTo(ConfirmationFailure.Tampered), "a different set is refused and the code survives");

        var placed = await gateway.ConfirmBatchAsync(pending!.Code, set, "close all · Live", "cli");
        Assert.That(placed.Select(o => o.Status), Is.All.EqualTo(GatewayStatus.Placed));
        Assert.That(live.Placed.Select(p => p.Symbol), Is.EqualTo(new[] { "NVDA", "TSLA" }));

        var again = Assert.ThrowsAsync<ConfirmationRefusedException>(() => gateway.ConfirmBatchAsync(pending.Code, set, "close all · Live", "cli"));
        Assert.That(again!.Failure, Is.EqualTo(ConfirmationFailure.AlreadyUsed));
        Assert.That(live.Placed, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Batch_WithARefusedMember_IssuesNoCode()
    {
        var bad = ExitOf("NVDA") with { Order = new OrderRequest { Symbol = "NVDA", Side = OrderSide.Sell, Type = OrderType.Market, TimeInForce = "DAY" } };

        var (dry, pending) = await Gateway().RequestBatchConfirmationAsync([ExitOf("TSLA"), bad], "close all · Live", "preview", "cli");

        Assert.That(pending, Is.Null);
        Assert.That(dry.Count(o => o.Status == GatewayStatus.Blocked), Is.EqualTo(1));
    }

    [Test]
    public async Task Batch_ExpiredCode_PlacesNothing()
    {
        var gateway = Gateway();
        var set = new[] { ExitOf("NVDA"), ExitOf("TSLA") };
        var (_, pending) = await gateway.RequestBatchConfirmationAsync(set, "close all · Live", "preview", "cli");
        clock.Advance(ConfirmationStore.Lifetime + TimeSpan.FromSeconds(1));

        var refused = Assert.ThrowsAsync<ConfirmationRefusedException>(() => gateway.ConfirmBatchAsync(pending!.Code, set, "close all · Live", "cli"));

        Assert.That(refused!.Failure, Is.EqualTo(ConfirmationFailure.Expired));
        Assert.That(live.Placed, Is.Empty);
    }

    [Test]
    public async Task OrderCode_CannotUnlockABatch_NorABatchCodeAnOrder()
    {
        var gateway = Gateway();
        var single = await gateway.SubmitAsync(Buy(BrokerMode.Live));
        var (_, batch) = await gateway.RequestBatchConfirmationAsync([ExitOf("NVDA"), ExitOf("TSLA")], "close all · Live", "preview", "cli");

        var wrongWay = Assert.ThrowsAsync<ConfirmationRefusedException>(() =>
            gateway.ConfirmBatchAsync(single.ConfirmationCode!, [ExitOf("NVDA"), ExitOf("TSLA")], "close all · Live", "cli"));
        var otherWay = Assert.ThrowsAsync<ConfirmationRefusedException>(() => gateway.ConfirmAsync(batch!.Code, Owner, "cli"));

        Assert.That(wrongWay!.Failure, Is.EqualTo(ConfirmationFailure.Tampered));
        Assert.That(otherWay!.Failure, Is.EqualTo(ConfirmationFailure.Tampered));
        Assert.That(live.Placed, Is.Empty);
    }

    private sealed class FakeRouting :Dictionary<BrokerMode, IBrokerClient>, IOrderRouting
    {
        public Task<IBrokerClient> ResolveAsync(Guid ownerUserId, BrokerMode mode, CancellationToken ct) => Task.FromResult(this[mode]);
    }

    private sealed class FakeBroker(BrokerType type, bool isPaper) : IBrokerClient
    {
        public List<OrderRequest> Placed { get; } = [];
        public string? Refuse { get; set; }
        public BrokerType BrokerType => type;
        public bool IsPaper => isPaper;
        public bool IsConnected => true;
        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            if (Refuse is not null) return Task.FromResult(new OrderResult { IsSuccess = false, Message = Refuse });
            Placed.Add(request);
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = $"ord-{Placed.Count}" });
        }
        public List<string> Cancelled { get; } = [];
        public List<(string Id, OrderRequest Changes)> Replaced { get; } = [];
        public List<string?> CancelAllFor { get; } = [];
        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
        {
            Cancelled.Add(orderId);
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId });
        }
        public Task<OrderResult> ReplaceOrderAsync(string orderId, OrderRequest changes, CancellationToken ct = default)
        {
            Replaced.Add((orderId, changes));
            return Task.FromResult(new OrderResult { IsSuccess = true, BrokerOrderId = orderId + "-r" });
        }
        public Task<OrderResult> CancelAllOrdersAsync(string? symbol = null, CancellationToken ct = default)
        {
            CancelAllFor.Add(symbol);
            return Task.FromResult(new OrderResult { IsSuccess = true, Message = "1 order(s) cancelled." });
        }
        public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Position>>([]);
    }
}
