using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using IdiotProof.Scripting;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Monitor;

/// <summary>
/// Operator CLI for the Monitor — runs against the SAME DI/config the worker
/// uses, so what it reports is exactly what the Monitor will do (no hand-rolled
/// approximation on the money path). Subcommands:
///
///   status            [--user &lt;guid&gt;]         verify keys / paper / fire / exit
///   set-keys          --user &lt;guid&gt; --key K --secret S [--live]
///   create-strategies --user &lt;guid&gt; --file watchlist.json
///
/// A subcommand builds the host, resolves services, does its work, and exits —
/// it never calls host.RunAsync(), so the trading worker never starts.
/// </summary>
public static class MonitorCli
{
    public static bool IsCommand(string arg) =>
        arg is "status" or "set-keys" or "create-strategies" or "create-account";

    public static async Task<int> RunAsync(IServiceProvider sp, string[] args)
    {
        var cmd = args[0];
        var opt = ParseOptions(args);
        try
        {
            return cmd switch
            {
                "status"            => await StatusAsync(sp, opt),
                "set-keys"          => await SetKeysAsync(sp, opt),
                "create-strategies" => await CreateStrategiesAsync(sp, opt),
                "create-account"    => await CreateAccountAsync(sp, opt),
                _                   => Fail($"Unknown command '{cmd}'."),
            };
        }
        catch (Exception ex)
        {
            return Fail($"{cmd} failed: {ex.Message}");
        }
    }

    // ── status ──────────────────────────────────────────────────────────
    private static async Task<int> StatusAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var keys      = sp.GetRequiredService<UserKeyService>();
        var resolver  = sp.GetRequiredService<UserBrokerResolver>();
        var strategies= sp.GetRequiredService<StrategyRepository>();
        var settings  = sp.GetRequiredService<AppSettings>();
        var feed      = sp.GetRequiredService<IdiotProof.DataFeeds.IMarketDataFeed>();

        var userId = await ResolveUserAsync(sp, opt);
        if (userId is null) return Fail("No --user given and no single user found.");

        Line("══════════════════════════════════════════════════════════════════");
        Line($" IdiotProof — Monitor status   (user {userId})");
        Line("══════════════════════════════════════════════════════════════════");

        // (1) Right Alpaca key?  (2) Paper?
        var k = await keys.GetOrCreateAsync(userId.Value);
        var choice = UserBrokerResolver.Choose(k);
        var broker = await resolver.ResolveAsync(userId.Value);
        Line("");
        Line("① / ②  BROKER KEY & MODE");
        Line($"   default broker pref : {k.DefaultBroker}");
        Line($"   user Alpaca key     : {Mask(k.AlpacaApiKeyId)}   (paper flag: {k.AlpacaIsPaper})");
        Line($"   routing decision    : {choice}");
        Line($"   RESOLVED BROKER     : {broker.BrokerType}   →  {(broker.IsPaper ? "PAPER (simulated / paper-api)" : "*** LIVE — REAL MONEY ***")}");
        // Live ping — a real authenticated call so we KNOW the key talks to the API.
        try
        {
            var positions = await broker.GetPositionsAsync();
            Line($"   LIVE API PING       : ✓ authenticated — {broker.BrokerType} reachable, {positions.Count} open position(s)");
        }
        catch (Exception ex)
        {
            Line($"   LIVE API PING       : ✗ FAILED — {ex.Message}");
        }
        Line($"   global data feed    : {feed.FeedName}");
        if (string.Equals(feed.FeedName, "Mock", StringComparison.OrdinalIgnoreCase) && broker.BrokerType != IdiotProof.Models.BrokerType.Sandbox)
            Line("   ⚠  feed is Mock but broker is non-Sandbox — the Monitor will BLOCK real entries on synthetic data (IP-A22).");
        if (!broker.IsPaper)
            Line("   ⚠  RESOLVED BROKER IS LIVE. Real orders will use real money.");

        // (3) Will these fire?  (4) Will they sell?
        var active = (await strategies.GetActiveAsync())
            .Where(s => s.OwnerUserId == userId.Value)
            .OrderBy(s => s.Symbol).ToList();

        Line("");
        Line($"③ / ④  ACTIVE STRATEGIES ({active.Count})  — entry criteria & exit plan");
        if (active.Count == 0)
            Line("   (none active for this user)");

        foreach (var s in active)
        {
            var loaded = StrategyLoader.Load(s.ScriptJson, s.ScriptText);
            Line("");
            Line($"   ▸ {s.Symbol}   \"{s.Title}\"");
            if (loaded.CanonicalError is { } err)
            {
                Line($"       ✗ QUARANTINED — canonical JSON rejected: {err}");
                continue;
            }
            var def = loaded.Definition;
            if (def is null) { Line("       ✗ could not parse script."); continue; }

            var canon = loaded.FromCanonicalJson ? "canonical JSON" : "legacy text-parse";
            Line($"       session   : {def.Session}   (Extended = premarket + RTH + after-hours, all day)");
            Line($"       source    : {canon}");
            Line($"       ENTRY (all must be true, AND):");
            foreach (var c in def.EntryConditions)
                Line($"           • {c.ToScript()}");
            if (def.EntryConditions.Count == 0) Line("           • (no entry conditions — fires immediately in-session)");

            Line($"       WILL IT FIRE? {FireVerdict(s, def)}");

            // Exit plan
            var exits = new List<string>();
            if (def.StopLossPrice is { } sp2)      exits.Add($"hard stop @ {sp2:F2}");
            if (def.StopLossPercent is { } sc)     exits.Add($"hard stop {sc:0.#}%");
            if (def.TrailingStopPercent is { } ts) exits.Add($"trailing stop {ts:0.#}% off peak");
            if (def.TakeProfitPrice is { } tp)     exits.Add($"take-profit @ {tp:F2}");
            if (def.PeakGivebackPercent is { } pg) exits.Add($"peak-giveback {pg:0.#}%");
            if (def.ExitTime is { } et)            exits.Add($"sell-by {et:hh\\:mm} ET");
            Line($"       EXIT (whichever hits first): {(exits.Count > 0 ? string.Join("  |  ", exits) : "⚠ NONE — no stop/target set!")}");
            Line($"       WILL IT SELL? {SellVerdict(def)}");
        }

        Line("");
        Line("══════════════════════════════════════════════════════════════════");
        return 0;
    }

    private static string FireVerdict(Strategy s, StrategyDefinition def)
    {
        if (!s.IsActive) return "NO — strategy is paused.";
        if (def.EntryConditions.Count == 0 && def.ConditionalBlocks.Count == 0)
            return "fires as soon as its session is open (no gating conditions).";
        return "YES — the Monitor evaluates it every tick while its session is open; " +
               "it fires the instant ALL entry conditions are simultaneously true, then the three gates clear (conditions → LLM → RiskGuardian).";
    }

    private static string SellVerdict(StrategyDefinition def)
    {
        var has = def.StopLossPercent is not null || def.StopLossPrice is not null
               || def.TrailingStopPercent is not null || def.TakeProfitPrice is not null
               || def.PeakGivebackPercent is not null || def.ExitTime is not null;
        if (!has) return "⚠ NO exit configured — position would only close manually. Add a stop.";
        var parts = new List<string>();
        if (def.StopLossPercent is not null || def.StopLossPrice is not null) parts.Add("the hard stop caps the downside");
        if (def.TrailingStopPercent is not null) parts.Add("the trailing stop rides the winner and sells once it gives back the trail % from its peak (the 'juice ran out')");
        if (def.TakeProfitPrice is not null) parts.Add("the take-profit exits at target");
        return "YES — " + string.Join("; ", parts) + ". Managed every tick by GapperExitEvaluator.";
    }

    // ── create-account ──────────────────────────────────────────────────
    private static async Task<int> CreateAccountAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        if (!opt.TryGetValue("email", out var email) || !opt.TryGetValue("password", out var password))
            return Fail("create-account requires --email and --password.");

        // Same disposable-domain gate as web registration (IP-A23).
        var blocklist = sp.GetRequiredService<EmailDomainBlocklistService>();
        if (await blocklist.IsBlockedAsync(email))
            return Fail($"email domain rejected (malformed or disposable): {email}");
        if (password.Length < 8 || password.Length > 128 || !password.Any(char.IsDigit))
            return Fail("password must be 8–128 chars and contain at least one digit.");

        var admin = sp.GetRequiredService<MindAttic.Authentication.Services.IUserAdminService>();
        var result = await admin.CreateAsync(
            userName: email, email: email, role: "User",
            password: password, mustChangePassword: false);

        if (!result.Ok)
            return Fail($"account creation failed: {result.Error}");

        Line($"✓ Account created via the real auth path (Argon2id): {email}");
        return 0;
    }

    // ── set-keys ────────────────────────────────────────────────────────
    private static async Task<int> SetKeysAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var keys = sp.GetRequiredService<UserKeyService>();
        var userId = await ResolveUserAsync(sp, opt);
        if (userId is null) return Fail("set-keys requires --user <guid>.");
        if (!opt.TryGetValue("key", out var apiKey) || !opt.TryGetValue("secret", out var secret))
            return Fail("set-keys requires --key and --secret.");

        var isPaper = !opt.ContainsKey("live");
        var existing = await keys.GetOrCreateAsync(userId.Value);
        existing.UserId             = userId.Value;
        existing.AlpacaApiKeyId     = apiKey;
        existing.AlpacaApiSecretKey = secret;
        existing.AlpacaIsPaper      = isPaper;
        existing.DefaultBroker      = "alpaca"; // route to the user's own Alpaca
        existing.DefaultDataFeed    = string.IsNullOrWhiteSpace(existing.DefaultDataFeed) ? "Alpaca" : existing.DefaultDataFeed;
        await keys.SaveAsync(userId.Value, existing);

        Line($"✓ Saved per-user Alpaca keys for {userId}: {Mask(apiKey)}  mode={(isPaper ? "PAPER" : "LIVE")}  routing=alpaca");
        if (!isPaper) Line("⚠  LIVE keys set — real money.");
        return 0;
    }

    // ── create-strategies ───────────────────────────────────────────────
    private static async Task<int> CreateStrategiesAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var repo = sp.GetRequiredService<StrategyRepository>();
        var userId = await ResolveUserAsync(sp, opt);
        if (userId is null) return Fail("create-strategies requires --user <guid>.");
        if (!opt.TryGetValue("file", out var file) || !File.Exists(file))
            return Fail($"create-strategies requires --file <watchlist.json> (not found: {opt.GetValueOrDefault("file")}).");

        var items = JsonSerializer.Deserialize<List<WatchlistItem>>(File.ReadAllText(file),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var existing = await repo.GetAllForUserAsync(userId.Value);
        int created = 0, skipped = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Symbol) || string.IsNullOrWhiteSpace(item.Script))
            {
                Line($"   ✗ skipping malformed item (symbol/script missing).");
                continue;
            }
            // Idempotent: don't duplicate an existing same-title+symbol row.
            if (existing.Any(e => e.Symbol.Equals(item.Symbol, StringComparison.OrdinalIgnoreCase)
                               && e.Title == item.Title))
            {
                Line($"   = {item.Symbol} \"{item.Title}\" already exists — skipped.");
                skipped++;
                continue;
            }

            // CreateAsync derives canonical JSON from the script (parse →
            // StrategyJson.Serialize), so the row is canon-first (IP-LAW-8).
            var s = await repo.CreateAsync(userId.Value, item.Title, item.Symbol, item.Script,
                description: item.Description);

            // Verify the canon materializes before activating — fail loud, never
            // arm a strategy the Monitor would quarantine.
            var loaded = StrategyLoader.Load(s.ScriptJson, s.ScriptText);
            if (loaded.Definition is null || loaded.CanonicalError is not null)
            {
                Line($"   ✗ {item.Symbol}: script did not produce a valid canon ({loaded.CanonicalError ?? "parse failed"}) — left PAUSED.");
                continue;
            }

            if (item.Active)
            {
                var m = await repo.SetActiveAsync(s.Id, true, userId.Value);
                Line($"   ✓ {item.Symbol} \"{item.Title}\" created + {(m == StrategyMutation.Ok ? "ACTIVE" : $"activate={m}")}  ({loaded.Definition.EntryConditions.Count} entry conditions)");
            }
            else
            {
                Line($"   ✓ {item.Symbol} \"{item.Title}\" created (paused).");
            }
            created++;
        }
        Line($"Done — {created} created, {skipped} skipped.");
        return 0;
    }

    private sealed class WatchlistItem
    {
        public string Title { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Script { get; set; } = "";
        public string? Description { get; set; }
        public bool Active { get; set; }
    }

    // ── helpers ─────────────────────────────────────────────────────────
    private static async Task<Guid?> ResolveUserAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        if (opt.TryGetValue("user", out var u) && Guid.TryParse(u, out var g)) return g;
        // Fall back to the single user if there is exactly one.
        var db = await sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var ids = db.AuthUsers.Select(x => x.Id).Take(2).ToList();
        return ids.Count == 1 ? ids[0] : null;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            var key = args[i][2..];
            var val = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
            d[key] = val;
        }
        return d;
    }

    private static string Mask(string? s) =>
        string.IsNullOrEmpty(s) ? "(none)" : s.Length <= 6 ? s[..1] + "…" : s[..4] + "…" + s[^2..];

    private static void Line(string s) => Console.WriteLine(s);
    private static int Fail(string s) { Console.Error.WriteLine("ERROR: " + s); return 1; }
}
