using PowerForge;

namespace PSPublishModule;

internal static class ModuleRepositoryProfileResultMapper
{
    internal static ModuleRepositoryProfileResult ToCmdletResult(
        ModuleRepositoryProfile profile,
        string profileStorePath,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.User)
    {
        return new ModuleRepositoryProfileResult
        {
            Name = profile.Name,
            Provider = profile.Provider,
            AzureDevOpsOrganization = profile.AzureDevOpsOrganization,
            AzureDevOpsProject = profile.AzureDevOpsProject,
            AzureArtifactsFeed = profile.AzureArtifactsFeed,
            RepositoryName = profile.RepositoryName,
            Tool = profile.Tool,
            BootstrapMode = profile.BootstrapMode,
            Trusted = profile.Trusted,
            Priority = profile.Priority,
            AuthenticationMode = profile.AuthenticationMode,
            ProfileStorePath = profileStorePath,
            Scope = scope,
            CreatedAtUtc = profile.CreatedAtUtc,
            UpdatedAtUtc = profile.UpdatedAtUtc
        };
    }
}
