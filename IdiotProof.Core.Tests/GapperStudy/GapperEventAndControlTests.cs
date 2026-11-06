using IdiotProof.Core.Research.Gappers;
using IdiotProof.Models;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>IP-T442: tiers, sub-type and the $50k premarket floor on synthetic minute bars; the seeded-hash control pick.</summary>
[TestFixture]
public class GapperEventAndControlTests
{
    private const decimal PrevClose = 2.00m;

    [Test]
    public void PremarketGapper_Tier50_WithEnoughDollarVolume()
    {
        // 04:00-07:59 flat at 2.00 on 1k shares/min ($2k/min → $480k before the pop), 08:00 pops to 3.10 (+55%).
        var bars = Session(Day, i => i < 240 ? 2.00 : 3.10, i => i < 600 ? 1_000 : 0);
        var c = GapperEventClassifier.Classify(Params, bars, PrevClose, Day);
        Assert.Multiple(() =>
        {
            Assert.That(c.GapMax, Is.EqualTo(0.55).Within(1e-9));
            Assert.That(c.Tier("Tier20")!.Qualified, Is.True);
            Assert.That(c.Tier("Tier50")!.Qualified, Is.True);
            Assert.That(c.Tier("Tier100")!.Touched, Is.False);
            Assert.That(c.Tier("Tier20")!.TouchTimeEt, Is.EqualTo(new TimeOnly(8, 0)));
            Assert.That(c.SubType, Is.EqualTo(GapperEventClassifier.PremarketSubType));
            Assert.That(c.HighestQualifiedTier, Is.EqualTo("Tier50"));
            Assert.That(c.IsGapper, Is.True);
            Assert.That(c.SipShare, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void TierTouchedOnNoVolume_IsRecordedButNotQualified()
    {
        // Ten shares a minute: by the 08:00 touch only 240 x 10 x $2 = $4.8k has printed, under the $50k floor.
        var bars = Session(Day, i => i < 240 ? 2.00 : 2.50, i => i < 600 ? 10 : 0);
        var c = GapperEventClassifier.Classify(Params, bars, PrevClose, Day);
        Assert.Multiple(() =>
        {
            Assert.That(c.Tier("Tier20")!.Touched, Is.True);
            Assert.That(c.Tier("Tier20")!.Qualified, Is.False);
            Assert.That(c.Tier("Tier20")!.PmDollarVolumeAtTouch, Is.LessThan(Params.Gapper.PremarketDollarVolumeFloor));
            Assert.That(c.IsGapper, Is.False);
            Assert.That(c.HighestQualifiedTier, Is.Null);
        });
    }

    [Test]
    public void OpenGapper_WhenTheLevelFirstPrintsAfterTheBell()
    {
        var bars = Session(Day, i => i < 331 ? 2.00 : 2.45, i => 5_000);
        var c = GapperEventClassifier.Classify(Params, bars, PrevClose, Day);
        Assert.Multiple(() =>
        {
            Assert.That(c.Tier("Tier20")!.TouchTimeEt, Is.EqualTo(new TimeOnly(9, 31)));
            Assert.That(c.SubType, Is.EqualTo(GapperEventClassifier.OpenSubType));
            Assert.That(c.PmHighTimeEt, Is.Not.Null);
            Assert.That(c.HodTimeEt, Is.EqualTo(new TimeOnly(9, 31)));
        });
    }

    [Test]
    public void OnlyBarsInsideTheGapperWindow_Count()
    {
        // The pop comes at 11:00, after the 10:30 window closes.
        var bars = Session(Day, i => i < 420 ? 2.00 : 2.60, i => 5_000);
        var c = GapperEventClassifier.Classify(Params, bars, PrevClose, Day);
        Assert.That(c.Tier("Tier20")!.Touched, Is.False);
        Assert.That(c.GapMax, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void ControlHash_IsDeterministic_AndTheSmallestHashWins()
    {
        var d = new DateOnly(2024, 3, 5);
        var pool = new[] { "AAA", "BBB", "CCC", "DDD" }.Select(s => new ControlCandidate(s, 4, 1)).ToList();
        var expected = pool.OrderBy(c => GapperControlPicker.Hash(Params, c.Symbol, d), StringComparer.Ordinal).First().Symbol;
        var first = GapperControlPicker.Assign(Params, d, [new GapperForControl("GAP", 4, 1)], pool);
        var again = GapperControlPicker.Assign(Params, d, [new GapperForControl("GAP", 4, 1)], pool.AsEnumerable().Reverse().ToList());
        Assert.Multiple(() =>
        {
            Assert.That(first["GAP"], Is.EqualTo(expected));
            Assert.That(again["GAP"], Is.EqualTo(expected), "pool order never changes the pick");
            Assert.That(GapperControlPicker.Hash(Params, "AAA", d), Is.EqualTo(GapperControlPicker.Hash(Params, "aaa", d)));
            Assert.That(GapperControlPicker.Hash(Params, "AAA", d), Is.Not.EqualTo(GapperControlPicker.Hash(Params, "AAA", d.AddDays(1))));
        });
    }

    [Test]
    public void Controls_FallBackToTheNearestDecile_ThenTheNearestTier_AndNeverShare()
    {
        var d = new DateOnly(2024, 3, 5);
        var pool = new List<ControlCandidate>
        {
            new("CELL", 4, 1),
            new("DEC3", 3, 1), new("DEC6", 6, 1),
            new("TIER2", 4, 2),
        };
        var gappers = new List<GapperForControl> { new("G1", 4, 1), new("G2", 4, 1), new("G3", 4, 1), new("G4", 4, 1), new("G5", 4, 1) };
        var picks = GapperControlPicker.Assign(Params, d, gappers, pool);
        Assert.Multiple(() =>
        {
            Assert.That(picks["G1"], Is.EqualTo("CELL"), "the only same-cell member goes to the first gapper");
            Assert.That(picks["G2"], Is.EqualTo("DEC3"), "then the nearest decile at the same tier, lower first");
            Assert.That(picks["G3"], Is.EqualTo("DEC6"));
            Assert.That(picks["G4"], Is.EqualTo("TIER2"), "then the nearest price tier");
            Assert.That(picks.ContainsKey("G5"), Is.False, "no member left: no control, never a shared one");
            Assert.That(picks.Values, Is.Unique);
        });
    }
}
