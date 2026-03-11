namespace PowerForge;

internal readonly struct CredentialResolutionResult
{
    internal CredentialResolutionResult(
        RepositoryCredential? credential,
        PrivateGalleryBootstrapMode bootstrapModeUsed,
        PrivateGalleryCredentialSource credentialSource)
    {
        Credential = credential;
        BootstrapModeUsed = bootstrapModeUsed;
        CredentialSource = credentialSource;
    }

    internal RepositoryCredential? Credential { get; }
    internal PrivateGalleryBootstrapMode BootstrapModeUsed { get; }
    internal PrivateGalleryCredentialSource CredentialSource { get; }
}

internal readonly struct BootstrapPrerequisiteStatus
{
    internal BootstrapPrerequisiteStatus(
        bool psResourceGetAvailable,
        string? psResourceGetVersion,
        bool psResourceGetMeetsMinimumVersion,
        bool psResourceGetSupportsExistingSessionBootstrap,
        string? psResourceGetMessage,
        bool powerShellGetAvailable,
        string? powerShellGetVersion,
        string? powerShellGetMessage,
        AzureArtifactsCredentialProviderDetectionResult credentialProviderDetection,
        string[] readinessMessages)
    {
        PSResourceGetAvailable = psResourceGetAvailable;
        PSResourceGetVersion = psResourceGetVersion;
        PSResourceGetMeetsMinimumVersion = psResourceGetMeetsMinimumVersion;
        PSResourceGetSupportsExistingSessionBootstrap = psResourceGetSupportsExistingSessionBootstrap;
        PSResourceGetMessage = psResourceGetMessage;
        PowerShellGetAvailable = powerShellGetAvailable;
        PowerShellGetVersion = powerShellGetVersion;
        PowerShellGetMessage = powerShellGetMessage;
        CredentialProviderDetection = credentialProviderDetection;
        ReadinessMessages = readinessMessages;
    }

    internal bool PSResourceGetAvailable { get; }
    internal string? PSResourceGetVersion { get; }
    internal bool PSResourceGetMeetsMinimumVersion { get; }
    internal bool PSResourceGetSupportsExistingSessionBootstrap { get; }
    internal string? PSResourceGetMessage { get; }
    internal bool PowerShellGetAvailable { get; }
    internal string? PowerShellGetVersion { get; }
    internal string? PowerShellGetMessage { get; }
    internal AzureArtifactsCredentialProviderDetectionResult CredentialProviderDetection { get; }
    internal string[] ReadinessMessages { get; }
}

internal readonly struct BootstrapPrerequisiteInstallResult
{
    internal BootstrapPrerequisiteInstallResult(
        string[] installedPrerequisites,
        string[] messages,
        BootstrapPrerequisiteStatus status)
    {
        InstalledPrerequisites = installedPrerequisites;
        Messages = messages;
        Status = status;
    }

    internal string[] InstalledPrerequisites { get; }
    internal string[] Messages { get; }
    internal BootstrapPrerequisiteStatus Status { get; }
}

internal readonly struct RepositoryAccessProbeResult
{
    internal RepositoryAccessProbeResult(bool succeeded, string tool, string? message)
    {
        Succeeded = succeeded;
        Tool = tool;
        Message = message;
    }

    internal bool Succeeded { get; }
    internal string Tool { get; }
    internal string? Message { get; }
}
