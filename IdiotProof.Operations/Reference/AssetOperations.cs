using System.Text.Json;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Reference;

/// <summary>Alpaca's asset classes on <c>/v2/assets</c> (wire names us_equity, us_option, crypto).</summary>
public enum AssetClassFilter
{
    UsEquity,
    UsOption,
    Crypto,
}

/// <summary>Alpaca asset status filter.</summary>
public enum AssetStatusFilter
{
    Active,
    Inactive,
    All,
}

public sealed record AssetsListArgs
{
    [Option("class", Description = "us_equity, us_option or crypto (default: every class).")]
    public AssetClassFilter? Class { get; init; }

    [Option("exchange", Description = "Only this exchange (NASDAQ, NYSE, ARCA, AMEX, BATS, OTC, CRYPTO...).")]
    public string? Exchange { get; init; }

    [Option("status", Description = "active, inactive or all (default active).")]
    public AssetStatusFilter Status { get; init; } = AssetStatusFilter.Active;

    [Option("attributes", Description = "Alpaca attribute filter, comma separated (ptp_no_exception, ptp_with_exception, ipo, has_options, options_late_close, fractional_eh_enabled...).")]
    public string[] Attributes { get; init; } = [];

    [Option("search", Description = "Keep assets whose symbol starts with, or name contains, this text.")]
    public string? Search { get; init; }

    [Option("tradable", Description = "Only assets tradable at Alpaca.")]
    public bool Tradable { get; init; }

    [Option("shortable", Description = "Only shortable assets.")]
    public bool Shortable { get; init; }

    [Option("limit", Description = "Rows to show (default 50; the totals line counts them all).")]
    public int Limit { get; init; } = 50;

    [Option("mode", Description = "Alpaca account to ask: paper (default) or live.")]
    public AlpacaAccount Mode { get; init; } = AlpacaAccount.Paper;
}

[Operation("assets list", Summary = "Alpaca's tradable universe (/v2/assets) filtered by class, exchange, status and attributes, with totals.", Risk = OperationRisk.Read,
    Description = "Alpaca returns 10k+ assets; this shows the first --limit matches (sorted by symbol) and how many matched in all.")]
[Example("idiotproof assets list --class us_equity --exchange NASDAQ --search APP")]
[Example("idiotproof assets list --class crypto --limit 100")]
[Example("idiotproof assets list --attributes has_options --shortable")]
public sealed class AssetsListOperation : IOperation<AssetsListArgs>
{
    public async Task<OperationResult> ExecuteAsync(AssetsListArgs args, OperationContext ctx)
    {
        if (args.Limit < 1) return OperationResult.Usage("--limit must be at least 1.");
        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        var query = new Dictionary<string, string?>
        {
            ["status"] = args.Status == AssetStatusFilter.All ? null : OperationParameter.EnumWireName(args.Status),
            ["asset_class"] = args.Class is { } c ? WireClass(c) : null,
            ["exchange"] = string.IsNullOrWhiteSpace(args.Exchange) ? null : args.Exchange.Trim().ToUpperInvariant(),
            ["attributes"] = ReferenceSupport.Csv(args.Attributes),
        };
        var root = await api.GetAsync("/v2/assets", query, ctx.CancellationToken);
        var all = root.Items().ToList();

        var search = args.Search?.Trim();
        var matched = all.Where(a =>
                (!args.Tradable || a.Bool("tradable") == true) &&
                (!args.Shortable || a.Bool("shortable") == true) &&
                (string.IsNullOrEmpty(search) ||
                 (a.Str("symbol") ?? "").StartsWith(search, StringComparison.OrdinalIgnoreCase) ||
                 (a.Str("name") ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a.Str("symbol"), StringComparer.Ordinal)
            .ToList();
        var shown = matched.Take(args.Limit).ToList();

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Symbol").Column("Name", maxWidth: 40).Column("Class").Column("Exchange").Column("Status")
            .Column("Trade").Column("Short").Column("ETB").Column("Margin").Column("Frac");
        foreach (var a in shown)
            table.Row(a.Str("symbol"), a.Str("name"), a.Str("class"), a.Str("exchange"), a.Str("status"),
                ReferenceSupport.YesNo(a.Bool("tradable")), ReferenceSupport.YesNo(a.Bool("shortable")),
                ReferenceSupport.YesNo(a.Bool("easy_to_borrow")), ReferenceSupport.YesNo(a.Bool("marginable")),
                ReferenceSupport.YesNo(a.Bool("fractionable")));

        var totals = ReferenceSupport.Totals("assets", shown.Count, matched.Count, all.Count);
        var human = shown.Count == 0 ? $"No assets matched. {totals}" : $"{table.Render()}\n{totals}";
        return OperationResult.Ok(new
        {
            total = all.Count,
            matched = matched.Count,
            shown = shown.Count,
            assets = shown.Select(AssetsShowOperation.Shape),
        }, human);
    }

    internal static string WireClass(AssetClassFilter c) => c switch
    {
        AssetClassFilter.UsEquity => "us_equity",
        AssetClassFilter.UsOption => "us_option",
        _ => "crypto",
    };
}

public sealed record AssetsShowArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, crypto pair (BTC/USD) or Alpaca asset id.")]
    public string Symbol { get; init; } = "";

    [Option("mode", Description = "Alpaca account to ask: paper (default) or live.")]
    public AlpacaAccount Mode { get; init; } = AlpacaAccount.Paper;
}

[Operation("assets show", Summary = "One Alpaca asset: tradable, shortable, easy-to-borrow, marginable, fractionable and margin requirements.", Risk = OperationRisk.Read)]
[Example("idiotproof assets show AAPL")]
[Example("idiotproof assets show BTC/USD")]
public sealed class AssetsShowOperation : IOperation<AssetsShowArgs>
{
    public async Task<OperationResult> ExecuteAsync(AssetsShowArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol, e.g. idiotproof assets show AAPL.");
        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        var a = await api.GetAsync("/v2/assets/" + Uri.EscapeDataString(symbol.ToUpperInvariant()), null, ctx.CancellationToken);

        var block = new KeyValueBlock()
            .Add("Symbol", a.Str("symbol"))
            .Add("Name", a.Str("name"))
            .Add("Class", a.Str("class"))
            .Add("Exchange", a.Str("exchange"))
            .Add("Status", a.Str("status"))
            .Add("Tradable", ReferenceSupport.YesNo(a.Bool("tradable")))
            .Add("Shortable", ReferenceSupport.YesNo(a.Bool("shortable")))
            .Add("Easy to borrow", ReferenceSupport.YesNo(a.Bool("easy_to_borrow")))
            .Add("Marginable", ReferenceSupport.YesNo(a.Bool("marginable")))
            .Add("Fractionable", ReferenceSupport.YesNo(a.Bool("fractionable")))
            .Add("Maintenance margin", Pct(a.Dec("maintenance_margin_requirement")))
            .Add("Margin req. long", Pct(a.Dec("margin_requirement_long")))
            .Add("Margin req. short", Pct(a.Dec("margin_requirement_short")))
            .Add("Min order size", a.Str("min_order_size"))
            .Add("Min trade increment", a.Str("min_trade_increment"))
            .Add("Price increment", a.Str("price_increment"))
            .Add("Attributes", string.Join(", ", a.Strings("attributes")), "-")
            .Add("Asset id", a.Str("id"));
        return OperationResult.Ok(Shape(a), block.Render());
    }

    /// <summary>Alpaca reports margin requirements in percent units (30 = 30%).</summary>
    private static string? Pct(decimal? v) => v is { } p ? Fmt.PercentPoints(p, 0) : null;

    internal static object Shape(JsonElement a) => new
    {
        id = a.Str("id"),
        symbol = a.Str("symbol"),
        name = a.Str("name"),
        assetClass = a.Str("class"),
        exchange = a.Str("exchange"),
        status = a.Str("status"),
        tradable = a.Bool("tradable"),
        shortable = a.Bool("shortable"),
        easyToBorrow = a.Bool("easy_to_borrow"),
        marginable = a.Bool("marginable"),
        fractionable = a.Bool("fractionable"),
        maintenanceMarginRequirement = a.Dec("maintenance_margin_requirement"),
        marginRequirementLong = a.Dec("margin_requirement_long"),
        marginRequirementShort = a.Dec("margin_requirement_short"),
        attributes = a.Strings("attributes").ToArray(),
    };
}
