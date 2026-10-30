using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The Monitor's LLM gate votes on the strategy owner's own Claude key (API Keys page), falling
/// back to the host key. Fakes stand in for the owner-key store and the host settings.
/// </summary>
[TestFixture]
public sealed class UserClaudeKeyResolverTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob   = Guid.NewGuid();

    private static UserClaudeKeyResolver Resolver(
        Dictionary<Guid, UserApiKeys> rows, string? hostKey, bool hostEnabled, Exception? storeFailure = null) =>
        new((id, _) => storeFailure is not null
                ? Task.FromException<UserApiKeys>(storeFailure)
                : Task.FromResult(rows.TryGetValue(id, out var r) ? r : new UserApiKeys { UserId = id }),
            () => hostKey, () => hostEnabled, NullLogger<UserClaudeKeyResolver>.Instance);

    [Test]
    public async Task EachOwner_GetsTheirOwnKey_NeverAnotherUsers()
    {
        var resolver = Resolver(new()
        {
            [Alice] = new UserApiKeys { UserId = Alice, ClaudeApiKey = "sk-ant-alice", LlmVotingEnabled = true, ClaudeModel = "claude-haiku-4-5-20251001" },
            [Bob]   = new UserApiKeys { UserId = Bob,   ClaudeApiKey = "sk-ant-bob",   LlmVotingEnabled = true },
        }, hostKey: "sk-ant-host", hostEnabled: false);

        var alice = await resolver.ResolveAsync(Alice);
        var bob   = await resolver.ResolveAsync(Bob);

        Assert.Multiple(() =>
        {
            Assert.That(alice, Is.EqualTo(new SignalVotingCredentials(true, "sk-ant-alice", "claude-haiku-4-5-20251001", "owner")));
            Assert.That(bob.ClaudeApiKey, Is.EqualTo("sk-ant-bob"));
            Assert.That(bob.KeySource, Is.EqualTo("owner"));
        });
    }

    [Test]
    public async Task OwnerWithoutKey_FallsBackToTheHostKey()
    {
        var resolver = Resolver(new() { [Alice] = new UserApiKeys { UserId = Alice, LlmVotingEnabled = true } },
            hostKey: "sk-ant-host", hostEnabled: false);

        var creds = await resolver.ResolveAsync(Alice);

        Assert.That(creds, Is.EqualTo(new SignalVotingCredentials(true, "sk-ant-host", null, "host")));
    }

    [Test]
    public async Task NoOwnerKey_NoHostKey_ResolvesNone()
    {
        var creds = await Resolver(new(), hostKey: "  ", hostEnabled: true).ResolveAsync(Alice);
        Assert.That(creds, Is.EqualTo(new SignalVotingCredentials(true, null, null, "none")));
    }

    [Test]
    public async Task OwnerToggle_AddsTheGate_ButCannotRemoveAHostWideOne()
    {
        var off = new UserApiKeys { UserId = Alice, ClaudeApiKey = "sk-ant-alice", LlmVotingEnabled = false };

        var hostOn  = await Resolver(new() { [Alice] = off }, "sk-ant-host", hostEnabled: true).ResolveAsync(Alice);
        var hostOff = await Resolver(new() { [Alice] = off }, "sk-ant-host", hostEnabled: false).ResolveAsync(Alice);

        Assert.That(hostOn.VotingEnabled, Is.True, "host-wide voting stays on even if the owner switched theirs off");
        Assert.That(hostOff.VotingEnabled, Is.False);
    }

    [Test]
    public async Task UnreadableOwnerRow_FallsBackToTheHostKey()
    {
        var creds = await Resolver(new(), "sk-ant-host", hostEnabled: true,
            storeFailure: new InvalidOperationException("key ring mismatch")).ResolveAsync(Alice);

        Assert.That(creds, Is.EqualTo(new SignalVotingCredentials(true, "sk-ant-host", null, "host")));
    }
}
