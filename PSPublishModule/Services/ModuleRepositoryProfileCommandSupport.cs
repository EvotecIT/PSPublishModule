using System;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleRepositoryProfileCommandSupport
{
    internal static ModuleRepositoryProfile ResolveRequired(string profileName)
    {
        var store = new ModuleRepositoryProfileStore();
        var profile = store.GetProfile(profileName);
        if (profile is null)
            throw new InvalidOperationException($"Module repository profile '{profileName}' was not found. Create it with Set-ModuleRepositoryProfile first.");

        return profile;
    }
}
