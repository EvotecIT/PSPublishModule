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
[JsonSerializable(typeof(WebNavExportResult))]
[JsonSerializable(typeof(WebScaffoldResult))]
[JsonSerializable(typeof(WebEngineLockSpec))]
[JsonSerializable(typeof(WebEngineLockResult))]
[JsonSerializable(typeof(WebToolLockSpec))]
[JsonSerializable(typeof(WebWebsiteRunnerResult))]
[JsonSerializable(typeof(WebContentScaffoldResult))]
[JsonSerializable(typeof(WebLlmsResult))]
[JsonSerializable(typeof(WebSitemapResult))]
[JsonSerializable(typeof(WebAgentReadinessResult))]
[JsonSerializable(typeof(WebAgentReadinessCheck))]
[JsonSerializable(typeof(WebApiDocsResult))]
[JsonSerializable(typeof(WebXrefMergeResult))]
[JsonSerializable(typeof(WebChangelogResult))]
[JsonSerializable(typeof(WebEcosystemStatsResult))]
[JsonSerializable(typeof(WebReleaseHubResult))]
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
[JsonSerializable(typeof(LinkServiceSpec))]
[JsonSerializable(typeof(LinkRedirectRule[]))]
[JsonSerializable(typeof(LinkShortlinkRule[]))]
[JsonSerializable(typeof(LinkValidationIssue[]))]
[JsonSerializable(typeof(LinkValidationResult))]
[JsonSerializable(typeof(WebLinkApacheExportResult))]
[JsonSerializable(typeof(WebLinkShortlinkImportResult))]
[JsonSerializable(typeof(WebLink404ReportResult))]
[JsonSerializable(typeof(WebLink404PromoteResult))]
[JsonSerializable(typeof(WebLink404IgnoreResult))]
[JsonSerializable(typeof(WebLink404ReviewResult))]
[JsonSerializable(typeof(WebLinkReviewApplyResult))]
[JsonSerializable(typeof(WebLegacyAmpRedirectResult))]
[JsonSerializable(typeof(WebLinkCommandSummary))]
internal partial class PowerForgeWebCliJsonContext : JsonSerializerContext
{
}
