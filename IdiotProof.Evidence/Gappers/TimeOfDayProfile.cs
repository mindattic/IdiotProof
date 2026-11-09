using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// How much of a full day's volume (04:00-20:00 ET) has normally printed by each clock minute, keyed
/// by ADV decile: the median across control days of the cumulative fraction at that minute. Turns a
/// premarket cumulative volume into a time-of-day-aware ratio against ADV, so "3x normal at 07:00"
/// means the same thing at 07:00 and at 09:25. Fit on the study's training period, stored as JSON
/// and shipped to the live Monitor; the same object serves both.
/// </summary>
public sealed class TimeOfDayProfile
{
    public const int CurrentVersion = 1;
    public const int DecileCount = 10;
    public const int MinutesPerDay = 16 * 60;

    private readonly double[][] cumulative;

    public TimeOfDayProfile(IReadOnlyList<IReadOnlyList<double>> cumulativeFractionByDecile, int version = CurrentVersion)
    {
        if (cumulativeFractionByDecile.Count != DecileCount)
            throw new ArgumentException($"A profile needs {DecileCount} deciles, got {cumulativeFractionByDecile.Count}.");
        cumulative = new double[DecileCount][];
        for (int d = 0; d < DecileCount; d++)
        {
            if (cumulativeFractionByDecile[d].Count != MinutesPerDay + 1)
                throw new ArgumentException($"Decile {d} needs {MinutesPerDay + 1} minute points, got {cumulativeFractionByDecile[d].Count}.");
            cumulative[d] = cumulativeFractionByDecile[d].ToArray();
        }
        Version = version;
    }

    public int Version { get; }

    /// <summary>Cumulative fraction of a full day's volume printed by <paramref name="timeEt"/> (0 at 04:00, 1 at 20:00).</summary>
    public double FractionAt(int decile, TimeSpan timeEt)
    {
        var row = cumulative[ClampDecile(decile)];
        return row[MinuteIndex(timeEt)];
    }

    /// <summary>Expected share of the day's volume printed inside the single clock minute ending at <paramref name="timeEt"/>.</summary>
    public double MinuteFractionAt(int decile, TimeSpan timeEt)
    {
        var row = cumulative[ClampDecile(decile)];
        int i = MinuteIndex(timeEt);
        return i == 0 ? row[0] : row[i] - row[i - 1];
    }

    /// <summary>
    /// Cumulative volume so far against what ADV says should have printed by this clock time:
    /// cumVol / (adv20 x FractionAt). Null when the expectation is zero (nothing normally trades yet).
    /// </summary>
    public double? VolRatioTod(double cumVol, double adv20Shares, int decile, TimeSpan timeEt)
    {
        var expected = adv20Shares * FractionAt(decile, timeEt);
        return expected > 0 ? cumVol / expected : null;
    }

    /// <summary>
    /// Builds a profile from whole-day minute bars: each day's cumulative fraction curve, then the
    /// per-minute median across the days in each decile. Deciles with no days get a flat uniform curve.
    /// </summary>
    public static TimeOfDayProfile Build(IEnumerable<(int Decile, IReadOnlyList<Candle> DayBars)> days) => Build(days, b => (double)b.Volume);

    /// <summary>
    /// The trade-count profile (IP-T470): the same median cumulative-fraction curve, built from each
    /// bar's trade count (<see cref="GapperFormationFeatures.Trades"/>) instead of its volume, so the
    /// count CUSUM has a time-of-day expectation for prints as the volume CUSUM has for shares.
    /// </summary>
    public static TimeOfDayProfile BuildTradeCount(IEnumerable<(int Decile, IReadOnlyList<Candle> DayBars)> days) => Build(days, b => GapperFormationFeatures.Trades(b));

    /// <summary>Builds a profile of any per-bar measure (volume, trade count) the same way.</summary>
    public static TimeOfDayProfile Build(IEnumerable<(int Decile, IReadOnlyList<Candle> DayBars)> days, Func<Candle, double> measure)
    {
        var curves = new List<double[]>[DecileCount];
        for (int d = 0; d < DecileCount; d++) curves[d] = [];
        foreach (var (decile, bars) in days)
        {
            if (bars.Count == 0) continue;
            var curve = DayCurve(bars, measure);
            if (curve is not null) curves[ClampDecile(decile)].Add(curve);
        }

        var result = new double[DecileCount][];
        for (int d = 0; d < DecileCount; d++)
        {
            var row = new double[MinutesPerDay + 1];
            if (curves[d].Count == 0)
            {
                for (int m = 0; m <= MinutesPerDay; m++) row[m] = (double)m / MinutesPerDay;
            }
            else
            {
                var column = new double[curves[d].Count];
                for (int m = 0; m <= MinutesPerDay; m++)
                {
                    for (int k = 0; k < column.Length; k++) column[k] = curves[d][k][m];
                    row[m] = Median(column);
                }
            }
            result[d] = row;
        }
        return new TimeOfDayProfile(result);
    }

    private static double[]? DayCurve(IReadOnlyList<Candle> bars, Func<Candle, double> measure)
    {
        var day = MinuteBarMath.SessionDate(bars[0].EndUtc);
        var start = MinuteBarMath.ToUtc(day, MinuteBarMath.SessionStartEt);
        var perMinute = new double[MinutesPerDay + 1];
        double total = 0;
        foreach (var b in bars)
        {
            var idx = (int)Math.Round((b.EndUtc - start).TotalMinutes);
            if (idx < 1 || idx > MinutesPerDay) continue;
            var v = measure(b);
            perMinute[idx] += v;
            total += v;
        }
        if (total <= 0) return null;
        var curve = new double[MinutesPerDay + 1];
        double cum = 0;
        for (int m = 0; m <= MinutesPerDay; m++)
        {
            cum += perMinute[m];
            curve[m] = cum / total;
        }
        return curve;
    }

    public string ToJson() => JsonSerializer.Serialize(new Document
    {
        Version = Version,
        CumulativeFractionByDecile = cumulative,
    });

    public static TimeOfDayProfile FromJson(string json)
    {
        var doc = JsonSerializer.Deserialize<Document>(json)
            ?? throw new InvalidDataException("Time-of-day profile JSON is empty.");
        if (doc.Version != CurrentVersion)
            throw new InvalidDataException($"Time-of-day profile version {doc.Version} is not supported (this build reads {CurrentVersion}).");
        if (doc.CumulativeFractionByDecile is null)
            throw new InvalidDataException("Time-of-day profile JSON has no cumulativeFractionByDecile.");
        return new TimeOfDayProfile(doc.CumulativeFractionByDecile, doc.Version);
    }

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public static TimeOfDayProfile Load(string path) => FromJson(File.ReadAllText(path));

    private static double Median(double[] values)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }

    private static int ClampDecile(int decile) => Math.Clamp(decile, 0, DecileCount - 1);

    private static int MinuteIndex(TimeSpan timeEt)
    {
        var minutes = (int)Math.Floor((timeEt - MinuteBarMath.SessionStartEt).TotalMinutes);
        return Math.Clamp(minutes, 0, MinutesPerDay);
    }

    private sealed class Document
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("cumulativeFractionByDecile")] public double[][]? CumulativeFractionByDecile { get; set; }
    }
}
