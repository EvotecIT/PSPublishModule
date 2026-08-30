namespace PowerForge;

/// <summary>Portable project manifest for a reproducible PowerShell compilation artifact matrix.</summary>
public sealed class PowerShellCompilationProjectManifest
{
    /// <summary>Project-manifest schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Portable project name used for default artifact and package identities.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Named semantic profile applied to every target unless a future schema explicitly permits per-target profiles.</summary>
    public string SemanticProfileId { get; set; } = "PowerForge.Oracle.PowerShell/7.6";

    /// <summary>Source paths relative to the project manifest.</summary>
    public string[] Sources { get; set; } = Array.Empty<string>();

    /// <summary>Optional entrypoint relative to the project manifest.</summary>
    public string? EntryPoint { get; set; }

    /// <summary>Contained resource selection shared by the artifact matrix.</summary>
    public PowerShellCompilationProjectResourcePolicy Resources { get; set; } = new();

    /// <summary>Provider package paths relative to the project manifest.</summary>
    public string[] ProviderPackages { get; set; } = Array.Empty<string>();

    /// <summary>Provider trust policy applied before contracts enter analysis.</summary>
    public PowerShellCompilationProviderTrustPolicy ProviderTrust { get; set; } = new();

    /// <summary>Artifact variants. Every entry must have a unique exact target-contract identity.</summary>
    public PowerShellCompilationProjectArtifact[] Artifacts { get; set; } = Array.Empty<PowerShellCompilationProjectArtifact>();

    /// <summary>Portable diagnostics, redaction, and evidence retention policy.</summary>
    public PowerShellCompilationDiagnosticsPolicy Diagnostics { get; set; } = new();
}

/// <summary>Portable resource selection for a compilation project.</summary>
public sealed class PowerShellCompilationProjectResourcePolicy
{
    /// <summary>Optional-payload policy.</summary>
    public PowerShellCompilationResourceMode Mode { get; set; } = PowerShellCompilationResourceMode.Declared;

    /// <summary>Contained paths or globs explicitly included.</summary>
    public string[] Include { get; set; } = Array.Empty<string>();

    /// <summary>Contained paths or globs explicitly excluded.</summary>
    public string[] Exclude { get; set; } = Array.Empty<string>();
}

/// <summary>One qualified artifact variant in a PowerShell compilation project.</summary>
public sealed class PowerShellCompilationProjectArtifact
{
    /// <summary>Stable project-local target name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Exact semantic, runtime, architecture, and deployment target.</summary>
    public PowerShellCompilationTargetContract Target { get; set; } = new();

    /// <summary>Artifact output directory relative to the project manifest.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Reviewed dependency-lock path relative to the project manifest.</summary>
    public string DependencyLock { get; set; } = string.Empty;

    /// <summary>Optional reviewed provider-lock path relative to the project manifest.</summary>
    public string? ProviderLock { get; set; }

    /// <summary>Optional public ABI SHA-256 baseline.</summary>
    public string? ExpectedAbiSha256 { get; set; }

    /// <summary>Whether the independently buildable generated project is included.</summary>
    public bool EmitSource { get; set; }

    /// <summary>Whether redacted bound/lowered IR snapshots are included.</summary>
    public bool EmitIr { get; set; }
}

/// <summary>Content-addressed isolated restore evidence for one project environment.</summary>
public sealed class PowerShellCompilationProjectEnvironment
{
    /// <summary>Environment schema version.</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>SHA-256 over the portable project manifest.</summary>
    public string ProjectSha256 { get; set; } = string.Empty;

    /// <summary>Full machine-local NuGet package root. This value is evidence and is not copied into dependency locks.</summary>
    public string PackageRoot { get; set; } = string.Empty;

    /// <summary>Whether acquisition completed without consulting configured network sources.</summary>
    public bool Offline { get; set; }

    /// <summary>Exact package identities verified in the isolated root.</summary>
    public PowerShellCompilationProjectPackage[] Packages { get; set; } = Array.Empty<PowerShellCompilationProjectPackage>();

    /// <summary>Exact NuGet transitive-closure locks consumed for the selected targets.</summary>
    public PowerShellCompilationProjectResolvedLock[] ResolvedLocks { get; set; } = Array.Empty<PowerShellCompilationProjectResolvedLock>();

    /// <summary>Reviewed dependency-lock identities acquired into this environment.</summary>
    public string[] DependencyLockSha256 { get; set; } = Array.Empty<string>();

    /// <summary>SHA-256 over the normalized project, locks, and package identities.</summary>
    public string EnvironmentSha256 { get; set; } = string.Empty;
}

/// <summary>One exact package in an isolated compilation environment.</summary>
public sealed class PowerShellCompilationProjectPackage
{
    /// <summary>NuGet package id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Exact package version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>NuGet SHA-512 content identity.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>SHA-512 of the exact downloaded nupkg bytes in the isolated root.</summary>
    public string ArchiveSha512 { get; set; } = string.Empty;

    /// <summary>SHA-256 over the complete extracted package payload consumed from the isolated root.</summary>
    public string ExtractedFilesSha256 { get; set; } = string.Empty;
}

/// <summary>One target's complete NuGet transitive-closure lock.</summary>
public sealed class PowerShellCompilationProjectResolvedLock
{
    /// <summary>Project-local target identity.</summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>Project-relative packages.lock.json path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>SHA-256 over the exact lock bytes consumed by restore.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>Result for one project workflow invocation.</summary>
public sealed class PowerShellCompilationProjectResult
{
    /// <summary>Project operation name.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Portable project path.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Whether every selected target passed.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Per-target results.</summary>
    public PowerShellCompilationProjectTargetResult[] Targets { get; set; } = Array.Empty<PowerShellCompilationProjectTargetResult>();
}

/// <summary>Result for one qualified project target.</summary>
public sealed class PowerShellCompilationProjectTargetResult
{
    /// <summary>Project-local target name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Exact normalized target-contract SHA-256.</summary>
    public string TargetContractSha256 { get; set; } = string.Empty;

    /// <summary>Whether this target operation passed.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Stable explanatory message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Primary lock, artifact, evidence, or package path.</summary>
    public string? Path { get; set; }

    /// <summary>Dependency-lock SHA-256 when available.</summary>
    public string? DependencyLockSha256 { get; set; }

    /// <summary>Artifact SHA-256 when available.</summary>
    public string? ArtifactSha256 { get; set; }
}
