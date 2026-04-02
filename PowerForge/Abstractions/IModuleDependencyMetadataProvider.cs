using System.Collections.Generic;

namespace PowerForge;

internal interface IModuleDependencyMetadataProvider
{
    IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names);

    IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
        IReadOnlyCollection<string> names,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease);
}
