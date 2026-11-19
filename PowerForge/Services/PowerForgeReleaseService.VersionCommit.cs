namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    /// <summary>
    /// Executes the unified release workflow, then saves the version changes it stamped to Git
    /// when <see cref="PowerForgeReleaseSpec.CommitVersionChanges"/> asks for it.
    /// </summary>
    /// <remarks>
    /// The version commit only reports: it never changes <see cref="PowerForgeReleaseResult.Success"/>.
    /// </remarks>
    public PowerForgeReleaseResult Execute(PowerForgeReleaseSpec spec, PowerForgeReleaseRequest request)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        using var versionCommit = ReleaseVersionCommitSession.Begin(
            _logger,
            ResolveVersionCommitDirectories(spec, request),
            spec.CommitVersionChanges,
            MayPublishFromThisRun(request));
        PowerForgeReleaseResult result;
        try
        {
            result = ExecuteRelease(spec, request);
        }
        catch
        {
            versionCommit.Complete(releaseSucceeded: false, ResolveVersionCommitName(spec), request.ResolvedReleaseVersion);
            throw;
        }

        // The selected lanes are only known once the release has resolved them, which happens
        // before anything is stamped; a run whose selected lanes publish nothing never commits.
        // The build half of a two-phase release records its stamps for the publish half.
        versionCommit.SetPublishing(result.PublishesFromSelectedLanes);
        result.VersionCommit = versionCommit.Complete(
            result.Success,
            ResolveVersionCommitName(spec),
            request.ResolvedReleaseVersion ?? result.ModulePlan?.ModuleVersion,
            CollectReleaseVersions(request, result),
            deferUntilPublished: request.DeferAfterStagingValidation);
        return result;
    }

    /// <summary>
    /// Publishes the outputs of a two-phase release, then commits the version files its build half
    /// recorded when <see cref="PowerForgeReleaseSpec.CommitVersionChanges"/> asks for it.
    /// </summary>
    internal PowerForgeReleaseResult PublishBuiltReleaseOutputs(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        PowerForgeReleaseResult builtResult)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (builtResult is null)
            throw new ArgumentNullException(nameof(builtResult));

        // Nothing is stamped while publishing; only stamps the build half recorded for this
        // release can be committed here.
        using var versionCommit = ReleaseVersionCommitSession.Begin(
            _logger,
            ResolveVersionCommitDirectories(spec, request),
            spec.CommitVersionChanges,
            publishing: !request.PlanOnly && !request.ValidateOnly);
        PowerForgeReleaseResult result;
        try
        {
            result = PublishBuiltReleaseOutputsCore(spec, request, builtResult);
        }
        catch
        {
            versionCommit.Complete(releaseSucceeded: false, ResolveVersionCommitName(spec), request.ResolvedReleaseVersion);
            throw;
        }

        result.VersionCommit = versionCommit.Complete(
            result.Success,
            ResolveVersionCommitName(spec),
            request.ResolvedReleaseVersion ?? result.ModulePlan?.ModuleVersion,
            CollectReleaseVersions(request, result));
        return result;
    }

    /// <summary>Plans, validation and Apple-only actions never commit.</summary>
    private static bool MayPublishFromThisRun(PowerForgeReleaseRequest request)
        => !request.PlanOnly &&
           !request.ValidateOnly &&
           !request.AppleOnly &&
           request.AppleAction == PowerForgeAppleReleaseAction.Configured;

    /// <summary>The versions this release wrote, so only those count as version stamps.</summary>
    private static IEnumerable<string?> CollectReleaseVersions(PowerForgeReleaseRequest request, PowerForgeReleaseResult result)
    {
        var versions = new List<string?> { request.ResolvedReleaseVersion, result.ModulePlan?.ModuleVersion };
        if (!string.IsNullOrWhiteSpace(result.ModulePlan?.ModuleVersion) && !string.IsNullOrWhiteSpace(result.ModulePlan?.PreReleaseTag))
            versions.Add($"{result.ModulePlan!.ModuleVersion}-{result.ModulePlan.PreReleaseTag}");
        // Module-owned package lanes report through their checkpoints instead of Packages.
        var releases = new[] { result.Packages?.Result?.Release }
            .Concat(result.ModulePackagePlans.Select(static checkpoint => checkpoint.Release));
        foreach (var release in releases)
        {
            if (release is null)
                continue;
            versions.Add(release.ResolvedVersion);
            versions.AddRange(release.ResolvedVersionsByProject.Values);
            versions.AddRange(release.Projects.Select(static project => project.NewVersion));
        }

        return versions;
    }

    /// <summary>
    /// True when the lanes this run selected publish something, using the same lane selection
    /// the release itself runs with.
    /// </summary>
    private static bool PublishesFromSelectedLanes(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        bool runModule,
        bool runPackages,
        bool runTools,
        bool publishUnifiedGitHub)
    {
        if (!MayPublishFromThisRun(request))
            return false;

        var packagePublishing =
            (request.PublishNuget ?? spec.Packages?.PublishNuget) == true ||
            (request.PublishProjectGitHub ?? spec.Packages?.PublishGitHub) == true;
        if (runModule && spec.Module is not null &&
            ResolveModuleRunMode(spec.Module, request, !request.ModuleOnly && packagePublishing) == ConfigurationGateMode.Publish &&
            (request.ModuleIncludePublishing != false || spec.Module.IncludesPackages && packagePublishing))
        {
            return true;
        }

        if (runPackages && packagePublishing)
            return true;
        if (runTools && (request.PublishToolGitHub ?? spec.Tools?.GitHub?.Publish) == true)
            return true;

        return publishUnifiedGitHub;
    }

    /// <summary>
    /// The configuration directory and the source checkout of every lane that can stamp versions.
    /// The session refuses to guess when they belong to different checkouts.
    /// </summary>
    private static string?[] ResolveVersionCommitDirectories(PowerForgeReleaseSpec spec, PowerForgeReleaseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConfigPath))
            return Array.Empty<string?>();

        try
        {
            var configDirectory = Path.GetDirectoryName(Path.GetFullPath(request.ConfigPath.Trim().Trim('"')));
            if (string.IsNullOrWhiteSpace(configDirectory))
                return Array.Empty<string?>();

            // A configured lane without its own root works from the configuration directory;
            // otherwise the configuration directory is not a source checkout and is left out,
            // so release.json may live outside the checkout it releases.
            string Resolve(string? path)
                => string.IsNullOrWhiteSpace(path) ? configDirectory : Path.GetFullPath(Path.Combine(configDirectory, path!.Trim().Trim('"')));

            var directories = new List<string?>();
            if (spec.Module is not null && !request.PackagesOnly && !request.ToolsOnly)
                directories.Add(Resolve(spec.Module.RepositoryRoot));
            if (spec.Packages is not null && !request.ModuleOnly && !request.ToolsOnly)
                directories.Add(Resolve(spec.Packages.RootPath));
            if (spec.Tools is not null && !request.ModuleOnly && !request.PackagesOnly)
                directories.Add(Resolve(spec.Tools.ProjectRoot));
            if (directories.Count == 0)
                directories.Add(configDirectory);
            return directories.ToArray();
        }
        catch
        {
            return Array.Empty<string?>();
        }
    }

    private static string? ResolveVersionCommitName(PowerForgeReleaseSpec spec)
        => FirstNonEmpty(
            spec.Module?.ModuleName,
            spec.GitHub?.Repository,
            spec.Packages?.GitHubRepositoryName);
}
