using System.Globalization;
using System.Text.RegularExpressions;

namespace IdiotProof.Core.Research.Momentum;

/// <summary>A spoken level resolved to dollars, with the scale that was chosen and why.</summary>
public sealed record ResolvedPrice(decimal Price, string Spoken, string Scale);

/// <summary>
/// Turns a price as Momentum. says it (and as captions write it) into dollars (IP-T493). He drops the
/// decimal point: "270" is $2.70, "375" is $3.75, "56" may be $5.60, "53" on a 54-cent stock is $0.53,
/// "1250" is $12.50. Literal forms are taken as said: "$3.50", "five bucks", "10 bucks", "60 cents",
/// "a dollar 78", "a dollar and 60 cents", "a dollar fifty", "one eighty eight" ($1.88). A bare number is
/// scaled by 1, 1/10 or 1/100, whichever lands closest (in log terms) to a reference price the stock really
/// traded at (the prior close, the day's open).
/// </summary>
public static class SpokenPrice
{
    private static readonly Dictionary<string, int> Units = new(StringComparer.Ordinal)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
        ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16,
        ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new(StringComparer.Ordinal)
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    private static readonly Regex Number = new(@"\d+(?:,\d{3})*(?:\.\d+)?", RegexOptions.CultureInvariant);
    private static readonly Regex DollarNumber = new(@"\$\s*(\d+(?:,\d{3})*(?:\.\d+)?)", RegexOptions.CultureInvariant);
    private static readonly Regex VwapRx = new(@"\bvwap\b|\bv\s?wap\b|\bv\s?web\b|\bvwop\b|\bv-wap\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// Resolve <paramref name="spoken"/> against the reference prices (any that are positive). Null when the
    /// token carries no number (VWAP, "Friday high") or no reference is known for a bare number.
    /// </summary>
    public static ResolvedPrice? Resolve(string? spoken, params decimal?[] references)
    {
        if (string.IsNullOrWhiteSpace(spoken)) return null;
        var text = Regex.Replace(spoken.Trim().ToLowerInvariant().Replace('-', ' '), @"\s+", " ");

        // "$3.50", "$19 a share", "daily 200 moving average at $30": a dollar-signed number wins over any other number
        var dm = DollarNumber.Match(text);
        if (dm.Success)
        {
            var v = Parse(dm.Groups[1].Value);
            if (dm.Groups[1].Value.Contains('.')) return new ResolvedPrice(Round(v), spoken, "literal");
            return Scale(v, spoken, references, dollarSign: true);
        }

        // "a dollar 78", "a dollar and 60 cents", "one dollar fifty", "2 dollars 20"
        var dc = Regex.Match(text, @"\b(a|an|\d+|" + WordAlternation + @")\s+(?:dollars?|bucks?)\s*(?:and\s+)?(\d{1,2}|(?:" + WordAlternation + @")(?:\s+(?:" + WordAlternation + @"))?)?\b");
        if (dc.Success)
        {
            var dollars = dc.Groups[1].Value is "a" or "an" ? 1 : Words(dc.Groups[1].Value) ?? 0;
            var centsText = dc.Groups[2].Value;
            var cents = string.IsNullOrEmpty(centsText) ? 0 : Words(centsText) ?? 0;
            if (cents is >= 0 and < 100) return new ResolvedPrice(Round(dollars + cents / 100.0), spoken, cents == 0 ? "dollars" : "dollars+cents");
        }

        bool bucks = Regex.IsMatch(text, @"\b(bucks?|dollars?)\b");
        bool cents2 = Regex.IsMatch(text, @"\bcents?\b");

        var m = Number.Match(text);
        if (m.Success)
        {
            var v = Parse(m.Value);
            if (cents2) return new ResolvedPrice(Round(v / 100.0), spoken, "cents");
            if (m.Value.Contains('.')) return new ResolvedPrice(Round(v), spoken, "literal");
            if (bucks) return new ResolvedPrice(Round(v), spoken, "dollars");
            return Scale(v, spoken, references, dollarSign: false);
        }

        // words only: "one eighty eight" = 1.88, "two fifty" = 2.50, "five" = 5, "thirty five" = 35 (then scaled)
        var tokens = text.Split(' ').Where(t => Units.ContainsKey(t) || Tens.ContainsKey(t)).ToList();
        if (tokens.Count == 0) return null;
        if (cents2) return Words(string.Join(' ', tokens)) is { } c ? new ResolvedPrice(Round(c / 100.0), spoken, "cents") : null;
        if (tokens.Count >= 2 && Units.TryGetValue(tokens[0], out var head) && head is >= 1 and <= 19 && Words(string.Join(' ', tokens.Skip(1))) is { } tail and >= 10 and < 100)
            return new ResolvedPrice(Round(head + tail / 100.0), spoken, "dollars+cents");
        if (Words(string.Join(' ', tokens)) is not { } w) return null;
        if (bucks) return new ResolvedPrice(Round(w), spoken, "dollars");
        if (w < 10) return new ResolvedPrice(Round(w), spoken, "x1");
        return Scale(w, spoken, references, dollarSign: false);
    }

    /// <summary>
    /// A bare integer: try x1, x0.1 and x0.01 and keep the one closest to a reference. A "$" in front makes x1 the strong
    /// prior ("$5" = five dollars), but a caption "$270" next to a $2.40 stock is still $2.70.
    /// </summary>
    private static ResolvedPrice? Scale(double v, string spoken, decimal?[] references, bool dollarSign)
    {
        var refs = references.Where(r => r is > 0).Select(r => (double)r!.Value).ToList();
        if (refs.Count == 0) return dollarSign || v < 10 ? new ResolvedPrice(Round(v), spoken, "x1") : null;
        var candidates = new List<(double Price, string Scale, double Penalty)> { (v, "x1", dollarSign ? 0 : 0.05) };
        if (v >= 10) candidates.Add((v / 10, "/10", dollarSign ? 0.4 : 0.05));
        if (v >= 10) candidates.Add((v / 100, "/100", dollarSign ? 0.4 : v >= 100 ? 0 : 0.1));
        var best = candidates.Where(c => c.Price > 0)
            .Select(c => (c.Price, c.Scale, Score: refs.Min(r => Math.Abs(Math.Log(c.Price / r))) + c.Penalty))
            .OrderBy(c => c.Score)
            .First();
        return new ResolvedPrice(Round(best.Price), spoken, best.Scale);
    }

    /// <summary>The chart level a spoken token names instead of a number (VWAP, Friday's high), or null.</summary>
    public static string? Keyword(string? spoken)
    {
        if (string.IsNullOrWhiteSpace(spoken)) return null;
        var t = spoken.Trim().ToLowerInvariant();
        bool hasNumber = Number.IsMatch(t) || t.Split(' ', '-').Any(w => Units.ContainsKey(w) || Tens.ContainsKey(w)) || Regex.IsMatch(t, @"\ba dollar\b");
        if (VwapRx.IsMatch(t) && !hasNumber) return "vwap";
        if (hasNumber) return null;
        if (Regex.IsMatch(t, @"premarket high|pre-market high|\bpmh\b")) return "premarket-high";
        if (Regex.IsMatch(t, @"(prior|previous|yesterday|friday|monday|tuesday|wednesday|thursday).{0,16}high|\bpdh\b")) return "prior-day-high";
        if (Regex.IsMatch(t, @"(prior|previous|yesterday).{0,12}close|\bpdc\b")) return "prior-close";
        if (Regex.IsMatch(t, @"high of day|\bhod\b|new highs?|\bhighs\b")) return "high-of-day";
        return null;
    }

    /// <summary>True when a level mentions VWAP even beside a number ("a dollar 50 VWAP").</summary>
    public static bool MentionsVwap(string? spoken) => !string.IsNullOrWhiteSpace(spoken) && VwapRx.IsMatch(spoken.ToLowerInvariant());

    private static string WordAlternation => string.Join('|', Units.Keys.Concat(Tens.Keys));

    /// <summary>"seventy eight" = 78, "fifty" = 50, "12" = 12; null when the text is not a number.</summary>
    private static int? Words(string s)
    {
        s = s.Trim();
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
        int total = 0; bool any = false;
        foreach (var w in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Units.TryGetValue(w, out var u)) { total += u; any = true; }
            else if (Tens.TryGetValue(w, out var t)) { total += t; any = true; }
            else return any ? total : null;
        }
        return any ? total : null;
    }

    private static double Parse(string s) => double.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture);

    private static decimal Round(double v) => Math.Round((decimal)v, v < 1 ? 4 : 2, MidpointRounding.AwayFromZero);
}
