using System.Globalization;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// Reg SHO threshold-security lists (IP-T469), one pipe-delimited file per trading day: Nasdaq-listed from
/// <c>https://www.nasdaqtrader.com/dynamic/symdir/regsho/nasdaqthYYYYMMDD.txt</c> and NYSE, NYSE American,
/// NYSE Arca, NYSE Chicago and NYSE National from the NYSE download API. Each file is stamped the evening of
/// its own trading day (the last line, YYYYMMDDHHMMSS), so the list dated D-1 is known before 04:00 on D.
/// </summary>
public sealed class RegShoThresholdHistory
{
    /// <summary>The longest listed run counted back from D-1 (Rule 203 closes out after 13 settlement days, so real runs are far shorter).</summary>
    public const int MaxRunDays = 400;

    private readonly Dictionary<DateOnly, HashSet<string>> byDay;

    public RegShoThresholdHistory(IEnumerable<(DateOnly Day, string Symbol)> listings, IEnumerable<DateOnly>? daysWithoutListings = null)
    {
        byDay = [];
        foreach (var d in daysWithoutListings ?? []) byDay.TryAdd(d, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        foreach (var (day, symbol) in listings)
        {
            if (!byDay.TryGetValue(day, out var set)) byDay[day] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(symbol);
        }
    }

    /// <summary>Trading days with a list on disk.</summary>
    public int Days => byDay.Count;

    /// <summary>Symbols flagged Y in one day's file; the header and the trailing timestamp line are skipped.</summary>
    public static IEnumerable<string> Parse(IEnumerable<string> lines)
    {
        string[]? header = null;
        foreach (var line in lines)
        {
            var cells = line.Split('|');
            if (header is null)
            {
                if (cells.Length > 1 && cells[0].Trim() == "Symbol") header = cells;
                continue;
            }
            if (cells.Length < 4) continue;
            int flag = Array.FindIndex(header, h => h.Trim() == "Reg SHO Threshold Flag");
            var symbol = cells[0].Trim();
            if (symbol.Length == 0 || flag < 0 || flag >= cells.Length || !cells[flag].Trim().Equals("Y", StringComparison.OrdinalIgnoreCase)) continue;
            yield return symbol.ToUpperInvariant();
        }
    }

    /// <summary>True when the text is a threshold list (a header row), not an error or "not found" page.</summary>
    public static bool LooksValid(string text) => text.StartsWith("Symbol|", StringComparison.Ordinal);

    /// <summary>The trading day in a file name such as nasdaqth20211001.txt or nyse20211001.txt.</summary>
    public static DateOnly? DayOf(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.Length >= 8 && DateOnly.TryParseExact(stem[^8..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>Loads every *.txt day file in a directory (all listing markets merged per day); null when the directory does not exist.</summary>
    public static RegShoThresholdHistory? Load(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        var listings = new List<(DateOnly, string)>();
        var days = new List<DateOnly>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.txt"))
        {
            if (DayOf(file) is not { } day) continue;
            days.Add(day);
            listings.AddRange(Parse(File.ReadLines(file)).Select(s => (day, s)));
        }
        return new RegShoThresholdHistory(listings, days);
    }

    /// <summary>Null when no list for that day is on disk.</summary>
    public bool? ListedOn(string symbol, DateOnly day) => byDay.TryGetValue(day, out var set) ? set.Contains(symbol) : null;

    /// <summary>
    /// Point in time at D: whether the symbol was on the list dated D-1 and how many consecutive listed
    /// trading days end at D-1 (0 when not listed). Only lists dated strictly before D are read; both are
    /// null when the D-1 list is missing. A missing list inside a run ends the count.
    /// </summary>
    public (bool? ListedD1, int? DaysListed) KnownBefore(string symbol, DateOnly dateEt, Func<DateOnly, DateOnly> previousTradingDay)
    {
        var d = previousTradingDay(dateEt);
        if (d >= dateEt || ListedOn(symbol, d) is not { } listed) return (null, null);
        if (!listed) return (false, 0);
        int run = 0;
        while (run < MaxRunDays && ListedOn(symbol, d) == true)
        {
            run++;
            d = previousTradingDay(d);
        }
        return (true, run);
    }
}

/// <summary>One FINRA consolidated (CNMS) daily short-sale volume row.</summary>
public sealed record ShortVolumeRecord(DateOnly Date, string Symbol, double ShortVolume, double ShortExemptVolume, double TotalVolume)
{
    public double? Share => TotalVolume > 0 ? ShortVolume / TotalVolume : null;
}

/// <summary>
/// FINRA daily short-sale volume (IP-T469), consolidated NMS files <c>CNMSshvolYYYYMMDD.txt</c>
/// (Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market; volumes may be fractional since 2026).
/// The file for D-1 is published the evening of D-1, so D-1 and earlier are usable on D. The total is
/// FINRA-reported (off-exchange plus the participating venues), not consolidated volume: the share is a
/// relative measure for the same symbol over time.
/// </summary>
public sealed class ShortVolumeHistory
{
    /// <summary>Days before D-1 the z-score baseline spans (D-2..D-21).</summary>
    public const int ZBaselineDays = 20;
    public const int ZMinBaselineDays = 10;
    public const int AvgDays = 5;

    private readonly Dictionary<string, Dictionary<DateOnly, double>> shareBySymbol;

    public ShortVolumeHistory(IEnumerable<ShortVolumeRecord> records)
    {
        shareBySymbol = new Dictionary<string, Dictionary<DateOnly, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
        {
            if (r.Share is not { } share) continue;
            if (!shareBySymbol.TryGetValue(r.Symbol, out var days)) shareBySymbol[r.Symbol] = days = [];
            days[r.Date] = share;
        }
        Count = shareBySymbol.Values.Sum(d => d.Count);
    }

    public int Count { get; }

    public static IEnumerable<ShortVolumeRecord> Parse(IEnumerable<string> lines, ISet<string>? symbols = null)
    {
        string[]? header = null;
        int iDate = -1, iSym = -1, iShort = -1, iExempt = -1, iTotal = -1;
        foreach (var line in lines)
        {
            var cells = line.Split('|');
            if (header is null)
            {
                if (cells.Length < 5 || cells[0].Trim() != "Date") continue;
                header = cells.Select(c => c.Trim()).ToArray();
                iDate = Array.IndexOf(header, "Date"); iSym = Array.IndexOf(header, "Symbol"); iShort = Array.IndexOf(header, "ShortVolume");
                iExempt = Array.IndexOf(header, "ShortExemptVolume"); iTotal = Array.IndexOf(header, "TotalVolume");
                if (iSym < 0 || iShort < 0 || iTotal < 0) yield break;
                continue;
            }
            if (cells.Length <= Math.Max(iTotal, Math.Max(iSym, iShort))) continue;
            var symbol = cells[iSym].Trim().ToUpperInvariant();
            if (symbol.Length == 0 || (symbols is not null && !symbols.Contains(symbol))) continue;
            if (!DateOnly.TryParseExact(cells[iDate].Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (!double.TryParse(cells[iShort], NumberStyles.Float, CultureInfo.InvariantCulture, out var sv) ||
                !double.TryParse(cells[iTotal], NumberStyles.Float, CultureInfo.InvariantCulture, out var tv)) continue;
            double ex = iExempt >= 0 && iExempt < cells.Length && double.TryParse(cells[iExempt], NumberStyles.Float, CultureInfo.InvariantCulture, out var e) ? e : 0;
            yield return new ShortVolumeRecord(date, symbol, sv, ex, tv);
        }
    }

    /// <summary>True when the text is a short-volume file (its header row), not an error page.</summary>
    public static bool LooksValid(string text) => text.StartsWith("Date|Symbol|", StringComparison.Ordinal);

    /// <summary>Loads every CNMSshvol*.txt file, keeping only <paramref name="symbols"/> when given; null when the directory does not exist.</summary>
    public static ShortVolumeHistory? Load(string dir, ISet<string>? symbols = null)
    {
        if (!Directory.Exists(dir)) return null;
        var all = new List<ShortVolumeRecord>();
        foreach (var file in Directory.EnumerateFiles(dir, "CNMSshvol*.txt")) all.AddRange(Parse(File.ReadLines(file), symbols));
        return new ShortVolumeHistory(all);
    }

    /// <summary>
    /// Point in time at D: the short share on D-1, its mean over D-1..D-5 (the days present), and D-1 as a
    /// z-score against D-2..D-21 (at least ten days and a positive spread). Only days strictly before D are read.
    /// </summary>
    public (double? ShareD1, double? Avg5, double? Z) KnownBefore(string symbol, DateOnly dateEt, Func<DateOnly, DateOnly> previousTradingDay)
    {
        if (!shareBySymbol.TryGetValue(symbol, out var days)) return (null, null, null);
        var prior = new List<DateOnly>(1 + ZBaselineDays);
        var d = dateEt;
        for (int i = 0; i <= ZBaselineDays; i++)
        {
            d = previousTradingDay(d);
            if (d >= dateEt) return (null, null, null);
            prior.Add(d);
        }
        double? d1 = days.TryGetValue(prior[0], out var v1) ? v1 : null;
        var recent = prior.Take(AvgDays).Where(days.ContainsKey).Select(x => days[x]).ToList();
        double? avg = recent.Count > 0 ? recent.Average() : null;
        double? z = null;
        var baseline = prior.Skip(1).Where(days.ContainsKey).Select(x => days[x]).ToList();
        if (d1 is { } x1 && baseline.Count >= ZMinBaselineDays)
        {
            double mean = baseline.Average();
            double sd = Math.Sqrt(baseline.Sum(b => (b - mean) * (b - mean)) / (baseline.Count - 1));
            if (sd > 1e-9) z = (x1 - mean) / sd;
        }
        return (d1, avg, z);
    }
}

/// <summary>Reg SHO threshold and daily short-volume features at day D (all known before 04:00 ET on D).</summary>
public static class RegShoFeatures
{
    public const string ListedD1 = "x.regShoListedD1";
    public const string DaysListed = "x.regShoDaysListed";
    public const string ShortVolShareD1 = "x.shortVolShareD1";
    public const string ShortVolShareAvg5 = "x.shortVolShareAvg5";
    public const string ShortVolShareZ = "x.shortVolShareZ";
}
