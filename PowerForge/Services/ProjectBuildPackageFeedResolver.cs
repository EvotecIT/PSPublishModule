namespace PowerForge;

/// <summary>
/// Resolves reusable NuGet feed settings for project-build package restore, version lookup, and publish.
/// </summary>
internal static class ProjectBuildPackageFeedResolver
{
    private const string DefaultNuGetPublishSource = "https://api.nuget.org/v3/index.json";
    private const string GitHubPackagesHost = "nuget.pkg.github.com";

    /// <summary>
    /// Resolves NuGet feed settings from project-build configuration and existing secret conventions.
    /// </summary>
    public static ProjectBuildPackageFeedSettings Resolve(ProjectBuildConfiguration config, string configDir)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(configDir))
            throw new ArgumentException("Configuration directory is required.", nameof(configDir));

        var githubToken = ResolveGitHubToken(config, configDir);
        var githubPackagesOwner = ResolveGitHubPackagesOwner(config);
        var githubPackagesSource = ResolveGitHubPackagesSource(config, githubPackagesOwner);
        var versionSources = ResolveVersionSources(config, githubPackagesSource);
        var publishSource = ResolvePublishSource(config, githubPackagesSource);
        var publishApiKey = ProjectBuildSupportService.ResolveSecret(
            config.PublishApiKey,
            config.PublishApiKeyFilePath,
            config.PublishApiKeyEnvName,
            configDir);
        if (string.IsNullOrWhiteSpace(publishApiKey) && IsGitHubPackagesSource(publishSource))
            publishApiKey = githubToken;

        return new ProjectBuildPackageFeedSettings
        {
            VersionSources = versionSources,
            VersionSourceCredential = ResolveVersionSourceCredential(config, configDir, versionSources, githubPackagesOwner, githubToken),
            PublishSource = publishSource,
            PublishApiKey = publishApiKey,
            GitHubToken = githubToken,
            GitHubPackagesOwner = githubPackagesOwner
        };
    }

    /// <summary>
    /// Returns the default NuGet publish source used when no project-build source is configured.
    /// </summary>
    public static string GetDefaultPublishSource() => DefaultNuGetPublishSource;

    /// <summary>
    /// Returns true when a source points at GitHub Packages.
    /// </summary>
    public static bool IsGitHubPackagesSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        return Uri.TryCreate(source!.Trim(), UriKind.Absolute, out var uri) &&
               string.Equals(uri.Host, GitHubPackagesHost, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveGitHubToken(ProjectBuildConfiguration config, string configDir)
    {
        var token = ProjectBuildSupportService.ResolveSecret(
            config.GitHubAccessToken,
            config.GitHubAccessTokenFilePath,
            config.GitHubAccessTokenEnvName,
            configDir);
        if (!string.IsNullOrWhiteSpace(token))
            return token;

        if (config.UseGitHubPackages || ContainsGitHubPackagesSource(config.NugetSource) || IsGitHubPackagesSource(config.PublishSource))
            return ResolveFirstEnvironmentSecret("GITHUB_TOKEN", "GH_TOKEN");

        return null;
    }

    private static string? ResolveGitHubPackagesOwner(ProjectBuildConfiguration config)
    {
        var owner = TrimOrNull(config.GitHubPackagesOwner) ?? (config.UseGitHubPackages ? TrimOrNull(config.GitHubUsername) : null);
        if (string.IsNullOrWhiteSpace(owner))
            return null;

        return PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.GitHubPackages,
            gitHubOwner: owner).GitHubOwner;
    }

    private static string? ResolveGitHubPackagesSource(ProjectBuildConfiguration config, string? owner)
    {
        if (!config.UseGitHubPackages)
            return null;

        if (string.IsNullOrWhiteSpace(owner))
            return null;

        return PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.GitHubPackages,
            gitHubOwner: owner).PSResourceGetUri;
    }

    private static string[]? ResolveVersionSources(ProjectBuildConfiguration config, string? githubPackagesSource)
    {
        if (config.NugetSource is { Length: > 0 })
            return config.NugetSource;

        return string.IsNullOrWhiteSpace(githubPackagesSource) ? null : new[] { githubPackagesSource! };
    }

    private static string? ResolvePublishSource(ProjectBuildConfiguration config, string? githubPackagesSource)
    {
        if (!string.IsNullOrWhiteSpace(config.PublishSource))
            return config.PublishSource!.Trim();

        return githubPackagesSource;
    }

    private static RepositoryCredential? ResolveVersionSourceCredential(
        ProjectBuildConfiguration config,
        string configDir,
        string[]? versionSources,
        string? githubPackagesOwner,
        string? githubToken)
    {
        var nugetCredentialSecret = ProjectBuildSupportService.ResolveSecret(
            config.NugetCredentialSecret,
            config.NugetCredentialSecretFilePath,
            config.NugetCredentialSecretEnvName,
            configDir);
        var nugetUser = TrimOrNull(config.NugetCredentialUserName);
        if (!string.IsNullOrWhiteSpace(nugetUser) || !string.IsNullOrWhiteSpace(nugetCredentialSecret))
        {
            return new RepositoryCredential
            {
                UserName = nugetUser,
                Secret = nugetCredentialSecret
            };
        }

        if (ContainsGitHubPackagesSource(versionSources) && !string.IsNullOrWhiteSpace(githubToken))
        {
            return new RepositoryCredential
            {
                UserName = githubPackagesOwner,
                Secret = githubToken
            };
        }

        return null;
    }

    private static bool ContainsGitHubPackagesSource(string[]? sources)
        => sources is not null && sources.Any(IsGitHubPackagesSource);

    private static string? ResolveFirstEnvironmentSecret(params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value!.Trim();
            }
            catch
            {
                // best effort
            }
        }

        return null;
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
