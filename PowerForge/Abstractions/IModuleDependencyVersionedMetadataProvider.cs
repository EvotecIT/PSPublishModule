using System.Collections.Generic;

namespace PowerForge;

internal interface IModuleDependencyVersionedMetadataProvider : IModuleDependencyMetadataProvider
{
    IReadOnlyDictionary<string, InstalledModuleMetadata> GetInstalledModules(IReadOnlyList<RequiredModuleReference> references);
}
