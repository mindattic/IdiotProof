using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies;
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
        arg is "status" or "set-keys" or "create-strategies" or "create-account" or "test-order" or "flatten" or "replay" or "replay-live" or "replay-all" or "replay-regen" or "scan" or "replay-export" or "resync-canon" or "auto-gapper";

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
                "test-order"        => await TestOrderAsync(sp, opt),
                "flatten"           => await FlattenAsync(sp, opt),
                "replay"            => await StrategyReplay.RunAsync(sp, opt),
                "replay-live"       => await StrategyReplay.RunLiveAsync(sp, opt),
                "replay-all"        => await StrategyReplay.RunAllAsync(sp, opt),
                "replay-regen"      => await RunRegenAsync(sp, opt),
                "scan"              => await StrategyScanner.RunAsync(sp, opt),
                "replay-export"     => await RunExportAsync(sp, opt),
                "resync-canon"      => await ResyncCanonAsync(sp, opt),
                "auto-gapper"       => await AutoGapperAsync(sp, opt),
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
            Line($"   ▸ {s.Symbol}   \"{s.Title}\"   —  by {(string.IsNullOrWhiteSpace(s.Author) ? "unknown" : s.Author)}");
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

        // Same disposable-domain gate as web registration (IP-A23). Seed the
        // blocklist first — the Blazor host seeds it at startup, but the Monitor
        // may run standalone with an empty table, which would make the check a
        // silent no-op.
        var blocklist = sp.GetRequiredService<EmailDomainBlocklistService>();
        await blocklist.SeedAsync();
        if (EmailDomainBlocklistService.DomainOf(email) is null)
            return Fail($"email is malformed: {email}");
        if (await blocklist.IsBlockedAsync(email))
            return Fail($"email domain is disposable/blocked: {email}");
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

    // ── test-order ──────────────────────────────────────────────────────
    // Proves the resolved broker can place AND cancel an order autonomously
    // (no human step) through the EXACT code path the Monitor uses at fire
    // time. Places a deliberately-unfillable limit buy (far below market) and
    // cancels it — zero fill risk, paper account.
    private static async Task<int> TestOrderAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var resolver = sp.GetRequiredService<UserBrokerResolver>();
        var userId = await ResolveUserAsync(sp, opt);
        if (userId is null) return Fail("test-order requires --user <guid>.");

        var symbol = opt.GetValueOrDefault("symbol", "AAPL").ToUpperInvariant();
        var broker = await resolver.ResolveAsync(userId.Value);
        Line($"Broker: {broker.BrokerType} ({(broker.IsPaper ? "PAPER" : "LIVE")}) — placing an UNFILLABLE test limit then cancelling…");
        if (!broker.IsPaper && !opt.ContainsKey("force"))
            return Fail("resolved broker is LIVE — refusing a live test order without --force.");

        // Limit BUY 1 share @ $1: a buy limit that far below market can never fill.
        var order = await broker.PlaceOrderAsync(new IdiotProof.Models.OrderRequest
        {
            Symbol = symbol, Quantity = 1, Side = IdiotProof.Models.OrderSide.Buy,
            Type = IdiotProof.Models.OrderType.Limit, LimitPrice = 1.00m, TimeInForce = "DAY",
        });
        if (!order.IsSuccess)
            return Fail($"PLACE failed — the key CANNOT place orders: {order.Message}");
        Line($"   ✓ PLACE ok — order id {order.BrokerOrderId} (accepted by {broker.BrokerType}, no human step)");

        var cancel = await broker.CancelOrderAsync(order.BrokerOrderId);
        if (!cancel.IsSuccess)
        {
            Line($"   ⚠ CANCEL failed: {cancel.Message} — the resting $1 limit will expire at session end (unfillable). Cancel it in the Alpaca dashboard if you prefer.");
            return Fail("placed but could not cancel — see warning.");
        }
        Line($"   ✓ CANCEL ok — order {order.BrokerOrderId} cancelled.");
        Line("RESULT: the account can place AND cancel orders autonomously via the API. No 4 AM human intervention needed.");
        return 0;
    }

    // ── flatten ─────────────────────────────────────────────────────────
    // Manually close held positions (marketable sell) with the SAME bookkeeping
    // the Monitor's own exit path writes — records the exit fill, feeds realized
    // P&L into the daily circuit breaker, and closes the trade-diary row. For a
    // held position that needs to go NOW without waiting for a stop/sell-by.
    //
    // SAFETY: run this only with the Monitor STOPPED. The Monitor is the single
    // leader-lease trader; a manual flatten while it evaluates would race its
    // exit reconciliation and could oversell. Optional --symbol to flatten one.
    private static async Task<int> FlattenAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var userId = await ResolveUserAsync(sp, opt);
        if (userId is null) return Fail("flatten requires --user <guid> (or a single user).");
        var symbolFilter = opt.TryGetValue("symbol", out var sf) && !string.IsNullOrWhiteSpace(sf)
            ? sf.Trim().ToUpperInvariant() : null;

        var strategyRepo = sp.GetRequiredService<StrategyRepository>();
        var resolver     = sp.GetRequiredService<UserBrokerResolver>();
        var tradeDiary   = sp.GetRequiredService<TradeDiaryRepository>();
        var riskService  = sp.GetRequiredService<RiskGuardianService>();

        var holding = (await strategyRepo.GetActiveAsync())
            .Where(x => x.OwnerUserId == userId.Value && x.PositionQty > 0
                     && (symbolFilter is null || x.Symbol.Equals(symbolFilter, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (holding.Count == 0)
        {
            Line($"No held positions to flatten{(symbolFilter is null ? "" : $" for {symbolFilter}")}.");
            return 0;
        }

        var broker = await resolver.ResolveAsync(userId.Value);
        Line($"Flatten via {broker.BrokerType} ({(broker.IsPaper ? "PAPER" : "*** LIVE ***")}) — {holding.Count} position(s):");

        Dictionary<string, Position> positions;
        try
        {
            positions = (await broker.GetPositionsAsync())
                .ToDictionary(p => p.Symbol, p => p, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { return Fail($"could not read broker positions: {ex.Message}"); }

        var nowEt = MarketTime.ToEasternTimeOfDay(DateTime.UtcNow);
        var extended = (nowEt >= new TimeSpan(4, 0, 0) && nowEt < new TimeSpan(9, 30, 0))
                    || (nowEt >= new TimeSpan(16, 0, 0) && nowEt < new TimeSpan(20, 0, 0));

        int done = 0;
        foreach (var st in holding)
        {
            var sym = st.Symbol.ToUpperInvariant();
            positions.TryGetValue(sym, out var bp);
            var qty = st.PositionQty;
            if (bp is not null) qty = Math.Min(qty, (int)Math.Floor(bp.Quantity));

            if (qty <= 0)
            {
                Line($"  = {sym} \"{st.Title}\" — broker shows no shares; clearing bookkeeping only.");
                await strategyRepo.RecordExitFillAsync(st.Id, st.LastEntryPrice ?? 0m, "ManualFlatten-NoShares", DateTime.UtcNow);
                try { await tradeDiary.MarkNotFilledAsync(st.Id, DateTime.UtcNow); } catch { /* diary best-effort */ }
                continue;
            }

            var refPx = bp is not null && bp.Quantity > 0 ? bp.MarketValue / bp.Quantity : (st.LastEntryPrice ?? 0m);
            var limit = Math.Round(refPx * 0.995m, 2); // marketable sell limit (−0.5%)
            var order = await broker.PlaceOrderAsync(new OrderRequest
            {
                Symbol = sym, Quantity = qty, Side = OrderSide.Sell, Type = OrderType.Limit,
                LimitPrice = limit, TimeInForce = "DAY", ExtendedHours = extended,
                // Same naked-short protection as the Monitor's own exit path
                // (MonitorWorker.cs) — without this, a stale/wrong PositionQty
                // could sell into opening a short instead of just closing out.
                PositionIntent = "sell_to_close",
            });
            if (!order.IsSuccess)
            {
                Line($"  ✗ {sym} \"{st.Title}\" — SELL rejected by {broker.BrokerType}: {order.Message}");
                continue;
            }

            var entry = st.LastEntryPrice ?? limit;
            var realized = (limit - entry) * qty;
            var exitUtc = DateTime.UtcNow;
            await strategyRepo.RecordExitFillAsync(st.Id, limit, "ManualFlatten", exitUtc);
            try { var g = await riskService.GetForUserAsync(userId.Value); g.RecordTradePnL(realized); } catch { /* breaker best-effort */ }
            try { await tradeDiary.CloseAsync(st.Id, limit, "ManualFlatten", order.BrokerOrderId, realized, qty, exitUtc); }
            catch (Exception ex) { Line($"      (diary close failed: {ex.Message})"); }

            var pnlText = realized >= 0 ? $"+${realized:0.00}" : $"-${Math.Abs(realized):0.00}";
            Line($"  ✓ {sym} \"{st.Title}\" — SOLD {qty} @ ${limit:0.00}  P&L {pnlText}  (order {order.BrokerOrderId})");
            done++;
        }
        Line($"Flattened {done} of {holding.Count} position(s). Positions cleared; strategies remain active (deactivate/delete separately).");
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

        // Validate BEFORE storing — a typo'd/dead key would otherwise be saved
        // and silently break routing at trade time. --force skips the checks.
        if (!opt.ContainsKey("force"))
        {
            // Alpaca convention: paper key ids start "PK", live "AK".
            if (isPaper && !apiKey.StartsWith("PK", StringComparison.Ordinal))
                return Fail("key doesn't look like a PAPER key (expected 'PK…'). Use --live for a live key, or --force to override.");
            if (!isPaper && !apiKey.StartsWith("AK", StringComparison.Ordinal))
                return Fail("key doesn't look like a LIVE key (expected 'AK…'). Drop --live for a paper key, or --force to override.");

            // Live authenticated check against the matching endpoint.
            await using var probe = new IdiotProof.Brokers.AlpacaBrokerClient(apiKey, secret, isPaper);
            var acct = await probe.GetAccountAsync();
            if (acct.TryGetValue("error", out var err))
                return Fail($"key failed live auth against Alpaca {(isPaper ? "paper" : "LIVE")}: {err}. Use --force to store anyway.");
            Line($"   live auth ✓ — account {acct.GetValueOrDefault("account_number", "?")} status {acct.GetValueOrDefault("status", "?")} cash ${acct.GetValueOrDefault("cash", "?")}");
        }

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
        var seenInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, skipped = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Symbol) || string.IsNullOrWhiteSpace(item.Script))
            {
                Line($"   ✗ skipping malformed item (symbol/script missing).");
                continue;
            }
            // In-batch dedup: the same title+symbol twice in one file would
            // otherwise create two identical strategies.
            if (!seenInBatch.Add($"{item.Symbol}|{item.Title}"))
            {
                Line($"   = {item.Symbol} \"{item.Title}\" duplicated in file — skipped.");
                skipped++;
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
                description: item.Description, author: item.Author,
                originTranscript: item.OriginTranscript);

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
        public string? Author { get; set; }
        public string? OriginTranscript { get; set; }
        public bool Active { get; set; }
    }

    // ── resync-canon ──────────────────────────────────────────────────────
    // Re-derives every strategy's canonical JSON from its ScriptText, repairing
    // rows whose stored canon lost conditions to a since-fixed parser gap (e.g.
    // IsHigherLow being silently dropped). Dry-run by default; pass --apply to
    // write. Never regresses a row (see StrategyRepository.ResyncCanonFromTextAsync).
    private static async Task<int> ResyncCanonAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var repo  = sp.GetRequiredService<StrategyRepository>();
        var apply = opt.ContainsKey("apply");
        var r = await repo.ResyncCanonFromTextAsync(apply);

        Line($"Canon resync ({(apply ? "APPLY" : "dry-run")}) — scanned {r.Scanned}, " +
             $"{(apply ? "fixed" : "would fix")} {r.Changed}, skipped-to-avoid-regression {r.SkippedRegression}");
        foreach (var n in r.Notes) Line("   " + n);
        if (!apply && r.Changed > 0) Line("→ Re-run with --apply to write the changes.");
        return 0;
    }

    // ── auto-gapper ─────────────────────────────────────────────────────
    // Runs the 3:55 AM gapper discovery+synthesis on demand, bypassing the time
    // gate. DRY-RUN by default (preview only, arms nothing, writes nothing);
    // pass --arm to actually create + activate strategies and persist metrics.
    private static async Task<int> AutoGapperAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var scanner = sp.GetRequiredService<AutoGapperScanner>();
        var userId = await ResolveUserAsync(sp, opt);
        if (userId is null) return Fail("auto-gapper requires --user <guid> (or a single user).");

        var dryRun = !opt.ContainsKey("arm");
        Line($"Auto-gapper {(dryRun ? "DRY-RUN (preview — nothing armed)" : "ARM (creating + activating strategies)")} for user {userId}…");
        var r = await scanner.RunScanAsync(userId.Value, dryRun, phase: "manual", CancellationToken.None);
        Line($"→ screened {r.Screened}, qualified {r.Qualified}, {(dryRun ? "would-arm" : "armed")} {r.Armed}, skipped {r.Skipped}  ({r.Note})");
        if (dryRun && r.Armed > 0) Line("   Re-run with --arm to actually arm these (respects the paper-only guard).");
        return 0;
    }

    private static async Task<int> RunRegenAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        await StrategyReplay.RegenerateAsync(sp, opt);
        return 0;
    }

    private static async Task<int> RunExportAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        await StrategyDataset.ExportAsync(sp, opt);
        return 0;
    }

    // ── helpers ─────────────────────────────────────────────────────────
    private static async Task<Guid?> ResolveUserAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        if (opt.TryGetValue("user", out var u) && Guid.TryParse(u, out var g)) return g;
        // Fall back to the single user if there is exactly one — otherwise the
        // caller must pass --user. `await using` so the CLI doesn't leak a
        // DbContext (the old code never disposed it).
        await using var db = await sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var ids = db.AuthUsers.Select(x => x.Id).Take(2).ToList();
        if (ids.Count == 1) return ids[0];
        Console.Error.WriteLine(ids.Count == 0
            ? "No users exist — create one first (create-account)."
            : "Multiple users exist — pass --user <guid> to pick one.");
        return null;
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
