using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Monitor;

/// <summary>
/// Offline replay harness (CLI: <c>replay</c>). Pulls a past day's 1-minute bars
/// from Alpaca (SIP historical is free ≥15 min old — see IP market-data notes),
/// walks them through the SAME evaluation code the live Monitor runs
/// (IndicatorSnapshotBuilder + the strategy's real ICondition.Evaluate + the
/// shared MarketTime.IsInsideSession gate), and publishes a self-contained page
/// showing the candle chart, the exact fire tick(s), the strategy rendered as
/// phase cards, and its flow diagram.
///
/// Output layout (all timestamps US Eastern — the market clock):
///   &lt;out&gt;/&lt;ticker&gt;/&lt;yyyy-MM-ddTHH.mm.ss&gt;/index.htm   one replay run
///   &lt;out&gt;/&lt;ticker&gt;/index.htm                          index of all runs
///
/// A ticker with no saved strategy can still be replayed by applying a gapper
/// profile on the fly (<c>--profile &lt;id&gt;</c>) via GapperScriptFactory.
/// </summary>
public static class StrategyReplay
{
    private const int CandleWindow = 240; // trailing 4h — matches MonitorWorker

    public static async Task<int> RunAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        if (!opt.TryGetValue("symbol", out var symRaw) || string.IsNullOrWhiteSpace(symRaw))
            return Fail("replay requires --symbol <TICKER>.");
        var symbol = symRaw.Trim().ToUpperInvariant();

        // Date: ET calendar day to replay. Default = most recent completed ET
        // trading weekday.
        var dateEt = opt.TryGetValue("date", out var dRaw) && DateOnly.TryParse(dRaw, out var d)
            ? d : MarketTime.PreviousEquityTradingDayEt(DateTime.UtcNow);

        var feedTier = (opt.GetValueOrDefault("feed", "sip")).ToLowerInvariant();
        var outRoot = opt.GetValueOrDefault("out",
            @"D:\Projects\MindAttic\mindattic.com\idiotproof\replays");

        var userId = await ResolveUserAsync(sp, opt);
        if (userId is null) return Fail("No --user given and no single user found.");

        var keys = sp.GetRequiredService<UserKeyService>();
        var k = await keys.GetOrCreateAsync(userId.Value);
        if (string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) || string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey))
            return Fail($"user {userId} has no Alpaca data keys — set them first (set-keys).");

        // Resolve the strategy: saved row for this symbol, or a gapper profile
        // applied on the fly (so scanner tickers with no saved strategy work).
        StrategyDefinition def;
        string strategyTitle;
        if (opt.TryGetValue("profile", out var profileId) && !string.IsNullOrWhiteSpace(profileId))
        {
            if (profileId.Equals("momentum", StringComparison.OrdinalIgnoreCase))
            {
                def = BuildMomentum(symbol);
                strategyTitle = def.Name!;
            }
            else
            {
                var profile = LoadProfile(profileId, opt.GetValueOrDefault("profiles-path"));
                if (profile is null) return Fail($"gapper profile '{profileId}' not found.");
                def = GapperScriptFactory.Compose(symbol, profile).Build();
                strategyTitle = def.Name ?? $"{symbol} Gapper";
            }
        }
        else
        {
            var repo = sp.GetRequiredService<StrategyRepository>();
            var all = await repo.GetAllForUserAsync(userId.Value);
            var matches = all.Where(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)).ToList();
            var chosen = matches.FirstOrDefault(s => s.IsActive) ?? matches.FirstOrDefault();
            if (chosen is null)
                return Fail($"no saved strategy for {symbol}. Pass --profile <id> to replay a gapper profile on the fly.");
            var loaded = StrategyLoader.Load(chosen.ScriptJson, chosen.ScriptText);
            if (loaded.Definition is null)
                return Fail($"{symbol} strategy failed to load: {loaded.CanonicalError ?? "unparseable"}.");
            def = loaded.Definition;
            strategyTitle = chosen.Title;
        }

        // ── Fetch the day's bars (04:00–20:00 ET → UTC) on the chosen tier ──
        var dayStartUtc = EtToUtc(dateEt, new TimeSpan(4, 0, 0));
        var dayEndUtc = EtToUtc(dateEt, new TimeSpan(20, 0, 0));
        // Free/basic keys serve SIP historical only for data older than ~15 min;
        // a window whose END is inside that wall makes the WHOLE request 403 and
        // silently downgrade to IEX. Clamp to now-16min so delayed SIP always
        // succeeds — this is also what makes an intraday replay of *today* work.
        var sipWall = DateTime.UtcNow - TimeSpan.FromMinutes(16);
        if (feedTier == "sip" && dayEndUtc > sipWall) dayEndUtc = sipWall;
        if (dayEndUtc <= dayStartUtc)
            return Fail($"nothing to replay yet for {dateEt:yyyy-MM-dd} — the session start is still within the ~15-min SIP delay. Try again later or replay a past day.");
        await using var feed = new AlpacaDataFeed(k.AlpacaApiKeyId, k.AlpacaApiSecretKey, feedTier);

        var candles = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, dayStartUtc, dayEndUtc, TimeSpan.FromMinutes(1)))
            candles.Add(c);

        Console.WriteLine($"replay {symbol} {dateEt:yyyy-MM-dd} · feed={feed.FeedTier} · {candles.Count} bars · \"{strategyTitle}\"");
        if (candles.Count == 0)
            return Fail($"no {feed.FeedTier} bars for {symbol} on {dateEt:yyyy-MM-dd}. " +
                        "Thin/absent premarket on this feed — try --feed sip, a liquid RTH day, or a different date.");

        var previousClose = await ((IMarketDataFeed)feed).GetPreviousCloseAsync(symbol, dayStartUtc);
        var emas = EmaPeriodCollector.Collect(def);
        var condLabels = def.EntryConditions.Select(c => c.ToScript()).ToList();

        // Whether the strategy re-arms after an exit (multiple round-trips per
        // day). The saved gappers are one-shot; --repeat forces the simulation
        // to show every leg the day offered, regardless of the saved flag.
        var repeat = def.ShouldRepeat || opt.ContainsKey("repeat");

        int n = candles.Count;
        var et = new string[n]; var cnd = new bool[n][];
        var inSess = new bool[n]; var allTrue = new bool[n];
        var snapCache = new (double o, double h, double l, double c, long v, double vwap, double whigh, double volx)[n];
        int bestPassed = -1; string? bestFail = null;

        // ── Pass 1: per-bar snapshot + condition truth (the real evaluator) ──
        for (int i = 0; i < n; i++)
        {
            var lo = Math.Max(0, i - (CandleWindow - 1));
            var window = candles.GetRange(lo, i - lo + 1);
            var snap = IndicatorSnapshotBuilder.BuildWithEmas(symbol, window, emas, previousClose);
            inSess[i] = MarketTime.IsInsideSession(def.Session, candles[i].EndUtc);

            var row = new bool[condLabels.Count];
            int passed = 0; string? firstFail = null;
            for (int j = 0; j < def.EntryConditions.Count; j++)
            {
                row[j] = def.EntryConditions[j].Evaluate(snap);
                if (row[j]) passed++; else firstFail ??= condLabels[j];
            }
            cnd[i] = row;
            allTrue[i] = passed == condLabels.Count;
            if (inSess[i] && passed > bestPassed) { bestPassed = passed; bestFail = firstFail; }

            var tod = MarketTime.ToEasternTimeOfDay(candles[i].StartUtc);
            et[i] = $"{(int)tod.TotalHours:00}:{tod.Minutes:00}";
            snapCache[i] = ((double)candles[i].Open, (double)candles[i].High, (double)candles[i].Low,
                (double)candles[i].Close, (long)candles[i].Volume,
                Round(snap.Vwap ?? snap.Price), Round(snap.WindowHigh ?? snap.Price), Math.Round(snap.VolumeRatio, 2));
        }

        // ── Pass 2: round-trip simulation — enter on a fire, then hand each
        //    subsequent bar to the REAL GapperExitEvaluator until it says sell;
        //    re-arm after the exit if the strategy repeats. Each round-trip is
        //    one payoff (entry → exit, with reason and P&L). ──
        var entryIdx = new HashSet<int>(); var exitIdx = new HashSet<int>();
        var payoffs = new List<object>(); double totalPnl = 0;
        for (int i = 0; i < n; )
        {
            if (!(inSess[i] && allTrue[i])) { i++; continue; }
            var entryPx = snapCache[i].c; var entryUtc = candles[i].EndUtc;
            entryIdx.Add(i);
            int exitAt = -1; GapperExitDecision? dec = null;
            for (int j = i + 1; j < n; j++)
            {
                dec = GapperExitEvaluator.Evaluate(def, entryPx, entryUtc, candles.GetRange(0, j + 1), candles[j].EndUtc);
                if (dec is not null) { exitAt = j; break; }
            }
            if (exitAt < 0) exitAt = n - 1;            // never exited → flat at EOD
            exitIdx.Add(exitAt);
            var exitPx = dec?.CurrentPrice ?? snapCache[exitAt].c;
            var pnl = entryPx > 0 ? (exitPx - entryPx) / entryPx * 100.0 : 0;
            totalPnl += pnl;
            payoffs.Add(new
            {
                entryEt = et[i], entryPx = Round(entryPx),
                exitEt = et[exitAt], exitPx = Round(exitPx),
                reason = dec?.Reason.ToString() ?? "EndOfData",
                pnlPct = Math.Round(pnl, 2),
            });
            if (!repeat) break;                        // one-shot: first leg only
            i = exitAt + 1;                            // re-arm after the exit
        }

        // Build the rows now that entry/exit indices are known.
        var rows = new List<BarRow>(n);
        for (int i = 0; i < n; i++)
        {
            var s = snapCache[i];
            rows.Add(new BarRow(et[i], s.o, s.h, s.l, s.c, s.v, s.vwap, s.whigh, s.volx,
                cnd[i], inSess[i], entryIdx.Contains(i), exitIdx.Contains(i)));
        }

        var fires = payoffs.Select(p => ((dynamic)p).entryEt).Cast<string>().ToList();
        string? firstFireEt = fires.FirstOrDefault();
        double? entryPrice = payoffs.Count > 0 ? ((dynamic)payoffs[0]).entryPx : null;
        var genEt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MarketTime.Eastern);

        // Folder id: ET generation stamp to the second, plus a bijective
        // base-26 suffix (-a … -z, -aa …) when that folder already exists — so
        // several runs the same day (a repeating/multi-execute strategy, or just
        // re-running) never collide or overwrite an earlier graph.
        var tickerDir = Path.Combine(outRoot, symbol.ToLowerInvariant());
        var baseStamp = genEt.ToString("yyyy-MM-ddTHH.mm.ss", CultureInfo.InvariantCulture);
        var stamp = baseStamp;
        for (int sfx = 1; Directory.Exists(Path.Combine(tickerDir, stamp)); sfx++)
            stamp = $"{baseStamp}-{Base26(sfx)}";

        // ── DATA payload for the page ──
        var data = new
        {
            symbol,
            strategy = strategyTitle,
            dateEt = dateEt.ToString("yyyy-MM-dd"),
            feed = feed.FeedTier.ToUpperInvariant(),
            generatedEt = genEt.ToString("yyyy-MM-dd HH:mm:ss 'ET'", CultureInfo.InvariantCulture),
            session = def.Session.ToString(),
            side = def.Direction == TradeDirection.Short ? "SELL short" : "BUY long",
            conditions = condLabels,
            repeat,
            firstFire = firstFireEt,
            entryPrice,
            fires,
            payoffs,
            payoffCount = payoffs.Count,
            totalPnl = Math.Round(totalPnl, 2),
            bestPassed = Math.Max(0, bestPassed),
            bestFail,
            mermaid = def.ToMermaid(),
            bars = rows,
        };
        var dataJson = JsonSerializer.Serialize(data, JsonOpts);

        // ── Render + write the run page ──
        var page = Template
            .Replace("__DATA__", dataJson)
            .Replace("__STRATEGY_HTML__", def.ToHtml());

        var runDir = Path.Combine(tickerDir, stamp);
        Directory.CreateDirectory(runDir);
        await File.WriteAllTextAsync(Path.Combine(runDir, "index.htm"), page, new UTF8Encoding(false));

        // A small sidecar the ticker index reads to summarize each run.
        var runMeta = new
        {
            stamp, dateEt = data.dateEt, feed = data.feed, strategy = strategyTitle,
            fired = firstFireEt is not null, fireEt = firstFireEt, entryPrice,
            payoffCount = payoffs.Count, totalPnl = Math.Round(totalPnl, 2),
            bars = rows.Count, generatedEt = data.generatedEt,
        };
        await File.WriteAllTextAsync(Path.Combine(runDir, "run.json"),
            JsonSerializer.Serialize(runMeta, JsonOpts), new UTF8Encoding(false));

        // ── Regenerate the per-ticker index and the root all-tickers index ──
        await WriteTickerIndexAsync(tickerDir, symbol);
        await WriteRootIndexAsync(outRoot);

        var urlBase = $"https://mindattic.com/idiotproof/replays/{symbol.ToLowerInvariant()}";
        if (payoffs.Count == 0)
            Console.WriteLine($"  no fire — best {Math.Max(0, bestPassed)}/{condLabels.Count} in-session (waiting on {bestFail ?? "n/a"})");
        else
        {
            Console.WriteLine($"  {payoffs.Count} payoff(s){(repeat ? " (repeat)" : " (one-shot)")}, total {totalPnl:+0.##;-0.##;0}%:");
            foreach (dynamic p in payoffs)
                Console.WriteLine($"     {p.entryEt}→{p.exitEt} ET  ${p.entryPx:0.##}→${p.exitPx:0.##}  {p.pnlPct:+0.##;-0.##;0}%  ({p.reason})");
        }
        Console.WriteLine($"  run:   {Path.Combine(runDir, "index.htm")}");
        Console.WriteLine($"  index: {Path.Combine(tickerDir, "index.htm")}");
        Console.WriteLine($"  URL:   {urlBase}/{stamp}/   (index: {urlBase}/)");
        Console.WriteLine("  publish: cd MindAttic.Deploy && npm run deploy -- --site idiotproof-replays");
        return 0;
    }

    private sealed record BarRow(
        string et, double o, double h, double l, double c, long v,
        double vwap, double whigh, double volx, bool[] cnd, bool inSession, bool fire, bool exit);

    // ── ticker index ──
    private static async Task WriteTickerIndexAsync(string tickerDir, string symbol)
    {
        Directory.CreateDirectory(tickerDir);
        var runs = new List<JsonElement>();
        foreach (var dir in Directory.GetDirectories(tickerDir))
        {
            var meta = Path.Combine(dir, "run.json");
            if (!File.Exists(meta)) continue;
            try { runs.Add(JsonDocument.Parse(await File.ReadAllTextAsync(meta)).RootElement.Clone()); }
            catch { /* skip unreadable run */ }
        }
        // Newest first — stamps are lexically sortable (ISO-ish).
        runs.Sort((a, b) => string.CompareOrdinal(b.GetProperty("stamp").GetString(), a.GetProperty("stamp").GetString()));

        var sb = new StringBuilder();
        foreach (var r in runs)
        {
            var st = r.GetProperty("stamp").GetString()!;
            var fired = r.GetProperty("fired").GetBoolean();
            var fireEt = r.TryGetProperty("fireEt", out var fe) && fe.ValueKind == JsonValueKind.String ? fe.GetString() : null;
            var price = r.TryGetProperty("entryPrice", out var pe) && pe.ValueKind == JsonValueKind.Number ? pe.GetDouble() : (double?)null;
            var day = r.GetProperty("dateEt").GetString();
            var feed = r.GetProperty("feed").GetString();
            var gen = r.TryGetProperty("generatedEt", out var g) ? g.GetString() : st;
            var verdict = fired
                ? $"<span class=\"fire\">FIRED {System.Net.WebUtility.HtmlEncode(fireEt)} ET @ ${price:0.##}</span>"
                : "<span class=\"nofire\">no fire</span>";
            sb.Append($"<a class=\"run\" href=\"./{Uri.EscapeDataString(st)}/index.htm\">" +
                      $"<span class=\"day\">{System.Net.WebUtility.HtmlEncode(day)}</span>" +
                      $"<span class=\"verdict\">{verdict}</span>" +
                      $"<span class=\"feed\">{System.Net.WebUtility.HtmlEncode(feed)}</span>" +
                      $"<span class=\"gen\">generated {System.Net.WebUtility.HtmlEncode(gen)}</span></a>");
        }
        if (runs.Count == 0) sb.Append("<p class=\"empty\">No replays yet.</p>");

        var html = IndexTemplate
            .Replace("__SYMBOL__", System.Net.WebUtility.HtmlEncode(symbol))
            .Replace("__COUNT__", runs.Count.ToString())
            .Replace("__RUNS__", sb.ToString());
        await File.WriteAllTextAsync(Path.Combine(tickerDir, "index.htm"), html, new UTF8Encoding(false));
    }

    /// <summary>
    /// Built-in repeating intraday-momentum strategy (`--profile momentum`).
    /// Enters on each VWAP reclaim with volume confirmation, rides with a tight
    /// trailing stop, flattens before the bell, and re-arms — so a multi-leg
    /// premarket day (dip → reclaim → run, several times) yields several
    /// payoffs instead of the gapper's single ride. Composed with the real DSL.
    /// </summary>
    private static StrategyDefinition BuildMomentum(string symbol) =>
        Stock.Ticker(symbol)
            .Name($"{symbol} Intraday Momentum (repeat)")
            .Session(TradingSession.Extended)
            .RequireEntryWindow("04:00", "09:29")
            .OnVwapReclaim()
            .WithVolumeConfirm(1.5)
            .IsPriceBetween(1, 1000)
            .Long()
            .QuantityNotional(1000)
            .StopLossPercent(4)
            .TrailingStopLoss(8)
            .SellBy("09:29")
            .Repeat()
            .Build();

    // ── root index: every ticker ever replayed, newest activity first ──
    private static async Task WriteRootIndexAsync(string outRoot)
    {
        Directory.CreateDirectory(outRoot);
        var tickers = new List<(string sym, int count, string latestStamp, string body)>();
        foreach (var tdir in Directory.GetDirectories(outRoot))
        {
            var sym = Path.GetFileName(tdir).ToUpperInvariant();
            var runs = new List<JsonElement>();
            foreach (var rdir in Directory.GetDirectories(tdir))
            {
                var meta = Path.Combine(rdir, "run.json");
                if (!File.Exists(meta)) continue;
                try { runs.Add(JsonDocument.Parse(await File.ReadAllTextAsync(meta)).RootElement.Clone()); }
                catch { /* skip */ }
            }
            if (runs.Count == 0) continue;
            runs.Sort((a, b) => string.CompareOrdinal(b.GetProperty("stamp").GetString(), a.GetProperty("stamp").GetString()));
            var latest = runs[0];
            var latestStamp = latest.GetProperty("stamp").GetString()!;
            var fired = runs.Count(r => r.TryGetProperty("fired", out var f) && f.GetBoolean());
            var latestVerdict = latest.TryGetProperty("fired", out var lf) && lf.GetBoolean()
                ? (latest.TryGetProperty("totalPnl", out var pnl) && pnl.ValueKind == JsonValueKind.Number
                    ? $"<span class=\"fire\">last: {(pnl.GetDouble() >= 0 ? "+" : "")}{pnl.GetDouble():0.##}%</span>"
                    : "<span class=\"fire\">last: fired</span>")
                : "<span class=\"nofire\">last: no fire</span>";
            var latestGen = latest.TryGetProperty("generatedEt", out var g) ? g.GetString() : latestStamp;
            var body =
                $"<a class=\"tk\" href=\"./{Uri.EscapeDataString(Path.GetFileName(tdir))}/index.htm\">" +
                $"<span class=\"sym\">{System.Net.WebUtility.HtmlEncode(sym)}</span>" +
                $"<span class=\"cnt\">{runs.Count} replay{(runs.Count == 1 ? "" : "s")} · {fired} fired</span>" +
                $"<span class=\"vd\">{latestVerdict}</span>" +
                $"<span class=\"gen\">latest {System.Net.WebUtility.HtmlEncode(latestGen)} ET-gen</span></a>";
            tickers.Add((sym, runs.Count, latestStamp, body));
        }
        tickers.Sort((a, b) => string.CompareOrdinal(b.latestStamp, a.latestStamp)); // newest activity first
        var rows = tickers.Count > 0 ? string.Concat(tickers.Select(t => t.body)) : "<p class=\"empty\">No replays yet.</p>";
        var html = RootIndexTemplate
            .Replace("__TICKERS__", rows)
            .Replace("__COUNT__", tickers.Count.ToString());
        await File.WriteAllTextAsync(Path.Combine(outRoot, "index.htm"), html, new UTF8Encoding(false));
    }

    // ── helpers ──
    private static GapperProfile? LoadProfile(string id, string? overridePath)
    {
        var path = overridePath ?? Path.Combine(
            @"D:\Projects\MindAttic\IdiotProof\IdiotProof.Blazor\wwwroot\data\gapper-profiles.json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        // File is either an array of profiles or { "profiles": [...] }.
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("profiles", out var p) ? p : default;
        if (arr.ValueKind != JsonValueKind.Array) return null;
        foreach (var el in arr.EnumerateArray())
        {
            var pid = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.Equals(pid, id, StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.Deserialize<GapperProfile>(el.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        return null;
    }

    private static async Task<Guid?> ResolveUserAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        if (opt.TryGetValue("user", out var u) && Guid.TryParse(u, out var g)) return g;
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var ids = db.AuthUsers.Select(x => x.Id).Take(2).ToList();
        return ids.Count == 1 ? ids[0] : null;
    }

    private static DateTime EtToUtc(DateOnly dateEt, TimeSpan tod)
    {
        var etLocal = DateTime.SpecifyKind(dateEt.ToDateTime(TimeOnly.FromTimeSpan(tod)), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(etLocal, MarketTime.Eastern);
    }

    /// <summary>Bijective base-26 suffix: 1→a, 26→z, 27→aa, 28→ab, …</summary>
    private static string Base26(int n)
    {
        var s = "";
        while (n > 0) { n--; s = (char)('a' + n % 26) + s; n /= 26; }
        return s;
    }

    private static double Round(double v) => Math.Round(v, 4);
    private static int Fail(string s) { Console.Error.WriteLine("ERROR: " + s); return 1; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    // Templates live at the bottom for readability. Tokens: __DATA__,
    // __STRATEGY_HTML__ (run page); __SYMBOL__, __COUNT__, __RUNS__ (index).
    private const string Template = ReplayTemplates.Run;
    private const string IndexTemplate = ReplayTemplates.Index;
    private const string RootIndexTemplate = ReplayTemplates.RootIndex;
}
