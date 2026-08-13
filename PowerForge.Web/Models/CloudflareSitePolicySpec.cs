namespace PowerForge.Web;

/// <summary>Defines optional Cloudflare delivery policy for a generated static website.</summary>
public sealed class CloudflareSitePolicySpec
{
    /// <summary>Optional cache TTL overrides for successful static-site responses.</summary>
    public CloudflareCacheSpec? Cache { get; set; }

    /// <summary>Cache purge mode used by deployment pipelines: files, hostname, or everything.</summary>
    public string PurgeMode { get; set; } = "files";

    /// <summary>
    /// When set, PowerForge manages the zone's Smart Tiered Cache setting as part of the recoverable site policy.
    /// Leave null to preserve the operator-managed zone setting.
    /// </summary>
    public bool? SmartTieredCache { get; set; }
}

/// <summary>Defines Cloudflare edge and browser TTL overrides for successful static-site responses.</summary>
public sealed class CloudflareCacheSpec
{
    /// <summary>Cloudflare edge TTL in seconds. Seven days is the static-site default.</summary>
    public int EdgeTtlSeconds { get; set; } = 604800;

    /// <summary>Browser TTL in seconds. Keep this short because an edge purge cannot clear visitor browsers.</summary>
    public int BrowserTtlSeconds { get; set; } = 300;
}
