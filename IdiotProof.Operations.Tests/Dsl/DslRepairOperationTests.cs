using System.Text.Json;
using IdiotProof.Operations.Dsl;
using IdiotProof.Operations.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Dsl;

/// <summary>
/// IP-T523: dsl validate and dsl parse return each fix and the corrected script as data, apply
/// them only on --fix, and an import error carries the same payload without applying it.
/// </summary>
public class DslRepairOperationTests
{
    private const string Broken = "Ticker(\"NVDA\")\n.IsAbveVwap()\n.Long().StopLoss(95).TakeProfit(110)";
    private const string Corrected = "Ticker(\"NVDA\")\n.IsAboveVwap()\n.Long().StopLoss(95).TakeProfit(110)";

    private static OperationContext Context() => new() { Services = new ServiceCollection().BuildServiceProvider() };

    private static JsonElement Data(OperationResult r) => JsonSerializer.SerializeToElement(r.Data, OperationJson.Options);

    [Test]
    public async Task Validate_WithoutFix_StillFails_ButCarriesTheFixAndTheCorrectedScript()
    {
        var r = await new DslValidateOperation().ExecuteAsync(new DslCheckArgs { Text = Broken }, Context());
        var data = Data(r);
        var fix = data.GetProperty("findings").EnumerateArray()
            .Single(f => f.GetProperty("rule").GetString() == "syntax IPS001").GetProperty("fix");

        Assert.Multiple(() =>
        {
            Assert.That(r.Status, Is.EqualTo(OperationStatus.Failed), "the text as written is still refused");
            Assert.That(data.GetProperty("fixed").GetBoolean(), Is.False);
            Assert.That(data.TryGetProperty("script", out _), Is.False);
            Assert.That(fix.GetProperty("offset").GetInt32(), Is.EqualTo(16));
            Assert.That(fix.GetProperty("length").GetInt32(), Is.EqualTo(10));
            Assert.That(fix.GetProperty("replacement").GetString(), Is.EqualTo("IsAboveVwap"));
            Assert.That(data.GetProperty("fixes").GetArrayLength(), Is.EqualTo(1));
            Assert.That(data.GetProperty("corrected_script").GetString(), Is.EqualTo(Corrected));
            Assert.That(r.Human, Does.Contain("Corrected script").And.Contain(".IsAboveVwap()").And.Contain("--fix"));
        });
    }

    [Test]
    public async Task Validate_WithFix_ReturnsTheCorrectedScript_AndItsVerdict()
    {
        var r = await new DslValidateOperation().ExecuteAsync(new DslCheckArgs { Text = Broken, Fix = true }, Context());
        var data = Data(r);

        Assert.Multiple(() =>
        {
            Assert.That(r.IsOk, Is.True, r.Human);
            Assert.That(data.GetProperty("fixed").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("script").GetString(), Is.EqualTo(Corrected));
            Assert.That(data.GetProperty("valid").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("fixes").GetArrayLength(), Is.EqualTo(1), "what was changed is still reported");
            Assert.That(data.GetProperty("findings").EnumerateArray()
                .Any(f => f.GetProperty("rule").GetString()!.StartsWith("syntax")), Is.False);
            Assert.That(r.Human, Does.Contain("Fixed: 1 fix(es) applied"));
        });
    }

    [Test]
    public async Task Validate_WithFix_WhenNothingIsDeterminable_ChangesNothing()
    {
        var text = "Ticker(\"NVDA\").IsAboveEma(nine).Long().StopLoss(95)";
        var r = await new DslValidateOperation().ExecuteAsync(new DslCheckArgs { Text = text, Fix = true }, Context());
        var data = Data(r);

        Assert.Multiple(() =>
        {
            Assert.That(r.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(data.GetProperty("fixed").GetBoolean(), Is.False);
            Assert.That(data.TryGetProperty("corrected_script", out _), Is.False);
            Assert.That(data.GetProperty("fixes").GetArrayLength(), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Validate_OfJson_HasNoScriptFixes()
    {
        var parsed = await new DslParseOperation().ExecuteAsync(new DslCheckArgs { Text = Corrected }, Context());
        var canon = Data(parsed).GetProperty("canon").GetRawText();
        var r = await new DslValidateOperation().ExecuteAsync(new DslCheckArgs { Text = canon, Fix = true }, Context());
        Assert.That(Data(r).GetProperty("fixed").GetBoolean(), Is.False);
        Assert.That(Data(r).GetProperty("fixes").GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public async Task Parse_WithoutFix_ParsesTheTextAsWritten_AndReportsTheCorrection()
    {
        var r = await new DslParseOperation().ExecuteAsync(new DslCheckArgs { Text = Broken }, Context());
        var clean = await new DslParseOperation().ExecuteAsync(new DslCheckArgs { Text = Corrected }, Context());
        var data = Data(r);

        Assert.Multiple(() =>
        {
            Assert.That(r.IsOk, Is.True, r.Message);
            Assert.That(data.GetProperty("fixed").GetBoolean(), Is.False);
            Assert.That(data.GetProperty("stamp").GetString(), Is.Not.EqualTo(Data(clean).GetProperty("stamp").GetString()),
                "the misspelled verb was skipped, not corrected");
            Assert.That(data.GetProperty("diagnostics").EnumerateArray().Single().GetProperty("fix").GetProperty("replacement").GetString(),
                Is.EqualTo("IsAboveVwap"));
            Assert.That(data.GetProperty("corrected_script").GetString(), Is.EqualTo(Corrected));
        });
    }

    [Test]
    public async Task Parse_WithFix_ParsesTheCorrectedScript()
    {
        var r = await new DslParseOperation().ExecuteAsync(new DslCheckArgs { Text = Broken, Fix = true }, Context());
        var clean = await new DslParseOperation().ExecuteAsync(new DslCheckArgs { Text = Corrected }, Context());
        var data = Data(r);

        Assert.Multiple(() =>
        {
            Assert.That(data.GetProperty("fixed").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("script").GetString(), Is.EqualTo(Corrected));
            Assert.That(data.GetProperty("stamp").GetString(), Is.EqualTo(Data(clean).GetProperty("stamp").GetString()));
        });
    }

    [Test]
    public void Import_OfAScriptThatDoesNotParse_CarriesTheCorrectedScript_ButDoesNotApplyIt()
    {
        var ex = Assert.Throws<OperationException>(() => StrategyImportOperation.Parse("Tiker(\"NVDA\").IsAboveVwap().Long().StopLoss(95)"));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Status, Is.EqualTo(OperationStatus.UsageError), "saving paths refuse the script; they never take the correction");
            Assert.That(ex.Payload, Is.InstanceOf<DslRepairData>());
            var payload = (DslRepairData)ex.Payload!;
            Assert.That(payload.CorrectedScript, Is.EqualTo("Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(95)"));
            Assert.That(payload.Fixes.Single().Replacement, Is.EqualTo("Ticker"));
            Assert.That(ex.Message, Does.Contain("Corrected script"));
        });
    }
}
