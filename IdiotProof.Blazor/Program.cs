using IdiotProof.Engine;
using IdiotProof.Engine.Storage;
using IdiotProof.Blazor.Components;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Hubs;
using IdiotProof.Blazor.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;
using MindAttic.Authentication.Web;
using MindAttic.Legion;
using MindAttic.Vault.Configuration;
using MindAttic.Vault.DependencyInjection;
using MindAttic.Vault.Paths;

var builder = WebApplication.CreateBuilder(args);

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
//   AddMindAtticVaultFiles surfaces %APPDATA%\MindAttic\<bucket>\providers.json on dev.
//   AddEnvironmentVariables (already present) picks up App Service Application
//     Settings and Azure Key Vault references in production.
builder.Configuration
    .AddMindAtticVaultFiles();

// Vault: cloud-native credential resolvers (LlmCredentialResolver,
// BrokerCredentialResolver) registered alongside the legacy file-backed stores.
builder.Services.AddMindAtticVault(builder.Configuration);

// ── Storage ──────────────────────────────────────────────────────────────────────
// Resolves to %LOCALAPPDATA%\IdiotProof (or $IDIOTPROOF_DATA_DIR if set) so the CLI
// runner and the Blazor server share the same Workspaces/Settings/Data tree.
var storageProvider = new WebStorageProvider();
storageProvider.EnsureDirectories();

// ── Blazor ───────────────────────────────────────────────────────────────────────
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Database ─────────────────────────────────────────────────────────────────────
// Connection string priority: env var ConnectionStrings__IdiotProof →
// appsettings ConnectionStrings:IdiotProof → LocalDB fallback. Same pattern as
// StreetSamurai. Runtime + design-time (AppDbContextFactory) resolve identically.
var connStr =
    Environment.GetEnvironmentVariable("ConnectionStrings__IdiotProof")
    ?? builder.Configuration.GetConnectionString("IdiotProof")
    ?? @"Server=(localdb)\MSSQLLocalDB;Database=IdiotProof;Trusted_Connection=True;TrustServerCertificate=True;";

builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer(connStr));
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// ── Authentication (MindAttic.Authentication — replaces ASP.NET Core Identity) ───
// Registers Argon2id+pepper hashing, session management, MFA, lockout, audit trail,
// and the MA cookie scheme ("MindAttic.Auth"). The Security vault bucket
// (%APPDATA%\MindAttic\Security\providers.json) must supply pepper.v1 and
// bootstrap-token before startup.
builder.Services.AddMindAtticAuthentication<AppDbContext>(
    builder.Configuration,
    opts =>
    {
        opts.AppName = "IdiotProof";
        opts.IsProduction = !builder.Environment.IsDevelopment();

        // Production key-ring persistence (IP-A9). The library fail-closes in
        // production without this. DataProtection:KeyRingPath must point at
        // durable storage shared by every instance AND by the Monitor console
        // (which reads the same ring to decrypt per-user API keys) — on Azure
        // App Service, %HOME%\data\dp-keys works (durable, instance-shared).
        // Upgrade path: swap for Azure Blob + Key Vault when infra lands.
        var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            opts.ConfigureDataProtection = dp =>
                dp.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }
        // else: dev uses the library's DevKeyRingPath convention
        // (%APPDATA%\MindAttic\DataProtection\IdiotProof); production without
        // the config key still fail-closes — that's the intended posture.
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery();

// ── Engine ───────────────────────────────────────────────────────────────────────
// Register the SQL-backed workspace store before AddIdiotProofEngine so the
// TryAddSingleton inside the engine picks it up instead of the JSON-on-disk default.
builder.Services.AddSingleton<IdiotProof.Engine.Workspace.IWorkspaceStore>(sp =>
    new IdiotProof.Blazor.Services.SqlWorkspaceStore(
        sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
        storageProvider));

builder.Services.AddIdiotProofEngine(storageProvider, builder.Configuration);

// ── SignalR ───────────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 32 * 1024;
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// ── Web services ─────────────────────────────────────────────────────────────────
// Strategy evaluation AND order execution are owned by IdiotProof.Monitor (the
// second startup project) — the single pipeline per RFC 0002 / IP-A8. The
// Blazor host writes user edits to SQL; the Monitor re-reads them every tick
// (default 5s), so UI changes apply to the running console automatically. The
// old in-process StrategyExecutionService (WorkspaceTab-binding evaluation)
// was deleted 2026-07-18 — do not resurrect a second evaluation loop here.
builder.Services.AddSingleton<TradingStateService>();
// MindAttic.Legion is the gateway for all LLM communication — register the
// universal client before any service that talks to an LLM.
builder.Services.AddLegionClient();
// E2E test seam: IDIOTPROOF_FAKE_LLM=1 (Development only) re-points the
// LegionClient transport at FakeLlmHandler so Cypress runs are deterministic
// and never call a vendor. Cy.intercept can't see server-side HTTP, so the
// seam has to live here. Pair it with a dummy ClaudeApiKey env var.
if (builder.Environment.IsDevelopment()
    && Environment.GetEnvironmentVariable("IDIOTPROOF_FAKE_LLM") == "1")
{
    builder.Services.AddTransient<LegionClient>(_ =>
        new LegionClient(new HttpClient(new FakeLlmHandler()), options: null));
}
builder.Services.AddSingleton<IdiotProof.Blazor.Services.LlmVotingService>();
builder.Services.AddScoped<UserKeyService>();
builder.Services.AddSingleton<StrategyRepository>();
builder.Services.AddSingleton<UserPreferencesService>();
builder.Services.AddSingleton<StrategyScriptGenerator>();
builder.Services.AddSingleton<GapperProfileService>();
builder.Services.AddSingleton<GapperInterpreter>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<AuditLogRepository>();
builder.Services.AddSingleton<TradeDiaryRepository>();
builder.Services.AddSingleton<EmailDomainBlocklistService>();
builder.Services.AddSingleton<ConditionProgressRepository>();
builder.Services.AddSingleton<RiskGuardianService>();
builder.Services.AddHttpClient();

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

    // IP-A13 one-shot: legacy strategies get their canonical JSON derived
    // from ScriptText so the Monitor can run JSON-first everywhere.
    var backfilled = await scope.ServiceProvider.GetRequiredService<StrategyRepository>()
        .BackfillCanonicalJsonAsync();
    if (backfilled > 0)
        app.Logger.LogInformation("Backfilled canonical ScriptJson for {Count} legacy strategies.", backfilled);

    // Seed the disposable-email-domain blocklist (IP-A23). Idempotent.
    var seededDomains = await scope.ServiceProvider.GetRequiredService<EmailDomainBlocklistService>()
        .SeedAsync();
    if (seededDomains > 0)
        app.Logger.LogInformation("Seeded {Count} disposable email domains into the blacklist.", seededDomains);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
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
    // Reject malformed and disposable/temporary email domains (IP-A23) — a
    // real account for a real (paper) trading key needs a real inbox.
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

    var result = await adminSvc.CreateAsync(
        userName: email, email: email, role: "User",
        password: password, mustChangePassword: false);

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

// ── Forgot-password reset (DEVELOPMENT ONLY — mapped inside the env gate) ─────────
// This endpoint resets a password on nothing more than a matching email: no
// token, no old password, no session. That is an unauthenticated account
// takeover for ANY user if it is ever reachable in production, so it is only
// mapped in Development. Production uses the library's token-based
// /_ma-auth/reset/* flow once an email sender exists; until then the
// ForgotPassword page tells production users to contact the administrator.
if (app.Environment.IsDevelopment())
app.MapPost("/forgot-password-submit", async (HttpContext ctx, IUserAdminService adminSvc) =>
{
    var form     = await ctx.Request.ReadFormAsync();
    var email    = form["email"].ToString().Trim();
    var password = form["password"].ToString();
    var confirm  = form["confirm"].ToString();

    if (string.IsNullOrWhiteSpace(email))
    { ctx.Response.Redirect("/forgot-password?error=email"); return; }
    if (password != confirm)
    { ctx.Response.Redirect("/forgot-password?error=mismatch"); return; }
    if (password.Length < 8)
    { ctx.Response.Redirect("/forgot-password?error=short"); return; }
    if (password.Length > 128)
    { ctx.Response.Redirect("/forgot-password?error=long"); return; }
    if (!password.Any(char.IsDigit))
    { ctx.Response.Redirect("/forgot-password?error=digit"); return; }

    var users = await adminSvc.ListAsync();
    var user  = users.FirstOrDefault(u =>
        string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    if (user is null)
    { ctx.Response.Redirect("/forgot-password?error=unknown"); return; }

    var result = await adminSvc.ResetPasswordAsync(user.Id, password, requireChange: false);
    if (!result.Ok)
    {
        ctx.Response.Redirect($"/forgot-password?error={Uri.EscapeDataString(result.Error ?? "reset")}");
        return;
    }

    ctx.Response.Redirect("/forgot-password?status=ok");
});

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapHub<TradingHub>("/hubs/trading");

app.Run();
