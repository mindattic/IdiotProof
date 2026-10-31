using IdiotProof.Operations.Tests.Keys;
using IdiotProof.Operations.Users;
using Microsoft.Extensions.DependencyInjection;
using MindAttic.Authentication.Services;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Users;

/// <summary>`users create` (IP-T254) through a fake admin service and the real blocklist on an in-memory database.</summary>
public class UsersCreateOperationTests
{
    private KeysTestKit kit = null!;
    private FakeUserAdmin admin = null!;

    [SetUp]
    public void SetUp()
    {
        kit = new KeysTestKit();
        admin = new FakeUserAdmin();
        kit.Services.AddSingleton<IUserAdminService>(admin);
        kit.Services.AddSingleton<EmailDomainBlocklistService>();
    }

    [TearDown]
    public void TearDown() => kit.Dispose();

    [Test]
    public async Task Create_GoesThroughTheAdminService()
    {
        var result = await new UsersCreateOperation().ExecuteAsync(
            new UsersCreateArgs { Email = " new@example.com ", Password = "s3cretpass", Role = "admin" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Ok), result.Message);
        Assert.That(admin.Created.Single(), Is.EqualTo(("new@example.com", "Admin", false)));
        Assert.That(result.Human, Does.Contain("Created new@example.com (Admin)").And.Not.Contain("s3cretpass"));
    }

    [Test]
    public async Task Create_RefusesDisposableDomains_WeakPasswords_AndBadRoles()
    {
        var disposable = await new UsersCreateOperation().ExecuteAsync(new UsersCreateArgs { Email = "x@mailinator.com", Password = "s3cretpass" }, kit.Context());
        var weak = await new UsersCreateOperation().ExecuteAsync(new UsersCreateArgs { Email = "x@example.com", Password = "nodigits" }, kit.Context());
        var role = await new UsersCreateOperation().ExecuteAsync(new UsersCreateArgs { Email = "x@example.com", Password = "s3cretpass", Role = "Root" }, kit.Context());
        var malformed = await new UsersCreateOperation().ExecuteAsync(new UsersCreateArgs { Email = "not-an-email", Password = "s3cretpass" }, kit.Context());

        Assert.Multiple(() =>
        {
            Assert.That(disposable.Status, Is.EqualTo(OperationStatus.Blocked));
            Assert.That(weak.Status, Is.EqualTo(OperationStatus.UsageError));
            Assert.That(role.Status, Is.EqualTo(OperationStatus.UsageError));
            Assert.That(malformed.Status, Is.EqualTo(OperationStatus.UsageError));
            Assert.That(admin.Created, Is.Empty);
        });
    }

    [Test]
    public async Task Create_ReportsTheAdminServicesRefusal()
    {
        admin.Refuse = "A user with that name already exists.";

        var result = await new UsersCreateOperation().ExecuteAsync(new UsersCreateArgs { Email = "dup@example.com", Password = "s3cretpass" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("already exists"));
    }
}

/// <summary>Records CreateAsync calls; every other admin action is out of scope for these tests.</summary>
internal sealed class FakeUserAdmin : IUserAdminService
{
    public List<(string Email, string Role, bool MustChange)> Created { get; } = [];

    public string? Refuse { get; set; }

    public Task<CreateUserResult> CreateAsync(string userName, string? email, string role, string password,
        bool mustChangePassword = true, string? displayName = null, CancellationToken ct = default)
    {
        if (Refuse is not null) return Task.FromResult(new CreateUserResult(false, null, Refuse));
        Created.Add((email!, role, mustChangePassword));
        return Task.FromResult(new CreateUserResult(true, Guid.NewGuid(), null));
    }

    public Task<IReadOnlyList<AuthUserSummary>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AuthUserSummary?> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> CountAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AdminActionResult> SetRoleAsync(Guid id, string role, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AdminActionResult> SetActiveAsync(Guid id, bool active, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AdminActionResult> UpdateProfileAsync(Guid id, string? email, string? displayName = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AdminActionResult> ResetPasswordAsync(Guid id, string newPassword, bool requireChange = true, CancellationToken ct = default) => throw new NotSupportedException();
}
