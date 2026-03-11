using System;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Result returned when registering or refreshing a private module repository.
/// </summary>
internal sealed class ModuleRepositoryRegistrationResult
{
    public string RepositoryName { get; set; } = string.Empty;
    public string Provider { get; set; } = "AzureArtifacts";
    public PrivateGalleryBootstrapMode BootstrapModeRequested { get; set; }
    public PrivateGalleryBootstrapMode BootstrapModeUsed { get; set; }
    public PrivateGalleryCredentialSource CredentialSource { get; set; }
    public string AzureDevOpsOrganization { get; set; } = string.Empty;
    public string? AzureDevOpsProject { get; set; }
    public string AzureArtifactsFeed { get; set; } = string.Empty;
    public string PowerShellGetSourceUri { get; set; } = string.Empty;
    public string PowerShellGetPublishUri { get; set; } = string.Empty;
    public string PSResourceGetUri { get; set; } = string.Empty;
    public RepositoryRegistrationTool Tool { get; set; }
    public RepositoryRegistrationTool ToolRequested { get; set; }
    public RepositoryRegistrationTool ToolUsed { get; set; }
    public bool PowerShellGetCreated { get; set; }
    public bool PSResourceGetCreated { get; set; }
    public bool Trusted { get; set; }
    public bool CredentialUsed { get; set; }
    public bool RegistrationPerformed { get; set; }
    public bool PSResourceGetRegistered { get; set; }
    public bool PowerShellGetRegistered { get; set; }
    public bool PSResourceGetAvailable { get; set; }
    public string? PSResourceGetVersion { get; set; }
    public bool PSResourceGetMeetsMinimumVersion { get; set; }
    public bool PSResourceGetSupportsExistingSessionBootstrap { get; set; }
    public bool PowerShellGetAvailable { get; set; }
    public string? PowerShellGetVersion { get; set; }
    public bool AzureArtifactsCredentialProviderDetected { get; set; }
    public string[] AzureArtifactsCredentialProviderPaths { get; set; } = Array.Empty<string>();
    public string? AzureArtifactsCredentialProviderVersion { get; set; }
    public string[] ReadinessMessages { get; set; } = Array.Empty<string>();
    public string[] InstalledPrerequisites { get; set; } = Array.Empty<string>();
    public string[] PrerequisiteInstallMessages { get; set; } = Array.Empty<string>();
    public string[] UnavailableTools { get; set; } = Array.Empty<string>();
    public string[] Messages { get; set; } = Array.Empty<string>();
    public bool AccessProbePerformed { get; set; }
    public bool AccessProbeSucceeded { get; set; }
    public string? AccessProbeTool { get; set; }
    public string? AccessProbeMessage { get; set; }

    public bool ExistingSessionBootstrapReady => PSResourceGetSupportsExistingSessionBootstrap && AzureArtifactsCredentialProviderDetected;
    public bool CredentialPromptBootstrapReady => (PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion) || PowerShellGetAvailable;
    public bool InstallPrerequisitesRecommended => !PSResourceGetAvailable || !PSResourceGetMeetsMinimumVersion || !AzureArtifactsCredentialProviderDetected;
    public PrivateGalleryBootstrapMode RecommendedBootstrapMode
        => ExistingSessionBootstrapReady ? PrivateGalleryBootstrapMode.ExistingSession
            : CredentialPromptBootstrapReady ? PrivateGalleryBootstrapMode.CredentialPrompt
            : PrivateGalleryBootstrapMode.Auto;
    public bool InstallPSResourceReady => PSResourceGetRegistered && ExistingSessionBootstrapReady;
    public bool InstallModuleReady => PowerShellGetRegistered;
    public string[] ReadyCommands
    {
        get
        {
            var ready = new List<string>(2);
            if (InstallPSResourceReady) ready.Add("Install-PSResource");
            if (InstallModuleReady) ready.Add("Install-Module");
            return ready.ToArray();
        }
    }

    public string PreferredInstallCommand => InstallPSResourceReady ? "Install-PSResource" : InstallModuleReady ? "Install-Module" : string.Empty;
    public string RecommendedWrapperInstallCommand
        => string.IsNullOrWhiteSpace(RepositoryName) ? "Install-PrivateModule -Name <ModuleName>" : $"Install-PrivateModule -Name <ModuleName> -Repository '{RepositoryName}'";

    public string RecommendedNativeInstallCommand
        => string.IsNullOrWhiteSpace(RepositoryName) || string.IsNullOrWhiteSpace(PreferredInstallCommand)
            ? string.Empty
            : PreferredInstallCommand == "Install-PSResource"
                ? $"Install-PSResource -Name <ModuleName> -Repository '{RepositoryName}'"
                : $"Install-Module -Name <ModuleName> -Repository '{RepositoryName}'";

    public string RecommendedBootstrapCommand
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AzureDevOpsOrganization) || string.IsNullOrWhiteSpace(AzureArtifactsFeed))
                return string.Empty;

            var parts = new List<string>
            {
                "Register-ModuleRepository",
                $"-AzureDevOpsOrganization '{AzureDevOpsOrganization}'"
            };

            if (!string.IsNullOrWhiteSpace(AzureDevOpsProject))
                parts.Add($"-AzureDevOpsProject '{AzureDevOpsProject}'");

            parts.Add($"-AzureArtifactsFeed '{AzureArtifactsFeed}'");

            if (!string.IsNullOrWhiteSpace(RepositoryName) &&
                !string.Equals(RepositoryName, AzureArtifactsFeed, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"-Name '{RepositoryName}'");
            }

            if (InstallPrerequisitesRecommended)
                parts.Add("-InstallPrerequisites");

            if (RecommendedBootstrapMode == PrivateGalleryBootstrapMode.ExistingSession)
                parts.Add("-BootstrapMode ExistingSession");
            else if (RecommendedBootstrapMode == PrivateGalleryBootstrapMode.CredentialPrompt)
            {
                parts.Add("-BootstrapMode CredentialPrompt");
                parts.Add("-Interactive");
            }

            return string.Join(" ", parts);
        }
    }
}
