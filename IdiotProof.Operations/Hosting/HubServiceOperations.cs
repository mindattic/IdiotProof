using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Hosting;

/// <summary>One command line the service installer runs (sc.exe / reg.exe).</summary>
public sealed record ServiceCommand(string FileName, IReadOnlyList<string> Arguments, string Purpose, bool HasSecret = false)
{
    /// <summary>The line as a person would type it, with any password masked.</summary>
    public string Display(string? secret = null)
    {
        var line = FileName + " " + string.Join(' ', Arguments.Select(Quote));
        return secret is { Length: > 0 } ? line.Replace(Quote(secret), "********") : line;
    }

    private static string Quote(string a) => a.Length == 0 || a.Any(c => c is ' ' or '"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a;
}

/// <summary>Runs service commands; a seam so tests never touch the service control manager.</summary>
public interface IServiceCommandRunner
{
    /// <summary>True when this process may change services (an elevated administrator).</summary>
    bool IsElevated { get; }

    /// <summary>Runs one command and returns its exit code and output.</summary>
    Task<(int ExitCode, string Output)> RunAsync(ServiceCommand command, CancellationToken ct);
}

/// <summary>The real runner: starts sc.exe / reg.exe and waits.</summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessServiceCommandRunner : IServiceCommandRunner
{
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public async Task<(int ExitCode, string Output)> RunAsync(ServiceCommand command, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(command.FileName) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in command.Arguments) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"{command.FileName} did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, ((await stdout) + (await stderr)).Trim());
    }
}

/// <summary>
/// The sc.exe / reg.exe lines that install the Hub as a Windows service running as the USER
/// (IP-T76/IP-T84): LocalDB and the Data Protection ring are per-user, so LocalSystem would see
/// an empty database and could not decrypt anyone's broker keys.
/// </summary>
public static class HubServiceCommands
{
    /// <summary>Where the service keeps its environment (REG_MULTI_SZ "Environment").</summary>
    public static string RegistryKey => $@"HKLM\SYSTEM\CurrentControlSet\Services\{HubHosting.ServiceName}";

    /// <summary>The account to run as: <c>DOMAIN\user</c>, or <c>.\user</c> for a local account.</summary>
    public static string DefaultAccount() =>
        $"{(string.IsNullOrEmpty(Environment.UserDomainName) || Environment.UserDomainName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ? "." : Environment.UserDomainName)}\\{Environment.UserName}";

    public static IReadOnlyList<ServiceCommand> Install(string exe, string account, string password, bool monitor, string startType, string windowLog) =>
    [
        new("sc.exe", ["create", HubHosting.ServiceName, "binPath=", $"\"{exe}\"", "start=", startType, "obj=", account, "password=", password, "DisplayName=", "IdiotProof Hub"],
            $"register the service to run as {account}", HasSecret: true),
        new("sc.exe", ["description", HubHosting.ServiceName, "IdiotProof Hub: serves every idiotproof CLI/MCP operation on 127.0.0.1" + (monitor ? " and hosts the trading Monitor." : " (Monitor off).")],
            "describe it"),
        new("sc.exe", ["failure", HubHosting.ServiceName, "reset=", "86400", "actions=", "restart/60000/restart/60000//0"],
            "restart it after a crash (twice a day at most)"),
        new("reg.exe", ["add", RegistryKey, "/v", "Environment", "/t", "REG_MULTI_SZ", "/d",
                $"IDIOTPROOF_HUB_MONITOR={(monitor ? "1" : "0")}\\0{HubServiceEnvironment.WindowLog}={windowLog}", "/f"],
            monitor ? "host the Monitor (the service TRADES)" : "keep the Monitor off (IDIOTPROOF_HUB_MONITOR=0)"),
    ];

    public static IReadOnlyList<ServiceCommand> Uninstall() =>
    [
        new("sc.exe", ["stop", HubHosting.ServiceName], "stop it (graceful: drain, stop the Monitor, release the lease)"),
        new("sc.exe", ["delete", HubHosting.ServiceName], "remove it"),
    ];
}

/// <summary>Environment names the service is given (the Hub's HubConsole reads the log one).</summary>
public static class HubServiceEnvironment
{
    /// <summary>The Hub's window-mirror log (HubConsole.LogEnvVar in the Hub).</summary>
    public const string WindowLog = "IDIOTPROOF_HUB_LOG";
}

public sealed record HubInstallServiceArgs
{
    [Option("exe", Description = "The Hub exe the service runs (default: the published C:\\Apps\\IdiotProof\\Hub\\idiotproof-hub.exe).")]
    public string? Exe { get; init; }

    [Option("account", Description = "Windows account to run as (default: you, as DOMAIN\\user or .\\user). Must be the user who owns the LocalDB database and keys.")]
    public string? Account { get; init; }

    [Option("password", Description = "That account's Windows password (required unless --dry-run; never logged).")]
    public string? Password { get; init; }

    [Option("monitor", Description = "Host the trading Monitor in the service (it places real orders unattended). Off by default.")]
    public bool Monitor { get; init; }

    [Option("start", Description = "Start type: delayed-auto (default), auto or demand.")]
    public string Start { get; init; } = "delayed-auto";

    [Option("dry-run", Description = "Show the commands without running them.")]
    public bool DryRun { get; init; }
}

[Operation("hub install-service", Summary = "Install the Hub as a Windows service that runs as you (Monitor off unless --monitor).", Risk = OperationRisk.Destructive,
    Description = "Runs sc.exe create/description/failure and sets the service's environment in the registry. Needs an elevated Hub; otherwise it prints the commands and points at tools\\install-hub-service.ps1, which elevates itself. Grant the account 'Log on as a service' (the script does). Stop any interactive Hub first: both use port 5910.")]
[Example("idiotproof hub install-service --dry-run")]
[Example("idiotproof hub install-service --password \"...\" --monitor")]
public sealed class HubInstallServiceOperation : IOperation<HubInstallServiceArgs>
{
    /// <summary>The published Hub the service runs by default.</summary>
    public const string DefaultExe = @"C:\Apps\IdiotProof\Hub\idiotproof-hub.exe";

    public async Task<OperationResult> ExecuteAsync(HubInstallServiceArgs args, OperationContext ctx)
    {
        if (!OperatingSystem.IsWindows()) return OperationResult.Blocked("Windows services exist only on Windows.");
        if (args.Start is not ("delayed-auto" or "auto" or "demand")) return OperationResult.Usage("--start must be delayed-auto, auto or demand.");

        var exe = args.Exe ?? DefaultExe;
        var account = args.Account ?? HubServiceCommands.DefaultAccount();
        var windowLog = HubHosting.WindowLogPath(true, null, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!;
        if (!args.DryRun && string.IsNullOrEmpty(args.Password))
            return OperationResult.Usage($"--password is required: the service runs as {account}, and Windows needs that account's password to start it. Use --dry-run to see the commands.");
        if (!args.DryRun && !File.Exists(exe))
            return OperationResult.Blocked($"{exe} does not exist - publish first (run.ps1) or pass --exe.");

        var password = args.Password ?? "<password>";
        var commands = HubServiceCommands.Install(exe, account, password, args.Monitor, args.Start == "delayed-auto" ? "delayed-auto" : args.Start, windowLog);
        return await RunAsync(ctx, commands, args.DryRun, args.Password,
            $"Service {HubHosting.ServiceName} installed to run {exe} as {account}" + (args.Monitor ? " WITH the Monitor (it trades)." : " with the Monitor off.") +
            $"\nStart it with `sc.exe start {HubHosting.ServiceName}` once any interactive Hub is stopped (`idiotproof hub stop`). Its window log: {windowLog}" +
            "\nIf it fails to start with error 1069, grant the account 'Log on as a service' (tools\\install-hub-service.ps1 does).");
    }

    internal static async Task<OperationResult> RunAsync(OperationContext ctx, IReadOnlyList<ServiceCommand> commands, bool dryRun, string? secret, string doneText)
    {
        var plan = string.Join("\n", commands.Select(c => $"  {c.Display(secret)}\n      # {c.Purpose}"));
        var data = new { Service = HubHosting.ServiceName, DryRun = dryRun, Commands = commands.Select(c => new { Command = c.Display(secret), c.Purpose }).ToList() };
        if (dryRun) return OperationResult.Ok(data, $"Would run (elevated):\n{plan}");

        var runner = ctx.Services.GetService<IServiceCommandRunner>()
                     ?? (OperatingSystem.IsWindows() ? new ProcessServiceCommandRunner() : null);
        if (runner is null) return OperationResult.Blocked("No service runner on this platform.", data);
        if (!runner.IsElevated)
            return OperationResult.Blocked(
                $"The Hub is not running as an administrator, so it can't change services. Run tools\\install-hub-service.ps1 (it elevates itself), or these from an elevated prompt:\n{plan}", data);

        var log = new StringBuilder();
        foreach (var c in commands)
        {
            var (code, output) = await runner.RunAsync(c, ctx.CancellationToken);
            var shown = secret is { Length: > 0 } ? output.Replace(secret, "********") : output;
            log.Append($"  {c.Display(secret)}  -> exit {code}{(shown.Length > 0 ? ": " + shown.ReplaceLineEndings(" ") : "")}\n");
            // "stop" on a stopped service (1062) is fine during uninstall; anything else non-zero stops the run.
            if (code != 0 && !(c.Arguments.Count > 0 && c.Arguments[0] == "stop" && code is 1062 or 1060))
                return OperationResult.Fail($"{c.Purpose} failed (exit {code}).\n{log}", data);
        }
        return OperationResult.Ok(data, doneText + "\n" + log.ToString().TrimEnd());
    }
}

public sealed record HubUninstallServiceArgs
{
    [Option("dry-run", Description = "Show the commands without running them.")]
    public bool DryRun { get; init; }
}

[Operation("hub uninstall-service", Summary = "Stop and remove the Hub's Windows service.", Risk = OperationRisk.Destructive,
    Description = "sc.exe stop (graceful: the Hub drains operations, stops the Monitor and releases the leader lease), then sc.exe delete. Needs an elevated Hub; otherwise prints the commands. Run it from an interactive Hub or the script, not the service's own Hub.")]
[Example("idiotproof hub uninstall-service --dry-run")]
[Example("idiotproof hub uninstall-service")]
public sealed class HubUninstallServiceOperation : IOperation<HubUninstallServiceArgs>
{
    public Task<OperationResult> ExecuteAsync(HubUninstallServiceArgs args, OperationContext ctx) =>
        !OperatingSystem.IsWindows()
            ? Task.FromResult(OperationResult.Blocked("Windows services exist only on Windows."))
            : HubInstallServiceOperation.RunAsync(ctx, HubServiceCommands.Uninstall(), args.DryRun, null,
                $"Service {HubHosting.ServiceName} removed. `idiotproof hub start` runs the Hub interactively again.");
}
