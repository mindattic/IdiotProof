using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using IdiotProof.Monitor;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Gapper;

/// <summary>Shared facts about the auto-gapper (the movers-driven, adaptive, paper-only generator).</summary>
public static class GapperAutoRules
{
    /// <summary>The <c>Strategy.Author</c> the <see cref="AutoGapperScanner"/> stamps on every row it arms.</summary>
    public const string AuthorTag = "IdiotProof Auto-Gapper";

    /// <summary>The host's settings (auto-gapper knobs), or defaults when the host registers none.</summary>
    public static AppSettings Settings(OperationContext ctx) => ctx.Services.GetService<AppSettings>() ?? new AppSettings();

    /// <summary>True when the auto-gapper is configured to route its strategies to paper.</summary>
    public static bool IsPaperOnly(AppSettings s) => string.Equals((s.AutoGapperBrokerMode ?? "paper").Trim(), "paper", StringComparison.OrdinalIgnoreCase);

    internal static string Et(DateTime? utc) => utc is { } u ? Fmt.Et(u) : "—";
}

public sealed record GapperAutoStatusArgs
{
    [Option("last", Description = "How many recent scans to show (default 7).")]
    public int Last { get; init; } = 7;
}

[Operation("gapper auto status", Summary = "Auto-gapper settings, its recent scans (screened, qualified, armed, skipped) and your auto-armed gappers.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper auto status")]
[Example("idiotproof gapper auto status --last 20")]
public sealed class GapperAutoStatusOperation : IOperation<GapperAutoStatusArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperAutoStatusArgs args, OperationContext ctx)
    {
        if (args.Last is < 1 or > 200) return OperationResult.Usage("--last must be 1-200.");
        var owner = ctx.RequireOwner();
        var settings = GapperAutoRules.Settings(ctx);
        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var scans = await db.AutoGapperScans.AsNoTracking().OrderByDescending(s => s.ScanStartedUtc).Take(args.Last).ToListAsync(ctx.CancellationToken);
        var armed = (await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken))
            .Where(s => s.Author == GapperAutoRules.AuthorTag && s.IsActive).OrderBy(s => s.Symbol).ToList();

        var data = new
        {
            Settings = new { settings.AutoGapperMinGapPercent, settings.AutoGapperMinPrice, settings.AutoGapperMaxCount, settings.AutoGapperBrokerMode, PaperOnly = GapperAutoRules.IsPaperOnly(settings) },
            Scans = scans.Select(s => new { s.Id, s.ScanEtDate, s.Phase, s.ScanStartedUtc, s.ScanCompletedUtc, s.MoversScreened, s.Qualified, s.Armed, s.Skipped, s.BrokerMode, s.Note }).ToList(),
            ArmedActive = armed.Select(s => new { s.Id, s.Symbol, s.Title, s.PositionQty }).ToList(),
        };

        var kv = new KeyValueBlock()
            .Add("qualifies", $"gap ≥{GapperRules.Num(settings.AutoGapperMinGapPercent)}%, price ≥{Fmt.Money(settings.AutoGapperMinPrice)}")
            .Add("arms", $"top {settings.AutoGapperMaxCount} by conviction (gap × premarket liquidity)")
            .Add("routing", GapperAutoRules.IsPaperOnly(settings) ? "paper only" : $"'{settings.AutoGapperBrokerMode}' — NOT paper; `gapper auto enable` refuses to arm")
            .Add("trigger", "on demand: `idiotproof gapper auto enable` (the Monitor has no scheduled auto-arm)")
            .Add("your auto-armed", armed.Count == 0 ? "none active" : string.Join(", ", armed.Select(s => s.PositionQty == 0 ? s.Symbol : $"{s.Symbol} (holding {s.PositionQty})")));

        var text = kv.Render();
        if (scans.Count == 0) text += "\n\nNo auto-gapper scans recorded yet.";
        else
        {
            var table = new TextTable { MaxWidth = ctx.Width }
                .Column("day").Column("phase").Column("started").Column("screened", Align.Right).Column("qualified", Align.Right)
                .Column("armed", Align.Right).Column("skipped", Align.Right).Column("mode").Column("note", maxWidth: 30);
            foreach (var s in scans)
                table.Row(s.ScanEtDate, s.Phase, Fmt.EtTime(s.ScanStartedUtc), s.MoversScreened.ToString(), s.Qualified.ToString(),
                    s.Armed.ToString(), s.Skipped.ToString(), s.BrokerMode, s.Note ?? (s.ScanCompletedUtc is null ? "incomplete" : ""));
            text += "\n\n" + table.Render();
        }
        return OperationResult.Ok(data, text);
    }
}
