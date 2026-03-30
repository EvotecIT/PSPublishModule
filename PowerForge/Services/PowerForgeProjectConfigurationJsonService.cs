using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Reads and writes PowerShell-authored project release configuration objects as JSON.
/// </summary>
public sealed class PowerForgeProjectConfigurationJsonService
{
    private static readonly JsonSerializerOptions DeserializeOptions = CreateDeserializeOptions();
    private static readonly JsonSerializerOptions SerializeOptions = CreateSerializeOptions();

    /// <summary>
    /// Loads a <see cref="ConfigurationProject"/> from a JSON file.
    /// </summary>
    public ConfigurationProject Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var json = File.ReadAllText(fullPath);
        var project = JsonSerializer.Deserialize<ConfigurationProject>(json, DeserializeOptions);
        if (project is null)
            throw new InvalidOperationException($"Unable to deserialize project configuration: {fullPath}");

        return project;
    }

    /// <summary>
    /// Serializes a <see cref="ConfigurationProject"/> to JSON.
    /// </summary>
    public string Serialize(ConfigurationProject project)
    {
        if (project is null)
            throw new ArgumentNullException(nameof(project));

        return JsonSerializer.Serialize(project, SerializeOptions) + Environment.NewLine;
    }

    /// <summary>
    /// Saves a <see cref="ConfigurationProject"/> to a JSON file and returns the resolved path.
    /// </summary>
    public string Save(ConfigurationProject project, string outputPath, bool overwrite)
    {
        if (project is null)
            throw new ArgumentNullException(nameof(project));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));

        var fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath) && !overwrite)
            throw new InvalidOperationException($"Project configuration already exists: {fullPath}");

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(fullPath, Serialize(project));
        return fullPath;
    }

    private static JsonSerializerOptions CreateDeserializeOptions()
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static JsonSerializerOptions CreateSerializeOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
