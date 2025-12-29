using System.Text.Json;
using PowerForge;

namespace PowerForge.Cli;

internal static class CliJson
{
    internal static readonly JsonSerializerOptions Options;
    internal static readonly PowerForgeCliJsonContext Context;

    static CliJson()
    {
        Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        // Polymorphic segments in ModulePipelineSpec (IConfigurationSegment[]).
        Options.Converters.Add(new ConfigurationSegmentJsonConverter());

        // Encapsulates the options with the source-generated context (also makes Options read-only).
        Context = new PowerForgeCliJsonContext(Options);
    }

    internal static T DeserializeOrThrow<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, string fullPath)
    {
        var obj = JsonSerializer.Deserialize(json, typeInfo);
        if (obj is null)
            throw new InvalidOperationException($"Failed to deserialize config file: {fullPath}");
        return obj;
    }

    internal static JsonElement SerializeToElement<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.SerializeToElement(value, typeInfo);

    internal static JsonElement SerializeToElement<T>(T[] value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T[]> typeInfo)
        => JsonSerializer.SerializeToElement(value, typeInfo);
}
