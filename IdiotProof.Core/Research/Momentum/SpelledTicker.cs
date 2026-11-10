using System.Text.RegularExpressions;

namespace IdiotProof.Core.Research.Momentum;

/// <summary>
/// Tickers as YouTube captions write them (IP-T493). Momentum. often spells a ticker out ("P A R A",
/// "P.A.R.A.", "P-A-R-A"), says it as a word ("CHSN") or the captions mishear it ("CHS"). This class
/// finds spelled-out tickers in running text, normalises one spoken form to a candidate symbol, and lists
/// the near variants the resolver checks against real assets and that day's movers.
/// </summary>
public static class SpelledTicker
{
    // two to five single capital letters separated by a space, dot or hyphen (with an optional trailing dot)
    private static readonly Regex Spelled = new(@"(?<![A-Za-z])((?:[A-Z](?:[\.\-]\s*|\s+)){1,4}[A-Z])\.?(?![A-Za-z])", RegexOptions.CultureInvariant);
    private static readonly Regex Dotted = new(@"(?<![A-Za-z])((?:[A-Z]\.){2,5})(?![A-Za-z])", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> NotTickers = new(StringComparer.Ordinal) { "IA", "US", "OK", "TV" };

    /// <summary>Every spelled-out ticker in <paramref name="text"/>, in order, joined ("P A R A" → "PARA").</summary>
    public static IReadOnlyList<string> FindSpelled(string? text)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(text)) return found;
        foreach (Match m in Spelled.Matches(text))
        {
            var raw = Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim();
            var joined = Join(raw);
            if (NotTickers.Contains(joined) || found.Contains(joined)) continue;
            if (joined.Length is >= 2 and <= 5) found.Add(joined);
        }
        foreach (Match m in Dotted.Matches(text))
        {
            var joined = Join(m.Groups[1].Value);
            if (joined.Length is >= 2 and <= 5 && !found.Contains(joined) && !NotTickers.Contains(joined)) found.Add(joined);
        }
        return found;
    }

    /// <summary>
    /// One spoken ticker as a candidate symbol: "P A R A" → "PARA", "$chsn" → "CHSN", "P.A.R.A." → "PARA",
    /// "BRK.B" → "BRK.B". Null when nothing ticker-shaped is left (more than five letters, digits only).
    /// </summary>
    public static string? Normalize(string? spoken)
    {
        if (string.IsNullOrWhiteSpace(spoken)) return null;
        var s = spoken.Trim().TrimStart('$').ToUpperInvariant();
        // a class share keeps its dot: BRK.B, but only when the part before it is a whole word
        var classShare = Regex.Match(s, @"^([A-Z]{1,4})\.([A-Z])$");
        if (classShare.Success) return classShare.Groups[1].Value + "." + classShare.Groups[2].Value;
        var letters = Join(s);
        return letters.Length is >= 1 and <= 5 ? letters : null;
    }

    /// <summary>
    /// Near variants of a candidate for misheard captions: the <paramref name="known"/> symbols one edit away, a letter
    /// added ("CHS" → "CHSN", "OEG" → "ONEG"), dropped (at least two letters left) or swapped for another (three
    /// letters or more). The caller scores them against real data.
    /// </summary>
    public static IReadOnlyList<string> Variants(string candidate, IEnumerable<string> known)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(candidate)) return result;
        foreach (var k in known)
        {
            if (k == candidate || result.Contains(k)) continue;
            if (k.Length == candidate.Length + 1 && OneInsert(candidate, k)) result.Add(k);
            else if (k.Length == candidate.Length - 1 && k.Length >= 2 && OneInsert(k, candidate)) result.Add(k);
            else if (k.Length == candidate.Length && candidate.Length >= 3 && Diff(k, candidate) == 1) result.Add(k);
        }
        return result;
    }

    /// <summary>True when <paramref name="longer"/> is <paramref name="shorter"/> with one letter inserted somewhere.</summary>
    private static bool OneInsert(string shorter, string longer)
    {
        int i = 0;
        while (i < shorter.Length && shorter[i] == longer[i]) i++;
        return string.CompareOrdinal(shorter, i, longer, i + 1, shorter.Length - i) == 0;
    }

    private static string Join(string s) => new(s.Where(char.IsLetter).Select(char.ToUpperInvariant).ToArray());

    private static int Diff(string a, string b)
    {
        int d = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) d++;
        return d;
    }
}
