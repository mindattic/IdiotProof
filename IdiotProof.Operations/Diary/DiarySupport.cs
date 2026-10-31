using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Diary;

/// <summary>The filters every diary operation shares: when, which account kind, which symbol / strategy / status.</summary>
public record DiaryFilterArgs
{
    [Option("since", Description = "First ET date of entry, yyyy-MM-dd (default: everything).")]
    public DateOnly? Since { get; init; }

    [Option("until", Description = "Last ET date of entry, yyyy-MM-dd (default: today).")]
    public DateOnly? Until { get; init; }

    [Option("mode", Description = "Only trades in this mode: sandbox, paper or live.")]
    public BrokerMode? Mode { get; init; }

    [Option("symbol", Description = "Only this symbol.")]
    public string? Symbol { get; init; }

    [Option("strategy", Description = "Only this strategy: an id prefix or part of its title at trade time.")]
    public string? Strategy { get; init; }
}

/// <summary>One diary trade as the diary operations show it (the full row, flattened, with derived mode and hold time).</summary>
public sealed record DiaryRow(
    Guid Id, string Short, Guid StrategyId, string Strategy, string Symbol, string Direction, string Mode, string Broker,
    DateTime EntryUtc, decimal EntryPrice, int Quantity, decimal? Notional, DateTime? ExitUtc, decimal? ExitPrice,
    string? ExitReason, decimal? RealizedPnL, decimal? ReturnPercent, string Status, decimal? StopLossPrice,
    decimal? StopLossPercent, decimal? TrailingStopPercent, decimal? TakeProfitPrice, decimal? InitialRisk,
    decimal? RMultiple, double? HoldMinutes, string? EntryOrderId, string? ExitOrderId);

/// <summary>Loading, filtering and rendering the owner's trade diary.</summary>
public static class DiarySupport
{
    /// <summary>Sandbox (IdiotProof's simulator), Paper (Alpaca paper) or Live (real money), from the row's own record.</summary>
    public static BrokerMode ModeOf(TradeDiaryEntry t) =>
        t.Broker.Equals("Sandbox", StringComparison.OrdinalIgnoreCase) ? BrokerMode.Sandbox
        : t.IsPaper ? BrokerMode.Paper : BrokerMode.Live;

    /// <summary>The owner's diary rows matching <paramref name="f"/>, newest entry first.</summary>
    public static async Task<List<TradeDiaryEntry>> LoadAsync(OperationContext ctx, DiaryFilterArgs f, string? status = null, int take = 50_000)
    {
        var owner = ctx.RequireOwner();
        if (f.Since is { } s && f.Until is { } u && u < s) throw OperationException.Usage("--until is before --since.");

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var q = db.TradeDiary.AsNoTracking().Where(t => t.OwnerUserId == owner);
        if (f.Since is { } since) { var from = Account.AccountApis.EtMidnightUtc(since); q = q.Where(t => t.EntryUtc >= from); }
        if (f.Until is { } until) { var to = Account.AccountApis.EtMidnightUtc(until.AddDays(1)); q = q.Where(t => t.EntryUtc < to); }
        if (!string.IsNullOrWhiteSpace(f.Symbol)) { var sym = f.Symbol.Trim().ToUpperInvariant(); q = q.Where(t => t.Symbol == sym); }
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
        q = f.Mode switch
        {
            BrokerMode.Sandbox => q.Where(t => t.Broker == "Sandbox"),
            BrokerMode.Paper => q.Where(t => t.Broker != "Sandbox" && t.IsPaper),
            BrokerMode.Live => q.Where(t => t.Broker != "Sandbox" && !t.IsPaper),
            _ => q,
        };
        var rows = await q.OrderByDescending(t => t.EntryUtc).Take(take).ToListAsync(ctx.CancellationToken);

        if (!string.IsNullOrWhiteSpace(f.Strategy))
        {
            var key = f.Strategy.Trim();
            var compact = key.Replace("-", "");
            rows = rows.Where(t =>
                    (compact.Length >= 4 && t.StrategyId.ToString("N").StartsWith(compact, StringComparison.OrdinalIgnoreCase))
                    || t.StrategyTitle.Contains(key, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        return rows;
    }

    /// <summary>Finds one of the owner's diary trades by full id or an id prefix of at least 4 hex digits.</summary>
    public static async Task<TradeDiaryEntry> ResolveAsync(OperationContext ctx, string reference)
    {
        var owner = ctx.RequireOwner();
        var r = (reference ?? "").Trim();
        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        if (Guid.TryParse(r, out var id))
            return await db.TradeDiary.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.OwnerUserId == owner, ctx.CancellationToken)
                ?? throw OperationException.Usage($"No diary trade of yours has id {id}.");

        var compact = r.Replace("-", "");
        if (compact.Length < 4 || !compact.All(Uri.IsHexDigit))
            throw OperationException.Usage("Name a trade by its id or the 8-character prefix `idiotproof diary list` shows.");
        var mine = await db.TradeDiary.AsNoTracking().Where(t => t.OwnerUserId == owner)
            .Select(t => new { t.Id, t.Symbol, t.EntryUtc }).ToListAsync(ctx.CancellationToken);
        var matches = mine.Where(t => t.Id.ToString("N").StartsWith(compact, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => await db.TradeDiary.AsNoTracking().FirstAsync(t => t.Id == matches[0].Id, ctx.CancellationToken),
            0 => throw OperationException.Usage($"No diary trade of yours starts with '{r}'. `idiotproof diary list` shows them."),
            _ => throw OperationException.Usage($"'{r}' matches {matches.Count} trades — use more of the id:\n" +
                string.Join("\n", matches.Take(10).Select(m => $"  {m.Id}  {m.Symbol,-6} {Fmt.Et(m.EntryUtc)}"))),
        };
    }

    /// <summary>Dollars at risk at entry: the stop distance times the quantity, when a stop was set.</summary>
    public static decimal? InitialRisk(TradeDiaryEntry t)
    {
        var stop = t.StopLossPrice
            ?? (t.StopLossPercent is { } pct
                ? t.Direction == "Short" ? t.EntryPrice * (1 + pct / 100m) : t.EntryPrice * (1 - pct / 100m)
                : null);
        if (stop is not { } sp || t.Quantity <= 0) return null;
        var perShare = Math.Abs(t.EntryPrice - sp);
        return perShare == 0 ? null : perShare * t.Quantity;
    }

    public static DiaryRow ToRow(TradeDiaryEntry t)
    {
        var risk = InitialRisk(t);
        return new DiaryRow(
            t.Id, t.Id.ToString("N")[..8], t.StrategyId, t.StrategyTitle, t.Symbol, t.Direction, ModeOf(t).ToString().ToLowerInvariant(),
            t.Broker, t.EntryUtc, t.EntryPrice, t.Quantity, t.Notional, t.ExitUtc, t.ExitPrice, t.ExitReason, t.RealizedPnL,
            t.ReturnPercent, t.Status, t.StopLossPrice, t.StopLossPercent, t.TrailingStopPercent, t.TakeProfitPrice,
            risk is { } r ? Math.Round(r, 2) : null,
            risk is { } rr && t.RealizedPnL is { } pnl && t.Status == TradeDiaryStatus.Closed ? Math.Round(pnl / rr, 2) : null,
            t.ExitUtc is { } exit ? Math.Round((exit - t.EntryUtc).TotalMinutes, 1) : null,
            t.EntryOrderId, t.ExitOrderId);
    }

    /// <summary>"12m", "3h 05m", "2d 4h".</summary>
    public static string Hold(double? minutes)
    {
        if (minutes is not { } m) return "";
        var span = TimeSpan.FromMinutes(m);
        return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m"
            : $"{(int)span.TotalMinutes}m";
    }

    /// <summary>The filters as one line for headings.</summary>
    public static string Describe(DiaryFilterArgs f)
    {
        var parts = new List<string>();
        if (f.Since is { } s) parts.Add($"since {s:yyyy-MM-dd}");
        if (f.Until is { } u) parts.Add($"until {u:yyyy-MM-dd}");
        if (f.Mode is { } m) parts.Add(m.ToString().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(f.Symbol)) parts.Add(f.Symbol.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(f.Strategy)) parts.Add($"strategy '{f.Strategy.Trim()}'");
        return parts.Count == 0 ? "all trades" : string.Join(", ", parts);
    }
}
