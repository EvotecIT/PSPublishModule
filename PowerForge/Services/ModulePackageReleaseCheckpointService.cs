namespace PowerForge;

/// <summary>
/// Resolves module-owned package lanes and captures their immutable release plans.
/// </summary>
internal sealed class ModulePackageReleaseCheckpointService
{
    private readonly ProjectBuildHostService _projectBuildHostService;

    internal ModulePackageReleaseCheckpointService(ProjectBuildHostService? projectBuildHostService = null)
    {
        _projectBuildHostService = projectBuildHostService ?? new ProjectBuildHostService();
    }

    internal PowerForgeModulePackageReleaseCheckpoint[] Capture(
        string releaseConfigPath,
        PowerForgeReleaseSpec spec)
    {
        var checkpoints = new List<PowerForgeModulePackageReleaseCheckpoint>();
        foreach (var lane in ResolveLanes(releaseConfigPath, spec)
                     .Where(static lane => lane.PublishNuget || lane.PublishGitHub))
        {
            var request = new ProjectBuildHostRequest
            {
                ConfigPath = lane.ResolutionConfigPath,
                ExecuteBuild = false,
                PlanOnly = true,
                UpdateVersions = false,
                Build = false,
                PublishNuget = false,
                PublishGitHub = false
            };
            var execution = lane.Reference is not null
                ? _projectBuildHostService.Execute(request, lane.Reference, lane.ConfigPath)
                : _projectBuildHostService.Execute(request, lane.Inline!, lane.ResolutionConfigPath);
            if (!execution.Success || execution.Result.Release is null)
            {
                throw new InvalidOperationException(
                    $"{lane.Name}: package release plan could not be checkpointed. {execution.ErrorMessage}");
            }

            checkpoints.Add(new PowerForgeModulePackageReleaseCheckpoint
            {
                Key = lane.Key,
                Name = lane.Name,
                ConfigPath = Path.GetFullPath(lane.ConfigPath),
                Release = execution.Result.Release
            });
        }

        return checkpoints.ToArray();
    }

    internal static PowerForgeModulePackageReleaseCheckpoint Restore(
        ModulePackageReleaseLane lane,
        IEnumerable<PowerForgeModulePackageReleaseCheckpoint>? checkpoints)
    {
        var normalizedConfigPath = Path.GetFullPath(lane.ConfigPath);
        var matches = (checkpoints ?? [])
            .Where(checkpoint =>
                !string.IsNullOrWhiteSpace(checkpoint.Key)
                    ? string.Equals(checkpoint.Key, lane.Key, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(checkpoint.Name, lane.Name, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(
                          Path.GetFullPath(checkpoint.ConfigPath),
                          normalizedConfigPath,
                          StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"{lane.Name}: the signed build checkpoint does not contain its package release plan."),
            _ => throw new InvalidOperationException(
                $"{lane.Name}: the signed build checkpoint contains duplicate package release plans.")
        };
    }

    internal static IReadOnlyList<ModulePackageReleaseLane> ResolveLanes(
        string releaseConfigPath,
        PowerForgeReleaseSpec spec)
    {
        if (spec.Module?.IncludesPackages != true)
            return [];
        if (string.IsNullOrWhiteSpace(spec.Module.ConfigPath))
            return [];

        var releaseDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var repositoryRoot = string.IsNullOrWhiteSpace(spec.Module.RepositoryRoot)
            ? releaseDirectory
            : PathTokenProtection.GetFullPath(releaseDirectory, spec.Module.RepositoryRoot!);
        var moduleConfigPath = PathTokenProtection.GetFullPath(repositoryRoot, spec.Module.ConfigPath!);
        var context = new ModulePipelineConfigurationService().Load(moduleConfigPath);
        var lanes = new List<ModulePackageReleaseLane>();
        var segments = context.Spec.Segments ?? [];
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            switch (segment)
            {
                case ConfigurationProjectBuildSegment project when project.Configuration.Enabled:
                {
                    var configPath = ModulePipelineConfigurationService.ResolveProjectBuildConfigurationPath(
                        context,
                        project.Configuration);
                    var publish = new ProjectBuildSupportService(new NullLogger()).LoadConfig(configPath);
                    lanes.Add(new ModulePackageReleaseLane(
                        $"ProjectBuild:{index}",
                        project.Configuration.Name ?? Path.GetFileNameWithoutExtension(configPath),
                        configPath,
                        configPath,
                        project.Configuration,
                        null,
                        project.Configuration.PublishNuget ?? (publish.PublishNuget == true),
                        project.Configuration.PublishGitHub ?? (publish.PublishGitHub == true)));
                    break;
                }
                case ConfigurationPackageBuildSegment package when package.Configuration.Enabled:
                    lanes.Add(new ModulePackageReleaseLane(
                        $"PackageBuild:{index}",
                        package.Configuration.Name ?? "Inline package build",
                        moduleConfigPath,
                        Path.Combine(context.ProjectRoot, Path.GetFileName(moduleConfigPath)),
                        null,
                        package.Configuration,
                        package.Configuration.PublishNuget == true,
                        package.Configuration.PublishGitHub == true));
                    break;
            }
        }

        return lanes;
    }
}

/// <summary>
/// Resolved module-owned package lane used by checkpoint capture and staged publication.
/// </summary>
internal sealed class ModulePackageReleaseLane
{
    internal ModulePackageReleaseLane(
        string key,
        string name,
        string configPath,
        string resolutionConfigPath,
        ProjectBuildConfigurationReference? reference,
        PackageBuildConfiguration? inline,
        bool publishNuget,
        bool publishGitHub)
    {
        Key = key;
        Name = name;
        ConfigPath = configPath;
        ResolutionConfigPath = resolutionConfigPath;
        Reference = reference;
        Inline = inline;
        PublishNuget = publishNuget;
        PublishGitHub = publishGitHub;
    }

    internal string Key { get; }

    internal string Name { get; }

    internal string ConfigPath { get; }

    internal string ResolutionConfigPath { get; }

    internal ProjectBuildConfigurationReference? Reference { get; }

    internal PackageBuildConfiguration? Inline { get; }

    internal bool PublishNuget { get; }

    internal bool PublishGitHub { get; }
}
