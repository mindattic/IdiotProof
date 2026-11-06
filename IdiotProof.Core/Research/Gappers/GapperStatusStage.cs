using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

public sealed record SplitStatus(string Split, int UniverseDays, int Candidates, int Tier20, int Tier50, int Tier100, int Controls, double BarCoveragePct, int Snapshots, int Labels, DateOnly? First, DateOnly? Last);

public sealed record StudyStatus(
    string Database, string BarRoot, string StudyDir, string? FrozenCommit, DateTime? FrozenUtc,
    List<SplitStatus> Splits, int DailyBarRows, int DailySymbols, int SplitRows, int CikMapped, int CikTotal,
    int NewsRows, int EdgarRows, int EdgarWithAcceptance, bool ProfileExists, bool UnlockFileExists, bool HoldoutRunExists, string HoldoutState);

/// <summary><c>research gappers status</c>: what exists per split and the holdout lock state.</summary>
public sealed class GapperStatusStage(GapperStudyServices s, Hosting.IdiotProofDatabase database)
{
    public async Task<StudyStatus> RunAsync(CancellationToken ct)
    {
        var p = s.Params;
        var store = await s.StoreAsync(ct);
        await using var db = await s.Db.CreateDbContextAsync(ct);
        var splits = new List<SplitStatus>();
        var perSplit = await db.GapperStudyUniverseDays.AsNoTracking().GroupBy(r => r.Split).Select(g => new
        {
            Split = g.Key,
            UniverseDays = g.Count(r => r.ExcludedReason == null),
            Candidates = g.Count(r => r.IsCandidate),
            Tier20 = g.Count(r => r.IsGapper),
            Tier50 = g.Count(r => r.GapTier == "Tier50" || r.GapTier == "Tier100"),
            Tier100 = g.Count(r => r.GapTier == "Tier100"),
            Controls = g.Count(r => r.ControlForSymbol != null),
            First = g.Min(r => r.DateEt), Last = g.Max(r => r.DateEt),
        }).ToListAsync(ct);
        var snapshots = await db.GapperFormationSnapshots.AsNoTracking().Where(r => r.Source == GapperSnapshotStage.Source).GroupBy(r => r.Split).Select(g => new { Split = g.Key, Count = g.Count() }).ToListAsync(ct);
        var labels = await db.GapperStudyLabels.AsNoTracking().GroupBy(r => r.Split).Select(g => new { Split = g.Key, Count = g.Count() }).ToListAsync(ct);
        foreach (var r in perSplit.OrderBy(r => r.Split, StringComparer.Ordinal))
        {
            var needed = await db.GapperStudyUniverseDays.AsNoTracking().Where(x => x.Split == r.Split && (x.IsCandidate || x.ControlForSymbol != null)).Select(x => new { x.Symbol, x.DateEt }).ToListAsync(ct);
            int covered = 0;
            foreach (var g in needed.GroupBy(n => n.Symbol))
            {
                var have = store.CoveredDays(g.Key, g.Min(x => x.DateEt), g.Max(x => x.DateEt));
                covered += g.Count(x => have.Contains(x.DateEt));
            }
            splits.Add(new SplitStatus(r.Split, r.UniverseDays, r.Candidates, r.Tier20, r.Tier50, r.Tier100, r.Controls,
                needed.Count == 0 ? 0 : Math.Round(100.0 * covered / needed.Count, 2),
                snapshots.FirstOrDefault(x => x.Split == r.Split)?.Count ?? 0, labels.FirstOrDefault(x => x.Split == r.Split)?.Count ?? 0, r.First, r.Last));
        }

        var unlock = s.Paths.UnlockFile(p);
        var run = s.Paths.HoldoutRunFile(p);
        var lockState = File.Exists(run) ? "holdout run recorded (second run refused)" : File.Exists(unlock) ? "unlock file present, not yet run" : "locked";
        return new StudyStatus(
            database.ConnectionString, store.Root, s.Paths.StudyDir, HoldoutLock.GitFrozenCommit(s.Paths), p.Hypotheses.FrozenUtc, splits,
            await db.GapperStudyDailyBars.CountAsync(ct), await db.GapperStudyDailyBars.Select(b => b.Symbol).Distinct().CountAsync(ct),
            await db.GapperStudySplits.CountAsync(ct), await db.GapperStudyCikMap.CountAsync(r => r.Cik != null, ct), await db.GapperStudyCikMap.CountAsync(ct),
            await db.CatalystEvents.CountAsync(e => e.Backfilled, ct), await db.EdgarFilingIndex.CountAsync(ct), await db.EdgarFilingIndex.CountAsync(r => r.AcceptedUtc != null, ct),
            File.Exists(s.Paths.ProfileFile(false)), File.Exists(unlock), File.Exists(run), lockState);
    }
}
