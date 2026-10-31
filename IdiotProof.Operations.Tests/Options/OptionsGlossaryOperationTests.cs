using IdiotProof.Operations.Options;
using IdiotProof.Shared.Options;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Options;

/// <summary>options glossary (IP-T199): the web page's own jargon, listed or explained in full.</summary>
public class OptionsGlossaryOperationTests
{
    private static OperationContext Context() => new() { Services = new ServiceCollection().BuildServiceProvider() };

    [Test]
    public async Task NoTerm_ListsEveryEntry()
    {
        var result = await new OptionsGlossaryOperation().ExecuteAsync(new OptionsGlossaryArgs(), Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("hype").And.Contain("breakeven").And.Contain($"{OptionsGlossary.All.Count} terms"));
        Assert.That(OptionsDataKit.Data(result).GetProperty("terms").GetArrayLength(), Is.EqualTo(OptionsGlossary.All.Count));
    }

    [Test]
    public async Task OneTerm_PrintsTheFullExplanation_ByKeyOrTitle()
    {
        var byKey = await new OptionsGlossaryOperation().ExecuteAsync(new OptionsGlossaryArgs { Term = "Sell to close" }, Context());
        var byTitle = await new OptionsGlossaryOperation().ExecuteAsync(new OptionsGlossaryArgs { Term = "breakeven" }, Context());

        Assert.That(byKey.IsOk, Is.True, byKey.Message);
        Assert.That(byKey.Human, Does.Contain(OptionsGlossary.Get("sell-to-close").Long));
        Assert.That(byTitle.Human, Does.Contain(OptionsGlossary.Get("breakeven").Long));
    }

    [Test]
    public async Task UnknownTerm_IsAUsageErrorWithSuggestions()
    {
        var result = await new OptionsGlossaryOperation().ExecuteAsync(new OptionsGlossaryArgs { Term = "hyp" }, Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(result.Message, Does.Contain("hype"));
    }
}
