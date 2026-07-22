namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static int CountConfiguredPackageProjects(ProjectBuildConfiguration packages)
    {
        if (packages.ExpectedVersionMapAsInclude && packages.ExpectedVersionMap?.Count > 0)
            return packages.ExpectedVersionMap.Count;
        if (packages.VersionTracks?.Count > 0)
        {
            return packages.VersionTracks.Values.Sum(track =>
                1 + (track.Projects?.Length ?? 0));
        }
        return 1;
    }

    private static int CountConfiguredToolOutputs(PowerForgeToolReleaseSpec tools, DotNetPublishSpec? dotNetSpec)
    {
        if (dotNetSpec is not null)
            return Math.Max(1, dotNetSpec.Targets.Sum(target =>
                Math.Max(1, target.Publish.Frameworks?.Length ?? 0) *
                Math.Max(1, target.Publish.Runtimes?.Length ?? 0) *
                Math.Max(1, target.Publish.Styles?.Length ?? 0)));

        return Math.Max(1, (tools.Targets ?? Array.Empty<PowerForgeToolReleaseTarget>()).Sum(target =>
            Math.Max(1, target.Frameworks?.Length ?? 0) *
            Math.Max(1, target.Runtimes?.Length ?? 0) *
            Math.Max(1, target.Flavors?.Length ?? 0)));
    }

    private void ValidateVersionCoordinationConfiguration(PowerForgeReleaseSpec spec, bool runModule)
    {
        if (!runModule || spec.Module?.SynchronizeVersionWithPackages != true)
            return;

        if (spec.Module.IncludesPackages)
        {
            throw new InvalidOperationException(
                "Module.SynchronizeVersionWithPackages cannot be combined with Module.IncludesPackages. " +
                "The outer release must own the package lane so it can resolve the shared version before building the module.");
        }

        if (spec.Packages is null)
            throw new InvalidOperationException("Module.SynchronizeVersionWithPackages requires a Packages section.");
        if (string.IsNullOrWhiteSpace(spec.Module.VersionPrimaryProject))
            throw new InvalidOperationException("Module.VersionPrimaryProject is required when package version synchronization is enabled.");
    }

    private static bool ShouldCoordinateModuleAndPackageVersions(
        PowerForgeReleaseSpec spec,
        bool runModule,
        bool runPackages)
        => runModule && runPackages && spec.Module?.SynchronizeVersionWithPackages == true;

    private string ResolveCoordinatedModuleVersionFloor(
        PowerForgeModuleReleaseOptions options,
        string releaseConfigPath,
        PowerForgeReleaseRequest request)
    {
        var expectedVersion = request.ModuleVersion ?? options.ModuleVersion;
        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            throw new InvalidOperationException(
                "Module.ModuleVersion is required when package version synchronization is enabled. " +
                "Use an exact version or an X-pattern such as 3.0.X.");
        }

        var configDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var repositoryRoot = Path.GetFullPath(Path.IsPathRooted(options.RepositoryRoot)
            ? options.RepositoryRoot!
            : Path.Combine(configDirectory, string.IsNullOrWhiteSpace(options.RepositoryRoot) ? "." : options.RepositoryRoot!));
        if (string.IsNullOrWhiteSpace(options.ManifestPath))
            throw new InvalidOperationException("Module.ManifestPath is required when package version synchronization is enabled.");

        var manifestPath = Path.GetFullPath(Path.IsPathRooted(options.ManifestPath)
            ? options.ManifestPath!
            : Path.Combine(repositoryRoot, options.ManifestPath!));
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Module manifest was not found: {manifestPath}", manifestPath);

        var moduleName = Path.GetFileNameWithoutExtension(manifestPath);
        var preRelease = request.ModulePreReleaseTag ?? options.PreReleaseTag;
        var step = new ModuleVersionStepper(_logger).Step(
            expectedVersion!,
            moduleName,
            manifestPath,
            prerelease: !string.IsNullOrWhiteSpace(preRelease));
        var candidate = ModulePathTokenFormatter.FormatVersionWithPreRelease(step.Version, preRelease);
        if (!PackageVersionUtility.TryNormalizeExact(candidate, out var normalizedFloor))
            throw new InvalidOperationException($"Coordinated module version floor '{candidate}' is not a valid exact package version.");

        _logger.Info(
            $"Shared release version floor resolved from module '{moduleName}': {normalizedFloor}. " +
            $"Package project '{options.VersionPrimaryProject}' may resolve a higher version.");
        return normalizedFloor;
    }

    private ProjectBuildHostExecutionResult ExecutePackageRelease(
        ProjectBuildConfiguration packages,
        string configPath,
        PowerForgeReleaseRequest request,
        string? configurationOverride,
        bool publishUnifiedGitHub,
        string? releaseVersionFloor = null,
        string? releaseVersionFloorProject = null,
        bool forcePlanOnly = false,
        bool suppressPublishing = false)
    {
        ApplyPackageRequestOverrides(packages, request, configurationOverride);
        var packageRequest = new ProjectBuildHostRequest
        {
            ConfigPath = configPath,
            ExecuteBuild = !forcePlanOnly && !request.PlanOnly && !request.ValidateOnly,
            PlanOnly = forcePlanOnly || request.PlanOnly || request.ValidateOnly ? true : null,
            PublishNuget = suppressPublishing ? false : request.PublishNuget,
            PublishGitHub = suppressPublishing || publishUnifiedGitHub ? false : request.PublishProjectGitHub,
            ReleaseVersionFloor = releaseVersionFloor,
            ReleaseVersionFloorProject = releaseVersionFloorProject
        };

        return _executePackages(packageRequest, packages, configPath);
    }

    private static string ResolveCoordinatedPackageVersion(
        PowerForgeModuleReleaseOptions options,
        ProjectBuildHostExecutionResult packages,
        string versionFloor)
    {
        var release = packages.Result.Release
            ?? throw new InvalidOperationException("The package lane did not return a release plan with resolved versions.");
        var primaryProject = options.VersionPrimaryProject!.Trim();
        if (!release.ResolvedVersionsByProject.TryGetValue(primaryProject, out var resolvedVersion) ||
            string.IsNullOrWhiteSpace(resolvedVersion))
        {
            throw new InvalidOperationException(
                $"The package lane did not resolve a version for Module.VersionPrimaryProject '{primaryProject}'.");
        }

        if (!PackageVersionUtility.TryNormalizeExact(resolvedVersion, out var normalizedVersion))
            throw new InvalidOperationException($"Resolved package version '{resolvedVersion}' is not a valid exact package version.");
        if (PackageVersionUtility.Compare(normalizedVersion, versionFloor) < 0)
        {
            throw new InvalidOperationException(
                $"Resolved package version '{normalizedVersion}' is lower than the coordinated module version floor '{versionFloor}'.");
        }

        return normalizedVersion;
    }

    private static string? NullIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
