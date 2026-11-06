namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>A cached tradable ticker as the universe sees it (from <c>TrackedTickers</c>).</summary>
public sealed record TrackedTickerInfo(string Symbol, bool IsTradable, decimal? LastPrice, long? SharesOutstanding);

/// <summary>A symbol named by a catalyst published since the prior close.</summary>
public sealed record CatalystSymbolHit(string Symbol, double Score, int Direction, DateTime PublishedUtc, string Headline, string PreClass, int SourceTier);

/// <summary>A screener row: movers carry the percent change and price, most-actives carry neither.</summary>
public sealed record ScreenerHit(string Symbol, double? Percent = null, double? Price = null);

/// <summary>One symbol of today's formation universe and every reason it is in.</summary>
public sealed record FormationMember(string Symbol, IReadOnlyList<string> Reasons, double? CatalystScore, double? GapPct, double? Price, long? SharesOutstanding)
{
    public const string CatalystReason = "catalyst";
    public const string MoverReason = "mover";
    public const string MostActiveReason = "most-active";
    public const string TrackedReason = "tracked";

    public double? MarketCap => Price is { } p && SharesOutstanding is { } s ? p * s : null;

    public string Why => string.Join("+", Reasons);
}

/// <summary>
/// The pure assembly of the day's universe: tracked tickers inside the price band (and the
/// market-cap band when shares outstanding are known; unknown shares are kept), plus every symbol
/// with a catalyst since the prior close, plus the movers and most-actives screens, deduplicated
/// and ranked by catalyst score, then gap percent, then how many lists named it, then symbol; cut
/// at the cap. The price band applies to every member whose price is known.
/// </summary>
public static class FormationUniverse
{
    public static IReadOnlyList<FormationMember> Build(
        IEnumerable<TrackedTickerInfo> tracked,
        IEnumerable<CatalystSymbolHit> catalysts,
        IEnumerable<ScreenerHit> movers,
        IEnumerable<ScreenerHit> mostActives,
        FormationConfig cfg)
    {
        var members = new Dictionary<string, Draft>(StringComparer.OrdinalIgnoreCase);
        var trackedBySymbol = new Dictionary<string, TrackedTickerInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in tracked)
        {
            var sym = Normalize(t.Symbol);
            if (sym.Length == 0) continue;
            trackedBySymbol[sym] = t;
            if (!t.IsTradable) continue;
            var d = Draft.For(members, sym);
            d.Reasons.Add(FormationMember.TrackedReason);
            d.Price ??= (double?)t.LastPrice;
            d.Shares ??= t.SharesOutstanding;
        }

        foreach (var c in catalysts)
        {
            var sym = Normalize(c.Symbol);
            if (sym.Length == 0) continue;
            var d = Draft.For(members, sym);
            d.Reasons.Add(FormationMember.CatalystReason);
            d.CatalystScore = Math.Max(d.CatalystScore ?? double.NegativeInfinity, c.Score);
        }

        foreach (var m in movers)
        {
            var sym = Normalize(m.Symbol);
            if (sym.Length == 0) continue;
            var d = Draft.For(members, sym);
            d.Reasons.Add(FormationMember.MoverReason);
            if (m.Percent is { } pct) d.GapPct = Math.Max(d.GapPct ?? double.NegativeInfinity, pct);
            if (m.Price is > 0) d.Price = m.Price;
        }

        foreach (var a in mostActives)
        {
            var sym = Normalize(a.Symbol);
            if (sym.Length == 0) continue;
            var d = Draft.For(members, sym);
            d.Reasons.Add(FormationMember.MostActiveReason);
            if (a.Price is > 0) d.Price ??= a.Price;
        }

        foreach (var d in members.Values)
        {
            if (trackedBySymbol.TryGetValue(d.Symbol, out var t))
            {
                d.Price ??= (double?)t.LastPrice;
                d.Shares ??= t.SharesOutstanding;
            }
        }

        return members.Values
            .Where(d => InBands(d, cfg))
            .Select(d => d.ToMember())
            .OrderByDescending(m => m.CatalystScore ?? double.NegativeInfinity)
            .ThenByDescending(m => m.GapPct ?? double.NegativeInfinity)
            .ThenByDescending(m => m.Reasons.Count)
            .ThenBy(m => m.Symbol, StringComparer.Ordinal)
            .Take(Math.Max(1, cfg.MaxSymbols))
            .ToList();
    }

    private static bool InBands(Draft d, FormationConfig cfg)
    {
        if (d.Price is { } p && (p < cfg.PriceMin || p >= cfg.PriceMax)) return false;
        if (d.Price is { } px && d.Shares is { } sh)
        {
            var cap = px * sh;
            if (cfg.MarketCapMin is { } lo && cap < lo) return false;
            if (cfg.MarketCapMax is { } hi && cap > hi) return false;
        }
        return true;
    }

    private static string Normalize(string symbol) => (symbol ?? "").Trim().ToUpperInvariant();

    private sealed class Draft(string symbol)
    {
        public string Symbol { get; } = symbol;
        public List<string> Reasons { get; } = [];
        public double? CatalystScore { get; set; }
        public double? GapPct { get; set; }
        public double? Price { get; set; }
        public long? Shares { get; set; }

        public static Draft For(Dictionary<string, Draft> all, string symbol)
        {
            if (!all.TryGetValue(symbol, out var d)) all[symbol] = d = new Draft(symbol);
            return d;
        }

        public FormationMember ToMember() => new(Symbol, Reasons.Distinct().ToList(), CatalystScore, GapPct, Price, Shares);
    }
}
