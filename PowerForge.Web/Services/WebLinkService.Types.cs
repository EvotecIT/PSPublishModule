using System;
using System.Collections.Generic;

namespace PowerForge.Web;

/// <summary>Input paths and host aliases used when loading link-service data.</summary>
public sealed class WebLinkLoadOptions
{
    /// <summary>Path to redirect JSON.</summary>
    public string? RedirectsPath { get; set; }
    /// <summary>Path to shortlink JSON.</summary>
    public string? ShortlinksPath { get; set; }
    /// <summary>Compatibility CSV redirect inputs.</summary>
    public string[] RedirectCsvPaths { get; set; } = Array.Empty<string>();
    /// <summary>Named host aliases, for example en, pl, or short.</summary>
    public IReadOnlyDictionary<string, string> Hosts { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Host-to-language-prefix map for domains where a language is deployed at the web root.</summary>
    public IReadOnlyDictionary<string, string> LanguageRootHosts { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Loaded link-service rules and source tracking metadata.</summary>
public sealed class WebLinkDataSet
{
    /// <summary>Loaded redirect rules.</summary>
    public LinkRedirectRule[] Redirects { get; set; } = Array.Empty<LinkRedirectRule>();
    /// <summary>Loaded shortlink rules.</summary>
    public LinkShortlinkRule[] Shortlinks { get; set; } = Array.Empty<LinkShortlinkRule>();
    /// <summary>Source files that were found and read.</summary>
    public string[] UsedSources { get; set; } = Array.Empty<string>();
    /// <summary>Configured source files that were missing.</summary>
    public string[] MissingSources { get; set; } = Array.Empty<string>();
    /// <summary>Named host aliases, for example en, pl, or short.</summary>
    public IReadOnlyDictionary<string, string> Hosts { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Host-to-language-prefix map for domains where a language is deployed at the web root.</summary>
    public IReadOnlyDictionary<string, string> LanguageRootHosts { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Options for exporting link-service rules to Apache rewrite syntax.</summary>
public sealed class WebLinkApacheExportOptions
{
    /// <summary>Output file path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>When true, emit explanatory comments at the top of the output.</summary>
    public bool IncludeHeader { get; set; } = true;
    /// <summary>When true, emit <c>ErrorDocument 404 /404.html</c>.</summary>
    public bool IncludeErrorDocument404 { get; set; }
    /// <summary>Named host aliases used for short-host path inference.</summary>
    public IReadOnlyDictionary<string, string> Hosts { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Host-to-language-prefix map for domains where a language is deployed at the web root.</summary>
    public IReadOnlyDictionary<string, string> LanguageRootHosts { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Result from Apache link-service export.</summary>
public sealed class WebLinkApacheExportResult
{
    /// <summary>Resolved output path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Number of emitted rewrite rules.</summary>
    public int RuleCount { get; set; }
}
