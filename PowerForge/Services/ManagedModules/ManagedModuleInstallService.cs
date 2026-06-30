namespace PowerForge;

/// <summary>
/// Installs PowerShell module packages using managed repository and archive operations.
/// </summary>
public sealed partial class ManagedModuleInstallService
{
    private const int DefaultDependencyInstallConcurrency = 32;
    internal const int MaximumDependencyInstallConcurrency = 256;

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
        if (TrySelectInstalledNoOpVersion(request, moduleRoot, context: null, out var installedVersion))
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
        var requiresVerifiedPackage = !string.IsNullOrWhiteSpace(request.ExpectedPackageSha256);
        var action = exists
            ? request.Force || requiresVerifiedPackage ? ManagedModuleInstallPlanAction.Reinstall : ManagedModuleInstallPlanAction.SkipExisting
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
        var installLockWaitElapsed = TimeSpan.Zero;
        using var dependencyScope = context.Enter(request.Name);
        var knownVersion = string.IsNullOrWhiteSpace(request.Version) ? null : request.Version!.Trim();
        var knownCoalescingKey = knownVersion is null ? null : TryCreateInstallCoalescingKey(request, knownVersion, moduleRoot);
        ManagedModuleInstallPending? preResolvedPendingInstall = null;
        string? preResolvedCoalescingKey = null;

        ManagedModuleInstallResult CompletePreResolvedInstall(ManagedModuleInstallResult result)
        {
            if (preResolvedCoalescingKey is not null)
                context.RecordCompletedInstall(preResolvedCoalescingKey, result);

            if (preResolvedPendingInstall is not null)
            {
                preResolvedPendingInstall.Complete(result);
                preResolvedPendingInstall.Dispose();
                preResolvedPendingInstall = null;
            }

            return result;
        }

        if (knownCoalescingKey is not null && context.TryGetCompletedInstall(knownCoalescingKey, out var completedInstall))
        {
            var completedModulePath = Path.Combine(moduleRoot, request.Name.Trim(), completedInstall.Version);
            _logger.Verbose($"Managed module install skipped operation-local completed target: {completedModulePath}");
            return CreateAlreadyInstalledResult(
                request,
                completedInstall.Version,
                moduleRoot,
                completedModulePath,
                stopwatch.Elapsed,
                TimeSpan.Zero,
                repositoryRequestCount: 0,
                installLockWaitElapsed);
        }

        try
        {
            if (knownCoalescingKey is not null)
            {
                if (!context.TryBeginInstall(knownCoalescingKey, out var existingInstall, out var pendingInstall, out var runIndependently))
                {
                    if (!runIndependently)
                    {
                        var coalescedWaitStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        ManagedModuleInstallResult completed;
                        using (context.EnterInstallWait(knownCoalescingKey))
                        {
                            completed = await existingInstall.ConfigureAwait(false);
                        }

                        coalescedWaitStopwatch.Stop();
                        var completedModulePath = Path.Combine(moduleRoot, request.Name.Trim(), completed.Version);
                        return CreateAlreadyInstalledResult(
                            request,
                            completed.Version,
                            moduleRoot,
                            completedModulePath,
                            stopwatch.Elapsed,
                            TimeSpan.Zero,
                            repositoryRequestCount: 0,
                            installLockWaitElapsed,
                            coalescedWaitStopwatch.Elapsed);
                    }
                }
                else
                {
                    preResolvedPendingInstall = pendingInstall;
                    preResolvedCoalescingKey = knownCoalescingKey;
                }
            }

            using (AcquireInstallLock(moduleRoot, request.Name, cancellationToken, out var initialLockWaitElapsed))
            {
                installLockWaitElapsed += initialLockWaitElapsed;
                if (TrySelectInstalledNoOpVersion(request, moduleRoot, context, out var installedVersion))
                {
                    var installedModulePath = Path.Combine(moduleRoot, request.Name.Trim(), installedVersion);
                    _logger.Verbose($"Managed module install skipped existing satisfying version: {installedModulePath}");
                    var result = CreateAlreadyInstalledResult(
                        request,
                        installedVersion,
                        moduleRoot,
                        installedModulePath,
                        stopwatch.Elapsed,
                        TimeSpan.Zero,
                        repositoryRequestCount: 0,
                        installLockWaitElapsed);

                    context.RecordInstalledVersion(moduleRoot, request.Name, installedVersion);
                    return CompletePreResolvedInstall(result);
                }
            }

            using var requestScope = _repositoryClient.BeginRequestScope();

            var versionResolutionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var versionInfo = await ResolveSelectedVersionInfoAsync(request, cancellationToken).ConfigureAwait(false);
            var version = versionInfo.Version;
            versionResolutionStopwatch.Stop();
            var modulePath = Path.Combine(moduleRoot, request.Name.Trim(), version);

            var coalescingKey = preResolvedCoalescingKey ?? TryCreateInstallCoalescingKey(request, version, moduleRoot);
            if (preResolvedPendingInstall is not null && coalescingKey is not null)
            {
                var result = await InstallResolvedAsync(
                    request,
                    context,
                    cancellationToken,
                    stopwatch,
                    requestScope,
                    versionResolutionStopwatch.Elapsed,
                    installLockWaitElapsed,
                    version,
                    moduleRoot,
                    modulePath).ConfigureAwait(false);
                return CompletePreResolvedInstall(result);
            }

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
                            installLockWaitElapsed,
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
                                installLockWaitElapsed,
                                version,
                                moduleRoot,
                                modulePath).ConfigureAwait(false);
                            context.RecordCompletedInstall(coalescingKey, result);
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
                installLockWaitElapsed,
                version,
                moduleRoot,
                modulePath).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (preResolvedPendingInstall is not null)
            {
                preResolvedPendingInstall.Fail(exception);
                preResolvedPendingInstall.Dispose();
            }

            throw;
        }
    }

    private static bool TrySelectInstalledNoOpVersion(
        ManagedModuleInstallRequest request,
        string moduleRoot,
        ManagedModuleInstallContext? context,
        out string version)
    {
        version = string.Empty;
        if (request.Force)
            return false;
        if (!string.IsNullOrWhiteSpace(request.ExpectedPackageSha256))
            return false;

        var installedVersion = GetInstalledVersions(moduleRoot, request.Name, context)
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

    private static bool RequiresVerifiedPackage(ManagedModuleInstallRequest request)
        => !string.IsNullOrWhiteSpace(request.ExpectedPackageSha256);

    private async Task<ManagedModuleInstallResult> InstallResolvedAsync(
        ManagedModuleInstallRequest request,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken,
        System.Diagnostics.Stopwatch stopwatch,
        ManagedModuleRepositoryClient.RepositoryRequestScope requestScope,
        TimeSpan versionResolutionElapsed,
        TimeSpan installLockWaitElapsed,
        string version,
        string moduleRoot,
        string modulePath)
    {
        var ownsCache = string.IsNullOrWhiteSpace(request.PackageCacheDirectory);
        var cacheDirectory = ownsCache
            ? Path.Combine(Path.GetTempPath(), "PFMM.C", NewShortId())
            : Path.GetFullPath(request.PackageCacheDirectory!.Trim().Trim('"'));
        var moduleName = request.Name.Trim();
        var stageRoot = CreateStageRoot(moduleRoot, moduleName);
        var stageModulePath = CreateStageModulePath(stageRoot, moduleName, version);
        ManagedModuleExtractedPackageLease? directPayloadLease = null;
#if !NET472
        ManagedModuleBufferedPackage? bufferedPackage = null;
#endif

        try
        {
            using (AcquireInstallLock(moduleRoot, request.Name, cancellationToken, out var resolvedLockWaitElapsed))
            {
                installLockWaitElapsed += resolvedLockWaitElapsed;
                if (Directory.Exists(modulePath) && !request.Force && !RequiresVerifiedPackage(request))
                {
                    _logger.Verbose($"Managed module install skipped existing version: {modulePath}");
                    context.RecordInstalledVersion(moduleRoot, request.Name, version);
                    return CreateAlreadyInstalledResult(
                        request,
                        version,
                        moduleRoot,
                        modulePath,
                        stopwatch.Elapsed,
                        versionResolutionElapsed,
                        requestScope.Count,
                        installLockWaitElapsed);
                }
            }

            var downloadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            ManagedModuleDownloadResult download;
            long packageRepositoryRequestCount;
            long packageRepositoryRedirectCount;
            using (var packageRequestScope = _repositoryClient.BeginRequestScope())
            {
#if !NET472
                if (ShouldUseBufferedPackageExtraction(request, ownsCache))
                {
                    bufferedPackage = await DownloadBufferedPackageForInstallAsync(request, version, cancellationToken).ConfigureAwait(false);
                    download = bufferedPackage.Download;
                }
                else
#endif
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
            StartDependencyVersionSelectionPrewarm(request, download.Metadata, context, cancellationToken);
            var validationModulePath = stageModulePath;
            ManagedModuleArchiveExtractionResult? extraction = null;
            if (!ownsCache && !request.Force && !RequiresVerifiedPackage(request))
            {
                directPayloadLease = _extractedPackageCache.TryAcquirePayload(
                    download.PackageSha256,
                    cacheDirectory,
                    cancellationToken);
                if (directPayloadLease is not null)
                    validationModulePath = directPayloadLease.PayloadPath;
            }

            if (directPayloadLease is null)
            {
#if !NET472
                if (bufferedPackage is not null)
                {
                    extraction = await ExtractBufferedPackageForInstallAsync(
                        bufferedPackage,
                        stageModulePath,
                        cancellationToken).ConfigureAwait(false);
                }
                else
#endif
                extraction = ownsCache
                    ? _extractor.ExtractPackage(download.PackagePath, stageModulePath)
                    : _extractedPackageCache.MaterializePackage(
                        download.PackagePath,
                        download.PackageSha256,
                        cacheDirectory,
                        stageModulePath,
                        _extractor,
                        cancellationToken);
            }

            var authenticode = request.AuthenticodeCheck
                ? _authenticodeVerifier.VerifyDirectory(validationModulePath)
                : null;
            var finalParent = Path.GetDirectoryName(modulePath) ?? moduleRoot;
            Directory.CreateDirectory(finalParent);
            var dependencyStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var dependencyResults = request.SkipDependencyCheck
                ? Array.Empty<ManagedModuleInstallResult>()
                : await InstallDependenciesAsync(request, download.Metadata, cacheDirectory, context, cancellationToken).ConfigureAwait(false);
            dependencyStopwatch.Stop();

            var promotionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var promotionLockWaitElapsed = TimeSpan.Zero;
            var promotionMoveElapsed = TimeSpan.Zero;
            var promotionBackupMoveElapsed = TimeSpan.Zero;
            var promotionFinalMoveElapsed = TimeSpan.Zero;
            var promotionBackupCleanupElapsed = TimeSpan.Zero;
            var promotionHadExistingTarget = false;
            var promotionMaterializedDirectly = false;
            var promotionDirectMaterializationElapsed = TimeSpan.Zero;
            using (AcquireInstallLock(moduleRoot, ".promotion", cancellationToken, out var promotionGateWaitElapsed))
            {
                promotionLockWaitElapsed += promotionGateWaitElapsed;
                installLockWaitElapsed += promotionGateWaitElapsed;

                if (!request.AllowClobber)
                    ManagedModuleClobberDetector.ThrowIfConflicts(moduleRoot, request.Name.Trim(), validationModulePath);

                using (AcquireInstallLock(moduleRoot, request.Name, cancellationToken, out var resolvedPromotionLockWaitElapsed))
                {
                    promotionLockWaitElapsed += resolvedPromotionLockWaitElapsed;
                    installLockWaitElapsed += resolvedPromotionLockWaitElapsed;
                    if (Directory.Exists(modulePath) && !request.Force && !RequiresVerifiedPackage(request))
                    {
                        _logger.Verbose($"Managed module install skipped concurrently installed version: {modulePath}");
                        context.RecordInstalledVersion(moduleRoot, request.Name, version);
                        return CreateAlreadyInstalledResult(
                            request,
                            version,
                            moduleRoot,
                            modulePath,
                            stopwatch.Elapsed,
                            versionResolutionElapsed,
                            requestScope.Count,
                            installLockWaitElapsed);
                    }

                    if (directPayloadLease is not null)
                    {
                        try
                        {
                            extraction = _extractedPackageCache.MaterializePackage(directPayloadLease, modulePath, cancellationToken);
                            promotionMaterializedDirectly = true;
                            promotionDirectMaterializationElapsed = extraction.Elapsed;
                        }
                        catch (Exception ex) when (IsRecoverableCacheMaterializationException(ex))
                        {
                            directPayloadLease.Dispose();
                            directPayloadLease = null;
                            extraction = _extractedPackageCache.MaterializePackage(
                                download.PackagePath,
                                download.PackageSha256,
                                cacheDirectory,
                                stageModulePath,
                                _extractor,
                                cancellationToken);
                            if (request.AuthenticodeCheck)
                                authenticode = _authenticodeVerifier.VerifyDirectory(stageModulePath);
                            if (!request.AllowClobber)
                                ManagedModuleClobberDetector.ThrowIfConflicts(moduleRoot, request.Name.Trim(), stageModulePath);

                            var promotionResult = PromoteStagedModule(stageModulePath, modulePath);
                            promotionMoveElapsed = promotionResult.Elapsed;
                            promotionBackupMoveElapsed = promotionResult.BackupMoveElapsed;
                            promotionFinalMoveElapsed = promotionResult.FinalMoveElapsed;
                            promotionBackupCleanupElapsed = promotionResult.BackupCleanupElapsed;
                            promotionHadExistingTarget = promotionResult.HadExistingTarget;
                        }
                    }
                    else
                    {
                        var promotionResult = PromoteStagedModule(stageModulePath, modulePath);
                        promotionMoveElapsed = promotionResult.Elapsed;
                        promotionBackupMoveElapsed = promotionResult.BackupMoveElapsed;
                        promotionFinalMoveElapsed = promotionResult.FinalMoveElapsed;
                        promotionBackupCleanupElapsed = promotionResult.BackupCleanupElapsed;
                        promotionHadExistingTarget = promotionResult.HadExistingTarget;
                    }
                }
            }

            CleanupEmptyStage(stageRoot);
            promotionStopwatch.Stop();
            var materialization = extraction ?? throw new InvalidOperationException("Managed module package materialization did not complete.");

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
                InstallLockWaitElapsed = installLockWaitElapsed,
                Download = download,
                AuthenticodeVerification = authenticode,
                DownloadElapsed = downloadStopwatch.Elapsed,
                FileCount = materialization.FileCount,
                ExtractedBytes = materialization.BytesWritten,
                ExtractionElapsed = materialization.Elapsed,
                ExtractionFromCache = materialization.FromCache,
                ExtractionCacheLockWaitElapsed = materialization.CacheLockWaitElapsed,
                DependencyElapsed = dependencyStopwatch.Elapsed,
                PromotionElapsed = promotionStopwatch.Elapsed,
                PromotionLockWaitElapsed = promotionLockWaitElapsed,
                PromotionMoveElapsed = promotionMoveElapsed,
                PromotionHadExistingTarget = promotionHadExistingTarget,
                PromotionBackupMoveElapsed = promotionBackupMoveElapsed,
                PromotionFinalMoveElapsed = promotionFinalMoveElapsed,
                PromotionBackupCleanupElapsed = promotionBackupCleanupElapsed,
                PromotionMaterializedDirectly = promotionMaterializedDirectly,
                PromotionDirectMaterializationElapsed = promotionDirectMaterializationElapsed,
                RepositoryRequestCount = requestScope.Count,
                PackageRepositoryRequestCount = packageRepositoryRequestCount,
                PackageRepositoryRedirectCount = packageRepositoryRedirectCount,
                DependencyResults = dependencyResults
            };
            context.RecordInstalledVersion(moduleRoot, request.Name, version);
            _receiptStore.WriteReceipt(request.Repository, result);
            return result;
        }
        finally
        {
#if !NET472
            bufferedPackage?.Dispose();
#endif
            directPayloadLease?.Dispose();
            ManagedModuleExtractedPackageCache.DeleteDirectoryQuietly(stageRoot);
            if (ownsCache)
                ManagedModuleExtractedPackageCache.DeleteDirectoryQuietly(cacheDirectory);
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

                throw new InvalidOperationException(
                    $"Version '{exactVersion}' of '{request.Name}' was not found in repository '{request.Repository.Name}'.");
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

            return resolveExactMetadata
                ? await EnrichVersionInfoWithPackageMetadataAsync(
                    request.Repository,
                    latestVersion,
                    request.Credential,
                    cancellationToken).ConfigureAwait(false)
                : latestVersion;
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

        return resolveExactMetadata
            ? await EnrichVersionInfoWithPackageMetadataAsync(
                request.Repository,
                latest,
                request.Credential,
                cancellationToken).ConfigureAwait(false)
            : latest;
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

        var exactMatch = versions.FirstOrDefault(version => version.Version.Equals(exactVersion, StringComparison.OrdinalIgnoreCase));
        return exactMatch is null
            ? null
            : await EnrichVersionInfoWithPackageMetadataAsync(
                request.Repository,
                exactMatch,
                request.Credential,
                cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedModuleVersionInfo> EnrichVersionInfoWithPackageMetadataAsync(
        ManagedModuleRepository repository,
        ManagedModuleVersionInfo versionInfo,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (versionInfo.RequireLicenseAcceptance || !string.IsNullOrWhiteSpace(versionInfo.License))
            return versionInfo;

        var metadata = await _repositoryClient.GetPackageMetadataAsync(
            repository,
            versionInfo.Name,
            versionInfo.Version,
            credential,
            cancellationToken).ConfigureAwait(false);

        return metadata is null
            ? versionInfo
            : CopyVersionInfoWithPackageMetadata(versionInfo, metadata);
    }

    private static ManagedModuleVersionInfo CopyVersionInfoWithPackageMetadata(
        ManagedModuleVersionInfo versionInfo,
        ManagedModulePackageMetadata metadata)
        => new()
        {
            Name = versionInfo.Name,
            Version = versionInfo.Version,
            RepositoryName = versionInfo.RepositoryName,
            RepositorySource = versionInfo.RepositorySource,
            PackageSource = versionInfo.PackageSource,
            IsPrerelease = versionInfo.IsPrerelease,
            Listed = versionInfo.Listed,
            License = metadata.License,
            RequireLicenseAcceptance = metadata.RequireLicenseAcceptance
        };

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
        TimeSpan installLockWaitElapsed,
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
            InstallLockWaitElapsed = installLockWaitElapsed,
            RepositoryRequestCount = repositoryRequestCount
        };

    private static ManagedModuleInstallLock AcquireInstallLock(
        string moduleRoot,
        string moduleName,
        CancellationToken cancellationToken,
        out TimeSpan waitElapsed)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var installLock = ManagedModuleInstallLock.Acquire(moduleRoot, moduleName, cancellationToken);
        stopwatch.Stop();
        waitElapsed = stopwatch.Elapsed;
        return installLock;
    }

    private static void Validate(ManagedModuleInstallRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Repository is null)
            throw new ArgumentException("Repository is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Module name is required.", nameof(request));
        _ = ManagedModulePackageIdentity.RequireSafeId(request.Name, nameof(request));
        if (!string.IsNullOrWhiteSpace(request.Version))
            _ = ManagedModulePackageIdentity.RequireSafeVersion(request.Version!, nameof(request));
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
        if (request.DependencyConcurrency < 0 || request.DependencyConcurrency > MaximumDependencyInstallConcurrency)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.DependencyConcurrency,
                $"DependencyConcurrency must be between 0 and {MaximumDependencyInstallConcurrency}. Use 0 for the engine default.");

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

    private static bool IsRecoverableCacheMaterializationException(Exception ex)
    {
        if (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            return true;

        return ex is AggregateException aggregate &&
               aggregate.InnerExceptions.Count > 0 &&
               aggregate.InnerExceptions.All(IsRecoverableCacheMaterializationException);
    }

    private static string CreateStageRoot(string moduleRoot, string moduleName)
    {
        var root = Path.GetFullPath(moduleRoot.Trim().Trim('"'))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(root, ".pfmm-stage-" + NewShortId());
    }

    private static string CreateStageModulePath(string stageRoot, string moduleName, string version)
    {
        return Path.Combine(stageRoot, ManagedModulePackageIdentity.RequireSafeVersion(version, nameof(version)));
    }

}
