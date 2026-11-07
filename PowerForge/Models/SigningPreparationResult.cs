namespace PowerForge;

/// <summary>Evidence of one verified signature, not a guarantee of later provider availability.</summary>
public sealed class SigningPreparationResult
{
    /// <summary>Receipt format version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>UTC observation time after signature verification.</summary>
    public DateTimeOffset VerifiedAtUtc { get; set; }
    /// <summary>Verified signing identity.</summary>
    public string Thumbprint { get; set; } = string.Empty;
    /// <summary>Generated, signed proof file retained locally.</summary>
    public string ArtifactPath { get; set; } = string.Empty;
    /// <summary>SHA-256 of the signed proof bytes.</summary>
    public string ArtifactSha256 { get; set; } = string.Empty;
    /// <summary>Authenticode readback status.</summary>
    public string SignatureStatus { get; set; } = string.Empty;
    /// <summary>Provider-owned cache behavior; this operation neither reads nor changes it.</summary>
    public string CacheScope { get; set; } = "provider-managed; expiry unknown";
}
