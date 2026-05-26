using System;

namespace PowerForge;

internal sealed class ModuleRepositoryProfile
{
    public string Name { get; set; } = string.Empty;
    public PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;
    public string AzureDevOpsOrganization { get; set; } = string.Empty;
    public string? AzureDevOpsProject { get; set; }
    public string AzureArtifactsFeed { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string RepositoryUri { get; set; } = string.Empty;
    public string RepositorySourceUri { get; set; } = string.Empty;
    public string RepositoryPublishUri { get; set; } = string.Empty;
    public string JFrogBaseUri { get; set; } = string.Empty;
    public string JFrogRepository { get; set; } = string.Empty;
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.PSResourceGet;
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.ExistingSession;
    public bool Trusted { get; set; } = true;
    public int? Priority { get; set; }
    public string AuthenticationMode { get; set; } = "AzureArtifactsCredentialProvider";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class ModuleRepositoryProfileDocument
{
    public int Version { get; set; } = 1;
    public ModuleRepositoryProfile[] Profiles { get; set; } = Array.Empty<ModuleRepositoryProfile>();
}
