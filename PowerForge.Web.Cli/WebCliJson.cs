using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge.Web.Cli;

internal static class WebCliJson
{
    internal static readonly JsonSerializerOptions Options;
    internal static readonly PowerForgeWebCliJsonContext Context;

    static WebCliJson()
    {
        Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        Context = new PowerForgeWebCliJsonContext(Options);
    }

    internal static JsonElement SerializeToElement<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.SerializeToElement(value, typeInfo);
}
