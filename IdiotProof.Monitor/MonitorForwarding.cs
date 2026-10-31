using System.Net.Http.Json;
using IdiotProof.Operations;

namespace IdiotProof.Monitor;

/// <summary>
/// The Monitor's operator subcommands that now have an operation forward to it through the Hub
/// (IP-T138), so there is one implementation and every action is ledgered and gated there:
/// <code>
///   status                                  → monitor status
///   set-keys --key K --secret S [--live]    → keys set alpaca-paper|alpaca-live --key-id K --secret S
///   flatten --symbol S                      → trade flatten --strategy S   (that strategy's own mode)
///   resync-canon [--apply]                  → strategy resync-canon [--apply]
///   premarket-fade                          → gapper premarket-fade run
///   create-account --email E --password P   → users create E --password P
///   create-strategies --file F              → strategy from-watchlist F
/// </code>
/// <c>--user &lt;guid&gt;</c> passes through. The rest (replay*, scan, test-order, auto-gapper)
/// still run in-process, as does any command given <c>--local</c>.
/// </summary>
public static class MonitorForwarding
{
    /// <summary>Forces the old in-process implementation.</summary>
    public const string LocalFlag = "--local";

    /// <summary>An operation call: the argv the Hub receives (name first) and how it reads to a person.</summary>
    public sealed record Forward(IReadOnlyList<string> Argv)
    {
        public string Display => "idiotproof " + string.Join(' ', Argv.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
    }

    /// <summary>
    /// The forward for <paramref name="args"/>, or null when the command still runs in-process.
    /// <paramref name="usage"/> is set (and null returned) when the old form has no safe equivalent.
    /// </summary>
    public static Forward? Translate(string[] args, out string? usage)
    {
        usage = null;
        if (args.Length == 0 || args.Contains(LocalFlag, StringComparer.OrdinalIgnoreCase)) return null;
        var opt = ParseOptions(args);
        List<string>? argv = args[0] switch
        {
            "status" => ["monitor", "status"],
            "set-keys" => SetKeys(opt, ref usage),
            "flatten" => Flatten(opt, ref usage),
            "resync-canon" => opt.ContainsKey("apply") ? ["strategy", "resync-canon", "--apply"] : ["strategy", "resync-canon"],
            "premarket-fade" => ["gapper", "premarket-fade", "run"],
            "create-account" => opt.TryGetValue("email", out var email) && opt.TryGetValue("password", out var password)
                ? ["users", "create", email, "--password", password]
                : Usage(ref usage, "create-account requires --email and --password (forwards to `idiotproof users create <email> --password <p>`)."),
            "create-strategies" => opt.TryGetValue("file", out var file)
                ? ["strategy", "from-watchlist", Path.GetFullPath(file)]
                : Usage(ref usage, "create-strategies requires --file <watchlist.json> (forwards to `idiotproof strategy from-watchlist <file>`)."),
            _ => null,
        };
        if (argv is null) return null;
        if (opt.TryGetValue("user", out var user)) argv.AddRange(["--user", user]);
        return new Forward(argv);
    }

    private static List<string>? SetKeys(Dictionary<string, string> opt, ref string? usage)
    {
        if (!opt.TryGetValue("key", out var key) || !opt.TryGetValue("secret", out var secret))
            return Usage(ref usage, "set-keys requires --key and --secret (forwards to `idiotproof keys set alpaca-paper|alpaca-live --key-id K --secret S`).");
        List<string> argv = ["keys", "set", opt.ContainsKey("live") ? "alpaca-live" : "alpaca-paper", "--key-id", key, "--secret", secret];
        if (opt.ContainsKey("force")) argv.Add("--force");
        return argv;
    }

    private static List<string>? Flatten(Dictionary<string, string> opt, ref string? usage)
    {
        // The old flatten closed every strategy-held position; `trade flatten --all` closes the whole
        // ACCOUNT, which is broader, so there is no blind forward for the no-symbol form.
        if (opt.TryGetValue("symbol", out var symbol) && symbol != "true")
            return ["trade", "flatten", "--strategy", symbol.Trim().ToUpperInvariant()];
        return Usage(ref usage,
            "flatten now runs through the Hub's gateway: `idiotproof trade flatten --strategy <symbol>` per strategy " +
            "(or `--all --mode paper|live` for the whole account). Pass --symbol <SYM> here, or --local for the old in-process flatten.");
    }

    private static List<string>? Usage(ref string? usage, string message)
    {
        usage = message;
        return null;
    }

    /// <summary>Sends the forward to the Hub and prints its answer; returns the operation's exit code.</summary>
    public static async Task<int> RunAsync(Forward forward, TextWriter output, TextWriter error, HttpMessageHandler? handler = null)
    {
        error.WriteLine($"  (forwarded to the Hub: {forward.Display})");
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.BaseAddress = HubWire.ResolveBaseUrl();
        http.Timeout = TimeSpan.FromMinutes(5);
        using var request = new HttpRequestMessage(HttpMethod.Post, HubWire.OperationPath)
        {
            Content = JsonContent.Create(new HubOperationRequest { Argv = forward.Argv, Caller = OperationCaller.Cli, Output = OutputMode.Human }, options: OperationJson.Options),
        };
        if (HubKey.ForClient() is { } key) request.Headers.Add(HubWire.KeyHeader, key);

        HttpResponseMessage response;
        try { response = await http.SendAsync(request); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            error.WriteLine($"The Hub is not answering at {http.BaseAddress} ({ex.Message}). Start it (`idiotproof hub start`, or run this exe with no arguments), " +
                            $"or add {LocalFlag} to run the old in-process command.");
            return 1;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                error.WriteLine($"The Hub refused the call ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
                return 1;
            }
            var body = await response.Content.ReadFromJsonAsync<HubOperationResponse>(OperationJson.Options);
            if (body is null)
            {
                error.WriteLine($"The Hub answered {(int)response.StatusCode} with no result.");
                return 1;
            }
            (body.ExitCode == 0 ? output : error).WriteLine(body.Text);
            return body.ExitCode;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = args[i][2..];
            var val = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            d[key] = val;
        }
        return d;
    }
}
