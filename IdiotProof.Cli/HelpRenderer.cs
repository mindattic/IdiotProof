using System.Text;
using IdiotProof.Operations;

namespace IdiotProof.Cli;

/// <summary>
/// Generated help (IP-T59): <c>idiotproof help</c> (every noun with its verbs and summaries),
/// <c>idiotproof &lt;noun&gt; --help</c> (that noun's verbs) and
/// <c>idiotproof &lt;noun&gt; &lt;verb&gt; --help</c> (the command's own help). Rendered from a
/// <see cref="CatalogView"/>, so it reads the same whichever catalog source was available.
/// </summary>
public static class HelpRenderer
{
    private const string Program = OperationDescriptor.ProgramName;

    public static string GlobalFlags =>
        $"""
         Global flags (anywhere on the line):
           --json            Print the result envelope as JSON instead of text.
           --user <guid>     Act for this owner (default: `{Program} users use`, else the Hub's).
           --confirm <code>  Execute a previewed action (the code from an exit-4 result).
           --hub <url>       Hub address (default: IDIOTPROOF_HUB_URL, else http://127.0.0.1:{HubWire.DefaultPort}/).
           --no-color        Never color output.   --color  Color even when piped.
           -h, --help        Help for whatever precedes it.
           -                 As any argument or option value: read that value from stdin.
         """;

    /// <summary>Every noun and its verbs.</summary>
    public static string Overview(CatalogView view)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Program} — the IdiotProof command line. Times are Eastern (ET).");
        sb.AppendLine();
        sb.AppendLine($"Usage: {Program} <noun> <verb> [arguments] [--options]");
        sb.AppendLine();
        sb.AppendLine("Commands:");

        var singles = view.Entries.Where(e => e.Verb.Length == 0).ToList();
        var width = view.Entries.Where(e => e.Verb.Length > 0).Select(e => e.Verb.Length).DefaultIfEmpty(0).Max();
        width = Math.Max(width, singles.Select(e => e.Noun.Length).DefaultIfEmpty(0).Max());
        foreach (var e in singles)
            sb.Append("  ").Append(e.Noun.PadRight(width + 2)).AppendLine(e.Summary);
        foreach (var noun in view.Nouns.Where(n => view.ForNoun(n).Any(e => e.Verb.Length > 0)))
        {
            sb.Append("  ").AppendLine(noun);
            foreach (var e in view.ForNoun(noun).Where(e => e.Verb.Length > 0))
                sb.Append("    ").Append(e.Verb.PadRight(width)).Append("  ").AppendLine(e.Summary);
        }
        sb.AppendLine();
        sb.AppendLine(GlobalFlags);
        sb.AppendLine();
        sb.AppendLine("Exit codes:");
        foreach (var (code, meaning) in CliExitCodes.All) sb.Append("  ").Append(code).Append("  ").AppendLine(meaning);
        sb.AppendLine();
        sb.AppendLine($"More: `{Program} <noun> --help`, `{Program} <noun> <verb> --help`, `{Program} agent bootstrap`.");
        sb.Append("Catalog: ").Append(view.Source);
        return sb.ToString();
    }

    /// <summary>One noun's verbs.</summary>
    public static string Noun(CatalogView view, string noun)
    {
        var entries = view.ForNoun(noun);
        var sb = new StringBuilder();
        sb.AppendLine($"{Program} {noun.ToLowerInvariant()} — {entries.Count} command(s)");
        sb.AppendLine();
        var width = entries.Select(e => e.Verb.Length).DefaultIfEmpty(0).Max();
        foreach (var e in entries)
            sb.Append("  ").Append(e.Verb.PadRight(width)).Append("  ").Append(e.Risk.PadRight(11)).AppendLine(e.Summary);
        sb.AppendLine();
        sb.Append($"Run `{Program} {noun.ToLowerInvariant()} <verb> --help` for one command's parameters and examples.");
        return sb.ToString();
    }

    /// <summary>One command's help.</summary>
    public static string Command(CatalogEntry entry) => entry.Help;

    /// <summary>
    /// Help for the words typed (<c>[]</c>, <c>[noun]</c>, <c>[noun, verb…]</c>). Returns the text
    /// and whether it was an error (an unknown topic, with a suggestion when one is close).
    /// </summary>
    public static (string Text, bool IsError) For(CatalogView view, IReadOnlyList<string> words)
    {
        var topic = words.TakeWhile(w => !w.StartsWith('-')).ToList();
        if (topic.Count == 0) return (Overview(view), false);
        // Extra words after a known command are its arguments (`strategy show 12 --help`).
        if (view.TryResolve(topic, out var entry, out _)) return (Command(entry!), false);
        if (topic.Count == 1 && view.HasNoun(topic[0])) return (Noun(view, topic[0]), false);

        var typed = string.Join(' ', topic);
        var hint = view.Suggest(typed) is { } s ? $" Did you mean '{s}'?" : "";
        if (view.HasNoun(topic[0]))
            return ($"Unknown command '{typed}'.{hint}{Environment.NewLine}{Environment.NewLine}{Noun(view, topic[0])}", true);
        return ($"Unknown command '{typed}'.{hint} Run `{Program} help` for the list.", true);
    }
}
