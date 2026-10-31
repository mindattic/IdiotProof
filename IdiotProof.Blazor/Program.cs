using IdiotProof.Engine.Storage;
using IdiotProof.Blazor.Auth;
using IdiotProof.Blazor.Components;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Hubs;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Identity;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;
using MindAttic.Authentication.Web;
using MindAttic.Vault.Paths;

var builder = WebApplication.CreateBuilder(args);

// When running as a published exe from a non-source directory (local deploy), the
// dev-mode static-web-assets manifest is absent so WebRootPath can be null. Pin it.
builder.Environment.WebRootPath ??= Path.Combine(builder.Environment.ContentRootPath, "wwwroot");

// .env autoload (Development only). DEV_USERNAME / DEV_PASSWORD live in the roaming
// MindAttic store at %APPDATA%\MindAttic\IdiotProof\.env — outside the repo, so no
// credential file is ever checked out. The Login page prefills from these during
// local debug runs. Never loaded in any non-Development build.
if (builder.Environment.IsDevelopment())
{
    var envPath = Path.Combine(VaultPaths.RoamingBucket("IdiotProof"), ".env");
    if (File.Exists(envPath))
    {
        foreach (var raw in File.ReadAllLines(envPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            if (val.Length >= 2 && val[0] == '"' && val[^1] == '"') val = val[1..^1];
            Environment.SetEnvironmentVariable(key, val);
        }
    }
}

// Cloud-native configuration chain. Layered (later sources win):
//   AddJsonFile (already added by WebApplicationBuilder for appsettings.json).
//   AddIdiotProofVault surfaces %APPDATA%\MindAttic\<bucket>\providers.json on dev (incl. Security).
//   AddEnvironmentVariables (already present) picks up App Service Application
//     Settings and Azure Key Vault references in production.
builder.Configuration.AddIdiotProofVault();

// ── Storage ──────────────────────────────────────────────────────────────────────
// Resolves to %LOCALAPPDATA%\IdiotProof (or $IDIOTPROOF_DATA_DIR if set) so the CLI
// runner and the Blazor server share the same Workspaces/Settings/Data tree.
var storageProvider = new WebStorageProvider();
storageProvider.EnsureDirectories();

// ── Blazor ───────────────────────────────────────────────────────────────────────
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

// ── The shared IdiotProof graph (HOUSE-LAW-6) ────────────────────────────────────
// SQL (env ConnectionStrings__IdiotProof → appsettings → LocalDB), Vault, the AppSettings
// overlay chain, the SQL workspace store, MindAttic.Authentication on the shared Data
// Protection key ring (Blob + Key Vault, else DataProtection:KeyRingPath, else the library's
// dev ring; production with neither fails closed), MindAttic.Legion + the voter panel, and
// every strategy/broker/risk/research service. The Hub and the Monitor register the same graph.
//
// Strategy evaluation AND order execution are owned by the Monitor (IP-LAW-10). This host
// writes user edits to SQL; the Monitor re-reads them every tick. Do not resurrect a second
// evaluation loop here.
builder.Services.AddIdiotProofCore(builder.Configuration, storageProvider, new CoreHostOptions
{
    IsProduction = !builder.Environment.IsDevelopment(),
    // E2E seam: IDIOTPROOF_FAKE_LLM=1 (Development only) re-points the LegionClient transport at
    // FakeLlmHandler so Cypress runs are deterministic and never call a vendor (cy.intercept can't
    // see server-side HTTP). Pair it with a dummy ClaudeApiKey env var.
    UseFakeLlm = builder.Environment.IsDevelopment()
        && Environment.GetEnvironmentVariable("IDIOTPROOF_FAKE_LLM") == "1",
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery();

// ── SignalR ───────────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 32 * 1024;
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// ── Web-only services ────────────────────────────────────────────────────────────
// Who this request acts for: the signed-in user's NameIdentifier claim (the Hub/CLI/MCP use
// OperationOwnerContext). Read per call; anonymous means no owner and callers fail closed.
builder.Services.AddScoped<IOwnerContext, SignedInOwnerContext>();
// The requesting user's Claude key (their own, then the host's) for Describe, the Gapper
// transcript reader and Research; resolved per call, never cached across users.
// The same OwnerClaudeKeyContext the Hub uses, over the signed-in owner above; the resolver
// takes the user's own key, then the host key from the MindAttic Vault keyring (IP-T330).
builder.Services.AddScoped<IClaudeKeyContext, OwnerClaudeKeyContext>();
// The Activity page's Commands list: the owner's CLI/MCP CommandLedger rows.
builder.Services.AddScoped<CommandActivityService>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddSingleton<LogAlertService>();
// Pusher singletons: registered as both injectable singleton and hosted service so
// pages can @inject them directly to subscribe to change events.
builder.Services.AddSingleton<LiveBarPusher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveBarPusher>());
builder.Services.AddSingleton<ConditionProgressPusher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ConditionProgressPusher>());
builder.Services.AddSingleton<AuditLogPusher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuditLogPusher>());

// Dev credential carrier — populated from .env only in Development.
// In Production this resolves to a singleton with both fields null, so the
// Login page renders empty inputs as it always has.
builder.Services.AddSingleton(new DevCredentials(
    builder.Environment.IsDevelopment() ? Environment.GetEnvironmentVariable("DEV_USERNAME") : null,
    builder.Environment.IsDevelopment() ? Environment.GetEnvironmentVariable("DEV_PASSWORD") : null));

// ── App ──────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Apply pending EF migrations on startup. Creates the IdiotProof database on
// LocalDB if missing, then keeps schema in sync with the codebase.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Best-effort one-shots — NEVER let a backfill/seed failure abort web
    // startup (the app must come up even if these hiccup).
    try
    {
        // One-shot (IP-LAW-8): legacy strategies get their canonical JSON derived
        // from ScriptText so the Monitor can run JSON-first everywhere.
        var backfilled = await scope.ServiceProvider.GetRequiredService<StrategyRepository>()
            .BackfillCanonicalJsonAsync();
        if (backfilled > 0)
            app.Logger.LogInformation("Backfilled canonical ScriptJson for {Count} legacy strategies.", backfilled);
    }
    catch (Exception ex) { app.Logger.LogError(ex, "Canonical-JSON backfill failed at startup (continuing)."); }

    try
    {
        // Seed the disposable-email-domain blocklist. Idempotent.
        var seededDomains = await scope.ServiceProvider.GetRequiredService<EmailDomainBlocklistService>()
            .SeedAsync();
        if (seededDomains > 0)
            app.Logger.LogInformation("Seeded {Count} disposable email domains into the blacklist.", seededDomains);
    }
    catch (Exception ex) { app.Logger.LogError(ex, "Email-domain blocklist seed failed at startup (continuing)."); }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
// UseStaticFiles with an explicit PhysicalFileProvider so published-exe-as-Development
// serves files from the published wwwroot. MapStaticAssets (below) uses the dev-mode
// static-web-assets manifest which resolves source paths — those don't exist in the
// publish output, so MapStaticAssets returns Content-Length:0 for every file.
// AppContext.BaseDirectory is the exe's own folder — always correct for a published exe
// regardless of the process working directory, which varies by how the bat is launched. A plain
// `dotnet run` has no wwwroot under bin/, so fall back to the project's web root there.
var staticRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (!Directory.Exists(staticRoot)) staticRoot = app.Environment.WebRootPath;
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(staticRoot)
});
// UseMindAtticAuthentication wires UseAuthentication + UseAuthorization + forced-step
// (MFA, must-change-password) middleware. Call after UseStaticFiles, before mapping.
app.UseMindAtticAuthentication();
app.UseAntiforgery();

// ── Auth routes (/_ma-auth/*) ─────────────────────────────────────────────────────
// Login, logout, MFA challenge, change-password, and reset flows are owned by the
// library. Login.razor posts to /_ma-auth/login; no custom /login-submit needed.
app.MapMindAtticAuthEndpoints();

// ── Register endpoint (no self-registration in MA; use IUserAdminService) ─────────
// Registration is kept as a custom form endpoint so new users can create their own
// accounts without needing an admin UI. After creation, the user signs in via the
// library's /_ma-auth/login flow.
app.MapPost("/register-submit", async (HttpContext ctx, IUserAdminService adminSvc, EmailDomainBlocklistService blocklist) =>
{
    var form     = await ctx.Request.ReadFormAsync();
    var email    = form["email"].ToString().Trim();
    var password = form["password"].ToString();
    var confirm  = form["confirm"].ToString();

    if (string.IsNullOrWhiteSpace(email))
    { ctx.Response.Redirect("/register?error=email"); return; }
    // Reject malformed and disposable/temporary email domains — a
    // real account for a real (paper) trading key needs a real inbox. Give a
    // distinct message for a malformed address vs a disposable domain.
    if (EmailDomainBlocklistService.DomainOf(email) is null)
    { ctx.Response.Redirect("/register?error=email"); return; }
    if (await blocklist.IsBlockedAsync(email))
    { ctx.Response.Redirect("/register?error=domain"); return; }
    if (password != confirm)
    { ctx.Response.Redirect("/register?error=mismatch"); return; }
    if (password.Length < 8)
    { ctx.Response.Redirect("/register?error=short"); return; }
    // Upper bound: Argon2id cost scales with input size, so an unbounded
    // password is a cheap CPU-exhaustion vector on an anonymous endpoint.
    if (password.Length > 128)
    { ctx.Response.Redirect("/register?error=long"); return; }
    // The UI advertises "8+ chars, one digit" — enforce the digit half too.
    if (!password.Any(char.IsDigit))
    { ctx.Response.Redirect("/register?error=digit"); return; }

    var atIndex = email.IndexOf('@');
    var defaultDisplayName = atIndex > 0 ? email[..atIndex] : email;
    var result = await adminSvc.CreateAsync(
        userName: email, email: email, role: "User",
        password: password, mustChangePassword: false, displayName: defaultDisplayName);

    if (!result.Ok)
    {
        ctx.Response.Redirect($"/register?error={Uri.EscapeDataString(result.Error ?? "create")}");
        return;
    }

    // Account created — the user is NOT signed in yet (no cookie issued here;
    // sign-in is the library's /_ma-auth/login flow). Send them to login with
    // a success flag so the page can say "account created, please sign in"
    // instead of the button implying they're already in.
    ctx.Response.Redirect("/login?registered=1");
});

// ── Password reset ────────────────────────────────────────────────────────────────
// Self-service reset is the library's token flow: /forgot-password (MaForgotPassword)
// posts to /_ma-auth/reset/request, which emails a single-use link to
// MindAttic:Auth:Reset:PublicBaseUrl + /account/reset (ResetPassword.razor,
// MaResetPassword → /_ma-auth/reset/confirm). Mail goes over SMTP from the Vault
// Notifications bucket; without it the library logs a startup warning and sends nothing.

// ── Alpaca OAuth / Connect — account linking instead of raw keys ──
// DORMANT: obtains + stores a scoped token; trading still routes through the
// key/secret path until Bearer mode is paper-verified. Wholly inert unless
// Alpaca:OAuth:ClientId/:ClientSecret/:RedirectUri are configured.
app.MapGet("/connect/alpaca", (HttpContext ctx, IConfiguration cfg) =>
{
    var clientId = cfg["Alpaca:OAuth:ClientId"];
    var redirect = cfg["Alpaca:OAuth:RedirectUri"];
    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirect))
    {
        ctx.Response.StatusCode = 503;
        return ctx.Response.WriteAsync("Alpaca OAuth is not configured (set Alpaca:OAuth:ClientId / :ClientSecret / :RedirectUri).");
    }
    if (ctx.User.Identity?.IsAuthenticated != true) { ctx.Response.Redirect("/login?returnUrl=/connect/alpaca"); return Task.CompletedTask; }
    var state = Guid.NewGuid().ToString("N");
    ctx.Response.Cookies.Append("ip_oauth_state", state,
        new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10) });
    ctx.Response.Redirect(IdiotProof.Brokers.AlpacaOAuthClient.BuildAuthorizeUrl(clientId, redirect, state));
    return Task.CompletedTask;
});

app.MapGet("/connect/alpaca/callback", async (HttpContext ctx, IConfiguration cfg, UserKeyService keys) =>
{
    var clientId = cfg["Alpaca:OAuth:ClientId"];
    var clientSecret = cfg["Alpaca:OAuth:ClientSecret"];
    var redirect = cfg["Alpaca:OAuth:RedirectUri"];
    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(redirect))
    { ctx.Response.StatusCode = 503; await ctx.Response.WriteAsync("Alpaca OAuth is not configured."); return; }

    var code  = ctx.Request.Query["code"].ToString();
    var state = ctx.Request.Query["state"].ToString();
    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || state != ctx.Request.Cookies["ip_oauth_state"])
    { ctx.Response.Redirect("/?oauth=state_error"); return; }

    var userIdStr = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userIdStr, out var userId)) { ctx.Response.Redirect("/login?returnUrl=/connect/alpaca"); return; }

    var token = await IdiotProof.Brokers.AlpacaOAuthClient.ExchangeCodeAsync(clientId, clientSecret, code, redirect);
    if (token is null) { ctx.Response.Redirect("/?oauth=exchange_failed"); return; }

    var existing = await keys.GetOrCreateAsync(userId);
    existing.UserId = userId;
    existing.AlpacaOAuthAccessToken  = token.AccessToken;
    existing.AlpacaOAuthRefreshToken = token.RefreshToken;
    existing.AlpacaOAuthScope        = token.Scope;
    await keys.SaveAsync(userId, existing);   // encrypted at rest; NOT yet routed (dormant)
    ctx.Response.Redirect("/?oauth=connected");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .WithStaticAssets();

app.MapHub<TradingHub>("/hubs/trading");

app.Run();
