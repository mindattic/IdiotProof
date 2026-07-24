using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Per-user broker routing rule (IP-A9): a user's orders go to THEIR Alpaca
/// account only when they opted in AND supplied both keys; anything less falls
/// through to the global router, whose default is Sandbox (IP-LAW-3) — a
/// missing or undecryptable key can never route money into the wrong account.
/// </summary>
[TestFixture]
public sealed class UserBrokerResolverTests
{
    private static UserApiKeys Keys(string? broker, string? keyId, string? secret,
        string? liveKeyId = null, string? liveSecret = null) => new()
    {
        UserId = Guid.NewGuid(),
        DefaultBroker = broker!,
        AlpacaApiKeyId = keyId,
        AlpacaApiSecretKey = secret,
        AlpacaLiveApiKeyId = liveKeyId,
        AlpacaLiveApiSecretKey = liveSecret,
    };

    [Test]
    public void Choose_AlpacaOptInWithBothKeys_RoutesToUserAccount()
    {
        Assert.That(UserBrokerResolver.Choose(Keys("alpaca", "PKTEST", "secret")),
            Is.EqualTo(BrokerChoice.UserAlpaca));
        Assert.That(UserBrokerResolver.Choose(Keys("ALPACA", "PKTEST", "secret")),
            Is.EqualTo(BrokerChoice.UserAlpaca), "broker name comparison is case-insensitive");
    }

    [Test]
    public void Choose_MissingEitherKey_FallsThroughToGlobalDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UserBrokerResolver.Choose(Keys("alpaca", null, "secret")), Is.EqualTo(BrokerChoice.GlobalDefault));
            Assert.That(UserBrokerResolver.Choose(Keys("alpaca", "PKTEST", null)), Is.EqualTo(BrokerChoice.GlobalDefault));
            Assert.That(UserBrokerResolver.Choose(Keys("alpaca", "", "")), Is.EqualTo(BrokerChoice.GlobalDefault));
        });
    }

    [Test]
    public void Choose_NoBrokerPreference_FallsThroughToGlobalDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UserBrokerResolver.Choose(Keys(null, "PKTEST", "secret")), Is.EqualTo(BrokerChoice.GlobalDefault));
            Assert.That(UserBrokerResolver.Choose(Keys("sandbox", "PKTEST", "secret")), Is.EqualTo(BrokerChoice.GlobalDefault));
        });
    }

    // Paper and Live are separate Alpaca accounts with separate key pairs — a user
    // can have one configured without the other, and the wrong pair must never be
    // reused across the boundary (Alpaca rejects a paper key against the live API).

    [Test]
    public void Choose_LiveMode_RequiresTheLiveKeyPair()
    {
        var paperOnly = Keys("alpaca", "PKTEST", "papersecret");
        Assert.That(UserBrokerResolver.Choose(paperOnly, isPaper: true), Is.EqualTo(BrokerChoice.UserAlpaca));
        Assert.That(UserBrokerResolver.Choose(paperOnly, isPaper: false), Is.EqualTo(BrokerChoice.GlobalDefault),
            "a paper-only account must not route Live to the paper keys");

        var both = Keys("alpaca", "PKTEST", "papersecret", "AKLIVE", "livesecret");
        Assert.That(UserBrokerResolver.Choose(both, isPaper: false), Is.EqualTo(BrokerChoice.UserAlpaca));
    }
}
