namespace PowerForge.Web;

/// <summary>
/// Declares an external repository that can be synchronized locally as part of a site build bootstrap.
/// Used by `powerforge-web sources-sync` (and `powerforge-web build --sync-sources`).
/// </summary>
public sealed class SourceRepoSpec
{
    /// <summary>Repository URL/path or owner/name shorthand.</summary>
    public string Repo { get; set; } = string.Empty;

    /// <summary>
    /// Optional destination directory for checkout. If omitted, defaults to
    /// &lt;ProjectsRoot&gt;/&lt;Slug&gt; (or ./projects/&lt;Slug&gt; when ProjectsRoot is not set).
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>Optional stable slug used for default destination inference.</summary>
    public string? Slug { get; set; }

    /// <summary>Branch/tag/commit reference to check out after sync.</summary>
    public string? Ref { get; set; }

    /// <summary>Base URL/path used to expand owner/repo shorthand (defaults to https://github.com).</summary>
    public string? RepoBaseUrl { get; set; }

    /// <summary>Authentication mode: auto (default), token, ssh, or none.</summary>
    public string? AuthType { get; set; }

    /// <summary>Env var containing repository token. Defaults to GITHUB_TOKEN.</summary>
    public string? TokenEnv { get; set; }

    /// <summary>Inline token (discouraged; prefer TokenEnv + CI secrets).</summary>
    public string? Token { get; set; }

    /// <summary>Username paired with token for HTTP Basic auth (default x-access-token).</summary>
    public string? Username { get; set; }

    /// <summary>When true, delete destination before sync.</summary>
    public bool? Clean { get; set; }

    /// <summary>Clone depth (0 = full history).</summary>
    public int? Depth { get; set; }

    /// <summary>When true, fetch tags when updating an existing checkout.</summary>
    public bool? FetchTags { get; set; }

    /// <summary>Sparse checkout include paths/patterns.</summary>
    public string[] SparseCheckout { get; set; } = Array.Empty<string>();

    /// <summary>Comma-separated sparse checkout patterns (alias style).</summary>
    public string? SparsePaths { get; set; }

    /// <summary>Initialize/update submodules after sync.</summary>
    public bool? Submodules { get; set; }

    /// <summary>When true, update submodules recursively.</summary>
    public bool? SubmodulesRecursive { get; set; }

    /// <summary>Submodule clone depth (0 = full history).</summary>
    public int? SubmoduleDepth { get; set; }

    /// <summary>Git command timeout in seconds.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Retry attempts for git command failures.</summary>
    public int? Retry { get; set; }

    /// <summary>Delay between retry attempts in milliseconds.</summary>
    public int? RetryDelayMs { get; set; }
}

