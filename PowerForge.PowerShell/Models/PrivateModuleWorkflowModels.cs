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
    internal IReadOnlyDictionary<string, string> RequiredVersions { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyDictionary<string, string> MinimumVersions { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyDictionary<string, bool> MinimumVersionInclusivity { get; set; } = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyDictionary<string, string> MaximumVersions { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyDictionary<string, bool> MaximumVersionInclusivity { get; set; } = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
    internal string? InstallScope { get; set; }
    internal IReadOnlyDictionary<string, string> InstallScopes { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    internal bool UseAzureArtifacts { get; set; }
    internal bool UseMicrosoftArtifactRegistry { get; set; }
    internal string? ProfileName { get; set; }
    internal string RepositoryName { get; set; } = string.Empty;
    internal PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;
    internal string AzureDevOpsOrganization { get; set; } = string.Empty;
    internal string? AzureDevOpsProject { get; set; }
    internal string AzureArtifactsFeed { get; set; } = string.Empty;
    internal string Repository { get; set; } = string.Empty;
    internal string RepositoryUri { get; set; } = string.Empty;
    internal string RepositorySourceUri { get; set; } = string.Empty;
    internal string RepositoryPublishUri { get; set; } = string.Empty;
    internal string JFrogBaseUri { get; set; } = string.Empty;
    internal string JFrogRepository { get; set; } = string.Empty;
    internal RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.Auto;
    internal RepositoryApiVersion ApiVersion { get; set; } = RepositoryApiVersion.Auto;
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
    internal ModuleStateDeliveryTransport DeliveryTransport { get; set; } = ModuleStateDeliveryTransport.PrivateModule;
    internal string? VersionPolicy { get; set; }
    internal ManagedModuleInstallScope ManagedScope { get; set; } = ManagedModuleInstallScope.CurrentUser;
    internal ManagedModuleShellEdition ManagedShellEdition { get; set; } = ManagedModuleShellEdition.Auto;
    internal string? ManagedModuleRoot { get; set; }
    internal string? ManagedPackageCacheDirectory { get; set; }
    internal string? ManagedRepositorySource { get; set; }
    internal bool ManagedAllowClobber { get; set; }
    internal bool ManagedAcceptLicense { get; set; }
    internal bool ManagedSkipDependencyCheck { get; set; }
    internal bool ManagedRequireSourceMatch { get; set; }
    internal bool ManagedAllowLoadedModuleUpdate { get; set; }
    internal IReadOnlyList<ManagedModuleLoadedModule> ManagedLoadedModules { get; set; } = System.Array.Empty<ManagedModuleLoadedModule>();
}

internal sealed class PrivateModuleWorkflowResult
{
    internal bool OperationPerformed { get; set; }
    internal string RepositoryName { get; set; } = string.Empty;
    internal ModuleStateDeliveryTransport RequestedTransport { get; set; } = ModuleStateDeliveryTransport.PrivateModule;
    internal ModuleStateDeliveryTransport EffectiveTransport { get; set; } = ModuleStateDeliveryTransport.PrivateModule;
    internal string DeliveryTransportReason { get; set; } = string.Empty;
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
