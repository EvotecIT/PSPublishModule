namespace PowerForge;

/// <summary>Identifies the signed files staged for one NuGet release.</summary>
public sealed class NuGetReleaseManifest
{
    /// <summary>Supported manifest format version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Git commit from which the local release was built.</summary>
    public string SourceCommitSha { get; set; } = string.Empty;
    /// <summary>Package and symbol assets, including their signed-file hashes.</summary>
    public NuGetReleaseAsset[] Assets { get; set; } = Array.Empty<NuGetReleaseAsset>();
}

/// <summary>Identity and integrity of one signed NuGet release asset.</summary>
public sealed class NuGetReleaseAsset
{
    /// <summary>Filename relative to the manifest directory; directories are not allowed.</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Package id read from the package nuspec.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Normalized NuGet package version.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>SHA-256 of the exact signed asset uploaded to GitHub.</summary>
    public string Sha256 { get; set; } = string.Empty;
    /// <summary>SHA-256 fingerprint of the author signing certificate.</summary>
    public string AuthorCertificateFingerprint { get; set; } = string.Empty;
}
