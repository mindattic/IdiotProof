using System.Reflection;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-T526: <see cref="ScriptDiagnosticCodes"/> is the one table of IdiotScript diagnostic codes,
/// and <see cref="ScriptDiagnostics"/> reports only through it. Every code the analyzer emits on a
/// corpus that reaches every code is in the table with the severity it was emitted with; a code
/// that emits a fix is declared as fixable and one declared fixable does emit a fix; and no code
/// sits in the table that nothing emits.
/// </summary>
public class ScriptDiagnosticCodesTests
{
    private static readonly IReadOnlyList<ScriptDiagnostic> Emitted = [.. ScriptDiagnosticCorpus.Scripts.SelectMany(ScriptDiagnostics.Analyze)];

    [Test]
    public void TheTable_IsWellFormed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ScriptDiagnosticCodes.All, Is.Not.Empty);
            Assert.That(ScriptDiagnosticCodes.All.Select(d => d.Code), Is.Unique.And.Ordered);
            foreach (var d in ScriptDiagnosticCodes.All)
            {
                Assert.That(d.Code, Does.Match("^IPS[0-9]{3}$"));
                Assert.That(d.Title, Is.Not.Empty, d.Code);
                Assert.That(d.Meaning, Is.Not.Empty, d.Code);
                Assert.That(d.Fix is null, Is.Not.EqualTo(d.NoFix is null), d.Code + " says what its fix does, or why it has none, and not both");
                Assert.That(d.CanFix, Is.EqualTo(d.Fix is not null), d.Code);
                Assert.That(ScriptDiagnosticCodes.Find(d.Code.ToLowerInvariant()), Is.SameAs(d));
            }
        });
    }

    [Test]
    public void EveryField_IsInTheTable()
    {
        var fields = typeof(ScriptDiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ScriptDiagnosticDescriptor)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(fields.Select(f => ((ScriptDiagnosticDescriptor)f.GetValue(null)!).Code), Is.EquivalentTo(ScriptDiagnosticCodes.All.Select(d => d.Code)));
            foreach (var f in fields)
                Assert.That(((ScriptDiagnosticDescriptor)f.GetValue(null)!).Code, Is.EqualTo(f.Name), "a field is named after its code");
        });
    }

    [Test]
    public void TheAnalyzer_ReportsOnlyThroughADescriptor()
    {
        // The private reporting helpers take a descriptor, not a code string and a severity.
        var helpers = typeof(ScriptDiagnostics).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(ScriptDiagnostic) || m.ReturnType == typeof(ScriptFix)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(helpers.Select(m => m.Name), Is.SupersetOf(new[] { "Make", "MakeFix" }));
            foreach (var m in helpers.Where(m => m.Name is "Make" or "MakeFix"))
            {
                Assert.That(m.GetParameters()[0].ParameterType, Is.EqualTo(typeof(ScriptDiagnosticDescriptor)), m.Name);
                Assert.That(m.GetParameters().Select(p => p.ParameterType), Has.None.EqualTo(typeof(DiagnosticSeverity)), m.Name);
            }
        });
    }

    [Test]
    public void EveryCodeTheAnalyzerEmits_IsInTheTable_WithTheSameSeverity()
    {
        Assert.Multiple(() =>
        {
            foreach (var d in Emitted)
            {
                var descriptor = ScriptDiagnosticCodes.Find(d.Code);
                Assert.That(descriptor, Is.Not.Null, d.Code + " is emitted but not in ScriptDiagnosticCodes");
                Assert.That(d.Severity, Is.EqualTo(descriptor!.Severity), d.Code);
                if (d.Fix is not null)
                {
                    Assert.That(descriptor.CanFix, Is.True, d.Code + " carried a fix but the table says it never does");
                    Assert.That(d.Fix.Code, Is.EqualTo(d.Code));
                }
            }
        });
    }

    [Test]
    public void EveryCodeInTheTable_IsEmitted_AndEveryFixableOneEmitsAFix()
    {
        var emitted = Emitted.Select(d => d.Code).ToHashSet();
        var withFix = Emitted.Where(d => d.Fix is not null).Select(d => d.Code).ToHashSet();
        Assert.Multiple(() =>
        {
            foreach (var d in ScriptDiagnosticCodes.All)
            {
                Assert.That(emitted, Does.Contain(d.Code), d.Code + ": add a script that triggers it to the corpus, or remove the code");
                if (d.CanFix) Assert.That(withFix, Does.Contain(d.Code), d.Code + " is declared fixable but no corpus script gets a fix for it");
                else Assert.That(withFix, Does.Not.Contain(d.Code), d.Code);
            }
        });
    }
}
