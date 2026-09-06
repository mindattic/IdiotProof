using System.Globalization;

namespace IdiotProof.Shared.Options;

/// <summary>
/// A research claim reduced to what the sell-signal needs. The full <c>ResearchClaim</c>
/// entity lives in the Blazor host's data layer; the host projects it into this before
/// calling <see cref="SellSignalEvaluator"/> so this project stays free of EF.
/// </summary>
public sealed record BullishClaimSummary(string Ticker, DateTime ClaimDateUtc, int SignificanceScore, string Headline);

/// <summary>
/// The "cash in when the hype is highest" nudge for an OPEN long option position.
/// Pure function; informational only — it never places or suggests a specific order.
/// <para>
/// Fires when BOTH hold:
/// <list type="number">
///   <item>Current extrinsic (time/hype) value is within <see cref="NearHighTolerance"/> of the
///   highest extrinsic value observed since the position was opened / this session began.</item>
///   <item>At least one Bullish research claim for the underlying within <see cref="NewsWindow"/>.</item>
/// </list>
/// The reasoning: extrinsic value is exactly what you are SELLING when you close early; when
/// it's at a local peak and the news tape is hot, that's the moment the market is paying most
/// for the idea rather than the reality.
/// </para>
/// </summary>
public static class SellSignalEvaluator
{
    /// <summary>Within 5% of the observed extrinsic high counts as "near the high".</summary>
    public const decimal NearHighTolerance = 0.05m;

    public static readonly TimeSpan NewsWindow = TimeSpan.FromDays(7);

    public static SellSignal? Evaluate(
        string underlying,
        decimal currentExtrinsicPerShare,
        decimal extrinsicPercentOfPremium,
        IReadOnlyList<decimal> extrinsicHistoryPerShare,
        IReadOnlyList<BullishClaimSummary> claims,
        DateTime nowUtc)
    {
        if (currentExtrinsicPerShare <= 0m) return null;

        var high = extrinsicHistoryPerShare.Count > 0
            ? Math.Max(extrinsicHistoryPerShare.Max(), currentExtrinsicPerShare)
            : currentExtrinsicPerShare;
        var nearHigh = high > 0m && currentExtrinsicPerShare >= high * (1m - NearHighTolerance);
        if (!nearHigh) return null;

        var cutoff = nowUtc - NewsWindow;
        var recent = claims
            .Where(c => string.Equals(c.Ticker, underlying, StringComparison.OrdinalIgnoreCase) && c.ClaimDateUtc >= cutoff)
            .OrderByDescending(c => c.SignificanceScore)
            .ToList();
        if (recent.Count == 0) return null;

        var top = recent[0];
        var text = string.Create(CultureInfo.InvariantCulture,
            $"Extrinsic value near its recent high (${currentExtrinsicPerShare:0.00}/sh, {extrinsicPercentOfPremium:0}% of premium) + {recent.Count} recent bullish item{(recent.Count == 1 ? "" : "s")} for {underlying.ToUpperInvariant()} (top score {top.SignificanceScore}: \"{Truncate(top.Headline, 80)}\") — consider taking profit.");

        return new SellSignal(text, currentExtrinsicPerShare, high, recent.Count, top.SignificanceScore);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

public sealed record SellSignal(string Message, decimal CurrentExtrinsic, decimal ExtrinsicHigh, int BullishClaimCount, int TopSignificance);
