using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Tests.Keys;
using IdiotProof.Operations.Users;
using MindAttic.Authentication.Entities;

namespace IdiotProof.Operations.Tests.Users;

/// <summary>`users list` (IP-T253) over an in-memory database.</summary>
public class UsersListOperationTests
{
    private KeysTestKit kit = null!;

    [SetUp]
    public void SetUp() => kit = new KeysTestKit();

    [TearDown]
    public void TearDown() => kit.Dispose();

    private async Task<Guid> AddUserAsync(string userName, string? email, DateTime created, string broker = "Sandbox")
    {
        await using var db = await kit.Db.CreateDbContextAsync();
        var user = new AuthUser
        {
            UserName = userName, NormalizedUserName = userName.ToUpperInvariant(),
            Email = email, NormalizedEmail = email?.ToUpperInvariant(),
            PasswordHash = "hash-never-shown", Role = "User", IsActive = true, CreatedUtc = created,
        };
        db.AuthUsers.Add(user);
        db.UserApiKeys.Add(new UserApiKeys { UserId = user.Id, DefaultBroker = broker });
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Test]
    public async Task List_HidesTestFixtures_StarsTheOwner_InEt()
    {
        var real = await AddUserAsync("trader@example.com", "trader@example.com", new DateTime(2026, 7, 20, 4, 24, 0, DateTimeKind.Utc), "alpaca");
        await AddUserAsync("options-1@idiotproof.local", "options-1@idiotproof.local", new DateTime(2026, 9, 6, 2, 0, 0, DateTimeKind.Utc));
        await AddUserAsync("gapper-verify", null, new DateTime(2026, 7, 19, 2, 0, 0, DateTimeKind.Utc));

        var result = await new UsersListOperation().ExecuteAsync(new UsersListArgs(), kit.Context(owner: real));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok));
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("trader@example.com").And.Contain(real.ToString()).And.Contain("alpaca"));
            Assert.That(result.Human, Does.Contain("2026-07-20 12:24 AM ET"));
            Assert.That(result.Human, Does.Not.Contain("idiotproof.local").And.Not.Contain("gapper-verify"));
            Assert.That(result.Human, Does.Contain("2 test-fixture account(s) hidden"));
            Assert.That(result.Human, Does.Not.Contain("hash-never-shown"));
        });

        var all = await new UsersListOperation().ExecuteAsync(new UsersListArgs { All = true }, kit.Context(owner: real));
        Assert.That(all.Human, Does.Contain("options-1@idiotproof.local").And.Contain("gapper-verify"));
    }
}
