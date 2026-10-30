using System.Text;
using System.Text.RegularExpressions;

namespace IdiotProof.Operations;

/// <summary>Name conventions shared by the catalog, the binder and the front doors.</summary>
public static partial class OperationNames
{
    /// <summary>Option / argument / operation-word names: <c>max-price</c>, <c>etf2</c>.</summary>
    public static bool IsKebab(string name) => KebabRegex().IsMatch(name);

    /// <summary><c>max-price</c> → <c>max_price</c> (the MCP/JSON property name).</summary>
    public static string ToSnake(string kebab) => kebab.Replace('-', '_');

    /// <summary><c>strategy list</c> → <c>strategy_list</c>; <c>order cancel-all</c> → <c>order_cancel_all</c>.</summary>
    public static string ToToolName(string operationName) =>
        ToSnake(string.Join('_', operationName.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    /// <summary><c>MaxPrice</c> → <c>max-price</c>; <c>ETFOnly</c> → <c>etf-only</c>; <c>Top10</c> → <c>top10</c>.</summary>
    public static string ToKebab(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c))
            {
                var prevLowerOrDigit = i > 0 && (char.IsLower(pascal[i - 1]) || char.IsDigit(pascal[i - 1]));
                var acronymEnd = i > 0 && char.IsUpper(pascal[i - 1]) && i + 1 < pascal.Length && char.IsLower(pascal[i + 1]);
                if (prevLowerOrDigit || acronymEnd) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c == '_')
            {
                sb.Append('-');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Folds every spelling a caller might send — <c>max-price</c>, <c>max_price</c>, <c>maxPrice</c>,
    /// <c>MaxPrice</c> — to one key, so JSON keys match by option name, snake_case or property name.
    /// </summary>
    public static string Fold(string name) =>
        string.Concat(name.Where(c => c is not ('-' or '_'))).ToLowerInvariant();

    /// <summary>Edit distance, for "did you mean --active?" suggestions.</summary>
    public static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>The closest candidate within a small edit distance, or null.</summary>
    public static string? Suggest(string input, IEnumerable<string> candidates)
    {
        var best = candidates
            .Select(c => (Name: c, Score: Distance(input.ToLowerInvariant(), c.ToLowerInvariant())))
            .OrderBy(x => x.Score)
            .FirstOrDefault();
        return best.Name is not null && best.Score <= Math.Max(2, input.Length / 3) ? best.Name : null;
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex KebabRegex();
}
