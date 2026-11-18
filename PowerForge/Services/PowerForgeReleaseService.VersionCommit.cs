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
        versionCommit.SetPublishing(result.PublishesFromSelectedLanes);
        result.VersionCommit = versionCommit.Complete(
            result.Success,
            ResolveVersionCommitName(spec),
            request.ResolvedReleaseVersion ?? result.ModulePlan?.ModuleVersion);
        return result;
    }

    /// <summary>Plans, validation, Apple-only actions and the build half of a two-phase release never commit.</summary>
    private static bool MayPublishFromThisRun(PowerForgeReleaseRequest request)
        => !request.PlanOnly &&
           !request.ValidateOnly &&
           !request.DeferAfterStagingValidation &&
           !request.AppleOnly &&
           request.AppleAction == PowerForgeAppleReleaseAction.Configured;

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

            string? Resolve(string? path)
                => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(Path.Combine(configDirectory, path!.Trim().Trim('"')));

            var directories = new List<string?> { configDirectory };
            if (!request.PackagesOnly && !request.ToolsOnly)
                directories.Add(Resolve(spec.Module?.RepositoryRoot));
            if (!request.ModuleOnly && !request.ToolsOnly)
                directories.Add(Resolve(spec.Packages?.RootPath));
            if (!request.ModuleOnly && !request.PackagesOnly)
                directories.Add(Resolve(spec.Tools?.ProjectRoot));
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
