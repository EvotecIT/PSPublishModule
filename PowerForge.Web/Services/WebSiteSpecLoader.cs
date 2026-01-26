using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Loads site and project specifications from disk.</summary>
public static class WebSiteSpecLoader
{
    /// <summary>Loads a site spec and returns the resolved path.</summary>
    /// <param name="configPath">Path to site.json.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>Deserialized spec and full path.</returns>
    public static (SiteSpec Spec, string FullPath) LoadWithPath(string configPath, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Config path is required.", nameof(configPath));

        var fullPath = Path.GetFullPath(configPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Config file not found: {fullPath}");

        var json = File.ReadAllText(fullPath);
        var opts = options ?? WebJson.Options;
        var spec = JsonSerializer.Deserialize<SiteSpec>(json, opts);
        if (spec is null)
            throw new InvalidOperationException($"Failed to deserialize site config: {fullPath}");

        return (spec, fullPath);
    }

    /// <summary>Loads a project spec and returns the resolved path.</summary>
    /// <param name="projectPath">Path to project.json.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>Deserialized spec and full path.</returns>
    public static (ProjectSpec Spec, string FullPath) LoadProjectWithPath(string projectPath, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("Project path is required.", nameof(projectPath));

        var fullPath = Path.GetFullPath(projectPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Project file not found: {fullPath}");

        var json = File.ReadAllText(fullPath);
        var opts = options ?? WebJson.Options;
        var spec = JsonSerializer.Deserialize<ProjectSpec>(json, opts);
        if (spec is null)
            throw new InvalidOperationException($"Failed to deserialize project config: {fullPath}");

        return (spec, fullPath);
    }
}
