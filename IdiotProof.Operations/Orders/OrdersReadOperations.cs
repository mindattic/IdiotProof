using System.Globalization;
using IdiotProof.Operations.Account;

namespace IdiotProof.Operations.Orders;

/// <summary>Which orders <c>orders list</c> asks Alpaca for (its <c>status</c> filter).</summary>
public enum OrderStatusFilter
{
    Open,
    Closed,
    All,
}

public sealed record OrdersListArgs
{
    [Option("status", Description = "open (default: working orders), closed (filled/cancelled/expired) or all.")]
    public OrderStatusFilter Status { get; init; } = OrderStatusFilter.Open;

    [Option("symbol", Description = "Only orders for these symbols (comma-separated).")]
    public string? Symbol { get; init; }

    [Option("side", Description = "Only buy or sell orders.")]
    public string? Side { get; init; }

    [Option("limit", Description = "Most orders per account (default 50, Alpaca's max 500).")]
    public int Limit { get; init; } = 50;

    [Option("mode", Description = "paper, live or all (default all).")]
    public AccountScope Mode { get; init; } = AccountScope.All;
}

[Operation("orders list", Summary = "Your Alpaca orders (open by default) in the paper and live accounts, newest first, with bracket/OCO legs.", Risk = OperationRisk.Read)]
[Example("idiotproof orders list")]
[Example("idiotproof orders list --status closed --symbol NVDA --mode paper")]
public sealed class OrdersListOperation : IOperation<OrdersListArgs>
{
    public async Task<OperationResult> ExecuteAsync(OrdersListArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 500) return OperationResult.Usage("--limit is 1 to 500.");
        var side = args.Side?.Trim().ToLowerInvariant();
        if (side is not (null or "buy" or "sell")) return OperationResult.Usage("--side is buy or sell.");

        var query = new Dictionary<string, string?>
        {
            ["status"] = args.Status.ToString().ToLowerInvariant(),
            ["limit"] = args.Limit.ToString(CultureInfo.InvariantCulture),
            ["direction"] = "desc",
            ["nested"] = "true",
            ["symbols"] = string.IsNullOrWhiteSpace(args.Symbol) ? null : args.Symbol.Replace(" ", "").ToUpperInvariant(),
            ["side"] = side,
        };
        var reads = await OrderViews.ListAsync(ctx, args.Mode, query);

        var rows = new List<(string, OrderView)>();
        var notes = new List<string>();
        foreach (var r in reads)
        {
            if (r.Error is not null) { notes.Add($"{r.Name} account: {r.Error}"); continue; }
            rows.AddRange(r.Value!.Select(o => (r.Name, o)));
        }
        rows.Sort((a, b) => Nullable.Compare(b.Item2.SubmittedUtc, a.Item2.SubmittedUtc));

        var data = reads.Select(r => new
        {
            account = r.Key,
            error = r.Error,
            orders = r.Value?.Select(o => o.ToData()).ToList(),
        });
        var what = args.Status switch { OrderStatusFilter.Open => "open orders", OrderStatusFilter.Closed => "closed orders", _ => "orders" };
        var body = rows.Count == 0
            ? $"No {what}" + (args.Symbol is null ? "" : $" for {args.Symbol.ToUpperInvariant()}") + "."
            : $"{rows.Count} {what}\n" + OrderViews.Table(rows, ctx.Width, showAccount: reads.Count > 1);
        return OperationResult.Ok(data, string.Join("\n", notes.Append(body)));
    }
}

public sealed record OrdersShowArgs
{
    [Argument(0, Name = "id", Description = "The Alpaca order id (from `orders list`).")]
    public string Id { get; init; } = "";

    [Option("mode", Description = "paper, live or all (default all: whichever account has it).")]
    public AccountScope Mode { get; init; } = AccountScope.All;
}

[Operation("orders show", Summary = "One Alpaca order in full: prices, fills, timestamps (ET) and its bracket/OCO/OTO legs.", Risk = OperationRisk.Read)]
[Example("idiotproof orders show 61e69015-8549-4bfd-b9c3-01e75843f47d")]
public sealed class OrdersShowOperation : IOperation<OrdersShowArgs>
{
    public async Task<OperationResult> ExecuteAsync(OrdersShowArgs args, OperationContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Id)) return OperationResult.Usage("Name the order id.");
        var (account, order, why) = await OrderViews.FindAsync(ctx, args.Mode, args.Id.Trim(), null);
        if (order is null) return OperationResult.Fail($"No order {args.Id.Trim()} ({why}).");
        return OperationResult.Ok(order.ToData(account!.ToLowerInvariant()), OrderViews.Detail(account, order, ctx.Width));
    }
}
