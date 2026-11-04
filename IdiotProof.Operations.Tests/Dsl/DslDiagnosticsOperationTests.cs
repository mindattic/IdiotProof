using System.Text.Json;
using IdiotProof.Operations.Dsl;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Dsl;

/// <summary>IP-US-H1: dsl validate and dsl parse report line/column diagnostics for IdiotScript.</summary>
public class DslDiagnosticsOperationTests
{
    private static OperationContext Context() => new() { Services = new ServiceCollection().BuildServiceProvider() };

    private static JsonElement Data(OperationResult r) => JsonSerializer.SerializeToElement(r.Data, OperationJson.Options);

    [Test]
    public async Task Validate_FailsClosed_OnAnUnknownVerb_WithItsLineColumnAndSuggestion()
    {
        var text = "Ticker(\"NVDA\")\n.IsAbveVwap()\n.Long().StopLoss(95).TakeProfit(110)";
        var r = await new DslValidateOperation().ExecuteAsync(new DslTextArgs { Text = text }, Context());

        Assert.That(r.Status, Is.EqualTo(OperationStatus.Failed));
        var syntax = Data(r).GetProperty("findings").EnumerateArray()
            .Single(f => f.GetProperty("rule").GetString() == "syntax IPS001");
        Assert.That(syntax.GetProperty("message").GetString(), Does.StartWith("line 2, col 2:").And.Contain("'IsAboveVwap'"));
    }

    [Test]
    public async Task Validate_AWellFormedScript_HasNoSyntaxFindings()
    {
        var r = await new DslValidateOperation().ExecuteAsync(
            new DslTextArgs { Text = "Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(95).TakeProfit(110)" }, Context());
        Assert.That(r.IsOk, Is.True, r.Human);
        Assert.That(Data(r).GetProperty("findings").EnumerateArray()
            .Any(f => f.GetProperty("rule").GetString()!.StartsWith("syntax")), Is.False);
    }

    [Test]
    public async Task Parse_StillParsesWhatItCan_ButSaysWhatItSkipped()
    {
        var r = await new DslParseOperation().ExecuteAsync(
            new DslTextArgs { Text = "Ticker(\"NVDA\").IsAboveEma(nine).Long().StopLoss(95)" }, Context());

        Assert.That(r.IsOk, Is.True, r.Message);
        var d = Data(r).GetProperty("diagnostics").EnumerateArray().Single();
        Assert.That(d.GetProperty("code").GetString(), Is.EqualTo("IPS006"));
        Assert.That(d.GetProperty("column").GetInt32(), Is.EqualTo(27));
        Assert.That(r.Human, Does.Contain("1:27 error IPS006").And.Contain("^^^^"));
    }

    [Test]
    public async Task Parse_OfJson_CarriesNoScriptDiagnostics()
    {
        var script = await new DslParseOperation().ExecuteAsync(
            new DslTextArgs { Text = "Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(95)" }, Context());
        var canon = Data(script).GetProperty("canon").GetRawText();
        var r = await new DslParseOperation().ExecuteAsync(new DslTextArgs { Text = canon }, Context());
        Assert.That(Data(r).GetProperty("diagnostics").GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public void Import_OfAnUnparseableScript_ExplainsWhy()
    {
        var ex = Assert.Throws<OperationException>(() => IdiotProof.Operations.Strategies.StrategyImportOperation.Parse(".IsAboveVwap().Long()"));
        Assert.That(ex!.Message, Does.Contain("didn't parse").And.Contain("IPS005"));
    }
}
