using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Gapper;

[Operation("gapper caps", Summary = "Active-gapper counts against the caps: 3 manual gappers, one active strategy per symbol, the auto-gapper limit.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper caps")]
public sealed class GapperCapsOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var all = await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken);
        var activeGappers = all.Where(s => s.IsActive && GapperRules.IsGapper(s)).OrderBy(s => s.Symbol).ToList();
        var autoArmed = activeGappers.Count(s => s.Author == GapperAutoRules.AuthorTag);
        var duplicates = all.Where(s => s.IsActive).GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key.ToUpperInvariant()).ToList();
        var autoMax = ctx.Services.GetService(typeof(IdiotProof.Engine.Settings.AppSettings)) is IdiotProof.Engine.Settings.AppSettings settings
            ? settings.AutoGapperMaxCount : new IdiotProof.Engine.Settings.AppSettings().AutoGapperMaxCount;

        var free = Math.Max(0, GapperRules.MaxActiveGappers - activeGappers.Count);
        var data = new
        {
            ActiveGappers = activeGappers.Count,
            Cap = GapperRules.MaxActiveGappers,
            Free = free,
            Symbols = activeGappers.Select(s => s.Symbol).ToList(),
            AutoArmedActive = autoArmed,
            AutoGapperMaxPerRun = autoMax,
            DuplicateActiveSymbols = duplicates,
        };
        var kv = new KeyValueBlock()
            .Add("armed gappers", $"{activeGappers.Count}/{GapperRules.MaxActiveGappers}" + (activeGappers.Count == 0 ? "" : $"  ({string.Join(", ", activeGappers.Select(s => s.Symbol))})"))
            .Add("free slots", free == 0 ? "0 — disarm one before arming another" : free.ToString())
            .Add("one per symbol", duplicates.Count == 0 ? "ok — no symbol has two active strategies" : "VIOLATED: " + string.Join(", ", duplicates))
            .Add("auto-armed", $"{autoArmed} active (auto-gapper arms at most {autoMax} per run)");
        return OperationResult.Ok(data, kv.Render());
    }
}
