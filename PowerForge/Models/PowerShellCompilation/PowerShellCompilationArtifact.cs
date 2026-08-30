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
    /// <summary>Creates an artifact-build specification with the default mode for the artifact kind.</summary>
    public PowerShellCompilationBuildSpec(
        string sourcePath,
        string outputDirectory,
        string artifactName,
        PowerShellCompilationArtifactKind kind)
        : this(sourcePath, outputDirectory, artifactName, kind, GetDefaultMode(kind))
    {
    }

    /// <summary>Creates an artifact-build specification and explicitly selects unreviewed dependency resolution.</summary>
    public PowerShellCompilationBuildSpec(
        string sourcePath,
        string outputDirectory,
        string artifactName,
        PowerShellCompilationArtifactKind kind,
        bool allowUnreviewedDependencyResolution)
        : this(sourcePath, outputDirectory, artifactName, kind, GetDefaultMode(kind), allowUnreviewedDependencyResolution)
    {
    }

    /// <summary>Creates an artifact-build specification.</summary>
    public PowerShellCompilationBuildSpec(
        string sourcePath,
        string outputDirectory,
        string artifactName,
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        bool allowUnreviewedDependencyResolution = false)
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
        AllowUnreviewedDependencyResolution = allowUnreviewedDependencyResolution;
    }

    /// <summary>Returns the build mode used when a caller omits an explicit mode.</summary>
    public static PowerShellCompilationMode GetDefaultMode(PowerShellCompilationArtifactKind kind)
        => kind switch
        {
            PowerShellCompilationArtifactKind.Executable => PowerShellCompilationMode.Package,
            PowerShellCompilationArtifactKind.BinaryModule => PowerShellCompilationMode.Hybrid,
            PowerShellCompilationArtifactKind.Library => PowerShellCompilationMode.Hybrid,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    /// <summary>Returns whether an artifact kind can be produced with the requested mode.</summary>
    public static bool IsModeSupported(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode)
    {
        if (!Enum.IsDefined(typeof(PowerShellCompilationArtifactKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(PowerShellCompilationMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return mode switch
        {
            PowerShellCompilationMode.Package => kind == PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Hybrid => true,
            PowerShellCompilationMode.Strict => true,
            _ => false
        };
    }

    /// <summary>Rejects an artifact kind and mode combination that the compilation pipeline cannot produce.</summary>
    public static void EnsureModeSupported(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode)
    {
        if (IsModeSupported(kind, mode))
            return;
        if (mode == PowerShellCompilationMode.Analyze)
            throw new ArgumentException("Analyze selects planning behavior and is not an artifact build mode. Use Package, Hybrid, or Strict.", nameof(mode));
        if (mode == PowerShellCompilationMode.Package)
            throw new ArgumentException("Package compilation is supported only for Executable artifacts.", nameof(mode));
        throw new ArgumentException($"Compilation mode '{mode}' is not supported for artifact kind '{kind}'.", nameof(mode));
    }

    /// <summary>Returns the typed-language capabilities enabled by an artifact build.</summary>
    public static PowerShellCompilationCapability GetCapabilities(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode)
    {
        EnsureModeSupported(kind, mode);
        return kind == PowerShellCompilationArtifactKind.BinaryModule ||
               kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Hybrid
            ? PowerShellCompilationCapabilities.BinaryModule
            : kind == PowerShellCompilationArtifactKind.Library
                ? PowerShellCompilationCapabilities.StaticRuntimeFacts
            : kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Strict
                ? PowerShellCompilationCapabilities.TypedExecutable
                : PowerShellCompilationCapability.None;
    }

    /// <summary>PowerShell source file.</summary>
    public string SourcePath { get; }

    /// <summary>Optional source module manifest selected independently from the root script module.</summary>
    public string? ModuleManifestPath { get; set; }

    /// <summary>Optional contained literal dot-sourced files that participate in the same module compilation scope.</summary>
    public string[] CompilationSourcePaths { get; set; } = Array.Empty<string>();

    /// <summary>All contained authored runtime source files resolved for packaging or Hybrid preservation, including files outside the typed compilation scope.</summary>
    public string[] RuntimeSourcePaths { get; set; } = Array.Empty<string>();

    /// <summary>Policy for optional resource payload. Folder names are classification hints only.</summary>
    public PowerShellCompilationResourceMode ResourceMode { get; set; } = PowerShellCompilationResourceMode.Declared;

    /// <summary>Contained module-root resource paths or glob patterns to include.</summary>
    public string[] IncludeResource { get; set; } = Array.Empty<string>();

    /// <summary>Contained module-root resource paths or glob patterns to exclude from optional payload.</summary>
    public string[] ExcludeResource { get; set; } = Array.Empty<string>();

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

    /// <summary>Optional explicit semantic, execution, and deployment target. When supplied, it must match the compatibility build fields.</summary>
    public PowerShellCompilationTargetContract? TargetContract { get; set; }

    /// <summary>Whether the content-addressed generated-build cache may be used.</summary>
    public bool UseBuildCache { get; set; }

    /// <summary>Optional machine-local root for content-addressed generated-build cache entries.</summary>
    public string? BuildCacheDirectory { get; set; }

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

    /// <summary>Whether a durable, independently buildable copy of the generated source project should be published.</summary>
    public bool EmitSource { get; set; }

    /// <summary>Whether a redacted, semantic-only bound/lowered IR snapshot should be published for diffing.</summary>
    public bool EmitIrSnapshots { get; set; }

    /// <summary>Optional expected public ABI SHA-256 used to fail closed on ABI drift.</summary>
    public string? ExpectedPublicAbiSha256 { get; set; }

    /// <summary>Maximum time allowed for restore and compilation.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Optional dependency graph reviewed before the build. When supplied, the build fails unless
    /// the newly resolved graph and every hashed local input exactly match this lock.
    /// </summary>
    public PowerShellCompilationDependencyGraph? ExpectedDependencyLock { get; set; }

    /// <summary>Optional equivalent-workload runtime boundary profile to bind into manifest evidence.</summary>
    public PowerShellCompilationBoundaryRuntimeProfile? BoundaryRuntimeProfile { get; set; }

    /// <summary>
    /// Explicitly permits a build to resolve the current dependency graph without a separately reviewed lock.
    /// The resulting manifest records that the dependency lock was not reviewed.
    /// </summary>
    public bool AllowUnreviewedDependencyResolution { get; set; }

    /// <summary>Additional compile-time-only command semantic providers used by this build.</summary>
    public PowerShellCompilationCommandProviderContract[] CommandProviders { get; set; } = Array.Empty<PowerShellCompilationCommandProviderContract>();

    /// <summary>Explicit provider packages inspected without assembly loading or source execution.</summary>
    public PowerShellCompilationProviderPackageReference[] ProviderPackages { get; set; } = Array.Empty<PowerShellCompilationProviderPackageReference>();

    /// <summary>Separately reviewed provider-package lock required for trusted provider resolution.</summary>
    public PowerShellCompilationProviderLock? ExpectedProviderLock { get; set; }

    /// <summary>Provider package allow/deny, publisher, license, and signature policy.</summary>
    public PowerShellCompilationProviderTrustPolicy ProviderTrustPolicy { get; set; } = new();

    /// <summary>Explicit development opt-out permitting provider package resolution without a reviewed provider lock.</summary>
    public bool AllowUnreviewedProviderResolution { get; set; }
}

/// <summary>
/// Durable evidence describing one produced PowerShell artifact.
/// </summary>
public sealed class PowerShellCompilationArtifactManifest
{
    /// <summary>Manifest schema version.</summary>
    public int SchemaVersion { get; set; } = 11;

    /// <summary>Artifact name.</summary>
    public string ArtifactName { get; set; } = string.Empty;

    /// <summary>Artifact kind.</summary>
    public PowerShellCompilationArtifactKind Kind { get; set; }

    /// <summary>Requested compilation mode.</summary>
    public PowerShellCompilationMode Mode { get; set; }

    /// <summary>Full source path.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>All authored files resolved into the shared module compilation scope.</summary>
    public string[] SourceFiles { get; set; } = Array.Empty<string>();

    /// <summary>Target framework.</summary>
    public string TargetFramework { get; set; } = string.Empty;

    /// <summary>Optional runtime identifier.</summary>
    public string? RuntimeIdentifier { get; set; }

    /// <summary>Canonical semantic, execution, and deployment target consumed by artifact generation.</summary>
    public PowerShellCompilationTargetContract? TargetContract { get; set; }

    /// <summary>Compiler and SDK provenance captured for this build.</summary>
    public PowerShellCompilationToolchainEvidence? Toolchain { get; set; }

    /// <summary>Content-addressed generated-build cache result.</summary>
    public PowerShellCompilationBuildCacheEvidence? BuildCache { get; set; }

    /// <summary>Bound-IR optimization evidence for generated typed methods.</summary>
    public PowerShellCompilationOptimizationEvidence? IrOptimization { get; set; }

    /// <summary>Optional redacted bound/lowered IR snapshot artifact.</summary>
    public PowerShellCompilationIrSnapshotEvidence? IrSnapshots { get; set; }

    /// <summary>Deterministic final decision trace from authored units to artifact disposition.</summary>
    public PowerShellCompilationExplanation? DecisionTrace { get; set; }

    /// <summary>Immutable final authority for every authored unit after artifact shaping.</summary>
    public PowerShellCompilationUnitDispositionLedger? UnitDispositionLedger { get; set; }

    /// <summary>Redacted, integrity-bound inputs needed to reproduce the compiler decision.</summary>
    public PowerShellCompilationReproductionEvidence? Reproduction { get; set; }

    /// <summary>Static typed/hosted transition evidence for the artifact plan.</summary>
    public PowerShellCompilationBoundaryEvidence? Boundaries { get; set; }

    /// <summary>Portable statement-level mapping used for build and runtime diagnosis.</summary>
    public PowerShellCompilationFailureMap? FailureMap { get; set; }

    /// <summary>Auditable cache, graph, ABI, boundary, and provider decisions.</summary>
    public PowerShellCompilationAuditTrail? DiagnosticAudit { get; set; }

    /// <summary>Retention and redaction policy attached to this diagnostic evidence.</summary>
    public PowerShellCompilationDiagnosticsPolicy? DiagnosticsPolicy { get; set; }

    /// <summary>Whether the artifact must execute inside or host a PowerShell runtime.</summary>
    public bool RequiresPowerShellRuntime { get; set; }

    /// <summary>Whether any unit executes through dynamic PowerShell script semantics.</summary>
    public bool UsesPowerShellRuntimeFallback { get; set; }

    /// <summary>Versioned semantic profile for runtime-free Strict artifacts.</summary>
    public PowerShellCompilationSemanticProfile? SemanticProfile { get; set; }

    /// <summary>Normalized public CLR ABI, when the artifact exposes runtime-free managed methods.</summary>
    public PowerShellCompilationAbiManifest? PublicAbi { get; set; }

    /// <summary>SHA-256 of the complete generated C# input set before compilation.</summary>
    public string GeneratedSourceSha256 { get; set; } = string.Empty;

    /// <summary>Whether generated output embeds authored PowerShell source.</summary>
    public bool ContainsEmbeddedPowerShellSource { get; set; }

    /// <summary>Whether generated code permits dynamic PowerShell source evaluation.</summary>
    public bool AllowsPowerShellRuntimeEvaluation { get; set; }

    /// <summary>Whether the final managed dependency closure was mechanically checked.</summary>
    public bool DependencyClosureVerified { get; set; }

    /// <summary>Delivered artifact-set inspection evidence for runtime-free Strict output.</summary>
    public PowerShellCompilationDependencyClosure? DependencyClosure { get; set; }

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

    /// <summary>Number of authored executable units analyzed by the canonical compiler.</summary>
    public int AnalyzedUnits { get; set; }

    /// <summary>Number of analyzed units emitted into the final typed artifact surface.</summary>
    public int EmittedUnits { get; set; }

    /// <summary>Number of units routed through the PowerShell runtime in the delivered artifact.</summary>
    public int RuntimeRoutedUnits { get; set; }

    /// <summary>Number of analyzed units rejected by semantic compilation and routed to fallback.</summary>
    public int FallbackUnits { get; set; }

    /// <summary>Number of semantically eligible units routed to fallback during artifact shaping.</summary>
    public int ShapedFallbackUnits { get; set; }

    /// <summary>Number of executable units left on the PowerShell runtime path.</summary>
    public int RuntimeFallbackUnits { get; set; }

    /// <summary>Number of unsupported units intentionally absent from a typed-only CLR library.</summary>
    public int OmittedUnits { get; set; }

    /// <summary>Typed compilation coverage among analyzed units.</summary>
    public double CompilationCoveragePercentage { get; set; }

    /// <summary>Exact durable artifact path, or the portable relative artifact path when evidence is embedded in a package.</summary>
    public string ArtifactPath { get; set; } = string.Empty;

    /// <summary>Portable artifact path relative to the compilation evidence file.</summary>
    public string ArtifactRelativePath { get; set; } = string.Empty;

    /// <summary>Durable generated source project path when source emission was requested.</summary>
    public string? GeneratedSourcePath { get; set; }

    /// <summary>SHA-256 of the primary artifact.</summary>
    public string ArtifactSha256 { get; set; } = string.Empty;

    /// <summary>All durable files that form the artifact, including hybrid-module support files.</summary>
    public PowerShellCompilationArtifactFile[] Files { get; set; } = Array.Empty<PowerShellCompilationArtifactFile>();

    /// <summary>Discovered source, module, assembly, and content dependency decisions.</summary>
    public PowerShellCompilationDependency[] Dependencies { get; set; } = Array.Empty<PowerShellCompilationDependency>();

    /// <summary>Locked dependency graph used by analysis, build planning, and deployment validation.</summary>
    public PowerShellCompilationDependencyGraph? DependencyGraph { get; set; }

    /// <summary>Whether the build consumed a separately supplied and validated dependency lock.</summary>
    public bool DependencyLockReviewed { get; set; }

    /// <summary>Versioned command semantic providers used by compiled methods.</summary>
    public PowerShellCompilationCommandProviderContract[] CommandProviders { get; set; } = Array.Empty<PowerShellCompilationCommandProviderContract>();

    /// <summary>Exact package, assembly, dependency, publisher, license, and signature evidence for external providers.</summary>
    public PowerShellCompilationProviderLock? ProviderLock { get; set; }

    /// <summary>Whether provider package resolution matched a separately reviewed provider lock.</summary>
    public bool ProviderLockReviewed { get; set; }

    /// <summary>Hosted advanced-function lifecycle contracts generated for Hybrid binary cmdlets.</summary>
    public PowerShellCompilationLifecycleContract[] Lifecycles { get; set; } = Array.Empty<PowerShellCompilationLifecycleContract>();

    /// <summary>Resource selection totals for the produced artifact.</summary>
    public PowerShellCompilationResourceSummary ResourceSummary { get; set; } = new();

    /// <summary>Source diagnostics retained as honest fallback evidence.</summary>
    public PowerShellCompilationDiagnostic[] Diagnostics { get; set; } = Array.Empty<PowerShellCompilationDiagnostic>();
}

/// <summary>Delivered artifact-set inspection evidence for runtime-free Strict output.</summary>
public sealed class PowerShellCompilationDependencyClosure
{
    /// <summary>Closure evidence schema version.</summary>
    public int SchemaVersion { get; set; } = 3;

    /// <summary>Target framework whose reference pack supplied trusted runtime identities.</summary>
    public string TargetFramework { get; set; } = string.Empty;

    /// <summary>Runtime identifier whose managed/native delivery requirements were verified.</summary>
    public string RuntimeIdentifier { get; set; } = string.Empty;

    /// <summary>Whether every executable dependency format was understood and passed inspection.</summary>
    public bool Verified { get; set; }

    /// <summary>Detected primary artifact container format.</summary>
    public string ArtifactFormat { get; set; } = "ManagedAssembly";

    /// <summary>Number of delivered files whose existence, size, hash, or content was inspected.</summary>
    public int InspectedFiles { get; set; }

    /// <summary>Number of managed assemblies whose CLR metadata was inspected.</summary>
    public int ManagedAssemblies { get; set; }

    /// <summary>Number of delivered native libraries whose presence was inspected.</summary>
    public int NativeLibraries { get; set; }

    /// <summary>Native imports resolved to exact reviewed runtime-pack assets.</summary>
    public List<string> ReviewedNativeImports { get; set; } = new();

    /// <summary>Native imports classified by the explicit target operating-system ABI.</summary>
    public List<string> TargetAbiNativeImports { get; set; } = new();

    /// <summary>Exact delivered native runtime-pack content identities.</summary>
    public List<PowerShellCompilationDeliveredNativeDependency> DeliveredNativeDependencies { get; set; } = new();

    /// <summary>Number of entries read from a .NET single-file manifest.</summary>
    public int BundledEntries { get; set; }

    /// <summary>NativeAOT primary executable format, architecture, hash, and imported libraries.</summary>
    public PowerShellCompilationNativeExecutableEvidence? NativeExecutable { get; set; }

    /// <summary>Number of reviewed runtime-pack assemblies rewritten by the selected SDK optimization pipeline.</summary>
    public int TransformedManagedAssemblies { get; set; }

    /// <summary>Exact input and delivered content identities for inspected managed dependencies.</summary>
    public List<PowerShellCompilationDeliveredDependency> DeliveredDependencies { get; set; } = new();

    /// <summary>Formats or dependencies that prevented fail-closed certification.</summary>
    public List<string> Limitations { get; set; } = new();
}

/// <summary>Content-level derivation evidence for one delivered managed dependency.</summary>
public sealed class PowerShellCompilationDeliveredDependency
{
    /// <summary>Stable managed assembly display identity.</summary>
    public string Identity { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exact delivered bytes.</summary>
    public string DeliveredSha256 { get; set; } = string.Empty;

    /// <summary>Reviewed input SHA-256 identities that could supply this assembly.</summary>
    public string[] ReviewedInputSha256 { get; set; } = Array.Empty<string>();

    /// <summary>Content relationship: Exact or SdkOptimization.</summary>
    public string Derivation { get; set; } = "Exact";
}

/// <summary>Content-level evidence for one delivered native runtime-pack dependency.</summary>
public sealed class PowerShellCompilationDeliveredNativeDependency
{
    /// <summary>Delivered bundle or file name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exact delivered bytes.</summary>
    public string DeliveredSha256 { get; set; } = string.Empty;

    /// <summary>Reviewed runtime-pack source identity.</summary>
    public string ReviewedSource { get; set; } = string.Empty;
}

/// <summary>Mechanically inspected NativeAOT executable evidence.</summary>
public sealed class PowerShellCompilationNativeExecutableEvidence
{
    /// <summary>Executable container format: PE, ELF, or MachO.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Architecture encoded in the executable header.</summary>
    public string Architecture { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exact delivered executable.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Native libraries declared by the executable import/load table.</summary>
    public string[] ImportedLibraries { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Hash evidence for one durable file that forms a PowerShell compilation artifact.
/// </summary>
public sealed class PowerShellCompilationArtifactFile
{
    /// <summary>Full durable path, or the portable relative path when evidence is embedded in a package.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Portable file path relative to the compilation evidence file.</summary>
    public string RelativePath { get; set; } = string.Empty;

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

    /// <summary>Durable generated source project path when requested.</summary>
    public string? GeneratedSourcePath { get; set; }

    /// <summary>Failure message.</summary>
    public string? Error { get; set; }

    /// <summary>Combined bounded output from the generated .NET build.</summary>
    public string BuildOutput { get; set; } = string.Empty;

    /// <summary>Redacted source-mapped diagnosis when the build fails.</summary>
    public PowerShellCompilationFailure? Failure { get; set; }

    /// <summary>Artifact evidence when successful.</summary>
    public PowerShellCompilationArtifactManifest? Manifest { get; set; }
}
