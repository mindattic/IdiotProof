using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The generator repair loop (IP-T524): ask, validate, send the errors back a bounded number of
/// times, and never return a value the validator did not accept.
/// </summary>
[TestFixture]
public sealed class GenerationRepairLoopTests
{
    private sealed record Parsed(string Text);

    /// <summary>A scripted model: one reply per call (the last repeats), every feedback kept.</summary>
    private sealed class ScriptedModel(params string[] replies)
    {
        public List<GenerationFeedback?> Feedback { get; } = [];

        public Task<string> Ask(GenerationFeedback? feedback, CancellationToken ct)
        {
            Feedback.Add(feedback);
            return Task.FromResult(replies[Math.Min(Feedback.Count - 1, replies.Length - 1)]);
        }
    }

    /// <summary>"good" is valid, "fixable" is corrected without the model, "no" is the model declining, the rest is rejected.</summary>
    private static GenerationCheck<Parsed> Inspect(string output) => output switch
    {
        "good" => GenerationCheck<Parsed>.Valid(new Parsed(output)),
        "fixable" => GenerationCheck<Parsed>.Corrected(new Parsed("good"), ["a typo"], ["Replace 'fixable' with 'good'."]),
        "no" => GenerationCheck<Parsed>.Declined("can't be expressed"),
        _ => GenerationCheck<Parsed>.Rejected([$"'{output}' is not valid"], ["write 'good'"]),
    };

    [Test]
    public async Task AValidFirstOutput_UsesOneCall_AndReportsNothingRepaired()
    {
        var model = new ScriptedModel("good");
        var outcome = await GenerationRepairLoop.RunAsync<Parsed>(model.Ask, Inspect, maxRounds: 2);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.Repair.ModelCalls, Is.EqualTo(1));
            Assert.That(outcome.Repair.Rounds, Is.Zero);
            Assert.That(outcome.Repair.Clean, Is.True);
            Assert.That(outcome.Repair.Steps, Is.Empty);
            Assert.That(outcome.Repair.Render(), Is.Empty);
        });
    }

    [Test]
    public async Task ADeterministicFix_IsTakenWithoutAnotherCall()
    {
        var model = new ScriptedModel("fixable", "good");
        var outcome = await GenerationRepairLoop.RunAsync<Parsed>(model.Ask, Inspect, maxRounds: 2);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Value, Is.EqualTo(new Parsed("good")));
            Assert.That(model.Feedback, Has.Count.EqualTo(1), "the model is not asked again");
            Assert.That(outcome.Repair.Rounds, Is.Zero);
            Assert.That(outcome.Repair.DeterministicFixes, Is.EqualTo(1));
            Assert.That(outcome.Repair.Steps.Single().Kind, Is.EqualTo(RepairStep.Deterministic));
            Assert.That(outcome.Repair.Steps.Single().Fixes.Single(), Does.Contain("Replace 'fixable'"));
            Assert.That(outcome.Repair.Clean, Is.False);
        });
    }

    [Test]
    public async Task BadThenGood_SendsTheModelItsOwnOutputWithTheErrorsAndFixes()
    {
        var model = new ScriptedModel("bad", "good");
        var outcome = await GenerationRepairLoop.RunAsync<Parsed>(model.Ask, Inspect, maxRounds: 2);

        var feedback = model.Feedback[1]!;
        var sent = feedback.Render();
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(model.Feedback[0], Is.Null, "the first call carries no feedback");
            Assert.That(feedback.Round, Is.EqualTo(1));
            Assert.That(feedback.PreviousOutput, Is.EqualTo("bad"));
            Assert.That(sent, Does.StartWith(GenerationFeedback.RoundMarker + "1"));
            Assert.That(sent, Does.Contain("'bad' is not valid").And.Contain("write 'good'"));
            Assert.That(outcome.Repair.ModelCalls, Is.EqualTo(2));
            Assert.That(outcome.Repair.Rounds, Is.EqualTo(1));
            Assert.That(outcome.Repair.Steps.Single().Kind, Is.EqualTo(RepairStep.Model));
            Assert.That(outcome.Repair.Steps.Single().Resolved, Is.True);
            Assert.That(outcome.Repair.Steps.Single().Problems.Single(), Does.Contain("'bad' is not valid"));
        });
    }

    [Test]
    public async Task NeverValid_StopsAtTheBound_AndFailsClosedWithTheLastDiagnostics()
    {
        var model = new ScriptedModel("bad", "worse", "worst", "good");
        var outcome = await GenerationRepairLoop.RunAsync<Parsed>(model.Ask, Inspect, maxRounds: 2);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.Value, Is.Null, "nothing unvalidated is ever returned");
            Assert.That(model.Feedback, Has.Count.EqualTo(3), "one call plus two repair rounds, never the fourth");
            Assert.That(outcome.Output, Is.EqualTo("worst"));
            Assert.That(outcome.Errors.Single(), Does.Contain("'worst' is not valid"));
            Assert.That(outcome.Repair.Rounds, Is.EqualTo(2));
            Assert.That(outcome.Repair.MaxRounds, Is.EqualTo(2));
            Assert.That(outcome.Repair.Steps.Select(s => s.Resolved), Is.All.False);
        });
    }

    [Test]
    public async Task ZeroRounds_IsOneCallAndNoSecondChance()
    {
        var model = new ScriptedModel("bad", "good");
        var outcome = await GenerationRepairLoop.RunAsync<Parsed>(model.Ask, Inspect, maxRounds: 0);

        Assert.That(outcome.Success, Is.False);
        Assert.That(model.Feedback, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AModelThatDeclines_IsNotAskedAgain()
    {
        var model = new ScriptedModel("no", "good");
        var outcome = await GenerationRepairLoop.RunAsync<Parsed>(model.Ask, Inspect, maxRounds: 2);

        Assert.That(outcome.Success, Is.False);
        Assert.That(model.Feedback, Has.Count.EqualTo(1));
        Assert.That(outcome.Errors.Single(), Is.EqualTo("can't be expressed"));
    }

    [Test]
    public void TheRoundsSetting_IsRegistered_AndClamped()
    {
        var definition = SettingsMetadata.Find(GenerationRepairPolicy.RoundsKey);
        Assert.Multiple(() =>
        {
            Assert.That(definition, Is.Not.Null);
            Assert.That(GenerationRepairPolicy.Default.MaxRounds, Is.EqualTo(2));
            Assert.That(GenerationRepairPolicy.Clamp(-1), Is.Zero);
            Assert.That(GenerationRepairPolicy.Clamp(99), Is.EqualTo(GenerationRepairPolicy.MaxAllowedRounds));
        });
    }
}
