namespace PowerForge;

/// <summary>Manages the repository profiles shared by PowerForge's native and PowerShell commands.</summary>
public static class ManagedModuleRepositoryProfiles
{
    /// <summary>Lists profiles in precedence order, preferring user profiles over machine profiles with the same name.</summary>
    /// <param name="scope">The store or stores to read.</param>
    /// <returns>The effective profiles.</returns>
    public static ManagedModuleRepositoryProfileInfo[] List(ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.All)
        => ModuleRepositoryProfileStore.GetStores(scope)
            .SelectMany(store => store.GetProfiles().Select(profile => Describe(profile, store.Scope)))
            .GroupBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Adds a named HTTP(S) feed or local folder to the selected profile store.</summary>
    /// <param name="name">Profile name.</param>
    /// <param name="source">Feed URL or local folder.</param>
    /// <param name="trusted">Whether the profile asserts repository trust.</param>
    /// <param name="scope">The store to modify.</param>
    /// <returns>The saved profile.</returns>
    public static ManagedModuleRepositoryProfileInfo Add(string name, string source, bool trusted = false,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.User)
    {
        var store = new ModuleRepositoryProfileStore(scope);
        var repository = ManagedModuleStandaloneInput.ResolveRepository(source, name);
        var profile = new ModuleRepositoryProfile
        {
            Name = name,
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = name,
            RepositoryUri = repository.Source,
            Trusted = trusted,
            Priority = 50
        };
        return Describe(store.ImportProfiles([profile], overwrite: false).Single(), scope);
    }

    /// <summary>Updates a saved profile's trust or the source of a generic repository profile.</summary>
    /// <param name="name">Profile name.</param>
    /// <param name="source">Optional replacement for the generic source endpoint.</param>
    /// <param name="trusted">Optional replacement for the trust flag.</param>
    /// <param name="scope">The store to modify.</param>
    /// <returns>The saved profile.</returns>
    public static ManagedModuleRepositoryProfileInfo Set(string name, string? source = null, bool? trusted = null,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.User)
    {
        var store = new ModuleRepositoryProfileStore(scope);
        var profile = store.GetProfile(name)
            ?? throw new InvalidOperationException($"Repository profile '{name}' was not found in the {scope} store.");
        if (source is not null)
        {
            if (profile.Provider != PrivateGalleryProvider.NuGet)
                throw new InvalidOperationException("Change provider-specific endpoints with Set-ManagedModuleRepository.");
            var previousSource = profile.ReadSource;
            var replacement = ManagedModuleStandaloneInput.ResolveRepository(source, name).Source;
            profile.RepositoryUri = replacement;
            // Preserve independently configured read and publish endpoints.
            if (string.Equals(profile.RepositorySourceUri, previousSource, StringComparison.OrdinalIgnoreCase))
                profile.RepositorySourceUri = replacement;
            if (string.Equals(profile.RepositoryPublishUri, previousSource, StringComparison.OrdinalIgnoreCase))
                profile.RepositoryPublishUri = replacement;
        }
        profile.Trusted = trusted ?? profile.Trusted;
        return Describe(store.SaveProfile(profile), scope);
    }

    /// <summary>Removes a profile from the selected store.</summary>
    /// <param name="name">Profile name.</param>
    /// <param name="scope">The store to modify.</param>
    /// <returns>The removed profile.</returns>
    public static ManagedModuleRepositoryProfileInfo Remove(string name,
        ModuleRepositoryProfileScope scope = ModuleRepositoryProfileScope.User)
    {
        var store = new ModuleRepositoryProfileStore(scope);
        var profile = store.GetProfile(name);
        if (profile is null || !store.RemoveProfile(name))
            throw new InvalidOperationException($"Repository profile '{name}' was not found in the {scope} store.");
        return Describe(profile, scope);
    }

    /// <summary>Resolves a saved profile for module source operations.</summary>
    /// <param name="name">Profile name.</param>
    /// <returns>The repository and its persisted trust policy.</returns>
    public static ManagedModuleRepository Resolve(string name)
    {
        var profile = ModuleRepositoryProfileStore.GetStores(ModuleRepositoryProfileScope.All)
            .Select(store => store.GetProfile(name))
            .FirstOrDefault(static candidate => candidate is not null)
            ?? throw new ArgumentException($"Repository profile '{name}' was not found.", nameof(name));
        var repository = ManagedModuleStandaloneInput.ResolveRepository(profile.ReadSource, profile.EffectiveRepositoryName);
        return new ManagedModuleRepository(repository.Name, repository.Source, repository.Kind, profile.Trusted);
    }

    private static ManagedModuleRepositoryProfileInfo Describe(ModuleRepositoryProfile profile, ModuleRepositoryProfileScope scope)
        => new(profile.Name, profile.ReadSource, scope, profile.Trusted);
}

/// <summary>A saved module repository profile.</summary>
public sealed class ManagedModuleRepositoryProfileInfo
{
    internal ManagedModuleRepositoryProfileInfo(string name, string source, ModuleRepositoryProfileScope scope, bool trusted)
    {
        Name = name;
        Source = source;
        Scope = scope;
        Trusted = trusted;
    }

    /// <summary>Profile name.</summary>
    public string Name { get; }

    /// <summary>Read feed URL or folder.</summary>
    public string Source { get; }

    /// <summary>Store containing the profile.</summary>
    public ModuleRepositoryProfileScope Scope { get; }

    /// <summary>Whether the profile asserts repository trust.</summary>
    public bool Trusted { get; }
}
