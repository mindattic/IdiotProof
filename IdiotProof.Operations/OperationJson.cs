using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Operations;

/// <summary>
/// The JSON conventions of every operation surface (Hub API, <c>--json</c>, MCP results, the
/// catalog): snake_case property names (matching MCP tool input), enums as kebab-case strings,
/// and every <see cref="DateTime"/> written as an ISO instant carrying the Eastern offset
/// (<c>2026-10-30T09:31:00-04:00</c>) — unambiguous for machines, and already ET when an agent
/// quotes it to a person.
/// </summary>
public static class OperationJson
{
    public static readonly JsonSerializerOptions Options = Create(indented: false);

    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = null,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        options.Converters.Add(new EasternDateTimeConverter());
        options.Converters.Add(new EasternDateTimeOffsetConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Writes UTC instants with the ET offset; reads any ISO instant back as UTC.</summary>
    private sealed class EasternDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture).UtcDateTime;

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(EasternClock.ToEasternOffset(value).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture));
    }

    private sealed class EasternDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(EasternClock.ToEasternOffset(value.UtcDateTime).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture));
    }
}
