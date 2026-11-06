using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Tests;

/// <summary>A batch-stamped wire copy never sets the catalyst time when a real-time flash of the same release exists within the cluster window.</summary>
public class GapperCatalystStampTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);
    private static readonly DateTime T = new(2026, 7, 17, 13, 15, 0, DateTimeKind.Utc); // 09:15 ET

    private static CatalystObservation At(int hourEt, int minuteEt, bool upperBound, string preClass = "Fda") =>
        new(new DateTime(2026, 7, 17, hourEt + 4, minuteEt, 0, DateTimeKind.Utc), preClass, 1, upperBound ? 3 : 2, 50, upperBound);

    private static GapperFormationSnapshot Compute(params CatalystObservation[] catalysts) =>
        GapperFormationFeatures.Compute(new GapperFeatureInputs
        {
            Symbol = "T", DateEt = Day, DecisionUtc = T, PriorClose = 8m, Catalysts = catalysts,
            MinuteBars = [new Candle { Symbol = "T", StartUtc = T.AddMinutes(-2), EndUtc = T.AddMinutes(-1), Open = 10, High = 10, Low = 10, Close = 10, Volume = 100 }],
        });

    [Test]
    public void FlashAndWireCopyWithinAnHour_UseTheEarlierFlash()
    {
        var snap = Compute(At(8, 31, upperBound: false), At(9, 3, upperBound: true));
        Assert.Multiple(() =>
        {
            Assert.That(snap.CatalystAgeMinutes, Is.EqualTo(44), "09:15 minus the 08:31 flash, not the 09:03 batch stamp");
            Assert.That(snap.CatalystStampIsUpperBound, Is.False);
        });
    }

    [Test]
    public void WireCopyAlone_IsFlaggedAsAnUpperBound()
    {
        var snap = Compute(At(9, 3, upperBound: true));
        Assert.That(snap.CatalystAgeMinutes, Is.EqualTo(12));
        Assert.That(snap.CatalystStampIsUpperBound, Is.True);
    }

    [Test]
    public void ItemsMoreThanAnHourApart_AreSeparateCatalysts()
    {
        var snap = Compute(At(6, 0, upperBound: false, "Earnings"), At(9, 3, upperBound: true));
        Assert.That(snap.CatalystPreClass, Is.EqualTo("Fda"), "the newest release stands on its own");
        Assert.That(snap.CatalystStampIsUpperBound, Is.True);
    }

    [Test]
    public void NothingAfterT_IsRead()
    {
        var snap = Compute(At(8, 31, upperBound: false), At(9, 33, upperBound: true));
        Assert.That(snap.CatalystAgeMinutes, Is.EqualTo(44));
    }
}
