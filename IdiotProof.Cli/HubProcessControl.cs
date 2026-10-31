using System.Diagnostics;

namespace IdiotProof.Cli;

/// <summary>How to start a Hub: the command, where to run it, and a line describing it.</summary>
public sealed record HubLaunch(string FileName, string Arguments, string WorkingDirectory, string Description)
{
    /// <summary>Extra environment for the Hub (e.g. IDIOTPROOF_HUB_PORT matching the URL the CLI will poll).</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}

/// <summary>A Hub process this CLI started.</summary>
public sealed record StartedHub(int LauncherProcessId, Func<bool> HasExited);

/// <summary>Process plumbing behind <c>hub start|stop</c> (IP-T83); a seam so tests never spawn anything.</summary>
public interface IHubProcessControl
{
    /// <summary>Starts the Hub in its own visible window; it mirrors that window to <paramref name="logFile"/>.</summary>
    StartedHub Start(HubLaunch launch, string logFile);

    /// <summary>Ids of running <c>idiotproof-hub</c> processes.</summary>
    IReadOnlyList<int> FindHubProcessIds();

    /// <summary>Stops one process: close request first, then end it after <paramref name="grace"/>. Returns what happened.</summary>
    string Stop(int processId, TimeSpan grace);
}

/// <summary>The real process control, plus the launch resolution shared with tests.</summary>
public sealed class HubProcessControl : IHubProcessControl
{
    /// <summary>The Hub's process name (its AssemblyName).</summary>
    public const string ProcessName = "idiotproof-hub";

    /// <summary>
    /// The published exe when present, else <c>dotnet run</c> on the repo's IdiotProof.Hub (found by
    /// walking up from the CLI's folder to IdiotProof.slnx), else null.
    /// </summary>
    public static HubLaunch? ResolveLaunch(CliPaths paths, string baseDirectory)
    {
        if (File.Exists(paths.PublishedHubExe))
            return new HubLaunch(paths.PublishedHubExe, "", Path.GetDirectoryName(paths.PublishedHubExe)!,
                $"published Hub {paths.PublishedHubExe}");

        if (FindRepoRoot(baseDirectory) is { } root)
        {
            var project = Path.Combine(root, "IdiotProof.Hub");
            // Working directory = the project, so the Hub finds its appsettings like `cd IdiotProof.Hub; dotnet run`.
            return new HubLaunch("dotnet", $"run --project \"{project}\" --no-launch-profile", project,
                $"dotnet run --project {project}");
        }
        return null;
    }

    /// <summary>The nearest folder at or above <paramref name="start"/> holding IdiotProof.slnx (and IdiotProof.Hub), or null.</summary>
    public static string? FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IdiotProof.slnx")) && Directory.Exists(Path.Combine(dir.FullName, "IdiotProof.Hub")))
                return dir.FullName;
        }
        return null;
    }

    public StartedHub Start(HubLaunch launch, string logFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        // The Hub opens in its OWN visible, money-green console window (IP-T331, like Prose.Hub) and
        // mirrors that window to logFile itself (IDIOTPROOF_HUB_LOG). A hidden cmd.exe sets the
        // environment and `start`s it, then exits: ShellExecute, not CreateProcess with inherited
        // handles, so a piped caller (`idiotproof hub start | tail`, an agent's shell) still sees
        // end-of-output at once (IP-T296), and Ctrl+C here never reaches the Hub.
        var environment = new Dictionary<string, string>(launch.Environment) { ["IDIOTPROOF_HUB_LOG"] = logFile };
        var sets = string.Concat(environment.Select(kv => $"set \"{kv.Key}={kv.Value}\"&& "));
        var command = $"\"{sets}start \"IdiotProof Hub\" /D \"{launch.WorkingDirectory}\" \"{launch.FileName}\" {launch.Arguments}\"";
        var psi = new ProcessStartInfo("cmd.exe", $"/d /s /c {command}")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = launch.WorkingDirectory,
        };
        var process = Process.Start(psi) ?? throw new InvalidOperationException("cmd.exe did not start.");
        // The launcher exits as soon as the window is up, so "exited" can't mean "the Hub died";
        // a Hub that dies at startup says so in its log.
        return new StartedHub(process.Id, () => HubDiedAtStartup(logFile));
    }

    /// <summary>True when the Hub's log shows it failed to start (crash or port taken).</summary>
    internal static bool HubDiedAtStartup(string logFile)
    {
        try
        {
            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var text = new StreamReader(stream).ReadToEnd();
            return text.Contains("Unhandled exception", StringComparison.Ordinal) || text.Contains("is already in use", StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public IReadOnlyList<int> FindHubProcessIds()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        try { return [.. processes.Select(p => p.Id)]; }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    public string Stop(int processId, TimeSpan grace)
    {
        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return $"process {processId} is not running"; }

        using (process)
        {
            if (!string.Equals(process.ProcessName, ProcessName, StringComparison.OrdinalIgnoreCase))
                return $"process {processId} is {process.ProcessName}, not {ProcessName}; left alone";

            // Polite first: a Hub in a visible console window closes gracefully on WM_CLOSE.
            if (process.CloseMainWindow() && process.WaitForExit(grace))
                return $"process {processId} closed on request";

            try
            {
                process.Kill(entireProcessTree: false);
                return process.WaitForExit(TimeSpan.FromSeconds(10))
                    ? $"process {processId} had no window to accept a close request; ended it"
                    : $"process {processId} did not exit after being ended — check Task Manager";
            }
            catch (InvalidOperationException)
            {
                return $"process {processId} exited";
            }
        }
    }
}
