using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Core.Evidence;

/// <summary>What one symbol (or the whole store) holds: covered session days, bars, and the share stamped SIP.</summary>
public sealed record FileBarCoverage(int Days, int EmptyDays, long Bars, long SipBars)
{
    public double SipShare => Bars == 0 ? 0 : (double)SipBars / Bars;
}

/// <summary>
/// One-minute bars on disk for the gapper study (IP-T442), one packed little-endian file per
/// symbol-month at <c>{root}\{SYMBOL}\{yyyy-MM}.bin</c>: a 16-byte header (magic <c>IPGB</c>, format
/// version, record size) followed by appended day blocks, each a 12-byte day header (ET day number,
/// bar count, data tier) and fixed 64-byte records (start ticks, OHLC and VWAP scaled by 10^6,
/// volume scaled by 100, trade count). A day block is the coverage mark: a day with zero bars is
/// still written, so a pull never re-requests it. Appends are idempotent per (symbol, day) and a
/// truncated trailing block from a killed process is dropped on the next open. Implements
/// <see cref="IHistoricalBarStore"/> for minute bars (missing completed days are fetched from the
/// feed when one is given); daily bars and the universe delegate to an optional inner store.
/// </summary>
public sealed class FileBackedBarStore : IHistoricalBarStore
{
    private const uint Magic = 0x4247_5049; // "IPGB" little-endian
    private const int FormatVersion = 1;
    private const int HeaderSize = 16;
    private const int DayHeaderSize = 12;
    private const int RecordSize = 64;
    private const long PriceScale = 1_000_000;
    private const long VolumeScale = 100;

    private readonly string root;
    private readonly IMarketDataFeed? feed;
    private readonly IHistoricalBarStore? daily;
    private readonly TimeProvider clock;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.OrdinalIgnoreCase);

    public FileBackedBarStore(string root, IMarketDataFeed? feed = null, IHistoricalBarStore? daily = null, TimeProvider? clock = null)
    {
        this.root = Path.GetFullPath(root);
        this.feed = feed;
        this.daily = daily;
        this.clock = clock ?? TimeProvider.System;
    }

    public string Root => root;

    public static string FilePath(string root, string symbol, DateOnly dateEt) =>
        Path.Combine(root, symbol.ToUpperInvariant(), dateEt.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".bin");

    // ── Coverage ─────────────────────────────────────────────────────────

    public bool HasDay(string symbol, DateOnly dateEt)
    {
        var path = FilePath(root, symbol, dateEt);
        if (!File.Exists(path)) return false;
        using var stream = Open(path, FileAccess.Read);
        return ReadIndex(stream).ContainsKey(dateEt.DayNumber);
    }

    /// <summary>Session days in [from, to] that have a coverage mark (bars or an explicit empty day).</summary>
    public HashSet<DateOnly> CoveredDays(string symbol, DateOnly from, DateOnly to)
    {
        var result = new HashSet<DateOnly>();
        foreach (var path in MonthFiles(symbol, from, to))
        {
            using var stream = Open(path, FileAccess.Read);
            foreach (var day in ReadIndex(stream).Keys)
            {
                var d = DateOnly.FromDayNumber(day);
                if (d >= from && d <= to) result.Add(d);
            }
        }
        return result;
    }

    public FileBarCoverage Coverage(string symbol, DateOnly? from = null, DateOnly? to = null)
    {
        int days = 0, empty = 0;
        long bars = 0, sip = 0;
        var dir = Path.Combine(root, symbol.ToUpperInvariant());
        if (!Directory.Exists(dir)) return new FileBarCoverage(0, 0, 0, 0);
        foreach (var path in Directory.EnumerateFiles(dir, "*.bin"))
        {
            using var stream = Open(path, FileAccess.Read);
            foreach (var (day, entry) in ReadIndex(stream))
            {
                var d = DateOnly.FromDayNumber(day);
                if (from is { } f && d < f) continue;
                if (to is { } t && d > t) continue;
                days++;
                if (entry.Count == 0) empty++;
                bars += entry.Count;
                if (entry.Feed == 1) sip += entry.Count;
            }
        }
        return new FileBarCoverage(days, empty, bars, sip);
    }

    public IReadOnlyList<string> Symbols() =>
        Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Select(Path.GetFileName).Where(s => s is not null).Select(s => s!).OrderBy(s => s, StringComparer.Ordinal).ToList()
            : [];

    // ── Writes ───────────────────────────────────────────────────────────

    /// <summary>Appends one session day (idempotent: a day already present is left untouched). Returns true when written.</summary>
    public bool AppendDay(string symbol, DateOnly dateEt, IReadOnlyList<Candle> bars) =>
        AppendDays(symbol, [(dateEt, bars)]) > 0;

    /// <summary>Appends many days of one symbol, one file open per month. Returns the number of days written.</summary>
    public int AppendDays(string symbol, IEnumerable<(DateOnly DateEt, IReadOnlyList<Candle> Bars)> days)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        int written = 0;
        foreach (var month in days.GroupBy(d => (d.DateEt.Year, d.DateEt.Month)))
        {
            var path = FilePath(root, symbol, new DateOnly(month.Key.Year, month.Key.Month, 1));
            var gate = locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
            gate.Wait();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = Open(path, FileAccess.ReadWrite);
                var index = ReadIndex(stream, repair: true);
                stream.Seek(0, SeekOrigin.End);
                foreach (var (date, bars) in month)
                {
                    if (index.ContainsKey(date.DayNumber)) continue;
                    WriteDay(stream, date, bars);
                    index[date.DayNumber] = new DayEntry(0, bars.Count, 0);
                    written++;
                }
                stream.Flush(true);
            }
            finally { gate.Release(); }
        }
        return written;
    }

    // ── Reads ────────────────────────────────────────────────────────────

    public IReadOnlyList<Candle> ReadDay(string symbol, DateOnly dateEt) => ReadDays(symbol, dateEt, dateEt);

    /// <summary>Bars of the covered session days in [from, to], ascending; uncovered days contribute nothing.</summary>
    public IReadOnlyList<Candle> ReadDays(string symbol, DateOnly from, DateOnly to)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        var result = new List<Candle>();
        foreach (var path in MonthFiles(symbol, from, to))
        {
            using var stream = Open(path, FileAccess.Read);
            var index = ReadIndex(stream);
            foreach (var (day, entry) in index.OrderBy(kv => kv.Key))
            {
                var d = DateOnly.FromDayNumber(day);
                if (d < from || d > to || entry.Count == 0) continue;
                stream.Seek(entry.Offset, SeekOrigin.Begin);
                var buffer = new byte[RecordSize];
                var note = entry.Feed switch { 1 => AlpacaDataFeed.SourceLabel("sip"), 2 => AlpacaDataFeed.SourceLabel("iex"), 3 => "Mock", _ => "" };
                for (int i = 0; i < entry.Count; i++)
                {
                    stream.ReadExactly(buffer);
                    result.Add(ReadRecord(buffer, symbol, note));
                }
            }
        }
        result.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
        return result;
    }

    // ── IHistoricalBarStore ──────────────────────────────────────────────

    public async Task<IReadOnlyList<Candle>> GetBarsAsync(string symbol, DateOnly fromEt, DateOnly toEt, BarTimeframe timeframe, CancellationToken ct = default)
    {
        if (timeframe == BarTimeframe.Day1)
            return daily is null
                ? throw new NotSupportedException("The file-backed store holds minute bars only; give it an inner store for daily bars.")
                : await daily.GetBarsAsync(symbol, fromEt, toEt, timeframe, ct).ConfigureAwait(false);
        if (toEt < fromEt) return Array.Empty<Candle>();
        symbol = symbol.Trim().ToUpperInvariant();

        if (feed is not null)
        {
            var todayEt = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, MarketTime.Eastern));
            var covered = CoveredDays(symbol, fromEt, toEt);
            var missing = MarketCalendar.TradingDays(fromEt, toEt).Where(d => d < todayEt && !covered.Contains(d)).ToList();
            foreach (var run in Runs(missing))
            {
                var (startUtc, endUtc) = SessionWindow(run[0], run[^1]);
                var fetched = new List<Candle>();
                await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, startUtc, endUtc, TimeSpan.FromMinutes(1), ct).ConfigureAwait(false))
                    fetched.Add(c);
                var byDay = fetched.GroupBy(c => SessionDate(c.StartUtc)).ToDictionary(g => g.Key, g => (IReadOnlyList<Candle>)g.ToList());
                AppendDays(symbol, run.Select(d => (d, byDay.TryGetValue(d, out var list) ? list : Array.Empty<Candle>())));
            }
        }
        return ReadDays(symbol, fromEt, toEt);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Candle>>> GetBarsForSymbolsAsync(
        IEnumerable<string> symbols, DateOnly fromEt, DateOnly toEt, BarTimeframe timeframe, int maxConcurrency = 4, CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyList<Candle>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct())
            result[s] = await GetBarsAsync(s, fromEt, toEt, timeframe, ct).ConfigureAwait(false);
        return result;
    }

    public Task<IReadOnlyList<UniverseMember>> GetPointInTimeUniverseAsync(
        DateOnly asOfEt, IEnumerable<string> candidates, UniverseFilter? filter = null, CancellationToken ct = default) =>
        daily?.GetPointInTimeUniverseAsync(asOfEt, candidates, filter, ct)
        ?? throw new NotSupportedException("The file-backed store holds minute bars only; give it an inner store for the universe.");

    // ── Format ───────────────────────────────────────────────────────────

    private readonly record struct DayEntry(long Offset, int Count, byte Feed);

    private static FileStream Open(string path, FileAccess access) =>
        new(path, access == FileAccess.Read ? FileMode.Open : FileMode.OpenOrCreate, access, FileShare.Read, 1 << 16);

    /// <summary>Scans the day blocks; a truncated trailing block is ignored, and cut off when <paramref name="repair"/> is set.</summary>
    private static Dictionary<int, DayEntry> ReadIndex(FileStream stream, bool repair = false)
    {
        var index = new Dictionary<int, DayEntry>();
        Span<byte> header = stackalloc byte[HeaderSize];
        if (stream.Length < HeaderSize)
        {
            if (!repair) return index;
            stream.SetLength(0);
            BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], RecordSize);
            BinaryPrimitives.WriteInt32LittleEndian(header[12..], 0);
            stream.Write(header);
            return index;
        }
        stream.Seek(0, SeekOrigin.Begin);
        stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic)
            throw new InvalidDataException($"{stream.Name} is not a gapper-study bar file.");
        var version = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (version != FormatVersion)
            throw new InvalidDataException($"{stream.Name} is format version {version}; this build reads {FormatVersion}.");

        long pos = HeaderSize;
        Span<byte> dayHeader = stackalloc byte[DayHeaderSize];
        while (pos + DayHeaderSize <= stream.Length)
        {
            stream.Seek(pos, SeekOrigin.Begin);
            stream.ReadExactly(dayHeader);
            var day = BinaryPrimitives.ReadInt32LittleEndian(dayHeader);
            var count = BinaryPrimitives.ReadInt32LittleEndian(dayHeader[4..]);
            var feedCode = dayHeader[8];
            long dataStart = pos + DayHeaderSize;
            long next = dataStart + (long)count * RecordSize;
            if (count < 0 || next > stream.Length) break;
            index[day] = new DayEntry(dataStart, count, feedCode);
            pos = next;
        }
        if (repair && pos < stream.Length) stream.SetLength(pos);
        return index;
    }

    private static void WriteDay(FileStream stream, DateOnly dateEt, IReadOnlyList<Candle> bars)
    {
        var ordered = bars.OrderBy(b => b.StartUtc).DistinctBy(b => b.StartUtc).ToList();
        var feedCode = FeedCode(ordered);
        Span<byte> dayHeader = stackalloc byte[DayHeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(dayHeader, dateEt.DayNumber);
        BinaryPrimitives.WriteInt32LittleEndian(dayHeader[4..], ordered.Count);
        dayHeader[8] = feedCode;
        dayHeader[9] = dayHeader[10] = dayHeader[11] = 0;
        stream.Write(dayHeader);
        Span<byte> record = stackalloc byte[RecordSize];
        foreach (var bar in ordered)
        {
            record.Clear();
            BinaryPrimitives.WriteInt64LittleEndian(record, bar.StartUtc.Ticks);
            BinaryPrimitives.WriteInt64LittleEndian(record[8..], Scale(bar.Open, PriceScale));
            BinaryPrimitives.WriteInt64LittleEndian(record[16..], Scale(bar.High, PriceScale));
            BinaryPrimitives.WriteInt64LittleEndian(record[24..], Scale(bar.Low, PriceScale));
            BinaryPrimitives.WriteInt64LittleEndian(record[32..], Scale(bar.Close, PriceScale));
            BinaryPrimitives.WriteInt64LittleEndian(record[40..], Scale(bar.Volume, VolumeScale));
            var detailed = bar as DetailedCandle;
            BinaryPrimitives.WriteInt32LittleEndian(record[48..], (int)Math.Min(int.MaxValue, detailed?.TradeCount ?? 0));
            BinaryPrimitives.WriteInt64LittleEndian(record[52..], Scale(detailed?.Vwap ?? 0, PriceScale));
            stream.Write(record);
        }
    }

    private static Candle ReadRecord(ReadOnlySpan<byte> r, string symbol, string note)
    {
        var start = new DateTime(BinaryPrimitives.ReadInt64LittleEndian(r), DateTimeKind.Utc);
        return new DetailedCandle
        {
            Symbol = symbol,
            StartUtc = start,
            EndUtc = start.AddMinutes(1),
            Open = Unscale(BinaryPrimitives.ReadInt64LittleEndian(r[8..]), PriceScale),
            High = Unscale(BinaryPrimitives.ReadInt64LittleEndian(r[16..]), PriceScale),
            Low = Unscale(BinaryPrimitives.ReadInt64LittleEndian(r[24..]), PriceScale),
            Close = Unscale(BinaryPrimitives.ReadInt64LittleEndian(r[32..]), PriceScale),
            Volume = Unscale(BinaryPrimitives.ReadInt64LittleEndian(r[40..]), VolumeScale),
            TradeCount = BinaryPrimitives.ReadInt32LittleEndian(r[48..]),
            Vwap = Unscale(BinaryPrimitives.ReadInt64LittleEndian(r[52..]), PriceScale),
            Note = note,
        };
    }

    private static long Scale(decimal value, long scale) => (long)Math.Round(value * scale, MidpointRounding.AwayFromZero);
    private static decimal Unscale(long value, long scale) => (decimal)value / scale;

    private static byte FeedCode(IReadOnlyList<Candle> bars)
    {
        if (bars.Count == 0) return 0;
        var note = bars[0].Note ?? "";
        if (note.EndsWith(":sip", StringComparison.OrdinalIgnoreCase)) return 1;
        if (note.EndsWith(":iex", StringComparison.OrdinalIgnoreCase)) return 2;
        if (note.StartsWith("Mock", StringComparison.OrdinalIgnoreCase)) return 3;
        return 0;
    }

    private IEnumerable<string> MonthFiles(string symbol, DateOnly from, DateOnly to)
    {
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            var path = FilePath(root, symbol, m);
            if (File.Exists(path)) yield return path;
        }
    }

    private static IEnumerable<List<DateOnly>> Runs(List<DateOnly> days)
    {
        var run = new List<DateOnly>();
        foreach (var d in days)
        {
            if (run.Count > 0 && (MarketCalendar.NextTradingDay(run[^1]) != d || run.Count >= 30))
            {
                yield return run;
                run = [];
            }
            run.Add(d);
        }
        if (run.Count > 0) yield return run;
    }

    public static (DateTime StartUtc, DateTime EndUtc) SessionWindow(DateOnly first, DateOnly last) =>
        (EtToUtc(first, new TimeOnly(4, 0)), EtToUtc(last, new TimeOnly(20, 0)));

    public static DateTime EtToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time, DateTimeKind.Unspecified), MarketTime.Eastern);

    public static DateOnly SessionDate(DateTime startUtc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), MarketTime.Eastern));
}
