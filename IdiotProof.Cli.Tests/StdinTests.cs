namespace IdiotProof.Cli.Tests;

/// <summary>IP-T58: an argument value of exactly "-" is replaced by stdin, read only when asked for.</summary>
public class StdinTests
{
    [Test]
    public void Apply_ReplacesTheMarker_AndTrimsOneNewline()
    {
        var (tokens, error) = StdinArguments.Apply(["strategy", "note", "12", "--text", "-"], () => "line one\nline two\r\n");
        Assert.That(error, Is.Null);
        Assert.That(tokens, Is.EqualTo(new[] { "strategy", "note", "12", "--text", "line one\nline two" }));
    }

    [Test]
    public void Apply_DropsALeadingBom()
    {
        var (tokens, _) = StdinArguments.Apply(["x", "-"], () => "\uFEFFGap and go\r\n");
        Assert.That(tokens, Is.EqualTo(new[] { "x", "Gap and go" }));
    }

    [Test]
    public void Apply_InlineForm()
    {
        var (tokens, _) = StdinArguments.Apply(["x", "y", "--text=-"], () => "hello\n");
        Assert.That(tokens, Is.EqualTo(new[] { "x", "y", "--text=hello" }));
    }

    [Test]
    public void Apply_TwoMarkers_IsAnError()
    {
        var (_, error) = StdinArguments.Apply(["x", "-", "-"], () => "a");
        Assert.That(error, Does.Contain("Only one"));
    }

    [Test]
    public void Apply_NoMarker_NeverReadsStdin()
    {
        var reads = 0;
        var (tokens, _) = StdinArguments.Apply(["x", "y", "--n", "-5", "--"], () => { reads++; return ""; });
        Assert.That(reads, Is.Zero);
        Assert.That(tokens, Is.EqualTo(new[] { "x", "y", "--n", "-5", "--" }));
    }

    [Test]
    public async Task Cli_SendsStdinText_InPlaceOfTheDash()
    {
        using var cli = new CliHarness(stdin: "Gap and go\n");
        await cli.RunAsync("strategy", "sample", "-", "--owner-name", "ryan");
        Assert.That(cli.Hub.Requests.Single().Argv, Is.EqualTo(new[] { "strategy", "sample", "Gap and go", "--owner-name", "ryan" }));
        Assert.That(cli.StdinReads, Is.EqualTo(1));
    }

    [Test]
    public async Task Cli_WithoutDash_DoesNotTouchStdin()
    {
        using var cli = new CliHarness(stdin: "ignored");
        await cli.RunAsync("hub", "ping");
        Assert.That(cli.StdinReads, Is.Zero);
    }

    [Test]
    public async Task Cli_TwoDashes_IsUsage_AndNothingIsSent()
    {
        using var cli = new CliHarness(stdin: "x");
        Assert.That(await cli.RunAsync("strategy", "sample", "-", "--note", "-"), Is.EqualTo(CliExitCodes.Usage));
        Assert.That(cli.Hub.Requests, Is.Empty);
    }
}
