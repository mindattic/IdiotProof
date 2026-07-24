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
//   AddMindAtticVaultFiles surfaces %APPDATA%\MindAttic\<bucket>\providers.json on dev.
//   AddEnvironmentVariables (already present) picks up App Service Application
//     Settings and Azure Key Vault references in production.
builder.Configuration
    .AddMindAtticVaultFiles(src => src.Buckets = [..src.Buckets, "Security"]);

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
        // production without this. The ring must be durable storage shared by
        // every instance AND by the Monitor console (which reads the same ring
        // to decrypt per-user API keys).
        //
        // Two supported prod shapes, picked by which config keys are present:
        //   - DataProtection:AzureBlobUri + DataProtection:KeyVaultKeyUri — Azure
        //     Blob Storage + Key Vault (the multi-instance/Container-Apps shape;
        //     matches the pattern already used by StreetSamurai/Tutor). Auth via
        //     DefaultAzureCredential (managed identity in Azure, az/VS login in dev).
        //   - DataProtection:KeyRingPath — a single durable file path (e.g. Azure
        //     App Service's %HOME%\data\dp-keys, which is instance-shared but not
        //     multi-region). Kept for the current on-box/single-instance setup.
        // Neither key set → dev uses the library's DevKeyRingPath convention
        // (%APPDATA%\MindAttic\DataProtection\IdiotProof); production without
        // either still fail-closes — that's the intended posture.
        var blobUri  = builder.Configuration["DataProtection:AzureBlobUri"];
        var kvKeyUri = builder.Configuration["DataProtection:KeyVaultKeyUri"];
        var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(blobUri) && !string.IsNullOrWhiteSpace(kvKeyUri))
        {
            var credential = new Azure.Identity.DefaultAzureCredential();
            opts.ConfigureDataProtection = dp =>
                dp.PersistKeysToAzureBlobStorage(new Uri(blobUri), credential)
                  .ProtectKeysWithAzureKeyVault(new Uri(kvKeyUri), credential);
        }
        else if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            opts.ConfigureDataProtection = dp =>
                dp.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }
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
builder.Services.AddScoped<AccountSummaryService>();
builder.Services.AddSingleton<StrategyRepository>();
builder.Services.AddSingleton<UserPreferencesService>();
builder.Services.AddSingleton<StrategyScriptGenerator>();
builder.Services.AddSingleton<GapperProfileService>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddSingleton<GapperInterpreter>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<AuditLogRepository>();
builder.Services.AddSingleton<LogAlertService>();
builder.Services.AddSingleton<TradeDiaryRepository>();
builder.Services.AddSingleton<EmailDomainBlocklistService>();
builder.Services.AddSingleton<ConditionProgressRepository>();
builder.Services.AddSingleton<LiveBarRepository>();
builder.Services.AddSingleton<RiskGuardianService>();
// Pusher singletons: registered as both injectable singleton and hosted service so
// pages can @inject them directly to subscribe to change events.
builder.Services.AddSingleton<LiveBarPusher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveBarPusher>());
builder.Services.AddSingleton<ConditionProgressPusher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ConditionProgressPusher>());
builder.Services.AddSingleton<AuditLogPusher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuditLogPusher>());
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("edgar", c =>
{
    // SEC EDGAR requires a descriptive User-Agent per their access policy
    c.DefaultRequestHeaders.UserAgent.ParseAdd("IdiotProof/1 research@idiotproof.app");
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("usspends", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<EdgarService>();
builder.Services.AddScoped<UsSpendsService>();
builder.Services.AddScoped<AlpacaNewsService>();
builder.Services.AddScoped<CatalystExtractor>();
builder.Services.AddScoped<ClaimVectorService>();
builder.Services.AddSingleton<ClaimCorrelationService>();
builder.Services.AddScoped<ResearchService>();
builder.Services.AddSingleton<LiveModeElevationService>();

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
        // IP-A13 one-shot: legacy strategies get their canonical JSON derived
        // from ScriptText so the Monitor can run JSON-first everywhere.
        var backfilled = await scope.ServiceProvider.GetRequiredService<StrategyRepository>()
            .BackfillCanonicalJsonAsync();
        if (backfilled > 0)
            app.Logger.LogInformation("Backfilled canonical ScriptJson for {Count} legacy strategies.", backfilled);
    }
    catch (Exception ex) { app.Logger.LogError(ex, "Canonical-JSON backfill failed at startup (continuing)."); }

    try
    {
        // Seed the disposable-email-domain blocklist (IP-A23). Idempotent.
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
app.UseStaticFiles(new StaticFileOptions
{
    // AppContext.BaseDirectory is the exe's own folder — always correct regardless
    // of the process working directory, which varies by how the bat is launched.
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(AppContext.BaseDirectory, "wwwroot"))
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
    // Reject malformed and disposable/temporary email domains (IP-A23) — a
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

// ── Alpaca OAuth / Connect (IP-A26) — account linking instead of raw keys ──
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
