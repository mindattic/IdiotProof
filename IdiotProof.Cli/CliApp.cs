using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using IdiotProof.Operations;
using IdiotProof.Operations.Streaming;

namespace IdiotProof.Cli;

/// <summary>
/// The <c>idiotproof</c> program (IP-T57): peel off the global flags, answer the client-local
/// commands (help, version, <c>agent bootstrap</c>, <c>hub status|start|stop|logs|version</c>,
/// <c>users use|whoami</c>) itself, and send everything else to the Hub as argv. The Hub binds
/// the operation's own parameters, so a new operation needs no CLI change.
/// </summary>
public sealed class CliApp(CliEnvironment env)
{
    private const string Program = OperationDescriptor.ProgramName;

    /// <summary>How long <c>hub start</c> waits for the Hub to answer.</summary>
    public TimeSpan StartWait { get; init; } = TimeSpan.FromSeconds(60);

    public static string CliVersion { get; } =
        typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? typeof(CliApp).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private static readonly Lazy<OperationCatalog> ReflectedCatalog =
        new(() => OperationCatalog.FromAssemblies(typeof(OperationCatalog).Assembly));

    private static readonly CatalogView LocalView = CatalogView.Of(LocalCommands.All, "the CLI");

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var (tokens, forceColor) = StripColorFlag(args);
        var extracted = ArgvParser.ExtractGlobals(tokens);
        if (extracted.Error is { } error)
        {
            await env.Err.WriteLineAsync($"Usage error: {error} Run `{Program} help`.");
            return CliExitCodes.Usage;
        }
        var flags = extracted.Flags;
        var rest = extracted.Remaining;

        using var client = new HubClient(ResolveHubUrl(flags), env.HubHandler, env.HubKeySource) { SlowNotice = env.SlowNotice };

        if (rest.Count == 0) return await HelpAsync(client, [], flags, ct);
        if (rest[0] == "help") return await HelpAsync(client, rest.Skip(1).ToList(), flags, ct);
        if (rest is ["--version"]) return await VersionAsync(client, flags, ct);

        if (LocalView.TryResolve(rest, out var local, out var localArgs))
        {
            if (flags.Help) return await PrintAsync(local!.Help, CliExitCodes.Ok);
            return local!.Name switch
            {
                "version" => await VersionAsync(client, flags, ct),
                "agent bootstrap" => await BootstrapAsync(client, flags, ct),
                "agent export" => await AgentExportAsync(localArgs, flags),
                "hub status" => await HubStatusAsync(client, flags, ct),
                "hub start" => await HubStartAsync(client, ct),
                "hub stop" => await HubStopAsync(client, localArgs),
                "hub logs" => await HubLogsAsync(localArgs),
                "hub version" => await HubVersionAsync(client, ct),
                "users use" => await UsersUseAsync(localArgs),
                "users whoami" => await WhoAmIAsync(flags),
                _ => await HelpAsync(client, rest, flags, ct),
            };
        }

        if (flags.Help) return await HelpAsync(client, rest, flags, ct);

        // A bare noun (`idiotproof strategy`) lists its verbs rather than bouncing off the Hub.
        if (rest.Count == 1 && !rest[0].StartsWith('-'))
        {
            var view = await LoadViewAsync(client, ct);
            if (view.Find(rest[0]) is null && view.HasNoun(rest[0]))
            {
                await env.Err.WriteLineAsync($"'{rest[0]}' needs a verb.{Environment.NewLine}");
                await env.Err.WriteLineAsync(HelpRenderer.Noun(view, rest[0]));
                return CliExitCodes.Usage;
            }
        }

        return await ForwardAsync(client, rest, flags, forceColor, ct);
    }

    // ── forwarding to the Hub ──

    private async Task<int> ForwardAsync(HubClient client, IReadOnlyList<string> rest, GlobalFlags flags, bool forceColor, CancellationToken ct)
    {
        var usesStdin = StdinArguments.Wants(rest);
        if (usesStdin && !env.IsInputRedirected)
            await env.Err.WriteLineAsync("Reading the '-' argument from stdin (finish with Ctrl+Z then Enter)…");
        var (argv, stdinError) = StdinArguments.Apply(rest, env.ReadStdin);
        if (stdinError is not null) return await ClientErrorAsync(flags, CliExitCodes.Usage, "usage", stdinError);

        var (owner, _) = new OwnerStore(env.Paths.ConfigFile).Resolve(flags.User);
        var request = new HubOperationRequest
        {
            Argv = argv,
            Caller = OperationCaller.Cli,
            Output = flags.Json ? OutputMode.Json : OutputMode.Human,
            OwnerId = owner,
            ConfirmationCode = flags.Confirm,
            Color = !flags.Json && UseColor(flags, forceColor),
            Width = env.Width,
        };

        // A streaming operation (IP-T324) prints its lines as they happen; Ctrl+C stops it. --json
        // keeps the one-envelope contract, so it gets the bounded window from /api/op instead.
        if (!flags.Json && IsStreaming(argv))
            return await StreamAsync(client, request, flags, ct);

        var result = await client.RunAsync(request, env.Err, ct);
        if (result.Kind == HubCallKind.HubDown)
        {
            // An unknown command is a usage error whether or not the Hub is up.
            var view = await LoadViewAsync(client, ct);
            if (!view.TryResolve(rest, out _, out _))
            {
                var (text, _) = HelpRenderer.For(view, rest);
                return await ClientErrorAsync(flags, CliExitCodes.Usage, "usage", text);
            }
        }
        if (result.Kind != HubCallKind.Completed || result.Response is null)
            return await ClientErrorAsync(flags, CliExitCodes.Failed, ErrorCode(result.Kind), result.Error ?? "The Hub call failed.");

        var response = result.Response;
        var output = response.ExitCode == CliExitCodes.Usage && !flags.Json ? env.Err : env.Out;
        if (response.Text.Length > 0) await output.WriteLineAsync(response.Text.TrimEnd());

        if (response.ExitCode == CliExitCodes.ConfirmationRequired && !flags.Json && ConfirmationCode(response) is { } code)
        {
            await env.Out.WriteLineAsync();
            await env.Out.WriteLineAsync($"To execute, run: {ConfirmCommand(rest, flags, code)}");
            if (usesStdin) await env.Out.WriteLineAsync("  (pipe the same input again for the '-' argument)");
        }
        return response.ExitCode;
    }

    /// <summary>True when the operation argv names is marked <see cref="StreamingAttribute"/> in the catalog built into this CLI.</summary>
    internal static bool IsStreaming(IReadOnlyList<string> argv) =>
        ReflectedCatalog.Value.TryResolve(argv, out var descriptor, out _) && StreamingAttribute.IsOn(descriptor!.OperationType);

    private async Task<int> StreamAsync(HubClient client, HubOperationRequest request, GlobalFlags flags, CancellationToken ct)
    {
        var result = await client.StreamAsync(request, async line =>
        {
            await env.Out.WriteLineAsync(line.Text.TrimEnd());
            await env.Out.FlushAsync(CancellationToken.None);
        }, ct);

        if (result.Kind == HubCallKind.Cancelled)
        {
            await env.Err.WriteLineAsync("Stopped.");
            return CliExitCodes.Ok;
        }
        if (result.Kind != HubCallKind.Completed || result.Response is null)
            return await ClientErrorAsync(flags, CliExitCodes.Failed, ErrorCode(result.Kind), result.Error ?? "The Hub stream failed.");

        var response = result.Response;
        var output = response.ExitCode == CliExitCodes.Usage ? env.Err : env.Out;
        if (response.Text.Length > 0) await output.WriteLineAsync(response.Text.TrimEnd());
        return response.ExitCode;
    }

    /// <summary>The same command line with <c>--confirm CODE</c> appended (keeping --user/--hub so the code's owner binding matches).</summary>
    internal static string ConfirmCommand(IReadOnlyList<string> rest, GlobalFlags flags, string code)
    {
        var parts = new List<string> { Program };
        parts.AddRange(rest.Select(Quote));
        if (flags.User is { } user) parts.Add($"--user {user:D}");
        if (flags.Hub is { } hub) parts.Add($"--hub {Quote(hub)}");
        parts.Add($"--confirm {code}");
        return string.Join(' ', parts);
    }

    private static string? ConfirmationCode(HubOperationResponse response) =>
        response.Envelope is { ValueKind: JsonValueKind.Object } envelope
        && envelope.TryGetProperty("confirmation", out var confirmation)
        && confirmation.ValueKind == JsonValueKind.Object
        && confirmation.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;

    internal static string Quote(string token) =>
        token.Length > 0 && !token.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '&' or '|' or '<' or '>' or '^')
            ? token
            : "\"" + token.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string ErrorCode(HubCallKind kind) => kind switch
    {
        HubCallKind.HubDown => "hub-not-running",
        HubCallKind.NoKey => "hub-key-missing",
        HubCallKind.Unauthorized => "hub-key-rejected",
        HubCallKind.Cancelled => "cancelled",
        HubCallKind.TimedOut => "timed-out",
        _ => "hub-error",
    };

    /// <summary>A failure the CLI itself detected: stderr text, or (with --json) an envelope on stdout.</summary>
    private async Task<int> ClientErrorAsync(GlobalFlags flags, int exitCode, string error, string message)
    {
        if (flags.Json)
        {
            var status = exitCode == CliExitCodes.Usage ? OperationStatus.UsageError : OperationStatus.Failed;
            await env.Out.WriteLineAsync(JsonSerializer.Serialize(
                new { Status = status, ExitCode = exitCode, Error = error, Message = message }, OperationJson.Indented));
        }
        else
        {
            await env.Err.WriteLineAsync(message);
        }
        return exitCode;
    }

    // ── help ──

    private async Task<int> HelpAsync(HubClient client, IReadOnlyList<string> words, GlobalFlags flags, CancellationToken ct)
    {
        var view = await LoadViewAsync(client, ct);
        if (flags.Json && words.Count == 0)
        {
            await env.Out.WriteLineAsync(view.RawJson ?? ReflectedCatalog.Value.ToJson());
            return CliExitCodes.Ok;
        }
        var (text, isError) = HelpRenderer.For(view, words);
        await (isError ? env.Err : env.Out).WriteLineAsync(text);
        return isError ? CliExitCodes.Usage : CliExitCodes.Ok;
    }

    /// <summary>
    /// The catalog for help (IP-T60): the Hub's live <c>/api/catalog</c> (cached to disk) when it
    /// answers, else the cached copy, else the operations built into this CLI; plus the CLI's own commands.
    /// </summary>
    internal async Task<CatalogView> LoadViewAsync(HubClient client, CancellationToken ct)
    {
        var cache = env.Paths.CatalogCache;
        if (await client.TryGetHealthAsync(ct) is not null && await client.TryGetCatalogJsonAsync(ct) is { } json)
        {
            try
            {
                var live = CatalogView.FromJson(json, $"live from the Hub at {client.BaseUrl}");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                    await File.WriteAllTextAsync(cache, json, CancellationToken.None);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* help still works */ }
                return live.With(LocalCommands.All);
            }
            catch (JsonException) { /* fall through to the cache */ }
        }

        if (File.Exists(cache))
        {
            try
            {
                var when = EasternClock.Format(File.GetLastWriteTimeUtc(cache));
                return CatalogView.FromJson(await File.ReadAllTextAsync(cache, ct), $"cached copy of the Hub's catalog from {when} (Hub not running)")
                    .With(LocalCommands.All);
            }
            catch (Exception ex) when (ex is JsonException or IOException) { /* fall through */ }
        }

        return CatalogView.FromCatalog(ReflectedCatalog.Value, "built into this CLI (Hub not running; Hub-only operations not shown)")
            .With(LocalCommands.All);
    }

    // ── local commands ──

    private async Task<int> VersionAsync(HubClient client, GlobalFlags flags, CancellationToken ct)
    {
        var hub = await client.TryGetVersionAsync(ct);
        if (flags.Json)
            await env.Out.WriteLineAsync(JsonSerializer.Serialize(new { Cli = CliVersion, Hub = hub, HubUrl = client.BaseUrl.ToString() }, OperationJson.Indented));
        else
            await env.Out.WriteLineAsync($"{Program} {CliVersion}{Environment.NewLine}hub        {(hub is null ? $"not running at {client.BaseUrl}" : $"{hub} at {client.BaseUrl}")}");
        return CliExitCodes.Ok;
    }

    private async Task<int> HubVersionAsync(HubClient client, CancellationToken ct)
    {
        if (await client.TryGetVersionAsync(ct) is not { } version)
        {
            await env.Err.WriteLineAsync(client.NotRunningMessage);
            return CliExitCodes.Failed;
        }
        await env.Out.WriteLineAsync(version);
        return CliExitCodes.Ok;
    }

    private async Task<int> HubStatusAsync(HubClient client, GlobalFlags flags, CancellationToken ct)
    {
        var health = await client.TryGetHealthAsync(ct);
        if (flags.Json)
        {
            object payload = health is null
                ? new { Status = "not-running", HubUrl = client.BaseUrl.ToString() }
                : new { health.Status, HubUrl = client.BaseUrl.ToString(), health.Version, health.StartedEt, health.MonitorHosted, health.Database, health.Operations, health.ProcessId };
            await env.Out.WriteLineAsync(JsonSerializer.Serialize(payload, OperationJson.Indented));
            return health is null ? CliExitCodes.Failed : CliExitCodes.Ok;
        }
        if (health is null)
        {
            await env.Err.WriteLineAsync(client.NotRunningMessage);
            return CliExitCodes.Failed;
        }
        await env.Out.WriteLineAsync(DescribeHealth(client.BaseUrl, health));
        return CliExitCodes.Ok;
    }

    internal static string DescribeHealth(Uri url, HubHealth health)
    {
        var started = health.StartedEt.UtcDateTime;
        var up = DateTime.UtcNow - started;
        var uptime = up.TotalHours >= 1 ? $"{(int)up.TotalHours}h {up.Minutes}m" : $"{Math.Max(0, up.Minutes)}m {Math.Max(0, up.Seconds)}s";
        return string.Join(Environment.NewLine,
            $"Hub running at {url}",
            $"  version     {health.Version}",
            $"  started     {EasternClock.Format(started)} (up {uptime})",
            $"  monitor     {(health.MonitorHosted ? "hosted — the trading loop is live" : "not hosted (IDIOTPROOF_HUB_MONITOR=0)")}",
            $"  database    {health.Database}",
            $"  operations  {health.Operations}",
            $"  process     {health.ProcessId}");
    }

    private async Task<int> HubStartAsync(HubClient client, CancellationToken ct)
    {
        if (await client.TryGetHealthAsync(ct) is { } running)
        {
            await env.Out.WriteLineAsync($"Already running.{Environment.NewLine}{DescribeHealth(client.BaseUrl, running)}");
            return CliExitCodes.Ok;
        }

        var launch = HubProcessControl.ResolveLaunch(env.Paths, env.BaseDirectory);
        if (launch is null)
        {
            await env.Err.WriteLineAsync(
                $"Can't find a Hub to start: no published Hub at {env.Paths.PublishedHubExe}, and no IdiotProof.slnx above {env.BaseDirectory}.");
            return CliExitCodes.Failed;
        }
        if (client.BaseUrl.IsLoopback)
            launch = launch with { Environment = new Dictionary<string, string> { [HubWire.PortEnvVar] = client.BaseUrl.Port.ToString(CultureInfo.InvariantCulture) } };

        var stamp = EasternClock.ToEastern(DateTime.UtcNow).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var log = Path.Combine(env.Paths.LogsDirectory, $"hub-{stamp}.log");
        var monitor = env.GetEnv("IDIOTPROOF_HUB_MONITOR") == "0"
            ? "off (IDIOTPROOF_HUB_MONITOR=0)"
            : "ON — the Hub will host the Monitor, which places real orders (set IDIOTPROOF_HUB_MONITOR=0 to keep it off)";
        await env.Out.WriteLineAsync($"Starting the Hub: {launch.Description}");
        await env.Out.WriteLineAsync($"  port     {client.BaseUrl.Port}");
        await env.Out.WriteLineAsync($"  monitor  {monitor}");
        await env.Out.WriteLineAsync($"  log      {log}");

        StartedHub started;
        try { started = env.HubProcesses.Start(launch, log); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            await env.Err.WriteLineAsync($"Could not start the Hub: {ex.Message}");
            return CliExitCodes.Failed;
        }

        var deadline = DateTime.UtcNow + StartWait;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (await client.TryGetHealthAsync(ct) is { } health)
            {
                await env.Out.WriteLineAsync(DescribeHealth(client.BaseUrl, health));
                return CliExitCodes.Ok;
            }
            if (started.HasExited())
            {
                await env.Err.WriteLineAsync($"The Hub exited during startup. Last lines of {log}:");
                await env.Err.WriteLineAsync(Tail(log, 20));
                return CliExitCodes.Failed;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); } catch (OperationCanceledException) { break; }
        }
        await env.Err.WriteLineAsync(
            $"The Hub did not answer within {StartWait.TotalSeconds:0} s (a first `dotnet run` may still be building). " +
            $"It keeps starting in the background — check `{Program} hub status` and `{Program} hub logs`.");
        return CliExitCodes.Failed;
    }

    private async Task<int> HubStopAsync(HubClient client, IReadOnlyList<string> args)
    {
        var all = args.Contains("--all");
        var health = await client.TryGetHealthAsync();
        if (health is { ProcessId: > 0 } && !all)
        {
            // Graceful first: the Hub shuts itself down (drains, stops the Monitor, releases the lease).
            if (await client.TryRequestShutdownAsync())
            {
                for (var i = 0; i < 40 && await client.TryGetHealthAsync() is not null; i++)
                    await Task.Delay(250);
                if (await client.TryGetHealthAsync() is null)
                {
                    await env.Out.WriteLineAsync($"Hub at {client.BaseUrl}: process {health.ProcessId} shut down on request.");
                    return CliExitCodes.Ok;
                }
            }
            var what = env.HubProcesses.Stop(health.ProcessId, TimeSpan.FromSeconds(10));
            await env.Out.WriteLineAsync($"Hub at {client.BaseUrl}: {what}.");
            return CliExitCodes.Ok;
        }

        var ids = env.HubProcesses.FindHubProcessIds();
        if (ids.Count == 0)
        {
            await env.Out.WriteLineAsync("No Hub is running.");
            return CliExitCodes.Ok;
        }
        if (!all)
        {
            await env.Err.WriteLineAsync(
                $"Nothing answers at {client.BaseUrl}, but {ids.Count} {HubProcessControl.ProcessName} process(es) are running " +
                $"(pid {string.Join(", ", ids)}) — on another port? Use --hub <url> to pick one, or `{Program} hub stop --all`.");
            return CliExitCodes.Failed;
        }
        foreach (var id in ids)
            await env.Out.WriteLineAsync($"{HubProcessControl.ProcessName}: {env.HubProcesses.Stop(id, TimeSpan.FromSeconds(10))}.");
        return CliExitCodes.Ok;
    }

    /// <summary><c>agent export [--out &lt;dir&gt;] [--check]</c>: regenerate the command, tool and catalog references (IP-T272).</summary>
    private async Task<int> AgentExportAsync(IReadOnlyList<string> args, GlobalFlags flags)
    {
        var list = args.ToList();
        var check = list.Remove("--check");
        string? outDir = null;
        var at = list.IndexOf("--out");
        if (at >= 0)
        {
            if (at + 1 >= list.Count)
            {
                await env.Err.WriteLineAsync("Usage error: --out needs a folder (the repo root that holds docs/).");
                return CliExitCodes.Usage;
            }
            outDir = list[at + 1];
            list.RemoveRange(at, 2);
        }
        if (list.Count > 0)
        {
            await env.Err.WriteLineAsync($"Usage error: unexpected '{list[0]}'. Usage: {Program} agent export [--out <dir>] [--check]");
            return CliExitCodes.Usage;
        }

        var root = outDir ?? HubProcessControl.FindRepoRoot(env.BaseDirectory) ?? HubProcessControl.FindRepoRoot(Environment.CurrentDirectory);
        if (root is null)
        {
            await env.Err.WriteLineAsync("Not inside the IdiotProof checkout; pass --out <repo folder>.");
            return CliExitCodes.Failed;
        }

        var files = AgentExport.Write(ReflectedCatalog.Value, root, check);
        var stale = files.Count(f => f.Changed);
        if (flags.Json)
        {
            await env.Out.WriteLineAsync(JsonSerializer.Serialize(new { Root = root, Check = check, Files = files }, OperationJson.Indented));
        }
        else
        {
            foreach (var f in files)
            {
                var state = !f.Changed ? "up to date" : check ? "STALE" : "written";
                await env.Out.WriteLineAsync($"{f.RelativePath,-36} {f.Operations,4} operations  {state}");
            }
            await env.Out.WriteLineAsync(check
                ? stale == 0 ? "All generated references are current." : $"{stale} file(s) stale: run `{Program} agent export`."
                : $"Exported to {root}.");
        }
        return check && stale > 0 ? CliExitCodes.Failed : CliExitCodes.Ok;
    }

    private async Task<int> HubLogsAsync(IReadOnlyList<string> args)
    {
        var lines = 40;
        var at = args.ToList().IndexOf("--lines");
        if (at >= 0 && (at + 1 >= args.Count || !int.TryParse(args[at + 1], out lines) || lines <= 0))
        {
            await env.Err.WriteLineAsync("Usage error: --lines needs a positive number.");
            return CliExitCodes.Usage;
        }

        var dir = new DirectoryInfo(env.Paths.LogsDirectory);
        var newest = dir.Exists
            ? dir.GetFiles("hub-*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (newest is null)
        {
            await env.Out.WriteLineAsync(
                $"No Hub logs yet in {dir.FullName}. `{Program} hub start` writes one there (hub-<date>-<time>.log, ET); " +
                "a Hub started by hand with `dotnet run` logs to its own console.");
            return CliExitCodes.Ok;
        }
        await env.Out.WriteLineAsync($"== {newest.FullName} (last {lines} lines; updated {EasternClock.Format(newest.LastWriteTimeUtc)}) ==");
        await env.Out.WriteLineAsync(Tail(newest.FullName, lines));
        return CliExitCodes.Ok;
    }

    /// <summary>The last lines of a file another process may still be writing.</summary>
    internal static string Tail(string path, int lines)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var queue = new Queue<string>(lines);
            while (reader.ReadLine() is { } line)
            {
                if (queue.Count == lines) queue.Dequeue();
                queue.Enqueue(line);
            }
            return string.Join(Environment.NewLine, queue);
        }
        catch (IOException ex)
        {
            return $"(could not read {path}: {ex.Message})";
        }
    }

    private async Task<int> UsersUseAsync(IReadOnlyList<string> args)
    {
        var store = new OwnerStore(env.Paths.ConfigFile);
        if (args is ["--clear"])
        {
            store.Save(null);
            await env.Out.WriteLineAsync($"Cleared the saved owner ({store.ConfigFile}). Calls now use the Hub's default owner.");
            return CliExitCodes.Ok;
        }
        if (args.Count != 1 || !Guid.TryParse(args[0], out var owner))
        {
            await env.Err.WriteLineAsync($"Usage: {Program} users use <guid> | --clear");
            return CliExitCodes.Usage;
        }
        store.Save(owner);
        await env.Out.WriteLineAsync($"Commands now act for {owner:D} (saved in {store.ConfigFile}). --user <guid> still overrides it per call.");
        return CliExitCodes.Ok;
    }

    private async Task<int> WhoAmIAsync(GlobalFlags flags)
    {
        var store = new OwnerStore(env.Paths.ConfigFile);
        var (owner, source) = store.Resolve(flags.User);
        var why = source switch
        {
            OwnerSource.Flag => "from --user on this command",
            OwnerSource.Saved => $"saved with `{Program} users use` in {store.ConfigFile}",
            _ => "none sent — the Hub decides (its IDIOTPROOF_OWNER, else the only user; ambiguous → a usage error asking for --user)",
        };
        if (flags.Json)
            await env.Out.WriteLineAsync(JsonSerializer.Serialize(new { Owner = owner, Source = source, Detail = why }, OperationJson.Indented));
        else
            await env.Out.WriteLineAsync(owner is { } g ? $"{g:D}  ({why})" : $"(no owner)  ({why})");
        return CliExitCodes.Ok;
    }

    private async Task<int> BootstrapAsync(HubClient client, GlobalFlags flags, CancellationToken ct)
    {
        var health = await client.TryGetHealthAsync(ct);
        var view = await LoadViewAsync(client, ct);
        var store = new OwnerStore(env.Paths.ConfigFile);
        var (owner, source) = store.Resolve(flags.User);
        var root = HubProcessControl.FindRepoRoot(env.BaseDirectory);
        string Doc(string relative) => root is null ? $"{relative} (in the IdiotProof repo)" : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        bool? DocExists(string relative) => root is null ? null : File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string[] docs = ["docs/agent/IDIOTPROOF_PROTOCOL.md", "docs/CLI_COMMANDS.md", "docs/MCP_TOOLS.md"];

        var report = new BootstrapReport
        {
            Cli = new(CliVersion, Program),
            NowEt = EasternClock.Format(DateTime.UtcNow),
            Hub = new(
                client.BaseUrl.ToString(),
                health is not null,
                health?.Version,
                health is null ? null : EasternClock.Format(health.StartedEt.UtcDateTime),
                health?.MonitorHosted,
                health?.Database,
                health?.Operations,
                health is null ? $"{Program} hub start" : null,
                string.IsNullOrWhiteSpace(env.HubKeySource()) ? "missing (the Hub creates it on first start)" : "present"),
            Owner = new(owner, source, store.ConfigFile),
            Catalog = new(view.Source, view.OperationCount, view.Nouns, env.Paths.CatalogCache, File.Exists(env.Paths.CatalogCache),
                [.. LocalCommands.All.Select(c => c.Name)]),
            Docs = [.. docs.Select(d => new BootstrapDoc(Doc(d), DocExists(d)))],
            Etiquette =
            [
                "All times are Eastern (ET). Quote them as ET to people; never convert to UTC.",
                "Every command acts for one owner: --user <guid>, else the saved `users use` default, else the Hub's default.",
                "Exit 4 = confirmation required: nothing happened yet. Show the person the preview verbatim; re-run the same command with --confirm <CODE> only on their explicit go-ahead. Never confirm Live-money actions on your own.",
                "Exit 3 = a safety gate blocked the action. Report the reason; do not look for a way around it.",
                "Exit 1 'Hub not running' = start it with `idiotproof hub start` (IDIOTPROOF_HUB_MONITOR=0 keeps the trading loop off).",
                "Use --json for machine-readable envelopes; '-' as a value reads that value from stdin.",
            ],
            ExitCodes = [.. CliExitCodes.All.Select(x => new BootstrapExitCode(x.Code, x.Meaning))],
        };

        if (flags.Json)
        {
            await env.Out.WriteLineAsync(JsonSerializer.Serialize(report, OperationJson.Indented));
            return CliExitCodes.Ok;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"IdiotProof agent bootstrap — {report.NowEt}");
        sb.AppendLine();
        sb.AppendLine(health is null ? $"Hub      not running at {client.BaseUrl} — start it with `{Program} hub start`" : DescribeHealth(client.BaseUrl, health));
        sb.AppendLine($"Hub key  {report.Hub.Key}");
        sb.AppendLine($"CLI      {Program} {CliVersion}");
        sb.AppendLine($"Owner    {(owner is { } o ? o.ToString("D") : "(none — Hub default)")}  [{OperationNames.ToKebab(source.ToString())}]");
        sb.AppendLine($"Catalog  {view.OperationCount} operations in {view.Nouns.Count} nouns, {view.Source}");
        sb.AppendLine($"         cache {env.Paths.CatalogCache}{(report.Catalog.CachePresent ? "" : " (not written yet)")}");
        sb.AppendLine();
        sb.AppendLine("Read:");
        foreach (var d in report.Docs) sb.Append("  ").Append(d.Path).AppendLine(d.Exists == false ? "  (not written yet)" : "");
        sb.AppendLine();
        sb.AppendLine("Etiquette:");
        foreach (var line in report.Etiquette) sb.Append("  - ").AppendLine(line);
        sb.AppendLine();
        sb.AppendLine("Exit codes:");
        foreach (var x in report.ExitCodes) sb.Append("  ").Append(x.Code).Append("  ").AppendLine(x.Meaning);
        sb.AppendLine();
        sb.Append($"Next: `{Program} help`, `{Program} <noun> --help`, `{Program} agent catalog --json`.");
        await env.Out.WriteLineAsync(sb.ToString());
        return CliExitCodes.Ok;
    }

    // ── helpers ──

    private async Task<int> PrintAsync(string text, int exitCode)
    {
        await env.Out.WriteLineAsync(text);
        return exitCode;
    }

    private Uri ResolveHubUrl(GlobalFlags flags) =>
        HubWire.ResolveBaseUrl(flags.Hub ?? env.GetEnv(HubWire.UrlEnvVar) ?? (int.TryParse(env.GetEnv(HubWire.PortEnvVar), out var port) && port is > 0 and < 65536
            ? $"http://127.0.0.1:{port}/"
            : $"http://127.0.0.1:{HubWire.DefaultPort}/"));

    /// <summary>Color only on a terminal with NO_COLOR unset, unless --color forces it; --no-color always wins.</summary>
    internal bool UseColor(GlobalFlags flags, bool forceColor) =>
        !flags.NoColor && (forceColor || (!env.IsOutputRedirected && string.IsNullOrEmpty(env.GetEnv("NO_COLOR"))));

    /// <summary><c>--color</c> is reserved but not one of ArgvParser's globals, so the CLI takes it itself (never after a bare <c>--</c>).</summary>
    internal static (IReadOnlyList<string> Tokens, bool ForceColor) StripColorFlag(IReadOnlyList<string> args)
    {
        var tokens = new List<string>(args.Count);
        var force = false;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--")
            {
                tokens.AddRange(args.Skip(i));
                break;
            }
            if (args[i] == "--color") force = true;
            else tokens.Add(args[i]);
        }
        return (tokens, force);
    }
}

/// <summary>The <c>agent bootstrap --json</c> shape (snake_case on the wire).</summary>
public sealed record BootstrapReport
{
    public required BootstrapCli Cli { get; init; }
    public required string NowEt { get; init; }
    public required BootstrapHub Hub { get; init; }
    public required BootstrapOwner Owner { get; init; }
    public required BootstrapCatalog Catalog { get; init; }
    public required IReadOnlyList<BootstrapDoc> Docs { get; init; }
    public required IReadOnlyList<string> Etiquette { get; init; }
    public required IReadOnlyList<BootstrapExitCode> ExitCodes { get; init; }
}

public sealed record BootstrapCli(string Version, string Program);

public sealed record BootstrapHub(
    string Url, bool Running, string? Version, string? StartedEt, bool? MonitorHosted, string? Database, int? Operations, string? StartWith, string Key);

public sealed record BootstrapOwner(Guid? Id, OwnerSource Source, string SavedIn);

public sealed record BootstrapCatalog(string Source, int Operations, IReadOnlyList<string> Nouns, string CachePath, bool CachePresent, IReadOnlyList<string> LocalCommands);

public sealed record BootstrapDoc(string Path, bool? Exists);

public sealed record BootstrapExitCode(int Code, string Meaning);
