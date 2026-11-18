using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// Branching states (IP-T529): a step's OnStop / OnTarget / OnExit routes. A route names a later
/// step or "end"; a strategy with no route keeps its exact canon (IP-LAW-14), and the strict reader
/// refuses a route that names no later step.
/// </summary>
public class DslBranchingStepsTests
{
    private const string Ride =
        "Ticker(\"GAPT\").Step(\"ride\").Session(IS.RTH).Quantity(1).IsAboveVwap().Long().StopLossPercent(5).TakeProfitPercent(10).SellBy(\"11:00\")";

    private const string Leg = ".IsAboveVwap().Long().StopLossPercent(4)";

    private static StrategyDefinition Parse(string script) => ScriptParser.ParseScript(script)!;

    private static string Routed =>
        Ride + ".ThenWhen(IsAboveVwap()).Within(1).OnStop(\"end\").OnTarget(\"runner\")"
             + ".Step(\"pullback\")" + Leg + ".OnExit(\"END\")"
             + ".Step(\"runner\")" + Leg;

    [Test]
    public void LinearSequence_WritesNoRouteKeys_AndHandsOnInOrder()
    {
        var def = Parse(Ride + ".ThenWhen(IsAboveVwap()).Within(1).Step(\"again\")" + Leg + ".Step(\"third\")" + Leg);
        var json = StrategyJson.Serialize(def);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("onStop").And.Not.Contain("onTarget").And.Not.Contain("onExit"));
            Assert.That(def.NextStepAfter(0, StepExitKind.Stop), Is.EqualTo(1));
            Assert.That(def.NextStepAfter(1, StepExitKind.Target), Is.EqualTo(2));
            Assert.That(def.NextStepAfter(2, StepExitKind.Other), Is.Null);
            Assert.That(def.WithinDaysInto(1), Is.EqualTo(1));
            Assert.That(def.UnreachableSteps(), Is.Empty);
            Assert.That(ScriptDiagnostics.Analyze(def.ToScript()), Is.Empty);
        });
    }

    [Test]
    public void Routes_RoundTripThroughScriptAndCanon()
    {
        var def = Parse(Routed);
        var json = StrategyJson.Serialize(def);
        var back = StrategyJson.Deserialize(json);
        Assert.Multiple(() =>
        {
            Assert.That(ScriptDiagnostics.Analyze(Routed), Is.Empty);
            Assert.That(def.OnStopStep, Is.EqualTo("end"));
            Assert.That(def.OnTargetStep, Is.EqualTo("runner"));
            Assert.That(def.StepAt(1).OnExitStep, Is.EqualTo("end"), "\"END\" is stored lower case");
            Assert.That(StrategyJson.Serialize(back), Is.EqualTo(json));
            Assert.That(StrategyJson.Serialize(Parse(def.ToScript())), Is.EqualTo(json));
            Assert.That(def.ToScript(), Does.Contain("OnStop(\"end\")").And.Contain("OnTarget(\"runner\")").And.Contain("OnExit(\"end\")"));
        });
    }

    [Test]
    public void Routes_DecideTheNextStep_ByHowTheStepEnded()
    {
        var def = Parse(Routed);
        Assert.Multiple(() =>
        {
            Assert.That(def.NextStepAfter(0, StepExitKind.Stop), Is.Null, "stopped out: the sequence ends");
            Assert.That(def.NextStepAfter(0, StepExitKind.Target), Is.EqualTo(2), "target: skip to the runner");
            Assert.That(def.NextStepAfter(0, StepExitKind.Other), Is.EqualTo(1), "no route: the next step in order");
            Assert.That(def.NextStepAfter(1, StepExitKind.Target), Is.Null, "OnExit covers a stop or target without a route of its own");
            // The Monitor's stored exit reasons and the trailing stop.
            Assert.That(def.NextStepAfter(0, "StopLoss"), Is.Null);
            Assert.That(def.NextStepAfter(0, "TrailingStop"), Is.Null);
            Assert.That(def.NextStepAfter(0, "TargetHit"), Is.EqualTo(2));
            Assert.That(def.NextStepAfter(0, "SellByTime"), Is.EqualTo(1));
            Assert.That(def.NextStepAfter(0, (string?)null), Is.EqualTo(1));
            // Both later steps are handed on to by "ride", whose Within is 1 day.
            Assert.That(def.WithinDaysInto(1), Is.EqualTo(1));
            Assert.That(def.WithinDaysInto(2), Is.EqualTo(1));
        });
    }

    [Test]
    public void ARouteToNoStep_OrBackwards_IsAnError_AndTheStrictReaderRefusesIt()
    {
        var script = Ride + ".OnStop(\"nope\").Step(\"b\")" + Leg + ".OnTarget(\"ride\")";
        var codes = ScriptDiagnostics.Analyze(script).Select(d => d.Code).ToList();
        var def = Parse(script);
        Assert.Multiple(() =>
        {
            Assert.That(codes, Is.EqualTo(new[] { "IPS026", "IPS027" }));
            Assert.That(def.RouteProblems(), Has.Count.EqualTo(2));
            Assert.That(() => StrategyJson.Deserialize(StrategyJson.Serialize(def)), Throws.TypeOf<StrategyJsonException>());
            // A route that slipped through still never arms an earlier or unknown step.
            Assert.That(def.NextStepAfter(0, StepExitKind.Stop), Is.Null);
            Assert.That(def.NextStepAfter(1, StepExitKind.Target), Is.Null);
        });
    }

    [Test]
    public void AStepNamedEnd_IsRefused()
    {
        var script = Ride + ".Step(\"end\")" + Leg;
        Assert.Multiple(() =>
        {
            Assert.That(ScriptDiagnostics.Analyze(script).Select(d => d.Code), Does.Contain("IPS026"));
            Assert.That(() => StrategyJson.Deserialize(StrategyJson.Serialize(Parse(script))), Throws.TypeOf<StrategyJsonException>());
        });
    }

    [Test]
    public void AStepEveryExitRoutesAround_IsAWarning_NotAnError()
    {
        var script = Ride + ".OnExit(\"c\").Step(\"b\")" + Leg + ".Step(\"c\")" + Leg;
        var diags = ScriptDiagnostics.Analyze(script);
        var def = Parse(script);
        Assert.Multiple(() =>
        {
            Assert.That(diags.Select(d => (d.Code, d.Severity)), Is.EqualTo(new[] { ("IPS028", DiagnosticSeverity.Warning) }));
            Assert.That(def.UnreachableSteps(), Is.EqualTo(new[] { 1 }));
            Assert.That(() => StrategyJson.Deserialize(StrategyJson.Serialize(def)), Throws.Nothing);
        });
    }

    [Test]
    public void ARouteKeyThatIsNotAStepName_IsRefusedByTheStrictReader()
    {
        var json = StrategyJson.Serialize(Parse(Ride + ".Step(\"b\")" + Leg));
        var broken = "{\"onStop\":5," + json.TrimStart()[1..];
        Assert.That(() => StrategyJson.Deserialize(broken), Throws.TypeOf<StrategyJsonException>());
    }

    [Test]
    public void RouteLines_SayWhereEachExitLeads()
    {
        var def = Parse(Routed);
        Assert.Multiple(() =>
        {
            Assert.That(def.RouteLines(), Is.EqualTo(new[]
            {
                "stopped out: the sequence ends",
                "target hit: go to step \"runner\"",
                "any other exit: the next step in order",
            }));
            Assert.That(def.StepAt(1).RouteLines(), Is.EqualTo(new[] { "any exit: the sequence ends" }));
            Assert.That(def.StepAt(2).RouteLines(), Is.Empty);
        });
    }
}
