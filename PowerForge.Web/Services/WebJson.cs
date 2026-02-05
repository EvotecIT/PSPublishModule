using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge.Web;

internal static class WebJson
{
    internal static readonly JsonSerializerOptions Options;

    static WebJson()
    {
        Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };
        Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }
}
