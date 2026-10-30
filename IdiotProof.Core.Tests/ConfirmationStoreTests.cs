using IdiotProof.Blazor.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Real money from the CLI/MCP needs a preview, then a code, then a second call. These pin the
/// code's guarantees: owner-bound, single-use, short-lived, and it replays exactly what was
/// previewed — any edit to the order is a different intent with a different hash.
/// </summary>
[TestFixture]
public sealed class ConfirmationStoreTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private FakeTimeProvider clock = null!;
    private ConfirmationStore store = null!;

    [SetUp]
    public void SetUp()
    {
        clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 14, 0, 0, TimeSpan.Zero));
        var factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"confirm-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        store = new ConfirmationStore(factory, clock);
    }

    private static OrderIntent Buy(int qty = 10, decimal limit = 131.20m) => new()
    {
        OwnerUserId = Alice, Mode = BrokerMode.Live, Purpose = OrderPurpose.Entry, Origin = OrderOrigin.Manual,
        Order = new OrderRequest { Symbol = "NVDA", Quantity = qty, Side = OrderSide.Buy, Type = OrderType.Limit, LimitPrice = limit },
        StopLossPrice = 127m, ReferencePrice = limit, Note = "CLI buy",
    };

    private Task<PendingConfirmation> IssueAsync(OrderIntent intent) =>
        store.IssueAsync(intent.OwnerUserId, ConfirmationStore.KindOrder, intent.ToJson(), intent.Hash(), intent.Describe());

    private static string Rehash(PendingConfirmation p) => OrderIntent.FromJson(p.PayloadJson).Hash();

    [Test]
    public void Hash_ChangesWithAnyOrderField_ButNotWithTheNote()
    {
        var baseline = Buy().Hash();

        Assert.Multiple(() =>
        {
            Assert.That(Buy(qty: 11).Hash(), Is.Not.EqualTo(baseline));
            Assert.That(Buy(limit: 131.21m).Hash(), Is.Not.EqualTo(baseline));
            Assert.That((Buy() with { Mode = BrokerMode.Paper }).Hash(), Is.Not.EqualTo(baseline));
            Assert.That((Buy() with { OwnerUserId = Bob }).Hash(), Is.Not.EqualTo(baseline));
            Assert.That((Buy() with { Note = "different words" }).Hash(), Is.EqualTo(baseline));
        });
    }

    [Test]
    public void Json_RoundTrips_ToTheSameHash()
    {
        var intent = Buy();
        Assert.That(OrderIntent.FromJson(intent.ToJson()).Hash(), Is.EqualTo(intent.Hash()));
    }

    [Test]
    public async Task Issue_ProducesAReadableSixCharCode_ValidForTwoMinutes()
    {
        var row = await IssueAsync(Buy());

        Assert.That(row.Code, Has.Length.EqualTo(ConfirmationStore.CodeLength));
        Assert.That(row.Code.All(ConfirmationStore.Alphabet.Contains), Is.True);
        Assert.That(row.ExpiresUtc - row.CreatedUtc, Is.EqualTo(ConfirmationStore.Lifetime));
    }

    [Test]
    public async Task Redeem_WorksOnce_ThenReportsAlreadyUsed()
    {
        var row = await IssueAsync(Buy());

        var first = await store.RedeemAsync(row.Code.ToLowerInvariant(), Alice, "cli", Rehash);
        var second = await store.RedeemAsync(row.Code, Alice, "cli", Rehash);

        Assert.That(first.Succeeded, Is.True, "codes are case-insensitive");
        Assert.That(OrderIntent.FromJson(first.Confirmation!.PayloadJson).Hash(), Is.EqualTo(Buy().Hash()));
        Assert.That(second.Failure, Is.EqualTo(ConfirmationFailure.AlreadyUsed));
    }

    [Test]
    public async Task Redeem_AfterTheLifetime_IsExpired()
    {
        var row = await IssueAsync(Buy());
        clock.Advance(ConfirmationStore.Lifetime + TimeSpan.FromSeconds(1));

        var result = await store.RedeemAsync(row.Code, Alice, "cli", Rehash);

        Assert.That(result.Failure, Is.EqualTo(ConfirmationFailure.Expired));
    }

    [Test]
    public async Task Redeem_ByAnotherUser_IsRefused()
    {
        var row = await IssueAsync(Buy());

        var result = await store.RedeemAsync(row.Code, Bob, "mcp", Rehash);

        Assert.That(result.Failure, Is.EqualTo(ConfirmationFailure.WrongOwner));
        Assert.That((await store.RedeemAsync(row.Code, Alice, "cli", Rehash)).Succeeded, Is.True,
            "a refused attempt by someone else does not burn the owner's code");
    }

    [Test]
    public async Task Redeem_WhenTheStoredPayloadNoLongerMatchesItsHash_IsTampered()
    {
        var row = await IssueAsync(Buy());

        var result = await store.RedeemAsync(row.Code, Alice, "cli", _ => Buy(qty: 1000).Hash());

        Assert.That(result.Failure, Is.EqualTo(ConfirmationFailure.Tampered));
    }

    [Test]
    public async Task Redeem_UnknownCode_IsNotFound_AndListActiveShowsOnlyUsableCodes()
    {
        var used = await IssueAsync(Buy());
        var open = await IssueAsync(Buy(qty: 2));
        await store.RedeemAsync(used.Code, Alice, "cli", Rehash);

        Assert.That((await store.RedeemAsync("ZZZZZZ", Alice, "cli", Rehash)).Failure, Is.EqualTo(ConfirmationFailure.NotFound));
        Assert.That((await store.ListActiveAsync(Alice)).Select(p => p.Code), Is.EqualTo(new[] { open.Code }));
    }

    [Test]
    public void ParseMode_AnythingUnrecognized_IsSandbox()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OrderIntent.ParseMode("live"), Is.EqualTo(BrokerMode.Live));
            Assert.That(OrderIntent.ParseMode("Paper"), Is.EqualTo(BrokerMode.Paper));
            Assert.That(OrderIntent.ParseMode(null), Is.EqualTo(BrokerMode.Sandbox));
            Assert.That(OrderIntent.ParseMode("real-money-please"), Is.EqualTo(BrokerMode.Sandbox));
            Assert.That(OrderIntent.ParseMode("7"), Is.EqualTo(BrokerMode.Sandbox));
        });
    }
}
