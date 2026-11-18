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
            ResolveVersionCommitDirectory(request),
            spec.CommitVersionChanges,
            PublishesFromThisRun(spec, request));
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

        result.VersionCommit = versionCommit.Complete(
            result.Success,
            ResolveVersionCommitName(spec),
            request.ResolvedReleaseVersion ?? result.ModulePlan?.ModuleVersion);
        return result;
    }

    /// <summary>
    /// True when this run publishes something itself. Plans, validation, local builds and the
    /// build half of a two-phase release never commit version changes.
    /// </summary>
    private static bool PublishesFromThisRun(PowerForgeReleaseSpec spec, PowerForgeReleaseRequest request)
    {
        if (request.PlanOnly || request.ValidateOnly || request.DeferAfterStagingValidation)
            return false;
        if (request.AppleOnly || request.AppleAction != PowerForgeAppleReleaseAction.Configured)
            return false;

        var moduleSelected = spec.Module is not null && !request.PackagesOnly && !request.ToolsOnly && !request.AppleOnly;
        var packagePublishing = !request.ModuleOnly && !request.ToolsOnly &&
                                ((request.PublishNuget ?? spec.Packages?.PublishNuget) == true ||
                                 (request.PublishProjectGitHub ?? spec.Packages?.PublishGitHub) == true);
        if (moduleSelected && ResolveModuleRunMode(spec.Module!, request, packagePublishing) == ConfigurationGateMode.Publish)
            return true;
        if (spec.Packages is not null && packagePublishing)
            return true;
        if (spec.Tools is not null && !request.ModuleOnly && !request.PackagesOnly &&
            (request.PublishToolGitHub ?? spec.Tools.GitHub?.Publish) == true)
            return true;

        return ShouldPublishUnifiedGitHub(spec, request, moduleSelected);
    }

    private static string? ResolveVersionCommitDirectory(PowerForgeReleaseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConfigPath))
            return null;

        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(request.ConfigPath.Trim().Trim('"')));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveVersionCommitName(PowerForgeReleaseSpec spec)
        => FirstNonEmpty(
            spec.Module?.ModuleName,
            spec.GitHub?.Repository,
            spec.Packages?.GitHubRepositoryName);
}
