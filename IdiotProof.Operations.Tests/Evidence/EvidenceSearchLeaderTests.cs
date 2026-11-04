using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IdiotProof.Core.Evidence;
using IdiotProof.Engine.Settings;
using IdiotProof.Models;
using IdiotProof.Operations.Evidence;
using Microsoft.Extensions.DependencyInjection;
using static IdiotProof.Operations.Tests.Evidence.EvidenceTestKit;

namespace IdiotProof.Operations.Tests.Evidence;

/// <summary>
/// evidence search scores leader-gated templates with prior-session leaders (IP-T398): without leader
/// context LeaderConfirms blocks every config, so the search would rank configs that can never trade.
/// </summary>
public class EvidenceSearchLeaderTests
{
    private const string LeaderTemplate = "Ticker(\"X\").LeaderConfirms(1, {inf}).Long().TakeProfitPercent(0.5).StopLossPercent(0.5).Repeat()";
    private const string PlainTemplate = "Ticker(\"X\").IsAboveVwap().Long().TakeProfitPercent({tp}).StopLossPercent(0.5).Repeat()";

    private sealed class RecordingStore(IHistoricalBarStore inner) : IHistoricalBarStore
    {
        public HashSet<string> Requested { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<Candle>> GetBarsAsync(string symbol, DateOnly fromEt, DateOnly toEt, BarTimeframe timeframe, CancellationToken ct = default)
        {
            Requested.Add(symbol);
            return inner.GetBarsAsync(symbol, fromEt, toEt, timeframe, ct);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<Candle>>> GetBarsForSymbolsAsync(IEnumerable<string> symbols, DateOnly fromEt, DateOnly toEt,
            BarTimeframe timeframe, int maxConcurrency = 4, CancellationToken ct = default)
        {
            var list = symbols.ToList();
            foreach (var s in list) Requested.Add(s);
            return inner.GetBarsForSymbolsAsync(list, fromEt, toEt, timeframe, maxConcurrency, ct);
        }

        public Task<IReadOnlyList<UniverseMember>> GetPointInTimeUniverseAsync(DateOnly asOfEt, IEnumerable<string> candidates, UniverseFilter? filter = null, CancellationToken ct = default) =>
            inner.GetPointInTimeUniverseAsync(asOfEt, candidates, filter, ct);
    }

    /// <summary>FOLL repeats LEAD's previous-minute move every day, so prior sessions always reveal LEAD as its leader.</summary>
    private static FakeBarStore CoupledStore(IReadOnlyList<DateOnly> days, bool includeLeader)
    {
        var store = new FakeBarStore();
        store.Minute["FOLL"] = [];
        if (includeLeader) store.Minute["LEAD"] = [];
        for (int d = 0; d < days.Count; d++)
        {
            var rng = new Random(300 + d);
            var lead = new double[400];
            lead[0] = 50;
            for (int i = 1; i < lead.Length; i++) lead[i] = lead[i - 1] * (1 + 0.004 * (rng.NextDouble() * 2 - 1));
            var foll = new double[400];
            foll[0] = 2.5;
            for (int i = 1; i < foll.Length; i++)
            {
                double leadRet = lead[i - 1] / (i >= 2 ? lead[i - 2] : lead[0]) - 1;
                foll[i] = foll[i - 1] * (1 + 0.9 * leadRet + 0.0002 * (rng.NextDouble() - 0.5));
            }
            if (includeLeader) store.Minute["LEAD"].AddRange(Session("LEAD", days[d], i => lead[i]));
            store.Minute["FOLL"].AddRange(Session("FOLL", days[d], i => foll[i]));
        }
        return store;
    }

    private static OperationContext ContextWithLeaders(IHistoricalBarStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(new AppSettings { LeaderEtfs = "LEAD" });
        return new OperationContext
        {
            Services = services.BuildServiceProvider(),
            Clock = new Fixtures.FixedClock(new DateTimeOffset(2026, 12, 1, 15, 0, 0, TimeSpan.Zero)),
        };
    }

    private static readonly JsonSerializerOptions Json = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    private static int TotalTrades(OperationResult result) =>
        Regex.Matches(JsonSerializer.Serialize(result.Data, Json), "\"Trades\":(\\d+)").Sum(m => int.Parse(m.Groups[1].Value));

    private static EvidenceSearchArgs Args(IReadOnlyList<DateOnly> days, string script, string grid) => new()
    {
        Symbols = ["FOLL"], Script = script, Grid = [grid], From = days[0], To = days[^1], Holdout = 0, Resamples = 200,
    };

    [Test]
    public async Task LeaderGatedTemplate_TradesWhenItsMeasuredLeaderIsInTheBarStore()
    {
        var days = Weekdays(8);
        var withLeader = await new EvidenceSearchOperation().ExecuteAsync(Args(days, LeaderTemplate, "inf=0.1,0.2"),
            ContextWithLeaders(CoupledStore(days, includeLeader: true)));
        var withoutLeader = await new EvidenceSearchOperation().ExecuteAsync(Args(days, LeaderTemplate, "inf=0.1,0.2"),
            ContextWithLeaders(CoupledStore(days, includeLeader: false)));

        Assert.That(withLeader.IsOk, Is.True, withLeader.Message);
        Assert.That(withoutLeader.IsOk, Is.True, withoutLeader.Message);
        Assert.Multiple(() =>
        {
            Assert.That(TotalTrades(withLeader), Is.GreaterThan(0), withLeader.Human);
            Assert.That(TotalTrades(withoutLeader), Is.EqualTo(0), "no measurable leader keeps the guard fail-closed");
        });
    }

    [Test]
    public async Task TemplateWithoutLeaderGuards_FetchesOnlyTheSearchedSymbols()
    {
        var days = Weekdays(6);
        var store = new RecordingStore(CoupledStore(days, includeLeader: true));

        var result = await new EvidenceSearchOperation().ExecuteAsync(Args(days, PlainTemplate, "tp=0.5,1"), ContextWithLeaders(store));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(store.Requested, Is.EquivalentTo(new[] { "FOLL" }));
    }
}
