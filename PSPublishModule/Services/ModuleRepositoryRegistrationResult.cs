using System;
using System.Collections.Generic;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Result returned when registering or refreshing a private module repository.
/// </summary>
public sealed class ModuleRepositoryRegistrationResult
{
    /// <summary>Repository name used for registration.</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>Provider used for registration.</summary>
    public string Provider { get; set; } = "AzureArtifacts";

    /// <summary>Bootstrap mode requested by the caller.</summary>
    public PrivateGalleryBootstrapMode BootstrapModeRequested { get; set; }

    /// <summary>Bootstrap mode actually used during registration.</summary>
    public PrivateGalleryBootstrapMode BootstrapModeUsed { get; set; }

    /// <summary>Source of the credential used during bootstrap.</summary>
    public PrivateGalleryCredentialSource CredentialSource { get; set; }

    /// <summary>Azure DevOps organization name.</summary>
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name.</summary>
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name.</summary>
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Resolved PowerShellGet source URI.</summary>
    public string PowerShellGetSourceUri { get; set; } = string.Empty;

    /// <summary>Resolved PowerShellGet publish URI.</summary>
    public string PowerShellGetPublishUri { get; set; } = string.Empty;

    /// <summary>Resolved PSResourceGet URI.</summary>
    public string PSResourceGetUri { get; set; } = string.Empty;

    /// <summary>Selected repository registration tool.</summary>
    public RepositoryRegistrationTool Tool { get; set; }

    /// <summary>Repository registration strategy requested by the caller.</summary>
    public RepositoryRegistrationTool ToolRequested { get; set; }

    /// <summary>Repository registration path that completed successfully.</summary>
    public RepositoryRegistrationTool ToolUsed { get; set; }

    /// <summary>Whether PowerShellGet registration created the repository (false means it was updated).</summary>
    public bool PowerShellGetCreated { get; set; }

    /// <summary>Whether PSResourceGet registration created the repository (false means it was updated).</summary>
    public bool PSResourceGetCreated { get; set; }

    /// <summary>Whether the repository is trusted.</summary>
    public bool Trusted { get; set; }

    /// <summary>PSResourceGet repository priority used for registration.</summary>
    public int? Priority { get; set; }

    /// <summary>Whether a credential was supplied for registration.</summary>
    public bool CredentialUsed { get; set; }

    /// <summary>Whether the registration action was executed.</summary>
    public bool RegistrationPerformed { get; set; }

    /// <summary>Whether PSResourceGet registration completed successfully.</summary>
    public bool PSResourceGetRegistered { get; set; }

    /// <summary>Whether PowerShellGet registration completed successfully.</summary>
    public bool PowerShellGetRegistered { get; set; }

    /// <summary>Whether PSResourceGet is available locally for bootstrap/use.</summary>
    public bool PSResourceGetAvailable { get; set; }

    /// <summary>Detected PSResourceGet version when available.</summary>
    public string? PSResourceGetVersion { get; set; }

    /// <summary>Whether the detected PSResourceGet version satisfies the private-gallery minimum.</summary>
    public bool PSResourceGetMeetsMinimumVersion { get; set; }

    /// <summary>Whether the detected PSResourceGet version supports Azure Artifacts ExistingSession bootstrap.</summary>
    public bool PSResourceGetSupportsExistingSessionBootstrap { get; set; }

    /// <summary>Whether PowerShellGet is available locally for bootstrap/use.</summary>
    public bool PowerShellGetAvailable { get; set; }

    /// <summary>Detected PowerShellGet version when available.</summary>
    public string? PowerShellGetVersion { get; set; }

    /// <summary>Whether Azure Artifacts Credential Provider was detected from standard NuGet plugin locations.</summary>
    public bool AzureArtifactsCredentialProviderDetected { get; set; }

    /// <summary>Detected Azure Artifacts credential-provider file paths.</summary>
    public string[] AzureArtifactsCredentialProviderPaths { get; set; } = Array.Empty<string>();

    /// <summary>Detected Azure Artifacts credential-provider version when available.</summary>
    public string? AzureArtifactsCredentialProviderVersion { get; set; }

    /// <summary>Readiness/preflight messages collected before registration.</summary>
    public string[] ReadinessMessages { get; set; } = Array.Empty<string>();

    /// <summary>Names of prerequisites installed by the current command execution.</summary>
    public string[] InstalledPrerequisites { get; set; } = Array.Empty<string>();

    /// <summary>Messages emitted while installing prerequisites.</summary>
    public string[] PrerequisiteInstallMessages { get; set; } = Array.Empty<string>();

    /// <summary>Tool names skipped because they were not available locally.</summary>
    public string[] UnavailableTools { get; set; } = Array.Empty<string>();

    /// <summary>Non-fatal messages collected during repository registration.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();

    /// <summary>Whether an authenticated repository probe was attempted.</summary>
    public bool AccessProbePerformed { get; set; }

    /// <summary>Whether the authenticated repository probe succeeded.</summary>
    public bool AccessProbeSucceeded { get; set; }

    /// <summary>Tool used for the repository access probe.</summary>
    public string? AccessProbeTool { get; set; }

    /// <summary>Outcome message returned from the repository access probe.</summary>
    public string? AccessProbeMessage { get; set; }

    /// <summary>Whether PSPublishModule attempted to prime the Azure Artifacts Credential Provider session after a failed ExistingSession probe.</summary>
    public bool CredentialProviderSessionPrimeAttempted { get; set; }

    /// <summary>Whether the Azure Artifacts Credential Provider session priming completed successfully.</summary>
    public bool CredentialProviderSessionPrimeSucceeded { get; set; }

    /// <summary>Whether session priming was intentionally skipped, for example in a CI/headless process.</summary>
    public bool CredentialProviderSessionPrimeSkipped { get; set; }

    /// <summary>Credential-provider executable or DLL path used for session priming.</summary>
    public string? CredentialProviderSessionPrimePath { get; set; }

    /// <summary>Outcome message from Azure Artifacts Credential Provider session priming.</summary>
    public string? CredentialProviderSessionPrimeMessage { get; set; }

    /// <summary>Whether PSPublishModule attempted JFrog CLI browser login.</summary>
    public bool JFrogCliLoginAttempted { get; set; }

    /// <summary>Whether JFrog CLI browser login completed successfully.</summary>
    public bool JFrogCliLoginSucceeded { get; set; }

    /// <summary>Whether JFrog CLI browser login was skipped.</summary>
    public bool JFrogCliLoginSkipped { get; set; }

    /// <summary>JFrog CLI executable path used for browser login.</summary>
    public string? JFrogCliPath { get; set; }

    /// <summary>Outcome message from JFrog CLI browser login.</summary>
    public string? JFrogCliLoginMessage { get; set; }

    private bool IsMicrosoftArtifactRegistry
        => string.Equals(Provider, "MicrosoftArtifactRegistry", StringComparison.OrdinalIgnoreCase);
    private bool IsCredentialBasedPrivateGallery
        => string.Equals(Provider, "JFrog", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(Provider, "NuGet", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the existing-session/device-login bootstrap path is ready.</summary>
    public bool ExistingSessionBootstrapReady
        => IsMicrosoftArtifactRegistry
            ? PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion
            : IsCredentialBasedPrivateGallery
                ? false
            : PSResourceGetSupportsExistingSessionBootstrap && AzureArtifactsCredentialProviderDetected;

    /// <summary>Whether the credential-prompt bootstrap path is available.</summary>
    public bool CredentialPromptBootstrapReady => (PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion) || PowerShellGetAvailable;

    /// <summary>Whether the repository bootstrap recommendation should include prerequisite installation.</summary>
    public bool InstallPrerequisitesRecommended
    {
        get
        {
            var psResourceGetReady = BootstrapModeRequested switch
            {
                PrivateGalleryBootstrapMode.ExistingSession => ExistingSessionBootstrapReady,
                PrivateGalleryBootstrapMode.CredentialPrompt => PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion,
                PrivateGalleryBootstrapMode.JFrogCli => PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion,
                _ => ExistingSessionBootstrapReady || (PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion)
            };
            var powerShellGetReady = BootstrapModeRequested == PrivateGalleryBootstrapMode.ExistingSession ||
                                     BootstrapModeRequested == PrivateGalleryBootstrapMode.JFrogCli
                ? false
                : PowerShellGetAvailable;

            if (IsMicrosoftArtifactRegistry)
                return !(PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion);

            return ToolRequested switch
            {
                RepositoryRegistrationTool.PSResourceGet => !psResourceGetReady,
                RepositoryRegistrationTool.PowerShellGet => !powerShellGetReady,
                RepositoryRegistrationTool.Both => !psResourceGetReady || !powerShellGetReady,
                _ => !psResourceGetReady && !powerShellGetReady
            };
        }
    }

    /// <summary>Suggested bootstrap mode based on detected prerequisites.</summary>
    public PrivateGalleryBootstrapMode RecommendedBootstrapMode
        => IsCredentialBasedPrivateGallery && BootstrapModeRequested == PrivateGalleryBootstrapMode.JFrogCli
            ? PrivateGalleryBootstrapMode.JFrogCli
            : IsMicrosoftArtifactRegistry
            ? PrivateGalleryBootstrapMode.ExistingSession
            : ExistingSessionBootstrapReady
            ? PrivateGalleryBootstrapMode.ExistingSession
            : CredentialPromptBootstrapReady
                ? PrivateGalleryBootstrapMode.CredentialPrompt
                : PrivateGalleryBootstrapMode.Auto;

    /// <summary>Whether native Install-PSResource is ready to use with this repository.</summary>
    public bool InstallPSResourceReady => PSResourceGetRegistered && (IsMicrosoftArtifactRegistry || IsCredentialBasedPrivateGallery || ExistingSessionBootstrapReady);

    /// <summary>Whether native Install-Module is ready to use with this repository.</summary>
    public bool InstallModuleReady => PowerShellGetRegistered && BootstrapModeUsed != PrivateGalleryBootstrapMode.JFrogCli;

    /// <summary>Names of the native commands that are ready for this repository.</summary>
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

    /// <summary>Preferred native install command for this repository.</summary>
    public string PreferredInstallCommand
        => InstallPSResourceReady
            ? "Install-PSResource"
            : InstallModuleReady
                ? "Install-Module"
                : string.Empty;

    /// <summary>Recommended wrapper command for installing modules from this repository.</summary>
    public string RecommendedWrapperInstallCommand
        => string.IsNullOrWhiteSpace(RepositoryName)
            ? "Install-PrivateModule -Name <ModuleName>"
            : $"Install-PrivateModule -Name <ModuleName> -Repository '{RepositoryName}'";

    /// <summary>Recommended native command for installing modules from this repository.</summary>
    public string RecommendedNativeInstallCommand
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RepositoryName) || string.IsNullOrWhiteSpace(PreferredInstallCommand))
                return string.Empty;

            return PreferredInstallCommand == "Install-PSResource"
                ? $"Install-PSResource -Name <ModuleName> -Repository '{RepositoryName}'"
                : $"Install-Module -Name <ModuleName> -Repository '{RepositoryName}'";
        }
    }

    /// <summary>Recommended bootstrap command based on detected prerequisites.</summary>
    public string RecommendedBootstrapCommand
    {
        get
        {
            if (IsMicrosoftArtifactRegistry)
            {
                var marParts = new List<string> { "Register-ModuleRepository", "-MicrosoftArtifactRegistry" };
                if (InstallPrerequisitesRecommended)
                    marParts.Add("-InstallPrerequisites");
                if (!string.IsNullOrWhiteSpace(RepositoryName) &&
                    !MicrosoftArtifactRegistryRepository.IsDefaultName(RepositoryName))
                    marParts.Add($"-Name '{RepositoryName}'");
                return string.Join(" ", marParts);
            }

            if (IsCredentialBasedPrivateGallery)
            {
                var privateGalleryParts = new List<string>
                {
                    "Register-ModuleRepository",
                    $"-Provider {Provider}"
                };

                if (!string.IsNullOrWhiteSpace(AzureArtifactsFeed))
                    privateGalleryParts.Add($"-Repository '{AzureArtifactsFeed}'");

                if (!string.IsNullOrWhiteSpace(PSResourceGetUri))
                    privateGalleryParts.Add($"-RepositoryUri '{PSResourceGetUri}'");

                if (!string.IsNullOrWhiteSpace(PowerShellGetSourceUri) &&
                    !string.Equals(PowerShellGetSourceUri, PSResourceGetUri, StringComparison.OrdinalIgnoreCase))
                    privateGalleryParts.Add($"-RepositorySourceUri '{PowerShellGetSourceUri}'");

                if (!string.IsNullOrWhiteSpace(PowerShellGetPublishUri) &&
                    !string.Equals(PowerShellGetPublishUri, PSResourceGetUri, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(PowerShellGetPublishUri, PowerShellGetSourceUri, StringComparison.OrdinalIgnoreCase))
                    privateGalleryParts.Add($"-RepositoryPublishUri '{PowerShellGetPublishUri}'");

                if (!string.IsNullOrWhiteSpace(RepositoryName) &&
                    !string.Equals(RepositoryName, AzureArtifactsFeed, StringComparison.OrdinalIgnoreCase))
                {
                    privateGalleryParts.Add($"-Name '{RepositoryName}'");
                }

                if (InstallPrerequisitesRecommended)
                    privateGalleryParts.Add("-InstallPrerequisites");

                if (RecommendedBootstrapMode == PrivateGalleryBootstrapMode.JFrogCli)
                {
                    privateGalleryParts.Add("-BootstrapMode JFrogCli");
                }
                else if (RecommendedBootstrapMode == PrivateGalleryBootstrapMode.CredentialPrompt)
                {
                    privateGalleryParts.Add("-BootstrapMode CredentialPrompt");
                    privateGalleryParts.Add("-Interactive");
                }

                return string.Join(" ", privateGalleryParts);
            }

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
            {
                parts.Add("-BootstrapMode ExistingSession");
            }
            else if (RecommendedBootstrapMode == PrivateGalleryBootstrapMode.CredentialPrompt)
            {
                parts.Add("-BootstrapMode CredentialPrompt");
                parts.Add("-Interactive");
            }

            return string.Join(" ", parts);
        }
    }
}
