using System.Globalization;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Orders;

public sealed record OrdersHistoryArgs
{
    [Option("days", Description = "How far back, in days (default 7, up to 365).")]
    public int Days { get; init; } = 7;

    [Option("symbol", Description = "Only orders for these symbols (comma-separated).")]
    public string? Symbol { get; init; }

    [Option("mode", Description = "paper, live or all (default all).")]
    public AccountScope Mode { get; init; } = AccountScope.All;
}

[Operation("orders history", Summary = "Closed Alpaca orders (filled, cancelled, expired, rejected) over the last N days, newest first, with fill prices and totals.", Risk = OperationRisk.Read)]
[Example("idiotproof orders history")]
[Example("idiotproof orders history --days 30 --symbol NVDA --mode paper")]
public sealed class OrdersHistoryOperation : IOperation<OrdersHistoryArgs>
{
    /// <summary>Alpaca's page cap for <c>/v2/orders</c>.</summary>
    private const int PageSize = 500;

    public async Task<OperationResult> ExecuteAsync(OrdersHistoryArgs args, OperationContext ctx)
    {
        if (args.Days is < 1 or > 365) return OperationResult.Usage("--days is 1 to 365.");
        var since = ctx.UtcNow.AddDays(-args.Days);
        var query = new Dictionary<string, string?>
        {
            ["status"] = "closed",
            ["after"] = since.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["limit"] = PageSize.ToString(CultureInfo.InvariantCulture),
            ["direction"] = "desc",
            ["nested"] = "true",
            ["symbols"] = string.IsNullOrWhiteSpace(args.Symbol) ? null : args.Symbol.Replace(" ", "").ToUpperInvariant(),
        };
        var reads = await OrderViews.ListAsync(ctx, args.Mode, query);

        var rows = new List<(string, OrderView)>();
        var notes = new List<string>();
        foreach (var r in reads)
        {
            if (r.Error is not null) { notes.Add($"{r.Name} account: {r.Error}"); continue; }
            if (r.Value!.Count >= PageSize) notes.Add($"{r.Name} account: showing the newest {PageSize}; narrow --days or --symbol for the rest.");
            rows.AddRange(r.Value.Select(o => (r.Name, o)));
        }
        rows.Sort((a, b) => Nullable.Compare(b.Item2.When, a.Item2.When));

        var filled = rows.Where(r => r.Item2.FilledQty is > 0).ToList();
        decimal Value(OrderView o) => (o.FilledQty ?? 0) * (o.FilledAvgPrice ?? 0);
        var bought = filled.Where(r => r.Item2.Side == "buy").Sum(r => Value(r.Item2));
        var sold = filled.Where(r => r.Item2.Side == "sell").Sum(r => Value(r.Item2));
        var byStatus = rows.GroupBy(r => r.Item2.Status).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} {g.Key}").ToList();

        var since0 = Fmt.EtDate(since);
        var body = rows.Count == 0
            ? $"No closed orders since {since0}."
            : $"{rows.Count} closed orders since {since0} ET: {string.Join(", ", byStatus)}; bought {Fmt.Money(bought)}, sold {Fmt.Money(sold)}\n"
              + OrderViews.Table(rows, ctx.Width, showAccount: reads.Count > 1, timeHeader: "Closed (ET)");
        var data = new
        {
            sinceUtc = since,
            days = args.Days,
            bought,
            sold,
            counts = rows.GroupBy(r => r.Item2.Status).ToDictionary(g => g.Key, g => g.Count()),
            accounts = reads.Select(r => new { account = r.Key, error = r.Error, orders = r.Value?.Select(o => o.ToData()).ToList() }),
        };
        return OperationResult.Ok(data, string.Join("\n", notes.Append(body)));
    }
}
