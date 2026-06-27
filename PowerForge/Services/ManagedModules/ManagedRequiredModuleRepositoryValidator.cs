namespace PowerForge;

/// <summary>
/// Verifies manifest RequiredModules against a managed repository and optionally mirrors missing packages.
/// </summary>
internal sealed class ManagedRequiredModuleRepositoryValidator
{
    private const string PowerShellGalleryV3 = "https://www.powershellgallery.com/api/v3/index.json";

    private readonly ILogger _logger;
    private readonly ManagedModuleRepositoryClient _repositoryClient;

    public ManagedRequiredModuleRepositoryValidator(ILogger logger, ManagedModuleRepositoryClient? repositoryClient = null)
    {
        _logger = logger ?? new NullLogger();
        _repositoryClient = repositoryClient ?? new ManagedModuleRepositoryClient(_logger);
    }

    public void Validate(
        PublishConfiguration publish,
        ManagedModuleRepository targetRepository,
        RepositoryCredential? targetCredential,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult)
        => ValidateAsync(publish, targetRepository, targetCredential, plan, buildResult).GetAwaiter().GetResult();

    private async Task ValidateAsync(
        PublishConfiguration publish,
        ManagedModuleRepository targetRepository,
        RepositoryCredential? targetCredential,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        CancellationToken cancellationToken = default)
    {
        if (publish is null) throw new ArgumentNullException(nameof(publish));
        if (targetRepository is null) throw new ArgumentNullException(nameof(targetRepository));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));

        var requiredModules = ModulePublisher.GetRequiredModulesForPublish(buildResult, plan);
        if (requiredModules.Length == 0)
            return;

        if (publish.PublishRequiredModules &&
            string.Equals(targetRepository.Name, "PSGallery", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PublishRequiredModules is only supported for private repository targets. Refusing to mirror dependencies to PSGallery.");
        }

        var externalModuleDependencies = RequiredModuleRepositoryValidator.GetExternalModulesForPublish(buildResult, plan);
        var source = publish.PublishRequiredModules
            ? ResolveSourceRepository(publish, targetRepository)
            : null;
        var sourceCredential = ReferenceEquals(source, targetRepository) ? targetCredential : null;
        var mirroredPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitingPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "PowerForge", "managed-required-modules", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(cacheDirectory);
            foreach (var requiredModule in requiredModules)
            {
                if (ModulePublisher.ShouldSkipRepositoryDependencyValidation(requiredModule, externalModuleDependencies))
                {
                    _logger.Info($"Skipping repository dependency verification for required module '{requiredModule.ModuleName}' because it is listed in ExternalModuleDependencies.");
                    continue;
                }

                var range = BuildRange(requiredModule);
                if (await TargetContainsMatchingVersionAsync(targetRepository, targetCredential, requiredModule.ModuleName, range, cancellationToken).ConfigureAwait(false))
                    continue;

                if (publish.PublishRequiredModules)
                {
                    await MirrorPackageAsync(
                        requiredModule.ModuleName,
                        range,
                        source!,
                        sourceCredential,
                        targetRepository,
                        targetCredential,
                        cacheDirectory,
                        mirroredPackages,
                        visitingPackages,
                        cancellationToken).ConfigureAwait(false);

                    if (await TargetContainsMatchingVersionAsync(targetRepository, targetCredential, requiredModule.ModuleName, range, cancellationToken).ConfigureAwait(false))
                        continue;
                }

                missing.Add($"{requiredModule.ModuleName} [{ModulePublisher.FormatRequiredModuleConstraint(requiredModule)}]");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(cacheDirectory))
                    Directory.Delete(cacheDirectory, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }

        if (missing.Count == 0)
            return;

        var message = $"Required module dependency check failed for repository '{targetRepository.Name}'. Missing or incompatible: {string.Join(", ", missing)}.";
        if (!publish.PublishRequiredModules)
            message += $" Enable PublishRequiredModules to mirror missing dependencies from '{ResolveSourceRepositoryName(publish)}' before publish.";

        throw new InvalidOperationException(message);
    }

    private async Task MirrorPackageAsync(
        string packageId,
        ManagedModuleVersionRange range,
        ManagedModuleRepository sourceRepository,
        RepositoryCredential? sourceCredential,
        ManagedModuleRepository targetRepository,
        RepositoryCredential? targetCredential,
        string cacheDirectory,
        ISet<string> mirroredPackages,
        ISet<string> visitingPackages,
        CancellationToken cancellationToken)
    {
        var selected = await SelectSourceVersionAsync(sourceRepository, sourceCredential, packageId, range, cancellationToken).ConfigureAwait(false);
        var packageKey = BuildPackageKey(packageId, selected.Version);
        if (mirroredPackages.Contains(packageKey))
            return;
        if (!visitingPackages.Add(packageKey))
            throw new InvalidOperationException($"Circular dependency detected while mirroring required module package '{packageId}' {selected.Version}.");

        try
        {
            if (await TargetContainsExactVersionAsync(targetRepository, targetCredential, packageId, selected.Version, cancellationToken).ConfigureAwait(false))
            {
                mirroredPackages.Add(packageKey);
                return;
            }

            _logger.Info($"Mirroring required module package '{packageId}' {selected.Version} from '{sourceRepository.Name}' to '{targetRepository.Name}'.");
            var download = await _repositoryClient.DownloadPackageAsync(
                sourceRepository,
                packageId,
                selected.Version,
                cacheDirectory,
                sourceCredential,
                cancellationToken).ConfigureAwait(false);

            foreach (var dependency in SelectPackageDependencies(download.Metadata))
            {
                await MirrorPackageAsync(
                    dependency.Id,
                    ManagedModuleVersionRange.Parse(dependency.VersionRange),
                    sourceRepository,
                    sourceCredential,
                    targetRepository,
                    targetCredential,
                    cacheDirectory,
                    mirroredPackages,
                    visitingPackages,
                    cancellationToken).ConfigureAwait(false);
            }

            var publish = await _repositoryClient.PublishPackageAsync(
                targetRepository,
                download.PackagePath,
                targetCredential,
                force: false,
                cancellationToken).ConfigureAwait(false);
            if (!publish.Published && !publish.Duplicate)
                throw new InvalidOperationException(publish.Message ?? $"Managed required-module publish did not publish '{packageId}' {selected.Version}.");

            mirroredPackages.Add(packageKey);
        }
        finally
        {
            visitingPackages.Remove(packageKey);
        }
    }

    private async Task<ManagedModuleVersionInfo> SelectSourceVersionAsync(
        ManagedModuleRepository sourceRepository,
        RepositoryCredential? sourceCredential,
        string packageId,
        ManagedModuleVersionRange range,
        CancellationToken cancellationToken)
    {
        var versions = await _repositoryClient.GetVersionsAsync(
            sourceRepository,
            packageId,
            range.AllowsPrerelease,
            sourceCredential,
            cancellationToken).ConfigureAwait(false);
        var selected = versions
            .Where(version => range.IsSatisfiedBy(version.Version))
            .LastOrDefault();
        if (selected is null)
            throw new InvalidOperationException(
                $"Required module '{packageId}' is missing in target repository, and no matching version was found in source repository '{sourceRepository.Name}'. Constraint: {range}.");

        return selected;
    }

    private async Task<bool> TargetContainsMatchingVersionAsync(
        ManagedModuleRepository targetRepository,
        RepositoryCredential? targetCredential,
        string packageId,
        ManagedModuleVersionRange range,
        CancellationToken cancellationToken)
    {
        try
        {
            var versions = await _repositoryClient.GetVersionsAsync(
                targetRepository,
                packageId,
                range.AllowsPrerelease,
                targetCredential,
                cancellationToken).ConfigureAwait(false);

            return versions.Any(version => range.IsSatisfiedBy(version.Version));
        }
        catch (Exception ex) when (ModulePublisher.IsRepositoryPackageNotFound(packageId, ex) ||
                                   ex is DirectoryNotFoundException)
        {
            return false;
        }
    }

    private Task<bool> TargetContainsExactVersionAsync(
        ManagedModuleRepository targetRepository,
        RepositoryCredential? targetCredential,
        string packageId,
        string version,
        CancellationToken cancellationToken)
        => TargetContainsMatchingVersionAsync(
            targetRepository,
            targetCredential,
            packageId,
            ManagedModuleVersionRange.Parse("[" + version + "]"),
            cancellationToken);

    private static IEnumerable<ManagedModuleDependencyInfo> SelectPackageDependencies(ManagedModulePackageMetadata? metadata)
    {
        if (metadata is null)
            return Array.Empty<ManagedModuleDependencyInfo>();

        return (metadata.Dependencies ?? Array.Empty<ManagedModuleDependencyInfo>())
            .Concat(metadata.ManifestDependencies ?? Array.Empty<ManagedModuleDependencyInfo>())
            .Where(static dependency => !string.IsNullOrWhiteSpace(dependency.Id))
            .GroupBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderBy(static dependency => string.IsNullOrWhiteSpace(dependency.TargetFramework) ? 0 : 1)
                .ThenBy(static dependency => dependency.VersionRange, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static ManagedModuleVersionRange BuildRange(RequiredModuleReference requiredModule)
    {
        if (!string.IsNullOrWhiteSpace(requiredModule.RequiredVersion))
            return ManagedModuleVersionRange.Parse("[" + requiredModule.RequiredVersion!.Trim() + "]");

        return ManagedModuleVersionRange.FromBounds(requiredModule.ModuleVersion, requiredModule.MaximumVersion);
    }

    private static ManagedModuleRepository ResolveSourceRepository(
        PublishConfiguration publish,
        ManagedModuleRepository targetRepository)
    {
        var source = ResolveSourceRepositoryName(publish);
        if (!string.IsNullOrWhiteSpace(publish.RequiredModuleSourceRepositoryUri))
        {
            return new ManagedModuleRepository(
                source,
                publish.RequiredModuleSourceRepositoryUri!.Trim(),
                ManagedModuleRepositoryKind.Auto,
                trusted: true);
        }

        if (string.Equals(source, "PSGallery", StringComparison.OrdinalIgnoreCase))
            return new ManagedModuleRepository("PSGallery", PowerShellGalleryV3, ManagedModuleRepositoryKind.NuGetV3, trusted: true);

        if (string.Equals(source, targetRepository.Name, StringComparison.OrdinalIgnoreCase))
            return targetRepository;

        if (LooksLikeRepositorySource(source))
            return new ManagedModuleRepository(source, source, ManagedModuleRepositoryKind.Auto, trusted: true);

        throw new InvalidOperationException(
            $"Managed required-module mirroring could not resolve source repository '{source}'. Use PSGallery, a repository URL, a local feed path, or the target repository name.");
    }

    private static string ResolveSourceRepositoryName(PublishConfiguration publish)
        => string.IsNullOrWhiteSpace(publish.RequiredModuleSourceRepository)
            ? "PSGallery"
            : publish.RequiredModuleSourceRepository!.Trim();

    private static bool LooksLikeRepositorySource(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out _))
            return true;

        return Path.IsPathRooted(value) ||
               value.StartsWith(".", StringComparison.Ordinal) ||
               value.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
               value.IndexOf(Path.AltDirectorySeparatorChar) >= 0;
    }

    private static string BuildPackageKey(string packageId, string version)
        => packageId.Trim() + "|" + version.Trim();
}
