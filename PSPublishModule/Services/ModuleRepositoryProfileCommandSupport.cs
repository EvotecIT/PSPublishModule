using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleRepositoryProfileCommandSupport
{
    internal static ModuleRepositoryProfile ResolveRequired(
        string profileName,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.All)
        => ResolveRequiredWithStore(profileName, scope).Profile;

    internal static ModuleRepositoryProfile? TryResolve(
        string? profileName,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.All)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return null;

        foreach (var store in ModuleRepositoryProfileStore.GetStores(scope))
        {
            var profile = store.GetProfile(profileName!);
            if (profile is not null)
                return profile;
        }

        return null;
    }

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

    internal static ResolvedModuleRepositoryProfile[] GetUniqueProfilesWithStores(ModuleRepositoryProfileScope scope)
    {
        var selected = new Dictionary<string, ResolvedModuleRepositoryProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var store in ModuleRepositoryProfileStore.GetStores(scope))
        {
            foreach (var profile in store.GetProfiles())
            {
                if (string.IsNullOrWhiteSpace(profile.Name) || selected.ContainsKey(profile.Name))
                    continue;

                selected[profile.Name] = new ResolvedModuleRepositoryProfile(profile, store);
            }
        }

        return selected.Values
            .OrderBy(item => item.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static ModuleRepositoryProfile[] ResolveUniqueProfiles(
        IEnumerable<string> profileNames,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.All)
    {
        if (profileNames is null) throw new ArgumentNullException(nameof(profileNames));

        return profileNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => ModuleRepositoryProfileStore.NormalizeName(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => ResolveRequired(name, scope))
            .ToArray();
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
