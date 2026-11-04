using IdiotProof.Core.Evidence;
using IdiotProof.Models;
using IdiotProof.Operations.Evidence;
using Microsoft.Extensions.DependencyInjection;


namespace IdiotProof.Operations.Tests.Evidence;

internal sealed class FakeBarStore : IHistoricalBarStore
{
    private static readonly TimeZoneInfo Et = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    public Dictionary<string, List<Candle>> Minute { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<Candle>> Daily { get; } = new(StringComparer.Ordinal);
    public Dictionary<DateOnly, List<UniverseMember>> Universe { get; } = new();
    public List<(DateOnly AsOf, List<string> Candidates)> UniverseCalls { get; } = new();

    public Task<IReadOnlyList<Candle>> GetBarsAsync(string symbol, DateOnly fromEt, DateOnly toEt, BarTimeframe timeframe, CancellationToken ct = default)
    {
        var src = timeframe == BarTimeframe.Day1 ? Daily : Minute;
        IReadOnlyList<Candle> bars = src.TryGetValue(symbol, out var list)
            ? list.Where(c => { var d = DateOf(c); return d >= fromEt && d <= toEt; }).ToList()
            : [];
        return Task.FromResult(bars);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Candle>>> GetBarsForSymbolsAsync(IEnumerable<string> symbols, DateOnly fromEt, DateOnly toEt,
        BarTimeframe timeframe, int maxConcurrency = 4, CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyList<Candle>>(StringComparer.Ordinal);
        foreach (var s in symbols) result[s] = await GetBarsAsync(s, fromEt, toEt, timeframe, ct);
        return result;
    }

    public Task<IReadOnlyList<UniverseMember>> GetPointInTimeUniverseAsync(DateOnly asOfEt, IEnumerable<string> candidates, UniverseFilter? filter = null, CancellationToken ct = default)
    {
        var list = candidates.ToList();
        UniverseCalls.Add((asOfEt, list));
        IReadOnlyList<UniverseMember> members = Universe.TryGetValue(asOfEt, out var m) ? m.Where(x => list.Contains(x.Symbol)).ToList() : [];
        return Task.FromResult(members);
    }

    public static DateOnly DateOf(Candle c) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(c.StartUtc, Et));
}

internal sealed class FakeCandidates(params CandidateAsset[] assets) : IEvidenceCandidateSource
{
    public Task<IReadOnlyList<CandidateAsset>> GetCandidatesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<CandidateAsset>>(assets);
}

internal static class EvidenceTestKit
{
    /// <summary>Weekdays starting Monday 2026-11-02 (EST, so 9:30 ET = 14:30 UTC).</summary>
    public static List<DateOnly> Weekdays(int count)
    {
        var days = new List<DateOnly>();
        for (var d = new DateOnly(2026, 11, 2); days.Count < count; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days.Add(d);
        return days;
    }

    /// <summary>One RTH session of 1-minute bars whose closes follow <paramref name="closeAt"/>(minute index).</summary>
    public static IEnumerable<Candle> Session(string symbol, DateOnly day, Func<int, double> closeAt, int minutes = 390)
    {
        var open = new DateTime(day.Year, day.Month, day.Day, 14, 30, 0, DateTimeKind.Utc);
        double prev = closeAt(0);
        for (int i = 0; i < minutes; i++)
        {
            double c = closeAt(i);
            decimal o = (decimal)prev, cl = (decimal)c;
            yield return new Candle
            {
                Symbol = symbol, StartUtc = open.AddMinutes(i), EndUtc = open.AddMinutes(i + 1),
                Open = o, Close = cl, High = Math.Max(o, cl), Low = Math.Min(o, cl), Volume = 1000,
            };
            prev = c;
        }
    }

    /// <summary>8 bars up 1c then 2 bars down 3c, repeating: after an up-leg the next bars always fall.</summary>
    public static double Sawtooth(int i)
    {
        int cycle = i / 10, pos = i % 10;
        double baseP = 3.0 + cycle * 0.02;
        return pos < 8 ? baseP + (pos + 1) * 0.01 : baseP + 0.08 - (pos - 7) * 0.03;
    }

    public static Func<int, double> RandomWalk(int seed)
    {
        var rng = new Random(seed);
        var path = new double[400];
        path[0] = 3.0;
        for (int i = 1; i < path.Length; i++) path[i] = Math.Max(0.5, path[i - 1] * (1 + 0.004 * (rng.NextDouble() * 2 - 1)));
        return i => path[i];
    }

    public static OperationContext Context(FakeBarStore store, IEvidenceCandidateSource? candidates = null, DateTimeOffset? now = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHistoricalBarStore>(store);
        if (candidates is not null) services.AddSingleton(candidates);
        return new OperationContext
        {
            Services = services.BuildServiceProvider(),
            Clock = new Fixtures.FixedClock(now ?? new DateTimeOffset(2026, 12, 1, 15, 0, 0, TimeSpan.Zero)),
        };
    }
}
