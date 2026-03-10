using System;

namespace PowerForge;

/// <summary>
/// Azure Artifacts repository endpoint helper for PowerShell/NuGet feeds.
/// </summary>
public static class AzureArtifactsRepositoryEndpoints
{
    /// <summary>
    /// Creates a repository definition for an Azure Artifacts feed.
    /// </summary>
    /// <param name="organization">Azure DevOps organization name.</param>
    /// <param name="project">Optional Azure DevOps project name for project-scoped feeds.</param>
    /// <param name="feed">Azure Artifacts feed name.</param>
    /// <param name="repositoryName">Optional repository name override. Defaults to the feed name.</param>
    /// <returns>Resolved repository endpoints for v2 and v3 clients.</returns>
    public static AzureArtifactsRepositoryEndpoint Create(
        string organization,
        string? project,
        string feed,
        string? repositoryName = null)
    {
        var normalizedOrganization = NormalizeRequiredSegment(organization, nameof(organization));
        var normalizedFeed = NormalizeRequiredSegment(feed, nameof(feed));
        var normalizedProject = NormalizeOptionalSegment(project);
        var resolvedName = string.IsNullOrWhiteSpace(repositoryName)
            ? normalizedFeed
            : repositoryName!.Trim();

        if (string.Equals(resolvedName, "PSGallery", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Azure Artifacts repository name cannot resolve to 'PSGallery'.", nameof(repositoryName));

        var baseUri = string.IsNullOrWhiteSpace(normalizedProject)
            ? $"https://pkgs.dev.azure.com/{Uri.EscapeDataString(normalizedOrganization)}/_packaging/{Uri.EscapeDataString(normalizedFeed)}/nuget"
            : $"https://pkgs.dev.azure.com/{Uri.EscapeDataString(normalizedOrganization)}/{Uri.EscapeDataString(normalizedProject)}/_packaging/{Uri.EscapeDataString(normalizedFeed)}/nuget";

        return new AzureArtifactsRepositoryEndpoint(
            repositoryName: resolvedName,
            organization: normalizedOrganization,
            project: normalizedProject,
            feed: normalizedFeed,
            powerShellGetSourceUri: baseUri + "/v2",
            powerShellGetPublishUri: baseUri + "/v2",
            psResourceGetUri: baseUri + "/v3/index.json");
    }

    /// <summary>
    /// Creates publish repository settings for an Azure Artifacts feed.
    /// </summary>
    /// <param name="organization">Azure DevOps organization name.</param>
    /// <param name="project">Optional Azure DevOps project name for project-scoped feeds.</param>
    /// <param name="feed">Azure Artifacts feed name.</param>
    /// <param name="repositoryName">Optional repository name override. Defaults to the feed name.</param>
    /// <param name="trusted">Whether the repository should be trusted.</param>
    /// <param name="priority">Optional PSResourceGet repository priority.</param>
    /// <param name="apiVersion">PSResourceGet API version. Defaults to v3 when omitted/auto.</param>
    /// <param name="ensureRegistered">Whether to register the repository before use.</param>
    /// <param name="unregisterAfterUse">Whether to unregister the repository after use.</param>
    /// <param name="credential">Optional repository credential.</param>
    /// <returns>Publish repository configuration.</returns>
    public static PublishRepositoryConfiguration CreatePublishRepositoryConfiguration(
        string organization,
        string? project,
        string feed,
        string? repositoryName = null,
        bool trusted = true,
        int? priority = null,
        RepositoryApiVersion apiVersion = RepositoryApiVersion.V3,
        bool ensureRegistered = true,
        bool unregisterAfterUse = false,
        RepositoryCredential? credential = null)
    {
        var endpoint = Create(organization, project, feed, repositoryName);
        var resolvedApiVersion = apiVersion == RepositoryApiVersion.Auto ? RepositoryApiVersion.V3 : apiVersion;
        var repositoryUri = resolvedApiVersion == RepositoryApiVersion.V2
            ? endpoint.PowerShellGetSourceUri
            : endpoint.PSResourceGetUri;

        return new PublishRepositoryConfiguration
        {
            Name = endpoint.RepositoryName,
            Uri = repositoryUri,
            SourceUri = endpoint.PowerShellGetSourceUri,
            PublishUri = endpoint.PowerShellGetPublishUri,
            Trusted = trusted,
            Priority = priority,
            ApiVersion = resolvedApiVersion,
            EnsureRegistered = ensureRegistered,
            UnregisterAfterUse = unregisterAfterUse,
            Credential = credential
        };
    }

    private static string NormalizeRequiredSegment(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", paramName);

        return value.Trim().Trim('/');
    }

    private static string? NormalizeOptionalSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value!.Trim().Trim('/');
    }
}

/// <summary>
/// Resolved Azure Artifacts feed endpoints for PowerShellGet and PSResourceGet.
/// </summary>
public sealed class AzureArtifactsRepositoryEndpoint
{
    /// <summary>
    /// Creates a new endpoint definition.
    /// </summary>
    public AzureArtifactsRepositoryEndpoint(
        string repositoryName,
        string organization,
        string? project,
        string feed,
        string powerShellGetSourceUri,
        string powerShellGetPublishUri,
        string psResourceGetUri)
    {
        RepositoryName = repositoryName;
        Organization = organization;
        Project = project;
        Feed = feed;
        PowerShellGetSourceUri = powerShellGetSourceUri;
        PowerShellGetPublishUri = powerShellGetPublishUri;
        PSResourceGetUri = psResourceGetUri;
    }

    /// <summary>Repository name used for local registration.</summary>
    public string RepositoryName { get; }

    /// <summary>Azure DevOps organization name.</summary>
    public string Organization { get; }

    /// <summary>Optional Azure DevOps project name.</summary>
    public string? Project { get; }

    /// <summary>Azure Artifacts feed name.</summary>
    public string Feed { get; }

    /// <summary>NuGet v2 source URI used by PowerShellGet.</summary>
    public string PowerShellGetSourceUri { get; }

    /// <summary>NuGet v2 publish URI used by PowerShellGet.</summary>
    public string PowerShellGetPublishUri { get; }

    /// <summary>NuGet v3 index URI used by PSResourceGet.</summary>
    public string PSResourceGetUri { get; }
}
