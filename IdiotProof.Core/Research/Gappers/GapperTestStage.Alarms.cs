using System.Diagnostics;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// Amendment 04 (IP-T475): the detection alarm finalist R4 against the gainers + news benchmark B4.
/// Every population morning of the split (candidates at weight 1, amendment 01's sample at 1/rate) is
/// swept minute by minute 06:00-09:10 ET through the same <see cref="AlarmSweep"/> and
/// <see cref="GapperAlarmData"/> code <c>explore alarms</c> uses, with the frozen train time-of-day
/// profile and nothing fitted on the split. H25 compares hit rate and false alarms per 100 scorable
/// mornings (whole-day bootstrap), H26 reads R4's weighted median lead. One cached sweep per split.
/// </summary>
public sealed partial class GapperTestStage
{
    /// <summary>Index of R4 in the sweep's rule pair (B4 is 1).</summary>
    private const int AlarmRule = 0;

    private readonly Dictionary<string, AlarmPanel> alarmPanels = new(StringComparer.Ordinal);

    private sealed record AlarmRuleSummary(List<AlarmLead> Leads, List<double> Lead10, List<double> PmRetAtAlarm, int HitRows, int FalseRows, int AlarmRows);

    private sealed record AlarmPanel(List<AlarmDayTotals> Days, AlarmRuleSummary[] Rules, int Rows, int CandidateRows, int SampleRows, int SkippedRows,
        int FormingRows, int ScorableRows, double Seconds);

    private async Task<AlarmPanel> AlarmPanelAsync(string split, CancellationToken ct)
    {
        if (alarmPanels.TryGetValue(split, out var cached)) return cached;
        var clock = Stopwatch.StartNew();
        var profilePath = s.Paths.ProfileFile(false);
        if (!File.Exists(profilePath)) throw new InvalidOperationException($"{Path.GetFileName(profilePath)} does not exist; the frozen train time-of-day profile is required.");
        var profile = TimeOfDayProfile.Load(profilePath);
        var store = await s.StoreAsync(ct);
        var amendment = s.Amendment ?? throw new InvalidOperationException("Amendment 01's population sample is required for amendment 04.");
        var reachEt = GapperStudyParams.ParseEt(P.Labels.Reach20.ByEt);
        var population = (await s.StudyDaysAsync(split, null, null, ct))
            .Select(u => (U: u, Sampled: !u.IsCandidate && amendment.IsSampleMember(u.Symbol, u.DateEt)))
            .Where(x => x.U.IsCandidate || x.Sampled).ToList();

        var rules = new[] { GapperAlarmData.R4, GapperAlarmData.B4 };
        var summaries = rules.Select(_ => new AlarmRuleSummary([], [], [], 0, 0, 0)).ToArray();
        int[] hitRows = new int[2], falseRows = new int[2], alarmRows = new int[2];
        var days = new List<AlarmDayTotals>();
        int rowsUsed = 0, candidates = 0, sampled = 0, skipped = 0, forming = 0, scorable = 0;
        foreach (var group in population.GroupBy(x => x.U.DateEt).OrderBy(g => g.Key))
        {
            ct.ThrowIfCancellationRequested();
            var day = group.Key;
            var rows = group.ToList();
            var symbols = rows.Select(x => x.U.Symbol).Distinct().ToList();
            Dictionary<string, List<CatalystObservation>> news;
            await using (var db = await s.Db.CreateDbContextAsync(ct))
                news = await GapperAlarmData.NewsAsync(db, symbols, day, ct);

            // R4 and B4 read neither the trade-count CUSUM nor ADT, so neither is loaded (the volume CUSUM, gap, conf and dollar volume are unaffected).
            var computed = new AlarmMorning?[rows.Count];
            Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, i =>
            {
                var (u, isSample) = rows[i];
                computed[i] = GapperAlarmData.MorningOf(u, isSample ? amendment.SampleWeight : 1, isSample, store.ReadDay(u.Symbol, u.DateEt), null,
                    news.GetValueOrDefault(u.Symbol) ?? [], profile, null, reachEt, P.Labels.Reach20.Threshold);
            });
            var dayRows = computed.Where(r => r is not null).Select(r => r!).ToList();
            skipped += rows.Count - dayRows.Count;
            if (dayRows.Count == 0) continue;

            var scores = AlarmSweep.Scores(dayRows);
            double dScorable = 0, dForming = 0, dPopulation = 0;
            double[] dAlarms = new double[2], dHits = new double[2], dFalse = new double[2];
            var alarms = rules.Select(rule => AlarmSweep.RankAlarms(dayRows, scores.Sorted(rule.Score), rule)).ToArray();
            for (int r = 0; r < dayRows.Count; r++)
            {
                var row = dayRows[r];
                dPopulation += row.Weight;
                if (row.Scorable) { dScorable += row.Weight; scorable++; }
                if (row.Forming) { dForming += row.Weight; forming++; }
                if (row.Sampled) sampled++; else candidates++;
                for (int k = 0; k < rules.Length; k++)
                {
                    var o = AlarmSweep.Outcome(row, alarms[k][r]);
                    if (!o.Alarmed) continue;
                    dAlarms[k] += row.Weight; alarmRows[k]++;
                    if (o.False) { dFalse[k] += row.Weight; falseRows[k]++; }
                    if (o.Hit)
                    {
                        dHits[k] += row.Weight; hitRows[k]++;
                        summaries[k].Leads.Add(new AlarmLead(day, o.Lead20, row.Weight));
                        if (o.Lead10 is { } l10) summaries[k].Lead10.Add(l10);
                        summaries[k].PmRetAtAlarm.Add(o.PmRetAtAlarm);
                    }
                }
            }
            rowsUsed += dayRows.Count;
            days.Add(new AlarmDayTotals(day, dScorable, dForming, dPopulation, dAlarms, dHits, dFalse));
            s.Logger.LogInformation("[{Now}] test alarms {Split}: {Day} done ({Rows} rows, {Done} so far)", GapperStudyServices.Et(s.UtcNow), split, day, dayRows.Count, rowsUsed);
        }
        var result = new AlarmPanel(days, summaries.Select((x, k) => x with { HitRows = hitRows[k], FalseRows = falseRows[k], AlarmRows = alarmRows[k] }).ToArray(),
            rowsUsed, candidates, sampled, skipped, forming, scorable, clock.Elapsed.TotalSeconds);
        alarmPanels[split] = result;
        return result;
    }

    private void DescribeAlarms(HypothesisRun run, AlarmPanel a, string split)
    {
        run.Inputs.N = a.Rows;
        run.Inputs.Days = a.Days.Count;
        run.Inputs.FirstDate = a.Days.Count == 0 ? null : a.Days.Min(d => d.Day).ToString("yyyy-MM-dd");
        run.Inputs.LastDate = a.Days.Count == 0 ? null : a.Days.Max(d => d.Day).ToString("yyyy-MM-dd");
        run.Inputs.Population = $"population mornings on {split}: {a.CandidateRows:N0} candidate days (weight 1) and {a.SampleRows:N0} population-sample days (weight {s.Amendment?.SampleWeight ?? 0}); " +
            $"{a.ScorableRows:N0} scorable (below +20% at 06:00), {a.FormingRows:N0} forming; {a.SkippedRows:N0} skipped (no prior close or no bars by the reach cut-off)";
        run.Inputs.FeatureColumns = [$"R4: {GapperAlarmData.R4Name}", $"B4: {GapperAlarmData.B4Name}", "swept 06:00-09:10 ET, first alarm per morning per rule, frozen train tod-profile.json"];
        run.Inputs.LabelColumns = ["Reach20By0945 (bar highs, first +20% touch)"];
    }

    /// <summary>Both rules side by side on every A04 part, so the report can show them without recomputing.</summary>
    private static void AlarmStatistics(HypothesisPart part, AlarmPanel a)
    {
        string[] keys = ["r4", "b4"];
        for (int k = 0; k < 2; k++)
        {
            var sum = a.Rules[k];
            var leadsWeighted = CrossSectionalRank.WeightedMedian(sum.Leads.Select(l => l.Minutes).ToList(), sum.Leads.Select(l => l.Weight).ToList());
            part.Statistic[keys[k] + "HitRate"] = AlarmComparison.HitRate(a.Days, k);
            part.Statistic[keys[k] + "FaPer100"] = AlarmComparison.FaPer100(a.Days, k);
            part.Statistic[keys[k] + "FaPer100AllRows"] = AlarmComparison.FaPer100(a.Days, k, allRows: true);
            part.Statistic[keys[k] + "AlarmsPerDay"] = AlarmComparison.AlarmsPerDay(a.Days, k);
            part.Statistic[keys[k] + "WeightedHits"] = a.Days.Sum(d => d.Hits[k]);
            part.Statistic[keys[k] + "WeightedFalse"] = a.Days.Sum(d => d.False[k]);
            part.Statistic[keys[k] + "HitRows"] = sum.HitRows;
            part.Statistic[keys[k] + "FalseRows"] = sum.FalseRows;
            part.Statistic[keys[k] + "AlarmRows"] = sum.AlarmRows;
            part.Statistic[keys[k] + "MedianLead"] = leadsWeighted;
            part.Statistic[keys[k] + "MedianLeadUnweighted"] = sum.Leads.Count == 0 ? null : Quantile(sum.Leads.Select(l => l.Minutes).ToList(), 0.5);
            part.Statistic[keys[k] + "LeadQ1"] = sum.Leads.Count == 0 ? null : Quantile(sum.Leads.Select(l => l.Minutes).ToList(), 0.25);
            part.Statistic[keys[k] + "LeadQ3"] = sum.Leads.Count == 0 ? null : Quantile(sum.Leads.Select(l => l.Minutes).ToList(), 0.75);
            part.Statistic[keys[k] + "Lead10Median"] = sum.Lead10.Count == 0 ? null : Quantile(sum.Lead10, 0.5);
            part.Statistic[keys[k] + "PmRetAtAlarmMedian"] = sum.PmRetAtAlarm.Count == 0 ? null : Quantile(sum.PmRetAtAlarm, 0.5);
        }
        part.Statistic["days"] = a.Days.Count;
        part.Statistic["rows"] = a.Rows;
        part.Statistic["candidateRows"] = a.CandidateRows;
        part.Statistic["sampleRows"] = a.SampleRows;
        part.Statistic["scorableRows"] = a.ScorableRows;
        part.Statistic["formingRows"] = a.FormingRows;
        part.Statistic["skippedRows"] = a.SkippedRows;
        part.Statistic["populationWeight"] = a.Days.Sum(d => d.Population);
        part.Statistic["scorableWeight"] = a.Days.Sum(d => d.Scorable);
        part.Statistic["formingWeight"] = a.Days.Sum(d => d.Forming);
        part.Statistic["sweepSeconds"] = Math.Round(a.Seconds, 1);
        part.Statistic["faDenominator"] = "weighted scorable mornings (below +20% at 06:00); faPer100AllRows divides by every population row as explore alarms did";
        part.Statistic["leadMedian"] = "weighted median (CrossSectionalRank.WeightedMedian); medianLeadUnweighted is explore's unweighted linear-interpolated median";
    }

    private static double Quantile(List<double> values, double q)
    {
        var v = values.OrderBy(x => x).ToList();
        if (v.Count == 1) return v[0];
        double pos = q * (v.Count - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(v.Count - 1, lo + 1);
        return v[lo] + (v[hi] - v[lo]) * (pos - lo);
    }

    // ── H25: R4 hit rate and false alarms against B4 ─────────────────────

    private async Task AlarmHitAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var a = await AlarmPanelAsync(split, ct);
        DescribeAlarms(run, a, split);
        double minHit = h.MinEffectNumber("hitRateDifference"), maxFa = h.MinEffectNumber("maxFaPer100Difference");
        var hit = new HypothesisPart { Name = "hitRateDifference", EffectName = "hitRateDifference", MinEffect = minHit, Direction = EffectDirection.AtLeast, N = a.FormingRows, Days = a.Days.Count };
        var fa = new HypothesisPart { Name = "faPer100Difference", EffectName = "faPer100Difference", MinEffect = maxFa, Direction = EffectDirection.AtMost, N = a.ScorableRows, Days = a.Days.Count };
        if (a.FormingRows < P.Statistics.MinBucketN || a.Days.Count < MinDays)
        {
            foreach (var part in new[] { hit, fa })
            {
                part.Verdict = HypothesisVerdict.Void;
                part.VerdictFixed = true;
                part.Reasons.Add($"{a.FormingRows} forming mornings (minBucketN {P.Statistics.MinBucketN}) on {a.Days.Count} days (minimum {MinDays})");
                AlarmStatistics(part, a);
                run.Parts.Add(part);
            }
            return;
        }
        var (dHit, dFa) = AlarmComparison.Differences(a.Days, P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);

        hit.Effect = dHit.Estimate;
        hit.CiLow = double.IsNaN(dHit.Low) ? null : dHit.Low;
        hit.CiHigh = double.IsNaN(dHit.High) ? null : dHit.High;
        hit.RawP = dHit.PValueTwoSided;
        hit.MeetsMinimum = dHit.Estimate >= minHit;
        // Amendment 04's rule, fixed here: supported at >= min with the lower bound above 0; refuted when the upper bound is below min.
        hit.Verdict = hit.MeetsMinimum == true && hit.CiLow > 0 ? HypothesisVerdict.Supported : hit.CiHigh < minHit ? HypothesisVerdict.Refuted : HypothesisVerdict.Undecided;
        hit.VerdictFixed = true;
        hit.Reasons.Add($"verdict (amendment 04): hit-rate difference {HypothesisCorrections.Fmt(hit.Effect)} [{HypothesisCorrections.Fmt(hit.CiLow)}, {HypothesisCorrections.Fmt(hit.CiHigh)}]; " +
            $"supported when >= {minHit} with lower bound > 0, refuted when upper bound < {minHit}");

        fa.Effect = dFa.Estimate;
        fa.CiLow = double.IsNaN(dFa.Low) ? null : dFa.Low;
        fa.CiHigh = double.IsNaN(dFa.High) ? null : dFa.High;
        fa.MeetsMinimum = dFa.Estimate <= maxFa;
        fa.Verdict = fa.MeetsMinimum == true ? HypothesisVerdict.Supported : fa.CiLow > maxFa ? HypothesisVerdict.Refuted : HypothesisVerdict.Undecided;
        fa.VerdictFixed = true;
        fa.Reasons.Add($"verdict (amendment 04): false-alarm difference per 100 mornings {HypothesisCorrections.Fmt(fa.Effect)} [{HypothesisCorrections.Fmt(fa.CiLow)}, {HypothesisCorrections.Fmt(fa.CiHigh)}]; " +
            $"holds when the point estimate <= {maxFa}, refuted when the lower bound > {maxFa}");

        foreach (var part in new[] { hit, fa })
        {
            AlarmStatistics(part, a);
            part.Statistic["bootstrap"] = $"whole-day resampling ({dHit.Draws} draws, seed {P.Statistics.Seed}, {P.Statistics.CiLevel:P0}) of the weighted rate differences, weights kept";
            part.Statistic["compared"] = "R4 - B4";
            run.Parts.Add(part);
        }
    }

    // ── H26: R4's median lead to the first +20% ──────────────────────────

    private async Task AlarmLeadAsync(GapperHypothesis h, HypothesisRun run, string split, CancellationToken ct)
    {
        var a = await AlarmPanelAsync(split, ct);
        DescribeAlarms(run, a, split);
        var min = h.MinEffectNumber("medianLeadMinutes");
        var leads = a.Rules[AlarmRule].Leads;
        var part = new HypothesisPart { Name = "medianLeadMinutes", EffectName = "medianLeadMinutes", MinEffect = min, Direction = EffectDirection.AtLeast, N = leads.Count, Days = leads.Select(l => l.Day).Distinct().Count() };
        AlarmStatistics(part, a);
        if (leads.Count < P.Statistics.MinBucketN)
        {
            part.Verdict = HypothesisVerdict.Void;
            part.VerdictFixed = true;
            part.Reasons.Add($"{leads.Count} R4 hits, fewer than minBucketN {P.Statistics.MinBucketN}");
            run.Parts.Add(part);
            return;
        }
        var m = AlarmComparison.MedianLead(leads, P.Statistics.BootstrapDraws, P.Statistics.Seed, P.Statistics.CiLevel, MinDays);
        part.Effect = m.Estimate;
        part.CiLow = double.IsNaN(m.Low) ? null : m.Low;
        part.CiHigh = double.IsNaN(m.High) ? null : m.High;
        part.MeetsMinimum = m.Estimate >= min;
        part.Statistic["bootstrap"] = $"day-clustered resampling of R4 hits ({m.Draws} draws, seed {P.Statistics.Seed}, {P.Statistics.CiLevel:P0}), weighted median";
        run.Parts.Add(part);
    }
}
