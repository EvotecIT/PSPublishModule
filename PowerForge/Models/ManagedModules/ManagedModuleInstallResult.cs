namespace PowerForge;

/// <summary>
/// Result returned by the managed module installer.
/// </summary>
public sealed class ManagedModuleInstallResult
{
    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Installed or selected version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Install outcome.
    /// </summary>
    public ManagedModuleInstallStatus Status { get; set; }

    /// <summary>
    /// Repository name used by the operation.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Repository source used by the operation.
    /// </summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>
    /// Exact version requested by the caller, when supplied.
    /// </summary>
    public string? RequestedVersion { get; set; }

    /// <summary>
    /// Minimum version requested by the caller, when supplied.
    /// </summary>
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// Maximum version requested by the caller, when supplied.
    /// </summary>
    public string? MaximumVersion { get; set; }

    /// <summary>
    /// NuGet-style version policy requested by the caller, when supplied.
    /// </summary>
    public string? VersionPolicy { get; set; }

    /// <summary>
    /// Expected SHA256 hash supplied by the caller, when package integrity verification was requested.
    /// </summary>
    public string? ExpectedPackageSha256 { get; set; }

    /// <summary>
    /// True when the caller required the repository to be trusted.
    /// </summary>
    public bool RequireTrustedRepository { get; set; }

    /// <summary>
    /// Package authors allowed by the caller's trust policy.
    /// </summary>
    public IReadOnlyList<string> AllowedAuthors { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Module root that contains module folders.
    /// </summary>
    public string ModuleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Versioned module directory.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Elapsed time spent by this install operation.
    /// </summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>
    /// Elapsed time spent resolving the selected package version before delivery.
    /// </summary>
    public TimeSpan VersionResolutionElapsed { get; set; }

    /// <summary>
    /// Elapsed time spent waiting for another in-flight install of the same resolved module target.
    /// </summary>
    public TimeSpan CoalescedWaitElapsed { get; set; }

    /// <summary>
    /// Elapsed time spent waiting for the per-module install lock.
    /// </summary>
    public TimeSpan InstallLockWaitElapsed { get; set; }

    /// <summary>
    /// Package download or copy result when the install performed package delivery.
    /// </summary>
    public ManagedModuleDownloadResult? Download { get; set; }

    /// <summary>
    /// Authenticode validation evidence when signature checking was requested.
    /// </summary>
    public ManagedModuleAuthenticodeVerificationResult? AuthenticodeVerification { get; set; }

    /// <summary>
    /// Elapsed time spent downloading or copying the package into the package cache.
    /// </summary>
    public TimeSpan DownloadElapsed { get; set; }

    /// <summary>
    /// Number of files extracted into the module directory.
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Number of extracted package bytes.
    /// </summary>
    public long ExtractedBytes { get; set; }

    /// <summary>
    /// Elapsed time spent extracting the package archive.
    /// </summary>
    public TimeSpan ExtractionElapsed { get; set; }

    /// <summary>
    /// True when module files were materialized from the expanded package cache instead of directly from the package archive.
    /// </summary>
    public bool ExtractionFromCache { get; set; }

    /// <summary>
    /// Elapsed time spent waiting for exclusive access to the expanded package cache before materialization.
    /// </summary>
    public TimeSpan ExtractionCacheLockWaitElapsed { get; set; }

    /// <summary>
    /// Elapsed time spent installing dependencies before this module was promoted.
    /// </summary>
    public TimeSpan DependencyElapsed { get; set; }

    /// <summary>
    /// Elapsed time spent moving the staged module into the final module root.
    /// </summary>
    public TimeSpan PromotionElapsed { get; set; }

    /// <summary>
    /// Repository HTTP request attempts observed during this install operation, including dependencies.
    /// </summary>
    public long RepositoryRequestCount { get; set; }

    /// <summary>
    /// Repository HTTP request attempts used to deliver this package, excluding dependency version resolution and dependency package delivery.
    /// </summary>
    public long PackageRepositoryRequestCount { get; set; }

    /// <summary>
    /// Repository HTTP redirects followed while delivering this package, excluding dependency version resolution and dependency package delivery.
    /// </summary>
    public long PackageRepositoryRedirectCount { get; set; }

    /// <summary>
    /// Receipt written after successful delivery, when the operation changed disk state.
    /// </summary>
    public ManagedModuleReceipt? Receipt { get; set; }

    /// <summary>
    /// Full path to the receipt written after successful delivery.
    /// </summary>
    public string? ReceiptPath { get; set; }

    /// <summary>
    /// Dependency install results completed before this module was promoted.
    /// </summary>
    public IReadOnlyList<ManagedModuleInstallResult> DependencyResults { get; set; } = Array.Empty<ManagedModuleInstallResult>();
}
