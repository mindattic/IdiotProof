using System.Text.Json;
using IdiotProof.Operations.Dsl;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Dsl;

/// <summary>IP-T396: branched strategies format to text that round-trips, and malformed blocks fail validation.</summary>
public class DslBranchOperationTests
{
    private static OperationContext Context() => new() { Services = new ServiceCollection().BuildServiceProvider() };

    private static JsonElement Data(OperationResult r) => JsonSerializer.SerializeToElement(r.Data, OperationJson.Options);

    private const string Branched =
        "Ticker(\"NVDA\")\n.BreaksAbove(100)\n.Long()\n.StopLossPercent(2)\n" +
        ".If(IsAboveVwap().And(IsAboveEma(9)))\n    .Then(StopLossPercent(1).TakeProfit(110))\n" +
        "    .ElseIf(IsBelowVwap(), Short().AddTarget(95, 100, \"T1\"))\n    .Else(TrailingStopLoss(1))\n    .EndIf()";

    [Test]
    public async Task Format_OfABranchedScript_RoundTrips()
    {
        var r = await new DslFormatOperation().ExecuteAsync(new DslTextArgs { Text = Branched }, Context());
        Assert.That(r.IsOk, Is.True, r.Message);
        var data = Data(r);
        Assert.That(data.GetProperty("round_trips").GetBoolean(), Is.True);
        Assert.That(data.GetProperty("script").GetString(), Does.Contain(".ElseIf(IsBelowVwap(), Short().AddTarget(95, 100, \"T1\"))"));
    }

    [Test]
    public async Task Validate_OfABranchedScript_HasNoSyntaxFindings()
    {
        var r = await new DslValidateOperation().ExecuteAsync(new DslTextArgs { Text = Branched }, Context());
        Assert.That(Data(r).GetProperty("findings").EnumerateArray()
            .Any(f => f.GetProperty("rule").GetString()!.StartsWith("syntax")), Is.False, r.Human);
    }

    [Test]
    public async Task Validate_FailsOnAMissingEndIf()
    {
        var text = Branched.Replace("\n    .EndIf()", "");
        var r = await new DslValidateOperation().ExecuteAsync(new DslTextArgs { Text = text }, Context());
        Assert.That(r.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(Data(r).GetProperty("findings").EnumerateArray()
            .Any(f => f.GetProperty("rule").GetString() == "syntax IPS013"), Is.True, r.Human);
    }
}
