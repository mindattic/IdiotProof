using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>A member of U(D) that is not a candidate on D, in its matching cell.</summary>
public sealed record ControlCandidate(string Symbol, int AdvDecile, int PriceTier);

/// <summary>A gapper day that needs a control.</summary>
public sealed record GapperForControl(string Symbol, int AdvDecile, int PriceTier);

/// <summary>
/// The frozen control rule (protocol §Controls): same day, same ADV20$ decile and price tier, the
/// member with the smallest SHA-256 of <c>{symbol}|{D:yyyy-MM-dd}|{salt}</c>; empty cell → nearest
/// decile at the same price tier, then nearest price tier. Gappers sharing a cell take members in
/// ascending hash order so the first gets the smallest hash exactly as written and no two gappers
/// share one control. Deterministic in (D, cell membership) and never re-drawn.
/// </summary>
public static class GapperControlPicker
{
    public static string Hash(GapperStudyParams p, string symbol, DateOnly d)
    {
        var input = p.Controls.HashInput
            .Replace("{symbol}", symbol.ToUpperInvariant(), StringComparison.Ordinal)
            .Replace("{D:yyyy-MM-dd}", d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{salt}", p.Controls.Salt, StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    /// <summary>Gapper symbol → control symbol for one day; gappers that found no member are absent.</summary>
    public static Dictionary<string, string> Assign(GapperStudyParams p, DateOnly d,
        IReadOnlyList<GapperForControl> gappers, IReadOnlyList<ControlCandidate> pool)
    {
        var byCell = pool
            .GroupBy(c => (c.AdvDecile, c.PriceTier))
            .ToDictionary(g => g.Key, g => g.OrderBy(c => Hash(p, c.Symbol, d), StringComparer.Ordinal).ThenBy(c => c.Symbol, StringComparer.Ordinal).Select(c => c.Symbol).ToList());
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var tierCount = Math.Max(1, p.Controls.PriceTiers.Count);

        foreach (var g in gappers.OrderBy(g => g.Symbol, StringComparer.Ordinal))
        {
            string? pick = null;
            foreach (var cell in SearchOrder(g.AdvDecile, g.PriceTier, tierCount))
            {
                if (!byCell.TryGetValue(cell, out var members)) continue;
                pick = members.FirstOrDefault(m => !used.Contains(m) && m != g.Symbol);
                if (pick is not null) break;
            }
            if (pick is null) continue;
            used.Add(pick);
            result[g.Symbol] = pick;
        }
        return result;
    }

    /// <summary>Same cell, then nearest decile at the same tier (lower first on ties), then nearest tier with the same rule inside it.</summary>
    private static IEnumerable<(int Decile, int Tier)> SearchOrder(int decile, int tier, int tierCount)
    {
        foreach (var t in Ordered(tier, 0, tierCount - 1))
            foreach (var dec in Ordered(decile, 0, 9))
                yield return (dec, t);
    }

    private static IEnumerable<int> Ordered(int center, int min, int max)
    {
        yield return center;
        for (int dist = 1; dist <= max - min; dist++)
        {
            if (center - dist >= min) yield return center - dist;
            if (center + dist <= max) yield return center + dist;
        }
    }
}
