using System.Diagnostics;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Gappers;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

public sealed record LabelStageRequest(string Split, bool Smoke, DateOnly? From, DateOnly? To);

public sealed record LabelStageResult(string Split, int SymbolDays, int Rows, int WithEntry, string? HoldoutNote, double Minutes);

/// <summary>
/// <c>research gappers label</c>: the frozen labels for every candidate day and control day of a
/// split at every decision time, from the file store's bars and the split-adjusted prior close.
/// Same holdout lock as the snapshot stage; labels are written once per (symbol, day, t).
/// </summary>
public sealed class GapperLabelStage(GapperStudyServices s)
{
    public async Task<LabelStageResult> RunAsync(LabelStageRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var p = s.Params;
        var split = req.Smoke ? GapperStudyServices.SmokeSplit : req.Split.ToLowerInvariant();
        var store = await s.StoreAsync(ct);
        string? holdoutNote = null;
        HoldoutLock? gate = null;
        if (split == "holdout")
        {
            gate = new HoldoutLock(s.Paths, p);
            var check = gate.Check("label");
            if (!check.Allowed) throw new InvalidOperationException("Holdout locked: " + check.Reason);
            holdoutNote = $"holdout unlocked with {check.FrozenHash}";
        }
        var profilePath = s.Paths.ProfileFile(req.Smoke);
        var profile = File.Exists(profilePath) ? TimeOfDayProfile.Load(profilePath) : null;

        List<GapperStudyUniverseDay> days;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var q = db.GapperStudyUniverseDays.AsNoTracking().Where(r => r.Split == split && r.ExcludedReason == null && (r.IsCandidate || r.ControlForSymbol != null));
            if (req.From is { } f) q = q.Where(r => r.DateEt >= f);
            if (req.To is { } t) q = q.Where(r => r.DateEt <= t);
            days = await q.OrderBy(r => r.DateEt).ThenBy(r => r.Symbol).ToListAsync(ct);
        }
        gate?.BeginStage("label", s.UtcNow, new { split, symbolDays = days.Count });

        var schedule = p.DecisionTimes();
        int rows = 0, withEntry = 0, done = 0;
        foreach (var dayGroup in days.GroupBy(d => d.DateEt).OrderBy(g => g.Key))
        {
            var toWrite = new List<GapperStudyLabel>();
            foreach (var u in dayGroup)
            {
                var bars = store.ReadDay(u.Symbol, u.DateEt);
                var day = GapperStudyLabeler.ComputeDay(p, bars, u.AdjPrevClose, u.DateEt, profile, u.AdvDecile, (double)u.Adv20Shares);
                foreach (var dt in schedule)
                {
                    var decisionDay = dt.DayOffset == 0 ? u.DateEt : GapperSnapshotStage.OffsetTradingDay(u.DateEt, dt.DayOffset);
                    var t = MinuteBarMath.ToUtc(decisionDay, dt.TimeEt);
                    var fwd = GapperStudyLabeler.ComputeForward(p, bars, u.DateEt, t);
                    toWrite.Add(new GapperStudyLabel
                    {
                        Symbol = u.Symbol, DateEt = u.DateEt, Split = split, DecisionLabel = dt.Label, DecisionTimeEt = TimeOnly.FromTimeSpan(dt.TimeEt), DecisionUtc = t,
                        EntryRef = fwd.EntryRef, EntryUtc = fwd.EntryUtc,
                        FwdMax0930 = fwd.FwdMax.GetValueOrDefault("09:30"), FwdMax0945 = fwd.FwdMax.GetValueOrDefault("09:45"),
                        FwdMax1030 = fwd.FwdMax.GetValueOrDefault("10:30"), FwdMax1600 = fwd.FwdMax.GetValueOrDefault("16:00"),
                        FwdClose0930 = fwd.FwdClose.GetValueOrDefault("09:30"), FwdClose0945 = fwd.FwdClose.GetValueOrDefault("09:45"),
                        FwdClose1030 = fwd.FwdClose.GetValueOrDefault("10:30"), FwdClose1600 = fwd.FwdClose.GetValueOrDefault("16:00"),
                        Reach20By0945 = day.Reach20By0945, PmHigh = day.PmHigh, HodTimePmEt = day.HodTimePmEt, HodTimeRthEt = day.HodTimeRthEt,
                        HodTimeEt = day.HodTimeEt, HodBucket = day.HodBucket, Fade = day.Fade, FadeFlag30 = day.FadeFlag30, CloseBelowOpen = day.CloseBelowOpen,
                        ExtensionFromOpen0945 = day.ExtensionFromOpen0945, VelocityDeathTimeEt = day.VelocityDeathTimeEt, DeathToHodMin = day.DeathToHodMin,
                        HaltFlag = day.HaltFlag, VwapLossTimeEt = day.VwapLossTimeEt, ComputedUtc = s.UtcNow,
                    });
                    rows++;
                    if (fwd.EntryRef is not null) withEntry++;
                }
            }
            await using (var db = await s.Db.CreateDbContextAsync(ct))
            {
                var symbols = dayGroup.Select(d => d.Symbol).ToList();
                var date = dayGroup.Key;
                await GapperStudyServices.DeleteAsync(db, db.GapperStudyLabels.Where(r => r.DateEt == date && symbols.Contains(r.Symbol)), ct);
                db.GapperStudyLabels.AddRange(toWrite);
                await db.SaveChangesAsync(ct);
            }
            done += dayGroup.Count();
            s.Logger.LogInformation("[{Now}] label {Split}: {Day} done, {Done}/{Total} symbol-days, {Rows} rows", GapperStudyServices.Et(s.UtcNow), split, dayGroup.Key, done, days.Count, rows);
        }
        gate?.EndStage("label", s.UtcNow, new { rows, symbolDays = days.Count });
        return new LabelStageResult(split, days.Count, rows, withEntry, holdoutNote, Math.Round(sw.Elapsed.TotalMinutes, 2));
    }
}
