using System;

namespace PowerForge;

/// <summary>
/// Artifact shapes produced by the PowerShell compilation pipeline.
/// </summary>
public enum PowerShellCompilationArtifactKind
{
    /// <summary>A runnable host that packages the original script and PowerShell runtime.</summary>
    Executable,

    /// <summary>A CLR class library containing genuinely typed compiled methods.</summary>
    Library,

    /// <summary>An importable PowerShell binary module containing typed compiled cmdlets.</summary>
    BinaryModule
}

/// <summary>Optional size and native-publication mode for a genuinely typed executable.</summary>
public enum PowerShellCompilationExecutableOptimization
{
    /// <summary>Normal managed .NET publication.</summary>
    None,

    /// <summary>Self-contained single-file publication with unused managed code trimmed.</summary>
    Trimmed,

    /// <summary>Self-contained native AOT publication.</summary>
    NativeAot
}

/// <summary>
/// Configuration for building a PowerShell compilation artifact.
/// </summary>
public sealed class PowerShellCompilationBuildSpec
{
    /// <summary>Creates an artifact-build specification.</summary>
    public PowerShellCompilationBuildSpec(
        string sourcePath,
        string outputDirectory,
        string artifactName,
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode = PowerShellCompilationMode.Package)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("A source path is required.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        if (string.IsNullOrWhiteSpace(artifactName)) throw new ArgumentException("An artifact name is required.", nameof(artifactName));
        if (!Enum.IsDefined(typeof(PowerShellCompilationArtifactKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(PowerShellCompilationMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        SourcePath = System.IO.Path.GetFullPath(sourcePath.Trim().Trim('"'));
        OutputDirectory = System.IO.Path.GetFullPath(outputDirectory.Trim().Trim('"'));
        ArtifactName = artifactName;
        Kind = kind;
        Mode = mode;
    }

    /// <summary>PowerShell source file.</summary>
    public string SourcePath { get; }

    /// <summary>Destination directory for durable artifacts and the manifest.</summary>
    public string OutputDirectory { get; }

    /// <summary>Artifact file and assembly name.</summary>
    public string ArtifactName { get; }

    /// <summary>Requested artifact shape.</summary>
    public PowerShellCompilationArtifactKind Kind { get; }

    /// <summary>Fallback policy.</summary>
    public PowerShellCompilationMode Mode { get; }

    /// <summary>Target framework used by the generated project.</summary>
    public string TargetFramework { get; set; } = "net8.0";

    /// <summary>Optional runtime identifier for executable publication.</summary>
    public string? RuntimeIdentifier { get; set; }

    /// <summary>Whether executable publication includes the .NET runtime.</summary>
    public bool SelfContained { get; set; }

    /// <summary>Whether executable publication requests a single-file bundle.</summary>
    public bool SingleFile { get; set; } = true;

    /// <summary>Optional optimization for a genuinely typed executable.</summary>
    public PowerShellCompilationExecutableOptimization Optimization { get; set; }

    /// <summary>Whether generated signable files should receive Authenticode signatures before hashes are recorded.</summary>
    public bool SignArtifact { get; set; }

    /// <summary>Optional code-signing certificate thumbprint. A unique code-signing certificate may be selected when omitted.</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Certificate store used for Authenticode signing.</summary>
    public CertificateStoreLocation CertificateStoreLocation { get; set; } = CertificateStoreLocation.CurrentUser;

    /// <summary>RFC3161 timestamp service used for Authenticode signing.</summary>
    public string TimeStampServer { get; set; } = "http://timestamp.digicert.com";

    /// <summary>Maximum time allowed for the Authenticode signing command.</summary>
    public int SigningTimeoutSeconds { get; set; } = 120;

    /// <summary>Whether the generated build workspace should be retained for inspection.</summary>
    public bool KeepBuildWorkspace { get; set; }

    /// <summary>Maximum time allowed for restore and compilation.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Durable evidence describing one produced PowerShell artifact.
/// </summary>
public sealed class PowerShellCompilationArtifactManifest
{
    /// <summary>Manifest schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Artifact name.</summary>
    public string ArtifactName { get; set; } = string.Empty;

    /// <summary>Artifact kind.</summary>
    public PowerShellCompilationArtifactKind Kind { get; set; }

    /// <summary>Requested compilation mode.</summary>
    public PowerShellCompilationMode Mode { get; set; }

    /// <summary>Full source path.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Target framework.</summary>
    public string TargetFramework { get; set; } = string.Empty;

    /// <summary>Optional runtime identifier.</summary>
    public string? RuntimeIdentifier { get; set; }

    /// <summary>Whether the artifact must execute inside or host a PowerShell runtime.</summary>
    public bool RequiresPowerShellRuntime { get; set; }

    /// <summary>Whether any unit executes through dynamic PowerShell script semantics.</summary>
    public bool UsesPowerShellRuntimeFallback { get; set; }

    /// <summary>Whether the .NET runtime is included with the artifact.</summary>
    public bool SelfContained { get; set; }

    /// <summary>Whether single-file publication was requested.</summary>
    public bool SingleFile { get; set; }

    /// <summary>Executable size/native-publication mode.</summary>
    public PowerShellCompilationExecutableOptimization Optimization { get; set; }

    /// <summary>Size of the primary artifact in bytes.</summary>
    public long ArtifactSizeBytes { get; set; }

    /// <summary>Whether signable artifact files were Authenticode-signed before hashing.</summary>
    public bool AuthenticodeSigned { get; set; }

    /// <summary>Thumbprint of the certificate used to sign generated files.</summary>
    public string? SigningCertificateThumbprint { get; set; }

    /// <summary>Number of generated files signed before publication.</summary>
    public int AuthenticodeSignedFiles { get; set; }

    /// <summary>Number of genuinely typed methods in the artifact.</summary>
    public int CompiledMethods { get; set; }

    /// <summary>Number of executable units left on the PowerShell runtime path.</summary>
    public int RuntimeFallbackUnits { get; set; }

    /// <summary>Number of unsupported units intentionally absent from a typed-only CLR library.</summary>
    public int OmittedUnits { get; set; }

    /// <summary>Typed compilation coverage among analyzed units.</summary>
    public double CompilationCoveragePercentage { get; set; }

    /// <summary>Exact artifact file path.</summary>
    public string ArtifactPath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the primary artifact.</summary>
    public string ArtifactSha256 { get; set; } = string.Empty;

    /// <summary>All durable files that form the artifact, including hybrid-module support files.</summary>
    public PowerShellCompilationArtifactFile[] Files { get; set; } = Array.Empty<PowerShellCompilationArtifactFile>();

    /// <summary>Source diagnostics retained as honest fallback evidence.</summary>
    public PowerShellCompilationDiagnostic[] Diagnostics { get; set; } = Array.Empty<PowerShellCompilationDiagnostic>();
}

/// <summary>
/// Hash evidence for one durable file that forms a PowerShell compilation artifact.
/// </summary>
public sealed class PowerShellCompilationArtifactFile
{
    /// <summary>Full durable path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Purpose of the file, such as Primary, TypedAssembly, or ScriptFallback.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>SHA-256 of the file content.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long SizeBytes { get; set; }
}

/// <summary>
/// Result of building a PowerShell compilation artifact.
/// </summary>
public sealed class PowerShellCompilationBuildResult
{
    /// <summary>Whether the requested artifact was produced.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Primary artifact path when successful.</summary>
    public string? ArtifactPath { get; set; }

    /// <summary>Machine-readable artifact manifest path when successful.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>Retained generated workspace path when requested.</summary>
    public string? BuildWorkspace { get; set; }

    /// <summary>Failure message.</summary>
    public string? Error { get; set; }

    /// <summary>Combined bounded output from the generated .NET build.</summary>
    public string BuildOutput { get; set; } = string.Empty;

    /// <summary>Artifact evidence when successful.</summary>
    public PowerShellCompilationArtifactManifest? Manifest { get; set; }
}
