namespace PowerForge;

/// <summary>
/// Describes the inputs used to verify a release installer produced by PowerForge.
/// </summary>
public sealed class DotNetPublishReleaseArtifactVerificationRequest
{
    /// <summary>Repository root used to resolve manifest-relative artifact paths.</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>Path to the PowerForge JSON artifact manifest.</summary>
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>Path to the PowerForge SHA-256 checksum manifest.</summary>
    public string ChecksumsPath { get; set; } = string.Empty;

    /// <summary>Path to the PowerForge dotnet-publish configuration.</summary>
    public string ConfigurationPath { get; set; } = string.Empty;

    /// <summary>Installer identifier expected in the manifest and configuration.</summary>
    public string InstallerId { get; set; } = string.Empty;

    /// <summary>Optional publish target selector used to disambiguate matrix installer entries.</summary>
    public string? Target { get; set; }

    /// <summary>Optional runtime identifier selector used to disambiguate matrix installer entries.</summary>
    public string? Runtime { get; set; }

    /// <summary>Optional target-framework selector used to disambiguate matrix installer entries.</summary>
    public string? Framework { get; set; }

    /// <summary>Optional publish-style selector used to disambiguate matrix installer entries.</summary>
    public string? Style { get; set; }

    /// <summary>Optional manifest-relative MSI path used to select one output from a multi-output installer build.</summary>
    public string? ArtifactPath { get; set; }

    /// <summary>Optional active publish profile used by the build.</summary>
    public string? Profile { get; set; }

    /// <summary>Optional signing profile override used by the build.</summary>
    public string? SignProfile { get; set; }

    /// <summary>Optional signing thumbprint override used by the build.</summary>
    public string? SignThumbprint { get; set; }

    /// <summary>Optional signing subject-name override used by the build.</summary>
    public string? SignSubjectName { get; set; }

    /// <summary>Optional signing enable override used by the build.</summary>
    public bool? EnableSigning { get; set; }

    /// <summary>Source revision expected by the caller, for example the version-tag workflow commit.</summary>
    public string ExpectedSourceRevision { get; set; } = string.Empty;
}

/// <summary>
/// Trusted local facts for one signed MSI after PowerForge release verification succeeds.
/// </summary>
public sealed class DotNetPublishReleaseArtifact
{
    /// <summary>Installer identifier from the PowerForge build configuration.</summary>
    public string InstallerId { get; set; } = string.Empty;

    /// <summary>Absolute path to the verified MSI.</summary>
    public string ArtifactPath { get; set; } = string.Empty;

    /// <summary>Safe installer file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Uppercase SHA-256 digest verified against the checksum manifest.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>MSI ProductVersion.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>MSI ProductCode in normalized brace form.</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>Stable MSI UpgradeCode in normalized brace form.</summary>
    public string UpgradeCode { get; set; } = string.Empty;

    /// <summary>MSI ProductName.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>MSI Manufacturer.</summary>
    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>Clean source revision recorded by the PowerForge manifest.</summary>
    public string SourceRevision { get; set; } = string.Empty;

    /// <summary>Subject of the certificate embedded in the valid Authenticode signature.</summary>
    public string SignerSubject { get; set; } = string.Empty;

    /// <summary>Normalized certificate thumbprint embedded in the valid Authenticode signature.</summary>
    public string SignerThumbprint { get; set; } = string.Empty;
}
