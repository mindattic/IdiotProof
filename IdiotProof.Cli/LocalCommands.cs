using IdiotProof.Operations;

namespace IdiotProof.Cli;

/// <summary>
/// The commands the CLI runs itself, without the Hub: help, version, <c>agent bootstrap</c>, Hub
/// lifecycle (<c>hub status|start|stop|logs|version</c>, IP-T83) and the saved owner default
/// (<c>users use|whoami</c>, IP-T63). They appear in help next to the Hub's operations and win
/// over a Hub operation of the same name.
/// </summary>
public static class LocalCommands
{
    private const string Program = OperationDescriptor.ProgramName;
    private const string NoHub = "Runs in the CLI itself; the Hub does not need to be running.";

    public static IReadOnlyList<CatalogEntry> All { get; } =
    [
        Entry("help", "Show commands, a noun's verbs, or one command's help.",
            $"{Program} help [<noun> [<verb>]]",
            $"""
             Equivalent forms: `{Program} --help`, `{Program} <noun> --help`, `{Program} <noun> <verb> --help`.
             Help comes from the Hub's catalog when it is running (cached for later), else from the
             cache, else from the operations built into this CLI.
             """,
            [$"{Program} help", $"{Program} help strategy", $"{Program} strategy list --help"]),
        Entry("version", "Show the CLI version and, when it is running, the Hub's.",
            $"{Program} version", null, [$"{Program} version"]),
        Entry("agent bootstrap", "Everything an agent needs to start: Hub health, owner, catalog, docs, etiquette.",
            $"{Program} agent bootstrap [--json]",
            "Works with the Hub down. Start every agent session with it.",
            [$"{Program} agent bootstrap", $"{Program} agent bootstrap --json"]),
        Entry("hub status", "Is the Hub running? Version, start time (ET), Monitor, database, operations.",
            $"{Program} hub status [--json]",
            "Exit 0 when the Hub answers, 1 when it does not.",
            [$"{Program} hub status"]),
        Entry("hub start", "Start the Hub in the background and wait (up to 60 s) until it answers.",
            $"{Program} hub start",
            """
            Runs the published C:\Apps\IdiotProof\Hub\idiotproof-hub.exe when present, else
            `dotnet run --project <repo>\IdiotProof.Hub`. Its console output goes to a hub-*.log file
            (see `hub logs`). It inherits this shell's environment: IDIOTPROOF_HUB_MONITOR=0 keeps the
            trading loop off; without it the Hub hosts the Monitor, which places real orders.
            """,
            [$"{Program} hub start"]),
        Entry("hub stop", "Stop the running Hub (asks it to close first, then ends the process).",
            $"{Program} hub stop [--all]",
            "Stops the Hub that answers at the resolved URL. --all stops every idiotproof-hub process.",
            [$"{Program} hub stop"]),
        Entry("hub logs", "Show the end of the newest Hub log.",
            $"{Program} hub logs [--lines <int>]",
            @"Logs live in %LOCALAPPDATA%\MindAttic\IdiotProof\Logs (hub-*.log, written by `hub start`).",
            [$"{Program} hub logs", $"{Program} hub logs --lines 200"]),
        Entry("hub version", "Show the running Hub's version.",
            $"{Program} hub version", null, [$"{Program} hub version"]),
        Entry("users use", "Save the owner every command acts for (until changed).",
            $"{Program} users use <guid> | --clear",
            @"Saved in %APPDATA%\MindAttic\IdiotProof\cli.json. --user <guid> on any command overrides it for that call. Client-side: it wins over any Hub operation of the same name.",
            [$"{Program} users use A99504C3-0759-4256-8ABB-A1F5C643808C", $"{Program} users use --clear"]),
        Entry("users whoami", "Show which owner commands act for, and why.",
            $"{Program} users whoami [--json]", null, [$"{Program} users whoami"]),
    ];

    private static CatalogEntry Entry(string name, string summary, string usage, string? description, string[] examples)
    {
        var words = name.Split(' ');
        var help = new System.Text.StringBuilder();
        help.AppendLine(summary);
        if (description is not null) help.AppendLine().AppendLine(description.Trim());
        help.AppendLine().Append("Usage: ").AppendLine(usage);
        help.AppendLine("Risk: read");
        help.AppendLine().AppendLine("Examples:");
        foreach (var e in examples) help.Append("  ").AppendLine(e);
        help.AppendLine().Append(NoHub);
        return new CatalogEntry
        {
            Name = name,
            Noun = words[0],
            Verb = string.Join(' ', words.Skip(1)),
            Summary = summary,
            Risk = "read",
            Usage = usage,
            Help = help.ToString(),
            Local = true,
        };
    }
}
