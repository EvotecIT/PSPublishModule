namespace PowerForge.Web;

/// <summary>Defines optional Cloudflare delivery policy for a generated static website.</summary>
public sealed class CloudflareSitePolicySpec
{
    /// <summary>Optional cache TTL overrides for successful static-site responses.</summary>
    public CloudflareCacheSpec? Cache { get; set; }

    /// <summary>Cache purge mode used by deployment pipelines: files, incremental, hostname, or everything.</summary>
    public string PurgeMode { get; set; } = "files";

    /// <summary>
    /// Site-relative URL paths that incremental deployments purge on every successful deployment.
    /// Use this for mutable entry points whose query variants have distinct Cloudflare cache keys.
    /// </summary>
    public string[] AlwaysPurgePaths { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Site-relative Cloudflare wildcard patterns for public content-addressed files whose URL changes whenever their bytes change.
    /// Successful non-HTML, non-XHTML responses matching a pattern receive <c>Cache-Control: public, max-age=31536000, immutable</c>
    /// through the managed response-header policy. Each pattern must end in a literal file extension; list only
    /// fingerprinted names (for example <c>/app/_framework/*.*.wasm</c>) because a browser copy cannot be purged.
    /// </summary>
    public string[] ImmutablePaths { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When set, PowerForge manages the zone's Smart Tiered Cache setting as part of the recoverable site policy.
    /// Leave null to preserve the operator-managed zone setting.
    /// </summary>
    public bool? SmartTieredCache { get; set; }
}

/// <summary>Defines Cloudflare edge TTL overrides for successful static-site responses.</summary>
public sealed class CloudflareCacheSpec
{
    /// <summary>Cloudflare edge TTL in seconds. Seven days is the static-site default.</summary>
    public int EdgeTtlSeconds { get; set; } = 604800;
}
