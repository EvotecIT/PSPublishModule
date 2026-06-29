namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
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
            var singleResult = await InstallDependencyCoreAsync(
                request,
                dependencies[0],
                cacheDirectory,
                context.CreateBranch(),
                cancellationToken).ConfigureAwait(false);

            return new[] { singleResult };
        }

        var concurrency = Math.Min(dependencies.Length, MaxDependencyInstallConcurrency);
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var results = new ManagedModuleInstallResult[dependencies.Length];
        var tasks = dependencies
            .Select((dependency, index) => InstallDependencyWithGateAsync(
                request,
                dependency,
                index,
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
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            results[index] = await InstallDependencyCoreAsync(
                request,
                dependency,
                cacheDirectory,
                context,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
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
        if (dependencyTrustPolicy is null &&
            TryCreateSatisfiedDependencyResult(request, dependency.Id, range, context, out var satisfiedResult))
        {
            return satisfiedResult;
        }

        var dependencyVersion = await ResolveDependencyVersionAsync(
            request,
            dependency.Id,
            range,
            cancellationToken).ConfigureAwait(false);

        return await InstallAsync(
            new ManagedModuleInstallRequest
            {
                Repository = request.Repository,
                Name = dependency.Id,
                Version = dependencyVersion,
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

    private async Task<string> ResolveDependencyVersionAsync(
        ManagedModuleInstallRequest request,
        string dependencyName,
        ManagedModuleVersionRange range,
        CancellationToken cancellationToken)
    {
        if (range.ExactVersion is not null)
            return range.ExactVersion;

        var includePrerelease = request.IncludePrerelease || range.AllowsPrerelease;
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
                .First())
            .OrderBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static ManagedModuleTrustPolicy? ResolveDependencyTrustPolicy(ManagedModuleTrustPolicy? trustPolicy)
    {
        if (trustPolicy is null || !ManagedModuleTrustEvaluator.HasPolicy(trustPolicy))
            return null;
        if (trustPolicy.ApplyToDependencies)
            return trustPolicy;
        if (!trustPolicy.RequireTrustedRepository)
            return null;

        return new ManagedModuleTrustPolicy
        {
            RequireTrustedRepository = true,
            ApplyToDependencies = false
        };
    }
}
