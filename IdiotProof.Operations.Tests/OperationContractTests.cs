using System.Text.Json;
using IdiotProof.Core.Identity;
using IdiotProof.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests;

/// <summary>OperationResult (IP-T53), OperationContext (IP-T52) and the JSON conventions.</summary>
public class OperationContractTests
{
    [TestCase(OperationStatus.Ok, 0)]
    [TestCase(OperationStatus.Failed, 1)]
    [TestCase(OperationStatus.UsageError, 2)]
    [TestCase(OperationStatus.Blocked, 3)]
    [TestCase(OperationStatus.ConfirmationRequired, 4)]
    public void ExitCode_IsTheStatusValue(OperationStatus status, int exit) =>
        Assert.That(new OperationResult { Status = status }.ExitCode, Is.EqualTo(exit));

    [Test]
    public void Factories_SetStatusAndMessage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OperationResult.Ok(1, "one").IsOk, Is.True);
            Assert.That(OperationResult.Fail("x").Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(OperationResult.Usage("x").ExitCode, Is.EqualTo(2));
            Assert.That(OperationResult.Blocked("closed", new { A = 1 }).Data, Is.Not.Null);
            Assert.That(OperationResult.Blocked("closed").Message, Is.EqualTo("closed"));
        });
    }

    [Test]
    public void NeedsConfirmation_CarriesCodeAndEtDeadline()
    {
        var expires = new DateTime(2026, 10, 30, 13, 36, 0, DateTimeKind.Utc);
        var result = OperationResult.NeedsConfirmation("ABC123", "Sell 10 AAPL", expires);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(4));
            Assert.That(result.Confirmation!.Code, Is.EqualTo("ABC123"));
            Assert.That(result.Message, Does.Contain("--confirm ABC123").And.Contain("9:36 AM ET"));
            Assert.That(result.Render(OutputMode.Human), Does.Contain("Sell 10 AAPL"));
        });
    }

    [Test]
    public void Json_Envelope_IsSnakeCaseWithKebabStatusAndEtTimes()
    {
        var at = new DateTime(2026, 10, 30, 13, 31, 0, DateTimeKind.Utc);
        var json = OperationResult.Ok(new { FilledAt = at, OrderSide = TradeSide.ShortSale }).ToJson();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("status").GetString(), Is.EqualTo("ok"));
            Assert.That(root.GetProperty("exit_code").GetInt32(), Is.EqualTo(0));
            Assert.That(root.GetProperty("data").GetProperty("filled_at").GetString(), Is.EqualTo("2026-10-30T09:31:00-04:00"));
            Assert.That(root.GetProperty("data").GetProperty("order_side").GetString(), Is.EqualTo("short-sale"));
            Assert.That(root.TryGetProperty("message", out _), Is.False, "nulls are omitted");
        });
    }

    [Test]
    public void Json_WinterTimes_UseEstOffset()
    {
        var json = JsonSerializer.Serialize(new DateTime(2026, 12, 1, 14, 30, 0, DateTimeKind.Utc), OperationJson.Options);
        Assert.That(json, Is.EqualTo("\"2026-12-01T09:30:00-05:00\""));
    }

    [Test]
    public void Render_Human_FallsBackToMessageThenData()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OperationResult.Ok(null, "hello").Render(OutputMode.Human), Is.EqualTo("hello"));
            Assert.That(OperationResult.Fail("nope").Render(OutputMode.Human), Is.EqualTo("Error: nope"));
            Assert.That(OperationResult.Ok(new { A = 1 }).Render(OutputMode.Human), Does.Contain("\"a\": 1"));
            Assert.That(OperationResult.Ok(new { A = 1 }).Render(OutputMode.Json), Does.Contain("\"exit_code\": 0"));
        });
    }

    [Test]
    public void OperationException_MapsToResult()
    {
        var result = OperationException.Blocked("halted", new { Ticker = "AAPL" }).ToResult();
        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(result.Message, Is.EqualTo("halted"));
        Assert.That(result.Data, Is.Not.Null);
    }

    [Test]
    public void RequireOwner_ReturnsOwner_OrThrowsUsageWithResolverMessage()
    {
        using var sp = new ServiceCollection().BuildServiceProvider();
        var id = Guid.NewGuid();
        Assert.That(new OperationContext { Services = sp, OwnerId = id }.RequireOwner(), Is.EqualTo(id));

        var none = new OperationContext { Services = sp, OwnerError = OwnerResolver.NoUsersMessage };
        var ex = Assert.Throws<OperationException>(() => none.RequireOwner());
        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(ex.Message, Is.EqualTo(OwnerResolver.NoUsersMessage));

        var unexplained = Assert.Throws<OperationException>(() => new OperationContext { Services = sp }.RequireOwner());
        Assert.That(unexplained!.Message, Does.Contain("--user"));
    }

    [Test]
    public void Context_ClockAndEt()
    {
        using var sp = new ServiceCollection().BuildServiceProvider();
        var ctx = new OperationContext { Services = sp, Clock = new Fixtures.FixedClock(Fixtures.FixedClock.MarketOpen) };
        Assert.That(ctx.UtcNow, Is.EqualTo(new DateTime(2026, 10, 30, 13, 31, 0, DateTimeKind.Utc)));
        Assert.That(ctx.Et(ctx.UtcNow), Is.EqualTo("2026-10-30 9:31 AM ET"));
    }

    [Test]
    public void EasternClock_UnspecifiedKindIsUtc_AndEtDateCanDifferFromUtcDate()
    {
        var lateUtc = new DateTime(2026, 10, 31, 1, 15, 0, DateTimeKind.Unspecified); // 9:15 PM ET on the 30th
        Assert.That(EasternClock.Format(lateUtc), Is.EqualTo("2026-10-30 9:15 PM ET"));
        Assert.That(EasternClock.FormatDate(lateUtc), Is.EqualTo("2026-10-30"));
    }
}
