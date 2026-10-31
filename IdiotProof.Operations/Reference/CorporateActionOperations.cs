using System.Globalization;
using System.Net;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Reference;

/// <summary>Which Alpaca corporate-actions feed to read.</summary>
public enum CorporateActionSource
{
    /// <summary>Market Data <c>/v1/corporate-actions</c>: every type, many symbols, paged.</summary>
    Data,

    /// <summary>Trading API <c>/v2/corporate_actions/announcements</c>: Dividend, Merger, Spinoff, Split.</summary>
    Announcements,
}

public sealed record CorporateActionsListArgs
{
    [Option("types", Description = "Types, comma separated. data: forward_split, reverse_split, unit_split, cash_dividend, stock_dividend, spin_off, cash_merger, stock_merger, stock_and_cash_merger, redemption, name_change, worthless_removal, rights_distribution. announcements: dividend, merger, spinoff, split.")]
    public string[] Types { get; init; } = [];

    [Option("symbol", Description = "Only these symbols, comma separated.")]
    public string[] Symbol { get; init; } = [];

    [Option("since", Description = "First date (default 30 days ago, ET).")]
    public DateOnly? Since { get; init; }

    [Option("until", Description = "Last date (default 30 days ahead, ET).")]
    public DateOnly? Until { get; init; }

    [Option("source", Description = "data (Market Data, default) or announcements (Trading API, max 90-day window).")]
    public CorporateActionSource Source { get; init; } = CorporateActionSource.Data;

    [Option("limit", Description = "Rows to show (default 100; the totals line counts them all).")]
    public int Limit { get; init; } = 100;

    [Option("mode", Description = "Alpaca account for --source announcements: paper (default) or live.")]
    public AlpacaAccount Mode { get; init; } = AlpacaAccount.Paper;
}

/// <summary>Flattens both corporate-action shapes into one row model.</summary>
internal sealed record CorporateActionRow(string Type, string Symbol, string Date, string? Record, string? Payable, string Detail, string? Id, JsonElement Raw);

internal static class CorporateActions
{
    private const int MaxPages = 50;

    public static async Task<List<CorporateActionRow>> FromDataAsync(AlpacaApi api, IReadOnlyDictionary<string, string?> query, CancellationToken ct)
    {
        var rows = new List<CorporateActionRow>();
        var q = new Dictionary<string, string?>(query) { ["limit"] = "1000", ["sort"] = "asc" };
        for (int page = 0; page < MaxPages; page++)
        {
            var root = await api.GetDataAsync("/v1/corporate-actions", q, ct);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("corporate_actions", out var groups) && groups.ValueKind == JsonValueKind.Object)
                foreach (var group in groups.EnumerateObject())
                    foreach (var item in group.Value.Items())
                        rows.Add(FromData(group.Name, item));
            var token = root.Str("next_page_token");
            if (string.IsNullOrEmpty(token)) break;
            q["page_token"] = token;
        }
        return rows;
    }

    /// <summary>"cash_dividends" → "cash_dividend"; picks the key date and a one-line detail.</summary>
    public static CorporateActionRow FromData(string group, JsonElement e)
    {
        var type = group.EndsWith('s') ? group[..^1] : group;
        var symbol = e.Str("symbol") ?? e.Str("acquiree_symbol") ?? e.Str("source_symbol") ?? e.Str("old_symbol") ?? e.Str("new_symbol") ?? "-";
        var date = e.Str("ex_date") ?? e.Str("effective_date") ?? e.Str("process_date") ?? "-";
        var parts = new List<string>();
        if (e.Dec("rate") is { } rate) parts.Add(type.Contains("dividend", StringComparison.Ordinal) && type.StartsWith("cash", StringComparison.Ordinal) ? $"{Fmt.Money(rate, 4)}/sh" : $"rate {Fmt.Number(rate, 6)}");
        if (e.Dec("new_rate") is { } nr && e.Dec("old_rate") is { } od) parts.Add($"{Fmt.Number(nr, 6)}:{Fmt.Number(od, 6)}");
        if (e.Bool("special") == true) parts.Add("special");
        if (e.Str("acquirer_symbol") is { } acq) parts.Add($"-> {acq}" + (e.Dec("acquirer_rate") is { } ar ? $" x{Fmt.Number(ar, 6)}" : ""));
        if (e.Dec("cash_rate") is { } cr) parts.Add($"{Fmt.Money(cr, 4)} cash/sh");
        if (type == "spin_off" && e.Str("new_symbol") is { } spun) parts.Add($"spins {spun}" + (e.Dec("new_rate") is { } sr ? $" x{Fmt.Number(sr, 6)}" : ""));
        if (type == "name_change" && e.Str("new_symbol") is { } ns) parts.Add($"{e.Str("old_symbol")} -> {ns}");
        return new CorporateActionRow(type, symbol, date, e.Str("record_date"), e.Str("payable_date"), string.Join(", ", parts), e.Str("id"), e);
    }

    public static CorporateActionRow FromAnnouncement(JsonElement e)
    {
        var type = (e.Str("ca_type") ?? "-") + (e.Str("ca_sub_type") is { } sub && sub != e.Str("ca_type") ? "/" + sub : "");
        var symbol = e.Str("initiating_symbol") ?? e.Str("target_symbol") ?? "-";
        var parts = new List<string>();
        if (e.Dec("cash") is { } cash && cash != 0) parts.Add($"{Fmt.Money(cash, 4)}/sh");
        if (e.Dec("new_rate") is { } nr && e.Dec("old_rate") is { } od && !(nr == 1 && od == 1)) parts.Add($"{Fmt.Number(nr, 6)}:{Fmt.Number(od, 6)}");
        if (e.Str("target_symbol") is { } target && target != symbol) parts.Add($"target {target}");
        return new CorporateActionRow(type, symbol, e.Str("ex_date") ?? "-", e.Str("record_date"), e.Str("payable_date"), string.Join(", ", parts), e.Str("id"), e);
    }

    /// <summary>Every scalar field of a corporate action, labelled "Ex date: 2026-11-05".</summary>
    public static string Describe(JsonElement e)
    {
        var block = new KeyValueBlock();
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                var value = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => p.Value.GetRawText(),
                    _ => null,
                };
                if (string.IsNullOrEmpty(value)) continue;
                var label = p.Name.Replace('_', ' ');
                block.Add(char.ToUpperInvariant(label[0]) + label[1..], value);
            }
        return block.Render();
    }
}

[Operation("corporate-actions list", Summary = "Splits, dividends, mergers, spinoffs and name changes from Alpaca (Market Data or Trading API announcements) over a date window.", Risk = OperationRisk.Read)]
[Example("idiotproof corporate-actions list --symbol AAPL,MSFT --types cash_dividend")]
[Example("idiotproof corporate-actions list --types forward_split,reverse_split --since 2026-10-01 --until 2026-11-30")]
[Example("idiotproof corporate-actions list --source announcements --types split --symbol NVDA")]
public sealed class CorporateActionsListOperation : IOperation<CorporateActionsListArgs>
{
    private static readonly string[] AnnouncementTypes = ["Dividend", "Merger", "Spinoff", "Split"];

    public async Task<OperationResult> ExecuteAsync(CorporateActionsListArgs args, OperationContext ctx)
    {
        if (args.Limit < 1) return OperationResult.Usage("--limit must be at least 1.");
        var today = DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        var since = args.Since ?? today.AddDays(-30);
        var until = args.Until ?? today.AddDays(30);
        if (until < since) return OperationResult.Usage("--until is before --since.");
        var symbols = ReferenceSupport.Csv(args.Symbol).ToUpperInvariant();
        var types = ReferenceSupport.Csv(args.Types);
        string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        List<CorporateActionRow> rows;
        if (args.Source == CorporateActionSource.Data)
        {
            await using var api = await ReferenceSupport.DataAsync(ctx);
            rows = await CorporateActions.FromDataAsync(api, new Dictionary<string, string?>
            {
                ["symbols"] = symbols,
                ["types"] = types.ToLowerInvariant(),
                ["start"] = Iso(since),
                ["end"] = Iso(until),
            }, ctx.CancellationToken);
        }
        else
        {
            if (until.DayNumber - since.DayNumber > 90)
                return OperationResult.Usage("--source announcements allows at most a 90-day window between --since and --until.");
            var caTypes = types.Length == 0
                ? AnnouncementTypes
                : types.Split(',').Select(t => AnnouncementTypes.FirstOrDefault(a => string.Equals(a, t.Replace("_", ""), StringComparison.OrdinalIgnoreCase))).ToArray();
            if (caTypes.Any(t => t is null))
                return OperationResult.Usage("--source announcements takes --types dividend, merger, spinoff or split.");
            await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
            rows = [];
            foreach (var symbol in symbols.Length == 0 ? new string?[] { null } : symbols.Split(','))
            {
                var root = await api.GetAsync("/v2/corporate_actions/announcements", new Dictionary<string, string?>
                {
                    ["ca_types"] = string.Join(",", caTypes),
                    ["since"] = Iso(since),
                    ["until"] = Iso(until),
                    ["symbol"] = symbol,
                }, ctx.CancellationToken);
                rows.AddRange(root.Items().Select(CorporateActions.FromAnnouncement));
            }
        }

        rows = [.. rows.OrderBy(r => r.Date, StringComparer.Ordinal).ThenBy(r => r.Symbol, StringComparer.Ordinal)];
        var shown = rows.Take(args.Limit).ToList();
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Date").Column("Symbol").Column("Type").Column("Detail", maxWidth: 40).Column("Record").Column("Payable").Column("Id");
        foreach (var r in shown) table.Row(r.Date, r.Symbol, r.Type, r.Detail, r.Record ?? "-", r.Payable ?? "-", r.Id ?? "-");

        var byType = rows.GroupBy(r => r.Type).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
        var header = $"{(args.Source == CorporateActionSource.Data ? "Market Data" : "Announcements")} {Iso(since)} to {Iso(until)}";
        var totals = string.Create(CultureInfo.InvariantCulture, $"Showing {shown.Count:N0} of {rows.Count:N0}") +
                     (rows.Count == 0 ? "." : $" ({string.Join(", ", byType)}).");
        var human = shown.Count == 0 ? $"{header}: no corporate actions." : $"{header}\n{table.Render()}\n{totals}";
        return OperationResult.Ok(new
        {
            source = OperationParameter.EnumWireName(args.Source),
            since = Iso(since),
            until = Iso(until),
            total = rows.Count,
            shown = shown.Count,
            actions = shown.Select(r => new { r.Type, r.Symbol, r.Date, recordDate = r.Record, payableDate = r.Payable, r.Detail, r.Id, raw = r.Raw }),
        }, human);
    }
}

public sealed record CorporateActionsShowArgs
{
    [Argument(0, Name = "id", Description = "Corporate action id (from corporate-actions list).")]
    public string Id { get; init; } = "";

    [Option("mode", Description = "Alpaca account for the announcements lookup: paper (default) or live.")]
    public AlpacaAccount Mode { get; init; } = AlpacaAccount.Paper;
}

[Operation("corporate-actions show", Summary = "Every field of one Alpaca corporate action, looked up in Market Data and then Trading API announcements.", Risk = OperationRisk.Read)]
[Example("idiotproof corporate-actions show 6a4e1c55-1f0e-4c8e-9a1b-2b1fbd7a4f10")]
public sealed class CorporateActionsShowOperation : IOperation<CorporateActionsShowArgs>
{
    public async Task<OperationResult> ExecuteAsync(CorporateActionsShowArgs args, OperationContext ctx)
    {
        var id = args.Id.Trim();
        if (id.Length == 0) return OperationResult.Usage("Name a corporate action id (see idiotproof corporate-actions list).");

        await using (var data = await ReferenceSupport.DataAsync(ctx))
        {
            try
            {
                var rows = await CorporateActions.FromDataAsync(data, new Dictionary<string, string?> { ["ids"] = id }, ctx.CancellationToken);
                if (rows.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) is { } hit)
                    return Render(hit, "market data");
            }
            catch (AlpacaApiException ex) when (ex.Status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            {
                // Not a Market Data id; fall through to the Trading API announcements.
            }
        }

        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        try
        {
            var e = await api.GetAsync("/v2/corporate_actions/announcements/" + Uri.EscapeDataString(id), null, ctx.CancellationToken);
            return Render(CorporateActions.FromAnnouncement(e), "announcements");
        }
        catch (AlpacaApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
        {
            return OperationResult.Fail($"No corporate action with id {id} in Market Data or announcements ({ex.AlpacaMessage}).");
        }
    }

    private static OperationResult Render(CorporateActionRow row, string source) =>
        OperationResult.Ok(new { source, row.Type, row.Symbol, row.Date, row.Detail, row.Id, raw = row.Raw },
            $"{row.Type}  {row.Symbol}  {row.Date}{(row.Detail.Length > 0 ? "  " + row.Detail : "")}  ({source})\n{CorporateActions.Describe(row.Raw)}");
}
