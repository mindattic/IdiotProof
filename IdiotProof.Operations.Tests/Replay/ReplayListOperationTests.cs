using IdiotProof.Blazor.Data;
using IdiotProof.Operations.Replay;

namespace IdiotProof.Operations.Tests.Replay;

/// <summary>replay list (IP-T179): the archive newest first, filtered by symbol, day, kind and fired.</summary>
public class ReplayListOperationTests
{
    internal static async Task<ReplayRun> SeedRunAsync(ReplayWorld w, string symbol, string day, bool fired, double pnl, string kind = "sim", int minutesAgo = 0)
    {
        var run = new ReplayRun
        {
            Symbol = symbol, Strategy = $"{symbol} strat", DateEt = day, Feed = "SIP", Kind = kind, Stamp = $"{day}T09.00.00",
            GeneratedUtc = new DateTime(2026, 10, 30, 14, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo), GeneratedEt = day,
            Fired = fired, PayoffCount = fired ? 1 : 0, TotalPnl = pnl, FirstFireEt = fired ? "04:31" : null, EntryPrice = fired ? 10 : null,
        };
        await using var db = await w.Db.CreateDbContextAsync();
        db.ReplayRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    [Test]
    public async Task ListsNewestFirst_AndFilters()
    {
        using var w = new ReplayWorld();
        await SeedRunAsync(w, "NVDA", "2026-10-20", fired: true, pnl: 3.5, minutesAgo: 30);
        await SeedRunAsync(w, "NVDA", "2026-10-29", fired: false, pnl: 0, minutesAgo: 10);
        await SeedRunAsync(w, "TSLA", "2026-10-29", fired: true, pnl: -1.25, kind: "live", minutesAgo: 5);

        var all = await new ReplayListOperation().ExecuteAsync(new ReplayListArgs(), w.Context());
        var nvdaSince = await new ReplayListOperation().ExecuteAsync(new ReplayListArgs { Symbol = "nvda", Since = new DateOnly(2026, 10, 25) }, w.Context());
        var live = await new ReplayListOperation().ExecuteAsync(new ReplayListArgs { Kind = "live" }, w.Context());
        var fired = await new ReplayListOperation().ExecuteAsync(new ReplayListArgs { Fired = true }, w.Context());

        Assert.That(all.IsOk, Is.True, all.Message);
        Assert.Multiple(() =>
        {
            Assert.That(all.Human!.IndexOf("TSLA"), Is.LessThan(all.Human.IndexOf("2026-10-20")), "newest first");
            Assert.That(all.Human, Does.Contain("LIVE").And.Contain("-1.25%").And.Contain("3 of 3 run(s)"));
            Assert.That(nvdaSince.Human, Does.Contain("2026-10-29").And.Not.Contain("2026-10-20").And.Not.Contain("TSLA"));
            Assert.That(live.Human, Does.Contain("TSLA").And.Not.Contain("NVDA"));
            Assert.That(fired.Human, Does.Contain("2 of 2"));
        });
    }

    [Test]
    public async Task ABadKind_IsAUsageError()
    {
        using var w = new ReplayWorld();
        var r = await new ReplayListOperation().ExecuteAsync(new ReplayListArgs { Kind = "both" }, w.Context());
        Assert.That(r.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
