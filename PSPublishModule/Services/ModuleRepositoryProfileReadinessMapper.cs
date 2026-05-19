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
        BootstrapPrerequisiteStatus status)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        var endpoint = AzureArtifactsRepositoryEndpoints.Create(
            profile.AzureDevOpsOrganization,
            profile.AzureDevOpsProject,
            profile.AzureArtifactsFeed,
            profile.RepositoryName);

        var existingSessionReady = PrivateGalleryVersionPolicy.IsExistingSessionBootstrapReady(status);
        var credentialPromptReady = PrivateGalleryVersionPolicy.IsCredentialPromptBootstrapReady(status);
        var installPrerequisitesRecommended =
            !status.PSResourceGetAvailable ||
            !status.PSResourceGetMeetsMinimumVersion ||
            !status.CredentialProviderDetection.IsDetected;

        return new ModuleRepositoryProfileReadinessResult
        {
            Name = profile.Name,
            ProfileFound = true,
            Provider = profile.Provider,
            AzureDevOpsOrganization = endpoint.Organization,
            AzureDevOpsProject = endpoint.Project,
            AzureArtifactsFeed = endpoint.Feed,
            RepositoryName = endpoint.RepositoryName,
            PowerShellGetSourceUri = endpoint.PowerShellGetSourceUri,
            PowerShellGetPublishUri = endpoint.PowerShellGetPublishUri,
            PSResourceGetUri = endpoint.PSResourceGetUri,
            Tool = profile.Tool,
            BootstrapMode = profile.BootstrapMode,
            Trusted = profile.Trusted,
            Priority = profile.Priority,
            AuthenticationMode = profile.AuthenticationMode,
            ProfileStorePath = profileStorePath,
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
            RecommendedInstallCommand = $"Install-PrivateModule -Name <ModuleName> -ProfileName '{profile.Name}'",
            ReadinessMessages = status.ReadinessMessages
        };
    }

    internal static ModuleRepositoryProfileReadinessResult ToMissingProfileResult(string name, string profileStorePath)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        var messages = new List<string>(1)
        {
            $"Module repository profile '{normalizedName}' was not found. Create it with Set-ModuleRepositoryProfile before connecting, installing, updating, or publishing."
        };

        return new ModuleRepositoryProfileReadinessResult
        {
            Name = normalizedName,
            ProfileFound = false,
            ProfileStorePath = profileStorePath,
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
}
