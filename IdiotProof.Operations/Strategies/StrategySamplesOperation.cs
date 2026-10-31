using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;
using Pre20260217 = IdiotProof.Strategies.Strategies_20260217;

namespace IdiotProof.Operations.Strategies;

/// <summary>A built-in example strategy: a short name to ask for it by, what it shows, and how to build it.</summary>
public sealed record StrategySample(string Name, string Pattern, string Teaches, Func<StrategyDefinition> Build);

/// <summary>
/// The built-in sample strategies — worked examples of the IdiotScript phases, each one valid
/// (a stop the Risk Guardian accepts, 1 share) so it can be created as-is and edited.
/// </summary>
public static class StrategySamples
{
    public static readonly IReadOnlyList<StrategySample> All =
    [
        new("vwap-reclaim", "VWAP reclaim with trend filter", "Filters (ADX, EMA stack) + an entry trigger + percent stop and target",
            () => Stock.Ticker("SPY").Name("SPY VWAP reclaim")
                .RequireAdxAbove(20).RequireEmaStack(9, 21)
                .OnVwapReclaim().WithVolumeConfirm(1.2)
                .Quantity(1).Long().StopLossPercent(1.5).TakeProfitPercent(3).Build()),
        new("ema-pullback", "Pullback to the 9 EMA in an uptrend", "Entry conditions that must all hold at once + a trailing stop + a sell-by time",
            () => Stock.Ticker("QQQ").Name("QQQ 9 EMA pullback")
                .IsAboveVwap().IsBetweenEma(9, 21).OnReclaim(9)
                .Quantity(1).Long().StopLossPercent(1).TrailingStopLoss(1.5).SellBy("15:45").Build()),
        new("opening-window", "First-hour breakout with an entry window", "An ET entry window filter + a breakout level + a price stop and target",
            () => Stock.Ticker("AAPL").Name("AAPL first-hour breakout")
                .RequireEntryWindow("09:45", "10:30").Breakout(230).IsAboveVwap().WithVolumeConfirm(1.5)
                .Quantity(1).Long().StopLoss(227.5).TakeProfit(236).Build()),
        new("rsi-bounce", "Oversold bounce at support", "Oscillator + candle pattern triggers + a peak-giveback exit",
            () => Stock.Ticker("AMD").Name("AMD oversold bounce")
                .IsRsiOversold(30).IsHammer().IsAtSupport(0.5)
                .Quantity(1).Long().StopLossPercent(2).TakeProfitPercent(4).PeakGiveback(40).Build()),
        new("nci-breakout-pullback", "Breakout, pullback, VWAP hold (2026-02-17 watchlist)", "No break, no trade: Breakout + Pullback latch + two-target scale-out",
            () => WithOneShare(Pre20260217.NCI())),
        new("erna-premarket", "Premarket momentum with dual support (2026-02-17 watchlist)", "Premarket session + HoldsAbove support",
            () => WithOneShare(Pre20260217.ERNA())),
        new("sune-wedge", "Wedge breakout, three targets (2026-02-17 watchlist)", "A three-rung take-profit ladder",
            () => WithOneShare(Pre20260217.SUNE())),
    ];

    public static StrategySample? Find(string name) =>
        All.FirstOrDefault(s => s.Name.Equals((name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    private static StrategyDefinition WithOneShare(StrategyDefinition def)
    {
        if (def.Quantity <= 0 && def.NotionalAmount is null) def.Quantity = 1;
        return def;
    }
}

public sealed record StrategySamplesArgs
{
    [Argument(0, Name = "name", Description = "A sample's name to show in full (omit to list them all).", Required = false)]
    public string? Name { get; init; }

    [Option("format", Description = "How to show one sample: script (default), stamp or json.", Short = 'f')]
    public StrategyExportFormat Format { get; init; } = StrategyExportFormat.Script;
}

[Operation("strategy samples", Summary = "List the built-in sample strategies, or show one as IdiotScript, a stamp or canonical JSON.", Risk = OperationRisk.Read,
    Description = "Samples are worked examples you can save and edit: `idiotproof strategy samples vwap-reclaim --format json | idiotproof strategy create --canon -`.")]
[Example("idiotproof strategy samples")]
[Example("idiotproof strategy samples vwap-reclaim")]
[Example("idiotproof strategy samples sune-wedge --format stamp")]
public sealed class StrategySamplesOperation : IOperation<StrategySamplesArgs>
{
    public Task<OperationResult> ExecuteAsync(StrategySamplesArgs args, OperationContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            var items = StrategySamples.All.Select(s =>
            {
                var def = s.Build();
                return new { s.Name, def.Symbol, Direction = def.Direction.ToString(), s.Pattern, s.Teaches, Stamp = def.ShortId() };
            }).ToList();
            var table = new TextTable { MaxWidth = ctx.Width }.Column("name").Column("sym").Column("stamp").Column("pattern", maxWidth: 52).Column("teaches", maxWidth: 60);
            foreach (var i in items) table.Row(i.Name, i.Symbol, i.Stamp, i.Pattern, i.Teaches);
            return Task.FromResult(OperationResult.Ok(items,
                table.Render() + $"\n{items.Count} samples. Show one with `idiotproof strategy samples <name>`."));
        }

        var sample = StrategySamples.Find(args.Name)
            ?? throw OperationException.Usage($"No sample named '{args.Name}'. Samples: {string.Join(", ", StrategySamples.All.Select(s => s.Name))}.");
        var d = sample.Build();
        var text = args.Format switch
        {
            StrategyExportFormat.Json => StrategyJson.Serialize(d),
            StrategyExportFormat.Stamp => d.ToStamp(),
            StrategyExportFormat.Armor => d.ToArmor(),
            _ => d.ToScript(),
        };
        var human = args.Format is StrategyExportFormat.Json or StrategyExportFormat.Armor
            ? text
            : $"{sample.Name} — {sample.Pattern}\nTeaches: {sample.Teaches}\n\n{text}\n\nSave it: idiotproof strategy samples {sample.Name} --format json | idiotproof strategy create --canon -";
        return Task.FromResult(OperationResult.Ok(new
        {
            sample.Name, d.Symbol, sample.Pattern, sample.Teaches, Stamp = d.ShortId(),
            Format = args.Format.ToString().ToLowerInvariant(), Text = text, Script = d.ToScript(),
        }, human));
    }
}
