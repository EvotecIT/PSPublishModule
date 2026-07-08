using System;
using PowerForge;

namespace PSPublishModule;

internal static class ManagedModuleCatalogCommandSupport
{
    internal static ManagedModuleCatalogStore CreateStore(ModuleRepositoryProfileScope scope)
    {
        if (scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("A concrete catalog scope is required.", nameof(scope));

        return new ManagedModuleCatalogStore(ResolvePath(scope));
    }

    internal static ManagedModuleCatalogStore[] CreateStores(ModuleRepositoryProfileScope scope)
    {
        return scope switch
        {
            ModuleRepositoryProfileScope.User => new[] { CreateStore(ModuleRepositoryProfileScope.User) },
            ModuleRepositoryProfileScope.Machine => new[] { CreateStore(ModuleRepositoryProfileScope.Machine) },
            ModuleRepositoryProfileScope.All => new[]
            {
                CreateStore(ModuleRepositoryProfileScope.User),
                CreateStore(ModuleRepositoryProfileScope.Machine)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };
    }

    internal static string ResolvePath(ModuleRepositoryProfileScope scope)
        => scope switch
        {
            ModuleRepositoryProfileScope.User => ManagedModuleCatalogStore.GetDefaultPath(machine: false),
            ModuleRepositoryProfileScope.Machine => ManagedModuleCatalogStore.GetDefaultPath(machine: true),
            _ => throw new ArgumentException("A concrete catalog scope is required.", nameof(scope))
        };
}
