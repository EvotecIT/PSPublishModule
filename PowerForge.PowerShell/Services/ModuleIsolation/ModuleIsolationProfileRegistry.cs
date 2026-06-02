using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Resolves built-in module isolation profiles by name.
/// </summary>
public sealed class ModuleIsolationProfileRegistry
{
    private static readonly ModuleIsolationProfile[] BuiltInProfiles =
    [
        ModuleIsolationProfile.ExchangeOnlineManagement,
        ModuleIsolationProfile.MicrosoftTeams,
        ModuleIsolationProfile.MicrosoftGraphAuthentication
    ];

    private readonly Dictionary<string, ModuleIsolationProfile> _profiles;

    /// <summary>Initializes a registry with built-in profiles and optional additional profiles.</summary>
    public ModuleIsolationProfileRegistry(IEnumerable<ModuleIsolationProfile>? profiles = null)
    {
        _profiles = BuiltInProfiles
            .Concat(profiles ?? Array.Empty<ModuleIsolationProfile>())
            .Where(static profile => !string.IsNullOrWhiteSpace(profile.Name))
            .GroupBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets all registered profiles.</summary>
    public IReadOnlyCollection<ModuleIsolationProfile> Profiles => _profiles.Values;

    /// <summary>Resolves a profile by name.</summary>
    public ModuleIsolationProfile Resolve(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            throw new ArgumentException("Profile name is required.", nameof(profileName));

        if (_profiles.TryGetValue(profileName.Trim(), out var profile))
            return profile;

        var available = string.Join(", ", _profiles.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase));
        throw new InvalidOperationException($"Unknown isolated module profile '{profileName}'. Available profiles: {available}.");
    }
}
