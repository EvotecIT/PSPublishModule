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
        => AzureArtifactsModuleEndpoints.Create(organization, project, feed, repositoryName);

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
        if (resolvedApiVersion is RepositoryApiVersion.ContainerRegistry or RepositoryApiVersion.Local or RepositoryApiVersion.NugetServer)
            throw new ArgumentException("Azure Artifacts repositories support only Auto, V2, or V3 API versions.", nameof(apiVersion));

        var repositoryUri = resolvedApiVersion == RepositoryApiVersion.V2
            ? endpoint.PowerShellGetSourceUri
            : endpoint.PSResourceGetUri;

        return new PublishRepositoryConfiguration
        {
            Name = endpoint.RepositoryName,
            Uri = repositoryUri,
            SourceUri = endpoint.PowerShellGetSourceUri,
            PublishUri = endpoint.PowerShellGetPublishUri,
            UseProviderEndpointDefaults = true,
            Trusted = trusted,
            Priority = priority,
            ApiVersion = resolvedApiVersion,
            EnsureRegistered = ensureRegistered,
            UnregisterAfterUse = unregisterAfterUse,
            Credential = credential
        };
    }
}
