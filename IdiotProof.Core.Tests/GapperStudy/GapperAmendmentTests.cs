using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Evidence.Gappers;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>
/// IP-T448 amendment 01: the registered file parses and draws its seeded 10% sample; frozen H1 is
/// unchanged by the amendment's presence; H1p and H12p see the false positives and the losers that
/// candidate days hide; and amended runs are corrected as their own set.
/// </summary>
[TestFixture]
public class GapperAmendmentTests
{
    private const string Split = "train";
    private static readonly string[] DecisionLabels = ["08:00", "08:30", "09:00"];

    /// <summary>The committed study folder, found by walking up from the test binary; GAPPER_STUDY_DIR points at a host's temp copy while one is alive.</summary>
    private static readonly Lazy<GapperStudyPaths> Committed = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, GapperStudyPaths.RelativeDir.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(Path.Combine(candidate, "amendment-01.json"))) return new GapperStudyPaths(candidate);
        }
        throw new FileNotFoundException("amendment-01.json was not found above the test binary.");
    });

    [Test]
    public void RegisteredAmendment_Parses_AndDrawsATenPercentDeterministicSample()
    {
        var a = GapperAmendment.Load(Committed.Value)!;
        var frozen = GapperHypothesisSet.Load(Committed.Value);
        int members = 0, n = 0;
        for (int s = 0; s < 400; s++)
            for (int d = 0; d < 50; d++, n++)
                if (a.IsSampleMember($"SYM{s}", new DateOnly(2023, 1, 2).AddDays(d))) members++;
        var h12p = a.Resolve(a.Find("H12p")!, frozen);
        Assert.Multiple(() =>
        {
            Assert.That(a.Id, Is.EqualTo("A01"));
            Assert.That(a.PopulationSample.Rate, Is.EqualTo(0.10));
            Assert.That(a.SampleWeight, Is.EqualTo(10));
            Assert.That(a.Hypotheses.Select(h => (h.Id, h.BasedOn)), Is.EqualTo(new[] { ("H1p", "H1"), ("H9p", "H9"), ("H11p", "H11"), ("H12p", "H12") }));
            Assert.That((double)members / n, Is.EqualTo(0.10).Within(0.01), "the hash draws close to the registered rate");
            Assert.That(a.IsSampleMember("ABCD", new DateOnly(2024, 3, 5)), Is.EqualTo(GapperAmendment.Load(Committed.Value)!.IsSampleMember("abcd", new DateOnly(2024, 3, 5))), "deterministic and case-insensitive on the symbol");
            Assert.That(h12p.Test, Is.EqualTo("search-grid-spa"));
            Assert.That(h12p.Feature, Is.EqualTo(frozen.Find("H12").Feature), "the amended hypothesis keeps the frozen feature text");
            Assert.That(h12p.MinEffectNumber("meanNetPctPerTrade"), Is.EqualTo(frozen.Find("H12").MinEffectNumber("meanNetPctPerTrade")), "and the frozen thresholds");
            Assert.That(a.Sha256, Has.Length.EqualTo(64));
        });
    }

    private static GapperStudyHost Host(bool withAmendment)
    {
        var source = Committed.Value.AmendmentFile;
        var host = new GapperStudyHost();
        if (withAmendment) File.Copy(source, host.Paths.AmendmentFile);
        return host;
    }

    /// <summary>
    /// 60 days x 20 candidate symbols whose top VolRatioTod values mostly reach +20%, plus, per day,
    /// the first 20 non-candidate symbols the amendment draws, half of them with an equally high
    /// VolRatioTod that never runs. Non-candidate rows exist only for drawn symbols, as the snapshot stage writes them.
    /// </summary>
    private static async Task SeedAsync(GapperStudyHost host, bool withSample, Action<bool, Random, GapperFormationSnapshotRow, GapperStudyLabel>? fill = null)
    {
        var rng = new Random(42);
        var a = GapperAmendment.Load(Committed.Value)!;
        await using var db = await host.DbAsync();
        var day = new DateOnly(2023, 1, 3);
        for (int d = 0; d < 60; d++)
        {
            var symbols = Enumerable.Range(0, 20).Select(i => (Symbol: $"C{i:00}", Candidate: true)).ToList();
            if (withSample)
                symbols.AddRange(Enumerable.Range(0, 2000).Select(i => $"N{i:0000}").Where(sym => a.IsSampleMember(sym, day)).Take(20).Select(sym => (sym, false)));
            foreach (var (symbol, candidate) in symbols)
            {
                db.GapperStudyUniverseDays.Add(new GapperStudyUniverseDay { Symbol = symbol, DateEt = day, Split = Split, AdjPrevClose = 2m, Adv20Dollars = 400_000m, Adv20Shares = 200_000m, Sessions = 20, AdvDecile = 3, PriceTier = 1, IsCandidate = candidate, DayVolume = 300_000m });
                double ratio = candidate || rng.NextDouble() < 0.5 ? rng.NextDouble() * 10 : 8 + rng.NextDouble() * 2;
                bool reach = candidate && rng.NextDouble() < (ratio > 8 ? 0.8 : 0.05);
                foreach (var label in DecisionLabels)
                {
                    var t = MinuteBarMath.ToUtc(day, GapperStudyParams.ParseEt(label));
                    var snap = new GapperFormationSnapshotRow { Source = GapperSnapshotStage.Source, Symbol = symbol, DateEt = day, Split = Split, DecisionLabel = label, DecisionTimeEt = TimeOnly.FromTimeSpan(GapperStudyParams.ParseEt(label)), DecisionUtc = t, IsCandidate = candidate, FeatureVersion = 1, PmCumVol = 50_000, VolRatioTod = ratio, ExtraJson = "{}" };
                    var lab = new GapperStudyLabel { Symbol = symbol, DateEt = day, Split = Split, DecisionLabel = label, DecisionTimeEt = TimeOnly.FromTimeSpan(GapperStudyParams.ParseEt(label)), DecisionUtc = t, EntryRef = 2m, EntryUtc = t, Reach20By0945 = reach };
                    fill?.Invoke(candidate, rng, snap, lab);
                    db.GapperFormationSnapshots.Add(snap);
                    db.GapperStudyLabels.Add(lab);
                }
            }
            day = MarketCalendar.NextTradingDay(day);
        }
        await db.SaveChangesAsync();
    }

    private static Task<TestStageResult> RunAsync(GapperStudyHost host, params string[] ids) =>
        host.Get<GapperTestStage>().RunAsync(new TestStageRequest(ids, Split, Smoke: false), CancellationToken.None);

    [Test]
    public async Task FrozenH1_IsIdenticalWithAndWithoutTheAmendment_AndH1pSeesTheFalsePositives()
    {
        HypothesisPart frozenAlone, frozenBeside;
        HypothesisRun amended;
        using (var plain = Host(withAmendment: false))
        {
            await SeedAsync(plain, withSample: true);
            frozenAlone = (await RunAsync(plain, "H1")).Runs.Single().Parts.Single();
        }
        using (var host = Host(withAmendment: true))
        {
            await SeedAsync(host, withSample: true);
            var r = await RunAsync(host, "H1", "H1p");
            frozenBeside = r.Runs.Single(x => x.Id == "H1").Parts.Single();
            amended = r.Runs.Single(x => x.Id == "H1p");
            Assert.That(r.Runs.Single(x => x.Id == "H1").Inputs.N, Is.EqualTo(60 * 20), "frozen H1 never reads a sample row");
        }
        var part = amended.Parts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(frozenBeside.Effect, Is.EqualTo(frozenAlone.Effect));
            Assert.That(frozenBeside.RawP, Is.EqualTo(frozenAlone.RawP));
            Assert.That(frozenBeside.FamilyQ, Is.EqualTo(frozenAlone.FamilyQ), "the amended run does not join the frozen BH set");
            Assert.That(frozenBeside.HolmP, Is.EqualTo(frozenAlone.HolmP), "nor the frozen Holm set");
            Assert.That(amended.Inputs.AmendmentId, Is.EqualTo("A01"));
            Assert.That(amended.Inputs.AmendsHypothesis, Is.EqualTo("H1"));
            Assert.That(part.Statistic["sampleRows"], Is.EqualTo(60 * 20));
            Assert.That(part.Statistic["candidateRows"], Is.EqualTo(60 * 20));
            Assert.That(part.Statistic["populationWeight"], Is.EqualTo(60 * 20 + 60 * 20 * 10.0));
            Assert.That(part.Effect, Is.LessThan(frozenAlone.Effect!.Value), "weighted non-candidates with a high ratio that never run pull the lift down");
            Assert.That((double)part.Statistic["groupRate"]!, Is.LessThan((double)frozenAlone.Statistic["bucketRate"]! / 3), "and the top bucket's absolute precision falls far more");
            Assert.That(part.Effect, Is.GreaterThan(1), "the planted signal still lifts the population base rate");
            Assert.That(Path.GetFileName(amended.File)!, Does.StartWith("test-H1p-train-"));
        });
    }

    [Test]
    public async Task H12p_CountsTheSampleDaysLosers_ThatCandidateOnlyH12NeverSees()
    {
        using var host = Host(withAmendment: true);
        Directory.CreateDirectory(host.Paths.DataDir);
        File.WriteAllText(GapperSpreadMultiplier.File(host.Paths), "{\"multiplier\": 1.5, \"n\": 500}");
        var store = host.Store;
        await SeedAsync(host, withSample: true, (candidate, rng, snap, lab) =>
        {
            snap.VolRatioTod = 12; snap.Velocity3m = 2.0; snap.PmCumVol = 100_000;
            snap.ExtraJson = "{\"csSpreadPrior\": 0.004}";
            lab.EntryRef = 2.00m;
            if (snap.DecisionLabel == "08:30" && !store.HasDay(snap.Symbol, snap.DateEt))
                store.AppendDay(snap.Symbol, snap.DateEt, candidate
                    ? Session(snap.DateEt, i => i < 270 ? 2.00 : i < 285 ? 2.00 + 0.02 * (i - 270) : 2.30 - 0.01 * (i - 285), _ => 500)
                    : Session(snap.DateEt, i => i < 270 ? 2.00 : 2.00 - 0.004 * (i - 270), _ => 500));
        });
        var r = await RunAsync(host, "H12", "H12p");
        var frozen = JsonDocument.Parse(File.ReadAllText(r.Runs.Single(x => x.Id == "H12").File!)).RootElement.GetProperty("parts")[0].GetProperty("statistic");
        var amended = JsonDocument.Parse(File.ReadAllText(r.Runs.Single(x => x.Id == "H12p").File!)).RootElement.GetProperty("parts")[0].GetProperty("statistic");
        double frozenMean = frozen.GetProperty("configs")[0].GetProperty("meanNetPct").GetDouble();
        double amendedMean = amended.GetProperty("configs")[0].GetProperty("meanNetPct").GetDouble();
        Assert.Multiple(() =>
        {
            Assert.That(frozen.GetProperty("cellsPassingLoosest").GetInt32(), Is.EqualTo(60 * 20), "frozen H12 trades candidate days only");
            Assert.That(amended.GetProperty("cellsPassingLoosest").GetInt32(), Is.EqualTo(60 * 40), "H12p also trades every drawn non-candidate pass");
            Assert.That(amended.GetProperty("sampleCellsPassingLoosest").GetInt32(), Is.EqualTo(60 * 20));
            Assert.That(amendedMean, Is.LessThan(frozenMean), "the sample's falling days, each standing for ten, drag the per-trade mean down");
            Assert.That(amendedMean, Is.LessThan(0), "ten-to-one weighted losers make the rule a loser over the population");
            Assert.That(amended.GetProperty("spaSeries").GetString(), Does.Contain("per-day"));
        });
    }

    [Test]
    public async Task StudyDays_AddDrawnNonCandidates_AndSampleOnlyDaysUseTheAmendedDecisionTimes()
    {
        using var host = Host(withAmendment: true);
        var a = GapperAmendment.Load(Committed.Value)!;
        var day = new DateOnly(2023, 2, 1);
        var drawn = Enumerable.Range(0, 500).Select(i => $"N{i:000}").Where(sym => a.IsSampleMember(sym, day)).ToList();
        var notDrawn = Enumerable.Range(0, 500).Select(i => $"N{i:000}").First(sym => !a.IsSampleMember(sym, day));
        await using (var db = await host.DbAsync())
        {
            GapperStudyUniverseDay Row(string sym, bool candidate = false, string? control = null, string? excluded = null) =>
                new() { Symbol = sym, DateEt = day, Split = Split, AdjPrevClose = 2m, Adv20Dollars = 400_000m, Adv20Shares = 200_000m, Sessions = 20, AdvDecile = 3, PriceTier = 1, IsCandidate = candidate, ControlForSymbol = control, ExcludedReason = excluded };
            db.GapperStudyUniverseDays.Add(Row("CAND", candidate: true));
            db.GapperStudyUniverseDays.Add(Row(notDrawn, control: "CAND"));
            foreach (var sym in drawn) db.GapperStudyUniverseDays.Add(Row(sym));
            db.GapperStudyUniverseDays.Add(Row(drawn[0] + "X", excluded: "split-window"));
            await db.SaveChangesAsync();
        }
        var s = host.Get<GapperStudyServices>();
        var days = await s.StudyDaysAsync(Split, null, null, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(drawn, Has.Count.GreaterThan(30), "about 10% of 500 symbols are drawn");
            Assert.That(days.Select(d => d.Symbol), Is.EquivalentTo(new[] { "CAND", notDrawn }.Concat(drawn)), "candidate, control and every drawn non-candidate; nothing undrawn or excluded");
            Assert.That(days.Count(GapperStudyServices.SampleOnly), Is.EqualTo(drawn.Count));
            Assert.That(s.SampleDecisionTimes().Select(d => d.Label), Is.EqualTo(new[] { "08:00", "08:30", "09:00" }));
        });
    }

    [Test]
    public void ApplyAll_CorrectsAmendedRunsAsTheirOwnSet_LeavingFrozenCorrectionsUntouched()
    {
        var p = GapperStudyParams.Load(Committed.Value);
        HypothesisRun Run(string id, string family, double rawP, string? amends = null)
        {
            var run = new HypothesisRun { Id = id, Family = family, Primary = true, Split = Split };
            if (amends is not null) { run.Inputs.AmendmentId = "A01"; run.Inputs.AmendsHypothesis = amends; }
            run.Parts.Add(new HypothesisPart { Name = "x", Effect = 4, MinEffect = 3, MeetsMinimum = true, RawP = rawP, Direction = EffectDirection.AtLeast });
            return run;
        }
        var alone = new[] { Run("H1", "F1", 0.02), Run("H9", "F1", 0.04) };
        HypothesisCorrections.ApplyAll(alone, p, "alone");
        var mixed = new[] { Run("H1", "F1", 0.02), Run("H9", "F1", 0.04), Run("H1p", "F1", 0.0001, "H1"), Run("H9p", "F1", 0.9, "H9") };
        HypothesisCorrections.ApplyAll(mixed, p, "mixed");
        Assert.Multiple(() =>
        {
            Assert.That(mixed[0].Parts[0].FamilyQ, Is.EqualTo(alone[0].Parts[0].FamilyQ));
            Assert.That(mixed[1].Parts[0].FamilyQ, Is.EqualTo(alone[1].Parts[0].FamilyQ));
            Assert.That(mixed[0].Parts[0].HolmP, Is.EqualTo(alone[0].Parts[0].HolmP));
            Assert.That(mixed[1].Parts[0].HolmP, Is.EqualTo(alone[1].Parts[0].HolmP));
            Assert.That(mixed[2].Parts[0].HolmP, Is.EqualTo(0.0002).Within(1e-12), "Holm over the two amended primaries only");
            Assert.That(mixed[3].Parts[0].HolmP, Is.EqualTo(0.9).Within(1e-12));
            Assert.That(mixed[2].CorrectionScope, Does.Contain("amendment A01"));
        });
    }
}
