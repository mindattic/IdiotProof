namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// What is known about one stock at the evening decision time (the basis session's close, D-1 16:00 ET) for the next
/// session D. Null means unknown (the source was unavailable), which scores nothing and is said so.
/// </summary>
public sealed record GapperRadarFacts(
    string Symbol,
    decimal? PriorClose,
    int? RankHighD1,
    int? RankCloseD1,
    double? HighPctD1,
    double? ClosePctD1,
    double? DollarVolumeD1,
    double? DollarVolumeRatioD1,
    int Up20Last5,
    int Up20Last10,
    int Up20Last30,
    int Up100Last10,
    int? DaysSinceReverseSplit,
    decimal? ReverseSplitRatio,
    bool ReverseSplitOnD,
    bool ReverseSplitOnD1,
    bool ReverseSplitOnBasis,
    int? HaltsLast5,
    int? CatalystBits,
    IReadOnlyList<string> CatalystHeadlines,
    bool NewListing,
    bool ForeignProxy,
    bool? Tradable,
    int? MomentumRank);

/// <summary>One reason a stock is on the radar: its group, the sign, the train lift it carries and its points.</summary>
public sealed record GapperRadarReason(string Group, string Sign, double Lift, double Points);

public sealed record GapperRadarScore(string Symbol, double Points, IReadOnlyList<GapperRadarReason> Reasons, IReadOnlyList<string> Flags, bool Excluded);

/// <summary>
/// The gapper radar's ranking (IP-T500). Each sign carries the gapper study's train-split lift for a next-day high of
/// +50% or more over the prior close (IP-T484, runs/explore-watchlist-20261109-1937.txt, every sign known by D-1 16:00;
/// base rate 0.199% of symbol-days). Correlated signs share a group and only the strongest one in a group counts, so the
/// points are log2(lift) summed over groups. Points rank names; they are not a probability. The study's point score
/// reached 5.6% precision and 15% recall at the top 10 on its folds (25x the base rate): most names on the list will not
/// run, and the study found no long entry on these names that survives costs (IP-T494).
/// Pure and deterministic, so the live operation and any backtest share it.
/// </summary>
public static class GapperRadar
{
    public static class Lifts
    {
        public const double Up100Last10 = 24.7, Up20Last5 = 10.5, Up20x3Last30 = 9.7, Up20Last10 = 7.6, Up20Last30 = 4.7;
        public const double TopTenMoverD1 = 23.7;
        public const double DollarVolume5x = 17.3;
        public const double HaltLast5 = 15.0;
        public const double ReverseSplit11To30 = 8.5, ReverseSplit31To90 = 9.4, ReverseSplitOnD1 = 8.4;
        /// <summary>
        /// Split-day lifts for +100% (not +50%) from the split-day study (IP-T490, train): +100% on 1.8% of effective days and
        /// 2.2% of the days after, 6.3% of days after a ratio above 25:1, against a 0.052% base rate. They fade 6-14% by the close.
        /// </summary>
        public const double ReverseSplitEffectiveDay = 35, ReverseSplitDayAfter = 42, ReverseSplitDayAfterOver25 = 121;
        public const double PriceUnder1 = 7.0, Price1To5 = 1.8, Price5To20 = 0.3;
        public const double NewListing = 3.2;
        public const double ForeignProxy = 1.5;
        public const double NoCatalyst = 0.6;
    }

    /// <summary>Train lift (>= +50%) of a catalyst published in the 5 days to D-1 16:00, by <see cref="CatalystTypes.Keys"/>.</summary>
    public static readonly IReadOnlyDictionary<string, double> CatalystLift = new Dictionary<string, double>
    {
        ["loi"] = 6.1, ["merger"] = 5.4, ["pipe"] = 5.0, ["rsplit"] = 7.7, ["fda"] = 3.8, ["offering"] = 3.6, ["acqAcquirer"] = 2.7,
        ["theme"] = 2.7, ["contract"] = 2.6, ["earnOther"] = 1.9, ["earnBeat"] = 1.1, ["upgrade"] = 0.8, ["earnMiss"] = 0.8,
        ["guidance"] = 0.8, ["acqTarget"] = 0.6, ["buyback"] = 0.2, ["uplist"] = 8.7, ["compliance"] = 1.0,
    };

    /// <param name="momentumLift">
    /// The lift of a Momentum. pick, once the IP-T500 edge study validates one; null (the default) shows the pick as a
    /// reason worth 0 points.
    /// </param>
    public static GapperRadarScore Score(GapperRadarFacts f, double? momentumLift = null)
    {
        var reasons = new List<GapperRadarReason>();
        var flags = new List<string>();
        void Add(string group, string sign, double lift) => reasons.Add(new(group, sign, lift, Math.Log2(lift)));

        // recent runs (the second-run setup)
        if (f.Up100Last10 > 0) Add("recent run", $"+100% day in the last 10 sessions ({f.Up100Last10}x)", Lifts.Up100Last10);
        else if (f.Up20Last5 > 0) Add("recent run", $"+20% day in the last 5 sessions ({f.Up20Last5}x)", Lifts.Up20Last5);
        else if (f.Up20Last30 >= 3) Add("recent run", $"{f.Up20Last30} days of +20% in the last 30 sessions", Lifts.Up20x3Last30);
        else if (f.Up20Last10 > 0) Add("recent run", "+20% day in the last 10 sessions", Lifts.Up20Last10);
        else if (f.Up20Last30 > 0) Add("recent run", "+20% day in the last 30 sessions", Lifts.Up20Last30);

        if (f.RankHighD1 is <= 10 || f.RankCloseD1 is <= 10)
            Add("today's move", $"top-10 mover today (high rank {f.RankHighD1?.ToString() ?? "-"}, close rank {f.RankCloseD1?.ToString() ?? "-"})", Lifts.TopTenMoverD1);

        if (f.DollarVolumeRatioD1 is >= 5) Add("volume", $"dollar volume {f.DollarVolumeRatioD1:0.#}x its 20-day mean", Lifts.DollarVolume5x);

        if (f.HaltsLast5 is > 0) Add("halts", $"{f.HaltsLast5} halt(s) in the last 5 sessions", Lifts.HaltLast5);

        var ratio = f.ReverseSplitRatio is { } r ? $" 1:{r:0.##}" : "";
        if (f.ReverseSplitOnD) Add("reverse split", $"reverse split{ratio} effective this session (split-day lift for +100%, fades 6-14% by the close)", Lifts.ReverseSplitEffectiveDay);
        else if (f.ReverseSplitOnBasis)
            Add("reverse split", $"day after a reverse split{ratio} (split-day lift for +100%)", f.ReverseSplitRatio is > 25 ? Lifts.ReverseSplitDayAfterOver25 : Lifts.ReverseSplitDayAfter);
        else if (f.ReverseSplitOnD1) Add("reverse split", $"reverse split{ratio} effective the session after (calendared)", Lifts.ReverseSplitOnD1);
        else if (f.DaysSinceReverseSplit is >= 31 and <= 90) Add("reverse split", $"reverse split{ratio} {f.DaysSinceReverseSplit} days ago", Lifts.ReverseSplit31To90);
        else if (f.DaysSinceReverseSplit is >= 11 and <= 30) Add("reverse split", $"reverse split{ratio} {f.DaysSinceReverseSplit} days ago", Lifts.ReverseSplit11To30);
        else if (f.DaysSinceReverseSplit is >= 0 and <= 10) flags.Add($"reverse split{ratio} {f.DaysSinceReverseSplit} days ago: too fresh for the study's window, no points");

        if (f.PriorClose is { } pc)
        {
            if (pc < 1) Add("price", $"price {pc:0.####} under $1", Lifts.PriceUnder1);
            else if (pc < 5) Add("price", $"price {pc:0.##} in $1-$5", Lifts.Price1To5);
            else if (pc <= 20) Add("price", $"price {pc:0.##} in $5-$20", Lifts.Price5To20);
            else flags.Add($"price {pc:0.##} over $20: outside the study's universe");
        }

        if (f.CatalystBits is { } bits)
        {
            if (bits == 0) Add("catalyst", "no news or filing in the last 5 days", Lifts.NoCatalyst);
            else
            {
                var best = CatalystTypes.Keys.Select((k, i) => (k, i)).Where(x => (bits & (1 << x.i)) != 0 && CatalystLift.ContainsKey(x.k))
                    .Select(x => (x.k, x.i, lift: CatalystLift[x.k])).OrderByDescending(x => x.lift).FirstOrDefault();
                if (best.k is not null) Add("catalyst", $"catalyst: {CatalystTypes.Names[best.i]}", best.lift);
                if ((bits & CatalystTypes.Bit("fda")) != 0) flags.Add("FDA / clinical news: the worst catalyst class as a long (-0.8 to -1.1% at every intraday exit, IP-T492)");
            }
        }
        else flags.Add("catalysts unknown (news unavailable)");

        if (f.NewListing) Add("listing", "listed within the last 2 years", Lifts.NewListing);
        if (f.ForeignProxy) Add("profile", "foreign-filer name (Ltd / plc / N.V. / S.A. / AG / SE)", Lifts.ForeignProxy);

        if (f.MomentumRank is { } mr)
            reasons.Add(new("Momentum.", $"Momentum. pick #{mr} for this session", momentumLift ?? 1, momentumLift is { } ml ? Math.Log2(ml) : 0));

        // the squeeze profile: biggest runs, worst longs (gapper-anatomy-and-tradability.md section 4)
        if (f.CatalystBits == 0 && f.DaysSinceReverseSplit is >= 0 and <= 90 && f.ForeignProxy)
            flags.Add("squeeze profile (no catalyst, recent reverse split, foreign filer): the biggest runs and the worst longs; 74% of the run is gone by D+2");
        if (f.Up100Last10 > 0)
            flags.Add("already ran +100% this fortnight: a second run happens on 7% of runners within 5 sessions");

        bool excluded = false;
        if (f.Tradable == false) { excluded = true; flags.Insert(0, "Alpaca will not trade it (tradable:false or not listed)"); }

        var points = Math.Round(reasons.Sum(x => x.Points), 2);
        return new(f.Symbol, points, reasons, flags, excluded);
    }

    private static readonly System.Text.RegularExpressions.Regex Foreign = new(@"\b(Ltd|Limited|plc|PLC|N\.?V\.?|S\.?A\.?|AG|SE|Pte)\b\.?\s*$|\b(Ltd|Limited|plc)\b",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The study's foreign proxy on a company name (Cayman, BVI and most Asia-linked listings use these suffixes).</summary>
    public static bool IsForeignName(string? name) => !string.IsNullOrWhiteSpace(name) && Foreign.IsMatch(name);
}
