using System;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// User-facing private module repository profile saved by PSPublishModule.
/// </summary>
public sealed class ModuleRepositoryProfileResult
{
    /// <summary>Profile name used by connect/install/update cmdlets.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Private gallery provider.</summary>
    public PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;

    /// <summary>Azure DevOps organization name.</summary>
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name.</summary>
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Local PowerShell repository name.</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>Provider repository/feed id.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>PSResourceGet v3 repository URI.</summary>
    public string RepositoryUri { get; set; } = string.Empty;

    /// <summary>PowerShellGet source URI.</summary>
    public string RepositorySourceUri { get; set; } = string.Empty;

    /// <summary>PowerShellGet publish URI.</summary>
    public string RepositoryPublishUri { get; set; } = string.Empty;

    /// <summary>JFrog Artifactory base URI, when applicable.</summary>
    public string JFrogBaseUri { get; set; } = string.Empty;

    /// <summary>JFrog NuGet repository key, when applicable.</summary>
    public string JFrogRepository { get; set; } = string.Empty;

    /// <summary>GitHub user or organization namespace, when applicable.</summary>
    public string GitHubOwner { get; set; } = string.Empty;

    /// <summary>Registration tool selected for this profile.</summary>
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.PSResourceGet;

    /// <summary>PSResourceGet repository API version selected for this profile.</summary>
    public RepositoryApiVersion ApiVersion { get; set; } = RepositoryApiVersion.Auto;

    /// <summary>Bootstrap/authentication mode selected for this profile.</summary>
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.ExistingSession;

    /// <summary>Whether the local repository registration should be trusted.</summary>
    public bool Trusted { get; set; }

    /// <summary>Optional PSResourceGet repository priority.</summary>
    public int? Priority { get; set; }

    /// <summary>Authentication strategy represented by the profile.</summary>
    public string AuthenticationMode { get; set; } = "AzureArtifactsCredentialProvider";

    /// <summary>Profile storage path.</summary>
    public string ProfileStorePath { get; set; } = string.Empty;

    /// <summary>Profile storage scope.</summary>
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Profile creation timestamp in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Profile update timestamp in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
