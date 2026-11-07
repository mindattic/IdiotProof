using System.Globalization;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// Keys of the protocol features that ride in <see cref="GapperFormationSnapshot.Extra"/> rather
/// than in fixed columns (the row they are stored in was shaped before they landed, and the live
/// recorder mirrors the fixed columns one to one): the trade-count rate of change, the LLM
/// sentiment, the Form 4 purchases, and the latest 8-K's item codes as one flag per code plus a
/// count, so the map stays numeric end to end. <see cref="EightKItems"/> rebuilds the comma-joined
/// code string a reader expects.
/// </summary>
public static class GapperExtraKeys
{
    public const string TradesRoc = "tradesRoc";
    public const string CatalystSentiment = "catalystSentiment";
    public const string Form4BuysD30 = "form4BuysD30";
    public const string Form4BuyDollarsD30 = "form4BuyDollarsD30";
    public const string EightKItemCount = "eightKItemCount";
    public const string EightKItemPrefix = "eightK_";

    public static string EightKItemKey(string itemCode) => EightKItemPrefix + itemCode.Trim();

    /// <summary>"2.02,9.01" from the item flags; "" when an 8-K is present without known items; null when no 8-K is in the lookback.</summary>
    public static string? EightKItems(IReadOnlyDictionary<string, double> extra)
    {
        if (!extra.ContainsKey(EightKItemCount)) return null;
        var codes = extra.Keys.Where(k => k.StartsWith(EightKItemPrefix, StringComparison.Ordinal)).Select(k => k[EightKItemPrefix.Length..])
            .OrderBy(c => double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.MaxValue).ThenBy(c => c, StringComparer.Ordinal);
        return string.Join(",", codes);
    }
}
