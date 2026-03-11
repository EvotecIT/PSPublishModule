using PowerForge;

namespace PSPublishModule;

internal static class ModuleRepositoryRegistrationResultMapper
{
    internal static ModuleRepositoryRegistrationResult ToCmdletResult(PowerForge.ModuleRepositoryRegistrationResult result)
    {
        return new ModuleRepositoryRegistrationResult
        {
            RepositoryName = result.RepositoryName,
            Provider = result.Provider,
            BootstrapModeRequested = result.BootstrapModeRequested,
            BootstrapModeUsed = result.BootstrapModeUsed,
            CredentialSource = result.CredentialSource,
            AzureDevOpsOrganization = result.AzureDevOpsOrganization,
            AzureDevOpsProject = result.AzureDevOpsProject,
            AzureArtifactsFeed = result.AzureArtifactsFeed,
            PowerShellGetSourceUri = result.PowerShellGetSourceUri,
            PowerShellGetPublishUri = result.PowerShellGetPublishUri,
            PSResourceGetUri = result.PSResourceGetUri,
            Tool = result.Tool,
            ToolRequested = result.ToolRequested,
            ToolUsed = result.ToolUsed,
            PowerShellGetCreated = result.PowerShellGetCreated,
            PSResourceGetCreated = result.PSResourceGetCreated,
            Trusted = result.Trusted,
            CredentialUsed = result.CredentialUsed,
            RegistrationPerformed = result.RegistrationPerformed,
            PSResourceGetRegistered = result.PSResourceGetRegistered,
            PowerShellGetRegistered = result.PowerShellGetRegistered,
            PSResourceGetAvailable = result.PSResourceGetAvailable,
            PSResourceGetVersion = result.PSResourceGetVersion,
            PSResourceGetMeetsMinimumVersion = result.PSResourceGetMeetsMinimumVersion,
            PSResourceGetSupportsExistingSessionBootstrap = result.PSResourceGetSupportsExistingSessionBootstrap,
            PowerShellGetAvailable = result.PowerShellGetAvailable,
            PowerShellGetVersion = result.PowerShellGetVersion,
            AzureArtifactsCredentialProviderDetected = result.AzureArtifactsCredentialProviderDetected,
            AzureArtifactsCredentialProviderPaths = result.AzureArtifactsCredentialProviderPaths,
            AzureArtifactsCredentialProviderVersion = result.AzureArtifactsCredentialProviderVersion,
            ReadinessMessages = result.ReadinessMessages,
            InstalledPrerequisites = result.InstalledPrerequisites,
            PrerequisiteInstallMessages = result.PrerequisiteInstallMessages,
            UnavailableTools = result.UnavailableTools,
            Messages = result.Messages,
            AccessProbePerformed = result.AccessProbePerformed,
            AccessProbeSucceeded = result.AccessProbeSucceeded,
            AccessProbeTool = result.AccessProbeTool,
            AccessProbeMessage = result.AccessProbeMessage
        };
    }
}
