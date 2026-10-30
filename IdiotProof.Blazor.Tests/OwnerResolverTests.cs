using IdiotProof.Blazor.Data;
using IdiotProof.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindAttic.Authentication.Entities;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// OwnerResolver: the headless "who am I acting for" rule shared by the CLI, the Hub and the MCP
/// server. An explicit id wins, a lone user is the default, and zero or several users never
/// produce a guessed owner.
/// </summary>
[TestFixture]
public sealed class OwnerResolverTests
{
    private static IDbContextFactory<AppDbContext> NewDb() =>
        new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"owner-{Guid.NewGuid():N}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<AppDbContext>>();

    private static async Task<Guid> AddUserAsync(IDbContextFactory<AppDbContext> factory, string email)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = new AuthUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "x", Role = "User", IsActive = true,
        };
        db.AuthUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Test]
    public async Task ResolveAsync_ExplicitId_WinsWithoutTouchingUsers()
    {
        var factory = NewDb();
        await AddUserAsync(factory, "a@example.com");
        await AddUserAsync(factory, "b@example.com");
        var chosen = Guid.NewGuid();

        var result = await OwnerResolver.ResolveAsync(factory, chosen);

        Assert.That(result.OwnerId, Is.EqualTo(chosen));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ResolveAsync_SingleUser_IsTheDefaultOwner()
    {
        var factory = NewDb();
        var only = await AddUserAsync(factory, "solo@example.com");

        var result = await OwnerResolver.ResolveAsync(factory, null);

        Assert.That(result.Found, Is.True);
        Assert.That(result.OwnerId, Is.EqualTo(only));
    }

    [Test]
    public async Task ResolveAsync_NoUsers_FailsClosedWithCreateHint()
    {
        var result = await OwnerResolver.ResolveAsync(NewDb(), null);

        Assert.That(result.Found, Is.False);
        Assert.That(result.Error, Is.EqualTo(OwnerResolver.NoUsersMessage));
    }

    [Test]
    public async Task ResolveAsync_SeveralUsers_NeverGuesses()
    {
        var factory = NewDb();
        await AddUserAsync(factory, "a@example.com");
        await AddUserAsync(factory, "b@example.com");

        var result = await OwnerResolver.ResolveAsync(factory, null);

        Assert.That(result.Found, Is.False);
        Assert.That(result.Error, Is.EqualTo(OwnerResolver.AmbiguousMessage));
    }

    [Test]
    public async Task FixedOwnerContext_ReturnsWhatItWasGiven()
    {
        var id = Guid.NewGuid();
        Assert.That(await new FixedOwnerContext(id).GetOwnerIdAsync(), Is.EqualTo(id));
        Assert.That(await new FixedOwnerContext(null).GetOwnerIdAsync(), Is.Null);
    }
}
