namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
    private void StartDependencyVersionSelectionPrewarm(
        ManagedModuleInstallRequest request,
        ManagedModulePackageMetadata? metadata,
        ManagedModuleInstallContext context,
        CancellationToken cancellationToken)
    {
        if (request.SkipDependencyCheck ||
            request.AuthenticodeCheck ||
            request.Credential is not null ||
            cancellationToken.CanBeCanceled)
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

        var results = new ManagedModuleInstallResult[dependencies.Length];
        var startIndex = 0;
        var concurrency = Math.Min(dependencies.Length, MaxDependencyInstallConcurrency);
        if (dependencies.Length > MaxDependencyInstallConcurrency)
        {
            results[0] = await InstallDependencyBranchAsync(
                request,
                dependencies[0],
                cacheDirectory,
                context.CreateBranch(),
                cancellationToken).ConfigureAwait(false);
            startIndex = 1;
            concurrency = Math.Min(dependencies.Length - startIndex, MaxDependencyInstallConcurrency);
        }

        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var tasks = dependencies
            .Skip(startIndex)
            .Select((dependency, offset) => InstallDependencyWithGateAsync(
                request,
                dependency,
                startIndex + offset,
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
            satisfiedResult.Elapsed = satisfiedStopwatch.Elapsed;
            satisfiedResult.DependencyVersionRange = dependency.VersionRange;
            return satisfiedResult;
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
            new ManagedModuleInstallRequest
            {
                Repository = request.Repository,
                Name = dependency.Id,
                Version = dependencyVersion.Version,
                VersionPolicy = null,
                IncludePrerelease = request.IncludePrerelease || range.AllowsPrerelease,
                Scope = request.Scope,
                ShellEdition = request.ShellEdition,
                ModuleRoot = request.ModuleRoot,
                PackageCacheDirectory = cacheDirectory,
                ExpectedPackageSha256 = null,
                TrustPolicy = dependencyTrustPolicy,
                Credential = request.Credential,
                Force = false,
                AllowClobber = request.AllowClobber,
                AcceptLicense = request.AcceptLicense,
                AuthenticodeCheck = request.AuthenticodeCheck,
                SkipDependencyCheck = false
            },
            context,
            cancellationToken).ConfigureAwait(false);
        result.DependencyVersionRange = dependency.VersionRange;
        if (dependencyVersion.Shared)
            result.VersionSelectionWaitElapsed += dependencyVersionStopwatch.Elapsed;
        else
            result.VersionResolutionElapsed += dependencyVersionStopwatch.Elapsed;
        return result;
    }

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

        var modulePath = Path.Combine(moduleRoot, dependencyName.Trim(), installedVersion);
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
                AuthenticodeCheck = request.AuthenticodeCheck
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
                var latest = await context.GetOrAddDependencyVersionSelection(
                    latestCacheKey,
                    () => ResolveLatestDependencyVersionAsync(
                        request,
                        dependencyName,
                        includePrerelease,
                        cancellationToken)).ConfigureAwait(false);
                if (range.IsSatisfiedBy(latest.Version))
                    return latest;
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

    private async Task<string> ResolveLatestDependencyVersionAsync(
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
        if (latestVersion is null)
            throw new InvalidOperationException($"No dependency versions of '{dependencyName}' were found in repository '{request.Repository.Name}'.");

        return latestVersion.Version;
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
        if (request.Credential is not null || cancellationToken.CanBeCanceled)
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
        if (request.Credential is not null || cancellationToken.CanBeCanceled)
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
