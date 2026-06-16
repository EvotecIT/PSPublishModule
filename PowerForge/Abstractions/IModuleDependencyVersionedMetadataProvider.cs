using System.Collections.Generic;

namespace PowerForge;

internal interface IModuleDependencyVersionedMetadataProvider : IModuleDependencyMetadataProvider
{
    IReadOnlyDictionary<string, InstalledModuleMetadata> GetInstalledModules(IReadOnlyList<RequiredModuleReference> references);
}

internal interface IModuleDependencyReferenceMetadataProvider : IModuleDependencyMetadataProvider
{
    IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(RequiredModuleReference reference);
}
