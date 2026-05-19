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

    /// <summary>Registration tool selected for this profile.</summary>
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.PSResourceGet;

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

    /// <summary>Profile creation timestamp in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Profile update timestamp in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
