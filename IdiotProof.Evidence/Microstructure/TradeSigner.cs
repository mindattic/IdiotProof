namespace IdiotProof.Evidence.Microstructure;

/// <summary>One trade print.</summary>
public readonly record struct TradePrint(DateTime TimeUtc, double Price, double Size);

/// <summary>One NBBO update.</summary>
public readonly record struct QuoteTick(DateTime TimeUtc, double Bid, double Ask);

/// <summary>
/// Guesses which side started each trade: +1 a buyer lifted the offer, -1 a seller hit the bid,
/// 0 unknown. Each sign uses only that trade and what came before it.
/// </summary>
public static class TradeSigner
{
    /// <summary>
    /// Tick rule: an uptick is a buy, a downtick a sell, and a zero tick repeats the last non-zero
    /// direction. The first trade (and any before the first price change) is 0.
    /// </summary>
    public static int[] TickRule(IReadOnlyList<TradePrint> trades)
    {
        var signs = new int[trades.Count];
        int last = 0;
        for (int i = 1; i < trades.Count; i++)
        {
            double d = trades[i].Price - trades[i - 1].Price;
            if (d > 0) last = 1;
            else if (d < 0) last = -1;
            signs[i] = last;
        }
        return signs;
    }

    /// <summary>
    /// Lee-Ready: compare each trade to the midpoint of the latest quote at or before it (above = buy,
    /// below = sell); at the midpoint, or with no usable quote yet, fall back to the tick rule.
    /// </summary>
    public static int[] LeeReady(IReadOnlyList<TradePrint> trades, IReadOnlyList<QuoteTick> quotes)
    {
        var tick = TickRule(trades);
        var signs = new int[trades.Count];
        int q = -1;
        for (int i = 0; i < trades.Count; i++)
        {
            while (q + 1 < quotes.Count && quotes[q + 1].TimeUtc <= trades[i].TimeUtc) q++;
            signs[i] = tick[i];
            if (q < 0) continue;
            var quote = quotes[q];
            if (quote.Bid <= 0 || quote.Ask < quote.Bid) continue;
            double mid = (quote.Bid + quote.Ask) / 2;
            if (trades[i].Price > mid) signs[i] = 1;
            else if (trades[i].Price < mid) signs[i] = -1;
        }
        return signs;
    }
}
