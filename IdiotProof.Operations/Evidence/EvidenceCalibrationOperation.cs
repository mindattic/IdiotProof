using System.Text.Json;
using IdiotProof.Core.Adaptation;
using IdiotProof.Core.Data;
using IdiotProof.Evidence;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceCalibrationArgs
{
    [Option("source", Description = "exhaustion, adaptation or all (default).")]
    public string Source { get; init; } = "all";

    [Option("buckets", Description = "Probability buckets across 0..1 (default 5).")]
    public int Buckets { get; init; } = 5;
}

/// <summary>
/// Do the scanners' learned probabilities hold up? Compares each decision's predicted win probability
/// (an exhaustion long's counted reversal rate, an adaptation's chosen-variant posterior mean) with
/// whether its realized trades made money.
/// </summary>
[Operation("evidence calibration", Summary = "Learned-vs-realized calibration per template: predicted probability buckets vs realized win rate, Brier score against the base-rate baseline, sample counts.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence calibration")]
[Example("idiotproof evidence calibration --source exhaustion --buckets 4")]
public sealed class EvidenceCalibrationOperation : IOperation<EvidenceCalibrationArgs>
{
    public const string ExhaustionSource = "exhaustion";
    public const string AdaptationSource = "adaptation";

    public async Task<OperationResult> ExecuteAsync(EvidenceCalibrationArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var source = (args.Source ?? "all").Trim().ToLowerInvariant();
        if (source is not ("all" or ExhaustionSource or AdaptationSource))
            return OperationResult.Usage("--source must be exhaustion, adaptation or all.");
        if (args.Buckets is < 1 or > 20) return OperationResult.Usage("--buckets must be 1..20.");

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var samples = new List<CalibrationSample>();

        if (source is "all" or ExhaustionSource)
        {
            var rows = await db.ExhaustionDecisions.AsNoTracking()
                .Where(d => d.OwnerUserId == owner && d.Armed && d.OutcomeRecordedUtc != null && d.ReversalProbability != null)
                .ToListAsync(ctx.CancellationToken);
            samples.AddRange(rows.Select(d => new CalibrationSample(ExhaustionSource,
                $"{ExhaustionSource}-{d.Side.ToLowerInvariant()}", d.ReversalProbability!.Value, (d.RealizedPnL ?? 0) > 0)));
        }

        if (source is "all" or AdaptationSource)
        {
            var rows = await db.AdaptationDecisions.AsNoTracking()
                .Where(d => d.OwnerUserId == owner && d.Outcome == AdaptationService.Applied && d.OutcomeRecordedUtc != null)
                .ToListAsync(ctx.CancellationToken);
            foreach (var d in rows)
                if (ChosenPosteriorMean(d.VariantsJson, d.ChosenVariant) is { } p)
                    samples.Add(new CalibrationSample(AdaptationSource, d.ChosenVariant, p, (d.RealizedPnL ?? 0) > 0));
        }

        var groups = Calibration.Report(samples, args.Buckets);
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("source").Column("template").Column("n", Align.Right).Column("predicted", Align.Right)
            .Column("realized", Align.Right).Column("brier", Align.Right).Column("baseline", Align.Right);
        foreach (var g in groups)
            table.Row(g.Source, g.Template, g.Samples.ToString(), EvidenceSupport.Rate(g.MeanPredicted), EvidenceSupport.Rate(g.RealizedRate),
                g.Brier.ToString("0.000"), g.BaselineBrier.ToString("0.000"));

        var data = new
        {
            Source = source,
            args.Buckets,
            Samples = samples.Count,
            Groups = groups.Select(g => new
            {
                g.Source, g.Template, g.Samples, g.Wins,
                MeanPredicted = EvidenceSupport.Round(g.MeanPredicted), RealizedRate = EvidenceSupport.Round(g.RealizedRate),
                Brier = EvidenceSupport.Round(g.Brier), BaselineBrier = EvidenceSupport.Round(g.BaselineBrier),
                Buckets = g.Buckets.Select(b => new
                {
                    b.Low, b.High, b.Samples, MeanPredicted = EvidenceSupport.Round(b.MeanPredicted),
                    RealizedRate = EvidenceSupport.Round(b.RealizedRate), IntervalLow = EvidenceSupport.Round(b.IntervalLow),
                    IntervalHigh = EvidenceSupport.Round(b.IntervalHigh),
                }),
            }),
        };
        var text = groups.Count == 0
            ? "No decisions with realized outcomes yet: calibration needs armed/applied decisions whose trades have closed."
            : $"{table.Render()}\nbrier below baseline = the probabilities beat always predicting the realized rate; won = realized P&L > 0.";
        return OperationResult.Ok(data, text);
    }

    /// <summary>The chosen variant's Beta-posterior mean win rate from the stored shadow scores, or null when absent.</summary>
    public static double? ChosenPosteriorMean(string variantsJson, string chosen)
    {
        if (string.IsNullOrWhiteSpace(variantsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(variantsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var score in doc.RootElement.EnumerateArray())
            {
                if (!TryGet(score, "Variant", out var variant) || !TryGet(variant, "Name", out var name)) continue;
                if (name.GetString() != chosen) continue;
                return TryGet(score, "PosteriorMean", out var mean) && mean.ValueKind == JsonValueKind.Number ? mean.GetDouble() : null;
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        value = default;
        if (e.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        return false;
    }
}
