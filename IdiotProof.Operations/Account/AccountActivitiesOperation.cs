using System.Globalization;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Account;

public sealed record AccountActivitiesArgs
{
    [Option("since", Description = "First ET date to include, yyyy-MM-dd (default: 7 days ago).")]
    public DateOnly? Since { get; init; }

    [Option("until", Description = "Last ET date to include, yyyy-MM-dd (default: today).")]
    public DateOnly? Until { get; init; }

    [Option("type", Description = "Activity types, comma-separated: FILL, DIV, FEE, CSD (deposit), CSW (withdrawal), JNLC, INT, OPEXP, OPASN, ... (default: all).")]
    public string[]? Type { get; init; }

    [Option("limit", Description = "Most rows per account, newest first (default 100, max 1000).")]
    public int Limit { get; init; } = 100;

    [Option("mode", Description = "paper, live or all (default all).")]
    public AccountScope Mode { get; init; } = AccountScope.All;
}

/// <summary>
/// One account activity row. <see cref="TimeEt"/> is for people (a date only for activities Alpaca
/// dates but does not time); <see cref="TimeUtc"/> sorts and is midnight ET for date-only rows.
/// </summary>
public sealed record AccountActivityRow(
    string Account, string Id, string Type, string TimeEt, DateTime? TimeUtc, string? Symbol, string? Side,
    decimal? Qty, decimal? Price, decimal? Amount, string? Description);

/// <summary>
/// The account's activity log: fills, dividends, fees, deposits and withdrawals, interest, option
/// expirations and assignments. <c>GET /v2/account/activities</c> (or <c>/{type}</c> for one type),
/// newest first, paged by the last row's id.
/// </summary>
[Operation("account activities", Summary = "Account activity log: fills, dividends, fees, deposits, interest, option expirations/assignments (newest first, ET).", Risk = OperationRisk.Read)]
[Example("idiotproof account activities")]
[Example("idiotproof account activities --since 2026-10-01 --type FILL --mode paper")]
[Example("idiotproof account activities --type DIV,FEE,INT")]
public sealed class AccountActivitiesOperation : IOperation<AccountActivitiesArgs>
{
    /// <summary>Alpaca's largest page.</summary>
    public const int PageSize = 100;

    public async Task<OperationResult> ExecuteAsync(AccountActivitiesArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 1000) return OperationResult.Usage("--limit must be 1-1000.");
        var today = AccountApis.EtDate(ctx.UtcNow);
        var since = args.Since ?? today.AddDays(-7);
        var until = args.Until ?? today;
        if (until < since) return OperationResult.Usage("--until is before --since.");
        var types = (args.Type ?? []).Select(t => t.Trim().ToUpperInvariant()).Where(t => t.Length > 0).Distinct().ToList();
        if (types.Any(t => !t.All(char.IsLetter))) return OperationResult.Usage("--type takes activity codes like FILL, DIV, FEE.");

        var path = types.Count == 1 ? $"/v2/account/activities/{types[0]}" : "/v2/account/activities";
        var afterUtc = AccountApis.EtMidnightUtc(since);
        var untilUtc = AccountApis.EtMidnightUtc(until.AddDays(1));
        var reads = await AccountApis.ForEachAsync(ctx, args.Mode, api => FetchAsync(api, path, types, afterUtc, untilUtc, args.Limit, ctx.CancellationToken));

        var rows = new List<AccountActivityRow>();
        var errors = new List<object>();
        foreach (var r in reads)
        {
            if (r.Error is not null) { errors.Add(new { account = r.Key, error = r.Error }); continue; }
            rows.AddRange(r.Value!.Select(a => ToRow(r.Name, a)));
        }

        var head = $"Activities {since:yyyy-MM-dd} -> {until:yyyy-MM-dd} (ET)" + (types.Count > 0 ? $", {string.Join(",", types)}" : "");
        var blocks = new List<string> { head };
        if (rows.Count == 0) blocks.Add("No activity in that window.");
        else
        {
            var table = new TextTable { MaxWidth = ctx.Width }
                .Column("Time (ET)").Column("Account").Column("Type").Column("Symbol").Column("Side")
                .Column("Qty", Align.Right).Column("Price", Align.Right).Column("Amount", Align.Right).Column("Description", maxWidth: 40);
            foreach (var a in rows.OrderByDescending(a => a.TimeUtc))
                table.Row(a.TimeEt, a.Account, a.Type, a.Symbol ?? "", a.Side ?? "", a.Qty is { } q ? Fmt.Number(q) : "",
                    a.Price is { } p ? Fmt.Money(p, p < 1 ? 4 : 2) : "", AccountApis.Signed(a.Amount), a.Description ?? "");
            blocks.Add(table.Render());
        }
        foreach (var r in reads.Where(r => r.Error is not null)) blocks.Add($"{r.Name} account: {r.Error}");
        foreach (var r in reads.Where(r => r.Error is null && r.Value!.Count >= args.Limit))
            blocks.Add($"{r.Name}: showing the newest {args.Limit}; raise --limit or narrow --since for more.");

        var data = new { since = since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), until = until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), types, activities = rows, errors };
        return OperationResult.Ok(data, string.Join("\n\n", blocks));
    }

    /// <summary>Pages newest-first until <paramref name="limit"/> rows or the window runs out.</summary>
    internal static async Task<List<JsonElement>> FetchAsync(AlpacaApi api, string path, IReadOnlyList<string> types,
        DateTime afterUtc, DateTime untilUtc, int limit, CancellationToken ct)
    {
        var all = new List<JsonElement>();
        string? token = null;
        while (all.Count < limit)
        {
            var size = Math.Min(PageSize, limit - all.Count);
            var page = await api.GetAsync(path, new Dictionary<string, string?>
            {
                ["activity_types"] = types.Count > 1 ? string.Join(",", types) : null,
                ["after"] = afterUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ["until"] = untilUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ["direction"] = "desc",
                ["page_size"] = size.ToString(CultureInfo.InvariantCulture),
                ["page_token"] = token,
            }, ct);
            if (page.ValueKind != JsonValueKind.Array) break;
            var items = page.EnumerateArray().ToList();
            all.AddRange(items);
            if (items.Count < size) break;
            token = AccountApis.Str(items[^1], "id");
            if (token is null) break;
        }
        return all.Count > limit ? all[..limit] : all;
    }

    internal static AccountActivityRow ToRow(string account, JsonElement a)
    {
        var type = AccountApis.Str(a, "activity_type") ?? "?";
        var id = AccountApis.Str(a, "id") ?? "";
        var symbol = AccountApis.Str(a, "symbol");
        var qty = AccountApis.Dec(a, "qty");
        if (type == "FILL")
        {
            var price = AccountApis.Dec(a, "price");
            var side = AccountApis.Str(a, "side");
            var sign = side is "buy" ? -1m : 1m;
            var at = AccountApis.Instant(a, "transaction_time");
            var fill = AccountApis.Str(a, "type") is "partial_fill" ? "partial fill" : null;
            return new AccountActivityRow(account, id, type, at is { } t ? Fmt.Et(t) : "", at, symbol, side, qty, price,
                price * qty * sign, fill);
        }

        // Non-trade activities carry a date (sometimes a full timestamp) and a signed net_amount.
        var date = AccountApis.Str(a, "date");
        string when = "";
        DateTime? utc = null;
        if (date is { Length: > 10 } && AccountApis.Instant(a, "date") is { } dt) { when = Fmt.Et(dt); utc = dt; }
        else if (DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            when = date!;
            utc = AccountApis.EtMidnightUtc(d);
        }
        return new AccountActivityRow(account, id, type, when, utc, symbol, null, qty,
            AccountApis.Dec(a, "per_share_amount"), AccountApis.Dec(a, "net_amount"), AccountApis.Str(a, "description"));
    }
}
