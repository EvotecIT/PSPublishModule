using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Helpers for reading published PowerForge tool lock files.</summary>
public static class WebToolLockFile
{
    /// <summary>Default repository used by published PowerForge assets.</summary>
    public const string DefaultRepository = "EvotecIT/PSPublishModule";

    /// <summary>Default target used by website workflows.</summary>
    public const string DefaultTarget = "PowerForgeWeb";

    /// <summary>Schema URL for tool lock validation and editor hints.</summary>
    public const string DefaultSchemaUrl = "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.web.toollock.schema.json";

    /// <summary>Read and normalize a tool lock file.</summary>
    public static WebToolLockSpec Read(string path, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Tool lock path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Tool lock file not found: {fullPath}");

        var json = File.ReadAllText(fullPath);
        var parsed = JsonSerializer.Deserialize<WebToolLockSpec>(json, options ?? WebJson.Options);
        if (parsed is null)
            throw new InvalidOperationException($"Tool lock file is invalid JSON: {fullPath}");

        return Normalize(parsed);
    }

    /// <summary>Validate required tool lock fields.</summary>
    public static string[] Validate(WebToolLockSpec? spec)
    {
        if (spec is null)
            return new[] { "tool lock file is empty or invalid." };

        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(spec.Repository))
            issues.Add("tool lock is missing 'repository'.");
        if (string.IsNullOrWhiteSpace(spec.Tag))
            issues.Add("tool lock is missing 'tag'.");
        if (string.IsNullOrWhiteSpace(spec.Asset))
            issues.Add("tool lock is missing 'asset'.");
        return issues.ToArray();
    }

    /// <summary>Normalize optional defaults.</summary>
    public static WebToolLockSpec Normalize(WebToolLockSpec? spec)
    {
        var normalized = spec is null ? new WebToolLockSpec() : new WebToolLockSpec
        {
            Schema = spec.Schema,
            Repository = spec.Repository,
            Target = spec.Target,
            Tag = spec.Tag,
            Asset = spec.Asset,
            BinaryPath = spec.BinaryPath
        };

        if (string.IsNullOrWhiteSpace(normalized.Schema))
            normalized.Schema = DefaultSchemaUrl;
        normalized.Repository = NormalizeValue(normalized.Repository, DefaultRepository);
        normalized.Target = NormalizeValue(normalized.Target, DefaultTarget);
        normalized.Tag = NormalizeValue(normalized.Tag, string.Empty);
        normalized.Asset = NormalizeValue(normalized.Asset, string.Empty);
        normalized.BinaryPath = NormalizeValue(normalized.BinaryPath, string.Empty);
        return normalized;
    }

    private static string NormalizeValue(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }
}
