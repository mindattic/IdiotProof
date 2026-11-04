using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-US-H1: a broken IdiotScript is explained with exact line/column diagnostics instead of
/// being silently half-parsed, and a well-formed script produces none.
/// </summary>
public class ScriptDiagnosticsTests
{
    private static IReadOnlyList<ScriptDiagnostic> Errors(string text) =>
        ScriptDiagnostics.Analyze(text).Where(d => d.IsError).ToList();

    [Test]
    public void UnknownVerb_IsReportedAtItsExactLineAndColumn_WithADidYouMean()
    {
        var text = "Ticker(\"NVDA\")\n    .IsAbveVwap()\n    .Long()\n    .StopLoss(2)";
        var d = Errors(text).Single();
        Assert.Multiple(() =>
        {
            Assert.That(d.Code, Is.EqualTo("IPS001"));
            Assert.That(d.Line, Is.EqualTo(2));
            Assert.That(d.Column, Is.EqualTo(6));
            Assert.That(d.Length, Is.EqualTo("IsAbveVwap".Length));
            Assert.That(d.Suggestion, Is.EqualTo("IsAboveVwap"));
            Assert.That(d.Excerpt, Is.EqualTo("      .IsAbveVwap()\n       ^^^^^^^^^^"));
        });
    }

    [Test]
    public void DidYouMean_CountsATranspositionAsOneEdit_AndStaysQuietForGibberish()
    {
        Assert.That(Errors("Ticker(\"X\").IsAboveVawp().Long()").Single().Suggestion, Is.EqualTo("IsAboveVwap"));
        Assert.That(Errors("Ticker(\"X\").Stoplos(2).Long()").Single().Suggestion, Is.EqualTo("StopLoss"));
        Assert.That(Errors("Ticker(\"X\").Zzqxj().Long()").Single().Suggestion, Is.Null);
    }

    [Test]
    public void AliasesAndAnyCasing_AreKnown()
    {
        Assert.That(ScriptDiagnostics.Analyze("Ticker(\"X\").abovevwap().GAPUP(4).Long().StopLoss(1)"), Is.Empty);
    }

    [Test]
    public void UnclosedParen_PointsAtTheOpeningParen()
    {
        var d = Errors("Ticker(\"X\")\n.IsAboveEma(9\n.Long()").Single(e => e.Code == "IPS002");
        Assert.That((d.Line, d.Column), Is.EqualTo((2, 12)));
    }

    [Test]
    public void StrayCloseParen_IsReportedWhereItSits()
    {
        var d = Errors("Ticker(\"X\").Long())").Single();
        Assert.That((d.Code, d.Line, d.Column), Is.EqualTo(("IPS003", 1, 19)));
    }

    [Test]
    public void UnterminatedString_IsReported()
    {
        var errors = Errors("Ticker(\"X\").Name(\"oops).Long()");
        var d = errors.Single(e => e.Code == "IPS004");
        Assert.That(d.Column, Is.EqualTo(18));
        Assert.That(errors.Single(e => e.Code == "IPS002").Column, Is.EqualTo(17), "the string swallowed Name's ')'");
    }

    [Test]
    public void MissingTicker_IsAnError()
    {
        Assert.That(Errors(".IsAboveVwap().Long()").Select(d => d.Code), Does.Contain("IPS005"));
        Assert.That(ScriptDiagnostics.Analyze("   "), Is.Empty);
    }

    [Test]
    public void NonNumericArgument_IsPinnedToTheArgument()
    {
        var d = Errors("Ticker(\"X\").IsAboveEma(nine).Long()").Single();
        Assert.That((d.Code, d.Column, d.Length), Is.EqualTo(("IPS006", 24, 4)));
    }

    [Test]
    public void QuotedNumber_IsNotANumber_BecauseTheParserWouldIgnoreIt()
    {
        Assert.That(Errors("Ticker(\"X\").StopLoss(\"2\").Long()").Single().Code, Is.EqualTo("IPS006"));
    }

    [Test]
    public void FractionalWholeNumber_IsReported_WithWhatItWouldBecome()
    {
        var d = Errors("Ticker(\"X\").Quantity(1.5).Long()").Single();
        Assert.That(d.Code, Is.EqualTo("IPS009"));
        Assert.That(d.Message, Does.Contain("cut to 1"));
    }

    [Test]
    public void MissingRequiredArgument_IsReportedAtTheCloseParen()
    {
        var d = Errors("Ticker(\"X\").IsBetweenEma(9).Long()").Single();
        Assert.That((d.Code, d.Column), Is.EqualTo(("IPS007", 27)));
    }

    [Test]
    public void ExtraArguments_AndArgumentsOnANoArgVerb_AreWarnings()
    {
        var diags = ScriptDiagnostics.Analyze("Ticker(\"X\").IsAboveVwap(5).Entry(3, 4).Long()");
        Assert.That(diags.Select(d => (d.Severity, d.Code)),
            Is.EquivalentTo(new[] { (DiagnosticSeverity.Warning, "IPS010"), (DiagnosticSeverity.Warning, "IPS008") }));
    }

    [Test]
    public void NestedParenArguments_InBranches_AreScannedCorrectly()
    {
        var ok = "Ticker(\"X\")\n.IsAboveVwap()\n.Then(Long().TakeProfit(5).StopLoss(3))\n.ElseIf(IsBelowVwap(), Short().TakeProfit(2))\n.Else(Long())\n.StopLoss(1)";
        Assert.That(ScriptDiagnostics.Analyze(ok), Is.Empty);

        var bad = "Ticker(\"X\")\n.IsAboveVwap()\n.Then(Long().TakeProfitt(5))";
        var d = Errors(bad).Single();
        Assert.That((d.Line, d.Column, d.Suggestion), Is.EqualTo((3, 14, "TakeProfit")));
    }

    [Test]
    public void QualifiedNames_LambdasAndComments_AreNotVerbs()
    {
        var text = "// .Bogus() in a comment\nStock.Ticker(\"X\") /* .AlsoBogus() */\n.Session(IS.PREMARKET)\n.ElseIf(c => c.IsBelowVwap(), b => b.Short())\n.Long().StopLoss(1)";
        Assert.That(ScriptDiagnostics.Analyze(text), Is.Empty);
    }

    [Test]
    public void LoopAndTrapVerbs_AreKnown()
    {
        var text = "Ticker(\"X\").While().IsAboveVwap().EndWhile().ExitWhenFalse().For(5, 0.5, 3).TakeProfitEach()" +
                   ".ForEach(5, 5.5, 6.25).NotFallingKnife(0.4, 30).Repeat(2).Long().StopLoss(1)";
        Assert.That(ScriptDiagnostics.Analyze(text), Is.Empty);
    }

    [Test]
    public void EveryBundledSampleScript_HasNoDiagnostics()
    {
        foreach (var script in new[] { Strategies_20260217.NCI_Script(), Strategies_20260217.ERNA_Script(), Strategies_20260217.SUNE_Script() })
            Assert.That(ScriptDiagnostics.Analyze(script), Is.Empty, script);
        foreach (var def in Strategies_20260217.GetAllStrategies())
            Assert.That(ScriptDiagnostics.Analyze(def.ToScript()), Is.Empty, def.ToScript());
    }

    [Test]
    public void CanonicalScriptOfAWideStrategy_HasNoDiagnostics_AndStillParsesToTheSameStrategy()
    {
        var def = Stock.Ticker("ABCD")
            .Name("Wide, with a comma")
            .Session(TradingSession.Premarket)
            .IsAboveVwap().IsEmaAbove(9).RequireEmaStack(9, 21).IsRsiOversold(35).IsGapBetween(5, 40)
            .HoldsAbove(2.1).IsNear(2.4, 1.5).DippedTo(2.05).HigherLows(2).NotFallingKnife(0.45, 20)
            .Long()
            .QuantityNotional(250m)
            .TakeProfit(2.6, 2.8, 3.0)
            .StopLoss(1.95)
            .TrailingStopLoss(4)
            .SellBy("09:25")
            .PeakGiveback(30, "09:10")
            .Repeat(3)
            .Build();
        var text = def.ToScript();
        Assert.That(ScriptDiagnostics.Analyze(text), Is.Empty, text);
        Assert.That(ScriptParser.ParseScript(text)!.ShortId(), Is.EqualTo(def.ShortId()));
    }

    [Test]
    public void KnownVerbs_AreTheBuilderMethodsTheParserHandles()
    {
        Assert.That(ScriptDiagnostics.KnownVerbs, Does.Contain("IsAboveVwap").And.Contain("TakeProfitEach").And.Contain("NotFallingKnife"));
        Assert.That(ScriptDiagnostics.KnownVerbs, Does.Not.Contain("Build").And.Not.Contain("Then"));
        Assert.That(ScriptDiagnostics.KnownVerbs.All(ScriptDiagnostics.IsKnownVerb), Is.True);
    }

    [Test]
    public void Analyzing_NeverChangesWhatTheParserBuilds()
    {
        var text = "Ticker(\"X\").IsAbveVwap().IsAboveEma(9).Long().StopLoss(1)";
        var before = ScriptParser.ParseScript(text)!.ShortId();
        _ = ScriptDiagnostics.Analyze(text);
        Assert.That(ScriptParser.ParseScript(text)!.ShortId(), Is.EqualTo(before));
    }
}
