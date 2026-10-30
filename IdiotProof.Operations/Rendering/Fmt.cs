using System.Globalization;

namespace IdiotProof.Operations.Rendering;

/// <summary>
/// Value formatting for human output. Culture-invariant on purpose: the Hub may run under any
/// Windows locale, and a price must read <c>$1,234.56</c> for everyone. Times are always ET.
/// </summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary><c>$1,234.56</c>; negatives as <c>-$1,234.56</c>.</summary>
    public static string Money(decimal value, int decimals = 2)
    {
        var abs = Math.Abs(Math.Round(value, decimals, MidpointRounding.AwayFromZero));
        var text = "$" + abs.ToString("N" + decimals, Inv);
        return value < 0 && abs != 0 ? "-" + text : text;
    }

    public static string Money(double value, int decimals = 2) => Money((decimal)value, decimals);

    /// <summary>Like <see cref="Money(decimal, int)"/> with an explicit <c>+</c> on gains (P&amp;L columns).</summary>
    public static string SignedMoney(decimal value, int decimals = 2) =>
        value > 0 ? "+" + Money(value, decimals) : Money(value, decimals);

    /// <summary>
    /// A fraction as a percent: <c>0.0525</c> → <c>5.25%</c>. Pass <paramref name="signed"/> for
    /// <c>+5.25%</c> on positives. Values already in percent units go through <see cref="PercentPoints"/>.
    /// </summary>
    public static string Percent(decimal fraction, int decimals = 2, bool signed = false) =>
        PercentPoints(fraction * 100m, decimals, signed);

    public static string Percent(double fraction, int decimals = 2, bool signed = false) =>
        Percent((decimal)fraction, decimals, signed);

    /// <summary>A value already in percent units: <c>5.25</c> → <c>5.25%</c>.</summary>
    public static string PercentPoints(decimal percent, int decimals = 2, bool signed = false)
    {
        var rounded = Math.Round(percent, decimals, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("N" + decimals, Inv) + "%";
        return signed && rounded > 0 ? "+" + text : text;
    }

    /// <summary>Share counts and other plain numbers: <c>1,250</c>, <c>0.5</c>.</summary>
    public static string Number(decimal value, int maxDecimals = 4) =>
        Math.Round(value, maxDecimals, MidpointRounding.AwayFromZero).ToString("#,0." + new string('#', maxDecimals), Inv);

    /// <summary><c>2026-10-30 9:31 AM ET</c> from a UTC instant (Unspecified kind is taken as UTC).</summary>
    public static string Et(DateTime utc) => EasternClock.Format(utc);

    public static string Et(DateTimeOffset instant) => EasternClock.Format(instant.UtcDateTime);

    /// <summary><c>2026-10-30 9:31 AM ET</c>, or <paramref name="none"/> when null.</summary>
    public static string Et(DateTime? utc, string none = "—") => utc is { } v ? Et(v) : none;

    /// <summary><c>9:31 AM ET</c>.</summary>
    public static string EtTime(DateTime utc) => EasternClock.FormatTime(utc);

    /// <summary>The ET calendar date, <c>2026-10-30</c>.</summary>
    public static string EtDate(DateTime utc) => EasternClock.FormatDate(utc);
}
