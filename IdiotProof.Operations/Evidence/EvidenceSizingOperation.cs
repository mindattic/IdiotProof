using IdiotProof.Core.Data;
using IdiotProof.Evidence.Economics;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceSizingArgs
{
    [Option("strategy", Description = "Size from this strategy's closed trades in the diary (title, case-insensitive).")]
    public string? Strategy { get; init; }

    [Option("symbol", Description = "Size from this symbol's closed trades in the diary.")]
    public string? Symbol { get; init; }

    [Option("wins", Description = "Manual mode: winning trades (use with --losses and --payoff).")]
    public int? Wins { get; init; }

    [Option("losses", Description = "Manual mode: losing trades.")]
    public int? Losses { get; init; }

    [Option("payoff", Description = "Manual mode: average win / average loss.")]
    public double? Payoff { get; init; }

    [Option("fraction", Description = "Fraction of the posterior Kelly to recommend (default 0.5 = half Kelly).")]
    public double Fraction { get; init; } = 0.5;
}

/// <summary>
/// Advisory Kelly sizing from realized results: plug-in Kelly from the raw win rate next to the
/// growth-optimal fraction under a Beta posterior (shrunk for small samples) and a conservative
/// fraction at the posterior's 5th percentile. It reports numbers only; it never sets a quantity.
/// </summary>
[Operation("evidence sizing", Summary = "Advisory Kelly sizing (plug-in vs Beta-posterior vs conservative) from diary trades or manual counts; never changes order quantities.", Risk = OperationRisk.Read,
    Description = "The Risk Guardian and the 1-share default policy stay authoritative; this only reports what the measured edge would justify.")]
[Example("idiotproof evidence sizing --strategy \"SNDL dip-reclaim\"")]
[Example("idiotproof evidence sizing --wins 12 --losses 8 --payoff 1.3")]
public sealed class EvidenceSizingOperation : IOperation<EvidenceSizingArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceSizingArgs args, OperationContext ctx)
    {
        if (args.Fraction is < 0 or > 1) return OperationResult.Usage("--fraction must be in [0, 1].");
        bool manual = args.Wins is not null || args.Losses is not null || args.Payoff is not null;
        int wins, losses;
        double payoff;
        string basis;

        if (manual)
        {
            if (args.Wins is not { } w || args.Losses is not { } l || args.Payoff is not { } b || w < 0 || l < 0 || b <= 0)
                return OperationResult.Usage("Manual mode needs --wins >= 0, --losses >= 0 and --payoff > 0.");
            (wins, losses, payoff, basis) = (w, l, b, "manual counts");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(args.Strategy) && string.IsNullOrWhiteSpace(args.Symbol))
                return OperationResult.Usage("Pass --strategy or --symbol (diary trades), or --wins/--losses/--payoff.");
            var owner = ctx.RequireOwner();
            await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
            var query = db.TradeDiary.AsNoTracking()
                .Where(t => t.OwnerUserId == owner && t.Status == TradeDiaryStatus.Closed && t.ReturnPercent != null);
            if (!string.IsNullOrWhiteSpace(args.Symbol))
            {
                var symbol = args.Symbol.Trim().ToUpperInvariant();
                query = query.Where(t => t.Symbol == symbol);
            }
            var rows = await query.Select(t => new { t.StrategyTitle, t.ReturnPercent }).ToListAsync(ctx.CancellationToken);
            if (!string.IsNullOrWhiteSpace(args.Strategy))
                rows = rows.Where(r => string.Equals(r.StrategyTitle, args.Strategy.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            var returns = rows.Select(r => (double)r.ReturnPercent!.Value / 100.0).ToList();
            wins = returns.Count(r => r > 0);
            losses = returns.Count(r => r < 0);
            payoff = KellySizing.PayoffRatio(returns);
            basis = $"{returns.Count} closed diary trade(s)" + (returns.Count - wins - losses > 0 ? $" ({returns.Count - wins - losses} flat, ignored)" : "");
            if (double.IsNaN(payoff))
                return OperationResult.Ok(new { Basis = basis, Wins = wins, Losses = losses, Advisory = KellyAdvice.Disclaimer },
                    $"{basis}: need at least one win and one loss to estimate the payoff ratio.\n{KellyAdvice.Disclaimer}");
        }

        var a = KellySizing.Advise(wins, losses, payoff, fraction: args.Fraction);
        var block = new KeyValueBlock()
            .Add("basis", basis)
            .Add("record", $"{a.Wins}W / {a.Losses}L, payoff {a.PayoffRatio:0.00}:1")
            .Add("win rate", $"raw {EvidenceSupport.Rate(a.EmpiricalWinRate)}, posterior {EvidenceSupport.Rate(a.PosteriorWinRate)} (90% {EvidenceSupport.Rate(a.WinRateInterval.Low)}–{EvidenceSupport.Rate(a.WinRateInterval.High)})")
            .Add("kelly", $"plug-in {EvidenceSupport.Rate(a.PlugInFraction)}, posterior {EvidenceSupport.Rate(a.PosteriorFraction)}, conservative {EvidenceSupport.Rate(a.ConservativeFraction)}")
            .Add("advisory", $"{EvidenceSupport.Rate(a.RecommendedFraction)} of capital at risk ({args.Fraction:0.##} × posterior Kelly)")
            .Add("note", KellyAdvice.Disclaimer);

        return OperationResult.Ok(new
        {
            Basis = basis, a.Wins, a.Losses, PayoffRatio = EvidenceSupport.Round(a.PayoffRatio),
            EmpiricalWinRate = EvidenceSupport.Round(a.EmpiricalWinRate), PosteriorWinRate = EvidenceSupport.Round(a.PosteriorWinRate),
            WinRateLow = EvidenceSupport.Round(a.WinRateInterval.Low), WinRateHigh = EvidenceSupport.Round(a.WinRateInterval.High),
            PlugInKelly = EvidenceSupport.Round(a.PlugInFraction), PosteriorKelly = EvidenceSupport.Round(a.PosteriorFraction),
            ConservativeKelly = EvidenceSupport.Round(a.ConservativeFraction), RecommendedFraction = EvidenceSupport.Round(a.RecommendedFraction),
            Advisory = KellyAdvice.Disclaimer,
        }, block.Render());
    }
}
