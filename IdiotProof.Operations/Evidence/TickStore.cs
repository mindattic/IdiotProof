using System.Globalization;
using System.IO.Compression;
using IdiotProof.Evidence.Microstructure;
using IdiotProof.Operations.Market;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Evidence;

/// <summary>One session's trade prints and quotes for order-flow research (regular session, 9:30-16:00 ET).</summary>
public interface ITickStore
{
    Task<IReadOnlyList<TradePrint>> GetTradesAsync(string symbol, DateOnly dayEt, CancellationToken ct);
    Task<IReadOnlyList<QuoteTick>> GetQuotesAsync(string symbol, DateOnly dayEt, CancellationToken ct);
}

/// <summary>
/// Alpaca trades/quotes with a gzip file cache: a finished ET session is fetched once and served from
/// disk afterwards; today's session is never cached because it isn't complete yet.
/// </summary>
public sealed class AlpacaTickStore(OperationContext ctx, string? cacheRoot = null) : ITickStore
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly string root = cacheRoot
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdiotProof", "ticks");

    public async Task<IReadOnlyList<TradePrint>> GetTradesAsync(string symbol, DateOnly dayEt, CancellationToken ct)
    {
        var rows = await LoadAsync(symbol, dayEt, "trades", t => (MarketData.Time(t, "t"), MarketData.Dec(t, "p"), MarketData.Dec(t, "s")), ct);
        return rows.Select(r => new TradePrint(r.Time, r.A, r.B)).ToList();
    }

    public async Task<IReadOnlyList<QuoteTick>> GetQuotesAsync(string symbol, DateOnly dayEt, CancellationToken ct)
    {
        var rows = await LoadAsync(symbol, dayEt, "quotes", q => (MarketData.Time(q, "t"), MarketData.Dec(q, "bp"), MarketData.Dec(q, "ap")), ct);
        return rows.Select(r => new QuoteTick(r.Time, r.A, r.B)).ToList();
    }

    private async Task<List<(DateTime Time, double A, double B)>> LoadAsync(string symbol, DateOnly dayEt, string kind,
        Func<System.Text.Json.JsonElement, (DateTime?, decimal?, decimal?)> read, CancellationToken ct)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        bool finished = dayEt < DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        var path = Path.Combine(root, symbol, $"{dayEt:yyyy-MM-dd}.{kind}.csv.gz");
        if (finished && File.Exists(path)) return Read(path);

        var open = TimeZoneInfo.ConvertTimeToUtc(dayEt.ToDateTime(new TimeOnly(9, 30)), MarketTime.Eastern);
        var close = TimeZoneInfo.ConvertTimeToUtc(dayEt.ToDateTime(new TimeOnly(16, 0)), MarketTime.Eastern);
        if (close > ctx.UtcNow) close = ctx.UtcNow;
        var rows = new List<(DateTime, double, double)>();
        if (open >= close) return rows;

        var query = MarketData.Q(("start", MarketData.Rfc3339(open)), ("end", MarketData.Rfc3339(close)),
            ("limit", "10000"), ("feed", MarketData.Feed), ("sort", "asc"));
        await using var api = await MarketData.OpenAsync(ctx);
        await foreach (var e in api.GetDataPagedAsync($"/v2/stocks/{Uri.EscapeDataString(symbol)}/{kind}", kind, query, maxPages: 1000, ct: ct))
        {
            var (t, a, b) = read(e);
            if (t is { } time && a is { } av && b is { } bv) rows.Add((time, (double)av, (double)bv));
        }
        if (finished) Write(path, rows);
        return rows;
    }

    private static List<(DateTime, double, double)> Read(string path)
    {
        var rows = new List<(DateTime, double, double)>();
        using var reader = new StreamReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress));
        while (reader.ReadLine() is { } line)
        {
            var p = line.Split(',');
            if (p.Length != 3) continue;
            rows.Add((new DateTime(long.Parse(p[0], Inv), DateTimeKind.Utc), double.Parse(p[1], Inv), double.Parse(p[2], Inv)));
        }
        return rows;
    }

    private static void Write(string path, List<(DateTime Time, double A, double B)> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var writer = new StreamWriter(new GZipStream(File.Create(tmp), CompressionLevel.Optimal)))
            foreach (var r in rows) writer.WriteLine(string.Create(Inv, $"{r.Time.Ticks},{r.A},{r.B}"));
        File.Move(tmp, path, overwrite: true);
    }
}
