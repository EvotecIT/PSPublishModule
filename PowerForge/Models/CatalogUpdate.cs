namespace PowerForge;

/// <summary>Product policy for updates using existing signed MSI releases.</summary>
internal sealed class CatalogUpdateSpec
{
    public string? Schema { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string ReleaseConfigPath { get; set; } = string.Empty;
    public string? StoreConfigPath { get; set; }
    public string? StoreTargetName { get; set; }
    public string? StoreInstallerUrlTemplate { get; set; }
    public Dictionary<string, string> StoreArtifactKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CatalogInstaller
{
    public string FileName { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Length { get; set; }
    public string WingetUrl { get; set; } = string.Empty;
    public string StoreUrl { get; set; } = string.Empty;
}

/// <summary>Durable local receipt. Submitted means accepted by the submission client, not catalog publication.</summary>
internal sealed class CatalogUpdateReceipt
{
    public int SchemaVersion { get; set; } = 1;
    public string PackageIdentifier { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string DeliveryReleaseId { get; set; } = string.Empty;
    public string ProfileSha256 { get; set; } = string.Empty;
    public string ReleaseConfigSha256 { get; set; } = string.Empty;
    public Dictionary<string, string> Files { get; set; } = new();
    public CatalogInstaller[] Artifacts { get; set; } = Array.Empty<CatalogInstaller>();
    public CatalogChannelReceipt Winget { get; set; } = new();
    public CatalogChannelReceipt Store { get; set; } = new();
}

internal sealed class CatalogChannelReceipt
{
    // Attempting is intentionally durable before a remote mutation. Never replay an ambiguous request.
    public string State { get; set; } = "Prepared";
    public string? Reference { get; set; }
    public string? RemoteStatus { get; set; }
    public string? ConfigurationSha256 { get; set; }
    public DateTimeOffset? AttemptedUtc { get; set; }
    public string? ReservationKey { get; set; }
}
