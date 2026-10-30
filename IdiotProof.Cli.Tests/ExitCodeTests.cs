using IdiotProof.Operations;

namespace IdiotProof.Cli.Tests;

/// <summary>IP-T61: the exit-code contract — fixed numbers, documented, passed straight through from the Hub.</summary>
public class ExitCodeTests
{
    [Test]
    public void Codes_AreTheContractNumbers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CliExitCodes.Ok, Is.EqualTo(0));
            Assert.That(CliExitCodes.Failed, Is.EqualTo(1));
            Assert.That(CliExitCodes.Usage, Is.EqualTo(2));
            Assert.That(CliExitCodes.Blocked, Is.EqualTo(3));
            Assert.That(CliExitCodes.ConfirmationRequired, Is.EqualTo(4));
        });
    }

    [Test]
    public void Codes_MatchOperationStatus()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CliExitCodes.From(OperationStatus.Ok), Is.EqualTo(CliExitCodes.Ok));
            Assert.That(CliExitCodes.From(OperationStatus.Failed), Is.EqualTo(CliExitCodes.Failed));
            Assert.That(CliExitCodes.From(OperationStatus.UsageError), Is.EqualTo(CliExitCodes.Usage));
            Assert.That(CliExitCodes.From(OperationStatus.Blocked), Is.EqualTo(CliExitCodes.Blocked));
            Assert.That(CliExitCodes.From(OperationStatus.ConfirmationRequired), Is.EqualTo(CliExitCodes.ConfirmationRequired));
        });
    }

    [Test]
    public void EveryCode_IsDocumented()
    {
        Assert.That(CliExitCodes.All.Select(x => x.Code), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
        Assert.That(CliExitCodes.Describe(4), Does.Contain("--confirm"));
        Assert.That(CliExitCodes.Describe(99), Does.Contain("unknown"));
    }

    [Test]
    public async Task Help_ListsTheExitCodes()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        await cli.RunAsync("help");
        foreach (var (code, meaning) in CliExitCodes.All)
            Assert.That(cli.Out.ToString(), Does.Contain($"{code}  {meaning}"));
    }

    [TestCase(OperationStatus.Ok)]
    [TestCase(OperationStatus.Failed)]
    [TestCase(OperationStatus.UsageError)]
    [TestCase(OperationStatus.Blocked)]
    [TestCase(OperationStatus.ConfirmationRequired)]
    public async Task HubStatus_PassesThroughAsTheExitCode(OperationStatus status)
    {
        using var cli = new CliHarness();
        cli.Hub.OnOp = _ => FakeHub.Respond(new OperationResult { Status = status, Message = "m" }, "hub ping", OutputMode.Human);
        Assert.That(await cli.RunAsync("hub", "ping"), Is.EqualTo((int)status));
    }

    [Test]
    public async Task HubDown_IsFailed()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("hub", "ping"), Is.EqualTo(CliExitCodes.Failed));
    }

    [Test]
    public async Task UnknownCommand_WithHubDown_IsUsage()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;
        Assert.That(await cli.RunAsync("hub", "pnig"), Is.EqualTo(CliExitCodes.Usage));
        Assert.That(cli.Err.ToString(), Does.Contain("Did you mean 'hub ping'"));
    }

    [Test]
    public async Task BadGlobalFlag_IsUsage_AndNothingIsSent()
    {
        using var cli = new CliHarness();
        Assert.That(await cli.RunAsync("hub", "ping", "--user", "not-a-guid"), Is.EqualTo(CliExitCodes.Usage));
        Assert.That(cli.Hub.Requests, Is.Empty);
    }
}
