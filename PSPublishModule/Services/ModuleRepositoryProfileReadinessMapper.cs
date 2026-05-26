using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleRepositoryProfileReadinessMapper
{
    internal static ModuleRepositoryProfileReadinessResult ToCmdletResult(
        ModuleRepositoryProfile profile,
        string profileStorePath,
        BootstrapPrerequisiteStatus status,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.User)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            profile.Provider,
            profile.AzureDevOpsOrganization,
            profile.AzureDevOpsProject,
            profile.AzureArtifactsFeed,
            profile.RepositoryName,
            profile.Repository,
            profile.RepositoryUri,
            profile.RepositorySourceUri,
            profile.RepositoryPublishUri,
            profile.JFrogBaseUri,
            profile.JFrogRepository);

        var existingSessionReady = endpoint.Provider == PrivateGalleryProvider.AzureArtifacts &&
                                   PrivateGalleryVersionPolicy.IsExistingSessionBootstrapReady(status);
        var credentialPromptReady = PrivateGalleryVersionPolicy.IsCredentialPromptBootstrapReady(status);
        var installPrerequisitesRecommended = PrivateGalleryVersionPolicy.ShouldInstallPrerequisitesForBootstrap(
            status,
            profile.BootstrapMode,
            profile.Tool);

        return new ModuleRepositoryProfileReadinessResult
        {
            Name = profile.Name,
            ProfileFound = true,
            Provider = profile.Provider,
            AzureDevOpsOrganization = endpoint.AzureDevOpsOrganization ?? string.Empty,
            AzureDevOpsProject = endpoint.AzureDevOpsProject,
            AzureArtifactsFeed = endpoint.Repository,
            RepositoryName = endpoint.RepositoryName,
            Repository = endpoint.Repository,
            RepositoryUri = endpoint.PSResourceGetUri,
            RepositorySourceUri = endpoint.PowerShellGetSourceUri,
            RepositoryPublishUri = endpoint.PowerShellGetPublishUri,
            JFrogBaseUri = endpoint.JFrogBaseUri ?? string.Empty,
            JFrogRepository = endpoint.JFrogRepository ?? string.Empty,
            PowerShellGetSourceUri = endpoint.PowerShellGetSourceUri,
            PowerShellGetPublishUri = endpoint.PowerShellGetPublishUri,
            PSResourceGetUri = endpoint.PSResourceGetUri,
            Tool = profile.Tool,
            BootstrapMode = profile.BootstrapMode,
            Trusted = profile.Trusted,
            Priority = profile.Priority,
            AuthenticationMode = profile.AuthenticationMode,
            ProfileStorePath = profileStorePath,
            Scope = scope,
            PSResourceGetAvailable = status.PSResourceGetAvailable,
            PSResourceGetVersion = status.PSResourceGetVersion,
            PSResourceGetMeetsMinimumVersion = status.PSResourceGetMeetsMinimumVersion,
            PSResourceGetSupportsExistingSessionBootstrap = status.PSResourceGetSupportsExistingSessionBootstrap,
            PowerShellGetAvailable = status.PowerShellGetAvailable,
            PowerShellGetVersion = status.PowerShellGetVersion,
            AzureArtifactsCredentialProviderDetected = status.CredentialProviderDetection.IsDetected,
            AzureArtifactsCredentialProviderPaths = status.CredentialProviderDetection.Paths,
            AzureArtifactsCredentialProviderVersion = status.CredentialProviderDetection.Version,
            ExistingSessionBootstrapReady = existingSessionReady,
            CredentialPromptBootstrapReady = credentialPromptReady,
            InstallPrerequisitesRecommended = installPrerequisitesRecommended,
            RecommendedBootstrapMode = PrivateGalleryVersionPolicy.GetRecommendedBootstrapMode(status),
            RecommendedConnectCommand = BuildRecommendedConnectCommand(profile.Name, installPrerequisitesRecommended),
            RecommendedOnboardingCommand = BuildRecommendedOnboardingCommand(profile.Name, installPrerequisitesRecommended),
            RecommendedInstallCommand = $"Install-PrivateModule -Name <ModuleName> -ProfileName '{profile.Name}'",
            ReadinessMessages = status.ReadinessMessages
        };
    }

    internal static ModuleRepositoryProfileReadinessResult ToMissingProfileResult(
        string name,
        string profileStorePath,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.User)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        var messages = new List<string>(1)
        {
            $"Module repository profile '{normalizedName}' was not found. Create or import it with Initialize-ModuleRepository before installing, updating, or publishing."
        };

        return new ModuleRepositoryProfileReadinessResult
        {
            Name = normalizedName,
            ProfileFound = false,
            ProfileStorePath = profileStorePath,
            Scope = scope,
            ReadinessMessages = messages.ToArray()
        };
    }

    private static string BuildRecommendedConnectCommand(string profileName, bool installPrerequisitesRecommended)
    {
        var parts = new List<string>
        {
            "Connect-ModuleRepository",
            $"-ProfileName '{profileName}'"
        };

        if (installPrerequisitesRecommended)
            parts.Add("-InstallPrerequisites");

        return string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildRecommendedOnboardingCommand(string profileName, bool installPrerequisitesRecommended)
    {
        var parts = new List<string>
        {
            "Initialize-ModuleRepository",
            $"-ProfileName '{profileName}'"
        };

        if (installPrerequisitesRecommended)
            parts.Add("-InstallPrerequisites");

        return string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }
}
