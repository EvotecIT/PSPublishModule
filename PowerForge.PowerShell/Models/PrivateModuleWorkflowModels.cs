using System.Collections.Generic;

namespace PowerForge;

internal enum PrivateModuleWorkflowOperation
{
    Install,
    Update
}

internal sealed class PrivateModuleWorkflowRequest
{
    internal PrivateModuleWorkflowOperation Operation { get; set; }
    internal IReadOnlyList<string> ModuleNames { get; set; } = System.Array.Empty<string>();
    internal bool UseAzureArtifacts { get; set; }
    internal string RepositoryName { get; set; } = string.Empty;
    internal PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;
    internal string AzureDevOpsOrganization { get; set; } = string.Empty;
    internal string? AzureDevOpsProject { get; set; }
    internal string AzureArtifactsFeed { get; set; } = string.Empty;
    internal RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.Auto;
    internal PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.Auto;
    internal bool Trusted { get; set; } = true;
    internal int? Priority { get; set; }
    internal string? CredentialUserName { get; set; }
    internal string? CredentialSecret { get; set; }
    internal string? CredentialSecretFilePath { get; set; }
    internal bool PromptForCredential { get; set; }
    internal bool InstallPrerequisites { get; set; }
    internal bool Prerelease { get; set; }
    internal bool Force { get; set; }
}

internal sealed class PrivateModuleWorkflowResult
{
    internal bool OperationPerformed { get; set; }
    internal string RepositoryName { get; set; } = string.Empty;
    internal IReadOnlyList<ModuleDependencyInstallResult> DependencyResults { get; set; } = System.Array.Empty<ModuleDependencyInstallResult>();
}

internal sealed class PrivateModuleDependencyExecutionRequest
{
    internal PrivateModuleWorkflowOperation Operation { get; set; }
    internal IReadOnlyList<ModuleDependency> Modules { get; set; } = System.Array.Empty<ModuleDependency>();
    internal string RepositoryName { get; set; } = string.Empty;
    internal RepositoryCredential? Credential { get; set; }
    internal bool Prerelease { get; set; }
    internal bool Force { get; set; }
    internal bool PreferPowerShellGet { get; set; }
}
