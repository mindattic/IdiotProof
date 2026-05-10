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

// Cloud-native configuration chain. Layered (later sources win):
//   AddJsonFile (already added by WebApplicationBuilder for appsettings.json).
//   AddMindAtticVaultFiles surfaces %APPDATA%\MindAttic\<bucket>\providers.json on dev.
//   AddUserSecrets pinned to mindattic-vault-shared so the same secret store is
//     visible to every MindAttic project that pins the same id.
//   AddEnvironmentVariables (already present) picks up App Service Application
//     Settings and Azure Key Vault references in production.
builder.Configuration
    .AddMindAtticVaultFiles()
    .AddUserSecrets<Program>(optional: true);

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
// Pre-register SqlWorkspaceStore so AddIdiotProofEngine's TryAddSingleton for
// IWorkspaceStore sees ours and skips the JSON-on-disk default. The store
// also handles the one-shot import of any legacy on-disk workspaces the
// first time it sees an empty SQL Workspaces table — users with pre-existing
// on-disk data migrate transparently on first boot.
builder.Services.AddSingleton<IdiotProof.Engine.Workspace.IWorkspaceStore, IdiotProof.Blazor.Services.SqlWorkspaceStore>();
builder.Services.AddIdiotProofEngine(storageProvider, builder.Configuration);

// ── SignalR ───────────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 32 * 1024;
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// ── Web services ─────────────────────────────────────────────────────────────────
builder.Services.AddHostedService<StrategyExecutionService>();
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
builder.Services.AddSingleton<WorkspaceRepository>();
builder.Services.AddSingleton<AuditLogRepository>();
builder.Services.AddSingleton<ConditionProgressRepository>();
builder.Services.AddSingleton<RiskGuardianService>();
builder.Services.AddHttpClient();
builder.Services.AddAntiforgery();

// ── App ──────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Apply pending EF migrations on startup (creates the IdiotProof database on
// LocalDB if missing, then keeps schema in sync with the codebase). Seed the
// Learning Center catalog on the same scope so a fresh install has docs ready.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await IdiotProof.Blazor.Services.LearningContentSeeder.SeedAsync(dbFactory);
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

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapHub<TradingHub>("/hubs/trading");

app.Run();
