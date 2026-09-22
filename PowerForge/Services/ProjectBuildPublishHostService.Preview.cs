namespace PowerForge;

public sealed partial class ProjectBuildPublishHostService
{
    /// <summary>Reloads the configured feed address for an in-memory verification probe without resolving API keys or secret files.</summary>
    /// <remarks>The returned address may contain URL credentials. Do not log or persist it.</remarks>
    public string ResolvePublishDestinationForVerification(string configPath)
    {
        FrameworkCompatibility.NotNullOrWhiteSpace(configPath, nameof(configPath));
        var path = PathValueResolver.Resolve(Directory.GetCurrentDirectory(), configPath);
        var config = new ProjectBuildSupportService(_logger).LoadConfig(path);
        return ProjectBuildPackageFeedResolver.ResolvePublishDestination(config);
    }

    /// <summary>Reloads a referenced module project-build feed with its lane overrides, without resolving API keys or secret files.</summary>
    /// <remarks>The returned address may contain URL credentials. Do not log or persist it.</remarks>
    public string ResolvePublishDestinationForVerification(ProjectBuildConfigurationReference reference, string configPath)
    {
        FrameworkCompatibility.NotNull(reference, nameof(reference));
        FrameworkCompatibility.NotNullOrWhiteSpace(configPath, nameof(configPath));
        var path = PathValueResolver.Resolve(Directory.GetCurrentDirectory(), configPath);
        var config = new ProjectBuildSupportService(_logger).LoadConfig(path);
        ProjectBuildConfigurationAdapter.ApplyReference(config, reference);
        return ProjectBuildPackageFeedResolver.ResolvePublishDestination(config);
    }

    /// <summary>Resolves an inline module package-build feed without resolving API keys or secret files.</summary>
    /// <remarks>The returned address may contain URL credentials. Do not log or persist it.</remarks>
    public string ResolvePublishDestinationForVerification(PackageBuildConfiguration configuration)
    {
        FrameworkCompatibility.NotNull(configuration, nameof(configuration));
        return ProjectBuildPackageFeedResolver.ResolvePublishDestination(
            ProjectBuildConfigurationAdapter.FromPackageBuild(configuration));
    }

    /// <summary>Reads publication flags and destinations without resolving credential files or environment variables.</summary>
    /// <remarks>This is display data, not an executable publication plan or credential-readiness check.</remarks>
    public ProjectPublicationPreview PreviewConfiguration(string configPath)
    {
        FrameworkCompatibility.NotNullOrWhiteSpace(configPath, nameof(configPath));
        var path = PathValueResolver.Resolve(Directory.GetCurrentDirectory(), configPath);
        var config = new ProjectBuildSupportService(_logger).LoadConfig(path);
        var destination = ProjectBuildPackageFeedResolver.ResolvePublishDestination(config);
        var redacted = false;
        if (Uri.TryCreate(destination, UriKind.Absolute, out var uri))
        {
            redacted = !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment);
            if (redacted) destination = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri;
        }
        else if (destination.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                 destination.TrimStart().StartsWith("//", StringComparison.Ordinal) ||
                 destination.TrimStart().StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                 destination.TrimStart().StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            // A malformed network address must not fall back to displaying its raw credentials.
            destination = "Invalid publication URL (details omitted)";
            redacted = true;
        }
        return new ProjectPublicationPreview {
            PublishNuGet = config.PublishNuget == true,
            PublishGitHub = config.PublishGitHub == true,
            NuGetDestination = destination,
            DestinationRedacted = redacted,
            GitHubRepository = string.Join("/", new[] { config.GitHubUsername?.Trim(), config.GitHubRepositoryName?.Trim() }.Where(value => !string.IsNullOrEmpty(value)))
        };
    }
}
