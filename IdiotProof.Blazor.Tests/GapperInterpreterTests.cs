using IdiotProof.Blazor.Services;
using IdiotProof.Scripting;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The transcript→gapper extraction contract (IP-US-K10): the model's JSON is
/// re-validated fail-closed — partial overlays keep base-profile values,
/// invalid symbols/dial-ins are skipped with warnings, and garbage never
/// becomes a candidate. Pure parse layer; Legion is not involved.
/// </summary>
[TestFixture]
public sealed class GapperInterpreterTests
{
    private static GapperProfile Base() => new()
    {
        Id = "classic-gapper", Name = "Classic Gapper",
        MinGapPercent = 5, MinVolumeRatio = 2, MinPrice = 1, MaxPrice = 50,
        EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
        StopLossPercent = 5, PeakGivebackPercent = 25,
        ArmExitAtEt = "09:15", SellByEt = "09:28", DefaultNotional = 1000m,
    };

    [Test]
    public void Parse_PartialOverlay_ChangesOnlyThoseFields()
    {
        var (candidates, warnings) = GapperInterpreter.ParseCandidates(
            """
            [
              { "symbol": "acme", "rationale": "FDA gap, tight stop",
                "profile": { "minGapPercent": 10, "stopLossPercent": 3 } },
              { "symbol": "BETA", "rationale": "big cap gap and go" }
            ]
            """, Base());

        Assert.That(warnings, Is.Empty);
        Assert.That(candidates, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(candidates[0].Symbol, Is.EqualTo("ACME"), "symbols normalize to upper");
            Assert.That(candidates[0].Profile.MinGapPercent, Is.EqualTo(10), "overlay applied");
            Assert.That(candidates[0].Profile.StopLossPercent, Is.EqualTo(3), "overlay applied");
            Assert.That(candidates[0].Profile.PeakGivebackPercent, Is.EqualTo(25), "omitted field keeps base value");
            Assert.That(candidates[0].Profile.SellByEt, Is.EqualTo("09:28"), "omitted field keeps base value");
            Assert.That(candidates[1].Profile.MinGapPercent, Is.EqualTo(5), "no profile block = pure base profile");
            Assert.That(candidates[1].Rationale, Is.EqualTo("big cap gap and go"));
        });
    }

    [Test]
    public void Parse_ProseWrappedJson_ExtractsTheArray()
    {
        var (candidates, _) = GapperInterpreter.ParseCandidates(
            """Here are the plays I found: [ { "symbol": "GAPX", "rationale": "r" } ] Hope that helps!""",
            Base());
        Assert.That(candidates, Has.Count.EqualTo(1));
        Assert.That(candidates[0].Symbol, Is.EqualTo("GAPX"));
    }

    [Test]
    public void Parse_InvalidSymbol_SkippedWithWarning_OthersSurvive()
    {
        var (candidates, warnings) = GapperInterpreter.ParseCandidates(
            """
            [
              { "symbol": "NOT A TICKER!", "rationale": "bad" },
              { "symbol": "OK", "rationale": "good" }
            ]
            """, Base());

        Assert.That(candidates, Has.Count.EqualTo(1));
        Assert.That(candidates[0].Symbol, Is.EqualTo("OK"));
        Assert.That(warnings, Has.Some.Contains("invalid symbol"));
    }

    [Test]
    public void Parse_HallucinatedBadDialIns_FailClosed()
    {
        // arm time after sell-by = a momentum exit that can never fire; the
        // model's output must not become a queued candidate.
        var (candidates, warnings) = GapperInterpreter.ParseCandidates(
            """
            [ { "symbol": "BAD", "rationale": "r",
                "profile": { "armExitAtEt": "09:29", "sellByEt": "09:20" } } ]
            """, Base());

        Assert.That(candidates, Is.Empty);
        Assert.That(warnings, Has.Some.Contains("BAD"));
    }

    [Test]
    public void Parse_Garbage_ReturnsEmptyWithWarning()
    {
        var (candidates, warnings) = GapperInterpreter.ParseCandidates("total nonsense, no json", Base());
        Assert.That(candidates, Is.Empty);
        Assert.That(warnings, Is.Not.Empty);
    }

    [Test]
    public void Parse_CaseInsensitivePropertyNames_StillApply()
    {
        var (candidates, _) = GapperInterpreter.ParseCandidates(
            """[ { "Symbol": "CAPS", "Rationale": "r", "Profile": { "MinGapPercent": 7 } } ]""",
            Base());
        Assert.That(candidates, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(candidates[0].Symbol, Is.EqualTo("CAPS"));
            Assert.That(candidates[0].Profile.MinGapPercent, Is.EqualTo(7));
        });
    }

    [Test]
    public void SystemPrompt_CarriesTheLiveBaseDefaults()
    {
        var prompt = GapperInterpreter.BuildSystemPrompt(Base());
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("minGapPercent=5"));
            Assert.That(prompt, Does.Contain("sellByEt=09:28"));
            Assert.That(prompt, Does.Contain("ONLY a JSON array"));
        });
    }

    [Test]
    public void Parse_TruncatedArray_WarnsAboutTruncationSpecifically()
    {
        // IP-A21: a response cut off at the token cap starts an array but
        // never closes it — losing EVERY candidate. The warning must say the
        // response was cut off (actionable: shorten the transcript), not the
        // misleading generic "contained no JSON array".
        var (candidates, warnings) = GapperInterpreter.ParseCandidates(
            """[ { "symbol": "ACME", "rationale": "big gap", "profile": { "minGap""",
            Base());

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Is.Empty);
            Assert.That(warnings, Has.Some.Contains("cut off"));
        });
    }
}
