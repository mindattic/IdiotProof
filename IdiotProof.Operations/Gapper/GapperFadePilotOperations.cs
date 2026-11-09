using System.Globalization;
using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Monitoring.FadeShort;
using IdiotProof.Core.Services;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Gapper;

/// <summary>Shared pieces of the <c>gapper fade-pilot</c> operations (IP-T480).</summary>
internal static class FadePilotViews
{
    public static string YesNo(bool? v) => v is { } b ? (b ? "yes" : "no") : "—";

    public static string Pct(double? v, int decimals = 1) =>
        v is { } d ? d.ToString("+0." + new string('0', decimals) + ";-0." + new string('0', decimals), CultureInfo.InvariantCulture) + "%" : "—";

    public static string Fee(decimal? v) => v is { } f ? "$" + f.ToString("0.0000", CultureInfo.InvariantCulture) : "—";

    public static string Size(double? v) => v is { } s ? s.ToString("#,0", CultureInfo.InvariantCulture) : "—";

    public static string Time(DateTime? utc) => utc is { } u ? EasternClock.ToEastern(u).ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "—";

    public static object Data(FadeShortBorrowObservation r) => new
    {
        r.DateEt, r.Symbol, r.CandidateSource,
        Checked0940Et = r.Checked0940Utc is { } a ? EasternClock.ToEasternOffset(a) : (DateTimeOffset?)null,
        Checked0946Et = r.Checked0946Utc is { } b ? EasternClock.ToEasternOffset(b) : (DateTimeOffset?)null,
        r.Checked0940Utc, r.Checked0946Utc, r.SignalEvaluatedUtc,
        r.PrevClose, r.PmHigh, r.PmHighPct, r.PmHigh100, r.Open0930, r.Close0945, r.GapAt0945, r.ExtensionAt0945, r.SignalFired,
        r.Shortable0940, r.EasyToBorrow0940, r.Marginable0940, r.Status0940,
        r.Shortable, r.EasyToBorrow, r.Marginable, r.Status, r.BorrowStatus,
        r.LocateQueried, r.LocateAvailable0940, r.LocateSize0940, r.LocateFee0940, r.LocateAvailable, r.LocateSize, r.LocateFee, r.LocateNote,
        r.SsrD1LowVsD2Close, r.SsrActive, r.RegSho, r.Note, r.RawJson, r.CreatedUtc, r.UpdatedUtc,
    };
}

[Operation("gapper fade-pilot status", Summary = "Whether the no-order fade-short borrow pilot is enabled, its owner, broker mode, whether this Hub leads, its schedule and the rows recorded so far.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper fade-pilot status")]
public sealed class GapperFadePilotStatusOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var cfg = await new FadeShortPilotSettings(ctx.Get<SettingsRepository>()).ReadAsync(ctx.CancellationToken);
        var hosted = ctx.Services.GetService<MonitorRuntimeState>();
        bool? leader = hosted is null ? null : await hosted.IsLeaderAsync(ctx.CancellationToken);

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var total = await db.FadeShortBorrowObservations.CountAsync(ctx.CancellationToken);
        var days = await db.FadeShortBorrowObservations.Select(o => o.DateEt).Distinct().CountAsync(ctx.CancellationToken);
        var signals = await db.FadeShortBorrowObservations.CountAsync(o => o.SignalFired, ctx.CancellationToken);
        var lastUtc = await db.FadeShortBorrowObservations.MaxAsync(o => (DateTime?)o.UpdatedUtc, ctx.CancellationToken);

        var block = new KeyValueBlock()
            .Add("Enabled", cfg.Enabled ? "yes" : $"no (`gapper fade-pilot enable`, or set {FadeShortPilotSettings.EnabledKey} = true)")
            .Add("Owner", cfg.OwnerUserId?.ToString() ?? $"unset ({FadeShortPilotSettings.OwnerKey})")
            .Add("Broker mode", cfg.IsPaper ? "paper" : $"'{cfg.BrokerMode}' — NOT paper; the pilot refuses to run")
            .Add("Hosted", hosted is null ? "no (the pilot runs with the Monitor)" : "yes (this Hub hosts the Monitor)")
            .Add("Leader", leader is null ? "—" : leader.Value ? "yes" : "no (standby)")
            .Add("Schedule", "trading days 09:40 ET (candidates, asset, locate quote) and 09:46 ET (09:45 signal, asset, locate quote)")
            .Add("Signal", $"pmHigh ≥ +{FadeShortPilotMath.MinPmHighPct:0}% (and ≥ +{FadeShortPilotMath.PmHigh100Pct:0}% flagged), gap ≥ +{FadeShortPilotMath.MinGapAtDecisionPct:0}% and extension < {FadeShortPilotMath.MaxExtensionPct:0}% at 09:45")
            .Add("Orders", "none: GET-only (asset, advisory locate quote, bars, movers); never a locate request or an order")
            .Add("Feed", cfg.Feed)
            .Add("Max candidates", cfg.MaxCandidates.ToString(CultureInfo.InvariantCulture))
            .Add("Reg SHO dir", cfg.RegShoDir ?? FadeShortBorrowPilotWorker.DefaultRegShoDir())
            .Add("Rows", $"{total} over {days} day(s), {signals} signal(s) fired; last write {Fmt.Et(lastUtc, "never")}");

        return OperationResult.Ok(new
        {
            cfg.Enabled, cfg.OwnerUserId, cfg.BrokerMode, PaperOnly = cfg.IsPaper, Hosted = hosted is not null, Leader = leader,
            cfg.Feed, cfg.MaxCandidates, RegShoDir = cfg.RegShoDir ?? FadeShortBorrowPilotWorker.DefaultRegShoDir(),
            Rows = total, Days = days, Signals = signals, LastWriteUtc = lastUtc,
        }, block.Render());
    }
}

[Operation("gapper fade-pilot enable", Summary = "Turn the no-order fade-short borrow pilot on for you (paper route only; leader-only; 09:40 and 09:46 ET on trading days). It never places, reserves or cancels anything.", Risk = OperationRisk.Write)]
[Example("idiotproof gapper fade-pilot enable")]
public sealed class GapperFadePilotEnableOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var settings = ctx.Get<SettingsRepository>();
        var mode = (await settings.GetAsync(FadeShortPilotSettings.BrokerModeKey, ctx.CancellationToken) ?? "paper").Trim();
        if (!mode.Equals("paper", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Blocked($"{FadeShortPilotSettings.BrokerModeKey} is '{mode}', not paper. The pilot only ever reads through the paper route.");

        var existing = await settings.GetAsync(FadeShortPilotSettings.OwnerKey, ctx.CancellationToken);
        var ownerSet = Guid.TryParse(existing, out var current) ? current : owner;
        await settings.SetAsync(FadeShortPilotSettings.OwnerKey, ownerSet.ToString(), ctx.CancellationToken);
        await settings.SetAsync(FadeShortPilotSettings.EnabledKey, "true", ctx.CancellationToken);

        var note = ownerSet == owner ? "" : $" Owner stays {ownerSet} (already set).";
        return OperationResult.Ok(new { Enabled = true, OwnerUserId = ownerSet },
            $"Fade-short borrow pilot enabled for {ownerSet}.{note} With the Monitor leader on trading days it records candidates at 09:40 ET and the 09:45 signal at 09:46 ET, " +
            "reading assets and advisory locate quotes only (no orders, no locate requests). `gapper fade-pilot list` shows the rows.");
    }
}

[Operation("gapper fade-pilot disable", Summary = "Turn the fade-short borrow pilot off (settings only; it holds nothing to undo).", Risk = OperationRisk.Write)]
[Example("idiotproof gapper fade-pilot disable")]
public sealed class GapperFadePilotDisableOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        ctx.RequireOwner();
        await ctx.Get<SettingsRepository>().SetAsync(FadeShortPilotSettings.EnabledKey, "false", ctx.CancellationToken);
        return OperationResult.Ok(new { Enabled = false }, "Fade-short borrow pilot disabled; the worker skips its next phase.");
    }
}

public sealed record GapperFadePilotListArgs
{
    [Option("since", Description = "First ET date to include, yyyy-MM-dd (default: 30 days ago).")]
    public DateOnly? Since { get; init; }

    [Option("symbol", Description = "Only this ticker.", Short = 's')]
    public string? Symbol { get; init; }

    [Option("signals", Description = "Only rows where the 09:45 signal fired.")]
    public bool Signals { get; init; }

    [Option("last", Description = "Most rows, newest first (default 100, max 1000).")]
    public int Last { get; init; } = 100;
}

[Operation("gapper fade-pilot list", Summary = "Fade-short borrow pilot rows: day, symbol, premarket high, 09:45 gap and extension, signal, shortable/ETB at 09:40 and 09:46, locate quote (size, fee), SSR and Reg SHO; times in ET.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper fade-pilot list")]
[Example("idiotproof gapper fade-pilot list --since 2026-11-10 --signals")]
public sealed class GapperFadePilotListOperation : IOperation<GapperFadePilotListArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperFadePilotListArgs args, OperationContext ctx)
    {
        if (args.Last is < 1 or > 1000) return OperationResult.Usage("--last must be 1-1000.");
        ctx.RequireOwner();
        var since = args.Since ?? DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow)).AddDays(-30);
        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var q = db.FadeShortBorrowObservations.AsNoTracking().Where(o => o.DateEt >= since);
        if (!string.IsNullOrWhiteSpace(args.Symbol))
        {
            var sym = args.Symbol.Trim().ToUpperInvariant();
            q = q.Where(o => o.Symbol == sym);
        }
        if (args.Signals) q = q.Where(o => o.SignalFired);
        var rows = await q.OrderByDescending(o => o.DateEt).ThenByDescending(o => o.PmHighPct).Take(args.Last).ToListAsync(ctx.CancellationToken);

        var data = rows.Select(FadePilotViews.Data).ToList();
        if (rows.Count == 0)
            return OperationResult.Ok(data, $"No fade-short pilot rows since {since:yyyy-MM-dd}. The pilot writes them at 09:40 and 09:46 ET on trading days once `gapper fade-pilot enable` is on and this Hub leads the Monitor.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("day").Column("sym").Column("pmHigh", Align.Right).Column("gap@45", Align.Right).Column("ext@45", Align.Right).Column("sig")
            .Column("09:40 ET").Column("sh/etb").Column("09:46 ET").Column("sh/etb").Column("locate", Align.Right).Column("fee/sh", Align.Right)
            .Column("ssr").Column("regsho").Column("note", maxWidth: 30);
        foreach (var r in rows)
            table.Row(r.DateEt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.Symbol,
                FadePilotViews.Pct(r.PmHighPct, 0) + (r.PmHigh100 ? "*" : ""), FadePilotViews.Pct(r.GapAt0945), FadePilotViews.Pct(r.ExtensionAt0945),
                r.SignalFired ? "FIRE" : r.SignalEvaluatedUtc is null ? "—" : "no",
                FadePilotViews.Time(r.Checked0940Utc), $"{FadePilotViews.YesNo(r.Shortable0940)}/{FadePilotViews.YesNo(r.EasyToBorrow0940)}",
                FadePilotViews.Time(r.Checked0946Utc), $"{FadePilotViews.YesNo(r.Shortable)}/{FadePilotViews.YesNo(r.EasyToBorrow)}",
                r.LocateQueried ? FadePilotViews.Size(r.LocateSize ?? r.LocateSize0940) : "—", FadePilotViews.Fee(r.LocateFee ?? r.LocateFee0940),
                FadePilotViews.YesNo(r.SsrActive), FadePilotViews.YesNo(r.RegSho), r.LocateNote ?? r.Note ?? "");
        return OperationResult.Ok(data, $"Fade-short borrow pilot since {since:yyyy-MM-dd} (newest first; * = pmHigh ≥ +100%; sh/etb = shortable/easy-to-borrow):\n" +
            table.Render() + $"\n{rows.Count} row(s), {rows.Count(r => r.SignalFired)} signal(s).");
    }
}

public sealed record GapperFadePilotSummaryArgs
{
    [Option("since", Description = "First ET date to include, yyyy-MM-dd (default: everything).")]
    public DateOnly? Since { get; init; }
}

[Operation("gapper fade-pilot summary", Summary = "Of the fade-short signals the pilot saw: the share that were shortable, easy-to-borrow and locate-available at 09:46 ET, the +100% tier, and the quoted locate-fee distribution.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper fade-pilot summary")]
[Example("idiotproof gapper fade-pilot summary --since 2026-11-10")]
public sealed class GapperFadePilotSummaryOperation : IOperation<GapperFadePilotSummaryArgs>
{
    public sealed record Tier(string Name, int Signals, int Shortable, int EasyToBorrow, int HtbShortable, int LocateQuoted, int LocateAvailable, int SsrActive, int RegSho);

    public async Task<OperationResult> ExecuteAsync(GapperFadePilotSummaryArgs args, OperationContext ctx)
    {
        ctx.RequireOwner();
        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var q = db.FadeShortBorrowObservations.AsNoTracking();
        if (args.Since is { } since) q = q.Where(o => o.DateEt >= since);
        var all = await q.ToListAsync(ctx.CancellationToken);
        var signals = all.Where(o => o.SignalFired).ToList();

        var tiers = new[] { Count("pmHigh ≥ +50%", signals), Count("pmHigh ≥ +100%", signals.Where(o => o.PmHigh100).ToList()) };
        var fees = signals.Where(o => o.LocateFee is not null && o.LocateAvailable == true).Select(o => o.LocateFee!.Value).OrderBy(f => f).ToList();
        var feePct = signals.Where(o => o.LocateFee is not null && o.LocateAvailable == true && o.Close0945 is > 0)
            .Select(o => (double)(o.LocateFee!.Value / o.Close0945!.Value) * 100).OrderBy(f => f).ToList();

        var days = all.Select(o => o.DateEt).Distinct().Count();
        var data = new
        {
            Days = days, Candidates = all.Count, Signals = signals.Count, Tiers = tiers,
            FeePerShare = Dist(fees.Select(f => (double)f).ToList()), FeePctOfPrice = Dist(feePct),
        };

        var text = $"Fade-short borrow pilot{(args.Since is { } s ? $" since {s:yyyy-MM-dd}" : "")}: {days} day(s), {all.Count} candidate(s) at pmHigh ≥ +50%, {signals.Count} signal(s) fired at 09:45.";
        if (signals.Count == 0)
            return OperationResult.Ok(data, text + "\nNo signals yet; the shares below need at least one.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("tier").Column("signals", Align.Right).Column("shortable", Align.Right).Column("ETB", Align.Right).Column("HTB+short", Align.Right)
            .Column("loc quoted", Align.Right).Column("loc avail", Align.Right).Column("SSR", Align.Right).Column("RegSHO", Align.Right);
        foreach (var t in tiers)
            table.Row(t.Name, t.Signals.ToString(CultureInfo.InvariantCulture), Share(t.Shortable, t.Signals), Share(t.EasyToBorrow, t.Signals),
                Share(t.HtbShortable, t.Signals), Share(t.LocateQuoted, t.Signals), Share(t.LocateAvailable, t.Signals), Share(t.SsrActive, t.Signals), Share(t.RegSho, t.Signals));
        text += "\n" + table.Render();
        text += "\nLocate fee per share (available quotes): " + DistText(fees.Select(f => (double)f).ToList(), v => "$" + v.ToString("0.0000", CultureInfo.InvariantCulture));
        text += "\nLocate fee as % of the 09:45 price: " + DistText(feePct, v => v.ToString("0.00", CultureInfo.InvariantCulture) + "%");
        text += "\nShares are at 09:46 ET (the entry minute); HTB+short = shortable but not easy-to-borrow (needs a locate).";
        return OperationResult.Ok(data, text);
    }

    internal static Tier Count(string name, IReadOnlyList<FadeShortBorrowObservation> rows) => new(name, rows.Count,
        rows.Count(o => o.Shortable == true), rows.Count(o => o.EasyToBorrow == true), rows.Count(o => o.Shortable == true && o.EasyToBorrow == false),
        rows.Count(o => o.LocateQueried), rows.Count(o => o.LocateAvailable == true), rows.Count(o => o.SsrActive == true), rows.Count(o => o.RegSho == true));

    private static string Share(int n, int of) => of == 0 ? "—" : $"{n} ({100.0 * n / of:0}%)";

    private static object? Dist(IReadOnlyList<double> sorted) => sorted.Count == 0 ? null : new
    {
        N = sorted.Count, Min = sorted[0], P25 = Quantile(sorted, 0.25), Median = Quantile(sorted, 0.5), P75 = Quantile(sorted, 0.75), Max = sorted[^1],
    };

    private static string DistText(IReadOnlyList<double> sorted, Func<double, string> f) => sorted.Count == 0 ? "none quoted" :
        $"n={sorted.Count}, min {f(sorted[0])}, p25 {f(Quantile(sorted, 0.25))}, median {f(Quantile(sorted, 0.5))}, p75 {f(Quantile(sorted, 0.75))}, max {f(sorted[^1])}";

    internal static double Quantile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 1) return sorted[0];
        var h = (sorted.Count - 1) * p;
        int lo = (int)Math.Floor(h), hi = (int)Math.Ceiling(h);
        return sorted[lo] + (h - lo) * (sorted[hi] - sorted[lo]);
    }
}
