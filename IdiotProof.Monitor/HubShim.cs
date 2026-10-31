using System.Diagnostics;

using IdiotProof.Core.Monitoring;
namespace IdiotProof.Monitor;

/// <summary>How the shim starts the Hub: a command, its arguments and where to run it.</summary>
public sealed record HubShimLaunch(string FileName, string Arguments, string WorkingDirectory, string Description);

/// <summary>
/// <c>idiotproof-monitor.exe</c> with no subcommand is now a backward-compatible shim (IP-T81): the
/// Monitor lives in the Hub, so the old entry point (start.bat, desktop shortcuts, muscle memory)
/// starts the Hub instead, in this same console, and exits with the Hub's exit code. The Hub
/// hosts the Monitor unless <c>IDIOTPROOF_HUB_MONITOR=0</c>, which the child inherits like every
/// other <c>IDIOTPROOF_*</c> knob.
/// </summary>
public static class HubShim
{
    /// <summary>Overrides where the Hub exe is (absolute path).</summary>
    public const string HubExeEnvVar = "IDIOTPROOF_HUB_EXE";

    public const string HubExeName = "idiotproof-hub.exe";

    /// <summary>
    /// Where the Hub is, in order: <see cref="HubExeEnvVar"/>; the deploy layout
    /// (<c>C:\Apps\IdiotProof\Monitor\</c> next to <c>..\Hub\</c>); the same folder; the repo
    /// checkout this exe was built in (<c>dotnet run --project IdiotProof.Hub</c>). Null when none.
    /// </summary>
    public static HubShimLaunch? Resolve(string baseDirectory, string? envExe, Func<string, bool> fileExists, Func<string, bool> dirExists)
    {
        if (!string.IsNullOrWhiteSpace(envExe) && fileExists(envExe))
            return Exe(envExe, $"{HubExeEnvVar}={envExe}");

        var sibling = Path.GetFullPath(Path.Combine(baseDirectory, "..", "Hub", HubExeName));
        if (fileExists(sibling)) return Exe(sibling, $"deployed Hub {sibling}");

        var sameFolder = Path.Combine(baseDirectory, HubExeName);
        if (fileExists(sameFolder)) return Exe(sameFolder, $"Hub {sameFolder}");

        for (var dir = new DirectoryInfo(baseDirectory); dir is not null; dir = dir.Parent)
        {
            var project = Path.Combine(dir.FullName, "IdiotProof.Hub");
            if (fileExists(Path.Combine(dir.FullName, "IdiotProof.slnx")) && dirExists(project))
                return new HubShimLaunch("dotnet", $"run --project \"{project}\" --no-launch-profile", project, $"dotnet run --project {project}");
        }
        return null;
    }

    private static HubShimLaunch Exe(string path, string description) =>
        new(path, "", Path.GetDirectoryName(path)!, description);

    /// <summary>Starts the Hub in this console and waits for it; Ctrl+C reaches the Hub, which shuts down gracefully.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        var launch = Resolve(AppContext.BaseDirectory, Environment.GetEnvironmentVariable(HubExeEnvVar), File.Exists, Directory.Exists);
        if (launch is null)
        {
            Console.Error.WriteLine($"The Monitor now runs inside the IdiotProof Hub, but no Hub was found: publish it (run.ps1 puts it in C:\\Apps\\IdiotProof\\Hub) or set {HubExeEnvVar}.");
            return 1;
        }

        Console.WriteLine($"  idiotproof-monitor is a shim now: the Monitor runs inside the Hub. Starting {launch.Description}...");
        Console.WriteLine($"  (IDIOTPROOF_HUB_MONITOR={Environment.GetEnvironmentVariable("IDIOTPROOF_HUB_MONITOR") ?? "(unset: the Hub hosts the Monitor)"})");
        Console.WriteLine();

        var psi = new ProcessStartInfo(launch.FileName) { UseShellExecute = false, WorkingDirectory = launch.WorkingDirectory };
        var arguments = launch.Arguments + (args.Length > 0 ? " " + string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)) : "");
        psi.Arguments = arguments.Trim();

        using var hub = Process.Start(psi);
        if (hub is null)
        {
            Console.Error.WriteLine($"Could not start {launch.Description}.");
            return 1;
        }

        // The Hub shares this console, so Ctrl+C reaches it directly and it stops gracefully
        // (drain, stop the Monitor, release the lease). The shim just outlives it.
        Console.CancelKeyPress += (_, e) => e.Cancel = true;
        await hub.WaitForExitAsync();
        return hub.ExitCode;
    }
}
