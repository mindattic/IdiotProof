using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace IdiotProof.Scripting;

public enum DiagnosticSeverity { Warning, Error }

/// <summary>One problem in an IdiotScript, pinned to a 1-based line and column.</summary>
public sealed record ScriptDiagnostic(DiagnosticSeverity Severity, string Code, string Message, int Line, int Column,
    int Length, string? Suggestion, string Excerpt)
{
    public bool IsError => Severity == DiagnosticSeverity.Error;

    public override string ToString() =>
        $"{Line}:{Column} {(IsError ? "error" : "warning")} {Code}: {Message}" +
        (Suggestion is null ? "" : $" Did you mean '{Suggestion}'?") + "\n" + Excerpt;
}

/// <summary>
/// Explains, with exact line/column, everything in an IdiotScript that <see cref="ScriptParser"/>
/// would silently drop or misread: unknown verbs (with a did-you-mean), unbalanced parentheses,
/// unterminated strings, a missing Ticker, and arguments of the wrong type or count. It reads the
/// same text the parser reads and never changes what the parser builds; a script with no errors
/// here parses to exactly what it says.
/// </summary>
public static class ScriptDiagnostics
{
    private static readonly HashSet<string> Structural = new(StringComparer.OrdinalIgnoreCase)
    {
        "ticker", "then", "elseif", "else", "if", "endif", "and", "or", "not", "build",
    };

    // Argument shapes of the verbs the parser reads arguments for. Upper case = required, lower
    // case = optional; N number, I whole number, S text. A trailing '*' repeats the last kind.
    // An empty shape means the verb takes no arguments. Verbs not listed are not shape-checked.
    private static readonly Dictionary<string, string> Shapes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["session"] = "S", ["requireentrywindow"] = "SS", ["entrywindow"] = "SS",
        ["sellby"] = "S", ["exitstrategy"] = "S", ["peakgiveback"] = "Ns",
        ["isaboveema"] = "I", ["isbelowema"] = "I", ["isemaabove"] = "I", ["isemabelow"] = "I", ["onreclaim"] = "I",
        ["isbetweenema"] = "II", ["requireemastack"] = "II",
        ["requireadxabove"] = "n", ["trending"] = "n", ["isadxabove"] = "N",
        ["higherlows"] = "i", ["ishigherlows"] = "i", ["dippedto"] = "N", ["hasdippedto"] = "N",
        ["notfallingknife"] = "ni", ["notbulltrap"] = "ni", ["notstophunt"] = "ni", ["notvwaploss"] = "ni",
        ["isrsioversold"] = "n", ["isrsioverbought"] = "n", ["oversold"] = "n", ["overbought"] = "n",
        ["withvolumeconfirm"] = "n", ["volumespike"] = "n", ["isvolumeabove"] = "N",
        ["isgapup"] = "n", ["gapup"] = "n", ["isgapdown"] = "n", ["gapdown"] = "n", ["isgapbetween"] = "NN",
        ["isatsupport"] = "n", ["isatresistance"] = "n", ["breakout"] = "n", ["pullback"] = "n", ["entry"] = "N",
        ["holdsabove"] = "N", ["holdsbelow"] = "N", ["breaksabove"] = "N", ["breaksbelow"] = "N",
        ["isnear"] = "Nn", ["ispricebetween"] = "NN",
        ["quantity"] = "I", ["quantityshares"] = "I", ["quantitynotional"] = "N",
        ["stoploss"] = "N", ["stoplosspercent"] = "N", ["takeprofitpercent"] = "N", ["trailingstoploss"] = "N",
        ["takeprofit"] = "Nnn", ["addtarget"] = "NIs",
        ["exitatrollinghigh"] = "In", ["exitatrollinglow"] = "In",
        ["entryatrollinglow"] = "In", ["isnearrollinglow"] = "In", ["entryatsupport"] = "In",
        ["entryatrollinghigh"] = "In", ["isnearrollinghigh"] = "In", ["entryatresistance"] = "In", ["isbreakingout"] = "In",
        ["repeat"] = "i", ["for"] = "NNI", ["foreach"] = "N*",
        ["isabovevwap"] = "", ["abovevwap"] = "", ["isbelowvwap"] = "", ["belowvwap"] = "",
        ["onvwapreclaim"] = "", ["onvwaploss"] = "", ["isdipositive"] = "", ["isdinegative"] = "",
        ["ishigherlow"] = "", ["islowerhigh"] = "", ["isrsibullishdivergence"] = "", ["isrsibearishdivergence"] = "",
        ["ismacdbullish"] = "", ["bullishmacd"] = "", ["ismacdbearish"] = "", ["bearishmacd"] = "",
        ["isbullishengulfing"] = "", ["isbearishengulfing"] = "", ["ishammer"] = "", ["isshootingstar"] = "", ["isdoji"] = "",
        ["long"] = "", ["short"] = "", ["exitatpriorhigh"] = "", ["autonomoustrading"] = "", ["adaptiveorder"] = "",
        ["while"] = "", ["endwhile"] = "", ["exitwhenfalse"] = "", ["takeprofiteach"] = "",
    };

    private static readonly ConcurrentDictionary<string, bool> KnownCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<string[]> Canonical = new(() =>
        typeof(StrategyBuilder).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Distinct().Where(IsKnownVerb).OrderBy(n => n, StringComparer.Ordinal).ToArray());

    /// <summary>True when <see cref="ScriptParser"/> has a case for the verb (aliases included).</summary>
    public static bool IsKnownVerb(string name) => KnownCache.GetOrAdd(name, static n =>
    {
        try { return ScriptParser.ApplyVerb(Stock.Ticker("DIAG"), n, ""); }
        catch (Exception) { return true; } // it reached its case; the empty arguments made the builder object
    });

    /// <summary>The verbs the parser understands, by their builder names (for suggestions and docs).</summary>
    public static IReadOnlyList<string> KnownVerbs => Canonical.Value;

    public static IReadOnlyList<ScriptDiagnostic> Analyze(string? text)
    {
        var src = text ?? "";
        var diags = new List<ScriptDiagnostic>();
        var lineStarts = LineStarts(src);
        var tokens = Tokenize(src, diags, lineStarts);

        bool sawTicker = false;
        var open = new Stack<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.LParen) { open.Push(i); continue; }
            if (t.Kind == TokenKind.RParen)
            {
                if (open.Count == 0)
                    diags.Add(Make(DiagnosticSeverity.Error, "IPS003", "Unexpected ')' with no matching '('.", t.Start, 1, null, src, lineStarts));
                else open.Pop();
                continue;
            }
            if (t.Kind != TokenKind.Identifier) continue;

            bool call = i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.LParen;
            bool dotted = i > 0 && tokens[i - 1].Kind == TokenKind.Dot;
            if (!call && !(dotted && open.Count == 0)) continue;
            // Qualified names such as IS.PREMARKET or Stock.Ticker: only the call matters.
            if (!call && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Dot) continue;

            var name = t.Text;
            if (name.Equals("ticker", StringComparison.OrdinalIgnoreCase)) sawTicker = true;
            if (Structural.Contains(name)) continue;

            if (!IsKnownVerb(name))
            {
                diags.Add(Make(DiagnosticSeverity.Error, "IPS001",
                    $"Unknown verb '{name}': the parser skips it, so this part of the strategy would silently vanish.",
                    t.Start, name.Length, Suggest(name), src, lineStarts));
                continue;
            }
            if (call) CheckArguments(name, tokens, i + 1, diags, src, lineStarts);
        }

        while (open.Count > 0)
        {
            var t = tokens[open.Pop()];
            diags.Add(Make(DiagnosticSeverity.Error, "IPS002", "This '(' is never closed.", t.Start, 1, null, src, lineStarts));
        }
        if (!sawTicker && src.Trim().Length > 0)
            diags.Add(Make(DiagnosticSeverity.Error, "IPS005", "A script must name its symbol with Ticker(\"SYM\"); without it nothing parses.",
                FirstNonSpace(src), 1, null, src, lineStarts));

        return diags.OrderBy(d => d.Line).ThenBy(d => d.Column).ToList();
    }

    public static bool HasErrors(IEnumerable<ScriptDiagnostic> diagnostics) => diagnostics.Any(d => d.IsError);

    private static void CheckArguments(string verb, List<Token> tokens, int lparen, List<ScriptDiagnostic> diags, string src, int[] lineStarts)
    {
        if (!Shapes.TryGetValue(verb, out var shape)) return;
        var args = SplitArguments(tokens, lparen, out int closeAt);
        if (closeAt < 0) return; // unbalanced; reported elsewhere

        bool repeat = shape.EndsWith('*');
        var kinds = repeat ? shape[..^1] : shape;
        if (kinds.Length == 0 && !repeat)
        {
            if (args.Count > 0)
                diags.Add(Make(DiagnosticSeverity.Warning, "IPS010", $"{verb}() takes no arguments; these are ignored.",
                    args[0].Start, Math.Max(1, args[^1].End - args[0].Start), null, src, lineStarts));
            return;
        }

        for (int a = 0; a < args.Count; a++)
        {
            char kind;
            if (a < kinds.Length) kind = kinds[a];
            else if (repeat) kind = char.ToLowerInvariant(kinds[^1]);
            else
            {
                diags.Add(Make(DiagnosticSeverity.Warning, "IPS008", $"{verb} reads at most {kinds.Length} argument(s); the rest are ignored.",
                    args[a].Start, Math.Max(1, args[^1].End - args[a].Start), null, src, lineStarts));
                break;
            }
            var arg = args[a];
            var lower = char.ToLowerInvariant(kind);
            if (lower == 's') continue;
            if (!TryNumber(arg.Text, out var value))
            {
                diags.Add(Make(DiagnosticSeverity.Error, "IPS006",
                    $"{verb} argument {a + 1} must be a number, not '{arg.Text}'; the parser ignores it and the verb misreads its arguments.",
                    arg.Start, Math.Max(1, arg.End - arg.Start), null, src, lineStarts));
            }
            else if (lower == 'i' && value != decimal.Truncate(value))
            {
                diags.Add(Make(DiagnosticSeverity.Error, "IPS009",
                    $"{verb} argument {a + 1} must be a whole number; {arg.Text} would be cut to {decimal.Truncate(value)}.",
                    arg.Start, Math.Max(1, arg.End - arg.Start), null, src, lineStarts));
            }
        }

        for (int a = args.Count; a < kinds.Length; a++)
        {
            if (!char.IsUpper(kinds[a])) break;
            var close = tokens[closeAt];
            diags.Add(Make(DiagnosticSeverity.Error, "IPS007",
                $"{verb} needs {kinds.Count(char.IsUpper)} argument(s) but got {args.Count}; without them the verb is skipped.",
                close.Start, 1, null, src, lineStarts));
            break;
        }
    }

    private readonly record struct Arg(string Text, int Start, int End);

    private static List<Arg> SplitArguments(List<Token> tokens, int lparen, out int closeAt)
    {
        var args = new List<Arg>();
        int depth = 0, argStart = -1, argEnd = -1;
        closeAt = -1;
        for (int i = lparen; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.LParen) { depth++; if (depth == 1) continue; }
            else if (t.Kind == TokenKind.RParen)
            {
                depth--;
                if (depth == 0)
                {
                    if (argStart >= 0) args.Add(Slice(tokens, argStart, argEnd));
                    closeAt = i;
                    return args;
                }
            }
            else if (t.Kind == TokenKind.Comma && depth == 1)
            {
                args.Add(argStart >= 0 ? Slice(tokens, argStart, argEnd) : new Arg("", t.Start, t.Start));
                argStart = -1;
                continue;
            }
            if (argStart < 0) argStart = i;
            argEnd = i;
        }
        return args;
    }

    private static Arg Slice(List<Token> tokens, int from, int to)
    {
        var sb = new StringBuilder();
        for (int i = from; i <= to; i++) sb.Append(tokens[i].Text);
        return new Arg(sb.ToString(), tokens[from].Start, tokens[to].Start + tokens[to].Text.Length);
    }

    // Mirrors ScriptParser.ParseNumericArgs so "is it a number" means the same thing to both.
    private static bool TryNumber(string raw, out decimal value)
    {
        var s = raw.Trim().TrimEnd('m', 'M', 'd', 'D', 'f', 'F').Trim();
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static string? Suggest(string name)
    {
        var lower = name.ToLowerInvariant();
        int limit = lower.Length <= 6 ? 1 : lower.Length <= 12 ? 2 : 3;
        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var candidate in KnownVerbs)
        {
            int d = Distance(lower, candidate.ToLowerInvariant());
            if (d < bestDist) { bestDist = d; best = candidate; }
        }
        return bestDist <= limit ? best : null;
    }

    /// <summary>Optimal-string-alignment (Damerau-Levenshtein) distance: swaps of neighbors count as one edit.</summary>
    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
        for (int j = 1; j <= b.Length; j++)
        {
            int cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }
        return d[a.Length, b.Length];
    }

    private enum TokenKind { Identifier, Number, String, Dot, LParen, RParen, Comma, Other }

    private readonly record struct Token(TokenKind Kind, string Text, int Start);

    private static List<Token> Tokenize(string src, List<ScriptDiagnostic> diags, int[] lineStarts)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < src.Length)
        {
            char c = src[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? src.Length : end + 2;
                continue;
            }
            if (c == '"')
            {
                int start = i++;
                while (i < src.Length && src[i] != '"' && src[i] != '\n')
                    i += src[i] == '\\' && i + 1 < src.Length ? 2 : 1;
                if (i >= src.Length || src[i] != '"')
                {
                    diags.Add(Make(DiagnosticSeverity.Error, "IPS004", "This string is never closed with '\"'.", start, 1, null, src, lineStarts));
                    tokens.Add(new Token(TokenKind.String, src[start..i], start));
                    continue;
                }
                i++;
                tokens.Add(new Token(TokenKind.String, src[start..i], start));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Identifier, src[start..i], start));
                continue;
            }
            if (char.IsDigit(c) || ((c == '-' || c == '+') && i + 1 < src.Length && (char.IsDigit(src[i + 1]) || src[i + 1] == '.'))
                || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1]) && !PrecededByIdentifierOrParen(tokens)))
            {
                int start = i++;
                while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '.' || src[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Number, src[start..i], start));
                continue;
            }
            var kind = c switch
            {
                '.' => TokenKind.Dot,
                '(' => TokenKind.LParen,
                ')' => TokenKind.RParen,
                ',' => TokenKind.Comma,
                _ => TokenKind.Other,
            };
            tokens.Add(new Token(kind, c.ToString(), i));
            i++;
        }
        return tokens;
    }

    private static bool PrecededByIdentifierOrParen(List<Token> tokens) =>
        tokens.Count > 0 && tokens[^1].Kind is TokenKind.Identifier or TokenKind.RParen;

    private static ScriptDiagnostic Make(DiagnosticSeverity severity, string code, string message, int offset, int length,
        string? suggestion, string src, int[] lineStarts)
    {
        int line = Array.BinarySearch(lineStarts, offset);
        if (line < 0) line = ~line - 1;
        int lineStart = lineStarts[line];
        int lineEnd = src.IndexOf('\n', lineStart);
        if (lineEnd < 0) lineEnd = src.Length;
        var lineText = src[lineStart..lineEnd].TrimEnd('\r');
        int column = offset - lineStart;
        int caretLength = Math.Max(1, Math.Min(length, Math.Max(1, lineText.Length - column)));
        var excerpt = "  " + lineText + "\n  " + new string(' ', column) + new string('^', caretLength);
        return new ScriptDiagnostic(severity, code, message, line + 1, column + 1, length, suggestion, excerpt);
    }

    private static int[] LineStarts(string src)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < src.Length; i++) if (src[i] == '\n') starts.Add(i + 1);
        return starts.ToArray();
    }

    private static int FirstNonSpace(string src)
    {
        for (int i = 0; i < src.Length; i++) if (!char.IsWhiteSpace(src[i])) return i;
        return 0;
    }
}
