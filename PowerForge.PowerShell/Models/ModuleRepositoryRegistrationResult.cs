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
    public int? Priority { get; set; }
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
    public bool CredentialProviderSessionPrimeAttempted { get; set; }
    public bool CredentialProviderSessionPrimeSucceeded { get; set; }
    public bool CredentialProviderSessionPrimeSkipped { get; set; }
    public string? CredentialProviderSessionPrimePath { get; set; }
    public string? CredentialProviderSessionPrimeMessage { get; set; }
    public bool JFrogCliLoginAttempted { get; set; }
    public bool JFrogCliLoginSucceeded { get; set; }
    public bool JFrogCliLoginSkipped { get; set; }
    public string? JFrogCliPath { get; set; }
    public string? JFrogCliLoginMessage { get; set; }

    private bool IsMicrosoftArtifactRegistry
        => string.Equals(Provider, "MicrosoftArtifactRegistry", StringComparison.OrdinalIgnoreCase);
    private bool IsCredentialBasedPrivateGallery
        => string.Equals(Provider, "JFrog", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(Provider, "NuGet", StringComparison.OrdinalIgnoreCase);

    public bool ExistingSessionBootstrapReady
        => IsMicrosoftArtifactRegistry
            ? PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion
            : IsCredentialBasedPrivateGallery
                ? false
            : PSResourceGetSupportsExistingSessionBootstrap && AzureArtifactsCredentialProviderDetected;
    public bool CredentialPromptBootstrapReady => (PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion) || PowerShellGetAvailable;
    public bool InstallPrerequisitesRecommended
    {
        get
        {
            var status = new BootstrapPrerequisiteStatus(
                PSResourceGetAvailable,
                PSResourceGetVersion,
                PSResourceGetMeetsMinimumVersion,
                PSResourceGetSupportsExistingSessionBootstrap,
                null,
                PowerShellGetAvailable,
                PowerShellGetVersion,
                null,
                new AzureArtifactsCredentialProviderDetectionResult
                {
                    IsDetected = AzureArtifactsCredentialProviderDetected,
                    Paths = AzureArtifactsCredentialProviderPaths,
                    Version = AzureArtifactsCredentialProviderVersion
                },
                ReadinessMessages);

            return IsMicrosoftArtifactRegistry
                ? !(PSResourceGetAvailable && PSResourceGetMeetsMinimumVersion)
                : PrivateGalleryVersionPolicy.ShouldInstallPrerequisitesForBootstrap(status, BootstrapModeRequested, ToolRequested);
        }
    }
    public PrivateGalleryBootstrapMode RecommendedBootstrapMode
        => IsCredentialBasedPrivateGallery && BootstrapModeRequested == PrivateGalleryBootstrapMode.JFrogCli ? PrivateGalleryBootstrapMode.JFrogCli
            : IsMicrosoftArtifactRegistry ? PrivateGalleryBootstrapMode.ExistingSession
            : ExistingSessionBootstrapReady ? PrivateGalleryBootstrapMode.ExistingSession
            : CredentialPromptBootstrapReady ? PrivateGalleryBootstrapMode.CredentialPrompt
            : PrivateGalleryBootstrapMode.Auto;
    public bool InstallPSResourceReady => PSResourceGetRegistered && (IsMicrosoftArtifactRegistry || IsCredentialBasedPrivateGallery || ExistingSessionBootstrapReady);
    public bool InstallModuleReady => PowerShellGetRegistered && BootstrapModeUsed != PrivateGalleryBootstrapMode.JFrogCli;
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
        => string.IsNullOrWhiteSpace(RepositoryName) ? "Install-ManagedModule -Name <ModuleName>" : $"Install-ManagedModule -Name <ModuleName> -Repository '{RepositoryName}'";

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
            if (IsMicrosoftArtifactRegistry)
            {
                var marParts = new List<string> { "Initialize-ManagedModuleRepository", "-MicrosoftArtifactRegistry" };
                if (InstallPrerequisitesRecommended)
                    marParts.Add("-InstallPrerequisites");
                if (!string.IsNullOrWhiteSpace(RepositoryName) &&
                    !MicrosoftArtifactRegistryRepository.IsDefaultName(RepositoryName))
                    marParts.Add($"-RepositoryName '{RepositoryName}'");
                return string.Join(" ", marParts);
            }

            if (IsCredentialBasedPrivateGallery)
            {
                var privateGalleryParts = new List<string>
                {
                    "Initialize-ManagedModuleRepository",
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
                "Initialize-ManagedModuleRepository",
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
