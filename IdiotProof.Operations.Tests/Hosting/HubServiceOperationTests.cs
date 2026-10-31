using IdiotProof.Operations.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Hosting;

/// <summary>
/// hub install-service|uninstall-service (IP-T84) against a recording runner: nothing here touches
/// the real service control manager or registry.
/// </summary>
public class HubServiceOperationTests
{
    private sealed class Recorder(bool elevated, int failOn = -1) : IServiceCommandRunner
    {
        public List<ServiceCommand> Ran { get; } = [];
        public bool IsElevated => elevated;

        public Task<(int ExitCode, string Output)> RunAsync(ServiceCommand command, CancellationToken ct)
        {
            Ran.Add(command);
            return Task.FromResult(Ran.Count - 1 == failOn ? (5, "[SC] OpenSCManager FAILED 5: Access is denied.") : (0, "[SC] SUCCESS"));
        }
    }

    private static OperationContext Ctx(IServiceCommandRunner runner) => new()
    {
        Services = new ServiceCollection().AddSingleton(runner).BuildServiceProvider(),
    };

    private string exe = "";

    [SetUp]
    public void SetUp()
    {
        exe = Path.Combine(Path.GetTempPath(), $"idiotproof-hub-{Guid.NewGuid():N}.exe");
        File.WriteAllText(exe, "");
    }

    [TearDown]
    public void TearDown() => File.Delete(exe);

    [Test]
    public async Task DryRun_ShowsTheCommands_RunsNothing_MonitorOffByDefault()
    {
        var runner = new Recorder(elevated: true);

        var result = await new HubInstallServiceOperation().ExecuteAsync(new HubInstallServiceArgs { DryRun = true, Exe = exe, Account = @".\ryan" }, Ctx(runner));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(runner.Ran, Is.Empty);
        Assert.That(result.Human, Does.Contain("sc.exe create IdiotProof.Hub binPath=").And.Contain(@"obj= .\ryan").And.Contain("start= delayed-auto"));
        Assert.That(result.Human, Does.Contain("IDIOTPROOF_HUB_MONITOR=0"));
    }

    [Test]
    public async Task Install_RunsAsTheUser_WithTheMonitorOnlyWhenAsked_AndNeverEchoesThePassword()
    {
        var runner = new Recorder(elevated: true);

        var result = await new HubInstallServiceOperation().ExecuteAsync(
            new HubInstallServiceArgs { Exe = exe, Account = @"BOX\ryan", Password = "hunter2!", Monitor = true }, Ctx(runner));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(runner.Ran.Select(c => c.Arguments[0]), Is.EqualTo(new[] { "create", "description", "failure", "add" }));
        var create = runner.Ran[0].Arguments;
        Assert.That(create, Does.Contain(@"BOX\ryan").And.Contain("hunter2!"), "the real password reaches sc.exe");
        Assert.That(runner.Ran[3].Arguments, Has.Some.Contains("IDIOTPROOF_HUB_MONITOR=1"));
        Assert.That(result.Human, Does.Not.Contain("hunter2!"));
        Assert.That(result.Human, Does.Contain("WITH the Monitor"));
    }

    [Test]
    public async Task Install_WithoutAPassword_IsAUsageError()
    {
        var result = await new HubInstallServiceOperation().ExecuteAsync(new HubInstallServiceArgs { Exe = exe }, Ctx(new Recorder(true)));
        Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task NotElevated_IsBlocked_WithTheCommandsToRun()
    {
        var runner = new Recorder(elevated: false);

        var result = await new HubInstallServiceOperation().ExecuteAsync(new HubInstallServiceArgs { Exe = exe, Password = "pw-123" }, Ctx(runner));

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(runner.Ran, Is.Empty);
        Assert.That(result.Message, Does.Contain("install-hub-service.ps1").And.Contain("sc.exe create").And.Not.Contain("pw-123"));
    }

    [Test]
    public async Task Uninstall_StopsThenDeletes_AndStopsOnAFailure()
    {
        var ok = new Recorder(elevated: true);
        var result = await new HubUninstallServiceOperation().ExecuteAsync(new HubUninstallServiceArgs(), Ctx(ok));
        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(ok.Ran.Select(c => string.Join(' ', c.Arguments)), Is.EqualTo(new[] { "stop IdiotProof.Hub", "delete IdiotProof.Hub" }));

        var failing = new Recorder(elevated: true, failOn: 1);
        var failed = await new HubUninstallServiceOperation().ExecuteAsync(new HubUninstallServiceArgs(), Ctx(failing));
        Assert.That(failed.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(failed.Message, Does.Contain("remove it failed (exit 5)"));
    }

    [Test]
    public void HubServiceOperations_AreValidCatalogEntries()
    {
        Assert.That(OperationCatalog.Validate([typeof(HubInstallServiceOperation), typeof(HubUninstallServiceOperation)]), Is.Empty);
    }
}
