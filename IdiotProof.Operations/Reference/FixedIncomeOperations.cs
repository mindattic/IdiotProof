using System.Net;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Reference;

/// <summary>Which Alpaca fixed income universe to list.</summary>
public enum BondType
{
    Treasuries,
    Corporates,
}

public sealed record AssetsBondsArgs
{
    [Option("type", Description = "treasuries (default) or corporates.")]
    public BondType Type { get; init; } = BondType.Treasuries;

    [Option("subtype", Description = "Treasury subtype filter (bill, note, bond, strips, tips, floating_rate).")]
    public string? Subtype { get; init; }

    [Option("bond-status", Description = "Bond status filter (outstanding, matured, pre_issuance).")]
    public string? BondStatus { get; init; }

    [Option("cusips", Description = "Only these CUSIPs, comma separated.")]
    public string[] Cusips { get; init; } = [];

    [Option("isins", Description = "Only these ISINs, comma separated.")]
    public string[] Isins { get; init; } = [];

    [Option("search", Description = "Keep bonds whose description, issuer, ticker, CUSIP or ISIN contains this text.")]
    public string? Search { get; init; }

    [Option("limit", Description = "Rows to show (default 50; sorted by maturity).")]
    public int Limit { get; init; } = 50;

    [Option("mode", Description = "Alpaca account to ask: paper (default) or live.")]
    public AlpacaAccount Mode { get; init; } = AlpacaAccount.Paper;
}

/// <summary>Shared fail-closed handling for Alpaca's fixed income endpoints (not every account has them).</summary>
internal static class FixedIncome
{
    public static async Task<(JsonElement Root, OperationResult? Failure)> TryAsync(Func<Task<JsonElement>> call, string what)
    {
        try
        {
            return (await call(), null);
        }
        catch (AlpacaApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
        {
            return (default, OperationResult.Fail(
                $"Alpaca refused {what} ({(int)ex.Status} {ex.Status}): {ex.AlpacaMessage}. Fixed income may not be enabled for this account.",
                new { status = (int)ex.Status, alpacaCode = ex.AlpacaCode, alpacaMessage = ex.AlpacaMessage, path = ex.Path }));
        }
    }

    public static string Pct(decimal? v) => v is { } p ? Fmt.PercentPoints(p, 3) : "-";
}

[Operation("assets bonds", Summary = "Alpaca fixed income assets: US Treasuries or US corporate bonds with coupon, maturity, last close and yield.", Risk = OperationRisk.Read)]
[Example("idiotproof assets bonds --subtype bill --limit 20")]
[Example("idiotproof assets bonds --type corporates --search AAPL")]
public sealed class AssetsBondsOperation : IOperation<AssetsBondsArgs>
{
    public async Task<OperationResult> ExecuteAsync(AssetsBondsArgs args, OperationContext ctx)
    {
        if (args.Limit < 1) return OperationResult.Usage("--limit must be at least 1.");
        var segment = args.Type == BondType.Corporates ? "us_corporates" : "us_treasuries";
        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        var query = new Dictionary<string, string?>
        {
            ["subtype"] = args.Subtype?.Trim(),
            ["bond_status"] = args.BondStatus?.Trim(),
            ["cusips"] = ReferenceSupport.Csv(args.Cusips),
            ["isins"] = ReferenceSupport.Csv(args.Isins),
        };
        var (root, failure) = await FixedIncome.TryAsync(
            () => api.GetAsync("/v2/assets/fixed_income/" + segment, query, ctx.CancellationToken), $"the {segment} list");
        if (failure is not null) return failure;

        var all = (root.ValueKind == JsonValueKind.Array ? root.Items() : root.Items(segment)).ToList();
        var search = args.Search?.Trim();
        var matched = all.Where(b => string.IsNullOrEmpty(search) ||
                new[] { "description", "description_short", "issuer", "ticker", "cusip", "isin" }
                    .Any(f => (b.Str(f) ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(b => b.Str("maturity_date") ?? "9999", StringComparer.Ordinal)
            .ToList();
        var shown = matched.Take(args.Limit).ToList();

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("CUSIP").Column("ISIN").Column("Description", maxWidth: 36).Column("Maturity").Column("Coupon", Align.Right)
            .Column("Close", Align.Right).Column("YTM", Align.Right).Column("Status");
        foreach (var b in shown)
            table.Row(b.Str("cusip"), b.Str("isin"), b.Str("description_short") ?? b.Str("description"), b.Str("maturity_date"),
                FixedIncome.Pct(b.Dec("coupon")), b.Dec("close_price") is { } p ? Fmt.Number(p, 4) : "-",
                FixedIncome.Pct(b.Dec("close_yield_to_maturity")), b.Str("bond_status"));

        var totals = ReferenceSupport.Totals(args.Type == BondType.Corporates ? "corporate bonds" : "treasuries", shown.Count, matched.Count, all.Count);
        var human = shown.Count == 0 ? $"No bonds matched. {totals}" : $"{table.Render()}\n{totals}";
        return OperationResult.Ok(new
        {
            type = segment,
            total = all.Count,
            matched = matched.Count,
            shown = shown.Count,
            bonds = shown.Select(b => new
            {
                cusip = b.Str("cusip"),
                isin = b.Str("isin"),
                description = b.Str("description"),
                issuer = b.Str("issuer"),
                ticker = b.Str("ticker"),
                subtype = b.Str("subtype"),
                bondStatus = b.Str("bond_status"),
                tradable = b.Bool("tradable"),
                issueDate = b.Str("issue_date"),
                maturityDate = b.Str("maturity_date"),
                coupon = b.Dec("coupon"),
                couponType = b.Str("coupon_type"),
                couponFrequency = b.Str("coupon_frequency"),
                closePrice = b.Dec("close_price"),
                closePriceDate = b.Str("close_price_date"),
                closeYieldToMaturity = b.Dec("close_yield_to_maturity"),
                closeYieldToWorst = b.Dec("close_yield_to_worst"),
            }),
        }, human);
    }
}

public sealed record BondPricesArgs
{
    [Argument(0, Name = "isins", Description = "One or more bond ISINs (space or comma separated).")]
    public string[] Isins { get; init; } = [];
}

[Operation("market bond-prices", Summary = "Latest Alpaca fixed income prices and yields for the given ISINs.", Risk = OperationRisk.Read)]
[Example("idiotproof market bond-prices US912797KA41 US91282CJL54")]
public sealed class MarketBondPricesOperation : IOperation<BondPricesArgs>
{
    public async Task<OperationResult> ExecuteAsync(BondPricesArgs args, OperationContext ctx)
    {
        var isins = ReferenceSupport.Csv(args.Isins).ToUpperInvariant();
        if (isins.Length == 0) return OperationResult.Usage("Name at least one ISIN, e.g. idiotproof market bond-prices US912797KA41.");
        await using var api = await ReferenceSupport.DataAsync(ctx);
        var (root, failure) = await FixedIncome.TryAsync(
            () => api.GetDataAsync("/v1beta1/fixed_income/latest/prices", new Dictionary<string, string?> { ["isins"] = isins }, ctx.CancellationToken),
            "latest bond prices");
        if (failure is not null) return failure;

        var prices = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("prices", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("ISIN").Column("Price", Align.Right).Column("YTM", Align.Right).Column("YTW", Align.Right).Column("As of");
        var data = new List<object>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (prices.ValueKind == JsonValueKind.Object)
            foreach (var entry in prices.EnumerateObject().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                var e = entry.Value;
                found.Add(entry.Name);
                table.Row(entry.Name, e.Dec("p") is { } px ? Fmt.Number(px, 4) : "-", FixedIncome.Pct(e.Dec("ytm")), FixedIncome.Pct(e.Dec("ytw")), e.Instant("t"));
                data.Add(new { isin = entry.Name, price = e.Dec("p"), yieldToMaturity = e.Dec("ytm"), yieldToWorst = e.Dec("ytw"), timestamp = e.Str("t") });
            }
        var missing = isins.Split(',').Where(i => !found.Contains(i)).ToList();
        var human = found.Count == 0 ? "Alpaca returned no prices for those ISINs." : table.Render();
        if (found.Count > 0 && missing.Count > 0) human += $"\nNo price for: {string.Join(", ", missing)}.";
        return OperationResult.Ok(new { prices = data, missing }, human);
    }
}
