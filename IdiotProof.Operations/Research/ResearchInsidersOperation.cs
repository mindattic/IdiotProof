using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Research;

public sealed record ResearchInsidersArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker symbol, e.g. NVDA.")]
    public string Symbol { get; init; } = "";

    [Option("days", Description = "Transactions dated within this many days, 1-3650 (default 180).")]
    public int Days { get; init; } = 180;

    [Option("code", Description = "SEC transaction codes, comma-separated: P purchase, S sale, A award, M exercise, F tax, G gift (default: all).")]
    public string[]? Code { get; init; }

    [Option("limit", Description = "Most lines to show, 1-1000 (default 100).")]
    public int Limit { get; init; } = 100;
}

/// <summary>
/// Form 4 insider transactions for one ticker, parsed from the filing XML by the scan pass: who
/// (officer, director, 10% owner) bought or sold how many shares at what price, with open-market
/// purchase and sale totals. Newest first.
/// </summary>
[Operation("research insiders", Summary = "Form 4 insider transactions for a ticker: filer, role, buy/sell, shares, price, value.", Risk = OperationRisk.Read)]
[Example("idiotproof research insiders NVDA")]
[Example("idiotproof research insiders TSLA --code P,S --days 365")]
public sealed class ResearchInsidersOperation : IOperation<ResearchInsidersArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchInsidersArgs args, OperationContext ctx)
    {
        var symbol = (args.Symbol ?? "").Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a ticker, e.g. `idiotproof research insiders NVDA`.");
        if (args.Days is < 1 or > 3650) return OperationResult.Usage("--days must be 1-3650.");
        if (args.Limit is < 1 or > 1000) return OperationResult.Usage("--limit must be 1-1000.");
        var codes = (args.Code ?? []).Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        if (codes.Any(c => c.Length > 2 || !c.All(char.IsLetter))) return OperationResult.Usage("--code takes SEC transaction codes like P,S,A,M,F,G.");

        var fromDate = Account.AccountApis.EtDate(ctx.UtcNow).AddDays(-args.Days);
        await using var db = await ResearchSupport.Db(ctx).CreateDbContextAsync(ctx.CancellationToken);
        var q = from t in db.InsiderTransactions.AsNoTracking()
                join c in db.ResearchClaims.AsNoTracking() on t.ClaimId equals c.Id
                where c.Ticker == symbol && t.TransactionDate >= fromDate
                select t;
        if (codes.Count > 0) q = q.Where(t => codes.Contains(t.TransactionCode));
        var lines = await q.OrderByDescending(t => t.TransactionDate).ThenBy(t => t.FilerName)
            .Take(args.Limit).ToListAsync(ctx.CancellationToken);

        var rows = lines.Select(InsiderRow.From).ToList();
        var bought = lines.Where(t => t.TransactionCode == "P").ToList();
        var sold = lines.Where(t => t.TransactionCode == "S").ToList();
        var totals = new
        {
            Purchases = bought.Count,
            PurchaseShares = bought.Sum(t => t.SharesTransacted),
            PurchaseValue = bought.Sum(t => t.DollarValue ?? 0),
            Sales = sold.Count,
            SaleShares = sold.Sum(t => t.SharesTransacted),
            SaleValue = sold.Sum(t => t.DollarValue ?? 0),
            Filers = lines.Select(t => t.FilerName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        };
        var data = new { Symbol = symbol, Since = fromDate, Codes = codes, Totals = totals, Transactions = rows };

        var head = $"{symbol} insider transactions since {fromDate:yyyy-MM-dd}" + (codes.Count > 0 ? $" ({string.Join(",", codes)})" : "");
        if (rows.Count == 0)
            return OperationResult.Ok(data, $"{head}\nNo Form 4 lines on file. `idiotproof research scan --tickers {symbol} --days-back 30` pulls recent filings.");

        var net = totals.PurchaseValue - totals.SaleValue;
        var summary = $"{rows.Count} lines from {totals.Filers} filers. Open-market: {totals.Purchases} buys {Fmt.Money(totals.PurchaseValue, 0)}, "
            + $"{totals.Sales} sells {Fmt.Money(totals.SaleValue, 0)} (net {Fmt.SignedMoney(net, 0)}).";
        return OperationResult.Ok(data, $"{head}\n{InsiderRow.Table(rows, ctx.Width)}\n{summary}");
    }
}
