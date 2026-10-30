using System.Text;

namespace IdiotProof.Operations;

/// <summary>
/// The flags every front door owns, peeled off argv before the operation's own parameters are
/// bound (their names are reserved in <see cref="OperationCatalog.ReservedOptionNames"/>).
/// </summary>
public sealed record GlobalFlags
{
    /// <summary><c>--json</c>: print the result envelope instead of human text.</summary>
    public bool Json { get; init; }

    /// <summary><c>--help</c> / <c>-h</c>: print the operation's help instead of running it.</summary>
    public bool Help { get; init; }

    /// <summary><c>--user &lt;guid&gt;</c>: act for this owner.</summary>
    public Guid? User { get; init; }

    /// <summary><c>--confirm &lt;code&gt;</c>: the code from a ConfirmationRequired result.</summary>
    public string? Confirm { get; init; }

    /// <summary><c>--hub &lt;url&gt;</c>: Hub address override (the CLI's concern only).</summary>
    public string? Hub { get; init; }

    /// <summary><c>--no-color</c>: never emit ANSI, even on a terminal.</summary>
    public bool NoColor { get; init; }
}

/// <summary>Result of <see cref="ArgvParser.ExtractGlobals"/>: the global flags, the operation's own tokens, or a usage error.</summary>
public sealed record ExtractedGlobals(GlobalFlags Flags, IReadOnlyList<string> Remaining, string? Error);

/// <summary>
/// Pure argv helpers for the CLI (and for tests): split a command line the way a shell would,
/// and separate the global flags from the operation's own tokens. Binding the remaining tokens to
/// an args record is <see cref="ArgsBinder.Bind(OperationDescriptor, IReadOnlyList{string})"/>,
/// which needs the descriptor to know which options are flags.
/// </summary>
public static class ArgvParser
{
    /// <summary>
    /// Splits <c>strategy add "Gap and go" --ticker AAPL</c> into tokens: whitespace separates,
    /// double or single quotes group, a backslash escapes a quote inside double quotes.
    /// </summary>
    public static IReadOnlyList<string> Split(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        char? quote = null;
        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (quote is { } q)
            {
                if (c == '\\' && q == '"' && i + 1 < commandLine.Length && commandLine[i + 1] is '"' or '\\')
                    current.Append(commandLine[++i]);
                else if (c == q)
                    quote = null;
                else
                    current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inToken = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inToken) tokens.Add(current.ToString());
                current.Clear();
                inToken = false;
            }
            else
            {
                current.Append(c);
                inToken = true;
            }
        }
        if (quote is not null) throw new FormatException("Unterminated quote in command line.");
        if (inToken) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>
    /// Pulls the global flags out of argv wherever they appear (before or after the operation
    /// name) and returns the rest untouched. Tokens after a bare <c>--</c> are never globals.
    /// </summary>
    public static ExtractedGlobals ExtractGlobals(IReadOnlyList<string> argv)
    {
        var flags = new GlobalFlags();
        var rest = new List<string>();
        for (var i = 0; i < argv.Count; i++)
        {
            var token = argv[i];
            if (token == "--")
            {
                rest.AddRange(argv.Skip(i));
                break;
            }

            var (name, inline) = token.StartsWith("--", StringComparison.Ordinal)
                ? SplitInline(token[2..])
                : (null, null);
            if (token == "-h") name = "help";

            switch (name)
            {
                case "json": flags = flags with { Json = inline is null || bool.TryParse(inline, out var j) && j }; break;
                case "help": flags = flags with { Help = true }; break;
                case "no-color": flags = flags with { NoColor = true }; break;
                case "user" or "confirm" or "hub":
                    var value = inline ?? (i + 1 < argv.Count ? argv[++i] : null);
                    if (value is null) return new ExtractedGlobals(flags, rest, $"--{name} needs a value.");
                    if (name == "user")
                    {
                        if (!Guid.TryParse(value, out var user)) return new ExtractedGlobals(flags, rest, $"--user expects a user id (GUID), got '{value}'.");
                        flags = flags with { User = user };
                    }
                    else if (name == "confirm") flags = flags with { Confirm = value };
                    else flags = flags with { Hub = value };
                    break;
                default: rest.Add(token); break;
            }
        }
        return new ExtractedGlobals(flags, rest, null);
    }

    /// <summary><c>name=value</c> → (name, value); <c>name</c> → (name, null).</summary>
    internal static (string Name, string? Value) SplitInline(string body)
    {
        var eq = body.IndexOf('=');
        return eq < 0 ? (body, null) : (body[..eq], body[(eq + 1)..]);
    }

    /// <summary>A token that reads as a negative number (<c>-5</c>, <c>-0.25</c>) is a value, not an option.</summary>
    /// <summary>
    /// Option-shaped: <c>-x</c> (a dash then a letter), <c>--name</c>, <c>--name=value</c> or the <c>--</c> end marker. Anything
    /// else that merely starts with a dash — a negative number, <c>-</c> (stdin), a pasted
    /// <c>-----BEGIN IDIOTPROOF STRATEGY-----</c> block — is a value.
    /// </summary>
    public static bool IsOptionToken(string token) =>
        token == "--" || IsLongOptionToken(token) ||
        (token.Length >= 2 && token[0] == '-' && char.IsLetter(token[1]));

    /// <summary><c>--name</c> or <c>--name=value</c> with a letter first.</summary>
    public static bool IsLongOptionToken(string token) =>
        token.Length > 2 && token.StartsWith("--", StringComparison.Ordinal) && char.IsLetter(token[2]);

    internal static bool IsNegativeNumber(string token) =>
        token.Length > 1 && token[0] == '-' && (char.IsDigit(token[1]) || token[1] == '.' && token.Length > 2 && char.IsDigit(token[2]));
}
