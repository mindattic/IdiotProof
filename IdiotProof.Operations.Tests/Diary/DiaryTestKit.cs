using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Diary;

/// <summary>An owner and an EF InMemory diary / audit / ledger / live-bar store. No broker, no network.</summary>
internal sealed class DiaryKit
{
    public Guid Owner { get; } = Guid.NewGuid();

    public IDbContextFactory<AppDbContext> Db { get; }

    private readonly IServiceProvider services;

    public DiaryKit()
    {
        services = new ServiceCollection()
            .AddLogging()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"diary-{Guid.NewGuid():N}"))
            .BuildServiceProvider();
        Db = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    public OperationContext Context(Guid? owner = null, TimeProvider? clock = null) => new()
    {
        Services = services,
        OwnerId = owner ?? Owner,
        Caller = OperationCaller.Cli,
        Clock = clock ?? TimeProvider.System,
    };

    /// <summary>A closed (or open, when <paramref name="exit"/> is null) trade entered at <paramref name="entryUtc"/>.</summary>
    public async Task<TradeDiaryEntry> TradeAsync(
        string symbol, DateTime entryUtc, decimal entry, decimal? exit, int qty = 10, string broker = "Alpaca", bool paper = true,
        string title = "Gap and go", Guid? strategyId = null, decimal? stop = null, Guid? owner = null, double holdMinutes = 30,
        string direction = "Long")
    {
        var t = new TradeDiaryEntry
        {
            StrategyId = strategyId ?? Guid.NewGuid(), OwnerUserId = owner ?? Owner, StrategyTitle = title, Symbol = symbol,
            Direction = direction, Broker = broker, IsPaper = paper, EntryUtc = entryUtc, EntryPrice = entry, Quantity = qty,
            StopLossPrice = stop, Status = exit is null ? TradeDiaryStatus.Open : TradeDiaryStatus.Closed,
        };
        if (exit is { } x)
        {
            t.ExitUtc = entryUtc.AddMinutes(holdMinutes);
            t.ExitPrice = x;
            t.ExitReason = x >= entry ? "TargetHit" : "StopLoss";
            t.RealizedPnL = (direction == "Short" ? entry - x : x - entry) * qty;
            t.ReturnPercent = Math.Round(t.RealizedPnL.Value / (entry * qty) * 100m, 4);
        }
        await AddAsync(t);
        return t;
    }

    public async Task AddAsync(params object[] rows)
    {
        await using var db = await Db.CreateDbContextAsync();
        foreach (var r in rows) db.Add(r);
        await db.SaveChangesAsync();
    }

    /// <summary>A UTC instant for an ET wall-clock time (handles DST).</summary>
    public static DateTime Et(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, 0), IdiotProof.Scripting.MarketTime.Eastern);
}
