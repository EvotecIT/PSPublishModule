namespace PowerForge;

/// <summary>Resolved actions shared by repository and module package builds.</summary>
internal readonly struct ProjectBuildEffectiveActions
{
    internal ProjectBuildEffectiveActions(bool updateVersions, bool build, bool publishNuGet, bool publishGitHub)
    {
        UpdateVersions = updateVersions;
        Build = build;
        PublishNuGet = publishNuGet;
        PublishGitHub = publishGitHub;
    }

    internal bool UpdateVersions { get; }
    internal bool Build { get; }
    internal bool PublishNuGet { get; }
    internal bool PublishGitHub { get; }
}
