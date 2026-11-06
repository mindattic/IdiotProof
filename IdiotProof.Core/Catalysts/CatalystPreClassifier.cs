using System.Text.RegularExpressions;

namespace IdiotProof.Core.Catalysts;

/// <summary>What the rule tables said about one item: class, price direction (-1/0/+1), confidence 0..1 and which rule fired.</summary>
public readonly record struct CatalystPreClassification(CatalystPreClass Class, int Direction, double Confidence, string Rule);

/// <summary>
/// Deterministic, LLM-free catalyst classification from <c>catalyst-preclass.json</c> (IP-T443):
/// offering form types, 8-K item codes, ordered keyword rules, then the promotional-wire test.
/// The same code runs live and over history, so the gapper study and the Monitor agree.
/// </summary>
public static class CatalystPreClassifier
{
    public static CatalystPreClassification Classify(string? headline, string? summary, string? formType = null,
        IReadOnlyList<string>? itemCodes = null, string? source = null) =>
        Classify(CatalystCatalogs.PreClassRules, headline, summary, formType, itemCodes, source);

    public static CatalystPreClassification Classify(CatalystPreClassRules rules, string? headline, string? summary,
        string? formType = null, IReadOnlyList<string>? itemCodes = null, string? source = null)
    {
        var raw = $"{headline} {summary}".Trim();
        var text = $" {Regex.Replace(raw, @"\s+", " ").ToLowerInvariant()} ";

        if (!string.IsNullOrWhiteSpace(formType))
        {
            var form = formType.Trim().ToUpperInvariant();
            if (rules.OfferingFormPrefixes.Any(p => form.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                return new(CatalystPreClass.OfferingDilution, -1, 0.9, $"form {form}");
        }

        var fromItems = FromItems(rules, itemCodes, text);
        var fromKeywords = FromKeywords(rules, text);
        var best = Pick(fromItems, fromKeywords);
        if (best is { Class: not CatalystPreClass.Other } hit) return hit;

        if (Promo(rules, raw, text, source) is { } promo) return promo;
        return best ?? new(CatalystPreClass.Other, 0, 0, "no rule matched");
    }

    /// <summary>1 primary (EDGAR), 2 editorial, 3 wire/promotional, per the catalog's source table.</summary>
    public static int SourceTier(CatalystPreClassRules rules, string? sourceName, bool isEdgar)
    {
        if (isEdgar) return rules.SourceTiers.Edgar;
        if (string.IsNullOrWhiteSpace(sourceName)) return rules.SourceTiers.Default;
        if (rules.SourceTiers.BySource.TryGetValue(sourceName.Trim(), out var tier)) return tier;
        return IsWire(rules, sourceName) ? rules.SourceTiers.Wire : rules.SourceTiers.Default;
    }

    public static bool IsWire(CatalystPreClassRules rules, string? sourceName) =>
        !string.IsNullOrWhiteSpace(sourceName)
        && rules.Promo.WireSources.Any(w => sourceName.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static CatalystPreClassification? Pick(CatalystPreClassification? a, CatalystPreClassification? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return b.Value.Confidence > a.Value.Confidence ? b : a;
    }

    private static CatalystPreClassification? FromItems(CatalystPreClassRules rules, IReadOnlyList<string>? itemCodes, string text)
    {
        if (itemCodes is not { Count: > 0 }) return null;
        var items = itemCodes.Select(i => i.Trim()).Where(i => i.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hits = new List<CatalystPreClassification>();

        foreach (var combo in rules.EightK.Combos)
            if (combo.Items.Count > 0 && combo.Items.All(items.Contains))
                hits.Add(new(combo.Class, combo.Direction, combo.Confidence, $"items {string.Join("+", combo.Items)}"));

        foreach (var item in items)
            if (rules.EightK.Items.TryGetValue(item, out var rule))
                hits.Add(new(rule.Class, rule.Direction, rule.Confidence, $"item {item}"));

        var splitItems = items.Where(i => rules.EightK.ReverseSplitItems.Contains(i, StringComparer.OrdinalIgnoreCase)).ToList();
        if (splitItems.Count > 0)
        {
            var wording = rules.ReverseSplitPhrases.FirstOrDefault(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));
            hits.Add(wording is null
                ? new(CatalystPreClass.Other, 0, 0.3, $"item {splitItems[0]} without reverse-split wording")
                : new(CatalystPreClass.ReverseSplit, -1, rules.EightK.ReverseSplitConfidence, $"item {splitItems[0]} + \"{wording}\""));
        }

        if (hits.Count == 0) return null;
        foreach (var cls in rules.EightK.Priority)
            if (hits.FirstOrDefault(h => h.Class == cls) is { Rule.Length: > 0 } ranked) return ranked;
        return hits.OrderByDescending(h => h.Confidence).First();
    }

    private static CatalystPreClassification? FromKeywords(CatalystPreClassRules rules, string text)
    {
        foreach (var rule in rules.KeywordRules)
        {
            var phrase = rule.Any.FirstOrDefault(p => p.Length > 0 && text.Contains(p, StringComparison.OrdinalIgnoreCase));
            if (phrase is not null) return new(rule.Class, rule.Direction, rule.Confidence, $"\"{phrase.Trim()}\"");
        }
        return null;
    }

    private static CatalystPreClassification? Promo(CatalystPreClassRules rules, string raw, string text, string? source)
    {
        var promo = rules.Promo;
        var superlatives = promo.Superlatives.Where(s => text.Contains(s, StringComparison.OrdinalIgnoreCase)).Take(3).ToList();
        var exclamations = Math.Min(2, raw.Count(c => c == '!'));
        var shouting = Math.Min(2, Regex.Matches(raw, @"\b[A-Z]{5,}\b").Count);
        var signals = superlatives.Count + exclamations + shouting;
        if (promo.SpecificNumberPattern.Length > 0 && Regex.IsMatch(text, promo.SpecificNumberPattern, RegexOptions.IgnoreCase)) signals--;

        // A wire byline is itself a signal: Benzinga re-posts the same release under its own name.
        var required = IsWire(rules, source) ? promo.MinSignals : promo.MinSignals + 1;
        if (signals < required || superlatives.Count == 0) return null;
        return new(CatalystPreClass.PromoPr, promo.Direction, promo.Confidence,
            $"promo: {string.Join(", ", superlatives)}{(exclamations > 0 ? ", exclamation" : "")}{(shouting > 0 ? ", caps" : "")}");
    }
}
