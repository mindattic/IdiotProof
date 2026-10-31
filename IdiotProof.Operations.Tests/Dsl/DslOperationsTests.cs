using System.Text.Json;
using IdiotProof.Operations.Dsl;
using IdiotProof.Scripting;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Tests.Dsl;

/// <summary>dsl verbs|glossary|parse|format|validate (IP-T209..IP-T213).</summary>
public class DslOperationsTests
{
    private const string Script = "Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(95).TakeProfit(110)";

    private static OperationContext Context() => new() { Services = new ServiceCollection().BuildServiceProvider() };

    private static JsonElement Data(OperationResult r) => JsonSerializer.SerializeToElement(r.Data, OperationJson.Options);

    [Test]
    public async Task Verbs_AreReflectedFromStrategyBuilder_ByPhase()
    {
        var all = await new DslVerbsOperation().ExecuteAsync(new DslVerbsArgs(), Context());
        var risk = await new DslVerbsOperation().ExecuteAsync(new DslVerbsArgs { Phase = "risk" }, Context());

        Assert.That(all.IsOk, Is.True, all.Message);
        // IP-LAW-4: every public StrategyBuilder verb is listed, because the list IS the reflection.
        var builder = typeof(StrategyBuilder).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(StrategyBuilder) && !m.IsSpecialName)
            .Select(m => m.Name).Except(["Build", "If", "ToScript"]).Distinct();
        var listed = Data(all).GetProperty("verbs").EnumerateArray()
            .Where(v => v.GetProperty("phase").GetString() != DslVerbsOperation.ConditionsPhase)
            .Select(v => v.GetProperty("name").GetString());
        Assert.That(listed, Is.SupersetOf(builder));
        Assert.That(all.Human, Does.Contain("SETUP").And.Contain("EXIT").And.Contain("CONDITIONS"));
        var riskVerbs = Data(risk).GetProperty("verbs").EnumerateArray().Select(v => v.GetProperty("name").GetString()).ToList();
        Assert.That(riskVerbs, Does.Contain("StopLoss"));
        Assert.That(riskVerbs.All(v => v!.StartsWith("StopLoss") || v.StartsWith("TrailingStop")), Is.True);

        var bad = await new DslVerbsOperation().ExecuteAsync(new DslVerbsArgs { Phase = "launch" }, Context());
        Assert.That(bad.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task Glossary_ListsEveryEntry_AndResolvesAliases()
    {
        var all = await new DslGlossaryOperation().ExecuteAsync(new DslGlossaryArgs(), Context());
        var alias = await new DslGlossaryOperation().ExecuteAsync(new DslGlossaryArgs { Verb = "RequireAdxAbove" }, Context());
        var typo = await new DslGlossaryOperation().ExecuteAsync(new DslGlossaryArgs { Verb = "IsAboveVwapp" }, Context());

        Assert.That(Data(all).GetProperty("entries").GetArrayLength(), Is.EqualTo(DslGlossary.All.Count));
        Assert.That(alias.IsOk, Is.True, alias.Message);
        Assert.That(Data(alias).GetProperty("key").GetString(), Is.EqualTo("IsAdxAbove"));
        Assert.That(typo.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(typo.Message, Does.Contain("IsAboveVwap"));
    }

    [Test]
    public async Task Parse_GivesTheCanon_AndFormat_GivesTheScriptBack()
    {
        var parsed = await new DslParseOperation().ExecuteAsync(new DslTextArgs { Text = Script }, Context());
        Assert.That(parsed.IsOk, Is.True, parsed.Message);
        var canon = Data(parsed).GetProperty("canon").GetRawText();
        var def = StrategyJson.Deserialize(canon);
        Assert.That(def.Symbol, Is.EqualTo("NVDA"));
        Assert.That(Data(parsed).GetProperty("stamp").GetString(), Is.EqualTo(def.ShortId()));

        var formatted = await new DslFormatOperation().ExecuteAsync(new DslTextArgs { Text = canon }, Context());
        Assert.That(formatted.IsOk, Is.True, formatted.Message);
        Assert.That(Data(formatted).GetProperty("script").GetString(), Is.EqualTo(def.ToScript()));
        Assert.That(Data(formatted).GetProperty("round_trips").GetBoolean(), Is.True);
        Assert.That(Data(formatted).GetProperty("source").GetString(), Is.EqualTo("canonical JSON"));
    }

    [Test]
    public void Parse_Garbage_IsAUsageError()
    {
        var ex = Assert.ThrowsAsync<OperationException>(() => new DslParseOperation().ExecuteAsync(new DslTextArgs { Text = "{ not json" }, Context()));
        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task Validate_AGoodStrategy_IsValid()
    {
        var result = await new DslValidateOperation().ExecuteAsync(new DslTextArgs { Text = Script }, Context());

        Assert.That(result.IsOk, Is.True, result.Human);
        Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.True);
        Assert.That(result.Human, Does.StartWith("VALID: NVDA"));
    }

    [Test]
    public async Task Validate_FailsClosed_WithEveryReason()
    {
        // No stop, and the target sits on the wrong side for a short is not even checkable: no stop at all.
        var noStop = await new DslValidateOperation().ExecuteAsync(new DslTextArgs { Text = "Ticker(\"NVDA\").IsAboveVwap().Long().TakeProfit(110)" }, Context());
        var wrongSide = await new DslValidateOperation().ExecuteAsync(new DslTextArgs { Text = "Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(120).TakeProfit(110)" }, Context());
        var garbage = await new DslValidateOperation().ExecuteAsync(new DslTextArgs { Text = "{ \"schemaVersion\": 99 }" }, Context());

        Assert.Multiple(() =>
        {
            Assert.That(noStop.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(Rules(noStop), Does.Contain("stop"));
            Assert.That(noStop.Human, Does.Contain("IP-LAW-2"));
            Assert.That(wrongSide.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(Rules(wrongSide), Does.Contain("stop side"));
            Assert.That(garbage.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(Rules(garbage), Does.Contain("parses"));
        });
    }

    private static List<string?> Rules(OperationResult r) =>
        Data(r).GetProperty("findings").EnumerateArray().Where(f => f.GetProperty("severity").GetString() == "error")
            .Select(f => f.GetProperty("rule").GetString()).ToList();
}
