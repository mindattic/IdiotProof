using IdiotProof.Blazor.Services;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Verifies IP-LAW-4: the verb catalog in the LLM system prompt is built from reflection
/// on StrategyBuilder + Conditions, so it can never drift from the actual codebase.
/// </summary>
[TestFixture]
public sealed class StrategyScriptGeneratorTests
{
    // ── Verb catalog reflection (IP-LAW-4) ──

    [Test]
    public void BuildSystemPrompt_ContainsStrategyBuilderSection()
    {
        var prompt = StrategyScriptGenerator.BuildSystemPrompt();
        Assert.That(prompt, Does.Contain("StrategyBuilder verbs"));
    }

    [Test]
    public void BuildSystemPrompt_ContainsLong_FromStrategyBuilder()
    {
        var prompt = StrategyScriptGenerator.BuildSystemPrompt();
        Assert.That(prompt, Does.Contain("Long()"));
    }

    [Test]
    public void BuildSystemPrompt_ContainsShort_FromStrategyBuilder()
    {
        var prompt = StrategyScriptGenerator.BuildSystemPrompt();
        Assert.That(prompt, Does.Contain("Short()"));
    }

    [Test]
    public void BuildSystemPrompt_ContainsStopLoss_FromStrategyBuilder()
    {
        var prompt = StrategyScriptGenerator.BuildSystemPrompt();
        Assert.That(prompt, Does.Contain("StopLoss("));
    }

    [Test]
    public void BuildSystemPrompt_ContainsIsAboveVwap_FromStrategyBuilder()
    {
        var prompt = StrategyScriptGenerator.BuildSystemPrompt();
        Assert.That(prompt, Does.Contain("IsAboveVwap()"));
    }

    [Test]
    public void BuildSystemPrompt_ContainsConditionsSection()
    {
        var prompt = StrategyScriptGenerator.BuildSystemPrompt();
        Assert.That(prompt, Does.Contain("Static `Conditions` catalog"));
    }

    [Test]
    public void BuildSystemPrompt_ContainsIsAboveVwap_FromConditions()
    {
        var prompt = StrategyScriptGenerator.BuildSystemPrompt();
        // Conditions.IsAboveVwap is a static property — should appear in the catalog
        Assert.That(prompt, Does.Contain("IsAboveVwap"));
    }

    [Test]
    public void BuildSystemPrompt_ContainsOnReclaim_FromConditions()
    {
        var prompt = StrategyScriptGenerator.BuildSystemPrompt();
        // Conditions.OnReclaim is a static method — should appear in the catalog
        Assert.That(prompt, Does.Contain("OnReclaim("));
    }

    // ── Phase bucketing (GetVerbsByPhase) ──

    [Test]
    public void GetVerbsByPhase_BucketsRollingAndPriorHighExitsUnderExit_NotEntry()
    {
        var groups = StrategyScriptGenerator.GetVerbsByPhase();
        var exitVerbs = groups.Single(g => g.Phase == "Exit").Verbs;
        var entryVerbs = groups.Single(g => g.Phase == "Entry").Verbs;

        Assert.Multiple(() =>
        {
            Assert.That(exitVerbs, Has.Some.StartsWith("ExitAtRollingHigh("));
            Assert.That(exitVerbs, Has.Some.StartsWith("ExitAtRollingLow("));
            Assert.That(exitVerbs, Has.Some.StartsWith("ExitAtPriorHigh("));
            Assert.That(entryVerbs, Has.None.StartsWith("ExitAtRollingHigh("));
            Assert.That(entryVerbs, Has.None.StartsWith("ExitAtRollingLow("));
            Assert.That(entryVerbs, Has.None.StartsWith("ExitAtPriorHigh("));
        });
    }

    // ── Code-fence stripping ──

    [Test]
    public void StripCodeFence_RemovesCsharpFence()
    {
        const string input = "```csharp\nStock.Ticker(\"NVDA\").Long().Build();\n```";
        var result = StrategyScriptGenerator.StripCodeFence(input);
        Assert.That(result, Is.EqualTo("Stock.Ticker(\"NVDA\").Long().Build();"));
    }

    [Test]
    public void StripCodeFence_RemovesPlainFence()
    {
        const string input = "```\nStock.Ticker(\"SPY\").Short().Build();\n```";
        var result = StrategyScriptGenerator.StripCodeFence(input);
        Assert.That(result, Is.EqualTo("Stock.Ticker(\"SPY\").Short().Build();"));
    }

    [Test]
    public void StripCodeFence_LeavesCleanCodeUntouched()
    {
        const string clean = "Stock.Ticker(\"AAPL\").Long().Build();";
        var result = StrategyScriptGenerator.StripCodeFence(clean);
        Assert.That(result, Is.EqualTo(clean));
    }
}
