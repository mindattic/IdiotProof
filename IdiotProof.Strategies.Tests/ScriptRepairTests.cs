using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-T523: a diagnostic carries the edit that corrects it when exactly one exists, the whole
/// corrected script is offered only after it re-analyzes clean, and nothing is guessed.
/// </summary>
public class ScriptRepairTests
{
    private static ScriptRepair Repair(string text) => ScriptDiagnostics.Repair(text);

    private static ScriptFix FixOf(string text, string code) =>
        ScriptDiagnostics.Analyze(text).Single(d => d.Code == code).Fix!;

    [Test]
    public void UnknownVerb_WithOneClosestMatch_CarriesTheSpanAndTheReplacement()
    {
        var text = "Ticker(\"NVDA\")\n.IsAbveVwap()\n.Long().StopLoss(95)";
        var fix = FixOf(text, "IPS001");
        Assert.Multiple(() =>
        {
            Assert.That(text.Substring(fix.Offset, fix.Length), Is.EqualTo("IsAbveVwap"));
            Assert.That(fix.Replacement, Is.EqualTo("IsAboveVwap"));
            Assert.That((fix.Line, fix.Column), Is.EqualTo((2, 2)));
            Assert.That(fix.Apply(text), Is.EqualTo("Ticker(\"NVDA\")\n.IsAboveVwap()\n.Long().StopLoss(95)"));
            Assert.That(Repair(text).CorrectedScript, Is.EqualTo(fix.Apply(text)));
        });
    }

    [TestCase("Ticker(\"X\")\n.IsAboveEma(9\n.Long()", "Ticker(\"X\")\n.IsAboveEma(9)\n.Long()", TestName = "Unclosed paren before the next verb")]
    [TestCase("Ticker(\"X\").Long().StopLoss(95", "Ticker(\"X\").Long().StopLoss(95)", TestName = "Unclosed paren at the end")]
    [TestCase("Ticker(\"X\").Long())", "Ticker(\"X\").Long()", TestName = "Extra close paren")]
    [TestCase("Ticker(\"X\").Name(\"oops).Long()", "Ticker(\"X\").Name(\"oops\").Long()", TestName = "Unterminated string")]
    [TestCase("Ticker(\"NVDA", "Ticker(\"NVDA\")", TestName = "Unterminated string then its paren, in two passes")]
    [TestCase("Ticker(\"X\").StopLoss(\"2\").TakeProfit($95).Long()", "Ticker(\"X\").StopLoss(2).TakeProfit(95).Long()", TestName = "Quoted and dollar numbers")]
    [TestCase("Ticker(\"X\").IsAboveVwap(5).Entry(3, 4).Long()", "Ticker(\"X\").IsAboveVwap().Entry(3).Long()", TestName = "Arguments the parser ignores")]
    [TestCase("Ticker(\"X\").If(IsAboveVwap()).Then(Long().StopLoss(1)).StopLoss(2)", "Ticker(\"X\").If(IsAboveVwap()).Then(Long().StopLoss(1)).StopLoss(2).EndIf()", TestName = "Missing EndIf at the end")]
    [TestCase("Ticker(\"X\").If(IsAboveVwap()).Then(Long()).If(IsBelowVwap()).Then(Short()).EndIf()", "Ticker(\"X\").If(IsAboveVwap()).Then(Long()).EndIf().If(IsBelowVwap()).Then(Short()).EndIf()", TestName = "Missing EndIf before the next If")]
    [TestCase("Tiker(\"NVDA\").Long().StopLoss(1)", "Ticker(\"NVDA\").Long().StopLoss(1)", TestName = "Misspelled Ticker clears the missing-ticker error too")]
    public void DeterminableProblems_ProduceACorrectedScript_ThatIsClean(string broken, string corrected)
    {
        var repair = Repair(broken);
        Assert.Multiple(() =>
        {
            Assert.That(repair.HasErrors || repair.Diagnostics.Count > 0, Is.True);
            Assert.That(repair.Fixes, Is.Not.Empty);
            Assert.That(repair.CorrectedScript, Is.EqualTo(corrected));
            Assert.That(ScriptDiagnostics.Analyze(corrected), Is.Empty, "the corrected script is offered only when it re-analyzes clean");
            Assert.That(ScriptParser.ParseScript(corrected), Is.Not.Null);
        });
    }

    [TestCase("Ticker(\"X\").IsAboveEma(nine).Long()", "IPS006", TestName = "A word where a number belongs")]
    [TestCase(".IsAboveVwap().Long()", "IPS005", TestName = "No Ticker: which symbol is unknown")]
    [TestCase("Ticker(\"X\").IsBetweenEma(9).Long()", "IPS007", TestName = "A missing required argument has no default")]
    [TestCase("Ticker(\"X\").Zzqxj().Long()", "IPS001", TestName = "An unknown verb with no close match")]
    [TestCase("Ticker(\"X\").Quantity(1.5).Long()", "IPS009", TestName = "A fraction for a whole number: round which way")]
    [TestCase("Ticker(\"X\").Then(Long().StopLoss(1)\n.Else(Short())", "IPS002", TestName = "An unclosed paren around nested calls")]
    [TestCase("Ticker(\"X\").Session(IS.PREMARKET.Long()", "IPS002", TestName = "An unclosed paren after a name")]
    [TestCase("Ticker \"X\").Long()", "IPS003", TestName = "A close paren whose open paren may be what is missing")]
    public void ProblemsWithoutOneCorrectEdit_CarryNoFix_AndNoCorrectedScript(string broken, string code)
    {
        var repair = Repair(broken);
        Assert.Multiple(() =>
        {
            Assert.That(repair.Diagnostics.First(d => d.Code == code).Fix, Is.Null);
            Assert.That(repair.CorrectedScript, Is.Null);
        });
    }

    [Test]
    public void AFixThatExposesAnotherError_YieldsNoCorrectedScript()
    {
        // IsAbveEma becomes IsAboveEma, which then needs a period nobody supplied.
        var repair = Repair("Ticker(\"X\").IsAbveEma().Long()");
        Assert.That(repair.Fixes, Has.Count.EqualTo(1));
        Assert.That(repair.CorrectedScript, Is.Null);
    }

    [Test]
    public void IgnoredArgumentsThatAreACall_AreNotRemoved()
    {
        var d = ScriptDiagnostics.Analyze("Ticker(\"X\").While(IsAboveVwap()).Long()").Single(x => x.Code == "IPS010");
        Assert.That(d.Fix, Is.Null, "While(condition) was meant as something; deleting the condition would hide that");
    }

    [Test]
    public void ACleanScript_HasNoFixes_AndNoCorrectedScript()
    {
        var repair = Repair("Ticker(\"X\").IsAboveVwap().Long().StopLoss(1)");
        Assert.Multiple(() =>
        {
            Assert.That(repair.Diagnostics, Is.Empty);
            Assert.That(repair.Fixes, Is.Empty);
            Assert.That(repair.CorrectedScript, Is.Null);
        });
    }

    [Test]
    public void OverlappingOrCompetingFixes_AreNeverMerged()
    {
        var a = new ScriptFix("IPS001", 2, 4, 1, 3, "x", "a");
        var b = new ScriptFix("IPS006", 4, 2, 1, 5, "y", "b");
        var insert = new ScriptFix("IPS002", 3, 0, 1, 4, ")", "c");
        var other = new ScriptFix("IPS013", 3, 0, 1, 4, ".EndIf()", "d");
        Assert.Multiple(() =>
        {
            Assert.That(ScriptDiagnostics.ApplyFixes("0123456789", [a, b]), Is.Null);
            Assert.That(ScriptDiagnostics.ApplyFixes("0123456789", [insert, other]), Is.Null);
            Assert.That(ScriptDiagnostics.ApplyFixes("0123456789", [insert, insert]), Is.EqualTo("012))3456789"));
        });
    }

    [Test]
    public void Repairing_NeverChangesWhatTheParserBuilds()
    {
        var text = "Ticker(\"X\").IsAbveVwap().IsAboveEma(9).Long().StopLoss(1)";
        var before = ScriptParser.ParseScript(text)!.ShortId();
        _ = Repair(text);
        Assert.That(ScriptParser.ParseScript(text)!.ShortId(), Is.EqualTo(before));
    }

    [Test]
    public void ADiagnostic_PrintsItsFix()
    {
        var d = ScriptDiagnostics.Analyze("Ticker(\"X\").Stoplos(2).Long()").Single();
        Assert.That(d.ToString(), Does.EndWith("fix: Replace 'Stoplos' with 'StopLoss'."));
    }
}
