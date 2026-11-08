using System.Globalization;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// One minute of trade flow (avenues.md A4, IP-T460): prints, shares, odd-lot shares (under 100), off-exchange
/// shares (FINRA TRF, exchange "D"), and retail buy/sell shares signed by sub-penny price improvement
/// (Boehmer, Jones, Zhang and Zhang, JF 2021: a TRF print whose price has a fraction of a cent between 0.6 and
/// 1 is a retail buy, between 0 and 0.4 a retail sell). Barber et al. (JF 2024) show the sub-penny sign is noisy
/// against the quote midpoint, so it is used here only as a coarse imbalance.
/// </summary>
public sealed record TradeFlowMinute(DateTime StartUtc, int Prints, double Shares, double OddLotShares, double OffExchangeShares, double RetailBuyShares, double RetailSellShares)
{
    public double? RetailImbalance => RetailBuyShares + RetailSellShares is var n && n > 0 ? (RetailBuyShares - RetailSellShares) / n : null;
}

public static class TradeFlow
{
    public const string TrfExchange = "D";

    /// <summary>Sub-penny retail sign: +1 buy, -1 sell, 0 unsigned (round-cent or mid-cent prints, or not on the TRF).</summary>
    public static int RetailSign(double price, string exchange)
    {
        if (exchange != TrfExchange) return 0;
        double cents = price * 100;
        double frac = cents - Math.Floor(cents + 1e-9);
        if (frac < 1e-6) return 0;
        if (frac > 0.6 && frac < 1) return 1;
        if (frac > 0 && frac < 0.4) return -1;
        return 0;
    }

    public static List<TradeFlowMinute> Aggregate(IEnumerable<(DateTime Utc, double Price, double Size, string Exchange)> prints)
    {
        var byMinute = new SortedDictionary<DateTime, (int N, double Sh, double Odd, double Off, double Buy, double Sell)>();
        foreach (var (utc, price, size, exchange) in prints)
        {
            if (size <= 0 || price <= 0) continue;
            var minute = new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
            var a = byMinute.GetValueOrDefault(minute);
            int sign = RetailSign(price, exchange);
            byMinute[minute] = (a.N + 1, a.Sh + size, a.Odd + (size < 100 ? size : 0), a.Off + (exchange == TrfExchange ? size : 0),
                a.Buy + (sign > 0 ? size : 0), a.Sell + (sign < 0 ? size : 0));
        }
        return byMinute.Select(kv => new TradeFlowMinute(kv.Key, kv.Value.N, kv.Value.Sh, kv.Value.Odd, kv.Value.Off, kv.Value.Buy, kv.Value.Sell)).ToList();
    }

    /// <summary>Retail imbalance over the minutes in [fromUtc, toUtc); null when no signed shares.</summary>
    public static double? Imbalance(IReadOnlyList<TradeFlowMinute> minutes, DateTime fromUtc, DateTime toUtc)
    {
        double buy = 0, sell = 0;
        foreach (var m in minutes)
            if (m.StartUtc >= fromUtc && m.StartUtc < toUtc) { buy += m.RetailBuyShares; sell += m.RetailSellShares; }
        return buy + sell > 0 ? (buy - sell) / (buy + sell) : null;
    }

    /// <summary>
    /// The first minute at or after <paramref name="armUtc"/> where the trailing <paramref name="window"/>-minute
    /// retail imbalance falls below zero after having been at least <paramref name="armLevel"/> (buyers in
    /// control, then gone): the H17 "imbalance collapse" footprint. Null when it never happens.
    /// </summary>
    public static DateTime? ImbalanceCollapse(IReadOnlyList<TradeFlowMinute> minutes, DateTime armUtc, DateTime untilUtc, int window = 5, double armLevel = 0.2)
    {
        bool armed = false;
        for (var t = armUtc; t < untilUtc; t = t.AddMinutes(1))
        {
            var imb = Imbalance(minutes, t.AddMinutes(-window), t);
            if (imb is not { } v) continue;
            if (v >= armLevel) armed = true;
            else if (armed && v < 0) return t;
        }
        return null;
    }

    public static string ToCsv(IEnumerable<TradeFlowMinute> minutes) =>
        "startUtc,prints,shares,oddLot,offExchange,retailBuy,retailSell\n" + string.Concat(minutes.Select(m =>
            string.Create(CultureInfo.InvariantCulture, $"{m.StartUtc.Ticks},{m.Prints},{m.Shares},{m.OddLotShares},{m.OffExchangeShares},{m.RetailBuyShares},{m.RetailSellShares}\n")));

    public static List<TradeFlowMinute> FromCsv(IEnumerable<string> lines) =>
        lines.Skip(1).Select(l => l.Split(',')).Where(c => c.Length == 7).Select(c => new TradeFlowMinute(
            new DateTime(long.Parse(c[0], CultureInfo.InvariantCulture), DateTimeKind.Utc), int.Parse(c[1], CultureInfo.InvariantCulture),
            double.Parse(c[2], CultureInfo.InvariantCulture), double.Parse(c[3], CultureInfo.InvariantCulture), double.Parse(c[4], CultureInfo.InvariantCulture),
            double.Parse(c[5], CultureInfo.InvariantCulture), double.Parse(c[6], CultureInfo.InvariantCulture))).ToList();
}
