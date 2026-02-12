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
[JsonSerializable(typeof(WebContentScaffoldResult))]
[JsonSerializable(typeof(WebLlmsResult))]
[JsonSerializable(typeof(WebSitemapResult))]
[JsonSerializable(typeof(WebApiDocsResult))]
[JsonSerializable(typeof(WebChangelogResult))]
[JsonSerializable(typeof(WebPipelineResult))]
[JsonSerializable(typeof(WebMarkdownFixResult))]
[JsonSerializable(typeof(WebPublishSpec))]
[JsonSerializable(typeof(WebPublishResult))]
[JsonSerializable(typeof(WebOptimizeResult))]
[JsonSerializable(typeof(WebOptimizeHashedAssetEntry))]
[JsonSerializable(typeof(WebAuditResult))]
[JsonSerializable(typeof(WebDoctorResult))]
[JsonSerializable(typeof(WebAuditSummary))]
[JsonSerializable(typeof(WebAuditIssue))]
[JsonSerializable(typeof(WebAuditNavProfile[]))]
[JsonSerializable(typeof(WebAuditMediaProfile[]))]
[JsonSerializable(typeof(WebDotNetBuildResult))]
[JsonSerializable(typeof(WebDotNetPublishResult))]
[JsonSerializable(typeof(WebStaticOverlayResult))]
internal partial class PowerForgeWebCliJsonContext : JsonSerializerContext
{
}
