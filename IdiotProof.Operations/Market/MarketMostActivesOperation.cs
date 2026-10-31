using System.Globalization;
using System.Text.Json;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

/// <summary>What ranks the most-active screener.</summary>
public enum MostActiveBy
{
    Volume,
    Trades,
}

public sealed record MarketMostActivesArgs
{
    [Option("by", Description = "Rank by share volume (default) or by trade count.")]
    public MostActiveBy By { get; init; } = MostActiveBy.Volume;

    [Option("top", Description = "How many stocks (default 10, max 100).")]
    public int Top { get; init; } = 10;
}

/// <summary>Alpaca's most-active stocks screener (<c>/v1beta1/screener/stocks/most-actives</c>).</summary>
[Operation("market most-actives", Summary = "Today's most active stocks by share volume or trade count (Alpaca screener).", Risk = OperationRisk.Read)]
[Example("idiotproof market most-actives")]
[Example("idiotproof market most-actives --by trades --top 25")]
public sealed class MarketMostActivesOperation : IOperation<MarketMostActivesArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketMostActivesArgs args, OperationContext ctx)
    {
        if (args.Top is < 1 or > 100) return OperationResult.Usage("--top must be 1-100.");

        var by = args.By == MostActiveBy.Trades ? "trades" : "volume";
        await using var api = await MarketData.OpenAsync(ctx);
        var root = await api.GetDataAsync("/v1beta1/screener/stocks/most-actives",
            MarketData.Q(("by", by), ("top", args.Top.ToString(CultureInfo.InvariantCulture))), ctx.CancellationToken);
        var updated = MarketData.Time(root, "last_updated");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("#", Align.Right).Column("Symbol").Column("Volume", Align.Right).Column("Trades", Align.Right);
        var data = new List<object>();
        if (MarketData.Obj(root, "most_actives") is { ValueKind: JsonValueKind.Array } items)
        {
            var rank = 0;
            foreach (var m in items.EnumerateArray())
            {
                var symbol = MarketData.Str(m, "symbol") ?? "?";
                decimal? volume = MarketData.Dec(m, "volume"), trades = MarketData.Dec(m, "trade_count");
                table.Row((++rank).ToString(CultureInfo.InvariantCulture), symbol, MarketData.Compact(volume), MarketData.Compact(trades));
                data.Add(new { rank, symbol, volume, trades });
            }
        }
        var human = $"Most active by {by} as of {MarketData.Et(updated)}\n" + (data.Count == 0 ? "(none)" : table.Render());
        return OperationResult.Ok(new { by, lastUpdated = MarketData.EtOffset(updated), mostActives = data }, human);
    }
}
