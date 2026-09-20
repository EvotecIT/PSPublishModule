namespace PowerForge;

public sealed partial class ProjectBuildPublishHostService
{
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
        else if (destination.IndexOf("://", StringComparison.Ordinal) >= 0 || destination.TrimStart().StartsWith("//", StringComparison.Ordinal))
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
