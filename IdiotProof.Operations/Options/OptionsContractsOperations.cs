using System.Globalization;
using System.Text.Json;
using IdiotProof.Operations.Rendering;
using static IdiotProof.Operations.Options.OptionsDataReads;

namespace IdiotProof.Operations.Options;

/// <summary>Call or put, as Alpaca's contracts filter takes it.</summary>
public enum OptionsContractType
{
    Call,
    Put,
}

/// <summary>Listed (active) or expired/delisted (inactive) contracts.</summary>
public enum OptionsContractStatus
{
    Active,
    Inactive,
}

public sealed record OptionsContractsArgs
{
    [Argument(0, Name = "underlying", Description = "Underlying ticker(s), comma separated, e.g. AAPL or AAPL,MSFT.")]
    public string Underlying { get; init; } = "";

    [Option("expiry", Description = "Only this expiration date (yyyy-MM-dd).")]
    public DateOnly? Expiry { get; init; }

    [Option("type", Description = "Only calls or only puts.")]
    public OptionsContractType? Type { get; init; }

    [Option("strike-min", Description = "Lowest strike to include.")]
    public decimal? StrikeMin { get; init; }

    [Option("strike-max", Description = "Highest strike to include.")]
    public decimal? StrikeMax { get; init; }

    [Option("status", Description = "active (default) or inactive.")]
    public OptionsContractStatus Status { get; init; } = OptionsContractStatus.Active;

    [Option("horizon", Description = "Without --expiry: days ahead to include (default 1098; Alpaca's own default is only 1 week).")]
    public int Horizon { get; init; } = 1098;

    [Option("limit", Description = "Most contracts to return (default 200, max 10000).")]
    public int Limit { get; init; } = 200;
}

/// <summary>One contract from the catalog, as the JSON payload carries it.</summary>
public sealed record OptionContractRow(
    string Id, string Symbol, string? Name, string? Status, bool? Tradable, string Underlying, string? Type, string? Style,
    decimal? Strike, string? Expiration, decimal? Size, decimal? OpenInterest, string? OpenInterestDate,
    decimal? ClosePrice, string? ClosePriceDate)
{
    /// <summary>Projects one element of Alpaca's <c>option_contracts</c>.</summary>
    public static OptionContractRow From(JsonElement c) => new(
        Str(c, "id") ?? "", Str(c, "symbol") ?? "", Str(c, "name"), Str(c, "status"), Bool(c, "tradable"),
        Str(c, "underlying_symbol") ?? Str(c, "root_symbol") ?? "", Str(c, "type"), Str(c, "style"),
        Dec(c, "strike_price"), Str(c, "expiration_date"), Dec(c, "size"), Dec(c, "open_interest"), Str(c, "open_interest_date"),
        Dec(c, "close_price"), Str(c, "close_price_date"));
}

/// <summary>Search the options contract catalog (<c>GET /v2/options/contracts</c>) with every filter Alpaca offers.</summary>
[Operation("options contracts", Summary = "Search listed option contracts for an underlying by expiry, type, strike range and status.", Risk = OperationRisk.Read)]
[Example("idiotproof options contracts AAPL --expiry 2026-11-20 --type call")]
[Example("idiotproof options contracts SPY --strike-min 600 --strike-max 650 --limit 50")]
public sealed class OptionsContractsOperation : IOperation<OptionsContractsArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsContractsArgs args, OperationContext ctx)
    {
        var underlying = string.Join(',', args.Underlying.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()));
        if (underlying.Length == 0) return OperationResult.Usage("Name an underlying, e.g. `idiotproof options contracts AAPL`.");
        if (args.Limit is < 1 or > 10_000) return OperationResult.Usage("--limit must be 1-10000.");
        if (args.Horizon is < 1 or > 3660) return OperationResult.Usage("--horizon must be 1-3660 days.");
        if (args.StrikeMin is { } lo && args.StrikeMax is { } hi && lo > hi) return OperationResult.Usage("--strike-min is above --strike-max.");

        var inv = CultureInfo.InvariantCulture;
        var today = DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        var query = Query(
            ("underlying_symbols", underlying),
            ("status", args.Status == OptionsContractStatus.Active ? "active" : "inactive"),
            ("type", args.Type is { } t ? t == OptionsContractType.Call ? "call" : "put" : null),
            ("strike_price_gte", args.StrikeMin?.ToString(inv)),
            ("strike_price_lte", args.StrikeMax?.ToString(inv)));
        if (args.Expiry is { } e) query["expiration_date"] = Date(e);
        else if (args.Status == OptionsContractStatus.Active)
        {
            query["expiration_date_gte"] = Date(today);
            query["expiration_date_lte"] = Date(today.AddDays(args.Horizon));
        }

        await using var api = await DataApiAsync(ctx);
        var rows = (await ContractsAsync(api, query, args.Limit, ctx.CancellationToken)).Select(OptionContractRow.From).ToList();
        if (rows.Count == 0) return OperationResult.Ok(new { underlying, contracts = rows }, $"No {underlying} contracts match.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Symbol").Column("Type").Column("Strike", Align.Right).Column("Expiry").Column("Style")
            .Column("Open int.", Align.Right).Column("Close", Align.Right).Column("Tradable");
        foreach (var r in rows)
            table.Row(r.Symbol, r.Type ?? "-", r.Strike is { } k ? Strike(k) : "-", r.Expiration ?? "-", r.Style ?? "-",
                r.OpenInterest is { } oi ? Fmt.Number(oi) : "-", Money(r.ClosePrice), r.Tradable switch { true => "yes", false => "no", _ => "-" });
        var footer = rows.Count == args.Limit ? $"\n{rows.Count} contracts (the --limit; raise it or narrow the filters for more)." : $"\n{rows.Count} contracts.";
        return OperationResult.Ok(new { underlying, contracts = rows }, table.Render() + footer);
    }
}

public sealed record OptionsContractArgs
{
    [Argument(0, Name = "contract", Description = "OCC symbol (e.g. AAPL261120C00250000) or Alpaca contract id.")]
    public string Contract { get; init; } = "";
}

/// <summary>One contract's full catalog record (<c>GET /v2/options/contracts/{symbol_or_id}</c>).</summary>
[Operation("options contract", Summary = "Full catalog details of one option contract by OCC symbol or Alpaca id.", Risk = OperationRisk.Read)]
[Example("idiotproof options contract AAPL261120C00250000")]
public sealed class OptionsContractOperation : IOperation<OptionsContractArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsContractArgs args, OperationContext ctx)
    {
        var key = args.Contract.Trim();
        if (key.Length == 0) return OperationResult.Usage("Name a contract, e.g. `idiotproof options contract AAPL261120C00250000`.");
        if (!Guid.TryParse(key, out _)) key = key.ToUpperInvariant();

        await using var api = await DataApiAsync(ctx);
        var c = await api.GetAsync($"/v2/options/contracts/{Uri.EscapeDataString(key)}", null, ctx.CancellationToken);
        var r = OptionContractRow.From(c);
        var block = new KeyValueBlock { Ansi = Ansi.For(ctx) }
            .Add("Symbol", r.Symbol)
            .Add("Name", r.Name)
            .Add("Id", r.Id)
            .Add("Underlying", r.Underlying)
            .Add("Type", r.Type)
            .Add("Style", r.Style)
            .Add("Strike", r.Strike is { } k ? Money(k) : null)
            .Add("Expiration", r.Expiration)
            .Add("Size", r.Size is { } s ? Fmt.Number(s) + " shares" : null)
            .Add("Status", r.Status)
            .Add("Tradable", r.Tradable switch { true => "yes", false => "no", _ => null })
            .Add("Open interest", r.OpenInterest is { } oi ? $"{Fmt.Number(oi)} (as of {r.OpenInterestDate ?? "-"})" : null)
            .Add("Close price", r.ClosePrice is { } cp ? $"{Money(cp)} (on {r.ClosePriceDate ?? "-"})" : null);
        return OperationResult.Ok(r, block.Render());
    }
}
