using System.Globalization;

namespace IdiotProof.Models;

/// <summary>
/// One listed option contract, identified by its OCC symbol
/// (e.g. <c>BE251219C00038000</c> = BE, 2025-12-19, Call, $38.00 strike).
/// Immutable; built either from Alpaca's <c>/v2/options/contracts</c> catalog or
/// decoded straight from an OCC symbol via <see cref="ParseOcc"/>.
/// </summary>
public sealed class OptionContract
{
    public string OccSymbol { get; init; } = string.Empty;
    public string UnderlyingSymbol { get; init; } = string.Empty;
    public DateOnly Expiration { get; init; }
    public decimal Strike { get; init; }
    public OptionRight Right { get; init; }

    /// <summary>Shares per contract. US equity options are 100 (Alpaca <c>size</c>).</summary>
    public int Multiplier { get; init; } = 100;

    public bool Tradable { get; init; } = true;
    public long? OpenInterest { get; init; }

    /// <summary>Human label: "BE $38 Call · Dec 19 2025".</summary>
    public string DisplayName =>
        $"{UnderlyingSymbol} ${Strike.ToString("0.##", CultureInfo.InvariantCulture)} {Right} · {Expiration:MMM dd yyyy}";

    /// <summary>
    /// Decode an OCC option symbol. Layout is fixed-width from the right:
    /// <c>ROOT</c> (1–6 chars) + <c>YYMMDD</c> + <c>C|P</c> + strike × 1000 zero-padded to 8 digits.
    /// Returns null (never throws) on anything that doesn't fit — callers treat that as "not an option".
    /// </summary>
    public static OptionContract? ParseOcc(string? occSymbol)
    {
        if (string.IsNullOrWhiteSpace(occSymbol)) return null;
        var s = occSymbol.Trim().ToUpperInvariant();

        // Minimum: 1-char root + 6 date + 1 right + 8 strike = 16.
        if (s.Length < 16) return null;

        var strikePart = s[^8..];
        var rightChar = s[^9];
        var datePart = s[^15..^9];
        var root = s[..^15];

        if (root.Length == 0 || root.Length > 6 || !root.All(char.IsLetterOrDigit)) return null;
        if (rightChar is not ('C' or 'P')) return null;
        if (!long.TryParse(strikePart, NumberStyles.None, CultureInfo.InvariantCulture, out var strikeThousandths)) return null;
        if (!DateOnly.TryParseExact(datePart, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiration)) return null;

        return new OptionContract
        {
            OccSymbol = s,
            UnderlyingSymbol = root,
            Expiration = expiration,
            Strike = strikeThousandths / 1000m,
            Right = rightChar == 'C' ? OptionRight.Call : OptionRight.Put,
        };
    }

    /// <summary>Inverse of <see cref="ParseOcc"/>.</summary>
    public static string BuildOcc(string underlying, DateOnly expiration, OptionRight right, decimal strike)
    {
        var thousandths = (long)Math.Round(strike * 1000m, MidpointRounding.AwayFromZero);
        return $"{underlying.Trim().ToUpperInvariant()}{expiration:yyMMdd}{(right == OptionRight.Call ? 'C' : 'P')}{thousandths:D8}";
    }
}

/// <summary>
/// Live market snapshot for one contract. <see cref="ImpliedVolatility"/> and
/// <see cref="Greeks"/> are what the broker supplied (Alpaca computes them server-side)
/// and are null whenever it omits them — 0DTE contracts, missing inputs — so the UI can
/// fall back to the local Black-Scholes solver and badge the source honestly.
/// </summary>
public sealed record OptionQuote(
    string OccSymbol,
    decimal Bid,
    decimal Ask,
    decimal? LastTrade,
    decimal? ImpliedVolatility,
    OptionGreeks? Greeks,
    DateTime TimestampUtc)
{
    /// <summary>Mid-point premium per share; falls back to last trade, then whichever side is non-zero.</summary>
    public decimal Mid =>
        Bid > 0m && Ask > 0m ? (Bid + Ask) / 2m
        : LastTrade is { } lt && lt > 0m ? lt
        : Math.Max(Bid, Ask);
}

/// <summary>First-order option sensitivities as reported by the broker.</summary>
public sealed record OptionGreeks(decimal Delta, decimal Gamma, decimal Theta, decimal Vega, decimal Rho);
