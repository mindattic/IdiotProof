using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// <see cref="StrategyHtml"/> renders user-authored strategy text into pages (the replay archive, the
/// Blazor UI), so its escaping is an injection boundary.
/// </summary>
[TestFixture]
public sealed class StrategyHtmlTests
{
    /// <summary>A condition whose script text is whatever the test says (user-controlled text stand-in).</summary>
    private sealed class TextCondition(string script, StrategyPhase phase = StrategyPhase.Entry) : ICondition
    {
        public string ToScript() => script;
        public bool Evaluate(IndicatorSnapshot indicators) => false;
        public StrategyPhase Phase => phase;
    }

    private const string Hostile = "<img src=x onerror=\"alert('x')\"> & co";

    [Test]
    public void ToHtml_EncodesConditionScriptText()
    {
        // IP-T340: Filters/Entry card rows were emitted raw while Branch rows were encoded.
        var def = new StrategyDefinition { Symbol = "ABC", Quantity = 1 };
        def.EntryConditions.Add(new TextCondition(Hostile, StrategyPhase.Filters));
        def.EntryConditions.Add(new TextCondition(Hostile));

        var html = def.ToHtml();

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("<img"));
            Assert.That(html, Does.Not.Contain("\"alert"));
            Assert.That(html, Does.Contain("&lt;img src=x onerror=&quot;alert(&#39;x&#39;)&quot;&gt; &amp; co"));
        });
    }
}
