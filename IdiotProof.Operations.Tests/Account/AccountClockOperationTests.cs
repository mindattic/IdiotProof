using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;

namespace IdiotProof.Operations.Tests.Account;

/// <summary>account clock and account calendar (IP-T103): Alpaca's clock and calendar, shown in ET.</summary>
public class AccountClockOperationTests
{
    [Test]
    public async Task Clock_Open_ShowsTheCloseInEt()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/clock", """
            {"timestamp":"2026-10-30T09:31:00.5-04:00","is_open":true,
             "next_open":"2026-11-02T09:30:00-05:00","next_close":"2026-10-30T16:00:00-04:00"}
            """);

        var result = await new AccountClockOperation().ExecuteAsync(new AccountClockArgs(), AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.StartWith("Market OPEN · closes 2026-10-30 4:00 PM ET (in 6h 28m)"));
        Assert.That(result.Human, Does.Contain("2026-11-02 9:30 AM ET"));
    }

    [Test]
    public void Clock_SandboxIsRefused() =>
        Assert.ThrowsAsync<AlpacaAccessException>(() =>
            new AccountClockOperation().ExecuteAsync(new AccountClockArgs { Mode = BrokerMode.Sandbox }, AccountTestKit.Context(new ScriptedAlpacaFactory())));

    [Test]
    public async Task Calendar_ListsTradingDays_EarlyCloses_AndWeekdayHolidays()
    {
        var f = new ScriptedAlpacaFactory();
        f.Paper.On("GET /v2/calendar", """
            [{"date":"2026-11-25","open":"09:30","close":"16:00","session_open":"0400","session_close":"2000"},
             {"date":"2026-11-27","open":"09:30","close":"13:00","session_open":"0400","session_close":"1700"}]
            """);

        var result = await new AccountCalendarOperation().ExecuteAsync(
            new AccountCalendarArgs { From = new DateOnly(2026, 11, 25), To = new DateOnly(2026, 11, 29) }, AccountTestKit.Context(f));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(f.Paper.Requests.Single().Uri.Query, Is.EqualTo("?start=2026-11-25&end=2026-11-29"));
        var lines = result.Human!.Split('\n');
        Assert.That(lines[0], Does.Contain("2 trading days"));
        Assert.That(lines.Single(l => l.StartsWith("2026-11-25")), Does.Contain("4:00 AM").And.Contain("9:30 AM").And.Contain("8:00 PM"));
        Assert.That(lines.Single(l => l.StartsWith("2026-11-26")), Does.Contain("closed (holiday)"));
        Assert.That(lines.Single(l => l.StartsWith("2026-11-27")), Does.Contain("1:00 PM").And.Contain("early close"));
        Assert.That(lines.Any(l => l.StartsWith("2026-11-28")), Is.False, "weekends are not listed");
    }

    [Test]
    public async Task Calendar_BackwardsRange_IsUsage()
    {
        var result = await new AccountCalendarOperation().ExecuteAsync(
            new AccountCalendarArgs { From = new DateOnly(2026, 11, 25), To = new DateOnly(2026, 11, 1) }, AccountTestKit.Context(new ScriptedAlpacaFactory()));
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }
}
