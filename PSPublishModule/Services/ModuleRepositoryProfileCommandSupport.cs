using System;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleRepositoryProfileCommandSupport
{
    internal static ModuleRepositoryProfile ResolveRequired(
        string profileName,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.All)
        => ResolveRequiredWithStore(profileName, scope).Profile;

    internal static ResolvedModuleRepositoryProfile ResolveRequiredWithStore(
        string profileName,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.All)
    {
        foreach (var store in ModuleRepositoryProfileStore.GetStores(scope))
        {
            var profile = store.GetProfile(profileName);
            if (profile is not null)
                return new ResolvedModuleRepositoryProfile(profile, store);
        }

        throw new InvalidOperationException($"Module repository profile '{profileName}' was not found. Create it with Set-ModuleRepositoryProfile first.");
    }

    internal readonly struct ResolvedModuleRepositoryProfile
    {
        internal ResolvedModuleRepositoryProfile(ModuleRepositoryProfile profile, ModuleRepositoryProfileStore store)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        internal ModuleRepositoryProfile Profile { get; }

        internal ModuleRepositoryProfileStore Store { get; }
    }
}
