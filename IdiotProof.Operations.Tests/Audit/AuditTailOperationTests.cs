using IdiotProof.Operations.Audit;
using IdiotProof.Operations.Tests.Diary;

namespace IdiotProof.Operations.Tests.Audit;

/// <summary>audit tail (IP-T244): the latest rows oldest-first; --follow is bounded by --seconds and --max.</summary>
public class AuditTailOperationTests
{
    [Test]
    public async Task Tail_ShowsTheLatestRows_OldestFirst()
    {
        var kit = new DiaryKit();
        for (var i = 1; i <= 5; i++)
            await kit.AddAsync(AuditOperationsTests.Row(kit.Owner, "signal-fire", $"event {i}", DateTime.UtcNow.AddMinutes(i)));

        var result = await new AuditTailOperation().ExecuteAsync(new AuditTailArgs { Lines = 3 }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var human = result.Human!;
        Assert.That(human, Does.Not.Contain("event 2"));
        Assert.That(human.IndexOf("event 3"), Is.LessThan(human.IndexOf("event 5")));
    }

    [Test]
    public async Task Follow_PicksUpNewRows_AndStopsAtMax()
    {
        var kit = new DiaryKit();
        await kit.AddAsync(AuditOperationsTests.Row(kit.Owner, "system", "before", DateTime.UtcNow));

        var tail = new AuditTailOperation().ExecuteAsync(new AuditTailArgs { Follow = true, Seconds = 10, Max = 2, Lines = 1 }, kit.Context());
        await Task.Delay(300);
        await kit.AddAsync(AuditOperationsTests.Row(Guid.NewGuid(), "entry", "someone else's", DateTime.UtcNow));
        await kit.AddAsync(AuditOperationsTests.Row(kit.Owner, "entry", "new one", DateTime.UtcNow));
        await kit.AddAsync(AuditOperationsTests.Row(kit.Owner, "exit", "new two", DateTime.UtcNow));
        var result = await tail;

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("before").And.Contain("new one").And.Contain("new two")
            .And.Not.Contain("someone else's").And.Contain("2 new row(s) (stopped at --max 2)"));
    }

    [Test]
    public async Task Follow_EndsAfterSeconds_WhenNothingArrives()
    {
        var kit = new DiaryKit();
        var started = DateTime.UtcNow;

        var result = await new AuditTailOperation().ExecuteAsync(new AuditTailArgs { Follow = true, Seconds = 1 }, kit.Context());

        Assert.That(result.Human, Does.Contain("No new rows in 1s."));
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(5)));
    }
}
