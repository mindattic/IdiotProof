using IdiotProof.Core.Data;
using IdiotProof.Brokers;
using IdiotProof.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// UserBrokerResolver.ResolveAsync against a real UserKeyService over an in-memory database and a
/// temp-directory Vault (IP-T330: the pair comes from the user's own Vault entry, never SQL, never
/// another user's). Pins the consent rule an adversarial review caught: Vault supplies the
/// CREDENTIAL PAIR only, never the DefaultBroker opt-in, so a user who hasn't enabled Alpaca
/// routing can't be silently upgraded off Sandbox just because a providers.json exists.
/// </summary>
[TestFixture]
public sealed class UserBrokerResolverResolveAsyncTests
{
    private TempVault vault = null!;
    private UserKeyService keys = null!;
    private BrokerRouter router = null!;
    private readonly Guid owner = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        vault = new TempVault(owner);
        var factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"resolver-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();
        keys = new UserKeyService(factory, new EphemeralDataProtectionProvider(), vault.Vault, NullLogger<UserKeyService>.Instance);
        router = new BrokerRouter();
        router.Register(new SandboxBrokerClient());
    }

    [TearDown]
    public void TearDown() => vault.Dispose();

    private UserBrokerResolver Resolver() => new(keys, router, NullLogger<UserBrokerResolver>.Instance);

    [Test]
    public async Task VaultHasKeys_ButUserNeverOptedIntoAlpaca_StillRoutesToSandbox()
    {
        await keys.SaveAsync(owner, new UserApiKeys { UserId = owner, DefaultBroker = "Sandbox", AlpacaApiKeyId = "PKVAULT", AlpacaApiSecretKey = "vaultsecret" });

        var broker = await Resolver().ResolveAsync(owner, "Paper");

        Assert.That(broker.BrokerType, Is.EqualTo(BrokerType.Sandbox),
            "a Vault-supplied credential pair must never bypass the DefaultBroker opt-in — that's the sole consent gate for real-money routing.");
    }

    [Test]
    public async Task VaultHasKeys_UserOptedIn_RoutesToUserAlpaca()
    {
        await keys.SaveAsync(owner, new UserApiKeys { UserId = owner, DefaultBroker = "alpaca", AlpacaApiKeyId = "PKVAULT", AlpacaApiSecretKey = "vaultsecret" });

        var broker = await Resolver().ResolveAsync(owner, "Paper");

        Assert.That(broker.BrokerType, Is.EqualTo(BrokerType.Alpaca));
    }

    [Test]
    public async Task OwnersCanonicalPair_IsNeverHandedToAnotherOptedInUser()
    {
        vault.Vault.SetAlpaca(owner, isPaper: true, "PKOWNER", "owner-secret");
        var other = Guid.NewGuid();
        await keys.SaveAsync(other, new UserApiKeys { UserId = other, DefaultBroker = "alpaca" });

        var broker = await Resolver().ResolveAsync(other, "Paper");

        Assert.That(broker.BrokerType, Is.EqualTo(BrokerType.Sandbox), "another user must not trade the owner's account");
    }

    [Test]
    public async Task NoVaultKeys_OptedIn_FallsBackToSandbox()
    {
        await keys.SaveAsync(owner, new UserApiKeys { UserId = owner, DefaultBroker = "alpaca" });

        var broker = await Resolver().ResolveAsync(owner, "Paper");

        Assert.That(broker.BrokerType, Is.EqualTo(BrokerType.Sandbox));
    }
}
