namespace PowerForge;

/// <summary>
/// Private gallery provider used by end-user repository bootstrap commands.
/// </summary>
public enum PrivateGalleryProvider
{
    /// <summary>Azure Artifacts / Azure DevOps private feed.</summary>
    AzureArtifacts = 0,
    /// <summary>Alias for Azure Artifacts / Azure DevOps private feed.</summary>
    Azure = 0,
    /// <summary>JFrog Artifactory NuGet/PowerShell repository.</summary>
    JFrog = 1,
    /// <summary>Generic NuGet-backed PowerShell repository.</summary>
    NuGet = 2,
    /// <summary>GitHub Packages NuGet registry scoped to a GitHub user or organization.</summary>
    GitHubPackages = 3,
    /// <summary>Alias for GitHub Packages NuGet registry.</summary>
    GitHub = 3
}
