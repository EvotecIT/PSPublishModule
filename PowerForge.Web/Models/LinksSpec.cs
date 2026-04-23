using System;
using System.Collections.Generic;

namespace PowerForge.Web;

/// <summary>Configures reusable redirect, shortlink, and 404 workflow data for a site.</summary>
public sealed class LinkServiceSpec
{
    /// <summary>Path to committed redirect rules JSON.</summary>
    public string? Redirects { get; set; }
    /// <summary>Path to committed shortlink rules JSON.</summary>
    public string? Shortlinks { get; set; }
    /// <summary>Path to committed ignored 404 JSON.</summary>
    public string? Ignored404 { get; set; }
    /// <summary>Path to committed link group metadata JSON.</summary>
    public string? Groups { get; set; }
    /// <summary>Compatibility CSV inputs such as legacy WordPress redirect maps.</summary>
    public string[] RedirectCsvPaths { get; set; } = Array.Empty<string>();
    /// <summary>Apache include output path.</summary>
    public string? ApacheOut { get; set; }
    /// <summary>Named host aliases, for example en, pl, or short.</summary>
    public Dictionary<string, string> Hosts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Host-to-language-prefix map for domains where a language is deployed at the web root, for example evotec.pl =&gt; pl.</summary>
    public Dictionary<string, string> LanguageRootHosts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Match strategy for link-service redirects.</summary>
public enum LinkRedirectMatchType
{
    /// <summary>Match one normalized path, allowing an optional trailing slash.</summary>
    Exact,
    /// <summary>Match a path prefix and optionally carry the suffix into the target.</summary>
    Prefix,
    /// <summary>Match using a host runtime regular expression.</summary>
    Regex,
    /// <summary>Match a path plus query string condition.</summary>
    Query
}

/// <summary>Defines an engine-owned redirect rule for static export or dynamic serving.</summary>
public sealed class LinkRedirectRule
{
    /// <summary>Stable identifier used in reports and admin workflows.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>When false, the rule is ignored by validation/export.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Optional host scope such as evotec.xyz, evotec.pl, evo.yt, or *.</summary>
    public string? SourceHost { get; set; }
    /// <summary>Source path or regex pattern, normalized as a root-relative path for non-regex rules.</summary>
    public string SourcePath { get; set; } = string.Empty;
    /// <summary>Optional exact query string match without a leading question mark.</summary>
    public string? SourceQuery { get; set; }
    /// <summary>Source matching strategy.</summary>
    public LinkRedirectMatchType MatchType { get; set; } = LinkRedirectMatchType.Exact;
    /// <summary>Absolute URL or root-relative path target; optional only for 410 Gone rules.</summary>
    public string TargetUrl { get; set; } = string.Empty;
    /// <summary>HTTP status code to emit.</summary>
    public int Status { get; set; } = 301;
    /// <summary>When true, preserve the incoming query string if the rule does not already match a source query.</summary>
    public bool PreserveQuery { get; set; }
    /// <summary>Ordering hint; higher priority rules are emitted earlier.</summary>
    public int Priority { get; set; }
    /// <summary>Logical grouping, for example legacy-wordpress, amp, manual, or campaign.</summary>
    public string? Group { get; set; }
    /// <summary>Origin of the rule, for example manual, generated, imported-wordpress, or 404-promoted.</summary>
    public string? Source { get; set; }
    /// <summary>Human-readable review note.</summary>
    public string? Notes { get; set; }
    /// <summary>Allows redirects to absolute external HTTP/HTTPS targets.</summary>
    public bool AllowExternal { get; set; }
    /// <summary>Creation timestamp, preferably ISO 8601.</summary>
    public string? CreatedAt { get; set; }
    /// <summary>Update timestamp, preferably ISO 8601.</summary>
    public string? UpdatedAt { get; set; }
    /// <summary>Creator identifier for audit/review workflows.</summary>
    public string? CreatedBy { get; set; }
    /// <summary>Updater identifier for audit/review workflows.</summary>
    public string? UpdatedBy { get; set; }
    /// <summary>Optional expiry timestamp for temporary redirects.</summary>
    public string? ExpiresAt { get; set; }
    /// <summary>Resolved source file for imported/generated diagnostics.</summary>
    public string? OriginPath { get; set; }
    /// <summary>One-based source line for imported/generated diagnostics.</summary>
    public int OriginLine { get; set; }
}

/// <summary>Defines a reusable branded shortlink.</summary>
public sealed class LinkShortlinkRule
{
    /// <summary>URL-safe short slug.</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary>Optional host scope, such as evo.yt.</summary>
    public string? Host { get; set; }
    /// <summary>Optional path prefix; defaults to /go unless the host is configured as the short host.</summary>
    public string? PathPrefix { get; set; }
    /// <summary>Explicit shortlink destination.</summary>
    public string TargetUrl { get; set; } = string.Empty;
    /// <summary>HTTP status code; campaign/share links usually use 302.</summary>
    public int Status { get; set; } = 302;
    /// <summary>Friendly label for admin/search reports.</summary>
    public string? Title { get; set; }
    /// <summary>Optional context for maintainers.</summary>
    public string? Description { get; set; }
    /// <summary>Optional grouping tags.</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
    /// <summary>Owning person, team, or project.</summary>
    public string? Owner { get; set; }
    /// <summary>Optional UTM query template appended during export.</summary>
    public string? Utm { get; set; }
    /// <summary>Origin of the rule, for example manual, imported-pretty-links, or campaign.</summary>
    public string? Source { get; set; }
    /// <summary>Human-readable review note.</summary>
    public string? Notes { get; set; }
    /// <summary>Imported historical hit/click count from a previous shortlink system.</summary>
    public int ImportedHits { get; set; }
    /// <summary>When false, the shortlink is ignored by validation/export.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Allows absolute external HTTP/HTTPS targets.</summary>
    public bool AllowExternal { get; set; }
    /// <summary>Creation timestamp, preferably ISO 8601.</summary>
    public string? CreatedAt { get; set; }
    /// <summary>Update timestamp, preferably ISO 8601.</summary>
    public string? UpdatedAt { get; set; }
    /// <summary>Last target health-check timestamp, preferably ISO 8601.</summary>
    public string? LastCheckedAt { get; set; }
    /// <summary>Resolved source file for imported/generated diagnostics.</summary>
    public string? OriginPath { get; set; }
    /// <summary>One-based source line for imported/generated diagnostics.</summary>
    public int OriginLine { get; set; }
}

/// <summary>Defines a 404 observation that should be ignored by review workflows.</summary>
public sealed class Ignored404Rule
{
    /// <summary>Ignored path or pattern.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Optional host scope.</summary>
    public string? Host { get; set; }
    /// <summary>Reason this 404 should not create review noise.</summary>
    public string? Reason { get; set; }
    /// <summary>Creation timestamp, preferably ISO 8601.</summary>
    public string? CreatedAt { get; set; }
    /// <summary>Creator identifier for review workflows.</summary>
    public string? CreatedBy { get; set; }
}

/// <summary>Metadata for grouping redirect and shortlink records.</summary>
public sealed class LinkGroupSpec
{
    /// <summary>Stable group identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Human-readable group title.</summary>
    public string? Title { get; set; }
    /// <summary>Optional group description.</summary>
    public string? Description { get; set; }
    /// <summary>Owning person, team, or project.</summary>
    public string? Owner { get; set; }
}

/// <summary>Severity for link-service validation issues.</summary>
public enum LinkValidationSeverity
{
    /// <summary>Informational issue.</summary>
    Info,
    /// <summary>Reviewable warning.</summary>
    Warning,
    /// <summary>Blocking validation error.</summary>
    Error
}

/// <summary>One validation issue produced by the link service.</summary>
public sealed class LinkValidationIssue
{
    /// <summary>Issue severity.</summary>
    public LinkValidationSeverity Severity { get; set; }
    /// <summary>Stable diagnostic code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Human-readable issue message.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Rule source, such as redirect or shortlink.</summary>
    public string? Source { get; set; }
    /// <summary>Rule identifier or slug.</summary>
    public string? Id { get; set; }
    /// <summary>Related rule identifier, used for conflicts.</summary>
    public string? RelatedId { get; set; }
    /// <summary>Source host involved in the issue.</summary>
    public string? SourceHost { get; set; }
    /// <summary>Source path involved in the issue.</summary>
    public string? SourcePath { get; set; }
    /// <summary>Source query involved in the issue.</summary>
    public string? SourceQuery { get; set; }
    /// <summary>Target URL involved in the issue.</summary>
    public string? TargetUrl { get; set; }
    /// <summary>Related target URL, used for conflicts.</summary>
    public string? RelatedTargetUrl { get; set; }
    /// <summary>Normalized target URL used for duplicate comparison.</summary>
    public string? NormalizedTargetUrl { get; set; }
    /// <summary>Related normalized target URL used for duplicate comparison.</summary>
    public string? RelatedNormalizedTargetUrl { get; set; }
    /// <summary>HTTP status involved in the issue.</summary>
    public int Status { get; set; }
    /// <summary>Related HTTP status involved in the issue.</summary>
    public int RelatedStatus { get; set; }
    /// <summary>Resolved source file for diagnostics.</summary>
    public string? OriginPath { get; set; }
    /// <summary>One-based source line for diagnostics.</summary>
    public int OriginLine { get; set; }
    /// <summary>Related source file for diagnostics.</summary>
    public string? RelatedOriginPath { get; set; }
    /// <summary>Related one-based source line for diagnostics.</summary>
    public int RelatedOriginLine { get; set; }
}

/// <summary>Validation result for a link-service data set.</summary>
public sealed class LinkValidationResult
{
    /// <summary>Validation issues.</summary>
    public LinkValidationIssue[] Issues { get; set; } = Array.Empty<LinkValidationIssue>();
    /// <summary>Enabled redirect count.</summary>
    public int RedirectCount { get; set; }
    /// <summary>Enabled shortlink count.</summary>
    public int ShortlinkCount { get; set; }
    /// <summary>Error count.</summary>
    public int ErrorCount { get; set; }
    /// <summary>Warning count.</summary>
    public int WarningCount { get; set; }
    /// <summary>True when no errors were found.</summary>
    public bool Success => ErrorCount == 0;
}

/// <summary>Options for generating legacy WordPress AMP continuity redirects.</summary>
public sealed class WebLegacyAmpRedirectOptions
{
    /// <summary>Source legacy redirect CSV path.</summary>
    public string SourceCsvPath { get; set; } = string.Empty;
    /// <summary>Output CSV path for generated AMP redirects.</summary>
    public string OutputCsvPath { get; set; } = string.Empty;
    /// <summary>Default URL scheme used when generating absolute legacy AMP sources and targets.</summary>
    public string DefaultScheme { get; set; } = "https";
    /// <summary>Default English/root host for relative legacy rows.</summary>
    public string DefaultEnglishHost { get; set; } = "evotec.xyz";
    /// <summary>Default Polish host for relative Polish legacy rows.</summary>
    public string DefaultPolishHost { get; set; } = "evotec.pl";
}

/// <summary>Result for generated legacy WordPress AMP continuity redirects.</summary>
public sealed class WebLegacyAmpRedirectResult
{
    /// <summary>Resolved source legacy redirect CSV path.</summary>
    public string SourceCsvPath { get; set; } = string.Empty;
    /// <summary>Resolved output generated AMP redirect CSV path.</summary>
    public string OutputCsvPath { get; set; } = string.Empty;
    /// <summary>Number of source CSV rows considered.</summary>
    public int SourceRowCount { get; set; }
    /// <summary>Number of AMP redirects generated.</summary>
    public int GeneratedCount { get; set; }
    /// <summary>Number of skipped rows.</summary>
    public int SkippedCount { get; set; }
}
