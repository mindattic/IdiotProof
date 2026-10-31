using System.Globalization;
using System.Text.Json;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketMoversArgs
{
    [Option("top", Description = "How many gainers and how many losers (default 10, max 50).")]
    public int Top { get; init; } = 10;
}

/// <summary>
/// Alpaca's screener top gainers and losers (<c>/v1beta1/screener/stocks/movers</c>): change from
/// the prior close, as two aligned tables.
/// </summary>
[Operation("market movers", Summary = "Today's top stock gainers and losers by percent change (Alpaca screener).", Risk = OperationRisk.Read)]
[Example("idiotproof market movers")]
[Example("idiotproof market movers --top 20")]
public sealed class MarketMoversOperation : IOperation<MarketMoversArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketMoversArgs args, OperationContext ctx)
    {
        if (args.Top is < 1 or > 50) return OperationResult.Usage("--top must be 1-50.");

        await using var api = await MarketData.OpenAsync(ctx);
        var root = await api.GetDataAsync("/v1beta1/screener/stocks/movers",
            MarketData.Q(("top", args.Top.ToString(CultureInfo.InvariantCulture))), ctx.CancellationToken);
        var updated = MarketData.Time(root, "last_updated");

        var gainers = Movers(root, "gainers");
        var losers = Movers(root, "losers");
        var human = $"Movers as of {MarketData.Et(updated)}\n\nGAINERS\n{Render(gainers, ctx)}\n\nLOSERS\n{Render(losers, ctx)}";
        return OperationResult.Ok(new { lastUpdated = MarketData.EtOffset(updated), gainers, losers }, human);
    }

    private static List<Mover> Movers(JsonElement root, string property) =>
        MarketData.Obj(root, property) is { ValueKind: JsonValueKind.Array } items
            ? [.. items.EnumerateArray().Select(m => new Mover(MarketData.Str(m, "symbol") ?? "?", MarketData.Dec(m, "price"),
                MarketData.Dec(m, "change"), MarketData.Dec(m, "percent_change")))]
            : [];

    private static string Render(List<Mover> movers, OperationContext ctx)
    {
        if (movers.Count == 0) return "(none)";
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("#", Align.Right).Column("Symbol").Column("Price", Align.Right).Column("Change", Align.Right).Column("%", Align.Right);
        for (var i = 0; i < movers.Count; i++)
        {
            var m = movers[i];
            table.Row((i + 1).ToString(CultureInfo.InvariantCulture), m.Symbol, MarketData.Px(m.Price), MarketData.Signed(m.Change),
                m.PercentChange is { } p ? Fmt.PercentPoints(p, 2, signed: true) : "-");
        }
        return table.Render();
    }

    /// <summary>One screener mover; <see cref="PercentChange"/> is in percent units (12.5 = 12.5%).</summary>
    public sealed record Mover(string Symbol, decimal? Price, decimal? Change, decimal? PercentChange);
}
