using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class ModuleRepositoryProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _path;

    public ModuleRepositoryProfileStore()
        : this(null)
    {
    }

    public ModuleRepositoryProfileStore(string? path)
    {
        _path = string.IsNullOrWhiteSpace(path) ? GetDefaultPath() : System.IO.Path.GetFullPath(path!);
    }

    public string Path => _path;

    public ModuleRepositoryProfile[] GetProfiles()
        => ReadDocument().Profiles
            .Where(static profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

        var existing = document.Profiles.FirstOrDefault(existing => string.Equals(existing.Name, normalized.Name, StringComparison.OrdinalIgnoreCase));
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
        var kept = document.Profiles
            .Where(profile => !string.Equals(profile.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (kept.Length == document.Profiles.Length)
            return false;

        document.Profiles = kept;
        WriteDocument(document);
        return true;
    }

    internal static ModuleRepositoryProfile Normalize(ModuleRepositoryProfile profile)
    {
        if (profile.Provider != PrivateGalleryProvider.AzureArtifacts)
            throw new ArgumentException($"Provider '{profile.Provider}' is not supported yet. Supported value: AzureArtifacts.", nameof(profile));

        var name = NormalizeName(profile.Name);
        var endpoint = AzureArtifactsRepositoryEndpoints.Create(
            profile.AzureDevOpsOrganization,
            profile.AzureDevOpsProject,
            profile.AzureArtifactsFeed,
            profile.RepositoryName);

        return new ModuleRepositoryProfile
        {
            Name = name,
            Provider = profile.Provider,
            AzureDevOpsOrganization = endpoint.Organization,
            AzureDevOpsProject = endpoint.Project,
            AzureArtifactsFeed = endpoint.Feed,
            RepositoryName = endpoint.RepositoryName,
            Tool = profile.Tool,
            BootstrapMode = profile.BootstrapMode,
            Trusted = profile.Trusted,
            Priority = profile.Priority,
            AuthenticationMode = string.IsNullOrWhiteSpace(profile.AuthenticationMode)
                ? "AzureArtifactsCredentialProvider"
                : profile.AuthenticationMode.Trim(),
            CreatedAtUtc = profile.CreatedAtUtc,
            UpdatedAtUtc = profile.UpdatedAtUtc
        };
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

        return JsonSerializer.Deserialize<ModuleRepositoryProfileDocument>(json, JsonOptions) ?? new ModuleRepositoryProfileDocument();
    }

    private void WriteDocument(ModuleRepositoryProfileDocument document)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory!);

        var json = JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
        File.WriteAllText(_path, json);
    }

    private static string GetDefaultPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return System.IO.Path.GetFullPath(overridePath!);

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
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
