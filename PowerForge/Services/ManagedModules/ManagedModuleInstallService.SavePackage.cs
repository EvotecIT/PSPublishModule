using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
    private async Task<ManagedModuleInstallResult> SaveResolvedPackageAsync(
        ManagedModuleInstallRequest request,
        ManagedModuleVersionInfo versionInfo,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken,
        System.Diagnostics.Stopwatch stopwatch,
        ManagedModuleRepositoryClient.RepositoryRequestScope requestScope,
        TimeSpan versionResolutionElapsed,
        TimeSpan installLockWaitElapsed,
        string version,
        string moduleRoot,
        string packagePath,
        string cacheDirectory,
        bool ownsCache)
    {
        var stageRoot = CreateStageRoot(moduleRoot, request.Name);
        try
        {
            using (AcquireInstallLock(moduleRoot, request.Name, cancellationToken, out var initialLockWaitElapsed))
            {
                installLockWaitElapsed += initialLockWaitElapsed;
                if (File.Exists(packagePath) &&
                    !request.Force &&
                    !RequiresPackageDownloadBeforeNoOp(request))
                {
                    _logger.Verbose($"Managed module save skipped existing package: {packagePath}");
                    return await CreateAlreadySavedPackageResultAsync(
                        request,
                        version,
                        moduleRoot,
                        packagePath,
                        stopwatch.Elapsed,
                        versionResolutionElapsed,
                        requestScope.Count,
                        installLockWaitElapsed,
                        cacheDirectory,
                        context,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            var repositoryDependencyHintMetadata = CreateRepositoryDependencyHintMetadata(versionInfo);
            StartDependencyVersionSelectionPrewarm(
                request,
                repositoryDependencyHintMetadata,
                context,
                cancellationToken);
            StartDependencyPackagePrefetch(
                request,
                repositoryDependencyHintMetadata,
                context,
                cancellationToken);

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
            StartDependencyVersionSelectionPrewarm(request, download.Metadata, context, cancellationToken);
            StartDependencyPackagePrefetch(request, download.Metadata, context, cancellationToken);

            ManagedModuleAuthenticodeVerificationResult? authenticode = null;
            ManagedModuleArchiveExtractionResult? extraction = null;
            if (request.AuthenticodeCheck)
            {
                var validationModulePath = CreateStageModulePath(stageRoot, request.Name, version);
                extraction = _extractor.ExtractPackage(download.PackagePath, validationModulePath, download.Metadata?.Id);
                authenticode = _authenticodeVerifier.VerifyDirectory(validationModulePath);
            }

            System.Diagnostics.Stopwatch dependencyStopwatch;
            IReadOnlyList<ManagedModuleInstallResult> dependencyResults;
            if (request.SkipDependencyCheck)
            {
                dependencyStopwatch = System.Diagnostics.Stopwatch.StartNew();
                dependencyResults = Array.Empty<ManagedModuleInstallResult>();
            }
            else
            {
                dependencyStopwatch = System.Diagnostics.Stopwatch.StartNew();
                dependencyResults = await InstallDependenciesAsync(request, download.Metadata, cacheDirectory, context, cancellationToken).ConfigureAwait(false);
            }

            dependencyStopwatch.Stop();

            var promotionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var promotionLockWaitElapsed = TimeSpan.Zero;
            var promotionMoveElapsed = TimeSpan.Zero;
            var promotionHadExistingTarget = false;
            using (AcquirePromotionLock(moduleRoot, cancellationToken, out var promotionGateWaitElapsed))
            {
                promotionLockWaitElapsed += promotionGateWaitElapsed;
                installLockWaitElapsed += promotionGateWaitElapsed;

                using (AcquireInstallLock(moduleRoot, request.Name, cancellationToken, out var resolvedPromotionLockWaitElapsed))
                {
                    promotionLockWaitElapsed += resolvedPromotionLockWaitElapsed;
                    installLockWaitElapsed += resolvedPromotionLockWaitElapsed;

                    if (File.Exists(packagePath) &&
                        !request.Force &&
                        !RequiresPackageDownloadBeforeNoOp(request))
                    {
                        _logger.Verbose($"Managed module save skipped concurrently saved package: {packagePath}");
                        var existing = await CreateAlreadySavedPackageResultAsync(
                            request,
                            version,
                            moduleRoot,
                            packagePath,
                            stopwatch.Elapsed,
                            versionResolutionElapsed,
                            requestScope.Count,
                            installLockWaitElapsed,
                            cacheDirectory,
                            context,
                            cancellationToken).ConfigureAwait(false);
                        existing.DependencyResults = dependencyResults;
                        existing.DependencyElapsed = dependencyStopwatch.Elapsed;
                        existing.RepositoryRequestCount += SumRepositoryRequestCount(dependencyResults);
                        existing.PackageRepositoryRequestCount += SumPackageRepositoryRequestCount(dependencyResults);
                        existing.PackageRepositoryRedirectCount += SumPackageRepositoryRedirectCount(dependencyResults);
                        return existing;
                    }

                    promotionHadExistingTarget = File.Exists(packagePath);
                    var promotionMoveStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    CopyPackageToSavedPath(download.PackagePath, packagePath, overwrite: request.Force || RequiresPackageDownloadBeforeNoOp(request));
                    promotionMoveStopwatch.Stop();
                    promotionMoveElapsed = promotionMoveStopwatch.Elapsed;
                }
            }

            promotionStopwatch.Stop();
            var savedDownload = CopyDownloadForSavedPackage(download, packagePath, new FileInfo(packagePath).Length);
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
                ModulePath = packagePath,
                SavedAsNupkg = true,
                Elapsed = stopwatch.Elapsed,
                VersionResolutionElapsed = versionResolutionElapsed,
                InstallLockWaitElapsed = installLockWaitElapsed,
                Download = savedDownload,
                AuthenticodeVerification = authenticode,
                DownloadElapsed = downloadStopwatch.Elapsed,
                FileCount = 1,
                ExtractedBytes = 0,
                ExtractionElapsed = extraction?.Elapsed ?? TimeSpan.Zero,
                ExtractionFromCache = false,
                ExtractionCacheLockWaitElapsed = TimeSpan.Zero,
                DependencyElapsed = dependencyStopwatch.Elapsed,
                PromotionElapsed = promotionStopwatch.Elapsed,
                PromotionLockWaitElapsed = promotionLockWaitElapsed,
                PromotionMoveElapsed = promotionMoveElapsed,
                PromotionHadExistingTarget = promotionHadExistingTarget,
                RepositoryRequestCount = Math.Max(requestScope.Count, packageRepositoryRequestCount) + SumRepositoryRequestCount(dependencyResults),
                PackageRepositoryRequestCount = packageRepositoryRequestCount + SumPackageRepositoryRequestCount(dependencyResults),
                PackageRepositoryRedirectCount = packageRepositoryRedirectCount + SumPackageRepositoryRedirectCount(dependencyResults),
                DependencyResults = dependencyResults
            };
            return result;
        }
        finally
        {
            ManagedModuleExtractedPackageCache.DeleteDirectoryQuietly(stageRoot);
            if (ownsCache)
                ManagedModuleExtractedPackageCache.DeleteDirectoryQuietly(cacheDirectory);
        }
    }

    private async Task<ManagedModuleInstallResult> CreateAlreadySavedPackageResultAsync(
        ManagedModuleInstallRequest request,
        string version,
        string moduleRoot,
        string packagePath,
        TimeSpan elapsed,
        TimeSpan versionResolutionElapsed,
        long repositoryRequestCount,
        TimeSpan installLockWaitElapsed,
        string cacheDirectory,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        var metadata = ReadSavedPackageMetadata(request, version, packagePath);
        ManagedModulePackageIntegrity.VerifyDownload(
            CreateDownloadForExistingSavedPackage(request, version, packagePath, metadata),
            request.ExpectedPackageSha256);
        ManagedModuleTrustEvaluator.ThrowIfPackageRejected(request.Repository, metadata, request.TrustPolicy);
        ThrowIfLicenseAcceptanceRequired(metadata, request);

        var result = CreateAlreadyInstalledResult(
            request,
            version,
            moduleRoot,
            packagePath,
            elapsed,
            versionResolutionElapsed,
            repositoryRequestCount,
            installLockWaitElapsed);
        result.Download = CreateDownloadForExistingSavedPackage(request, version, packagePath, metadata);
        result.FileCount = 1;

        if (request.SkipDependencyCheck)
            return result;

        var dependencyStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var dependencyResults = await InstallDependenciesAsync(
            request,
            metadata,
            cacheDirectory,
            context,
            cancellationToken).ConfigureAwait(false);
        dependencyStopwatch.Stop();

        result.DependencyElapsed = dependencyStopwatch.Elapsed;
        result.DependencyResults = dependencyResults;
        result.RepositoryRequestCount += SumRepositoryRequestCount(dependencyResults);
        result.PackageRepositoryRequestCount += SumPackageRepositoryRequestCount(dependencyResults);
        result.PackageRepositoryRedirectCount += SumPackageRepositoryRedirectCount(dependencyResults);
        return result;
    }

    private ManagedModulePackageMetadata ReadSavedPackageMetadata(
        ManagedModuleInstallRequest request,
        string version,
        string packagePath)
    {
        try
        {
            var metadata = _repositoryClient.ReadPackageMetadata(packagePath);
            if (!metadata.Id.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                ManagedModuleVersionComparer.Instance.Compare(metadata.Version, version) != 0)
            {
                throw new InvalidOperationException(
                    $"Existing saved package '{packagePath}' does not match '{request.Name}' version '{version}'. Use Force to replace it.");
            }

            return metadata;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Existing saved package '{packagePath}' could not be read as '{request.Name}' version '{version}'. Use Force to replace it.",
                ex);
        }
    }

    private ManagedModuleDownloadResult CreateDownloadForExistingSavedPackage(
        ManagedModuleInstallRequest request,
        string version,
        string packagePath,
        ManagedModulePackageMetadata metadata)
        => new()
        {
            Name = request.Name.Trim(),
            Version = version,
            RepositoryName = request.Repository.Name,
            Source = packagePath,
            PackagePath = packagePath,
            BytesWritten = 0,
            FromCache = true,
            PackageSha256 = ComputePackageSha256(packagePath),
            Metadata = metadata
        };

    private static ManagedModuleDownloadResult CopyDownloadForSavedPackage(
        ManagedModuleDownloadResult download,
        string packagePath,
        long bytesWritten)
        => new()
        {
            Name = download.Name,
            Version = download.Version,
            RepositoryName = download.RepositoryName,
            Source = download.Source,
            PackagePath = packagePath,
            BytesWritten = bytesWritten,
            RequestCount = download.RequestCount,
            RedirectCount = download.RedirectCount,
            FromCache = download.FromCache,
            PackageSha256 = download.PackageSha256,
            Metadata = download.Metadata
        };

    private static void CopyPackageToSavedPath(string sourcePath, string destinationPath, bool overwrite)
    {
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory!);

        var tempPath = fullDestinationPath + ".tmp-" + NewShortId();
        var backupPath = fullDestinationPath + ".bak-" + NewShortId();
        try
        {
            File.Copy(sourcePath, tempPath, overwrite: true);
            if (File.Exists(fullDestinationPath))
            {
                if (!overwrite)
                    return;

                File.Replace(tempPath, fullDestinationPath, backupPath, ignoreMetadataErrors: true);
                return;
            }

            File.Move(tempPath, fullDestinationPath);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            catch
            {
            }
        }
    }

    private static string ComputePackageSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return string.Concat(sha256.ComputeHash(stream).Select(static value => value.ToString("x2")));
    }
}
