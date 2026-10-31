using Bunit;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T335: the settings tab is a portable component (IdiotProof.Settings RCL) that works with only
/// a user id and the core services: no route, no auth provider, no host JS. That is what lets
/// MindAttic.Vault.Desktop/Web host it unmodified.
/// </summary>
[TestFixture]
public sealed class IdiotProofSettingsTabTests
{
    private static BunitContext NewHost(out IDbContextFactory<AppDbContext> db)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose; // a host without ipGetTheme/ipSetTheme
        ctx.Services.AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"settings-tab-{Guid.NewGuid():N}"));
        ctx.Services.AddSingleton<UserPreferencesService>();
        ctx.Services.AddSingleton<RiskGuardianService>();
        db = ctx.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return ctx;
    }

    [Test]
    public void RendersWithJustAUserId_AndSavesClampedLimits()
    {
        using var ctx = NewHost(out _);
        var user = Guid.NewGuid();

        var tab = ctx.Render<IdiotProofSettings>(p => p.Add(t => t.UserId, user));
        Assert.That(tab.Find("#risk-per-trade").GetAttribute("value"), Is.EqualTo("100"));

        tab.Find("#risk-per-day").Change("40"); // below the per-trade cap: clamped up to it
        tab.Find("button.btn-success").Click();

        tab.WaitForAssertion(() => Assert.That(tab.Markup, Does.Contain("Saved.")));
        var prefs = ctx.Services.GetRequiredService<UserPreferencesService>().GetOrCreateAsync(user).GetAwaiter().GetResult();
        Assert.That(prefs.RiskMaxLossPerDay, Is.EqualTo(100m));
    }

    [Test]
    public void NoUser_ShowsSignIn_AndCannotSave()
    {
        using var ctx = NewHost(out _);

        var tab = ctx.Render<IdiotProofSettings>(p => p.Add(t => t.ApiKeysHref, "vault://keys"));

        Assert.That(tab.Markup, Does.Contain("Sign in to edit your risk limits."));
        Assert.That(tab.Find("button.btn-success").HasAttribute("disabled"), Is.True);
        Assert.That(tab.Find("a").GetAttribute("href"), Is.EqualTo("vault://keys"));
    }
}
