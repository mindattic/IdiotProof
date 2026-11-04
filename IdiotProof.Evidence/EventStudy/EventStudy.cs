using IdiotProof.Evidence.Search;

namespace IdiotProof.Evidence.EventStudy;

/// <summary>One dated event on one symbol. <paramref name="Sign"/> is +1 for a bullish claim, −1 bearish, 0 neutral.</summary>
public sealed record EventSpec(string Symbol, DateOnly Date, string Group, int Sign = 0);

/// <summary>Event window in trading days relative to day 0, inclusive on both ends, e.g. [-1, 1].</summary>
public readonly record struct EventWindow(int Start, int End)
{
    public int Length => End - Start + 1;
    public override string ToString() => $"[{Start},{End}]";
}

public sealed record EventStudyOptions
{
    /// <summary>Trading days in the estimation window.</summary>
    public int EstimationLength { get; init; } = 120;

    /// <summary>Trading days left empty between the estimation window and the earliest event-window day.</summary>
    public int Gap { get; init; } = 10;

    /// <summary>Fewest usable estimation returns (thinly traded names miss days).</summary>
    public int MinEstimationObservations { get; init; } = 60;
}

/// <summary>Daily close-to-close returns keyed by ET trading day.</summary>
public sealed class ReturnSeries
{
    private readonly Dictionary<DateOnly, double> returns;
    public ReturnSeries(IDictionary<DateOnly, double> returns) => this.returns = new Dictionary<DateOnly, double>(returns);

    public bool TryGet(DateOnly day, out double r) => returns.TryGetValue(day, out r);

    /// <summary>Returns from consecutive closes: a day's return exists only when the previous calendar trading day (per <paramref name="calendar"/>) also has a close.</summary>
    public static ReturnSeries FromCloses(IReadOnlyDictionary<DateOnly, double> closes, IReadOnlyList<DateOnly> calendar)
    {
        var r = new Dictionary<DateOnly, double>();
        for (int i = 1; i < calendar.Count; i++)
            if (closes.TryGetValue(calendar[i], out var c) && closes.TryGetValue(calendar[i - 1], out var p) && p > 0)
                r[calendar[i]] = c / p - 1;
        return new ReturnSeries(r);
    }
}

/// <summary>One event's result, or why it was dropped.</summary>
public sealed record EventResult(EventSpec Event, DateOnly? Day0, double Car, double Scar, int EstimationObservations, string? Skipped)
{
    public bool Valid => Skipped is null;
}

/// <summary>Cross-sectional summary of a group of events for one window.</summary>
public sealed record EventGroupResult(string Group, EventWindow Window, int Events, double MeanCar, double MedianCar, double FractionPositive,
    double CrossSectionalT, double CrossSectionalP, double PatellZ, double PatellP, double BmpT, double BmpP, double BootstrapP,
    double MeanSignedCar, double SignedBmpT, double SignedBmpP, int SignedEvents);

/// <summary>
/// Market-model event study (MacKinlay 1997): per event, fit α, β on an estimation window that ends
/// <see cref="EventStudyOptions.Gap"/> trading days before the earliest event-window day, then cumulate
/// abnormal returns over the window. Significance across events: the plain cross-sectional t on CARs,
/// Patell's Z (standardized by each event's own forecast error), Boehmer-Musumeci-Poulsen's t (Patell's
/// standardization with the cross-sectional variance, robust to event-induced variance) and a seeded
/// placebo bootstrap over random non-event days of the same symbols.
/// </summary>
public sealed class EventStudyEngine
{
    private readonly IReadOnlyList<DateOnly> calendar;
    private readonly Dictionary<DateOnly, int> dayIndex;
    private readonly ReturnSeries market;
    private readonly IReadOnlyDictionary<string, ReturnSeries> stocks;
    private readonly EventStudyOptions options;

    public EventStudyEngine(IReadOnlyList<DateOnly> calendar, ReturnSeries market, IReadOnlyDictionary<string, ReturnSeries> stocks, EventStudyOptions? options = null)
    {
        this.calendar = calendar.OrderBy(d => d).ToList();
        dayIndex = new Dictionary<DateOnly, int>();
        for (int i = 0; i < this.calendar.Count; i++) dayIndex[this.calendar[i]] = i;
        this.market = market;
        this.stocks = stocks;
        this.options = options ?? new EventStudyOptions();
    }

    /// <summary>Day 0 = the first trading day on or after the event date.</summary>
    public int? Day0Index(DateOnly date)
    {
        int lo = 0, hi = calendar.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (calendar[mid] < date) lo = mid + 1; else hi = mid; }
        return lo < calendar.Count ? lo : null;
    }

    /// <summary>Estimation-window index range [start, end) for an event at day-0 index <paramref name="day0"/>; always ends before the event window.</summary>
    public (int Start, int End) EstimationRange(int day0, EventWindow window)
    {
        int end = day0 + Math.Min(0, window.Start) - options.Gap;
        return (end - options.EstimationLength, end);
    }

    public EventResult Compute(EventSpec ev, EventWindow window) =>
        Day0Index(ev.Date) is { } d0 ? ComputeAt(ev, d0, window) : new EventResult(ev, null, double.NaN, double.NaN, 0, "no trading day on or after the event");

    private EventResult ComputeAt(EventSpec ev, int day0, EventWindow window)
    {
        var day = calendar[day0];
        if (!stocks.TryGetValue(ev.Symbol, out var stock)) return new EventResult(ev, day, double.NaN, double.NaN, 0, "no bars");
        var (estStart, estEnd) = EstimationRange(day0, window);
        if (estStart < 0) return new EventResult(ev, day, double.NaN, double.NaN, 0, "estimation window before data");
        if (day0 + window.End >= calendar.Count) return new EventResult(ev, day, double.NaN, double.NaN, 0, "event window past data");

        var s = new List<double>(options.EstimationLength);
        var m = new List<double>(options.EstimationLength);
        for (int i = estStart; i < estEnd; i++)
            if (stock.TryGet(calendar[i], out var rs) && market.TryGet(calendar[i], out var rm)) { s.Add(rs); m.Add(rm); }
        if (s.Count < options.MinEstimationObservations) return new EventResult(ev, day, double.NaN, double.NaN, s.Count, "too few estimation days");
        var model = MarketModel.Fit(s, m);
        if (model is null || model.ResidualVariance <= 0) return new EventResult(ev, day, double.NaN, double.NaN, s.Count, "degenerate estimation");

        double car = 0, sarSum = 0;
        for (int t = window.Start; t <= window.End; t++)
        {
            var d = calendar[day0 + t];
            if (!stock.TryGet(d, out var rs) || !market.TryGet(d, out var rm))
                return new EventResult(ev, day, double.NaN, double.NaN, s.Count, "missing bar in event window");
            double ar = model.AbnormalReturn(rs, rm);
            car += ar;
            sarSum += ar / Math.Sqrt(model.PredictionVariance(rm));
        }
        return new EventResult(ev, day, car, sarSum / Math.Sqrt(window.Length), s.Count, null);
    }

    public EventGroupResult Aggregate(string group, IReadOnlyList<EventResult> results, EventWindow window, int bootstrapDraws = 1000, int seed = 1337)
    {
        var valid = results.Where(r => r.Valid).ToList();
        int n = valid.Count;
        var cars = valid.Select(r => r.Car).ToList();
        var scars = valid.Select(r => r.Scar).ToList();

        double meanCar = n == 0 ? double.NaN : cars.Average();
        double median = n == 0 ? double.NaN : Median(cars);
        double fracPos = n == 0 ? double.NaN : (double)cars.Count(c => c > 0) / n;
        var (tCross, pCross) = TTest(cars);

        double patellZ = double.NaN, patellP = double.NaN;
        if (n > 0)
        {
            double varSum = valid.Sum(r => r.EstimationObservations > 4 ? (r.EstimationObservations - 2.0) / (r.EstimationObservations - 4.0) : 1.0);
            patellZ = scars.Sum() / Math.Sqrt(varSum);
            patellP = 2 * (1 - Distributions.NormalCdf(Math.Abs(patellZ)));
        }
        var (bmpT, bmpP) = TTest(scars);

        var signed = valid.Where(r => r.Event.Sign != 0).Select(r => r.Scar * r.Event.Sign).ToList();
        double meanSigned = valid.Where(r => r.Event.Sign != 0).Select(r => r.Car * r.Event.Sign).DefaultIfEmpty(double.NaN).Average();
        var (sT, sP) = TTest(signed);

        double bootP = n == 0 ? double.NaN : PlaceboBootstrap(valid, window, meanCar, bootstrapDraws, seed);
        return new EventGroupResult(group, window, n, meanCar, median, fracPos, tCross, pCross, patellZ, patellP, bmpT, bmpP, bootP,
            meanSigned, sT, sP, signed.Count);
    }

    /// <summary>
    /// Two-sided placebo p: draw, for each real event, a random trading day of the same symbol whose
    /// event window doesn't touch any real event window of that symbol, and compare the placebo mean CAR's
    /// magnitude with the observed one.
    /// </summary>
    public double PlaceboBootstrap(IReadOnlyList<EventResult> valid, EventWindow window, double observedMean, int draws, int seed)
    {
        if (valid.Count == 0 || draws < 1) return double.NaN;
        var rng = new Random(seed);
        var blocked = new Dictionary<string, HashSet<int>>();
        foreach (var r in valid)
        {
            if (!blocked.TryGetValue(r.Event.Symbol, out var set)) blocked[r.Event.Symbol] = set = new HashSet<int>();
            int d0 = dayIndex[r.Day0!.Value];
            for (int i = d0 + window.Start - window.Length; i <= d0 + window.End + window.Length; i++) set.Add(i);
        }

        var candidates = new Dictionary<string, List<int>>();
        foreach (var sym in valid.Select(r => r.Event.Symbol).Distinct())
        {
            var list = new List<int>();
            for (int d0 = 0; d0 < calendar.Count; d0++)
            {
                if (blocked[sym].Contains(d0)) continue;
                var probe = ComputeAt(new EventSpec(sym, calendar[d0], "placebo"), d0, window);
                if (probe.Valid) list.Add(d0);
            }
            candidates[sym] = list;
        }

        int extreme = 0, used = 0;
        for (int b = 0; b < draws; b++)
        {
            double sum = 0;
            int count = 0;
            foreach (var r in valid)
            {
                var pool = candidates[r.Event.Symbol];
                if (pool.Count == 0) continue;
                int d0 = pool[rng.Next(pool.Count)];
                var res = ComputeAt(new EventSpec(r.Event.Symbol, calendar[d0], "placebo"), d0, window);
                sum += res.Car;
                count++;
            }
            if (count == 0) continue;
            used++;
            if (Math.Abs(sum / count) >= Math.Abs(observedMean)) extreme++;
        }
        return used == 0 ? double.NaN : (1.0 + extreme) / (used + 1.0);
    }

    private static (double T, double P) TTest(IReadOnlyList<double> xs)
    {
        int n = xs.Count;
        if (n < 2) return (double.NaN, double.NaN);
        double mean = xs.Average();
        double sd = EvidenceStats.StdDev(xs);
        if (sd <= 0) return (double.NaN, double.NaN);
        double t = mean / (sd / Math.Sqrt(n));
        return (t, 2 * Distributions.StudentTUpperTail(Math.Abs(t), n - 1));
    }

    private static double Median(List<double> xs)
    {
        var s = xs.OrderBy(x => x).ToList();
        int n = s.Count;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
    }
}
