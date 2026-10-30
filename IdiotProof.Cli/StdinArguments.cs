namespace IdiotProof.Cli;

/// <summary>
/// The stdin convention (IP-T58): any argument or option value that is exactly <c>-</c> is
/// replaced by the whole of stdin before the call is sent, so long text never has to survive
/// shell quoting — <c>Get-Content notes.txt | idiotproof strategy note 12 --text -</c>. The
/// <c>--name=-</c> spelling works too. One trailing newline (and a leading BOM) is trimmed. Stdin is read only when a
/// <c>-</c> is present: reading it unconditionally hangs forever on an agent's never-closing pipe.
/// Only one argument may read stdin (it can be read once).
/// </summary>
public static class StdinArguments
{
    public const string Marker = "-";

    /// <summary>True when any token asks for stdin.</summary>
    public static bool Wants(IReadOnlyList<string> tokens) => tokens.Any(IsMarker);

    /// <summary>
    /// Replaces the stdin marker with the text <paramref name="readStdin"/> returns (called at most
    /// once). Returns an error instead when more than one token asks for stdin.
    /// </summary>
    public static (IReadOnlyList<string> Tokens, string? Error) Apply(IReadOnlyList<string> tokens, Func<string> readStdin)
    {
        var count = tokens.Count(IsMarker);
        if (count == 0) return (tokens, null);
        if (count > 1) return (tokens, "Only one argument can read stdin ('-'); stdin can be read once.");

        // PowerShell 5.1 pipes a UTF-8 BOM ahead of the text; it is never part of the value.
        var text = TrimOneNewline(readStdin().TrimStart('\uFEFF'));
        var result = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            if (token == Marker) result.Add(text);
            else if (IsInlineMarker(token)) result.Add(token[..^1] + text);
            else result.Add(token);
        }
        return (result, null);
    }

    private static bool IsMarker(string token) => token == Marker || IsInlineMarker(token);

    /// <summary><c>--text=-</c>.</summary>
    private static bool IsInlineMarker(string token) =>
        token.StartsWith("--", StringComparison.Ordinal) && token.Length > 3 && token.EndsWith("=-", StringComparison.Ordinal);

    private static string TrimOneNewline(string text) =>
        text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2]
        : text.EndsWith('\n') ? text[..^1]
        : text;
}
