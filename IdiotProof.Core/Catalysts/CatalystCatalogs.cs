using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Core.Catalogs;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// Loads the two catalyst catalogs (IP-LAW-7) once per process from the shipped <c>data/</c>
/// folder (or <c>IDIOTPROOF_CATALOG_DIR</c>). Tests hand <see cref="CatalystPreClassifier"/> and
/// <see cref="CatalystScorer"/> explicit rule objects instead, so nothing here is required to be mocked.
/// </summary>
public static class CatalystCatalogs
{
    public const string PreClassFile = "catalyst-preclass.json";
    public const string ScoringFile = "catalyst-scoring.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lazy<CatalystPreClassRules> preClass = new(() => Load<CatalystPreClassRules>(PreClassFile));
    private static readonly Lazy<CatalystScoringWeights> scoring = new(() => Load<CatalystScoringWeights>(ScoringFile));

    public static CatalystPreClassRules PreClassRules => preClass.Value;

    public static CatalystScoringWeights ScoringWeights => scoring.Value;

    public static T Load<T>(string fileName, IStaticCatalogSource? source = null)
    {
        var path = (source ?? StaticCatalogSource.Default).Resolve(fileName);
        return Parse<T>(File.ReadAllText(path), path);
    }

    public static T Parse<T>(string json, string origin = "<inline>") =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"Catalyst catalog {origin} is empty.");
}

public sealed class CatalystPreClassRules
{
    public int Version { get; init; }
    public List<string> OfferingFormPrefixes { get; init; } = [];
    public EightKRules EightK { get; init; } = new();
    public List<string> ReverseSplitPhrases { get; init; } = [];
    public List<KeywordRule> KeywordRules { get; init; } = [];
    public PromoRules Promo { get; init; } = new();
    public SourceTierRules SourceTiers { get; init; } = new();

    public sealed class EightKRules
    {
        public Dictionary<string, ItemRule> Items { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ComboRule> Combos { get; init; } = [];
        public List<string> ReverseSplitItems { get; init; } = [];
        public double ReverseSplitConfidence { get; init; } = 0.85;
        public List<CatalystPreClass> Priority { get; init; } = [];
    }

    public sealed class ItemRule
    {
        public CatalystPreClass Class { get; init; }
        public int Direction { get; init; }
        public double Confidence { get; init; }
    }

    public sealed class ComboRule
    {
        public List<string> Items { get; init; } = [];
        public CatalystPreClass Class { get; init; }
        public int Direction { get; init; }
        public double Confidence { get; init; }
    }

    public sealed class KeywordRule
    {
        public CatalystPreClass Class { get; init; }
        public int Direction { get; init; }
        public double Confidence { get; init; }
        public List<string> Any { get; init; } = [];
    }

    public sealed class PromoRules
    {
        public List<string> WireSources { get; init; } = [];
        public List<string> Superlatives { get; init; } = [];
        public string SpecificNumberPattern { get; init; } = "";
        public int MinSignals { get; init; } = 2;
        public double Confidence { get; init; } = 0.6;
        public int Direction { get; init; } = 1;
    }

    public sealed class SourceTierRules
    {
        public int Default { get; init; } = 2;
        public int Wire { get; init; } = 3;
        public int Edgar { get; init; } = 1;
        public Dictionary<string, int> BySource { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class CatalystScoringWeights
{
    public int Version { get; init; }
    public Dictionary<string, double> TypePriors { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> Magnitude { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> SourceWeights { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public double NoveltyBonusMax { get; init; } = 15;
    public int NoveltyWindowDays { get; init; } = 90;
    public double FreshnessHalfLifeMinutes { get; init; } = 240;
    public double FreshnessFloor { get; init; } = 0.05;
}
