using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Loads a strict Search Intelligence provider configuration from disk.</summary>
public static class WebSearchProviderConfigurationLoader
{
    /// <summary>Loads a provider configuration and returns its absolute path.</summary>
    /// <param name="configPath">Path to the provider configuration JSON file.</param>
    /// <param name="options">Optional serializer options.</param>
    /// <returns>The deserialized configuration and resolved path.</returns>
    public static (WebSearchProviderConfiguration Configuration, string FullPath) LoadWithPath(
        string configPath,
        JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Provider configuration path is required.", nameof(configPath));

        var fullPath = Path.GetFullPath(configPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Provider configuration file not found: {fullPath}");

        var serializerOptions = options is null
            ? new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            }
            : new JsonSerializerOptions(options);
        var configuration = JsonSerializer.Deserialize<WebSearchProviderConfiguration>(
            File.ReadAllText(fullPath),
            serializerOptions);
        if (configuration is null)
            throw new InvalidOperationException($"Failed to deserialize provider configuration: {fullPath}");

        return (configuration, fullPath);
    }
}
