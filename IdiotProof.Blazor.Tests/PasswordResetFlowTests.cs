using System.Reflection;
using System.Security.Cryptography;
using Bunit;
using IdiotProof.Blazor.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MindAttic.Authentication.Crypto;
using MindAttic.Authentication.Entities;
using MindAttic.Authentication.Options;
using MindAttic.Authentication.Secrets;
using MindAttic.Authentication.Services;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Self-service password reset, end to end over IdiotProof's own pieces: the Development public
/// base URL, the reset page at the library's ResetPath, and AppDbContext as the auth store. A fake
/// sender captures the email; nothing is sent.
/// </summary>
[TestFixture]
public class PasswordResetFlowTests
{
    private const string OldPassword = "Initial-Passphrase-1";
    private const string NewPassword = "Brand-New-Passphrase-42";

    private sealed class CapturingSender : IAuthEmailSender
    {
        public List<string> Links { get; } = new();
        public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken ct = default) { Links.Add(resetLink); return Task.CompletedTask; }
        public Task SendSecurityAlertAsync(string toEmail, string subject, string body, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AcceptAll : IPasswordPolicy
    {
        public Task<PasswordPolicyResult> ValidateAsync(string password, Guid? userId = null, CancellationToken ct = default) =>
            Task.FromResult(new PasswordPolicyResult(true, null));
    }

    private sealed class NoAudit : IAuthAuditWriter
    {
        public Task WriteAsync(AuthAuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(NUnit.Framework.TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IdiotProof.slnx"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "Could not locate the repo root (IdiotProof.slnx).");
        return dir!;
    }

    private static Type PageAt(string route) =>
        typeof(IdiotProof.Blazor.Components.App).Assembly.GetTypes()
            .Single(t => t.GetCustomAttributes<RouteAttribute>().Any(r => r.Template == route));

    /// <summary>The reset options exactly as the app binds them in Development.</summary>
    private static AuthResetOptions DevResetOptions()
    {
        var blazor = Path.Combine(RepoRoot().FullName, "IdiotProof.Blazor");
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(blazor, "appsettings.json"))
            .AddJsonFile(Path.Combine(blazor, "appsettings.Development.json"))
            .Build();
        return config.GetSection("MindAttic:Auth:Reset").Get<AuthResetOptions>() ?? new AuthResetOptions();
    }

    [Test]
    public void DevelopmentPublicBaseUrl_IsTheLaunchProfileOrigin()
    {
        var launch = File.ReadAllText(Path.Combine(RepoRoot().FullName, "IdiotProof.Blazor", "Properties", "launchSettings.json"));
        var origin = DevResetOptions().PublicBaseUrl;
        Assert.That(Uri.TryCreate(origin, UriKind.Absolute, out _), Is.True, "PublicBaseUrl must be absolute");
        Assert.That(launch, Does.Contain(origin), "reset links must point at the https origin the app runs on");
    }

    [Test]
    public void AzureWebApp_SetsItsOwnPublicBaseUrl()
    {
        var bicep = File.ReadAllText(Path.Combine(RepoRoot().FullName, "infra", "main.bicep"));
        Assert.That(bicep, Does.Contain("name: 'MindAttic__Auth__Reset__PublicBaseUrl'"));
        Assert.That(bicep, Does.Contain("value: 'https://${webAppName}.azurewebsites.net'"));
    }

    [Test]
    public void TheResetAndForgotPages_AreAnonymous()
    {
        foreach (var route in new[] { new AuthResetOptions().ResetPath, "/forgot-password" })
            Assert.That(PageAt(route).GetCustomAttributes(typeof(AllowAnonymousAttribute), true), Is.Not.Empty, route);
    }

    [Test]
    public void ForgotPasswordPage_PostsToTheLibraryRequestEndpoint()
    {
        using var ctx = new BunitContext();
        var cut = ctx.Render(b => { b.OpenComponent(0, PageAt("/forgot-password")); b.CloseComponent(); });
        Assert.That(cut.Find("form").GetAttribute("action"), Is.EqualTo("/_ma-auth/reset/request"));
        Assert.That(cut.FindAll("form[action='/forgot-password-submit']"), Is.Empty, "the direct, token-less reset is gone");
    }

    [Test]
    public async Task RequestReset_EmailsAnAbsoluteLinkToTheResetPage_WhichResetsThePassword()
    {
        var options = DevResetOptions();

        var secrets = new ConfigAuthSecrets(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{ConfigAuthSecrets.SectionPath}:pepper.v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            [$"{ConfigAuthSecrets.SectionPath}:reset-token-key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        }).Build());
        var hasher = new Argon2idPasswordHasher(secrets, Microsoft.Extensions.Options.Options.Create(new AuthCryptoOptions
        {
            MemoryKiB = AuthCryptoOptions.FloorMemoryKiB, Iterations = AuthCryptoOptions.FloorIterations,
            Parallelism = AuthCryptoOptions.FloorParallelism,
        }));
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"reset-{Guid.NewGuid():N}").Options);
        var stored = hasher.Hash(OldPassword);
        db.AuthUsers.Add(new AuthUser
        {
            UserName = "alice@example.com", NormalizedUserName = IUserStore.Normalize("alice@example.com"),
            Email = "alice@example.com", NormalizedEmail = "ALICE@EXAMPLE.COM",
            PasswordHash = stored.Phc, PasswordPepperKeyId = stored.PepperKeyId, Role = "User", IsActive = true,
        });
        await db.SaveChangesAsync();

        var sender = new CapturingSender();
        var reset = new PasswordResetService(new UserStore(db), db, hasher, new AcceptAll(), secrets, sender, new NoAudit(),
            Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System);

        await reset.RequestAsync("alice@example.com", "203.0.113.7", "agent");

        // The email carries an absolute link to the app's reset page.
        var link = new Uri(sender.Links.Single());
        Assert.That(link.GetLeftPart(UriPartial.Authority), Is.EqualTo(options.PublicBaseUrl.TrimEnd('/')));
        Assert.That(link.AbsolutePath, Is.EqualTo(options.ResetPath));
        var token = Uri.UnescapeDataString(link.Query.TrimStart('?').Split('&')
            .Single(p => p.StartsWith("token=", StringComparison.Ordinal))["token=".Length..]);

        // Following the link renders the page with the token in the form that posts to the confirm endpoint.
        using var ctx = new BunitContext();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"{options.ResetPath}{link.Query}");
        var cut = ctx.Render(b => { b.OpenComponent(0, PageAt(options.ResetPath)); b.CloseComponent(); });
        Assert.That(cut.Find("form").GetAttribute("action"), Is.EqualTo("/_ma-auth/reset/confirm"));
        Assert.That(cut.Find("input[name=token]").GetAttribute("value"), Is.EqualTo(token));

        // Submitting it (the endpoint calls ConfirmAsync) sets the new password.
        var result = await reset.ConfirmAsync(token, NewPassword);
        Assert.That(result.Ok, Is.True, result.Error);
        var user = db.AuthUsers.Single();
        Assert.That(hasher.Verify(NewPassword, user.PasswordHash, user.PasswordPepperKeyId, null).Succeeded, Is.True);
        Assert.That(hasher.Verify(OldPassword, user.PasswordHash, user.PasswordPepperKeyId, null).Succeeded, Is.False);
    }
}
