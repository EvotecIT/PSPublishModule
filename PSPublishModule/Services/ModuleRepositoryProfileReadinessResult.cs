using System;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Readiness information for a saved private module repository profile.
/// </summary>
public sealed class ModuleRepositoryProfileReadinessResult
{
    /// <summary>Profile name tested by the command.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the named profile exists in the local profile store.</summary>
    public bool ProfileFound { get; set; }

    /// <summary>Whether the saved profile's selected bootstrap path is ready locally.</summary>
    public bool IsReady
    {
        get
        {
            if (!ProfileFound)
                return false;
            if (BootstrapMode == PrivateGalleryBootstrapMode.ExistingSession)
                return ExistingSessionBootstrapReady;
            if (BootstrapMode == PrivateGalleryBootstrapMode.CredentialPrompt)
                return CredentialPromptBootstrapReady;

            return ExistingSessionBootstrapReady || CredentialPromptBootstrapReady;
        }
    }

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

    /// <summary>Resolved NuGet v2 source URI used by PowerShellGet.</summary>
    public string PowerShellGetSourceUri { get; set; } = string.Empty;

    /// <summary>Resolved NuGet v2 publish URI used by PowerShellGet.</summary>
    public string PowerShellGetPublishUri { get; set; } = string.Empty;

    /// <summary>Resolved NuGet v3 index URI used by PSResourceGet.</summary>
    public string PSResourceGetUri { get; set; } = string.Empty;

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

    /// <summary>Profile storage path used by PSPublishModule.</summary>
    public string ProfileStorePath { get; set; } = string.Empty;

    /// <summary>Profile storage scope used by PSPublishModule.</summary>
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Whether PSResourceGet is available locally.</summary>
    public bool PSResourceGetAvailable { get; set; }

    /// <summary>Detected PSResourceGet version when available.</summary>
    public string? PSResourceGetVersion { get; set; }

    /// <summary>Whether the detected PSResourceGet version satisfies the private-gallery minimum.</summary>
    public bool PSResourceGetMeetsMinimumVersion { get; set; }

    /// <summary>Whether the detected PSResourceGet version supports Azure Artifacts ExistingSession bootstrap.</summary>
    public bool PSResourceGetSupportsExistingSessionBootstrap { get; set; }

    /// <summary>Whether PowerShellGet is available locally.</summary>
    public bool PowerShellGetAvailable { get; set; }

    /// <summary>Detected PowerShellGet version when available.</summary>
    public string? PowerShellGetVersion { get; set; }

    /// <summary>Whether Azure Artifacts Credential Provider was detected from standard NuGet plugin locations.</summary>
    public bool AzureArtifactsCredentialProviderDetected { get; set; }

    /// <summary>Detected Azure Artifacts credential-provider file paths.</summary>
    public string[] AzureArtifactsCredentialProviderPaths { get; set; } = Array.Empty<string>();

    /// <summary>Detected Azure Artifacts credential-provider version when available.</summary>
    public string? AzureArtifactsCredentialProviderVersion { get; set; }

    /// <summary>Whether the Entra/device-login bootstrap path is ready for Azure Artifacts.</summary>
    public bool ExistingSessionBootstrapReady { get; set; }

    /// <summary>Whether the credential-prompt fallback bootstrap path is available.</summary>
    public bool CredentialPromptBootstrapReady { get; set; }

    /// <summary>Whether onboarding should install/update local prerequisites before connecting.</summary>
    public bool InstallPrerequisitesRecommended { get; set; }

    /// <summary>Suggested bootstrap mode based on detected local prerequisites.</summary>
    public PrivateGalleryBootstrapMode RecommendedBootstrapMode { get; set; } = PrivateGalleryBootstrapMode.Auto;

    /// <summary>Suggested command to connect or refresh the saved repository registration.</summary>
    public string RecommendedConnectCommand { get; set; } = string.Empty;

    /// <summary>Suggested one-command onboarding command for the saved profile.</summary>
    public string RecommendedOnboardingCommand { get; set; } = string.Empty;

    /// <summary>Suggested wrapper command for installing modules from this profile.</summary>
    public string RecommendedInstallCommand { get; set; } = string.Empty;

    /// <summary>Readiness messages collected from the local prerequisite scan.</summary>
    public string[] ReadinessMessages { get; set; } = Array.Empty<string>();
}
