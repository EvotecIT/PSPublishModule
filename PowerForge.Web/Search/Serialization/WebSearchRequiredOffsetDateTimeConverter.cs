using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Reads ISO 8601 timestamps only when they include an explicit UTC designator or numeric offset.</summary>
public sealed class WebSearchRequiredOffsetDateTimeConverter : JsonConverter<DateTimeOffset>
{
    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Search collection timestamps must be JSON strings with an explicit UTC offset.");

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text) || !HasExplicitOffset(text) || !reader.TryGetDateTimeOffset(out var value))
            throw new JsonException("Search collection timestamps must use ISO 8601 with 'Z' or an explicit ±HH:mm offset.");

        return value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    private static bool HasExplicitOffset(string value)
    {
        if (value.EndsWith('Z') || value.EndsWith('z'))
            return true;
        if (value.Length < 6)
            return false;

        var offsetStart = value.Length - 6;
        return (value[offsetStart] == '+' || value[offsetStart] == '-') &&
               value[offsetStart + 3] == ':' &&
               char.IsDigit(value[offsetStart + 1]) &&
               char.IsDigit(value[offsetStart + 2]) &&
               char.IsDigit(value[offsetStart + 4]) &&
               char.IsDigit(value[offsetStart + 5]);
    }
}
