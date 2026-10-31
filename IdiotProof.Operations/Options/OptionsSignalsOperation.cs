using System.Globalization;
using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Brokers;
using IdiotProof.Models;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;
using IdiotProof.Shared.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IdiotProof.Operations.Options.OptionsDataReads;

namespace IdiotProof.Operations.Options;

/// <summary>One open long option position measured against the sell-the-hype rule.</summary>
public sealed record OptionSignalRow(
    string Account, string Symbol, string Underlying, decimal Quantity, decimal? Price, decimal? Spot,
    decimal? ExtrinsicPerShare, decimal? ExtrinsicPercent, decimal? ExtrinsicHigh, int Samples,
    int BullishClaims, bool Nudge, string Verdict, string? Message);

/// <summary>
/// The Options page's "consider taking profit" nudge for every OPEN LONG option, computed the same
/// way (<see cref="SellSignalEvaluator"/>): hype (extrinsic value) within 5% of its high for the
/// session AND a bullish research claim on the underlying in the last 7 days. The page builds its
/// hype history from its own 20-second refreshes; a one-shot command can't, so the history here is
/// today's 5-minute option bars, each minus its intrinsic value at the underlying's close of the
/// same bar. Informational only: it never places, sizes or suggests a specific order.
/// </summary>
[Operation("options signals", Summary = "Sell-the-hype nudges on your open long options: hype near its session high plus bullish news (never places anything).", Risk = OperationRisk.Read)]
[Example("idiotproof options signals")]
[Example("idiotproof options signals --mode paper")]
public sealed class OptionsSignalsOperation : IOperation<AccountScopeArgs>
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<OperationResult> ExecuteAsync(AccountScopeArgs args, OperationContext ctx)
    {
        var reads = await PerAccountAsync(ctx, args.Mode, (api, ct) => api.GetAsync("/v2/positions", null, ct));
        var now = ctx.UtcNow;
        var rows = new List<OptionSignalRow>();
        var notes = new List<string>();
        var longs = new List<(string Account, JsonElement Position, OptionContract Contract)>();
        foreach (var r in reads)
        {
            if (r.Error is not null) { notes.Add($"{(r.Account == "paper" ? "Paper" : "Live")} account: {r.Error}"); continue; }
            if (r.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var p in r.Value.EnumerateArray())
            {
                if (!string.Equals(Str(p, "asset_class"), "us_option", StringComparison.OrdinalIgnoreCase)) continue;
                if ((Dec(p, "qty") ?? 0m) <= 0m) continue; // the nudge is for longs: a written option has no hype to sell
                if (OptionContract.ParseOcc(Str(p, "symbol") ?? "") is { } c) longs.Add((r.Account, p, c));
            }
        }

        if (longs.Count == 0)
        {
            var none = "No open long option positions, so there is nothing to nudge." + (notes.Count > 0 ? "\n" + string.Join("\n", notes) : "");
            return OperationResult.Ok(new { signals = rows, notes }, none);
        }

        var claimsByTicker = await BullishClaimsAsync(ctx, longs.Select(l => l.Contract.UnderlyingSymbol).Distinct(StringComparer.OrdinalIgnoreCase), now);
        await using var data = await DataApiAsync(ctx);
        var today = DateOnly.FromDateTime(EasternClock.ToEastern(now));
        var spotCache = new Dictionary<string, (decimal? Spot, Dictionary<DateTime, decimal> Closes)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (account, p, c) in longs)
        {
            var underlying = c.UnderlyingSymbol.ToUpperInvariant();
            if (!spotCache.TryGetValue(underlying, out var under))
            {
                under = (await SpotAsync(data, underlying, ctx.CancellationToken), await StockClosesAsync(data, underlying, today, ctx.CancellationToken));
                spotCache[underlying] = under;
            }

            var price = Dec(p, "current_price");
            var history = await HypeHistoryAsync(data, c, today, under.Closes, ctx.CancellationToken);
            var claims = claimsByTicker.GetValueOrDefault(underlying) ?? [];
            if (price is not { } premium || under.Spot is not { } spot)
            {
                rows.Add(new(account, c.OccSymbol, underlying, Dec(p, "qty") ?? 0m, price, under.Spot, null, null, null, history.Count,
                    claims.Count, false, price is null ? "no current price" : "no underlying price", null));
                continue;
            }

            var extrinsic = Math.Max(0m, premium - Intrinsic(c, spot));
            var percent = premium > 0m ? Math.Round(extrinsic / premium * 100m, 1) : 0m;
            var signal = SellSignalEvaluator.Evaluate(underlying, extrinsic, percent, history, claims, now);
            var high = history.Count > 0 ? Math.Max(history.Max(), extrinsic) : extrinsic;
            rows.Add(new(account, c.OccSymbol, underlying, Dec(p, "qty") ?? 0m, premium, spot, extrinsic, percent, high, history.Count,
                claims.Count(cl => cl.ClaimDateUtc >= now - SellSignalEvaluator.NewsWindow), signal is not null,
                signal is not null ? "NUDGE" : Why(extrinsic, high, history.Count, claims, now), signal?.Message));
        }

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Account").Column("Contract").Column("Qty", Align.Right).Column("Price", Align.Right)
            .Column("Hype/sh", Align.Right).Column("Hype %", Align.Right).Column("Session high", Align.Right)
            .Column("Samples", Align.Right).Column("Bullish 7d", Align.Right).Column("Verdict");
        foreach (var s in rows)
            table.Row(s.Account, s.Symbol, Fmt.Number(s.Quantity), Money(s.Price), Money(s.ExtrinsicPerShare),
                s.ExtrinsicPercent is { } pct ? Fmt.PercentPoints(pct, 0) : "-", Money(s.ExtrinsicHigh),
                s.Samples.ToString(Inv), s.BullishClaims.ToString(Inv), s.Verdict);

        var lines = new List<string> { table.Render() };
        lines.AddRange(rows.Where(s => s.Message is not null).Select(s => $"{s.Symbol}: {s.Message}"));
        lines.Add("Informational only: nothing is ever placed for you. `idiotproof options glossary sell-signal` explains the rule.");
        lines.AddRange(notes);
        return OperationResult.Ok(new { signals = rows, notes }, string.Join("\n", lines));
    }

    /// <summary>Why the nudge stayed quiet, in the order <see cref="SellSignalEvaluator"/> checks.</summary>
    private static string Why(decimal extrinsic, decimal high, int samples, IReadOnlyList<BullishClaimSummary> claims, DateTime now)
    {
        if (extrinsic <= 0m) return "no hype left";
        if (samples < SellSignalEvaluator.MinObservations) return $"warming up ({samples}/{SellSignalEvaluator.MinObservations} samples)";
        if (high > 0m && extrinsic < high * (1m - SellSignalEvaluator.NearHighTolerance))
            return $"hype {Fmt.PercentPoints((1m - extrinsic / high) * 100m, 0)} below its high";
        return claims.Any(c => c.ClaimDateUtc >= now - SellSignalEvaluator.NewsWindow) ? "quiet" : "no bullish news in 7d";
    }

    /// <summary>
    /// Today's hype series: each 5-minute option bar's close minus the intrinsic value at the
    /// underlying's close for the same bar (bars without a matching underlying bar are skipped).
    /// </summary>
    private static async Task<List<decimal>> HypeHistoryAsync(AlpacaApi api, OptionContract c, DateOnly today,
        Dictionary<DateTime, decimal> underlyingCloses, CancellationToken ct)
    {
        List<JsonElement> bars;
        try
        {
            bars = await OptionsSeries.ReadAsync(api, "/v1beta1/options/bars", "bars", c.OccSymbol,
                Query(("timeframe", "5Min"), ("start", OptionsSeries.StartOf(today))), 500, ct);
        }
        catch (AlpacaApiException)
        {
            return [];
        }
        var history = new List<decimal>();
        foreach (var b in bars)
        {
            if (Time(b, "t") is not { } t || Dec(b, "c") is not { } close) continue;
            if (!underlyingCloses.TryGetValue(t, out var spot)) continue;
            history.Add(Math.Max(0m, close - Intrinsic(c, spot)));
        }
        return history;
    }

    /// <summary>The underlying's 5-minute closes for today, keyed by bar start (UTC).</summary>
    private static async Task<Dictionary<DateTime, decimal>> StockClosesAsync(AlpacaApi api, string symbol, DateOnly today, CancellationToken ct)
    {
        var closes = new Dictionary<DateTime, decimal>();
        try
        {
            var root = await api.GetDataAsync("/v2/stocks/bars", Query(("symbols", symbol), ("timeframe", "5Min"),
                ("start", OptionsSeries.StartOf(today)), ("limit", "1000"), ("feed", Market.MarketData.Feed)), ct);
            if (Obj(root, "bars") is { } bySymbol && bySymbol.TryGetProperty(symbol, out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var b in arr.EnumerateArray())
                    if (Time(b, "t") is { } t && Dec(b, "c") is { } close) closes[t] = close;
        }
        catch (AlpacaApiException) { }
        return closes;
    }

    /// <summary>Bullish research claims in the news window per underlying; none (not a failure) when the research tables can't be read.</summary>
    private static async Task<Dictionary<string, List<BullishClaimSummary>>> BullishClaimsAsync(OperationContext ctx, IEnumerable<string> tickers, DateTime now)
    {
        var map = new Dictionary<string, List<BullishClaimSummary>>(StringComparer.OrdinalIgnoreCase);
        var wanted = tickers.Select(t => t.ToUpperInvariant()).ToList();
        if (ctx.Services.GetService<IDbContextFactory<AppDbContext>>() is not { } dbFactory) return map;
        var since = DateOnly.FromDateTime(now - SellSignalEvaluator.NewsWindow);
        await using var db = await dbFactory.CreateDbContextAsync(ctx.CancellationToken);
        var rows = await db.ResearchClaims
            .Where(c => wanted.Contains(c.Ticker) && c.Sentiment == "Bullish" && c.ArticleDate >= since)
            .Select(c => new { c.Ticker, c.ArticleDate, c.SignificanceScore, c.ClaimSummary })
            .ToListAsync(ctx.CancellationToken);
        foreach (var g in rows.GroupBy(r => r.Ticker, StringComparer.OrdinalIgnoreCase))
            map[g.Key] = g.Select(r => new BullishClaimSummary(r.Ticker, r.ArticleDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                (int)Math.Round(r.SignificanceScore ?? 0), r.ClaimSummary)).ToList();
        return map;
    }
}
