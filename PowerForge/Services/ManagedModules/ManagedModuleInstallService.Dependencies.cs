namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
    private const int SeedDependencyBeforeFanoutThreshold = 16;

    private void StartDependencyVersionSelectionPrewarm(
        ManagedModuleInstallRequest request,
        ManagedModulePackageMetadata? metadata,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        if (request.SkipDependencyCheck ||
            request.AuthenticodeCheck ||
            request.Credential is not null)
        {
            return;
        }

        var dependencies = SelectDependencies(metadata).ToArray();
        if (dependencies.Length == 0)
            return;

        var dependencyTrustPolicy = ResolveDependencyTrustPolicy(request.TrustPolicy);
        var tasks = dependencies
            .Select(dependency => StartDependencyVersionSelectionPrewarmCoreAsync(
                request,
                dependency,
                dependencyTrustPolicy,
                context,
                cancellationToken))
            .Where(static task => task is not null)
            .Cast<Task>()
            .ToArray();
        if (tasks.Length == 0)
            return;

        ObserveBackgroundDependencyVersionSelection(Task.WhenAll(tasks));
    }

    private Task? StartDependencyVersionSelectionPrewarmCoreAsync(
        ManagedModuleInstallRequest request,
        ManagedModuleDependencyInfo dependency,
        ManagedModuleTrustPolicy? dependencyTrustPolicy,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        var range = ManagedModuleVersionRange.Parse(dependency.VersionRange);
        if (range.ExactVersion is not null)
            return null;

        if (dependencyTrustPolicy is null && HasSatisfiedDependency(request, dependency.Id, range, context))
            return null;

        return ResolveDependencyVersionAsync(
            request,
            dependency.Id,
            range,
            context,
            cancellationToken);
    }

    private static void ObserveBackgroundDependencyVersionSelection(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<IReadOnlyList<ManagedModuleInstallResult>> InstallDependenciesAsync(
        ManagedModuleInstallRequest request,
        ManagedModulePackageMetadata? metadata,
        string cacheDirectory,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        var dependencies = SelectDependencies(metadata).ToArray();
        if (dependencies.Length == 0)
            return Array.Empty<ManagedModuleInstallResult>();

        if (dependencies.Length == 1)
        {
            var singleResult = await InstallDependencyBranchAsync(
                request,
                dependencies[0],
                cacheDirectory,
                context.CreateBranch(),
                cancellationToken).ConfigureAwait(false);

            return new[] { singleResult };
        }

        var concurrencyLimit = ResolveDependencyInstallConcurrency(request);
        var results = new ManagedModuleInstallResult[dependencies.Length];
        var fanoutStart = 0;

        if (dependencies.Length >= SeedDependencyBeforeFanoutThreshold && concurrencyLimit > 1)
        {
            results[0] = await InstallDependencyBranchAsync(
                request,
                dependencies[0],
                cacheDirectory,
                context.CreateBranch(),
                cancellationToken).ConfigureAwait(false);
            fanoutStart = 1;
        }

        var concurrency = Math.Min(dependencies.Length - fanoutStart, concurrencyLimit);

        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var tasks = dependencies
            .Skip(fanoutStart)
            .Select((dependency, offset) => InstallDependencyWithGateAsync(
                request,
                dependency,
                fanoutStart + offset,
                cacheDirectory,
                context.CreateBranch(),
                gate,
                results,
                cancellationToken))
            .ToArray();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            foreach (var task in tasks)
            {
                if (task.Exception is not null)
                    throw task.Exception.GetBaseException();
            }

            throw;
        }

        return results;
    }

    private async Task InstallDependencyWithGateAsync(
        ManagedModuleInstallRequest request,
        ManagedModuleDependencyInfo dependency,
        int index,
        string cacheDirectory,
        ManagedModuleInstallContext context,
        SemaphoreSlim gate,
        ManagedModuleInstallResult[] results,
        CancellationToken cancellationToken)
    {
        var gateWaitStopwatch = System.Diagnostics.Stopwatch.StartNew();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        gateWaitStopwatch.Stop();
        try
        {
            var branchStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await InstallDependencyCoreAsync(
                request,
                dependency,
                cacheDirectory,
                context,
                cancellationToken).ConfigureAwait(false);
            branchStopwatch.Stop();
            result.DependencyQueueWaitElapsed += gateWaitStopwatch.Elapsed;
            result.DependencyBranchElapsed += branchStopwatch.Elapsed;
            results[index] = result;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ManagedModuleInstallResult> InstallDependencyBranchAsync(
        ManagedModuleInstallRequest request,
        ManagedModuleDependencyInfo dependency,
        string cacheDirectory,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        var branchStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await InstallDependencyCoreAsync(
            request,
            dependency,
            cacheDirectory,
            context,
            cancellationToken).ConfigureAwait(false);
        branchStopwatch.Stop();
        result.DependencyBranchElapsed += branchStopwatch.Elapsed;
        return result;
    }

    private async Task<ManagedModuleInstallResult> InstallDependencyCoreAsync(
        ManagedModuleInstallRequest request,
        ManagedModuleDependencyInfo dependency,
        string cacheDirectory,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        var range = ManagedModuleVersionRange.Parse(dependency.VersionRange);
        var dependencyTrustPolicy = ResolveDependencyTrustPolicy(request.TrustPolicy);
        var satisfiedStopwatch = System.Diagnostics.Stopwatch.StartNew();
        ManagedModuleInstallResult satisfiedResult = null!;
        var isSatisfied = dependencyTrustPolicy is null &&
                          TryCreateSatisfiedDependencyResult(request, dependency.Id, range, context, out satisfiedResult);
        satisfiedStopwatch.Stop();
        if (isSatisfied)
        {
            ManagedModuleInstallResult satisfiedInstallResult;
            if (request.RepairInstalledManifestDependencies)
            {
                satisfiedInstallResult = await InstallAsync(
                    CreateDependencyInstallRequest(
                        request,
                        dependency.Id,
                        satisfiedResult.Version,
                        cacheDirectory,
                        dependencyTrustPolicy,
                        range),
                    context,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                satisfiedInstallResult = satisfiedResult;
                satisfiedInstallResult.Elapsed = satisfiedStopwatch.Elapsed;
            }

            satisfiedInstallResult.DependencyVersionRange = dependency.VersionRange;
            return satisfiedInstallResult;
        }

        var dependencyVersionStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var dependencyVersion = await ResolveDependencyVersionAsync(
            request,
            dependency.Id,
            range,
            context,
            cancellationToken).ConfigureAwait(false);
        dependencyVersionStopwatch.Stop();

        var result = await InstallAsync(
            CreateDependencyInstallRequest(
                request,
                dependency.Id,
                dependencyVersion.Version,
                cacheDirectory,
                dependencyTrustPolicy,
                range),
            context,
            cancellationToken).ConfigureAwait(false);
        result.DependencyVersionRange = dependency.VersionRange;
        if (dependencyVersion.Shared)
            result.VersionSelectionWaitElapsed += dependencyVersionStopwatch.Elapsed;
        else
            result.VersionResolutionElapsed += dependencyVersionStopwatch.Elapsed;
        return result;
    }

    private static ManagedModuleInstallRequest CreateDependencyInstallRequest(
        ManagedModuleInstallRequest request,
        string dependencyName,
        string dependencyVersion,
        string cacheDirectory,
        ManagedModuleTrustPolicy? dependencyTrustPolicy,
        ManagedModuleVersionRange range)
        => new()
        {
            Repository = request.Repository,
            Name = dependencyName,
            Version = dependencyVersion,
            VersionPolicy = null,
            IncludePrerelease = request.IncludePrerelease || range.AllowsPrerelease,
            Scope = request.Scope,
            ShellEdition = request.ShellEdition,
            ModuleRoot = request.ModuleRoot,
            PackageCacheDirectory = cacheDirectory,
            PackageCacheDirectoryIsOperationLocal = request.PackageCacheDirectoryIsOperationLocal ||
                                                    string.IsNullOrWhiteSpace(request.PackageCacheDirectory),
            ExpectedPackageSha256 = null,
            TrustPolicy = dependencyTrustPolicy,
            Credential = request.Credential,
            Force = false,
            AllowClobber = request.AllowClobber,
            AcceptLicense = request.AcceptLicense,
            AuthenticodeCheck = request.AuthenticodeCheck,
            DependencyConcurrency = request.DependencyConcurrency,
            SkipDependencyCheck = false,
            RepairInstalledManifestDependencies = request.RepairInstalledManifestDependencies
        };

    private static bool TryCreateSatisfiedDependencyResult(
        ManagedModuleInstallRequest request,
        string dependencyName,
        ManagedModuleVersionRange range,
        ManagedModuleInstallContext context,
        out ManagedModuleInstallResult result)
    {
        result = null!;
        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        var installedVersion = GetInstalledVersions(moduleRoot, dependencyName, context)
            .Where(version => AllowsInstalledDependencyVersion(version, request, range))
            .LastOrDefault();
        if (installedVersion is null)
            return false;

        var modulePath = ResolveInstalledModulePath(moduleRoot, dependencyName, installedVersion);
        result = CreateAlreadyInstalledResult(
            new ManagedModuleInstallRequest
            {
                Repository = request.Repository,
                Name = dependencyName,
                Version = installedVersion,
                Scope = request.Scope,
                ShellEdition = request.ShellEdition,
                ModuleRoot = request.ModuleRoot,
                Credential = request.Credential,
                Force = false,
                AllowClobber = request.AllowClobber,
                AcceptLicense = request.AcceptLicense,
                AuthenticodeCheck = request.AuthenticodeCheck,
                RepairInstalledManifestDependencies = request.RepairInstalledManifestDependencies
            },
            installedVersion,
            moduleRoot,
            modulePath,
            TimeSpan.Zero,
            TimeSpan.Zero,
            repositoryRequestCount: 0,
            installLockWaitElapsed: TimeSpan.Zero);
        context.RecordInstalledVersion(moduleRoot, dependencyName, installedVersion);
        return true;
    }

    private static bool HasSatisfiedDependency(
        ManagedModuleInstallRequest request,
        string dependencyName,
        ManagedModuleVersionRange range,
        ManagedModuleInstallContext context)
    {
        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        return GetInstalledVersions(moduleRoot, dependencyName, context)
            .Any(version => AllowsInstalledDependencyVersion(version, request, range));
    }

    private static bool AllowsInstalledDependencyVersion(
        string version,
        ManagedModuleInstallRequest request,
        ManagedModuleVersionRange range)
    {
        if (ManagedModuleVersionComparer.IsPrerelease(version) &&
            !request.IncludePrerelease &&
            !range.AllowsPrerelease)
            return false;

        return range.IsSatisfiedBy(version);
    }

    private async Task<ManagedModuleInstallResult> CreateAlreadyInstalledResultWithManifestDependencyRepairAsync(
        ManagedModuleInstallRequest request,
        string version,
        string moduleRoot,
        string modulePath,
        TimeSpan elapsed,
        TimeSpan versionResolutionElapsed,
        long repositoryRequestCount,
        TimeSpan installLockWaitElapsed,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken,
        TimeSpan coalescedWaitElapsed = default)
    {
        var result = CreateAlreadyInstalledResult(
            request,
            version,
            moduleRoot,
            modulePath,
            elapsed,
            versionResolutionElapsed,
            repositoryRequestCount,
            installLockWaitElapsed,
            coalescedWaitElapsed);

        if (request.SkipDependencyCheck)
            return result;

        var metadata = CreateInstalledManifestDependencyMetadata(request.Name, version, modulePath);
        if (metadata is null)
            return result;

        var repairRequest = request.RepairInstalledManifestDependencies
            ? request
            : CopyForInstalledManifestDependencyRepair(request);

        var dependencyStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var cacheDirectory = string.IsNullOrWhiteSpace(request.PackageCacheDirectory)
            ? Path.Combine(Path.GetTempPath(), "PFMM.C", NewShortId())
            : Path.GetFullPath(request.PackageCacheDirectory!.Trim().Trim('"'));
        var ownsCache = string.IsNullOrWhiteSpace(request.PackageCacheDirectory);

        try
        {
            var dependencyResults = await InstallDependenciesAsync(
                repairRequest,
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
        finally
        {
            dependencyStopwatch.Stop();
            if (ownsCache)
                ManagedModuleExtractedPackageCache.DeleteDirectoryQuietly(cacheDirectory);
        }
    }

    private static ManagedModuleInstallRequest CopyForInstalledManifestDependencyRepair(ManagedModuleInstallRequest request)
        => new()
        {
            Repository = request.Repository,
            Name = request.Name,
            Version = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            IncludePrerelease = request.IncludePrerelease,
            Scope = request.Scope,
            ShellEdition = request.ShellEdition,
            ModuleRoot = request.ModuleRoot,
            PackageCacheDirectory = request.PackageCacheDirectory,
            PackageCacheDirectoryIsOperationLocal = request.PackageCacheDirectoryIsOperationLocal,
            DependencyConcurrency = request.DependencyConcurrency,
            ExpectedPackageSha256 = request.ExpectedPackageSha256,
            TrustPolicy = request.TrustPolicy,
            Credential = request.Credential,
            Force = request.Force,
            AllowClobber = request.AllowClobber,
            AcceptLicense = request.AcceptLicense,
            AuthenticodeCheck = request.AuthenticodeCheck,
            SkipDependencyCheck = request.SkipDependencyCheck,
            RepairInstalledManifestDependencies = true
        };

    private static ManagedModulePackageMetadata? CreateInstalledManifestDependencyMetadata(
        string moduleName,
        string version,
        string modulePath)
    {
        var manifestPath = ResolveInstalledManifestPath(moduleName, modulePath);
        if (string.IsNullOrWhiteSpace(manifestPath))
            return null;

        var requiredModules = ModuleManifestValueReader.ReadRequiredModules(manifestPath!);
        if (requiredModules.Length == 0)
            return null;

        var externalDependencies = ModuleManifestValueReader
            .ReadPsDataStringOrArray(manifestPath!, "ExternalModuleDependencies")
            .Where(static dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(static dependency => dependency.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dependencies = requiredModules
            .Where(static module => !string.IsNullOrWhiteSpace(module.ModuleName))
            .Where(module => !externalDependencies.Contains(module.ModuleName!.Trim()))
            .Select(static module => new ManagedModuleDependencyInfo
            {
                Id = module.ModuleName.Trim(),
                VersionRange = ToManagedDependencyVersionRange(module),
                TargetFramework = null
            })
            .ToArray();
        if (dependencies.Length == 0)
            return null;

        return new ManagedModulePackageMetadata
        {
            Id = moduleName.Trim(),
            Version = version,
            Dependencies = dependencies,
            ManifestDependencies = dependencies,
            ManifestExternalModuleDependencies = externalDependencies.ToArray(),
            ModuleManifestPath = manifestPath
        };
    }

    private static bool WouldRepairInstalledManifestDependencies(
        ManagedModuleInstallRequest request,
        string moduleRoot,
        string modulePath)
    {
        if (request.SkipDependencyCheck)
            return false;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return WouldRepairInstalledManifestDependenciesCore(request, request.Name, moduleRoot, modulePath, visited);
    }

    private static bool WouldRepairInstalledManifestDependenciesCore(
        ManagedModuleInstallRequest request,
        string moduleName,
        string moduleRoot,
        string modulePath,
        ISet<string> visited)
    {
        if (!visited.Add(CreateManifestRepairVisitKey(moduleName, modulePath)))
            return false;

        var metadata = CreateInstalledManifestDependencyMetadata(moduleName, string.Empty, modulePath);
        if (metadata is null)
            return false;

        foreach (var dependency in SelectDependencies(metadata))
        {
            var range = ManagedModuleVersionRange.Parse(dependency.VersionRange);
            var installedVersion = GetInstalledVersions(moduleRoot, dependency.Id)
                .Where(version => AllowsInstalledDependencyVersion(version, request, range))
                .LastOrDefault();
            if (installedVersion is null)
                return true;

            var dependencyPath = ResolveInstalledModulePath(moduleRoot, dependency.Id, installedVersion);
            if (WouldRepairInstalledManifestDependenciesCore(request, dependency.Id, moduleRoot, dependencyPath, visited))
                return true;
        }

        return false;
    }

    private static string CreateManifestRepairVisitKey(string moduleName, string modulePath)
        => string.Join("|", moduleName.Trim(), NormalizeManifestRepairPath(modulePath));

    private static string NormalizeManifestRepairPath(string modulePath)
        => Path.GetFullPath(modulePath.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? ResolveInstalledManifestPath(string moduleName, string modulePath)
    {
        if (!Directory.Exists(modulePath))
            return null;

        var expectedManifestPath = Path.Combine(modulePath, moduleName.Trim() + ".psd1");
        if (File.Exists(expectedManifestPath))
            return expectedManifestPath;

        return Directory.EnumerateFiles(modulePath, "*.psd1", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
    }

    private static string? ToManagedDependencyVersionRange(RequiredModuleReference module)
    {
        if (!string.IsNullOrWhiteSpace(module.RequiredVersion))
            return "[" + module.RequiredVersion!.Trim() + "]";
        if (!string.IsNullOrWhiteSpace(module.ModuleVersion) && !string.IsNullOrWhiteSpace(module.MaximumVersion))
            return "[" + module.ModuleVersion!.Trim() + "," + module.MaximumVersion!.Trim() + "]";
        if (!string.IsNullOrWhiteSpace(module.ModuleVersion))
            return module.ModuleVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(module.MaximumVersion))
            return "(," + module.MaximumVersion!.Trim() + "]";

        return null;
    }

    private static long SumRepositoryRequestCount(IReadOnlyList<ManagedModuleInstallResult> dependencyResults)
        => dependencyResults.Sum(static dependency => dependency.RepositoryRequestCount);

    private static long SumPackageRepositoryRequestCount(IReadOnlyList<ManagedModuleInstallResult> dependencyResults)
        => dependencyResults.Sum(static dependency => dependency.PackageRepositoryRequestCount);

    private static long SumPackageRepositoryRedirectCount(IReadOnlyList<ManagedModuleInstallResult> dependencyResults)
        => dependencyResults.Sum(static dependency => dependency.PackageRepositoryRedirectCount);

    private static IReadOnlyList<string> GetInstalledVersions(
        string moduleRoot,
        string moduleName,
        ManagedModuleInstallContext? context = null)
        => context?.GetInstalledVersions(moduleRoot, moduleName) ?? ManagedModuleInstallContext.EnumerateInstalledVersions(moduleRoot, moduleName);

    private async Task<ManagedModuleDependencyVersionSelection> ResolveDependencyVersionAsync(
        ManagedModuleInstallRequest request,
        string dependencyName,
        ManagedModuleVersionRange range,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        if (range.ExactVersion is not null)
            return new ManagedModuleDependencyVersionSelection(range.ExactVersion, shared: false);

        var includePrerelease = request.IncludePrerelease || range.AllowsPrerelease;
        if (range.MaximumVersion is null)
        {
            var latestCacheKey = TryCreateDependencyLatestVersionSelectionKey(
                request,
                dependencyName,
                includePrerelease,
                cancellationToken);
            if (latestCacheKey is not null)
            {
                var latest = await context.GetOrAddOptionalDependencyVersionSelection(
                    latestCacheKey,
                    () => ResolveLatestDependencyVersionOrNullAsync(
                        request,
                        dependencyName,
                        includePrerelease,
                        cancellationToken)).ConfigureAwait(false);
                if (latest.HasValue && range.IsSatisfiedBy(latest.Value.Version))
                    return latest.Value;
            }
            else
            {
                var latestVersion = await _repositoryClient.GetLatestVersionAsync(
                    request.Repository,
                    dependencyName,
                    includePrerelease,
                    request.Credential,
                    cancellationToken).ConfigureAwait(false);
                if (latestVersion is not null)
                {
                    var latest = new ManagedModuleDependencyVersionSelection(latestVersion.Version, shared: false);
                    if (range.IsSatisfiedBy(latest.Version))
                        return latest;
                }
            }
        }

        var cacheKey = TryCreateDependencyVersionSelectionKey(
            request,
            dependencyName,
            range,
            includePrerelease,
            cancellationToken);
        if (cacheKey is not null)
        {
            return await context.GetOrAddDependencyVersionSelection(
                cacheKey,
                () => ResolveDependencyVersionUncachedAsync(
                    request,
                    dependencyName,
                    range,
                    includePrerelease,
                    cancellationToken)).ConfigureAwait(false);
        }

        var version = await ResolveDependencyVersionUncachedAsync(
            request,
            dependencyName,
            range,
            includePrerelease,
            cancellationToken).ConfigureAwait(false);
        return new ManagedModuleDependencyVersionSelection(version, shared: false);
    }

    private async Task<string?> ResolveLatestDependencyVersionOrNullAsync(
        ManagedModuleInstallRequest request,
        string dependencyName,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        var latestVersion = await _repositoryClient.GetLatestVersionAsync(
            request.Repository,
            dependencyName,
            includePrerelease,
            request.Credential,
            cancellationToken).ConfigureAwait(false);

        return latestVersion?.Version;
    }

    private async Task<string> ResolveDependencyVersionUncachedAsync(
        ManagedModuleInstallRequest request,
        string dependencyName,
        ManagedModuleVersionRange range,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        if (range.IsUnbounded)
        {
            var latestVersion = await _repositoryClient.GetLatestVersionAsync(
                request.Repository,
                dependencyName,
                includePrerelease,
                request.Credential,
                cancellationToken).ConfigureAwait(false);
            if (latestVersion is null)
                throw new InvalidOperationException($"No dependency versions of '{dependencyName}' were found in repository '{request.Repository.Name}'.");

            return latestVersion.Version;
        }

        var versions = await _repositoryClient.GetVersionsAsync(
            request.Repository,
            dependencyName,
            includePrerelease,
            request.Credential,
            cancellationToken).ConfigureAwait(false);

        var selected = versions
            .Where(static version => version.Listed)
            .Where(version => range.IsSatisfiedBy(version.Version))
            .LastOrDefault();
        if (selected is null)
            throw new InvalidOperationException($"No dependency version of '{dependencyName}' satisfies range '{range}' in repository '{request.Repository.Name}'.");

        return selected.Version;
    }

    private static string? TryCreateDependencyLatestVersionSelectionKey(
        ManagedModuleInstallRequest request,
        string dependencyName,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        if (request.Credential is not null)
            return null;

        return string.Join(
            "|",
            "dependency-latest-version",
            request.Repository.Kind.ToString(),
            NormalizeDependencyVersionCacheValue(request.Repository.Source),
            NormalizeDependencyVersionCacheValue(dependencyName),
            includePrerelease ? "pre" : "stable");
    }

    private static string? TryCreateDependencyVersionSelectionKey(
        ManagedModuleInstallRequest request,
        string dependencyName,
        ManagedModuleVersionRange range,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        if (request.Credential is not null)
            return null;

        return string.Join(
            "|",
            "dependency-version",
            request.Repository.Kind.ToString(),
            NormalizeDependencyVersionCacheValue(request.Repository.Source),
            NormalizeDependencyVersionCacheValue(dependencyName),
            range.ToString(),
            includePrerelease ? "pre" : "stable");
    }

    private static string NormalizeDependencyVersionCacheValue(string value)
    {
        var trimmed = value.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return trimmed.TrimEnd('/', '\\');
    }

    private static IEnumerable<ManagedModuleDependencyInfo> SelectDependencies(ManagedModulePackageMetadata? metadata)
    {
        if (metadata?.Dependencies is null || metadata.Dependencies.Count == 0)
            return Array.Empty<ManagedModuleDependencyInfo>();

        return metadata.Dependencies
            .Where(static dependency => !string.IsNullOrWhiteSpace(dependency.Id))
            .GroupBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderBy(static dependency => string.IsNullOrWhiteSpace(dependency.TargetFramework) ? 0 : 1)
                .ThenBy(static dependency => dependency.VersionRange, StringComparer.OrdinalIgnoreCase)
                .First());
    }

    private static int ResolveDependencyInstallConcurrency(ManagedModuleInstallRequest request)
        => request.DependencyConcurrency > 0
            ? request.DependencyConcurrency
            : DefaultDependencyInstallConcurrency;

    private static ManagedModuleTrustPolicy? ResolveDependencyTrustPolicy(ManagedModuleTrustPolicy? trustPolicy)
    {
        if (trustPolicy is null || !ManagedModuleTrustEvaluator.HasPolicy(trustPolicy))
            return null;
        var allowedAuthors = ManagedModuleTrustEvaluator.NormalizeAuthors(trustPolicy.AllowedAuthors);
        if (allowedAuthors.Count == 0)
            return null;
        if (trustPolicy.ApplyToDependencies)
            return trustPolicy;

        return null;
    }
}
