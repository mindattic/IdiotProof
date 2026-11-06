using IdiotProof.Scripting;

namespace IdiotProof.Core.Evidence;

/// <summary>NYSE trading days from the holiday table in <see cref="MarketTime"/>, for anything that walks sessions.</summary>
public static class MarketCalendar
{
    public static bool IsTradingDay(DateOnly d) =>
        d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !MarketTime.IsMarketHoliday(d);

    public static DateOnly NextTradingDay(DateOnly d)
    {
        do d = d.AddDays(1); while (!IsTradingDay(d));
        return d;
    }

    public static DateOnly PreviousTradingDay(DateOnly d)
    {
        do d = d.AddDays(-1); while (!IsTradingDay(d));
        return d;
    }

    /// <summary>Trading days in [from, to], ascending.</summary>
    public static List<DateOnly> TradingDays(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
            if (IsTradingDay(d)) days.Add(d);
        return days;
    }

    /// <summary>The <paramref name="count"/> trading days strictly before <paramref name="d"/>, ascending.</summary>
    public static List<DateOnly> PriorTradingDays(DateOnly d, int count)
    {
        var days = new List<DateOnly>(count);
        var cursor = d;
        while (days.Count < count)
        {
            cursor = PreviousTradingDay(cursor);
            days.Add(cursor);
        }
        days.Reverse();
        return days;
    }
}
