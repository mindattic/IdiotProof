using System.Globalization;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using static IdiotProof.Operations.Options.OptionsDataReads;

namespace IdiotProof.Operations.Options;

public sealed record OptionsChainArgs
{
    [Argument(0, Name = "symbol", Description = "Underlying ticker, e.g. AAPL.")]
    public string Symbol { get; init; } = "";

    [Option("expiry", Description = "Expiration date (yyyy-MM-dd) to show; default the nearest one.")]
    public DateOnly? Expiry { get; init; }

    [Option("horizon", Description = "Days ahead to list expirations for (default 1098, about 3 years, so LEAPS show; Alpaca's own default is 1 week).")]
    public int Horizon { get; init; } = 1098;

    [Option("strikes", Description = "Strikes to show on each side of the spot price (default 10; 0 shows every strike).")]
    public int Strikes { get; init; } = 10;

    [Option("feed", Description = "Options data feed: indicative (default, free) or opra (paid).")]
    public OptionsFeed Feed { get; init; } = OptionsFeed.Indicative;
}

/// <summary>One side (call or put) of a chain row.</summary>
public sealed record ChainSide(string Occ, decimal? Bid, decimal? Ask, decimal? Last, decimal? Iv, decimal? Delta, long? OpenInterest);

/// <summary>One strike of the chain: the call and the put at that strike.</summary>
public sealed record ChainRow(decimal Strike, ChainSide? Call, ChainSide? Put);

/// <summary>
/// An underlying's option chain for one expiration: the contract catalog from
/// <c>GET /v2/options/contracts</c> (sent with an explicit expiration window, because Alpaca's default
/// is only one week) joined with the chain snapshots (bid/ask/last, IV, delta) and centred on spot.
/// </summary>
[Operation("options chain", Summary = "An underlying's option chain for one expiration: calls and puts by strike with bid/ask, IV, delta and open interest.", Risk = OperationRisk.Read)]
[Example("idiotproof options chain AAPL")]
[Example("idiotproof options chain SPY --expiry 2026-12-18 --strikes 5")]
public sealed class OptionsChainOperation : IOperation<OptionsChainArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsChainArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name an underlying, e.g. `idiotproof options chain AAPL`.");
        if (args.Horizon is < 1 or > 3660) return OperationResult.Usage("--horizon must be 1-3660 days.");
        if (args.Strikes < 0) return OperationResult.Usage("--strikes must be 0 or more.");

        var ct = ctx.CancellationToken;
        var today = DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        await using var api = await DataApiAsync(ctx);

        var query = Query(("underlying_symbols", symbol), ("status", "active"));
        if (args.Expiry is { } e) query["expiration_date"] = Date(e);
        else
        {
            query["expiration_date_gte"] = Date(today);
            query["expiration_date_lte"] = Date(today.AddDays(args.Horizon));
        }
        var contracts = (await ContractsAsync(api, query, 10_000, ct))
            .Select(c => (Json: c, Contract: OptionContract.ParseOcc(Str(c, "symbol"))))
            .Where(c => c.Contract is not null)
            .Select(c => (c.Json, Contract: c.Contract!))
            .ToList();
        if (contracts.Count == 0)
            return OperationResult.Fail(args.Expiry is { } none
                ? $"No active {symbol} contracts expire on {Date(none)}."
                : $"No active {symbol} option contracts in the next {args.Horizon} days.");

        var expirations = contracts.Select(c => c.Contract.Expiration).Distinct().Order().ToList();
        var expiry = args.Expiry ?? expirations[0];
        var forExpiry = contracts.Where(c => c.Contract.Expiration == expiry).ToList();

        string? quoteNote = null;
        var snapshots = new Dictionary<string, JsonElement>();
        try
        {
            snapshots = await ChainSnapshotsAsync(api, symbol, expiry, args.Feed, ct);
        }
        catch (AlpacaApiException ex)
        {
            quoteNote = $"Quotes unavailable: {ex.AlpacaMessage}";
        }
        var spot = await SpotAsync(api, symbol, ct);

        var rows = forExpiry
            .GroupBy(c => c.Contract.Strike)
            .OrderBy(g => g.Key)
            .Select(g => new ChainRow(
                g.Key,
                Side(g.FirstOrDefault(c => c.Contract.Right == OptionRight.Call), snapshots),
                Side(g.FirstOrDefault(c => c.Contract.Right == OptionRight.Put), snapshots)))
            .ToList();
        var shown = Window(rows, spot, args.Strikes);

        var dte = expiry.DayNumber - today.DayNumber;
        var header = $"{symbol} options expiring {Date(expiry)} ({dte} DTE)   spot {Money(spot)}   feed {FeedName(args.Feed)}";
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Call OI", Align.Right).Column("Delta", Align.Right).Column("IV", Align.Right).Column("Bid", Align.Right).Column("Ask", Align.Right)
            .Column("Strike", Align.Right)
            .Column("Bid", Align.Right).Column("Ask", Align.Right).Column("IV", Align.Right).Column("Delta", Align.Right).Column("Put OI", Align.Right);
        var spotRow = spot is { } s && shown.Count > 0
            ? shown.IndexOf(shown.MinBy(r => Math.Abs(r.Strike - s))!)
            : -1;
        for (var i = 0; i < shown.Count; i++)
        {
            var r = shown[i];
            var strike = Strike(r.Strike) + (i == spotRow ? " <" : "");
            table.Row(
                r.Call?.OpenInterest?.ToString("N0", CultureInfo.InvariantCulture) ?? "-", Greek(r.Call?.Delta, 2), Iv(r.Call?.Iv), Money(r.Call?.Bid), Money(r.Call?.Ask),
                strike,
                Money(r.Put?.Bid), Money(r.Put?.Ask), Iv(r.Put?.Iv), Greek(r.Put?.Delta, 2), r.Put?.OpenInterest?.ToString("N0", CultureInfo.InvariantCulture) ?? "-");
        }

        var lines = new List<string> { header, "CALLS on the left, PUTS on the right; '<' marks the strike nearest spot.", table.Render() };
        if (shown.Count < rows.Count) lines.Add($"Showing {shown.Count} of {rows.Count} strikes (--strikes 0 for all).");
        var listed = string.Join(", ", expirations.Take(12).Select(d => d.ToString("MM/dd/yy", CultureInfo.InvariantCulture)));
        lines.Add($"Expirations ({expirations.Count}): {listed}{(expirations.Count > 12 ? ", ..." : "")}");
        if (quoteNote is not null) lines.Add(quoteNote);

        var data = new
        {
            underlying = symbol,
            spot,
            expiry = Date(expiry),
            dte,
            feed = FeedName(args.Feed),
            expirations = expirations.Select(Date),
            totalStrikes = rows.Count,
            rows = shown,
            quoteNote,
        };
        return OperationResult.Ok(data, string.Join("\n", lines));
    }

    private static ChainSide? Side((JsonElement Json, OptionContract Contract) c, Dictionary<string, JsonElement> snapshots)
    {
        if (c.Contract is null) return null;
        var occ = c.Contract.OccSymbol;
        var oi = Dec(c.Json, "open_interest") is { } o ? (long?)o : null;
        if (!snapshots.TryGetValue(occ, out var snap)) return new ChainSide(occ, null, null, null, null, null, oi);
        var q = ParseSnapshot(occ, snap);
        return new ChainSide(occ, q.Bid, q.Ask, q.LastTrade, q.ImpliedVolatility, q.Greeks?.Delta, oi);
    }

    /// <summary><paramref name="perSide"/> strikes below and at-or-above spot; all when 0 or spot is unknown and the list is short.</summary>
    internal static List<ChainRow> Window(List<ChainRow> rows, decimal? spot, int perSide)
    {
        if (perSide == 0 || rows.Count <= perSide * 2) return rows;
        var pivot = spot is { } s ? rows.FindIndex(r => r.Strike >= s) : rows.Count / 2;
        if (pivot < 0) pivot = rows.Count;
        var start = Math.Max(0, pivot - perSide);
        var end = Math.Min(rows.Count, pivot + perSide);
        return rows[start..end];
    }
}
