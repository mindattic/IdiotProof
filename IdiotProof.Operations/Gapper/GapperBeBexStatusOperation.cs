using System.Globalization;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Monitor;
using IdiotProof.Operations.Market;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Gapper;

/// <summary>
/// Reads back the BE/BEX decay gate: the latest decay reading the Monitor's after-close scan
/// stored, the threshold and lookback it used, and every BE strategy of yours that the scanner
/// gates (Author tag <see cref="BeBexDecayScanner.DecayGatedAuthorTag"/>) with whether it is
/// armed and whether the scanner itself disarmed it.
/// </summary>
[Operation("gapper be-bex status", Summary = "The BE/BEX decay gate: latest decay reading, threshold, and which gated BE strategies are armed or disarmed by it.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper be-bex status")]
public sealed class GapperBeBexStatusOperation : IOperation<NoArgs>
{
    private const int DefaultLookbackDays = 20;
    private const decimal DefaultThresholdPercent = 8m;

    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var settings = ctx.Get<SettingsRepository>();
        var lookback = await settings.GetIntAsync("decay.be-bex.lookbackdays", ctx.CancellationToken) ?? DefaultLookbackDays;
        var threshold = await settings.GetDecimalAsync("decay.be-bex.threshold", ctx.CancellationToken) ?? DefaultThresholdPercent;
        var latestJson = await settings.GetAsync("decay.be-bex.latest", ctx.CancellationToken);
        DecayReading? latest = null;
        if (!string.IsNullOrWhiteSpace(latestJson))
        {
            try { latest = JsonSerializer.Deserialize<DecayReading>(latestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException) { latest = null; }
        }

        var gated = (await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken))
            .Where(s => s.Symbol.Equals(BeBexDecayScanner.UnderlyingSymbol, StringComparison.OrdinalIgnoreCase) && s.Author == BeBexDecayScanner.DecayGatedAuthorTag)
            .ToList();
        var rows = new List<object>();
        var table = new TextTable { MaxWidth = ctx.Width }.Column("id").Column("title", maxWidth: 40).Column("armed").Column("disarmed by gate").Column("pos", Align.Right);
        foreach (var s in gated)
        {
            var byGate = await settings.GetBoolAsync($"decay.be-bex.disarmed.{s.Id}", ctx.CancellationToken) ?? false;
            rows.Add(new { s.Id, s.Title, s.IsActive, DisarmedByGate = byGate, s.PositionQty });
            table.Row(StrategyRefs.ShortGuid(s.Id), s.Title, s.IsActive ? "yes" : "no", byGate ? "yes" : "no", s.PositionQty == 0 ? "" : s.PositionQty.ToString());
        }

        var favorable = latest is not null && (decimal)latest.DecayPercent >= threshold;
        var kv = new KeyValueBlock()
            .Add("pair", $"{BeBexDecayScanner.UnderlyingSymbol} vs {BeBexDecayScanner.LeveragedSymbol} (2× daily-reset)")
            .Add("lookback", $"{lookback} trading days")
            .Add("threshold", $"decay ≥ {threshold.ToString("0.##", CultureInfo.InvariantCulture)}% disarms the gated BE strategy")
            .Add("latest", latest is null ? "no reading yet (the Monitor scans once, 4:30-5:00 PM ET)"
                : $"decay {Fmt.PercentPoints((decimal)latest.DecayPercent, 2, signed: true)} — BE {Fmt.PercentPoints((decimal)latest.BeReturnPercent, 2, signed: true)}, " +
                  $"BEX expected {Fmt.PercentPoints((decimal)latest.ExpectedBexReturnPercent, 2, signed: true)} vs actual {Fmt.PercentPoints((decimal)latest.ActualBexReturnPercent, 2, signed: true)} ({Fmt.Et(latest.ComputedUtc)})")
            .Add("gate says", latest is null ? "—" : favorable ? "DISARM (decay at/above threshold)" : "ARM (decay below threshold)");

        var data = new { LookbackDays = lookback, ThresholdPercent = threshold, Latest = latest, GateFavorsDisarm = latest is null ? (bool?)null : favorable, Strategies = rows };
        var text = kv.Render() + "\n\n" + (gated.Count == 0
            ? $"No gated BE strategies of yours (Author '{BeBexDecayScanner.DecayGatedAuthorTag}')."
            : table.Render());
        return OperationResult.Ok(data, text);
    }

    /// <summary>The JSON the scanner stores under <c>decay.be-bex.latest</c>.</summary>
    public sealed record DecayReading(DateTime ComputedUtc, int LookbackDays, double BeReturnPercent, double ExpectedBexReturnPercent, double ActualBexReturnPercent, double DecayPercent);
}
