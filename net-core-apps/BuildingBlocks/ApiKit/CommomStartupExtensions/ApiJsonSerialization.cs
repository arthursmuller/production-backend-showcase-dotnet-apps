using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommomStartupExtensions;

/// <summary>
/// Defines the shared System.Text.Json contract used by MVC controllers and Minimal APIs.
/// </summary>
public static class ApiJsonSerialization
{
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        AddIfMissing(
            options,
            new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        AddIfMissing(options, new UtcIso8601DateTimeConverter());
        AddIfMissing(options, new UtcIso8601DateTimeOffsetConverter());
    }

    private static void AddIfMissing(JsonSerializerOptions options, JsonConverter converter)
    {
        if (options.Converters.All(existing => existing.GetType() != converter.GetType()))
            options.Converters.Add(converter);
    }
}

/// <summary>
/// Reads ISO-8601 DateTime strings and writes their UTC instant with seven fractional digits.
/// </summary>
public class UtcIso8601DateTimeConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A DateTime JSON string is required.");

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value)
            || !DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw new JsonException("A valid ISO-8601 DateTime string is required.");
        }

        return parsed.Kind switch
        {
            DateTimeKind.Utc => parsed,
            DateTimeKind.Local => parsed.ToUniversalTime(),
            _ => DateTime.SpecifyKind(parsed, DateTimeKind.Utc),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTime value,
        JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        writer.WriteStringValue(utc.ToString(Format, CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Reads ISO-8601 DateTimeOffset strings and writes their UTC instant with seven fractional digits.
/// </summary>
public class UtcIso8601DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A DateTimeOffset JSON string is required.");

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value)
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw new JsonException("A valid ISO-8601 DateTimeOffset string is required.");
        }

        return parsed;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(
            value.ToUniversalTime().UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));
}
