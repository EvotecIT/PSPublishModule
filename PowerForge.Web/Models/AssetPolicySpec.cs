namespace PowerForge.Web;

/// <summary>Defines global asset policy for local vs CDN usage and optimization.</summary>
public sealed class AssetPolicySpec
{
    /// <summary>Asset mode: local, cdn, or hybrid (prefer local if available).</summary>
    public string Mode { get; set; } = "local";
    /// <summary>Optional asset hashing settings.</summary>
    public AssetHashSpec? Hashing { get; set; }
    /// <summary>Optional cache header settings.</summary>
    public CacheHeadersSpec? CacheHeaders { get; set; }
    /// <summary>Optional rewrite rules for external assets.</summary>
    public AssetRewriteSpec[] Rewrites { get; set; } = Array.Empty<AssetRewriteSpec>();
}

/// <summary>Defines asset hashing settings.</summary>
public sealed class AssetHashSpec
{
    /// <summary>Enable asset hashing.</summary>
    public bool Enabled { get; set; }
    /// <summary>File extensions to hash (default: .css, .js).</summary>
    public string[] Extensions { get; set; } = new[] { ".css", ".js" };
    /// <summary>Glob-style exclude patterns (relative to site root).</summary>
    public string[] Exclude { get; set; } = Array.Empty<string>();
    /// <summary>Optional manifest file path (relative to site root).</summary>
    public string? ManifestPath { get; set; }
}

/// <summary>Defines cache header output rules.</summary>
public sealed class CacheHeadersSpec
{
    /// <summary>Enable header file generation.</summary>
    public bool Enabled { get; set; }
    /// <summary>Output file name (default: _headers).</summary>
    public string? OutputPath { get; set; }
    /// <summary>Cache-Control value for HTML pages.</summary>
    public string? HtmlCacheControl { get; set; }
    /// <summary>Cache-Control value for immutable assets.</summary>
    public string? ImmutableCacheControl { get; set; }
    /// <summary>Optional path patterns to treat as immutable.</summary>
    public string[] ImmutablePaths { get; set; } = Array.Empty<string>();
}

/// <summary>Defines a rewrite rule for external assets.</summary>
public sealed class AssetRewriteSpec
{
    /// <summary>Match pattern (substring, prefix, exact, or regex).</summary>
    public string Match { get; set; } = string.Empty;
    /// <summary>Replacement value.</summary>
    public string Replace { get; set; } = string.Empty;
    /// <summary>Match type: contains (default), prefix, exact, regex.</summary>
    public string MatchType { get; set; } = "contains";
    /// <summary>Optional source file to copy into the site root.</summary>
    public string? Source { get; set; }
    /// <summary>Optional destination path (relative to site root).</summary>
    public string? Destination { get; set; }
}
