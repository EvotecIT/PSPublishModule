using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Reads module publish configuration segments from a PowerForge module pipeline JSON document.
/// </summary>
public sealed class ModulePublishConfigurationReader
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>
    /// Reads publish configuration segments from the specified module pipeline JSON file.
    /// </summary>
    /// <param name="jsonPath">Path to a module pipeline JSON file.</param>
    /// <returns>Publish configurations declared by the pipeline.</returns>
    public IReadOnlyList<PublishConfiguration> Read(string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
            throw new ArgumentException("JSON path is required.", nameof(jsonPath));

        var fullPath = Path.GetFullPath(jsonPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Module pipeline JSON file was not found: {fullPath}", fullPath);

        return ReadFromJson(File.ReadAllText(fullPath));
    }

    /// <summary>
    /// Reads publish configuration segments from a module pipeline JSON payload.
    /// </summary>
    /// <param name="json">Module pipeline JSON text.</param>
    /// <returns>Publish configurations declared by the pipeline.</returns>
    public IReadOnlyList<PublishConfiguration> ReadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<PublishConfiguration>();

        var spec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, JsonOptions);
        if (spec?.Segments is not { Length: > 0 })
            return Array.Empty<PublishConfiguration>();

        return spec.Segments
            .OfType<ConfigurationPublishSegment>()
            .Select(segment => ClonePublishConfiguration(segment.Configuration))
            .ToArray();
    }

    private static PublishConfiguration ClonePublishConfiguration(PublishConfiguration configuration)
    {
        configuration ??= new PublishConfiguration();
        return new PublishConfiguration {
            Destination = configuration.Destination,
            Tool = configuration.Tool,
            ApiKey = configuration.ApiKey,
            ID = configuration.ID,
            Enabled = configuration.Enabled,
            UserName = configuration.UserName,
            RepositoryName = configuration.RepositoryName,
            Repository = CloneRepository(configuration.Repository),
            Force = configuration.Force,
            OverwriteTagName = configuration.OverwriteTagName,
            DoNotMarkAsPreRelease = configuration.DoNotMarkAsPreRelease,
            GenerateReleaseNotes = configuration.GenerateReleaseNotes,
            Verbose = configuration.Verbose
        };
    }

    private static PublishRepositoryConfiguration? CloneRepository(PublishRepositoryConfiguration? repository)
    {
        if (repository is null)
            return null;

        return new PublishRepositoryConfiguration {
            Name = repository.Name,
            Uri = repository.Uri,
            SourceUri = repository.SourceUri,
            PublishUri = repository.PublishUri,
            Trusted = repository.Trusted,
            Priority = repository.Priority,
            ApiVersion = repository.ApiVersion,
            EnsureRegistered = repository.EnsureRegistered,
            UnregisterAfterUse = repository.UnregisterAfterUse,
            Credential = repository.Credential is null
                ? null
                : new RepositoryCredential {
                    UserName = repository.Credential.UserName,
                    Secret = repository.Credential.Secret
                }
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ConfigurationSegmentJsonConverter());
        return options;
    }
}
