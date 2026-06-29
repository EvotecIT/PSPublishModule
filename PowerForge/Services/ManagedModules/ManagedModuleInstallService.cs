namespace PowerForge;

/// <summary>
/// Installs PowerShell module packages using managed repository and archive operations.
/// </summary>
public sealed partial class ManagedModuleInstallService
{
    private const int MaxDependencyInstallConcurrency = 32;

    private readonly ILogger _logger;
    private readonly ManagedModuleRepositoryClient _repositoryClient;
    private readonly ManagedModuleArchiveExtractor _extractor;
    private readonly ManagedModuleExtractedPackageCache _extractedPackageCache;
    private readonly ManagedModuleAuthenticodeVerifier _authenticodeVerifier;
    private readonly ManagedModuleReceiptStore _receiptStore;

    /// <summary>
    /// Creates a managed module install service.
    /// </summary>
    /// <param name="logger">Logger used for diagnostics.</param>
    /// <param name="repositoryClient">Optional repository client.</param>
    public ManagedModuleInstallService(ILogger logger, ManagedModuleRepositoryClient? repositoryClient = null)
    {
        _logger = logger ?? new NullLogger();
        _repositoryClient = repositoryClient ?? new ManagedModuleRepositoryClient(_logger);
        _extractor = new ManagedModuleArchiveExtractor();
        _extractedPackageCache = new ManagedModuleExtractedPackageCache(_logger);
        _authenticodeVerifier = new ManagedModuleAuthenticodeVerifier();
        _receiptStore = new ManagedModuleReceiptStore();
    }

    /// <summary>
    /// Installs a module package into a versioned PowerShell module directory.
    /// </summary>
    /// <param name="request">Install request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Install result.</returns>
    public async Task<ManagedModuleInstallResult> InstallAsync(
        ManagedModuleInstallRequest request,
        CancellationToken cancellationToken = default)
        => await InstallAsync(request, new ManagedModuleInstallContext(), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Creates a non-mutating install plan for the requested module.
    /// </summary>
    /// <param name="request">Install request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Install plan.</returns>
    public async Task<ManagedModuleInstallPlan> PlanInstallAsync(
        ManagedModuleInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        ManagedModuleTrustEvaluator.ThrowIfRepositoryRejected(request.Repository, request.TrustPolicy);

        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        if (TrySelectInstalledNoOpVersion(request, moduleRoot, out var installedVersion))
        {
            var installedModulePath = Path.Combine(moduleRoot, request.Name.Trim(), installedVersion);
            return CreateInstallPlan(
                request,
                installedVersion,
                moduleRoot,
                installedModulePath,
                exists: true);
        }

        var versionInfo = await ResolveSelectedVersionInfoAsync(request, cancellationToken, resolveExactMetadata: true).ConfigureAwait(false);
        var modulePath = Path.Combine(moduleRoot, request.Name.Trim(), versionInfo.Version);
        return CreateInstallPlan(
            request,
            versionInfo.Version,
            moduleRoot,
            modulePath,
            Directory.Exists(modulePath),
            versionInfo);
    }

    private static ManagedModuleInstallPlan CreateInstallPlan(
        ManagedModuleInstallRequest request,
        string version,
        string moduleRoot,
        string modulePath,
        bool exists,
        ManagedModuleVersionInfo? versionInfo = null)
    {
        var action = exists
            ? request.Force ? ManagedModuleInstallPlanAction.Reinstall : ManagedModuleInstallPlanAction.SkipExisting
            : ManagedModuleInstallPlanAction.Install;

        return new ManagedModuleInstallPlan
        {
            Name = request.Name.Trim(),
            Version = version,
            Action = action,
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            ModuleRoot = moduleRoot,
            ModulePath = modulePath,
            ExistingVersionFound = exists,
            WouldWriteFiles = action != ManagedModuleInstallPlanAction.SkipExisting,
            RequestedVersion = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256),
            AuthenticodeCheck = request.AuthenticodeCheck,
            RequireTrustedRepository = request.TrustPolicy?.RequireTrustedRepository == true,
            AllowedAuthors = ManagedModuleTrustEvaluator.NormalizeAuthors(request.TrustPolicy?.AllowedAuthors),
            License = versionInfo?.License,
            LicenseAcceptanceRequired = versionInfo?.RequireLicenseAcceptance == true,
            LicenseAccepted = request.AcceptLicense
        };
    }

    private async Task<ManagedModuleInstallResult> InstallAsync(
        ManagedModuleInstallRequest request,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        Validate(request);
        ManagedModuleTrustEvaluator.ThrowIfRepositoryRejected(request.Repository, request.TrustPolicy);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        using (ManagedModuleInstallLock.Acquire(moduleRoot, request.Name, cancellationToken))
        {
            if (TrySelectInstalledNoOpVersion(request, moduleRoot, out var installedVersion))
            {
                var installedModulePath = Path.Combine(moduleRoot, request.Name.Trim(), installedVersion);
                _logger.Verbose($"Managed module install skipped existing satisfying version: {installedModulePath}");
                return CreateAlreadyInstalledResult(
                    request,
                    installedVersion,
                    moduleRoot,
                    installedModulePath,
                    stopwatch.Elapsed,
                    TimeSpan.Zero,
                    repositoryRequestCount: 0);
            }
        }

        using var requestScope = _repositoryClient.BeginRequestScope();

        var versionResolutionStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var versionInfo = await ResolveSelectedVersionInfoAsync(request, cancellationToken).ConfigureAwait(false);
        var version = versionInfo.Version;
        versionResolutionStopwatch.Stop();
        var modulePath = Path.Combine(moduleRoot, request.Name.Trim(), version);
        using var dependencyScope = context.Enter(request.Name);

        var coalescingKey = TryCreateInstallCoalescingKey(request, version, moduleRoot);
        if (coalescingKey is not null)
        {
            if (!context.TryBeginInstall(coalescingKey, out var existingInstall, out var pendingInstall, out var runIndependently))
            {
                if (!runIndependently)
                {
                    var coalescedWaitStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    ManagedModuleInstallResult completed;
                    using (context.EnterInstallWait(coalescingKey))
                    {
                        completed = await existingInstall.ConfigureAwait(false);
                    }

                    coalescedWaitStopwatch.Stop();
                    return CreateAlreadyInstalledResult(
                        request,
                        completed.Version,
                        moduleRoot,
                        modulePath,
                        stopwatch.Elapsed,
                        versionResolutionStopwatch.Elapsed,
                        requestScope.Count,
                        coalescedWaitStopwatch.Elapsed);
                }
            }
            else
            {
                using (pendingInstall)
                {
                    try
                    {
                        var result = await InstallResolvedAsync(
                            request,
                            context,
                            cancellationToken,
                            stopwatch,
                            requestScope,
                            versionResolutionStopwatch.Elapsed,
                            version,
                            moduleRoot,
                            modulePath).ConfigureAwait(false);
                        pendingInstall.Complete(result);
                        return result;
                    }
                    catch (Exception exception)
                    {
                        pendingInstall.Fail(exception);
                        throw;
                    }
                }
            }
        }

        return await InstallResolvedAsync(
            request,
            context,
            cancellationToken,
            stopwatch,
            requestScope,
            versionResolutionStopwatch.Elapsed,
            version,
            moduleRoot,
            modulePath).ConfigureAwait(false);
    }

    private static bool TrySelectInstalledNoOpVersion(
        ManagedModuleInstallRequest request,
        string moduleRoot,
        out string version)
    {
        version = string.Empty;
        if (request.Force)
            return false;

        var installedVersion = GetInstalledVersions(moduleRoot, request.Name)
            .Where(candidate => AllowsInstalledNoOpVersion(candidate, request))
            .LastOrDefault();
        if (installedVersion is null)
            return false;

        version = installedVersion;
        return true;
    }

    private static bool AllowsInstalledNoOpVersion(string version, ManagedModuleInstallRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Version))
            return ManagedModuleVersionComparer.Instance.Compare(version, request.Version!.Trim()) == 0;

        var range = ResolveVersionRange(request.VersionPolicy, request.MinimumVersion, request.MaximumVersion);
        if (ManagedModuleVersionComparer.IsPrerelease(version) &&
            !request.IncludePrerelease &&
            !range.AllowsPrerelease)
            return false;

        return range.IsSatisfiedBy(version);
    }

    private async Task<ManagedModuleInstallResult> InstallResolvedAsync(
        ManagedModuleInstallRequest request,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken,
        System.Diagnostics.Stopwatch stopwatch,
        ManagedModuleRepositoryClient.RepositoryRequestScope requestScope,
        TimeSpan versionResolutionElapsed,
        string version,
        string moduleRoot,
        string modulePath)
    {
        var ownsCache = string.IsNullOrWhiteSpace(request.PackageCacheDirectory);
        var cacheDirectory = ownsCache
            ? Path.Combine(Path.GetTempPath(), "PFMM.C", NewShortId())
            : Path.GetFullPath(request.PackageCacheDirectory!.Trim().Trim('"'));
        var stageRoot = CreateStageRoot(moduleRoot);
        var stageModulePath = Path.Combine(stageRoot, request.Name.Trim(), version);

        try
        {
            using (ManagedModuleInstallLock.Acquire(moduleRoot, request.Name, cancellationToken))
            {
                if (Directory.Exists(modulePath) && !request.Force)
                {
                    _logger.Verbose($"Managed module install skipped existing version: {modulePath}");
                    return CreateAlreadyInstalledResult(
                        request,
                        version,
                        moduleRoot,
                        modulePath,
                        stopwatch.Elapsed,
                        versionResolutionElapsed,
                        requestScope.Count);
                }
            }

            var downloadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            ManagedModuleDownloadResult download;
            long packageRepositoryRequestCount;
            long packageRepositoryRedirectCount;
            using (var packageRequestScope = _repositoryClient.BeginRequestScope())
            {
                download = await _repositoryClient.DownloadPackageAsync(
                    request.Repository,
                    request.Name,
                    version,
                    cacheDirectory,
                    request.Credential,
                    cancellationToken).ConfigureAwait(false);
                packageRepositoryRequestCount = packageRequestScope.Count;
                packageRepositoryRedirectCount = packageRequestScope.RedirectCount;
            }

            downloadStopwatch.Stop();
            ManagedModulePackageIntegrity.VerifyDownload(download, request.ExpectedPackageSha256);
            ManagedModuleTrustEvaluator.ThrowIfPackageRejected(request.Repository, download.Metadata, request.TrustPolicy);
            ThrowIfLicenseAcceptanceRequired(download.Metadata, request);
            var extraction = ownsCache
                ? _extractor.ExtractPackage(download.PackagePath, stageModulePath)
                : _extractedPackageCache.MaterializePackage(
                    download.PackagePath,
                    download.PackageSha256,
                    cacheDirectory,
                    stageModulePath,
                    _extractor,
                    cancellationToken);
            var authenticode = request.AuthenticodeCheck
                ? _authenticodeVerifier.VerifyDirectory(stageModulePath)
                : null;
            var finalParent = Path.GetDirectoryName(modulePath) ?? moduleRoot;
            Directory.CreateDirectory(finalParent);
            var dependencyStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var dependencyResults = request.SkipDependencyCheck
                ? Array.Empty<ManagedModuleInstallResult>()
                : await InstallDependenciesAsync(request, download.Metadata, cacheDirectory, context, cancellationToken).ConfigureAwait(false);
            dependencyStopwatch.Stop();

            if (!request.AllowClobber)
                ManagedModuleClobberDetector.ThrowIfConflicts(moduleRoot, request.Name.Trim(), stageModulePath);

            var promotionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            using (ManagedModuleInstallLock.Acquire(moduleRoot, request.Name, cancellationToken))
            {
                if (Directory.Exists(modulePath) && !request.Force)
                {
                    _logger.Verbose($"Managed module install skipped concurrently installed version: {modulePath}");
                    return CreateAlreadyInstalledResult(
                        request,
                        version,
                        moduleRoot,
                        modulePath,
                        stopwatch.Elapsed,
                        versionResolutionElapsed,
                        requestScope.Count);
                }

                PromoteStagedModule(stageModulePath, modulePath);
            }

            CleanupEmptyStage(stageRoot);
            promotionStopwatch.Stop();

            var result = new ManagedModuleInstallResult
            {
                Name = request.Name.Trim(),
                Version = version,
                Status = ManagedModuleInstallStatus.Installed,
                RepositoryName = request.Repository.Name,
                RepositorySource = request.Repository.Source,
                RequestedVersion = request.Version,
                MinimumVersion = request.MinimumVersion,
                MaximumVersion = request.MaximumVersion,
                VersionPolicy = request.VersionPolicy,
                ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256),
                RequireTrustedRepository = request.TrustPolicy?.RequireTrustedRepository == true,
                AllowedAuthors = ManagedModuleTrustEvaluator.NormalizeAuthors(request.TrustPolicy?.AllowedAuthors),
                ModuleRoot = moduleRoot,
                ModulePath = modulePath,
                Elapsed = stopwatch.Elapsed,
                VersionResolutionElapsed = versionResolutionElapsed,
                Download = download,
                AuthenticodeVerification = authenticode,
                DownloadElapsed = downloadStopwatch.Elapsed,
                FileCount = extraction.FileCount,
                ExtractedBytes = extraction.BytesWritten,
                ExtractionElapsed = extraction.Elapsed,
                ExtractionFromCache = extraction.FromCache,
                DependencyElapsed = dependencyStopwatch.Elapsed,
                PromotionElapsed = promotionStopwatch.Elapsed,
                RepositoryRequestCount = requestScope.Count,
                PackageRepositoryRequestCount = packageRepositoryRequestCount,
                PackageRepositoryRedirectCount = packageRepositoryRedirectCount,
                DependencyResults = dependencyResults
            };
            _receiptStore.WriteReceipt(request.Repository, result);
            return result;
        }
        finally
        {
            if (Directory.Exists(stageRoot))
                Directory.Delete(stageRoot, recursive: true);
            if (ownsCache && Directory.Exists(cacheDirectory))
                Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    private async Task<ManagedModuleVersionInfo> ResolveSelectedVersionInfoAsync(
        ManagedModuleInstallRequest request,
        CancellationToken cancellationToken,
        bool resolveExactMetadata = false)
    {
        if (!string.IsNullOrWhiteSpace(request.Version))
        {
            var exactVersion = request.Version!.Trim();
            if (resolveExactMetadata)
            {
                var exactMatch = await TryResolveExactVersionInfoAsync(request, exactVersion, cancellationToken).ConfigureAwait(false);
                if (exactMatch is not null)
                    return exactMatch;
            }

            return CreateRequestedVersionInfo(request, exactVersion);
        }

        var range = ResolveVersionRange(request.VersionPolicy, request.MinimumVersion, request.MaximumVersion);
        if (range.IsUnbounded)
        {
            var latestVersion = await _repositoryClient.GetLatestVersionAsync(
                request.Repository,
                request.Name,
                request.IncludePrerelease,
                request.Credential,
                cancellationToken).ConfigureAwait(false);
            if (latestVersion is null)
                throw new InvalidOperationException($"No versions of '{request.Name}' were found in repository '{request.Repository.Name}'.");

            return latestVersion;
        }

        var versions = await _repositoryClient.GetVersionsAsync(
            request.Repository,
            request.Name,
            request.IncludePrerelease || range.AllowsPrerelease,
            request.Credential,
            cancellationToken).ConfigureAwait(false);

        var latest = versions
            .Where(version => range.IsSatisfiedBy(version.Version))
            .LastOrDefault();
        if (latest is null)
            throw new InvalidOperationException($"No versions of '{request.Name}' satisfying range '{range}' were found in repository '{request.Repository.Name}'.");

        return latest;
    }

    private async Task<ManagedModuleVersionInfo?> TryResolveExactVersionInfoAsync(
        ManagedModuleInstallRequest request,
        string exactVersion,
        CancellationToken cancellationToken)
    {
        var versions = await _repositoryClient.GetVersionsAsync(
            request.Repository,
            request.Name,
            request.IncludePrerelease || ManagedModuleVersionComparer.IsPrerelease(exactVersion),
            request.Credential,
            cancellationToken).ConfigureAwait(false);

        return versions.FirstOrDefault(version => version.Version.Equals(exactVersion, StringComparison.OrdinalIgnoreCase));
    }

    private static ManagedModuleVersionInfo CreateRequestedVersionInfo(ManagedModuleInstallRequest request, string version)
        => new()
        {
            Name = request.Name.Trim(),
            Version = version,
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            IsPrerelease = ManagedModuleVersionComparer.IsPrerelease(version)
        };

    private static ManagedModuleInstallResult CreateAlreadyInstalledResult(
        ManagedModuleInstallRequest request,
        string version,
        string moduleRoot,
        string modulePath,
        TimeSpan elapsed,
        TimeSpan versionResolutionElapsed,
        long repositoryRequestCount,
        TimeSpan coalescedWaitElapsed = default)
        => new()
        {
            Name = request.Name.Trim(),
            Version = version,
            Status = ManagedModuleInstallStatus.AlreadyInstalled,
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            RequestedVersion = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256),
            RequireTrustedRepository = request.TrustPolicy?.RequireTrustedRepository == true,
            AllowedAuthors = ManagedModuleTrustEvaluator.NormalizeAuthors(request.TrustPolicy?.AllowedAuthors),
            ModuleRoot = moduleRoot,
            ModulePath = modulePath,
            Elapsed = elapsed,
            VersionResolutionElapsed = versionResolutionElapsed,
            CoalescedWaitElapsed = coalescedWaitElapsed,
            RepositoryRequestCount = repositoryRequestCount
        };

    private static void Validate(ManagedModuleInstallRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Repository is null)
            throw new ArgumentException("Repository is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Module name is required.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.Version) &&
            (!string.IsNullOrWhiteSpace(request.MinimumVersion) ||
             !string.IsNullOrWhiteSpace(request.MaximumVersion) ||
             !string.IsNullOrWhiteSpace(request.VersionPolicy)))
            throw new ArgumentException("Version cannot be combined with MinimumVersion, MaximumVersion, or VersionPolicy.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.VersionPolicy) &&
            (!string.IsNullOrWhiteSpace(request.MinimumVersion) || !string.IsNullOrWhiteSpace(request.MaximumVersion)))
            throw new ArgumentException("VersionPolicy cannot be combined with MinimumVersion or MaximumVersion.", nameof(request));
        if (request.Scope == ManagedModuleInstallScope.Custom && string.IsNullOrWhiteSpace(request.ModuleRoot))
            throw new ArgumentException("ModuleRoot is required when Scope is Custom.", nameof(request));

        _ = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256);
    }

    private static void ThrowIfLicenseAcceptanceRequired(
        ManagedModulePackageMetadata? metadata,
        ManagedModuleInstallRequest request)
    {
        if (metadata?.RequireLicenseAcceptance != true || request.AcceptLicense)
            return;

        throw new InvalidOperationException(
            $"Package '{metadata.Id}' {metadata.Version} requires license acceptance. Use AcceptLicense to continue.");
    }

    private static ManagedModuleVersionRange ResolveVersionRange(string? versionPolicy, string? minimumVersion, string? maximumVersion)
        => string.IsNullOrWhiteSpace(versionPolicy)
            ? ManagedModuleVersionRange.FromBounds(minimumVersion, maximumVersion)
            : ManagedModuleVersionRange.Parse(versionPolicy);

    private static string CreateStageRoot(string moduleRoot)
    {
#if NET472
        return Path.Combine(Path.GetTempPath(), "PFMM.S", NewShortId());
#else
        var root = Path.GetFullPath(moduleRoot.Trim().Trim('"'))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(root);
        return Path.Combine(string.IsNullOrWhiteSpace(parent) ? root : parent!, ".pfmm-stage-" + NewShortId());
#endif
    }

    private static void CleanupEmptyStage(string stageRoot)
    {
        if (!Directory.Exists(stageRoot))
            return;

        foreach (var directory in Directory.EnumerateDirectories(stageRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(static path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }

    private static void PromoteStagedModule(string stageModulePath, string modulePath)
    {
        var backupPath = default(string);
        try
        {
            if (Directory.Exists(modulePath))
            {
                backupPath = Path.Combine(Path.GetTempPath(), "PFMM.B", NewShortId());
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                Directory.Move(modulePath, backupPath);
            }

            Directory.Move(stageModulePath, modulePath);
            if (backupPath is not null && Directory.Exists(backupPath))
                Directory.Delete(backupPath, recursive: true);
        }
        catch
        {
            RestoreBackup(modulePath, backupPath);
            throw;
        }
    }

    private static void RestoreBackup(string modulePath, string? backupPath)
    {
        if (backupPath is null || !Directory.Exists(backupPath))
            return;

        if (Directory.Exists(modulePath))
            Directory.Delete(modulePath, recursive: true);

        Directory.Move(backupPath, modulePath);
    }

    private static string NewShortId()
        => Guid.NewGuid().ToString("N").Substring(0, 16);
}
