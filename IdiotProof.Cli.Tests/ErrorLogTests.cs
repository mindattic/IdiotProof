namespace IdiotProof.Cli.Tests;

/// <summary>
/// The CLI logs errors only (IP-T23): CLI-side failures reach its MindAttic.Log, named by the
/// command's noun and verb and never by its arguments; successes and usage errors log nothing.
/// </summary>
[TestFixture]
public sealed class ErrorLogTests
{
    [Test]
    public async Task HubDown_LogsTheFailureByCommandName()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;

        await cli.RunAsync("hub", "ping");

        Assert.That(cli.Logged, Has.Count.EqualTo(1));
        Assert.That(cli.Logged[0].Message, Does.StartWith("idiotproof hub ping: hub-not-running:"));
    }

    [Test]
    public async Task Success_LogsNothing()
    {
        using var cli = new CliHarness();

        await cli.RunAsync("hub", "ping");

        Assert.That(cli.Logged, Is.Empty);
    }

    [Test]
    public async Task UsageError_LogsNothing()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;

        await cli.RunAsync("hub", "pnig");

        Assert.That(cli.Logged, Is.Empty);
    }

    [Test]
    public async Task ArgumentValues_NeverReachTheLog()
    {
        using var cli = new CliHarness();
        cli.Hub.Up = false;

        await cli.RunAsync("hub", "ping", "sk-ant-SECRETVALUE", "--key", "PKSECRET123");

        Assert.That(cli.Logged, Is.Not.Empty);
        Assert.That(cli.Logged.Select(l => l.Message), Has.None.Contains("SECRET"));
    }

    [TestCase(new[] { "hub", "ping", "--json" }, "hub ping")]
    [TestCase(new[] { "users", "create", "someone", "hunter2" }, "users create")]
    [TestCase(new[] { "hub", "status" }, "hub status")]
    [TestCase(new[] { "sk-ant-abc", "list" }, "(unknown command)")]
    [TestCase(new[] { "--json" }, "(unknown command)")]
    [TestCase(new[] { "users", "sk-ant-abc" }, "users")]
    public void CommandName_KeepsOnlyCommandWords(string[] rest, string expected) =>
        Assert.That(CliApp.CommandName(rest), Is.EqualTo(expected));
}
