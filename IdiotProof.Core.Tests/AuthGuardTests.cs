using System.Collections.Concurrent;
using System.Reflection;
using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Authentication.Crypto;
using MindAttic.Authentication.Entities;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T11: the auth guards that gate Paper-to-Live promotion (<see cref="LiveModeElevationService"/>)
/// and registration (<see cref="EmailDomainBlocklistService"/>), on an in-memory database.
/// </summary>
[TestFixture]
public sealed class AuthGuardTests
{
    private IDbContextFactory<AppDbContext> factory = null!;

    [SetUp]
    public void SetUp() =>
        factory = new ServiceCollection()
            // Null checks off so the "no stored hash" row (an SSO-only or half-provisioned account) can exist.
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"auth-{Guid.NewGuid():N}", b => b.EnableNullChecks(false)))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();

    private LiveModeElevationService Elevation(IPasswordHasher? hasher = null) =>
        new(factory, hasher ?? new RecordingHasher(), NullLogger<LiveModeElevationService>.Instance);

    private async Task<Guid> AddUserAsync(string? hash, string? pepper = "pep-1", string? legacy = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var email = $"u{Guid.NewGuid():N}@example.com";
        var user = new AuthUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = hash!, PasswordPepperKeyId = pepper, LegacyHashScheme = legacy,
            Role = "User", IsActive = true,
        };
        db.AuthUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    // ── LiveModeElevationService ────────────────────────────────────────────────

    [Test]
    public void IsElevated_FalseByDefault_AndNoRemainingWindow()
    {
        var svc = Elevation();
        var user = Guid.NewGuid();
        Assert.Multiple(() =>
        {
            Assert.That(svc.IsElevated(user), Is.False);
            Assert.That(svc.RemainingWindow(user), Is.Null);
        });
    }

    [Test]
    public void Elevate_OpensAFiveMinuteWindow()
    {
        var svc = Elevation();
        var user = Guid.NewGuid();
        svc.Elevate(user);

        var remaining = svc.RemainingWindow(user);
        Assert.Multiple(() =>
        {
            Assert.That(svc.IsElevated(user), Is.True);
            Assert.That(remaining, Is.Not.Null);
            Assert.That(remaining!.Value, Is.GreaterThan(TimeSpan.FromMinutes(4.9)));
            Assert.That(remaining.Value, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(svc.IsElevated(Guid.NewGuid()), Is.False, "elevation is per user");
        });
    }

    [Test]
    public void Revoke_ClearsTheWindow()
    {
        var svc = Elevation();
        var user = Guid.NewGuid();
        svc.Elevate(user);
        svc.Revoke(user);
        Assert.Multiple(() =>
        {
            Assert.That(svc.IsElevated(user), Is.False);
            Assert.That(svc.RemainingWindow(user), Is.Null);
        });
    }

    [Test]
    public void ExpiredWindow_IsNotElevated_HasNoRemaining_AndIsPurgedOnNextElevate()
    {
        var svc = Elevation();
        var stale = Guid.NewGuid();
        var map = ElevatedUntil(svc);
        map[stale] = DateTime.UtcNow.AddSeconds(-1);

        Assert.Multiple(() =>
        {
            Assert.That(svc.IsElevated(stale), Is.False);
            Assert.That(svc.RemainingWindow(stale), Is.Null);
        });

        var fresh = Guid.NewGuid();
        svc.Elevate(fresh);
        Assert.Multiple(() =>
        {
            Assert.That(map.ContainsKey(stale), Is.False, "expired entry purged");
            Assert.That(map.ContainsKey(fresh), Is.True);
        });
    }

    [Test]
    public async Task VerifyPassword_PassesPepperKeyIdAndLegacyScheme()
    {
        var hasher = new RecordingHasher { Result = true };
        var user = await AddUserAsync("$argon2id$stored", pepper: "pep-7", legacy: "bcrypt");

        var ok = await Elevation(hasher).VerifyPasswordAsync(user, "pw");

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(hasher.Calls, Has.Count.EqualTo(1));
            Assert.That(hasher.Calls[0], Is.EqualTo(("pw", "$argon2id$stored", (string?)"pep-7", (string?)"bcrypt")));
        });
    }

    [Test]
    public async Task VerifyPassword_FalseWhenHasherRejects()
    {
        var user = await AddUserAsync("$argon2id$stored");
        Assert.That(await Elevation(new RecordingHasher { Result = false }).VerifyPasswordAsync(user, "bad"), Is.False);
    }

    [Test]
    public async Task VerifyPassword_FalseForUnknownUser_WithoutCallingHasher()
    {
        var hasher = new RecordingHasher { Result = true };
        Assert.Multiple(async () =>
        {
            Assert.That(await Elevation(hasher).VerifyPasswordAsync(Guid.NewGuid(), "pw"), Is.False);
            Assert.That(hasher.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task VerifyPassword_FalseForNullHash()
    {
        var hasher = new RecordingHasher { Result = true };
        var user = await AddUserAsync(null);

        Assert.Multiple(async () =>
        {
            Assert.That(await Elevation(hasher).VerifyPasswordAsync(user, "pw"), Is.False);
            Assert.That(hasher.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task VerifyPassword_FalseWhenHasherThrows()
    {
        var user = await AddUserAsync("$argon2id$stored");
        var hasher = new RecordingHasher { Throw = true };
        Assert.That(await Elevation(hasher).VerifyPasswordAsync(user, "pw"), Is.False);
    }

    private static ConcurrentDictionary<Guid, DateTime> ElevatedUntil(LiveModeElevationService svc) =>
        (ConcurrentDictionary<Guid, DateTime>)typeof(LiveModeElevationService)
            .GetField("elevatedUntil", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(svc)!;

    /// <summary>Records every Verify call; returns <see cref="Result"/> or throws.</summary>
    private sealed class RecordingHasher : IPasswordHasher
    {
        public bool Result { get; init; }
        public bool Throw { get; init; }
        public List<(string Password, string Stored, string? Pepper, string? Legacy)> Calls { get; } = [];

        public PasswordHash Hash(string password) => new("phc", "pep");

        public PasswordVerifyResult Verify(string password, string storedHash, string? pepperKeyId, string? legacyScheme)
        {
            Calls.Add((password, storedHash, pepperKeyId, legacyScheme));
            if (Throw) throw new InvalidOperationException("hasher exploded");
            return new PasswordVerifyResult(Result, false);
        }

        public void VerifyDecoy(string password) { }
    }

    // ── EmailDomainBlocklistService ─────────────────────────────────────────────

    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase("   ", null)]
    [TestCase("no-at-sign.com", null)]
    [TestCase("@example.com", null)]
    [TestCase("user@", null)]
    [TestCase("user@localhost", null)]
    [TestCase("user@.example.com", null)]
    [TestCase("user@example.com.", null)]
    [TestCase("user@exa mple.com", null)]
    [TestCase("user@Example.COM", "example.com")]
    [TestCase("user@ example.com ", "example.com")]
    [TestCase("a@b@mailinator.com", "mailinator.com")]
    public void DomainOf_ParsesOrRejects(string? email, string? expected) =>
        Assert.That(EmailDomainBlocklistService.DomainOf(email), Is.EqualTo(expected));

    [Test]
    public async Task IsBlocked_RejectsMalformed_AndMatchesSeededDomains()
    {
        var svc = new EmailDomainBlocklistService(factory);
        await svc.SeedAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await svc.IsBlockedAsync(null), Is.True);
            Assert.That(await svc.IsBlockedAsync("not-an-email"), Is.True);
            Assert.That(await svc.IsBlockedAsync("bot@MAILINATOR.com"), Is.True);
            Assert.That(await svc.IsBlockedAsync("bot@yopmail.com"), Is.True);
            Assert.That(await svc.IsBlockedAsync("person@gmail.com"), Is.False);
        });
    }

    [Test]
    public async Task IsBlocked_HonorsAdminAddedDomains()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.DomainNameBlacklist.Add(new BlockedEmailDomain { Domain = "spammy.test", Reason = "admin" });
            await db.SaveChangesAsync();
        }
        Assert.That(await new EmailDomainBlocklistService(factory).IsBlockedAsync("x@spammy.test"), Is.True);
    }

    [Test]
    public async Task Seed_IsIdempotent_SecondRunInsertsZero()
    {
        var svc = new EmailDomainBlocklistService(factory);
        var first = await svc.SeedAsync();
        var second = await svc.SeedAsync();

        await using var db = await factory.CreateDbContextAsync();
        var count = await db.DomainNameBlacklist.CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.GreaterThan(50));
            Assert.That(second, Is.Zero);
            Assert.That(count, Is.EqualTo(first));
        });
    }

    [Test]
    public async Task Seed_OnlyInsertsTheMissingDiff()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.DomainNameBlacklist.Add(new BlockedEmailDomain { Domain = "mailinator.com" });
            await db.SaveChangesAsync();
        }
        var svc = new EmailDomainBlocklistService(factory);
        var inserted = await svc.SeedAsync();

        await using var check = await factory.CreateDbContextAsync();
        Assert.That(await check.DomainNameBlacklist.CountAsync(), Is.EqualTo(inserted + 1));
    }
}
