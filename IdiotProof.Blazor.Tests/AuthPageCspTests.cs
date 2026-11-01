using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MindAttic.Authentication.Options;
using MindAttic.Authentication.Web;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T350: /login, /mfa and /account render through IdiotProof's App.razor but carry
/// MindAttic.Authentication's strict CSP (default-src 'self', script/style 'self' + a nonce).
/// App.razor used to pull Bootstrap from cdn.jsdelivr.net, fonts from Google Fonts and run an
/// inline theme script, all of which that CSP blocks, so the sign-in page loaded unstyled.
/// These read the real header the library sends and check every stylesheet, script and font the
/// shell loads against it, and that the CSP still forbids inline scripts.
/// </summary>
[TestFixture]
public sealed class AuthPageCspTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IdiotProof.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (IdiotProof.slnx) not found.");
    }

    private static string WwwRoot => Path.Combine(RepoRoot(), "IdiotProof.Blazor", "wwwroot");

    private static string AppShell() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "IdiotProof.Blazor", "Components", "App.razor"));

    /// <summary>Hosts just the library's middleware (as Program.cs wires it) and reads /login's CSP.</summary>
    private static async Task<string> AuthSurfaceCspAsync(string path = "/login")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddAuthorization();
        builder.Services.Configure<MfaOptions>(_ => { });
        await using var app = builder.Build();
        app.UseMindAtticAuthentication();
        app.MapGet(path, () => "ok");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            using var response = await http.GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.TryGetValues("Content-Security-Policy", out var values), Is.True,
                "MindAttic.Authentication no longer sets a CSP on the auth surface; revisit this test.");
            return string.Join(";", values!);
        }
        finally { await app.StopAsync(); }
    }

    private static Dictionary<string, string[]> Directives(string csp) =>
        csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Select(d => d.Split(' ', StringSplitOptions.RemoveEmptyEntries))
           .GroupBy(p => p[0].ToLowerInvariant())
           .ToDictionary(g => g.Key, g => g.First().Skip(1).ToArray());

    /// <summary>The CSP3 fallback chain: script-src-elem → script-src → default-src, etc.</summary>
    private static string[] Effective(Dictionary<string, string[]> d, params string[] chain)
    {
        foreach (var name in chain)
            if (d.TryGetValue(name, out var sources)) return sources;
        return [];
    }

    /// <summary>Whether a non-inline URL from the page is allowed by a source list (no nonce on it).</summary>
    private static bool Allows(string[] sources, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var abs) || abs.Scheme == "file")
            return sources.Contains("'self'");                         // same-origin relative URL
        return sources.Any(s => s == "*" || s.Equals($"{abs.Scheme}:", StringComparison.OrdinalIgnoreCase)
            || s.TrimEnd('/').Equals($"{abs.Scheme}://{abs.Host}", StringComparison.OrdinalIgnoreCase)
            || s.TrimEnd('/').Equals(abs.Host, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Attr(string html, string tag, string attr, string? mustContain = null) =>
        Regex.Matches(html, $@"<{tag}\b[^>]*>", RegexOptions.IgnoreCase)
             .Select(m => m.Value)
             .Where(t => mustContain is null || t.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
             .Select(t => Regex.Match(t, $@"\b{attr}\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase))
             .Where(m => m.Success)
             .Select(m => m.Groups[1].Value);

    [Test]
    public async Task AuthSurfaceCsp_StillForbidsInlineScriptsAndStyles()
    {
        var d = Directives(await AuthSurfaceCspAsync());
        var scripts = Effective(d, "script-src-elem", "script-src", "default-src");
        var styles = Effective(d, "style-src-elem", "style-src", "default-src");
        Assert.Multiple(() =>
        {
            Assert.That(scripts, Does.Not.Contain("'unsafe-inline'"));
            Assert.That(scripts, Does.Not.Contain("'unsafe-eval'"));
            Assert.That(styles, Does.Not.Contain("'unsafe-inline'"));
        });
    }

    [Test]
    public async Task AppShell_EveryStylesheetScriptAndFont_IsAllowedByTheAuthSurfaceCsp()
    {
        var d = Directives(await AuthSurfaceCspAsync());
        var scripts = Effective(d, "script-src-elem", "script-src", "default-src");
        var styles = Effective(d, "style-src-elem", "style-src", "default-src");
        var fonts = Effective(d, "font-src", "default-src");
        var shell = AppShell();

        var stylesheets = Attr(shell, "link", "href", "stylesheet").ToList();
        var scriptSrcs = Attr(shell, "script", "src").ToList();
        Assert.That(stylesheets, Is.Not.Empty);
        Assert.That(scriptSrcs, Is.Not.Empty);

        // Fonts come from the self-hosted stylesheets' url(...) references.
        var fontUrls = new List<string>();
        foreach (var css in stylesheets.Where(h => !h.StartsWith("_content/")))
        {
            var file = Path.Combine(WwwRoot, css.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(file), Is.True, $"stylesheet {css} is not in wwwroot");
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"url\(\s*[""']?([^""')]+?)[""']?\s*\)"))
            {
                var u = m.Groups[1].Value;
                if (u.StartsWith("data:")) continue;
                if (!u.Contains("://"))
                {
                    var local = Path.Combine(Path.GetDirectoryName(file)!, u.Split('?', '#')[0].Replace('/', Path.DirectorySeparatorChar));
                    if (Regex.IsMatch(u, @"\.(woff2?|ttf|otf)([?#]|$)"))
                        Assert.That(File.Exists(local), Is.True, $"{css} references missing font {u}");
                }
                if (Regex.IsMatch(u, @"\.(woff2?|ttf|otf)([?#]|$)") || u.Contains("fonts.gstatic.com")) fontUrls.Add(u);
            }
        }

        Assert.Multiple(() =>
        {
            foreach (var href in stylesheets) Assert.That(Allows(styles, href), Is.True, $"CSP style-src blocks stylesheet {href}");
            foreach (var src in scriptSrcs) Assert.That(Allows(scripts, src), Is.True, $"CSP script-src blocks script {src}");
            foreach (var f in fontUrls) Assert.That(Allows(fonts, f), Is.True, $"CSP font-src blocks font {f}");
            Assert.That(fontUrls, Is.Not.Empty, "expected the self-hosted Inter/Roboto Mono and Bootstrap Icons fonts");
        });
    }

    [Test]
    public void AppShell_HasNoInlineScripts()
    {
        var inline = Regex.Matches(AppShell(), @"<script\b(?![^>]*\bsrc\s*=)[^>]*>", RegexOptions.IgnoreCase)
                          .Select(m => m.Value).ToList();
        Assert.That(inline, Is.Empty, "an inline <script> in App.razor is blocked by the auth pages' CSP; move it to wwwroot/js");
    }
}
