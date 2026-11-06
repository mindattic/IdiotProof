using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Core.Catalogs;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>
/// The ET decision times the live recorder snapshots at, read from <c>formation-schedule.json</c>
/// (a copy of the study's <c>decisionTimesEt</c>, IP-LAW-7). Entries that are not a plain
/// <c>HH:mm</c> (the study's <c>D-1 16:00</c> reference) are skipped.
/// </summary>
public sealed class FormationSchedule
{
    public const string FileName = "formation-schedule.json";

    public FormationSchedule(IEnumerable<TimeSpan> decisionTimesEt)
    {
        DecisionTimesEt = decisionTimesEt.Distinct().OrderBy(t => t).ToList();
    }

    public IReadOnlyList<TimeSpan> DecisionTimesEt { get; }

    public static string Label(TimeSpan timeEt) => timeEt.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    public static FormationSchedule Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<Document>(json, Options)
            ?? throw new InvalidDataException("formation-schedule.json is empty.");
        var times = new List<TimeSpan>();
        foreach (var text in doc.DecisionTimesEt ?? [])
            if (TimeSpan.TryParseExact(text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var t)) times.Add(t);
        if (times.Count == 0) throw new InvalidDataException("formation-schedule.json names no HH:mm decision times.");
        return new FormationSchedule(times);
    }

    public static FormationSchedule Load(IStaticCatalogSource? source = null) =>
        Parse(File.ReadAllText((source ?? StaticCatalogSource.Default).Resolve(FileName)));

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class Document
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("decisionTimesEt")] public List<string>? DecisionTimesEt { get; set; }
    }
}
