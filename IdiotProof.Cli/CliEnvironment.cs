using IdiotProof.Operations;

namespace IdiotProof.Cli;

/// <summary>
/// Everything the CLI touches outside itself, so tests can run <see cref="CliApp"/> against
/// string writers, canned stdin, a temp folder and a fake Hub.
/// </summary>
public sealed class CliEnvironment
{
    public required TextWriter Out { get; init; }
    public required TextWriter Err { get; init; }

    /// <summary>Reads all of stdin (called only when an argument is <c>-</c>).</summary>
    public Func<string> ReadStdin { get; init; } = () => Console.In.ReadToEnd();

    public bool IsOutputRedirected { get; init; }
    public bool IsInputRedirected { get; init; }

    /// <summary>Terminal width when writing to a terminal; null when redirected or unknown.</summary>
    public int? Width { get; init; }

    public Func<string, string?> GetEnv { get; init; } = Environment.GetEnvironmentVariable;

    public CliPaths Paths { get; init; } = CliPaths.Default;

    /// <summary>The HTTP handler for Hub calls (tests pass a fake; null = a real socket handler).</summary>
    public HttpMessageHandler? HubHandler { get; init; }

    /// <summary>The Hub key source (default: <see cref="HubKey.ForClient"/>).</summary>
    public Func<string?> HubKeySource { get; init; } = HubKey.ForClient;

    public TimeSpan SlowNotice { get; init; } = HubClient.DefaultSlowNotice;

    /// <summary>Where the CLI runs from; <c>hub start</c> and bootstrap walk up from here to find the repo.</summary>
    public string BaseDirectory { get; init; } = AppContext.BaseDirectory;

    /// <summary>Starts, finds and stops Hub processes (tests substitute a fake).</summary>
    public IHubProcessControl HubProcesses { get; init; } = new HubProcessControl();

    /// <summary>The real console.</summary>
    public static CliEnvironment FromConsole()
    {
        int? width = null;
        if (!Console.IsOutputRedirected)
        {
            try { width = Console.WindowWidth is > 0 and var w ? w : null; }
            catch (IOException) { }
        }
        return new CliEnvironment
        {
            Out = Console.Out,
            Err = Console.Error,
            IsOutputRedirected = Console.IsOutputRedirected,
            IsInputRedirected = Console.IsInputRedirected,
            Width = width,
        };
    }
}
