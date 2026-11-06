using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Tests;

/// <summary>
/// Sequenced strategies on the Strategy row (IP-T445): the step position and transition wait live
/// in SQL, exits with more steps to come park the row in the wait, the Monitor advances and resets
/// it, and editor saves never touch any of it. Integration tests against SQL Server LocalDB, same
/// harness as StrategyRepositoryCycleTests.
/// </summary>
[TestFixture]
public sealed class StrategyRepositorySequenceTests
{
    private static readonly string DbName = $"IdiotProof_Test_{Guid.NewGuid():N}";
    private static readonly string ConnStr =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";

    private IDbContextFactory<AppDbContext> factory = null!;
    private StrategyRepository repo = null!;
    private readonly Guid owner = Guid.NewGuid();

    private static readonly DateTime Morning = new(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc);

    private const string TwoStepScript =
        "Stock.Ticker(\"TEST\").Step(\"short\").IsBelowVwap().Short().StopLossPercent(3).TakeProfitPercent(6)" +
        ".ThenWhen(IsHigherLow()).Within(7).Step(\"buy-back\").IsAboveVwap().Long().StopLossPercent(2).TakeProfitPercent(5).Build()";

    private const string GapperStepScript =
        "Stock.Ticker(\"TEST\").Step(\"ride\").IsAboveVwap().Long().StopLossPercent(5).PeakGiveback(25, \"09:35\")" +
        ".ThenWhen(IsBelowVwap()).Within(1).Step(\"fade\").IsBelowVwap().Short().StopLossPercent(5).TakeProfitPercent(8).Build()";

    private const string StepOneGapperScript =
        "Stock.Ticker(\"TEST\").Step(\"short\").IsBelowVwap().Short().StopLossPercent(3).TakeProfitPercent(6)" +
        ".ThenWhen(IsAboveVwap()).Step(\"ride\").IsAboveVwap().Long().StopLossPercent(5).PeakGiveback(25).Build()";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new SqlServerDbContextFactory(ConnStr);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("ALTER TABLE Strategies NOCHECK CONSTRAINT ALL;");
        repo = new StrategyRepository(factory);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        using var db = factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    private async Task<Strategy> CreateAsync(string script = TwoStepScript)
    {
        var def = ScriptParser.ParseScript(script)!;
        Assert.That(def.IsSequence, Is.True, "the fixture script must parse as a sequence");
        return await repo.CreateAsync(owner, title: "sequence test", symbol: "TEST", scriptText: script, scriptJson: StrategyJson.Serialize(def));
    }

    [Test]
    public async Task NewRow_StartsAtStepZero_NotAwaiting()
    {
        var s = await CreateAsync();
        var row = (await repo.GetByIdAsync(s.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(row.StepIndex, Is.EqualTo(0));
            Assert.That(row.AwaitingTransition, Is.False);
            Assert.That(row.StepArmedUtc, Is.Null);
        });
    }

    [Test]
    public async Task ExitFill_WithMoreSteps_ParksTheRowInTheTransitionWait()
    {
        var s = await CreateAsync();
        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning);

        await repo.RecordExitFillAsync(s.Id, 11m, "Target", Morning.AddMinutes(5), moreSteps: true);
        var waiting = (await repo.GetByIdAsync(s.Id))!;

        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning.AddMinutes(10));
        await repo.RecordExitFillAsync(s.Id, 11m, "Target", Morning.AddMinutes(15));
        var done = (await repo.GetByIdAsync(s.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(waiting.AwaitingTransition, Is.True);
            Assert.That(waiting.PositionQty, Is.EqualTo(0));
            Assert.That(waiting.LastExitedUtc, Is.EqualTo(Morning.AddMinutes(5)));
            Assert.That(done.AwaitingTransition, Is.False, "the last step's exit has no transition to wait for");
        });
    }

    [Test]
    public async Task AdvanceStep_ArmsTheNextStep_AndClearsTheWait()
    {
        var s = await CreateAsync();
        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning);
        await repo.RecordExitFillAsync(s.Id, 11m, "Target", Morning.AddMinutes(5), moreSteps: true);

        var armedUtc = Morning.AddMinutes(30);
        await repo.AdvanceStepAsync(s.Id, 1, armedUtc);
        var row = (await repo.GetByIdAsync(s.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(row.StepIndex, Is.EqualTo(1));
            Assert.That(row.AwaitingTransition, Is.False);
            Assert.That(row.StepArmedUtc, Is.EqualTo(armedUtc));
            Assert.That(row.EntryFilledUtc, Is.EqualTo(Morning), "the prior step's fill stays on the row for the one-shot guard to compare against");
        });
    }

    [Test]
    public async Task StepOneFill_IsNotANewCycle_AndItsPhantomDoesNotRefundOne()
    {
        var s = await CreateAsync();
        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning);
        await repo.RecordExitFillAsync(s.Id, 11m, "Target", Morning.AddMinutes(5), moreSteps: true);
        await repo.AdvanceStepAsync(s.Id, 1, Morning.AddMinutes(30));

        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning.AddMinutes(40), countsAsCycle: false);
        var afterStepOne = (await repo.GetByIdAsync(s.Id))!;
        await repo.ClearUnfilledEntryAsync(s.Id);
        var afterPhantom = (await repo.GetByIdAsync(s.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(afterStepOne.CyclesToday, Is.EqualTo(1), "only the step-0 fill counts toward Repeat(n)");
            Assert.That(afterStepOne.AwaitingTransition, Is.False);
            Assert.That(afterPhantom.CyclesToday, Is.EqualTo(1), "a step-1 phantom fill never used a cycle, so none is given back");
            Assert.That(afterPhantom.StepIndex, Is.EqualTo(1), "an unfilled step entry leaves the sequence where it stood");
            Assert.That(afterPhantom.PositionQty, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ResetSequence_RefusesWhileHolding_AndClearsWhenFlat()
    {
        var s = await CreateAsync();
        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning);
        await repo.RecordExitFillAsync(s.Id, 11m, "Target", Morning.AddMinutes(5), moreSteps: true);
        await repo.AdvanceStepAsync(s.Id, 1, Morning.AddMinutes(30));
        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning.AddMinutes(40), countsAsCycle: false);

        var whileHolding = await repo.ResetSequenceAsync(s.Id, owner);
        var stillHolding = (await repo.GetByIdAsync(s.Id))!;
        await repo.RecordExitFillAsync(s.Id, 9m, "StopLoss", Morning.AddMinutes(50));

        var wrongOwner = await repo.ResetSequenceAsync(s.Id, Guid.NewGuid());
        var flat = await repo.ResetSequenceAsync(s.Id, owner);
        var reset = (await repo.GetByIdAsync(s.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(whileHolding, Is.EqualTo(StrategyMutation.PositionOpen));
            Assert.That(stillHolding.StepIndex, Is.EqualTo(1), "a refused reset changes nothing");
            Assert.That(wrongOwner, Is.EqualTo(StrategyMutation.NotOwner));
            Assert.That(flat, Is.EqualTo(StrategyMutation.Ok));
            Assert.That(reset.StepIndex, Is.EqualTo(0));
            Assert.That(reset.AwaitingTransition, Is.False);
            Assert.That(reset.StepArmedUtc, Is.Null);
        });
    }

    [Test]
    public async Task Deactivating_IsAllowedWhileAwaitingATransition_AndReactivationResumes()
    {
        var s = await CreateAsync();
        Assert.That(await repo.SetActiveAsync(s.Id, true, owner), Is.EqualTo(StrategyMutation.Ok));
        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning);
        await repo.RecordExitFillAsync(s.Id, 11m, "Target", Morning.AddMinutes(5), moreSteps: true);

        var off = await repo.SetActiveAsync(s.Id, false, owner);
        var paused = (await repo.GetByIdAsync(s.Id))!;
        var on = await repo.SetActiveAsync(s.Id, true, owner);
        var resumed = (await repo.GetByIdAsync(s.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(off, Is.EqualTo(StrategyMutation.Ok), "flat between steps is still flat");
            Assert.That(paused.IsActive, Is.False);
            Assert.That(paused.AwaitingTransition, Is.True, "pausing keeps the wait on the row");
            Assert.That(on, Is.EqualTo(StrategyMutation.Ok));
            Assert.That(resumed.AwaitingTransition, Is.True, "re-activation resumes the wait");
            Assert.That(resumed.StepIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task EditorSave_LeavesTheSequenceColumnsAlone()
    {
        var s = await CreateAsync();
        await repo.RecordEntryFillAsync(s.Id, 1, 10m, Morning);
        await repo.RecordExitFillAsync(s.Id, 11m, "Target", Morning.AddMinutes(5), moreSteps: true);
        var armedUtc = Morning.AddMinutes(30);
        await repo.AdvanceStepAsync(s.Id, 1, armedUtc);

        // A detached editor snapshot taken before any of that happened.
        s.Title = "renamed in the editor";
        s.StepIndex = 0;
        s.AwaitingTransition = false;
        s.StepArmedUtc = null;
        Assert.That(await repo.UpdateAsync(s), Is.EqualTo(StrategyMutation.Ok));
        var row = (await repo.GetByIdAsync(s.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(row.Title, Is.EqualTo("renamed in the editor"));
            Assert.That(row.StepIndex, Is.EqualTo(1));
            Assert.That(row.StepArmedUtc, Is.EqualTo(armedUtc));
            Assert.That(row.AwaitingTransition, Is.False);
        });
    }

    [Test]
    public async Task IsGapper_SeesAGapperOnAnyStep_OfASequence()
    {
        var stepZero = await CreateAsync(GapperStepScript);
        var stepOne = await CreateAsync(StepOneGapperScript);
        var none = await CreateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(StrategyRepository.IsGapper(stepZero), Is.True);
            Assert.That(StrategyRepository.IsGapper(new Strategy { ScriptText = "", ScriptJson = stepOne.ScriptJson }), Is.True,
                "the root is written with a null giveback; the step-1 giveback still makes it a gapper");
            Assert.That(StrategyRepository.IsGapper(new Strategy { ScriptText = "", ScriptJson = none.ScriptJson }), Is.False);
        });
    }

    [Test]
    public async Task GapperRules_ApplyToASequence_WhoseLaterStepIsTheGapper()
    {
        var symbol = $"G{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var plain = await repo.CreateAsync(owner, "plain", symbol, "Stock.Ticker(\"X\").IsAboveVwap().Long().StopLoss(1).Build()");
        Assert.That(await repo.SetActiveAsync(plain.Id, true, owner), Is.EqualTo(StrategyMutation.Ok));
        var def = ScriptParser.ParseScript(StepOneGapperScript.Replace("\"TEST\"", $"\"{symbol}\""))!;
        var sequence = await repo.CreateAsync(owner, "sequence", symbol, "", scriptJson: StrategyJson.Serialize(def));

        Assert.That(await repo.SetActiveAsync(sequence.Id, true, owner), Is.EqualTo(StrategyMutation.SymbolAlreadyActive),
            "one active gapper per symbol applies when the gapper is a later step");
    }
}

file sealed class SqlServerDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
