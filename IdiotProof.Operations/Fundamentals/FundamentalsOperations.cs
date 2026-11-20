using System.Globalization;
using IdiotProof.Core.Fundamentals;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Fundamentals;

public sealed record FundamentalsShowArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker symbol, e.g. MPU.", Example = "MPU")]
    public string Symbol { get; init; } = "";

    [Option("source", Description = "Vendor to ask: polygon, fmp or finviz-elite (default: the first configured).")]
    public string? Source { get; init; }

    [Option("refresh", Description = "Ignore today's cached read and fetch again (default: false).")]
    public bool Refresh { get; init; }
}

/// <summary>
/// One symbol's float, short interest and shares outstanding from the configured vendor (IP-T603),
/// with each value's as-of date and the source. Cached once per ET day per source; read-only.
/// </summary>
[Operation("fundamentals show", Summary = "Float, short interest (% of float, days to cover) and shares outstanding for a symbol, with as-of dates and source.", Risk = OperationRisk.Read)]
[Example("idiotproof fundamentals show MPU")]
[Example("idiotproof fundamentals show MPU --source polygon --refresh")]
public sealed class FundamentalsShowOperation : IOperation<FundamentalsShowArgs>
{
    public async Task<OperationResult> ExecuteAsync(FundamentalsShowArgs args, OperationContext ctx)
    {
        var symbol = (args.Symbol ?? "").Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a ticker, e.g. `idiotproof fundamentals show MPU`.");
        if (!FundamentalsService.IsValidSymbol(symbol)) return OperationResult.Usage($"Not a ticker symbol: {symbol}");

        var service = ctx.Get<FundamentalsService>();
        if (args.Source is { Length: > 0 } && service.Find(args.Source) is null)
            return OperationResult.Usage($"--source must be one of: {string.Join(", ", service.Sources.Select(s => s.Name))}.");

        var r = await service.GetAsync(symbol, args.Source, args.Refresh, ctx.CancellationToken);
        var s = r.Snapshot;
        var data = new
        {
            symbol,
            source = r.Source,
            configured = r.Source is not null && service.Find(r.Source) is { IsConfigured: true },
            from_cache = r.FromCache,
            error = r.Error,
            snapshot = s,
        };
        if (r.Error is not null)
        {
            // "not configured" is a normal state (no vendor key yet), not a failure: callers like the journal leave the columns blank.
            var chosen = args.Source is { Length: > 0 } ? service.Find(args.Source) : service.Primary;
            return chosen?.IsConfigured == true ? OperationResult.Fail(r.Error, data) : OperationResult.Ok(data, r.Error);
        }
        if (s is null) return OperationResult.Ok(data, $"{symbol}: {r.Source} does not know this symbol.");

        var block = new KeyValueBlock()
            .Add("Symbol", symbol)
            .Add("Source", $"{r.Source}{(r.FromCache ? " (cached today)" : "")}, fetched {ctx.Et(s.FetchedUtc)}")
            .Add("Shares outstanding", With(Shares(s.SharesOutstanding), s.SharesOutstandingAsOf))
            .Add("Float", With(Shares(s.FloatShares) + (s.FloatPercentOfOutstanding is { } fp ? $" ({Pct(fp)} of outstanding)" : ""), s.FloatAsOf))
            .Add("Short interest", With(Shares(s.ShortInterest), s.ShortInterestAsOf))
            .Add("Short % of float", s.ShortPercentOfFloat is { } sp ? Pct(sp) : "-")
            .Add("Days to cover", s.DaysToCover is { } d ? d.ToString("0.##", CultureInfo.InvariantCulture) : "-");
        var lines = new List<string> { block.Render() };
        if (s.Notes.Count > 0) lines.Add("Notes: " + string.Join(" ", s.Notes));
        lines.Add("Vendor values are current reads, not point-in-time history: float is a candidate study feature, not a validated one.");
        return OperationResult.Ok(data, string.Join("\n", lines));
    }

    internal static string Shares(long? v) => v switch
    {
        null => "-",
        >= 1_000_000_000 => (v.Value / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + "B",
        >= 1_000_000 => (v.Value / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (v.Value / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + "k",
        _ => v.Value.ToString(CultureInfo.InvariantCulture),
    };

    private static string Pct(double p) => p.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string With(string value, DateOnly? asOf) =>
        value.StartsWith('-') ? "-" : asOf is { } a ? $"{value} (as of {a:yyyy-MM-dd})" : value;
}

/// <summary>Which fundamentals vendors are on (key in Vault) and which one answers by default. Never shows a key.</summary>
[Operation("fundamentals status", Summary = "Which float/short-interest vendors (Polygon, FMP, Finviz Elite) have a key in Vault and which one answers (never shows a key).", Risk = OperationRisk.Read)]
[Example("idiotproof fundamentals status")]
public sealed class FundamentalsStatusOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var service = ctx.Get<FundamentalsService>();
        var primary = service.Primary;
        var rows = service.Sources.Select(s => new
        {
            source = s.Name,
            vendor = s.DisplayName,
            vault_entry = s.VaultEntry,
            configured = s.IsConfigured,
            status = s.Status,
            provides = s.Provides,
        }).ToList();
        var data = new
        {
            configured = primary is not null,
            primary = primary?.Name,
            sources = rows,
            cache_directory = service.CacheDirectory,
            tokens_file = service.Keys?.TokensFile,
        };

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("source").Column("vendor").Column("status", maxWidth: 40).Column("provides", maxWidth: 60);
        foreach (var r in rows) table.Row(r.source, r.vendor, r.status, r.provides);
        var lines = new List<string>
        {
            primary is null
                ? "No fundamentals vendor is configured: every source is off until its key is in Vault."
                : $"Answering: {primary.Name} ({primary.DisplayName}). Order: {string.Join(", ", service.Sources.Select(s => s.Name))} (config IdiotProof:Fundamentals:Order).",
            "",
            table.Render(),
            "",
            $"Keys: MindAttic Vault Tokens bucket{(service.Keys?.TokensFile is { } f ? $" ({f})" : "")}, entry names above; or config MindAttic:Vault:Tokens:<entry>.",
            $"Cache: {service.CacheDirectory} (one fetch per symbol per source per ET day).",
        };
        return Task.FromResult(OperationResult.Ok(data, string.Join("\n", lines)));
    }
}
