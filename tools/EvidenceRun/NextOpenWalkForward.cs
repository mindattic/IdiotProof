using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Models;

/// <summary>
/// The analog walk-forward of EvidenceWalkForward (e8930f5) with the two IP-T375 protocol amendments:
/// (1) every label and every call enters at the NEXT bar's open and exits at the horizon bar's close
/// (bid-ask bounce cannot score; IP-T405), with close-entry scoring of the same calls kept only as a
/// diagnostic; (2) a symbol-day is excluded when a split (corporate action, or a split-sized daily or
/// overnight move: above +200% or below -66.7%, IP-T407) falls anywhere in its training+test span,
/// because the bar store is split-unadjusted (IP-T410).
/// </summary>
sealed class NextOpenWalkForward(IHistoricalBarStore store, IReadOnlyDictionary<string, IReadOnlyList<Candle>> daily,
    IReadOnlyDictionary<string, List<DateOnly>> splitExDates)
{
    public const double MaxUp = 3.0, MaxDown = 1.0 / 3.0;
    private static readonly TimeZoneInfo Et = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public sealed record Settings(int Window, int Horizon, double MinAgreement, int TrainDays = 20, int MinTrainDays = 3,
        int K = 25, double MinSimilarity = 0.8, int MinNeighbors = 8, int Seed = 1337);

    public sealed record Result(DirectionalScorecard NextOpen, DirectionalScorecard CloseEntry,
        List<DayTally> NextOpenDays, List<DayTally> CloseDays, int SymbolDays, int Points, int Ups, int Downs,
        int Skipped, int SplitExcludedByAction, int SplitExcludedByGap, int Symbols,
        int Ties, DirectionalScorecard NextOpenNoTies, List<DayTally> NoTieDays);

    public async Task<Result> RunAsync(IReadOnlyDictionary<string, SortedSet<DateOnly>> plan, Settings s)
    {
        var next = new DirectionalScorecard();
        var close = new DirectionalScorecard();
        var nextDays = new Dictionary<DateOnly, (int Calls, int Hits)>();
        var closeDays = new Dictionary<DateOnly, (int Calls, int Hits)>();
        var noTie = new DirectionalScorecard();
        var noTieDays = new Dictionary<DateOnly, (int Calls, int Hits)>();
        int ties = 0;
        int symbolDays = 0, points = 0, ups = 0, downs = 0, skipped = 0, byAction = 0, byGap = 0;

        foreach (var chunk in plan.Where(p => p.Value.Count > 0).OrderBy(p => p.Key, StringComparer.Ordinal).Chunk(8))
        {
            var from = chunk.Min(p => p.Value.Min).AddDays(-(s.TrainDays * 3 / 2 + 7));
            var to = chunk.Max(p => p.Value.Max);
            var bars = await store.GetBarsForSymbolsAsync(chunk.Select(p => p.Key), from, to, BarTimeframe.Minute1);

            foreach (var (symbol, days) in chunk)
            {
                var series = bars.TryGetValue(symbol, out var raw)
                    ? raw.Where(c => c.IsValid && c.Close > 0 && InRth(c)).ToList()
                    : new List<Candle>();
                var byDay = series.GroupBy(DateEt).ToDictionary(g => g.Key, g => g.ToList());
                var sessions = byDay.Keys.OrderBy(d => d).ToList();

                foreach (var day in days)
                {
                    if (!byDay.TryGetValue(day, out var dayBars)) { skipped++; continue; }
                    var prior = sessions.Where(d => d < day).TakeLast(s.TrainDays).ToList();
                    if (prior.Count < s.MinTrainDays) { skipped++; continue; }

                    var span = prior.Append(day).ToList();
                    if (splitExDates.TryGetValue(symbol, out var exDates) && exDates.Any(x => x > span[0] && x <= day)) { byAction++; continue; }
                    if (SplitSizedMove(symbol, span, byDay)) { byGap++; continue; }

                    var training = prior.SelectMany(d => byDay[d]).ToList();
                    var index = new LshIndex<double>(s.Window, seed: s.Seed);
                    int sessionStart = 0;
                    foreach (var d in prior)
                    {
                        int sessionEnd = sessionStart + byDay[d].Count;
                        for (int end = Math.Max(sessionStart, s.Window - 1); end < sessionEnd; end++)
                        {
                            if (end + s.Horizon >= sessionEnd) break;
                            double entry = (double)training[end + 1].Open;
                            if (entry <= 0) continue;
                            double fwd = (double)training[end + s.Horizon].Close / entry - 1;
                            index.Add(ShapeVector.FromWindow(training, end, s.Window), fwd);
                        }
                        sessionStart = sessionEnd;
                    }
                    if (index.Count == 0) { skipped++; continue; }

                    var tail = training.Skip(Math.Max(0, training.Count - (s.Window - 1))).ToList();
                    var test = tail.Concat(dayBars).ToList();
                    int firstInDay = tail.Count;
                    int dayNextCalls = 0, dayNextHits = 0, dayCloseCalls = 0, dayCloseHits = 0, dayNtCalls = 0, dayNtHits = 0;
                    for (int end = Math.Max(firstInDay, s.Window - 1); end + s.Horizon < test.Count; end += s.Horizon)
                    {
                        var call = Vote(index, ShapeVector.FromWindow(test, end, s.Window), s);
                        double entryNext = (double)test[end + 1].Open;
                        double exit = (double)test[end + s.Horizon].Close;
                        double fwdNext = entryNext > 0 ? exit / entryNext - 1 : 0;
                        double fwdClose = exit / (double)test[end].Close - 1;
                        points++;
                        if (fwdNext > 0) ups++; else if (fwdNext < 0) downs++;

                        int nextHitsBefore = next.Hits, closeHitsBefore = close.Hits;
                        next.Record(call, fwdNext);
                        close.Record(call, fwdClose);
                        if (call != Direction.Flat)
                        {
                            dayNextCalls++; dayNextHits += next.Hits - nextHitsBefore;
                            dayCloseCalls++; dayCloseHits += close.Hits - closeHitsBefore;
                            if (fwdNext == 0) ties++;
                            else
                            {
                                int ntBefore = noTie.Hits;
                                noTie.Record(call, fwdNext);
                                dayNtCalls++; dayNtHits += noTie.Hits - ntBefore;
                            }
                        }
                    }
                    symbolDays++;
                    Add(nextDays, day, dayNextCalls, dayNextHits);
                    Add(closeDays, day, dayCloseCalls, dayCloseHits);
                    Add(noTieDays, day, dayNtCalls, dayNtHits);
                }
            }
        }

        static List<DayTally> Tallies(Dictionary<DateOnly, (int Calls, int Hits)> d) =>
            d.OrderBy(kv => kv.Key).Select(kv => new DayTally(kv.Key, kv.Value.Calls, kv.Value.Hits)).ToList();
        return new Result(next, close, Tallies(nextDays), Tallies(closeDays), symbolDays, points, ups, downs, skipped, byAction, byGap, plan.Count,
            ties, noTie, Tallies(noTieDays));
    }

    /// <summary>Same voting rule as PatternAnalogPredictor.Predict (dead band 0).</summary>
    private static Direction Vote(LshIndex<double> index, double[] shape, Settings s)
    {
        var neighbors = index.Query(shape, s.K, s.MinSimilarity);
        int up = neighbors.Count(n => n.Payload > 0), down = neighbors.Count(n => n.Payload < 0);
        int decided = up + down;
        double pUp = (up + 1.0) / (decided + 2.0);
        if (neighbors.Count < s.MinNeighbors || decided == 0) return Direction.Flat;
        if (pUp >= s.MinAgreement) return Direction.Up;
        if (1 - pUp >= s.MinAgreement) return Direction.Down;
        return Direction.Flat;
    }

    /// <summary>A daily close-to-close or overnight (last close to next first open) move beyond the split thresholds anywhere in the span.</summary>
    private bool SplitSizedMove(string symbol, List<DateOnly> span, Dictionary<DateOnly, List<Candle>> byDay)
    {
        for (int i = 1; i < span.Count; i++)
        {
            double r = (double)byDay[span[i]][0].Open / (double)byDay[span[i - 1]][^1].Close;
            if (r > MaxUp || r < MaxDown) return true;
        }
        if (daily.TryGetValue(symbol, out var d))
        {
            var inSpan = d.Where(c => { var x = DateOnly.FromDateTime(c.StartUtc); return x >= span[0] && x <= span[^1]; })
                .OrderBy(c => c.StartUtc).ToList();
            for (int i = 1; i < inSpan.Count; i++)
            {
                if (inSpan[i - 1].Close <= 0) continue;
                double r = (double)(inSpan[i].Close / inSpan[i - 1].Close);
                if (r > MaxUp || r < MaxDown) return true;
            }
        }
        return false;
    }

    private static void Add(Dictionary<DateOnly, (int Calls, int Hits)> d, DateOnly day, int calls, int hits)
    {
        d.TryGetValue(day, out var v);
        d[day] = (v.Calls + calls, v.Hits + hits);
    }

    private static DateOnly DateEt(Candle c) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(c.StartUtc, DateTimeKind.Utc), Et));

    private static bool InRth(Candle c)
    {
        var tod = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(c.StartUtc, DateTimeKind.Utc), Et).TimeOfDay;
        return tod >= new TimeSpan(9, 30, 0) && tod < new TimeSpan(16, 0, 0);
    }
}
