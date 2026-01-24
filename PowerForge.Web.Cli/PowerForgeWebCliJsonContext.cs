using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(SiteSpec))]
[JsonSerializable(typeof(ProjectSpec))]
[JsonSerializable(typeof(WebSitePlan))]
[JsonSerializable(typeof(WebBuildResult))]
[JsonSerializable(typeof(WebVerifyResult))]
[JsonSerializable(typeof(WebScaffoldResult))]
internal partial class PowerForgeWebCliJsonContext : JsonSerializerContext
{
}
