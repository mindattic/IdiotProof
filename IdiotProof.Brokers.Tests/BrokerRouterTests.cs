using IdiotProof.Brokers;
using IdiotProof.Models;

namespace IdiotProof.Brokers.Tests;

[TestFixture]
public sealed class BrokerRouterTests
{
    // ── IP-US-E3 / IP-LAW-3: Sandbox is the always-safe default broker ──

    [Test]
    public void BrokerRouter_DefaultActiveBroker_IsSandbox()
    {
        var router = new BrokerRouter();
        Assert.That(router.ActiveBrokerType, Is.EqualTo(BrokerType.Sandbox));
    }

    [Test]
    public void BrokerRouter_GetActiveBroker_Throws_WhenActiveNotRegistered()
    {
        // A silent fallback here would route live-intended orders to Sandbox
        // with no indication — GetActiveBroker() deliberately throws instead
        // for any non-Sandbox active broker that isn't registered.
        var router = new BrokerRouter();
        var sandbox = new FakeBroker(BrokerType.Sandbox);
        router.Register(sandbox);

        router.SetActive(BrokerType.Alpaca);

        Assert.Throws<InvalidOperationException>(() => router.GetActiveBroker());
    }

    [Test]
    public void BrokerRouter_GetActiveBroker_Throws_WhenNoSandboxRegistered()
    {
        var router = new BrokerRouter();
        Assert.Throws<InvalidOperationException>(() => router.GetActiveBroker());
    }

    [Test]
    public void BrokerRouter_GetActiveBroker_ReturnsRegisteredBroker()
    {
        var router = new BrokerRouter();
        var sandbox = new FakeBroker(BrokerType.Sandbox);
        router.Register(sandbox);

        var active = router.GetActiveBroker();
        Assert.That(active, Is.SameAs(sandbox));
    }

    [Test]
    public void BrokerRouter_SetActive_ByString_Activates()
    {
        var router = new BrokerRouter();
        var sandbox = new FakeBroker(BrokerType.Sandbox);
        var alpaca  = new FakeBroker(BrokerType.Alpaca);
        router.Register(sandbox);
        router.Register(alpaca);

        router.SetActive("alpaca");

        Assert.That(router.ActiveBrokerType, Is.EqualTo(BrokerType.Alpaca));
        Assert.That(router.GetActiveBroker().BrokerType, Is.EqualTo(BrokerType.Alpaca));
    }

    [Test]
    public void BrokerRouter_SetActive_EmptyString_DoesNotChange()
    {
        var router = new BrokerRouter();
        router.SetActive((string?)null);
        Assert.That(router.ActiveBrokerType, Is.EqualTo(BrokerType.Sandbox));
    }

    [Test]
    public void BrokerRouter_GetBroker_ByType_FallsBackToSandbox_WhenTypeNotRegistered()
    {
        var router  = new BrokerRouter();
        var sandbox = new FakeBroker(BrokerType.Sandbox);
        router.Register(sandbox);

        var result = router.GetBroker(BrokerType.Alpaca);
        Assert.That(result.BrokerType, Is.EqualTo(BrokerType.Sandbox));
    }

    [Test]
    public void BrokerRouter_GetAll_ReturnsAllRegisteredBrokers()
    {
        var router  = new BrokerRouter();
        var sandbox = new FakeBroker(BrokerType.Sandbox);
        var alpaca  = new FakeBroker(BrokerType.Alpaca);
        router.Register(sandbox);
        router.Register(alpaca);

        var all = router.GetAll().ToList();
        Assert.That(all, Has.Count.EqualTo(2));
        Assert.That(all, Contains.Item(sandbox));
        Assert.That(all, Contains.Item(alpaca));
    }
}

// ── Minimal test double ──
file sealed class FakeBroker(BrokerType type) : IBrokerClient
{
    public BrokerType BrokerType => type;
    public bool IsPaper => true;
    public bool IsConnected => true;
    public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task DisconnectAsync() => Task.CompletedTask;
    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default) => throw new NotImplementedException();
}
