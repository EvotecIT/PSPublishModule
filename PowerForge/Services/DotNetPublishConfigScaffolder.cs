using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Scaffolds starter dotnet publish configuration files for the PowerForge engine.
/// </summary>
public sealed class DotNetPublishConfigScaffolder
{
    private static readonly JsonSerializerOptions SerializeOptions = CreateSerializeOptions();
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new scaffolder.
    /// </summary>
    /// <param name="logger">Logger used for warnings and verbose diagnostics.</param>
    public DotNetPublishConfigScaffolder(ILogger logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Generates a starter <c>powerforge.dotnetpublish.json</c> file.
    /// </summary>
    /// <param name="options">Scaffolding options.</param>
    /// <returns>Metadata describing generated config and inferred values.</returns>
    public DotNetPublishConfigScaffoldResult Generate(DotNetPublishConfigScaffoldOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        var projectRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(options.ProjectRoot)
            ? Directory.GetCurrentDirectory()
            : options.ProjectRoot.Trim().Trim('"'));
        if (!Directory.Exists(projectRoot))
            throw new DirectoryNotFoundException($"Project root does not exist: {projectRoot}");

        var projectPath = ResolveProjectPath(projectRoot, options.ProjectPath);
        var framework = !string.IsNullOrWhiteSpace(options.Framework)
            ? options.Framework!.Trim()
            : InferFramework(projectPath) ?? "net8.0";
        var runtimes = ResolveRuntimes(projectPath, options.Runtimes);
        var styles = ResolveStyles(options.Styles);
        var targetName = string.IsNullOrWhiteSpace(options.TargetName)
            ? Path.GetFileNameWithoutExtension(projectPath)
            : options.TargetName!.Trim();
        var configuration = string.IsNullOrWhiteSpace(options.Configuration)
            ? "Release"
            : options.Configuration.Trim();

        var outputPath = ResolvePath(projectRoot, string.IsNullOrWhiteSpace(options.OutputPath)
            ? "powerforge.dotnetpublish.json"
            : options.OutputPath);
        EnsurePathWithinRoot(projectRoot, outputPath, "Scaffold output path");

        if (File.Exists(outputPath) && !options.Overwrite)
            throw new IOException($"Config already exists: {outputPath}. Use overwrite=true to replace it.");

        var spec = BuildSpec(
            projectRoot,
            projectPath,
            framework,
            runtimes,
            styles,
            targetName,
            configuration,
            options.IncludeSchema);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var json = JsonSerializer.Serialize(spec, SerializeOptions);
        File.WriteAllText(outputPath, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        _logger.Verbose($"Generated dotnet publish scaffold: {outputPath}");
        return new DotNetPublishConfigScaffoldResult
        {
            ConfigPath = outputPath,
            ProjectPath = spec.Targets[0].ProjectPath,
            TargetName = targetName,
            Framework = framework,
            Runtimes = runtimes,
            Styles = styles
        };
    }

    private static DotNetPublishSpec BuildSpec(
        string projectRoot,
        string projectPath,
        string framework,
        string[] runtimes,
        DotNetPublishStyle[] styles,
        string targetName,
        string configuration,
        bool includeSchema)
    {
        var relativeProjectPath = GetRelativePathCompat(projectRoot, projectPath).Replace('\\', '/');
        var style = styles.Length > 0 ? styles[0] : DotNetPublishStyle.PortableCompat;

        return new DotNetPublishSpec
        {
            Schema = includeSchema ? "./Schemas/powerforge.dotnetpublish.schema.json" : null,
            SchemaVersion = 1,
            DotNet = new DotNetPublishDotNetOptions
            {
                ProjectRoot = ".",
                Configuration = configuration,
                Runtimes = runtimes
            },
            Targets = new[]
            {
                new DotNetPublishTarget
                {
                    Name = targetName,
                    ProjectPath = relativeProjectPath,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = framework,
                        Style = style,
                        Styles = styles,
                        Runtimes = runtimes,
                        UseStaging = true,
                        ClearOutput = true,
                        Slim = true,
                        PruneReferences = true,
                        Zip = true
                    }
                }
            },
            Outputs = new DotNetPublishOutputs
            {
                ManifestJsonPath = "Artifacts/DotNetPublish/manifest.json",
                ManifestTextPath = "Artifacts/DotNetPublish/manifest.txt",
                ChecksumsPath = "Artifacts/DotNetPublish/SHA256SUMS.txt",
                RunReportPath = "Artifacts/DotNetPublish/run-report.json"
            }
        };
    }

    private string ResolveProjectPath(string projectRoot, string? providedProjectPath)
    {
        if (!string.IsNullOrWhiteSpace(providedProjectPath))
        {
            var explicitPath = ResolvePath(projectRoot, providedProjectPath!);
            if (!File.Exists(explicitPath))
                throw new FileNotFoundException($"Project file not found: {explicitPath}");
            return explicitPath;
        }

        var candidates = Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredPath(path))
            .OrderBy(path => GetRelativePathCompat(projectRoot, path).Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
            .ThenBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException($"No .csproj files found under project root: {projectRoot}");

        if (candidates.Length > 1)
        {
            _logger.Warn(
                $"Found {candidates.Length} .csproj files. Scaffolding will use '{GetRelativePathCompat(projectRoot, candidates[0])}'. " +
                "Use --project to select a specific project.");
        }

        return candidates[0];
    }

    private static bool IsIgnoredPath(string path)
    {
        static bool IsIgnoredSegment(string segment)
            => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
               || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
               || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
               || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase);

        var relative = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = relative.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(IsIgnoredSegment);
    }

    private static string? InferFramework(string projectPath)
    {
        var doc = TryLoadProject(projectPath);
        if (doc is null) return null;

        var single = ReadFirstProperty(doc, "TargetFramework");
        if (!string.IsNullOrWhiteSpace(single))
            return single!.Trim();

        var multi = ReadFirstProperty(doc, "TargetFrameworks");
        if (string.IsNullOrWhiteSpace(multi))
            return null;

        return multi!
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string[] ResolveRuntimes(string projectPath, string[]? runtimes)
    {
        var explicitRuntimes = NormalizeStrings(runtimes);
        if (explicitRuntimes.Length > 0)
            return explicitRuntimes;

        var doc = TryLoadProject(projectPath);
        if (doc is not null)
        {
            var single = ReadFirstProperty(doc, "RuntimeIdentifier");
            var multi = ReadFirstProperty(doc, "RuntimeIdentifiers");
            var inferred = NormalizeStrings(
                string.IsNullOrWhiteSpace(single)
                    ? (string.IsNullOrWhiteSpace(multi)
                        ? Array.Empty<string>()
                        : multi!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    : new[] { single! });
            if (inferred.Length > 0)
                return inferred;
        }

        return new[] { "win-x64" };
    }

    private static DotNetPublishStyle[] ResolveStyles(DotNetPublishStyle[]? styles)
    {
        if (styles is null || styles.Length == 0)
            return new[] { DotNetPublishStyle.PortableCompat };

        return styles
            .Distinct()
            .ToArray();
    }

    private static string[] NormalizeStrings(IEnumerable<string>? values)
    {
        if (values is null) return Array.Empty<string>();

        var output = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var trimmed = value.Trim();
            if (trimmed.Length == 0) continue;
            if (output.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) continue;
            output.Add(trimmed);
        }

        return output.ToArray();
    }

    private static string? ReadFirstProperty(XDocument doc, string name)
    {
        return doc
            .Descendants()
            .Where(node => node.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(node => node.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static XDocument? TryLoadProject(string projectPath)
    {
        try
        {
            return XDocument.Load(projectPath);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolvePath(string projectRoot, string path)
    {
        var raw = (path ?? string.Empty).Trim().Trim('"');
        if (raw.Length == 0)
            throw new ArgumentException("Path is required.", nameof(path));

        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(projectRoot, raw));
    }

    private static void EnsurePathWithinRoot(string rootPath, string path, string label)
    {
        var root = NormalizeDirectoryPath(rootPath);
        var fullPath = Path.GetFullPath(path);

        if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return;

        throw new InvalidOperationException($"{label} must remain inside project root. Path: {fullPath}; Root: {rootPath}");
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            full += Path.DirectorySeparatorChar;
        return full;
    }

    private static string GetRelativePathCompat(string basePath, string fullPath)
    {
        var normalizedBase = NormalizeDirectoryPath(basePath);
        var normalizedFull = Path.GetFullPath(fullPath);

        var baseUri = new Uri(normalizedBase, UriKind.Absolute);
        var fullUri = new Uri(normalizedFull, UriKind.Absolute);
        var relativeUri = baseUri.MakeRelativeUri(fullUri);
        var relative = Uri.UnescapeDataString(relativeUri.ToString());
        if (relative.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return normalizedFull;

        return relative.Replace('/', Path.DirectorySeparatorChar);
    }

    private static JsonSerializerOptions CreateSerializeOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>
/// Options for dotnet publish config scaffolding.
/// </summary>
public sealed class DotNetPublishConfigScaffoldOptions
{
    /// <summary>
    /// Project root used to resolve relative paths.
    /// </summary>
    public string ProjectRoot { get; set; } = ".";

    /// <summary>
    /// Optional path to a specific project file. When omitted, first matching .csproj is used.
    /// </summary>
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Optional target name override. Defaults to csproj file name.
    /// </summary>
    public string? TargetName { get; set; }

    /// <summary>
    /// Optional framework override. When omitted, inferred from project file.
    /// </summary>
    public string? Framework { get; set; }

    /// <summary>
    /// Optional runtime override. When omitted, inferred from project file or defaults to win-x64.
    /// </summary>
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Optional style override. Default: PortableCompat.
    /// </summary>
    public DotNetPublishStyle[]? Styles { get; set; }

    /// <summary>
    /// Build configuration. Default: Release.
    /// </summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// Output config path. Default: powerforge.dotnetpublish.json.
    /// </summary>
    public string OutputPath { get; set; } = "powerforge.dotnetpublish.json";

    /// <summary>
    /// When true, existing config file is replaced.
    /// </summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// When true, emitted JSON includes <c>$schema</c> property.
    /// </summary>
    public bool IncludeSchema { get; set; } = true;
}

/// <summary>
/// Result returned after scaffolding a dotnet publish config file.
/// </summary>
public sealed class DotNetPublishConfigScaffoldResult
{
    /// <summary>
    /// Full path to the generated config file.
    /// </summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// Project path written into target configuration.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Generated target name.
    /// </summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>
    /// Generated framework value.
    /// </summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>
    /// Generated runtime identifiers.
    /// </summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Generated publish styles.
    /// </summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();
}
