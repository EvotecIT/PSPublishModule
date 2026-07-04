using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class ModuleRepositoryProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _path;
    private readonly ModuleRepositoryProfileScope _scope;

    public ModuleRepositoryProfileStore()
        : this(null, ModuleRepositoryProfileScope.User)
    {
    }

    public ModuleRepositoryProfileStore(string? path)
        : this(path, ModuleRepositoryProfileScope.User)
    {
    }

    public ModuleRepositoryProfileStore(ModuleRepositoryProfileScope scope)
        : this(null, scope)
    {
    }

    public ModuleRepositoryProfileStore(string? path, ModuleRepositoryProfileScope scope)
    {
        if (scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("A concrete profile store scope is required.", nameof(scope));

        _scope = scope;
        _path = string.IsNullOrWhiteSpace(path) ? GetDefaultPath(scope) : System.IO.Path.GetFullPath(path!);
    }

    public string Path => _path;

    public ModuleRepositoryProfileScope Scope => _scope;

    public ModuleRepositoryProfile[] GetProfiles()
        => ReadDocument().Profiles
            .Where(static profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ModuleRepositoryProfile[] GetProfiles(IEnumerable<string>? names)
    {
        if (names is null)
            return GetProfiles();

        var requestedNames = names
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => NormalizeName(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requestedNames.Length == 0)
            return GetProfiles();

        var profiles = GetProfiles();
        var selected = new List<ModuleRepositoryProfile>(requestedNames.Length);
        foreach (var name in requestedNames)
        {
            var profile = profiles.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
                throw new InvalidOperationException($"Module repository profile '{name}' was not found.");

            selected.Add(profile);
        }

        return selected.ToArray();
    }

    public ModuleRepositoryProfile? GetProfile(string name)
    {
        var normalizedName = NormalizeName(name);
        return GetProfiles().FirstOrDefault(profile => string.Equals(profile.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    public ModuleRepositoryProfile SaveProfile(ModuleRepositoryProfile profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        var normalized = Normalize(profile);
        var document = ReadDocument();
        var profiles = document.Profiles
            .Where(existing => existing is not null && !string.IsNullOrWhiteSpace(existing.Name))
            .Where(existing => !string.Equals(existing.Name, normalized.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var existing = document.Profiles
            .Where(static existing => existing is not null && !string.IsNullOrWhiteSpace(existing.Name))
            .FirstOrDefault(existing => string.Equals(existing.Name, normalized.Name, StringComparison.OrdinalIgnoreCase));
        var now = DateTimeOffset.UtcNow;
        normalized.CreatedAtUtc = existing?.CreatedAtUtc == default ? now : existing?.CreatedAtUtc ?? now;
        normalized.UpdatedAtUtc = now;
        profiles.Add(normalized);

        document.Profiles = profiles
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        WriteDocument(document);
        return normalized;
    }

    public bool RemoveProfile(string name)
    {
        var normalizedName = NormalizeName(name);
        var document = ReadDocument();
        var existing = document.Profiles
            .Where(static profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
            .ToArray();
        var kept = existing
            .Where(profile => !string.Equals(profile.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (kept.Length == existing.Length)
            return false;

        document.Profiles = kept;
        WriteDocument(document);
        return true;
    }

    public ModuleRepositoryProfile[] ImportProfiles(IEnumerable<ModuleRepositoryProfile> profiles, bool overwrite)
    {
        if (profiles is null) throw new ArgumentNullException(nameof(profiles));

        var normalizedProfiles = profiles
            .Where(static profile => profile is not null)
            .Select(Normalize)
            .ToArray();

        if (normalizedProfiles.Length == 0)
            return Array.Empty<ModuleRepositoryProfile>();

        var document = ReadDocument();
        var existingProfiles = document.Profiles
            .Where(static profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
            .Select(Normalize)
            .ToList();

        var imported = new Dictionary<string, ModuleRepositoryProfile>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        foreach (var profile in normalizedProfiles)
        {
            var existing = existingProfiles.FirstOrDefault(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !overwrite)
                throw new InvalidOperationException($"Module repository profile '{profile.Name}' already exists. Use overwrite to replace it.");

            if (existing is not null)
                existingProfiles.Remove(existing);

            profile.CreatedAtUtc = profile.CreatedAtUtc == default
                ? existing?.CreatedAtUtc == default ? now : existing?.CreatedAtUtc ?? now
                : profile.CreatedAtUtc;
            profile.UpdatedAtUtc = now;
            existingProfiles.Add(profile);
            imported[profile.Name] = profile;
        }

        document.Profiles = existingProfiles
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        WriteDocument(document);
        return imported.Values
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ModuleRepositoryProfile[] ReplaceProfiles(IEnumerable<ModuleRepositoryProfile> profiles)
    {
        if (profiles is null) throw new ArgumentNullException(nameof(profiles));

        var normalizedProfiles = profiles
            .Where(static profile => profile is not null)
            .Select(Normalize)
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        foreach (var profile in normalizedProfiles)
        {
            profile.CreatedAtUtc = profile.CreatedAtUtc == default ? now : profile.CreatedAtUtc;
            profile.UpdatedAtUtc = now;
        }

        WriteDocument(new ModuleRepositoryProfileDocument
        {
            Profiles = normalizedProfiles
        });

        return normalizedProfiles;
    }

    public void WriteProfilesFile(string path, IEnumerable<ModuleRepositoryProfile> profiles)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));
        if (profiles is null)
            throw new ArgumentNullException(nameof(profiles));

        var document = new ModuleRepositoryProfileDocument
        {
            Profiles = profiles
                .Where(static profile => profile is not null)
                .Select(Normalize)
                .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        WriteDocument(path, document);
    }

    public static ModuleRepositoryProfile[] ReadProfilesFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Module repository profile file was not found.", path);

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<ModuleRepositoryProfile>();

        var document = NormalizeDocument(JsonSerializer.Deserialize<ModuleRepositoryProfileDocument>(json, JsonOptions));
        return document.Profiles
            .Where(static profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
            .Select(Normalize)
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static ModuleRepositoryProfile Normalize(ModuleRepositoryProfile profile)
    {
        var name = NormalizeName(profile.Name);
        var provider = NormalizeProvider(profile.Provider);
        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            provider,
            profile.AzureDevOpsOrganization,
            profile.AzureDevOpsProject,
            profile.AzureArtifactsFeed,
            profile.RepositoryName,
            profile.Repository,
            profile.RepositoryUri,
            profile.RepositorySourceUri,
            profile.RepositoryPublishUri,
            profile.JFrogBaseUri,
            profile.JFrogRepository,
            profile.GitHubOwner);

        return new ModuleRepositoryProfile
        {
            Name = name,
            Provider = endpoint.Provider,
            AzureDevOpsOrganization = endpoint.AzureDevOpsOrganization ?? string.Empty,
            AzureDevOpsProject = endpoint.AzureDevOpsProject,
            AzureArtifactsFeed = endpoint.Repository,
            Repository = endpoint.Repository,
            RepositoryName = endpoint.RepositoryName,
            RepositoryUri = endpoint.PSResourceGetUri,
            RepositorySourceUri = endpoint.PowerShellGetSourceUri,
            RepositoryPublishUri = endpoint.PowerShellGetPublishUri,
            JFrogBaseUri = endpoint.JFrogBaseUri ?? string.Empty,
            JFrogRepository = endpoint.JFrogRepository ?? string.Empty,
            GitHubOwner = endpoint.GitHubOwner ?? string.Empty,
            Tool = profile.Tool,
            ApiVersion = profile.ApiVersion,
            BootstrapMode = ResolveBootstrapMode(endpoint.Provider, profile.BootstrapMode),
            Trusted = profile.Trusted,
            Priority = profile.Priority ?? PrivateGalleryDefaults.AzureArtifactsRepositoryPriority,
            AuthenticationMode = ResolveAuthenticationMode(endpoint.Provider, profile.AuthenticationMode),
            CreatedAtUtc = profile.CreatedAtUtc,
            UpdatedAtUtc = profile.UpdatedAtUtc
        };
    }

    private static PrivateGalleryProvider NormalizeProvider(PrivateGalleryProvider provider)
        => provider == PrivateGalleryProvider.AzureArtifacts
            ? PrivateGalleryProvider.AzureArtifacts
            : provider;

    private static PrivateGalleryBootstrapMode ResolveBootstrapMode(PrivateGalleryProvider provider, PrivateGalleryBootstrapMode mode)
        => provider == PrivateGalleryProvider.AzureArtifacts
            ? mode
            : provider == PrivateGalleryProvider.JFrog && mode == PrivateGalleryBootstrapMode.JFrogCli
                ? PrivateGalleryBootstrapMode.JFrogCli
                : provider == PrivateGalleryProvider.NuGet &&
                  (mode == PrivateGalleryBootstrapMode.Auto || mode == PrivateGalleryBootstrapMode.ExistingSession)
                    ? PrivateGalleryBootstrapMode.Auto
                : mode == PrivateGalleryBootstrapMode.Auto || mode == PrivateGalleryBootstrapMode.ExistingSession
                    ? PrivateGalleryBootstrapMode.CredentialPrompt
                    : mode;

    private static string GetDefaultAuthenticationMode(PrivateGalleryProvider provider)
        => provider switch
        {
            PrivateGalleryProvider.AzureArtifacts => "AzureArtifactsCredentialProvider",
            PrivateGalleryProvider.NuGet => string.Empty,
            _ => "CredentialPrompt"
        };

    private static string ResolveAuthenticationMode(PrivateGalleryProvider provider, string? authenticationMode)
    {
        if (string.IsNullOrWhiteSpace(authenticationMode))
            return GetDefaultAuthenticationMode(provider);

        var normalized = authenticationMode!.Trim();
        if (provider != PrivateGalleryProvider.AzureArtifacts &&
            string.Equals(normalized, "AzureArtifactsCredentialProvider", StringComparison.OrdinalIgnoreCase))
        {
            return GetDefaultAuthenticationMode(provider);
        }

        return normalized;
    }

    internal static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profile name is required.", nameof(name));

        return name.Trim();
    }

    private ModuleRepositoryProfileDocument ReadDocument()
    {
        if (!File.Exists(_path))
            return new ModuleRepositoryProfileDocument();

        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json))
            return new ModuleRepositoryProfileDocument();

        try
        {
            return NormalizeDocument(JsonSerializer.Deserialize<ModuleRepositoryProfileDocument>(json, JsonOptions));
        }
        catch (JsonException)
        {
            return new ModuleRepositoryProfileDocument();
        }
    }

    private static ModuleRepositoryProfileDocument NormalizeDocument(ModuleRepositoryProfileDocument? document)
    {
        document ??= new ModuleRepositoryProfileDocument();
        document.Profiles ??= Array.Empty<ModuleRepositoryProfile>();
        return document;
    }

    private void WriteDocument(ModuleRepositoryProfileDocument document)
        => WriteDocument(_path, document);

    private static void WriteDocument(string path, ModuleRepositoryProfileDocument document)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory!);

        var json = JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
        File.WriteAllText(path, json);
    }

    internal static ModuleRepositoryProfileStore[] GetStores(ModuleRepositoryProfileScope scope)
    {
        return scope switch
        {
            ModuleRepositoryProfileScope.User => new[] { new ModuleRepositoryProfileStore(ModuleRepositoryProfileScope.User) },
            ModuleRepositoryProfileScope.Machine => new[] { new ModuleRepositoryProfileStore(ModuleRepositoryProfileScope.Machine) },
            ModuleRepositoryProfileScope.All => new[]
            {
                new ModuleRepositoryProfileStore(ModuleRepositoryProfileScope.User),
                new ModuleRepositoryProfileStore(ModuleRepositoryProfileScope.Machine)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };
    }

    internal static string GetDefaultPath(ModuleRepositoryProfileScope scope)
    {
        if (scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("A concrete profile store scope is required.", nameof(scope));

        var overrideVariable = scope == ModuleRepositoryProfileScope.Machine
            ? "POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH"
            : "POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH";
        var overridePath = Environment.GetEnvironmentVariable(overrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return System.IO.Path.GetFullPath(overridePath!);

        var root = Environment.GetFolderPath(scope == ModuleRepositoryProfileScope.Machine
            ? Environment.SpecialFolder.CommonApplicationData
            : Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(root))
                root = Environment.CurrentDirectory;
        }

        return System.IO.Path.Combine(root, "PowerForge", "PrivateGalleries", "profiles.json");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
