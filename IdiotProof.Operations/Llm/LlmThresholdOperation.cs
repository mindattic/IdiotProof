using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Settings;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Llm;

public sealed record LlmThresholdArgs
{
    [Argument(0, Name = "value", Description = "Share of counted votes that must Approve, 0..1 (e.g. 0.66) or a percent (66); omit to show the current one.", Required = false)]
    public decimal? Value { get; init; }
}

/// <summary>
/// The LLM panel's consensus threshold: the share of counted votes that must Approve before an
/// automated entry passes the gate (Reject wins at the same share; anything in between is Abstain,
/// which blocks). Saved in SettingsKv so the Monitor's next vote uses it and it survives a restart.
/// Below one half a split panel could approve, so that is refused.
/// </summary>
[Operation("llm threshold", Summary = "Show or set the LLM panel's consensus threshold (0.5-1): the share of votes that must Approve.", Risk = OperationRisk.Write)]
[Example("idiotproof llm threshold")]
[Example("idiotproof llm threshold 0.75")]
public sealed class LlmThresholdOperation : IOperation<LlmThresholdArgs>
{
    public async Task<OperationResult> ExecuteAsync(LlmThresholdArgs args, OperationContext ctx)
    {
        ctx.RequireOwner();
        var before = await LlmReads.ThresholdAsync(ctx);
        if (args.Value is not { } raw)
            return OperationResult.Ok(new { threshold = before }, $"Consensus threshold: {Fmt.Percent(before, 0)} of counted votes must Approve.");

        var value = raw > 1m && raw <= 100m ? raw / 100m : raw;
        if (value is < 0.5m or > 1m)
            return OperationResult.Usage("The threshold is a share from 0.5 to 1 (or 50-100 as a percent); below one half a split panel could approve.");

        await LlmPanelSettings.SetThresholdAsync(ctx.Get<IDbContextFactory<AppDbContext>>(), value, ctx.CancellationToken);
        ctx.Get<AppSettings>().LlmConsensusThreshold = value; // this Hub's in-memory default follows too
        return OperationResult.Ok(new { threshold = value, was = before },
            $"Consensus threshold {Fmt.Percent(before, 0)} -> {Fmt.Percent(value, 0)}. The Monitor's next vote uses it.");
    }
}
