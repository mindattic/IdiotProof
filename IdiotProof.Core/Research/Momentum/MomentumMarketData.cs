using System.Net;
using System.Text.Json;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.DataFeeds;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Core.Research.Momentum;

/// <summary>One stock's line in a day's gainer table (prices on the trade date's share basis).</summary>
public sealed record MoverRow(string S, decimal Pc, decimal Ph, decimal O, decimal H, decimal C, decimal V, double Gap, double HighPct, double ClosePct, int RankHigh, int RankClose, int RankGap);

/// <summary>A session's gainers: liquid names (prior close >= $0.10, >= $1M traded), the top 300 by high, close and gap.</summary>
public sealed record MoversDay(DateOnly Date, DateOnly Prev, int Count, List<MoverRow> Rows)
{
    public MoverRow? Find(string symbol) => Rows.FirstOrDefault(r => r.S == symbol);
}

/// <summary>A daily bar with the raw-to-split-adjusted ratio of that day (adj open / raw open).</summary>
public sealed record SymbolDay(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, decimal Ratio);

/// <summary>
/// Market data for the Momentum. tracker (IP-T493), all through the Alpaca data API (SIP) with the owner's or host's
/// keys, paced by one rate gate and cached on disk under <c>{root}</c> so a rerun requests nothing twice:
/// <c>assets.json</c> (refreshed daily), <c>movers/{date}.json</c> (the day's gainer ranks from every active asset's
/// raw and split-adjusted daily bars), <c>bars/</c> (1-minute bars 04:00-20:00 ET in the
/// <see cref="FileBackedBarStore"/> format, raw prices as traded that day). Read-only against Alpaca.
/// </summary>
public sealed class MomentumMarketData : IAsyncDisposable
{
    public const decimal MinPrevClose = 0.10m;
    public const decimal MinDollarVolume = 1_000_000m;
    public const int KeepTop = 300;
    public static readonly TimeSpan SessionStart = new(4, 0, 0);
    public static readonly TimeSpan SessionEnd = new(20, 0, 0);

    private readonly string root;
    private readonly StudyKeys keys;
    private readonly AlpacaDataFeed feed;
    private readonly AlpacaDataRateGate gate;
    private readonly Func<CancellationToken, Task<List<StudyAsset>>> loadAssets;
    private readonly ILogger log;
    private readonly FileBackedBarStore store;
    private readonly Dictionary<DateOnly, MoversDay?> movers = [];
    private readonly Dictionary<string, List<SymbolDay>> daily = new(StringComparer.Ordinal);
    private Dictionary<string, StudyAsset>? assets;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public MomentumMarketData(string root, StudyKeys keys, AlpacaDataRateGate gate, Func<CancellationToken, Task<List<StudyAsset>>> loadAssets, ILogger log)
    {
        this.root = root;
        this.keys = keys;
        this.gate = gate;
        this.loadAssets = loadAssets;
        this.log = log;
        feed = new AlpacaDataFeed(keys.KeyId, keys.Secret, "sip");
        store = new FileBackedBarStore(Path.Combine(root, "bars"));
        Directory.CreateDirectory(Path.Combine(root, "movers"));
    }

    public string DataSource => "alpaca sip 1-min";
    public long Requests => gate.Requests;

    public ValueTask DisposeAsync() => feed.DisposeAsync();

    // ── assets ──

    public async Task<Dictionary<string, StudyAsset>> AssetsAsync(CancellationToken ct)
    {
        if (assets is not null) return assets;
        var path = Path.Combine(root, "assets.json");
        List<StudyAsset>? list = null;
        if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddDays(-1))
            list = JsonSerializer.Deserialize<List<StudyAsset>>(await File.ReadAllTextAsync(path, ct));
        if (list is null || list.Count == 0)
        {
            list = await loadAssets(ct);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(list, Json), ct);
        }
        assets = new Dictionary<string, StudyAsset>(StringComparer.Ordinal);
        foreach (var a in list) if (!assets.TryGetValue(a.Symbol, out var have) || (!have.Active && a.Active)) assets[a.Symbol] = a;
        return assets;
    }

    // ── movers ──

    public async Task<MoversDay?> MoversAsync(DateOnly d, CancellationToken ct)
    {
        if (movers.TryGetValue(d, out var m)) return m;
        await EnsureMoversAsync([d], ct);
        return movers.GetValueOrDefault(d);
    }

    /// <summary>Make sure each date (and its prior session) has a movers file; missing ones are computed in windows.</summary>
    public async Task EnsureMoversAsync(IEnumerable<DateOnly> dates, CancellationToken ct)
    {
        var missing = new List<DateOnly>();
        foreach (var d in dates.Distinct().OrderBy(x => x))
        {
            if (movers.ContainsKey(d)) continue;
            var path = MoversPath(d);
            if (File.Exists(path))
            {
                var day = JsonSerializer.Deserialize<MoversDay>(await File.ReadAllTextAsync(path, ct));
                movers[d] = day;
                if (day is not null && !movers.ContainsKey(day.Prev) && File.Exists(MoversPath(day.Prev)))
                    movers[day.Prev] = JsonSerializer.Deserialize<MoversDay>(await File.ReadAllTextAsync(MoversPath(day.Prev), ct));
                continue;
            }
            missing.Add(d);
        }
        if (missing.Count == 0) return;

        // windows of nearby dates share one pull of every active asset's daily bars
        var windows = new List<List<DateOnly>>();
        foreach (var d in missing)
        {
            if (windows.Count > 0 && d.DayNumber - windows[^1][0].DayNumber <= 28) windows[^1].Add(d);
            else windows.Add([d]);
        }
        var universe = (await AssetsAsync(ct)).Values.Where(a => a.Active).Select(a => a.Symbol).ToList();
        if (!universe.Contains("SPY")) universe.Add("SPY");
        foreach (var w in windows) await ComputeWindowAsync(universe, w, ct);
        foreach (var d in missing) movers.TryAdd(d, null);
    }

    private string MoversPath(DateOnly d) => Path.Combine(root, "movers", d.ToString("yyyy-MM-dd") + ".json");

    private async Task ComputeWindowAsync(List<string> universe, List<DateOnly> want, CancellationToken ct)
    {
        var from = want[0].AddDays(-14);
        var to = want[^1];
        log.LogInformation("momentum movers: daily bars for {N} assets {From}..{To}", universe.Count, from, to);
        var raw = await DailyForAsync(universe, from, to, "raw", ct);
        var adj = await DailyForAsync(universe, from, to, "split", ct);
        if (!raw.TryGetValue("SPY", out var spy) || spy.Count == 0) { log.LogWarning("momentum movers: no SPY bars {From}..{To}", from, to); return; }
        var sessions = spy.Select(b => b.Date).Distinct().OrderBy(x => x).ToList();

        foreach (var d in sessions.Skip(1))
        {
            var p = sessions[sessions.IndexOf(d) - 1];
            var rows = new List<(string S, decimal Pc, decimal Ph, Candle Bar)>();
            foreach (var (sym, bars) in raw)
            {
                var bd = bars.FirstOrDefault(b => b.Date == d);
                var bp = bars.FirstOrDefault(b => b.Date == p);
                if (bd is null || bp is null) continue;
                var ad = adj.GetValueOrDefault(sym)?.FirstOrDefault(b => b.Date == d);
                var ap = adj.GetValueOrDefault(sym)?.FirstOrDefault(b => b.Date == p);
                decimal rd = ad is not null && bd.Open > 0 ? ad.Open / bd.Open : 1, rp = ap is not null && bp.Open > 0 ? ap.Open / bp.Open : 1;
                decimal f = rd > 0 ? rp / rd : 1;
                if (Math.Abs(f - 1) < 0.02m) f = 1;
                var pc = bp.Close * f;
                if (pc < MinPrevClose || bd.Close * bd.Volume < MinDollarVolume) continue;
                rows.Add((sym, pc, bp.High * f, new Candle { Open = bd.Open, High = bd.High, Low = bd.Low, Close = bd.Close, Volume = bd.Volume }));
            }
            var lines = rows.Select(r => new
            {
                r.S, r.Pc, r.Ph, r.Bar,
                Gap = MomentumScenarioEvaluator.Pct(r.Bar.Open, r.Pc),
                Hi = MomentumScenarioEvaluator.Pct(r.Bar.High, r.Pc),
                Cl = MomentumScenarioEvaluator.Pct(r.Bar.Close, r.Pc),
            }).ToList();
            var rankHigh = lines.OrderByDescending(x => x.Hi).Select((x, i) => (x.S, i + 1)).ToDictionary(x => x.S, x => x.Item2);
            var rankClose = lines.OrderByDescending(x => x.Cl).Select((x, i) => (x.S, i + 1)).ToDictionary(x => x.S, x => x.Item2);
            var rankGap = lines.OrderByDescending(x => x.Gap).Select((x, i) => (x.S, i + 1)).ToDictionary(x => x.S, x => x.Item2);
            var kept = lines.Where(x => rankHigh[x.S] <= KeepTop || rankClose[x.S] <= KeepTop || rankGap[x.S] <= KeepTop)
                .OrderBy(x => rankHigh[x.S])
                .Select(x => new MoverRow(x.S, Math.Round(x.Pc, 4), Math.Round(x.Ph, 4), x.Bar.Open, x.Bar.High, x.Bar.Close, x.Bar.Volume, x.Gap, x.Hi, x.Cl, rankHigh[x.S], rankClose[x.S], rankGap[x.S]))
                .ToList();
            var day = new MoversDay(d, p, lines.Count, kept);
            movers[d] = day;
            await File.WriteAllTextAsync(MoversPath(d), JsonSerializer.Serialize(day, Json), ct);
        }
    }

    private async Task<Dictionary<string, List<SymbolDay>>> DailyForAsync(List<string> symbols, DateOnly from, DateOnly to, string adjustment, CancellationToken ct)
    {
        var result = new Dictionary<string, List<SymbolDay>>(StringComparer.Ordinal);
        var start = MinuteBarMath.ToUtc(from, TimeSpan.Zero);
        var end = MinuteBarMath.ToUtc(to.AddDays(1), TimeSpan.Zero);
        async Task Fetch(IReadOnlyList<string> chunk)
        {
            try
            {
                var got = await feed.GetHistoricalCandlesForSymbolsAsync(chunk, TimeSpan.FromDays(1), start, end, 10000, gate.WaitAsync, ct, adjustment);
                foreach (var (sym, bars) in got.Bars)
                    result[sym.ToUpperInvariant()] = bars.Select(c => new SymbolDay(DateOnly.FromDateTime(MinuteBarMath.ToEastern(c.StartUtc)), c.Open, c.High, c.Low, c.Close, c.Volume, 1))
                        .Where(b => b.Date >= from && b.Date <= to).OrderBy(b => b.Date).ToList();
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
            {
                if (chunk.Count == 1) return;
                await Fetch(chunk.Take(chunk.Count / 2).ToList());
                await Fetch(chunk.Skip(chunk.Count / 2).ToList());
            }
        }
        int n = 0;
        foreach (var chunk in symbols.Chunk(AlpacaDataFeed.MaxSymbolsPerRequest))
        {
            await Fetch(chunk);
            if (++n % 25 == 0) log.LogInformation("momentum daily {Adj}: {N} chunks, {Req} requests", adjustment, n, gate.Requests);
        }
        return result;
    }

    // ── one symbol's daily bars (split ratio per day) ──

    /// <summary>Daily bars from <paramref name="from"/> to <paramref name="to"/>, raw prices with each day's split ratio, for many symbols at once.</summary>
    public async Task EnsureDailyAsync(IEnumerable<string> symbols, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var need = symbols.Distinct().Where(s => !daily.TryGetValue(s, out var have) || have.Count == 0 || have[0].Date > from.AddDays(4) || have[^1].Date < to.AddDays(-4)).ToList();
        if (need.Count == 0) return;
        var raw = await DailyForAsync(need, from, to, "raw", ct);
        var adj = await DailyForAsync(need, from, to, "split", ct);
        foreach (var s in need)
        {
            var r = raw.GetValueOrDefault(s) ?? [];
            var a = (adj.GetValueOrDefault(s) ?? []).ToDictionary(x => x.Date);
            var merged = r.Select(b => b with { Ratio = a.TryGetValue(b.Date, out var x) && b.Open > 0 ? x.Open / b.Open : 1 }).ToList();
            if (daily.TryGetValue(s, out var old))
                merged = old.Concat(merged).GroupBy(x => x.Date).Select(g => g.Last()).OrderBy(x => x.Date).ToList();
            daily[s] = merged;
        }
    }

    public IReadOnlyList<SymbolDay> Daily(string symbol) => daily.GetValueOrDefault(symbol) ?? [];

    /// <summary>The prior session's close on the share basis of <paramref name="d"/> (a split between them is undone).</summary>
    public (decimal? PrevClose, decimal? PrevHigh, DateOnly? Prev, SymbolDay? Day) PriorClose(string symbol, DateOnly d)
    {
        var bars = Daily(symbol);
        var i = bars.ToList().FindIndex(b => b.Date == d);
        SymbolDay? today = i >= 0 ? bars[i] : null;
        var prev = bars.LastOrDefault(b => b.Date < d);
        if (prev is null) return (null, null, null, today);
        var rd = today?.Ratio ?? bars.FirstOrDefault(b => b.Date > d)?.Ratio ?? 1;
        decimal f = rd > 0 ? prev.Ratio / rd : 1;
        if (Math.Abs(f - 1) < 0.02m) f = 1;
        return (prev.Close * f, prev.High * f, prev.Date, today);
    }

    // ── minute bars ──

    /// <summary>Fetch every (symbol, completed session day) not yet in the minute store, 04:00-20:00 ET.</summary>
    public async Task EnsureMinuteAsync(IEnumerable<(string Symbol, DateOnly Date)> wanted, CancellationToken ct)
    {
        var need = wanted.Distinct().Where(w => !store.HasDay(w.Symbol, w.Date)).GroupBy(w => w.Date).ToList();
        foreach (var g in need)
        {
            var d = g.Key;
            var start = MinuteBarMath.ToUtc(d, SessionStart);
            var end = MinuteBarMath.ToUtc(d, SessionEnd);
            async Task Fetch(IReadOnlyList<string> chunk)
            {
                try
                {
                    var got = await feed.GetHistoricalCandlesForSymbolsAsync(chunk, TimeSpan.FromMinutes(1), start, end, 10000, gate.WaitAsync, ct);
                    foreach (var s in chunk)
                    {
                        var bars = got.Bars.FirstOrDefault(kv => string.Equals(kv.Key, s, StringComparison.OrdinalIgnoreCase)).Value ?? [];
                        store.AppendDay(s, d, bars.Where(b => b.StartUtc >= start && b.StartUtc < end).ToList());
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
                {
                    if (chunk.Count == 1) { store.AppendDay(chunk[0], d, []); return; }
                    await Fetch(chunk.Take(chunk.Count / 2).ToList());
                    await Fetch(chunk.Skip(chunk.Count / 2).ToList());
                }
            }
            foreach (var chunk in g.Select(x => x.Symbol).Distinct().Chunk(AlpacaDataFeed.MaxSymbolsPerRequest)) await Fetch(chunk);
        }
    }

    public IReadOnlyList<Candle> Minute(string symbol, DateOnly d) => store.HasDay(symbol, d) ? store.ReadDay(symbol, d) : [];

    /// <summary>The day's bars as evaluator input, with ET time of day and VWAP from 04:00.</summary>
    public IReadOnlyList<ScenarioBar> ScenarioBars(string symbol, DateOnly d) =>
        MomentumScenarioEvaluator.WithVwap(Minute(symbol, d).Select(c => (c.StartUtc, MinuteBarMath.ToEastern(c.StartUtc).TimeOfDay, c.Open, c.High, c.Low, c.Close, c.Volume)));
}
