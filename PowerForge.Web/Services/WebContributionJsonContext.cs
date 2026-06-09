using System.Text.Json.Serialization;

namespace PowerForge.Web;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(WebContributionAuthorCatalog))]
internal partial class WebContributionJsonContext : JsonSerializerContext
{
}
