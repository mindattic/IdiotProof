namespace IdiotProof.DataFeeds;

/// <summary>
/// One trading-status message from Alpaca's <c>statuses</c> channel. <see cref="Halted"/> reads
/// the UTP/CTA status code first ("H"/"P"/"2" halted or paused, "T"/"Q"/"3" resumed) and the
/// status text second ("halt"/"pause" against "resum"); null when the message says neither.
/// </summary>
public sealed record TradingStatus(string Symbol, string Code, string Message, string ReasonCode, string ReasonMessage, DateTime TimestampUtc, string Tape)
{
    private static readonly HashSet<string> HaltCodes = new(StringComparer.Ordinal) { "H", "P", "2" };
    private static readonly HashSet<string> ResumeCodes = new(StringComparer.Ordinal) { "T", "Q", "3" };

    public bool? Halted
    {
        get
        {
            if (HaltCodes.Contains(Code)) return true;
            if (ResumeCodes.Contains(Code)) return false;
            if (Message.Contains("resum", StringComparison.OrdinalIgnoreCase)) return false;
            if (Message.Contains("halt", StringComparison.OrdinalIgnoreCase) || Message.Contains("pause", StringComparison.OrdinalIgnoreCase)) return true;
            return null;
        }
    }
}

/// <summary>One limit-up/limit-down band message from the <c>lulds</c> channel.</summary>
public sealed record LuldBand(string Symbol, decimal? LimitUp, decimal? LimitDown, string Indicator, DateTime TimestampUtc, string Tape);
