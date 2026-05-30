using IdiotProof.Engine;
using IdiotProof.Engine.Storage;
using IdiotProof.Blazor.Components;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Hubs;
using IdiotProof.Blazor.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindAttic.Legion;
using MindAttic.Vault.Configuration;
using MindAttic.Vault.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// .env autoload (Development only). Lets the user park DEV_USERNAME / DEV_PASSWORD
// in a gitignored .env at the repo root so the Login page can prefill credentials
// during local debug runs. Never loaded in any non-Development build.
if (builder.Environment.IsDevelopment())
{
    var envPath = Path.Combine(builder.Environment.ContentRootPath, "..", ".env");
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

// ── Database (SQL Server + Identity) ─────────────────────────────────────────────
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

builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath        = "/login";
    o.LogoutPath       = "/logout";
    o.ExpireTimeSpan   = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
    o.Cookie.HttpOnly  = true;
    o.Cookie.SameSite  = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddDataProtection();

// ── Engine ───────────────────────────────────────────────────────────────────────
// AddIdiotProofEngine registers WorkspaceManager + a default IWorkspaceStore.
// We no longer surface workspaces in the UI, but the engine still wires the
// types — they sit unused until/unless a workspace concept comes back.
builder.Services.AddIdiotProofEngine(storageProvider, builder.Configuration);

// ── SignalR ───────────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 32 * 1024;
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// ── Web services ─────────────────────────────────────────────────────────────────
// Strategy evaluation is owned by IdiotProof.Monitor (the second startup
// project). The Blazor host writes user edits to SQL; the Monitor reads them on
// its 30s cadence and runs them autonomously, surviving Blazor restarts. Do not
// re-register StrategyExecutionService as a HostedService here — running both
// would double-fire signals and double-write audit rows against the same DB.
// The class itself stays on disk in case we ever need its in-process variant.
builder.Services.AddSingleton<TradingStateService>();
// MindAttic.Legion is the gateway for all LLM communication — register the
// universal client before any service that talks to an LLM.
builder.Services.AddLegionClient();
builder.Services.AddSingleton<IdiotProof.Blazor.Services.LlmVotingService>();
builder.Services.AddScoped<UserKeyService>();
builder.Services.AddSingleton<StrategyRepository>();
builder.Services.AddSingleton<UserPreferencesService>();
builder.Services.AddSingleton<StrategyScriptGenerator>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<AuditLogRepository>();
builder.Services.AddSingleton<ConditionProgressRepository>();
builder.Services.AddSingleton<RiskGuardianService>();
builder.Services.AddHttpClient();
builder.Services.AddAntiforgery();

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
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Auth endpoints ────────────────────────────────────────────────────────────────
app.MapPost("/login-submit", async (HttpContext ctx,
    SignInManager<AppUser> signInMgr,
    UserManager<AppUser> userMgr) =>
{
    var form      = await ctx.Request.ReadFormAsync();
    var email     = form["email"].ToString().Trim();
    var password  = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    var result = await signInMgr.PasswordSignInAsync(email, password,
        isPersistent: true, lockoutOnFailure: false);

    if (result.Succeeded)
    {
        ctx.Response.Redirect(!string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : "/");
        return;
    }

    ctx.Response.Redirect("/login?error=invalid");
});

app.MapPost("/logout", async (HttpContext ctx, SignInManager<AppUser> signInMgr) =>
{
    await signInMgr.SignOutAsync();
    ctx.Response.Redirect("/login");
});

app.MapPost("/register-submit", async (HttpContext ctx,
    SignInManager<AppUser> signInMgr,
    UserManager<AppUser> userMgr) =>
{
    // SignInAsync writes a Set-Cookie header, which only works before the response
    // starts — i.e. from a real HTTP request, not from inside a Blazor interactive
    // circuit (where the response is already flushed and SignalR is running).
    // The /register page posts to this endpoint so the cookie can be written cleanly.
    var form     = await ctx.Request.ReadFormAsync();
    var email    = form["email"].ToString().Trim();
    var password = form["password"].ToString();
    var confirm  = form["confirm"].ToString();

    if (string.IsNullOrWhiteSpace(email))
    { ctx.Response.Redirect("/register?error=email"); return; }
    if (password != confirm)
    { ctx.Response.Redirect("/register?error=mismatch"); return; }
    if (password.Length < 8)
    { ctx.Response.Redirect("/register?error=short"); return; }
    if (!password.Any(char.IsDigit))
    { ctx.Response.Redirect("/register?error=digit"); return; }

    var user   = new AppUser { UserName = email, Email = email };
    var result = await userMgr.CreateAsync(user, password);

    if (!result.Succeeded)
    {
        var code = result.Errors.FirstOrDefault()?.Code ?? "create";
        ctx.Response.Redirect($"/register?error={Uri.EscapeDataString(code)}");
        return;
    }

    await signInMgr.SignInAsync(user, isPersistent: true);
    ctx.Response.Redirect("/api-keys");
});

app.MapPost("/forgot-password-submit", async (HttpContext ctx,
    UserManager<AppUser> userMgr) =>
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
    if (!password.Any(char.IsDigit))
    { ctx.Response.Redirect("/forgot-password?error=digit"); return; }

    var user = await userMgr.FindByEmailAsync(email);
    if (user is null)
    { ctx.Response.Redirect("/forgot-password?error=unknown"); return; }

    // Local dev: no email service, so we trust local possession + email match.
    // Generate a one-shot token and immediately consume it.
    var token  = await userMgr.GeneratePasswordResetTokenAsync(user);
    var result = await userMgr.ResetPasswordAsync(user, token, password);

    if (!result.Succeeded)
    {
        var code = result.Errors.FirstOrDefault()?.Code ?? "reset";
        ctx.Response.Redirect($"/forgot-password?error={Uri.EscapeDataString(code)}");
        return;
    }

    ctx.Response.Redirect("/forgot-password?status=ok");
});

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapHub<TradingHub>("/hubs/trading");

app.Run();
