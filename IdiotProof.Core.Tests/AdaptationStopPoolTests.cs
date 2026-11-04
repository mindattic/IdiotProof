using IdiotProof.Core.Adaptation;
using IdiotProof.Evidence.Liquidity;
using IdiotProof.Models;

namespace IdiotProof.Core.Tests;

/// <summary>The adaptation stop-pool hook (IP-T404): learns raids from prior sessions only, offers only pools below.</summary>
public class AdaptationStopPoolTests
{
    private static readonly DateTime Mon = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    private static List<Candle> Bars(DateTime openUtc, IEnumerable<(double H, double L, double C)> bars) =>
        bars.Select((b, i) => new Candle
        {
            Symbol = "X", StartUtc = openUtc.AddMinutes(i), EndUtc = openUtc.AddMinutes(i + 1),
            Open = (decimal)b.C, Close = (decimal)b.C, High = (decimal)b.H, Low = (decimal)b.L, Volume = 1000,
        }).ToList();

    private static IEnumerable<(double, double, double)> Flat(int n, double c) => Enumerable.Repeat((c + 0.005, c - 0.005, c), n);

    private static IEnumerable<(double, double, double)> RaidBelow() =>
        Flat(12, 3.05).Append((3.05, 2.97, 3.02)).Concat(Flat(12, 3.04));

    [Test]
    public void StopPoolNudgeFor_LearnsPriorSessionRaids_AndOffersOnlyPoolsBelow()
    {
        var prior = Bars(Mon, Enumerable.Range(0, 10).SelectMany(_ => RaidBelow()));
        var today = Bars(Mon.AddDays(1), Flat(30, 3.05));

        var nudge = AdaptationService.StopPoolNudgeFor(prior, today);
        var key = new PoolKey(PoolKind.RoundNumber, PoolSide.Below, nudge.Tier);
        Assert.That(nudge.Table.ReverseRate(key).Samples, Is.GreaterThanOrEqualTo(10));
        Assert.That(nudge.PoolsBelow, Is.Not.Empty);
        Assert.That(nudge.PoolsBelow.All(p => p.Price < 3.05), Is.True);
    }

    [Test]
    public void StopPoolNudgeFor_IgnoresTodaysOwnRaids()
    {
        var today = Bars(Mon, Enumerable.Range(0, 10).SelectMany(_ => RaidBelow()));
        var nudge = AdaptationService.StopPoolNudgeFor([], today);
        Assert.That(nudge.Table.Approaches, Is.EqualTo(0), "today's approaches never grade today");
    }
}
