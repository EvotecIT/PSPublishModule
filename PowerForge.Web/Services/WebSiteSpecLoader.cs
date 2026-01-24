using System.Text.Json;

namespace PowerForge.Web;

public static class WebSiteSpecLoader
{
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
