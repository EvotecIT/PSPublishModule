namespace PowerForge;

/// <summary>
/// Identifies a non-installer release artifact contract supported by PowerForge verification.
/// </summary>
public enum PowerForgeReleaseArtifactKind
{
    /// <summary>A signed portable .NET CLI archive produced by the dotnet publish pipeline.</summary>
    PortableCli,

    /// <summary>A packed and signed PowerShell module archive.</summary>
    PowerShellModule
}

/// <summary>
/// Describes the inputs used to verify a portable CLI or packed PowerShell module release artifact.
/// </summary>
public sealed class PowerForgeReleaseArtifactVerificationRequest
{
    /// <summary>Kind of release artifact being verified.</summary>
    public PowerForgeReleaseArtifactKind Kind { get; set; }

    /// <summary>Stable artifact identifier, such as a dotnet publish target or module name.</summary>
    public string ArtifactId { get; set; } = string.Empty;

    /// <summary>Repository root used to resolve checksum-relative paths.</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>Path to the artifact being admitted to the release set.</summary>
    public string ArtifactPath { get; set; } = string.Empty;

    /// <summary>Path to the PowerForge SHA-256 checksum catalog.</summary>
    public string ChecksumsPath { get; set; } = string.Empty;

    /// <summary>Optional detached CMS signature over the exact checksum catalog bytes. Required for external SBOMs.</summary>
    public string? ChecksumsSignaturePath { get; set; }

    /// <summary>Source revision expected by the release workflow.</summary>
    public string ExpectedSourceRevision { get; set; } = string.Empty;

    /// <summary>Optional expected product or module version.</summary>
    public string? ExpectedVersion { get; set; }

    /// <summary>PowerForge JSON artifact manifest. Required for portable CLI artifacts.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>PowerForge dotnet-publish or release configuration. Required for portable CLI artifacts.</summary>
    public string? ConfigurationPath { get; set; }

    /// <summary>Optional publish profile used by the build.</summary>
    public string? Profile { get; set; }

    /// <summary>Optional publish target selector. Defaults to <see cref="ArtifactId"/>.</summary>
    public string? Target { get; set; }

    /// <summary>Optional runtime identifier selector.</summary>
    public string? Runtime { get; set; }

    /// <summary>Optional target framework selector.</summary>
    public string? Framework { get; set; }

    /// <summary>Optional publish style selector.</summary>
    public string? Style { get; set; }

    /// <summary>
    /// Paths of signed files. Portable CLI paths are repository-relative files represented by the archive;
    /// PowerShell module paths are archive entry names.
    /// </summary>
    public string[] SignaturePaths { get; set; } = Array.Empty<string>();

    /// <summary>Optional signing profile override used by the portable build.</summary>
    public string? SignProfile { get; set; }

    /// <summary>
    /// Publisher thumbprint supplied through an out-of-band trust channel. Verification requires this value
    /// or <see cref="SignSubjectName"/> and never establishes publisher trust from release metadata.
    /// </summary>
    public string? SignThumbprint { get; set; }

    /// <summary>
    /// Exact publisher subject supplied through an out-of-band trust channel. Verification requires this value
    /// or <see cref="SignThumbprint"/> and never establishes publisher trust from release metadata.
    /// </summary>
    public string? SignSubjectName { get; set; }

    /// <summary>
    /// Optional path to trusted PowerForge module signing evidence. Required for packed modules and expected
    /// to enumerate the complete signable file set selected by the module build.
    /// </summary>
    public string? SigningEvidencePath { get; set; }

    /// <summary>Optional signing enable override used by the portable build.</summary>
    public bool? EnableSigning { get; set; }

    /// <summary>Optional checksum-cataloged CycloneDX or SPDX SBOM sidecars.</summary>
    public string[] SbomPaths { get; set; } = Array.Empty<string>();
}

/// <summary>Hash-bound evidence file associated with a verified release artifact.</summary>
public sealed class PowerForgeReleaseEvidenceFile
{
    /// <summary>Evidence role, such as manifest, provenance, checksum catalog, or SBOM.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Absolute path, or an archive-qualified path for embedded evidence.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Uppercase SHA-256 digest.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>Signer evidence for one file represented by a verified release artifact.</summary>
public sealed class PowerForgeReleaseSignatureEvidence
{
    /// <summary>Signed file path represented by the artifact.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Signer certificate subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Normalized signer certificate thumbprint.</summary>
    public string Thumbprint { get; set; } = string.Empty;

    /// <summary>Ownership classification: <c>publisher</c> or <c>third-party</c>.</summary>
    public string Ownership { get; set; } = string.Empty;
}

/// <summary>
/// Trusted local evidence for one signed portable CLI or packed PowerShell module artifact.
/// </summary>
public sealed class PowerForgeReleaseArtifactEvidence
{
    /// <summary>Artifact kind.</summary>
    public PowerForgeReleaseArtifactKind ArtifactKind { get; set; }

    /// <summary>Stable artifact identifier.</summary>
    public string ArtifactId { get; set; } = string.Empty;

    /// <summary>Absolute path to the verified release artifact.</summary>
    public string ArtifactPath { get; set; } = string.Empty;

    /// <summary>Safe release artifact file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Uppercase SHA-256 digest verified against the checksum catalog.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Product or module version bound to the signed payload.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Clean source revision bound by the build manifest or embedded module provenance.</summary>
    public string SourceRevision { get; set; } = string.Empty;

    /// <summary>Subject shared by the verified Authenticode signatures.</summary>
    public string SignerSubject { get; set; } = string.Empty;

    /// <summary>Normalized thumbprint shared by the verified Authenticode signatures.</summary>
    public string SignerThumbprint { get; set; } = string.Empty;

    /// <summary>Authenticode verification status. Successful evidence always reports <c>valid</c>.</summary>
    public string SignatureStatus { get; set; } = string.Empty;

    /// <summary>Signed files whose bytes are represented by the artifact.</summary>
    public string[] SignaturePaths { get; set; } = Array.Empty<string>();

    /// <summary>Per-file signer and ownership evidence.</summary>
    public PowerForgeReleaseSignatureEvidence[] Signatures { get; set; } = Array.Empty<PowerForgeReleaseSignatureEvidence>();

    /// <summary>Hash-bound manifest, provenance, checksum, and SBOM evidence.</summary>
    public PowerForgeReleaseEvidenceFile[] EvidenceFiles { get; set; } = Array.Empty<PowerForgeReleaseEvidenceFile>();
}
