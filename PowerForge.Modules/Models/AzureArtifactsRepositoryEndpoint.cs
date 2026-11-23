namespace PowerForge;

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
