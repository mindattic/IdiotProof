using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>The population sample of amendment 01: which non-candidate universe days are drawn, and the weight each stands for.</summary>
public sealed class PopulationSampleSpec
{
    public double Rate { get; init; }
    public double Weight { get; init; }
    public string HashInput { get; init; } = "";
    public string Salt { get; init; } = "";
}

/// <summary>One amended hypothesis: the frozen hypothesis it re-asks over the population, with its own registration fields.</summary>
public sealed class AmendedHypothesis
{
    public string Id { get; init; } = "";
    public string BasedOn { get; init; } = "";
    public bool Primary { get; init; }
    public string Family { get; init; } = "";
    public string Claim { get; init; } = "";
    public string? Population { get; init; }
    public string? Method { get; init; }
    public Dictionary<string, JsonElement> MinEffect { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> SplitsAllowed { get; init; } = [];
}

/// <summary>
/// <c>amendment-01.json</c> (IP-T448): registered before any test on study data, it adds a seeded
/// uniform sample of non-candidate universe days and the population-weighted hypotheses H1p, H9p,
/// H11p and H12p. The frozen files are not touched; without the amendment file every stage behaves
/// exactly as before.
/// </summary>
public sealed class GapperAmendment
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public string Id { get; init; } = "";
    public int Version { get; init; }
    public DateTime? RegisteredUtc { get; init; }
    public string? Ticket { get; init; }
    public PopulationSampleSpec PopulationSample { get; init; } = new();
    public List<AmendedHypothesis> Hypotheses { get; init; } = [];

    [JsonIgnore] public string Sha256 { get; private set; } = "";

    /// <summary>The integer weight of a sample row (1/rate).</summary>
    [JsonIgnore] public int SampleWeight { get; private set; }

    public static GapperAmendment? Load(GapperStudyPaths paths) => File.Exists(paths.AmendmentFile) ? Parse(File.ReadAllText(paths.AmendmentFile)) : null;

    public static GapperAmendment Parse(string json)
    {
        var a = JsonSerializer.Deserialize<GapperAmendment>(json, Options) ?? throw new InvalidDataException("amendment file is empty.");
        var spec = a.PopulationSample;
        if (spec.Rate is <= 0 or > 1) throw new InvalidDataException($"populationSample.rate {spec.Rate} is not in (0, 1].");
        var weight = Math.Round(1 / spec.Rate);
        if (Math.Abs(spec.Weight - weight) > 1e-9 || Math.Abs(1 / spec.Rate - weight) > 1e-9)
            throw new InvalidDataException($"populationSample.weight {spec.Weight} must equal 1/rate and be an integer.");
        a.SampleWeight = (int)weight;
        a.Sha256 = GapperStudyParams.Sha256(json);
        return a;
    }

    /// <summary>Whether a symbol-day is drawn: the first 8 hex digits of the hash, as a fraction of 2^32, below the rate. Callers apply eligibility (not excluded, not a candidate).</summary>
    public bool IsSampleMember(string symbol, DateOnly d) => Draw(PopulationSample, symbol, d) < PopulationSample.Rate;

    public static double Draw(PopulationSampleSpec spec, string symbol, DateOnly d)
    {
        var input = spec.HashInput
            .Replace("{symbol}", symbol.ToUpperInvariant(), StringComparison.Ordinal)
            .Replace("{D:yyyy-MM-dd}", d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{salt}", spec.Salt, StringComparison.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BinaryPrimitives.ReadUInt32BigEndian(hash) / 4294967296.0;
    }

    public AmendedHypothesis? Find(string id) => Hypotheses.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The amended hypothesis as a runnable hypothesis: the frozen one's feature, label, time and test, with the amendment's id, claim, minimum effects and splits.</summary>
    public GapperHypothesis Resolve(AmendedHypothesis a, GapperHypothesisSet frozen)
    {
        var b = frozen.Find(a.BasedOn);
        return new GapperHypothesis
        {
            Id = a.Id, Primary = a.Primary, Family = a.Family, Claim = a.Claim, Feature = b.Feature, Label = b.Label, DecisionTime = b.DecisionTime,
            Test = b.Test, MinEffect = a.MinEffect, SplitsAllowed = a.SplitsAllowed, Notes = $"amendment {Id}: {a.Method}",
        };
    }
}
