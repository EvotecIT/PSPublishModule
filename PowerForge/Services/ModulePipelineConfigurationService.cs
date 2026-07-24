using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Loads and inspects a module pipeline JSON configuration for non-PowerShell hosts.
/// </summary>
public sealed class ModulePipelineConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>
    /// Loads a module pipeline configuration, validates its required build identity,
    /// and resolves project-relative publish and artefact paths.
    /// </summary>
    /// <param name="configPath">Path to the module pipeline JSON document.</param>
    public ModulePipelineConfigurationContext Load(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Module pipeline config path is required.", nameof(configPath));

        var fullPath = Path.GetFullPath(configPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Module pipeline config was not found: {fullPath}", fullPath);

        ModulePipelineSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<ModulePipelineSpec>(File.ReadAllText(fullPath), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Module pipeline config is not valid JSON: {fullPath}. {ex.Message}", ex);
        }

        if (spec?.Build is null)
            throw new InvalidOperationException($"Module pipeline config requires a Build section: {fullPath}");
        if (string.IsNullOrWhiteSpace(spec.Build.Name))
            throw new InvalidOperationException($"Module pipeline config requires Build.Name: {fullPath}");
        if (string.IsNullOrWhiteSpace(spec.Build.SourcePath))
            throw new InvalidOperationException($"Module pipeline config requires Build.SourcePath: {fullPath}");

        var configDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var projectRoot = PathTokenProtection.GetFullPath(configDirectory, spec.Build.SourcePath);
        spec.Build.SourcePath = projectRoot;
        ResolveProjectPaths(spec, projectRoot);

        return new ModulePipelineConfigurationContext
        {
            ConfigPath = fullPath,
            ProjectRoot = projectRoot,
            Spec = spec,
            EffectiveVersion = ResolveEffectiveVersion(spec),
            ArtifactPaths = ResolveArtifactPaths(spec, projectRoot)
        };
    }

    /// <summary>
    /// Attempts to load a valid module pipeline configuration.
    /// </summary>
    public bool TryLoad(string configPath, out ModulePipelineConfigurationContext? context)
    {
        try
        {
            context = Load(configPath);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        context = null;
        return false;
    }

    /// <summary>
    /// Resolves the effective module version using the same last-manifest-wins
    /// semantics as module pipeline execution.
    /// </summary>
    public static string? ResolveEffectiveVersion(ModulePipelineSpec? spec)
    {
        var manifestVersion = (spec?.Segments ?? Array.Empty<IConfigurationSegment>())
            .OfType<ConfigurationManifestSegment>()
            .Select(segment => segment.Configuration?.ModuleVersion)
            .LastOrDefault(version => !string.IsNullOrWhiteSpace(version));
        return string.IsNullOrWhiteSpace(manifestVersion)
            ? NullIfWhiteSpace(spec?.Build?.Version)
            : manifestVersion!.Trim();
    }

    private static void ResolveProjectPaths(ModulePipelineSpec spec, string projectRoot)
    {
        foreach (var publish in (spec.Segments ?? Array.Empty<IConfigurationSegment>())
                     .OfType<ConfigurationPublishSegment>())
        {
            if (!string.IsNullOrWhiteSpace(publish.Configuration.ApiKeyFilePath))
            {
                publish.Configuration.ApiKeyFilePath = PathTokenProtection.GetFullPath(
                    projectRoot,
                    publish.Configuration.ApiKeyFilePath!);
            }
        }

        foreach (var artefact in (spec.Segments ?? Array.Empty<IConfigurationSegment>())
                     .OfType<ConfigurationArtefactSegment>())
        {
            if (!string.IsNullOrWhiteSpace(artefact.Configuration.Path))
            {
                artefact.Configuration.Path = PathTokenProtection.GetFullPath(
                    projectRoot,
                    artefact.Configuration.Path!);
            }
        }
    }

    private static string[] ResolveArtifactPaths(ModulePipelineSpec spec, string projectRoot)
    {
        var configuredPaths = (spec.Segments ?? Array.Empty<IConfigurationSegment>())
            .OfType<ConfigurationArtefactSegment>()
            .Where(segment => segment.Configuration?.Enabled == true)
            .Select(segment => string.IsNullOrWhiteSpace(segment.Configuration.Path)
                ? Path.Combine(projectRoot, "Artefacts", segment.ArtefactType.ToString())
                : segment.Configuration.Path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (configuredPaths.Length > 0)
            return configuredPaths;

        return new[]
        {
            Path.Combine(projectRoot, "Artefacts", "Packed"),
            Path.Combine(projectRoot, "Artefacts", "PackedWithModules"),
            Path.Combine(projectRoot, "Artefacts", "Unpacked")
        };
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ConfigurationSegmentJsonConverter());
        return options;
    }
}

/// <summary>
/// Resolved view of a module pipeline JSON configuration.
/// </summary>
public sealed class ModulePipelineConfigurationContext
{
    /// <summary>Absolute configuration path.</summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Absolute module project root resolved from Build.SourcePath.</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>Validated module pipeline specification.</summary>
    public ModulePipelineSpec Spec { get; set; } = new();

    /// <summary>Effective version after applying last-manifest-wins semantics.</summary>
    public string? EffectiveVersion { get; set; }

    /// <summary>Resolved candidate artefact output directories.</summary>
    public string[] ArtifactPaths { get; set; } = Array.Empty<string>();
}
