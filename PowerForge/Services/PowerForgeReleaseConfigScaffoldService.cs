using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Scaffolds starter unified release configuration files for the PowerForge release engine.
/// </summary>
public sealed class PowerForgeReleaseConfigScaffoldService
{
    private static readonly JsonSerializerOptions DeserializeOptions = CreateDeserializeOptions();
    private static readonly JsonSerializerOptions SerializeOptions = CreateSerializeOptions();

    /// <summary>
    /// Resolves the final output path for a scaffold request.
    /// </summary>
    public string ResolveOutputPath(PowerForgeReleaseConfigScaffoldRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var projectRoot = ResolveProjectRoot(request);
        var outputPath = ResolvePath(
            projectRoot,
            string.IsNullOrWhiteSpace(request.OutputPath)
                ? Path.Combine("Build", "release.json")
                : request.OutputPath);

        EnsurePathWithinRoot(projectRoot, outputPath, "Release config output path");
        return outputPath;
    }

    /// <summary>
    /// Generates a starter unified release config file.
    /// </summary>
    public PowerForgeReleaseConfigScaffoldResult Generate(
        PowerForgeReleaseConfigScaffoldRequest request,
        ILogger? logger = null)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        logger ??= new NullLogger();
        var projectRoot = ResolveProjectRoot(request);
        var outputPath = ResolveOutputPath(request);

        if (File.Exists(outputPath) && !request.Force)
            throw new IOException($"Config already exists: {outputPath}. Use Force to overwrite it.");

        var packageConfigPath = request.SkipPackages
            ? null
            : ResolveOptionalConfigPath(
                projectRoot,
                request.PackagesConfigPath,
                "Package config",
                "Build/project.build.json",
                "project.build.json",
                Path.Combine(".powerforge", "project.build.json"));

        var dotNetPublishConfigPath = request.SkipTools
            ? null
            : ResolveOptionalConfigPath(
                projectRoot,
                request.DotNetPublishConfigPath,
                "DotNet publish config",
                "Build/powerforge.dotnetpublish.json",
                "powerforge.dotnetpublish.json",
                Path.Combine(".powerforge", "powerforge.dotnetpublish.json"));

        var moduleConfigPath = ResolveOptionalConfigPath(
            projectRoot,
            explicitPath: null,
            label: "Module pipeline config",
            "powerforge.json",
            Path.Combine("Build", "powerforge.json"),
            Path.Combine(".powerforge", "powerforge.json"));
        var moduleScriptPath = string.IsNullOrWhiteSpace(moduleConfigPath)
            ? ResolveOptionalConfigPath(
                projectRoot,
                explicitPath: null,
                label: "Module build script",
                Path.Combine("Module", "Build", "Build-Module.ps1"),
                Path.Combine("Build", "Build-Module.ps1"))
            : null;

        if (string.IsNullOrWhiteSpace(packageConfigPath) &&
            string.IsNullOrWhiteSpace(dotNetPublishConfigPath) &&
            string.IsNullOrWhiteSpace(moduleConfigPath) &&
            string.IsNullOrWhiteSpace(moduleScriptPath))
            throw new InvalidOperationException(
                "Could not find package, module, or DotNet publish inputs to scaffold from. Provide -PackagesConfigPath and/or -DotNetPublishConfigPath, or create powerforge.json, Module/Build/Build-Module.ps1, Build/project.build.json, or Build/powerforge.dotnetpublish.json first.");

        var packages = string.IsNullOrWhiteSpace(packageConfigPath)
            ? null
            : LoadProjectBuildConfig(packageConfigPath!);
        var modulePipeline = string.IsNullOrWhiteSpace(moduleConfigPath)
            ? null
            : LoadModulePipelineConfig(moduleConfigPath!);

        var spec = new PowerForgeReleaseSpec
        {
            Schema = request.IncludeSchema
                ? "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.release.schema.json"
                : null,
            SchemaVersion = 1,
            Module = BuildModuleSection(projectRoot, outputPath, moduleConfigPath, moduleScriptPath, modulePipeline),
            Packages = packages,
            Tools = BuildToolsSection(request, outputPath, packages, dotNetPublishConfigPath),
            Outputs = new PowerForgeReleaseOutputsOptions
            {
                Staging = new PowerForgeReleaseStagingOptions
                {
                    RootPath = "Artifacts/Release"
                }
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var json = JsonSerializer.Serialize(spec, SerializeOptions);
        File.WriteAllText(outputPath, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        logger.Verbose($"Generated unified release scaffold: {outputPath}");

        return new PowerForgeReleaseConfigScaffoldResult
        {
            ConfigPath = outputPath,
            IncludesPackages = packages is not null,
            IncludesTools = spec.Tools is not null,
            PackagesConfigPath = packageConfigPath,
            ModuleConfigPath = moduleConfigPath,
            ModuleScriptPath = moduleScriptPath,
            DotNetPublishConfigPath = dotNetPublishConfigPath,
            ToolGitHubOwner = spec.Tools?.GitHub.Owner,
            ToolGitHubRepository = spec.Tools?.GitHub.Repository
        };
    }

    private static string ResolveProjectRoot(PowerForgeReleaseConfigScaffoldRequest request)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Environment.CurrentDirectory
            : request.WorkingDirectory;
        var projectRoot = string.IsNullOrWhiteSpace(request.ProjectRoot)
            ? baseDirectory
            : request.ProjectRoot;

        var fullPath = Path.GetFullPath(Path.IsPathRooted(projectRoot)
            ? projectRoot
            : Path.Combine(baseDirectory, projectRoot));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Project root does not exist: {fullPath}");

        return fullPath;
    }

    private static string? ResolveOptionalConfigPath(
        string projectRoot,
        string? explicitPath,
        string label,
        params string[] defaultCandidates)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var resolved = ResolvePath(projectRoot, explicitPath!);
            if (!File.Exists(resolved))
                throw new FileNotFoundException($"{label} not found: {resolved}");

            return resolved;
        }

        foreach (var candidate in defaultCandidates)
        {
            var resolved = ResolvePath(projectRoot, candidate);
            if (File.Exists(resolved))
                return resolved;
        }

        return null;
    }

    private static string ResolvePath(string projectRoot, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(projectRoot, path));
    }

    private static void EnsurePathWithinRoot(string projectRoot, string path, string label)
    {
        var relative = GetRelativePathCompat(projectRoot, path);
        if (relative.StartsWith("..", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(relative))
            throw new InvalidOperationException($"{label} must stay within project root '{projectRoot}'.");
    }

    private static ProjectBuildConfiguration LoadProjectBuildConfig(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<ProjectBuildConfiguration>(json, DeserializeOptions);
        if (config is null)
            throw new InvalidOperationException($"Unable to deserialize project-build config: {path}");

        return config;
    }

    private static ModulePipelineSpec LoadModulePipelineConfig(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<ModulePipelineSpec>(json, DeserializeOptions);
        if (config?.Build is null)
            throw new InvalidOperationException($"Unable to deserialize module pipeline config: {path}");
        if (string.IsNullOrWhiteSpace(config.Build.Name))
            throw new InvalidOperationException($"Module pipeline config requires Build.Name: {path}");
        if (string.IsNullOrWhiteSpace(config.Build.SourcePath))
            throw new InvalidOperationException($"Module pipeline config requires Build.SourcePath: {path}");

        return config;
    }

    private static PowerForgeToolReleaseSpec? BuildToolsSection(
        PowerForgeReleaseConfigScaffoldRequest request,
        string outputPath,
        ProjectBuildConfiguration? packages,
        string? dotNetPublishConfigPath)
    {
        if (string.IsNullOrWhiteSpace(dotNetPublishConfigPath))
            return null;

        var outputDirectory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        var relativeDotNetPublishPath = GetRelativePathCompat(outputDirectory, dotNetPublishConfigPath!)
            .Replace('\\', '/');

        var toolGitHub = new PowerForgeToolReleaseGitHubOptions
        {
            Publish = false,
            Owner = NormalizeNullable(packages?.GitHubUsername),
            Repository = NormalizeNullable(packages?.GitHubRepositoryName),
            Token = NormalizeNullable(packages?.GitHubAccessToken),
            TokenFilePath = NormalizeNullable(packages?.GitHubAccessTokenFilePath),
            TokenEnvName = NormalizeNullable(packages?.GitHubAccessTokenEnvName),
            GenerateReleaseNotes = packages?.GitHubGenerateReleaseNotes ?? true,
            IsPreRelease = packages?.GitHubIsPreRelease ?? false,
            TagTemplate = "{Target}-v{Version}",
            ReleaseNameTemplate = "{Target} {Version}"
        };

        return new PowerForgeToolReleaseSpec
        {
            Configuration = string.IsNullOrWhiteSpace(request.Configuration) ? "Release" : request.Configuration.Trim(),
            DotNetPublishConfigPath = relativeDotNetPublishPath,
            GitHub = toolGitHub
        };
    }

    private static PowerForgeModuleReleaseOptions? BuildModuleSection(
        string projectRoot,
        string outputPath,
        string? moduleConfigPath,
        string? moduleScriptPath,
        ModulePipelineSpec? modulePipeline)
    {
        if (string.IsNullOrWhiteSpace(moduleConfigPath) && string.IsNullOrWhiteSpace(moduleScriptPath))
            return null;

        var outputDirectory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        var relativeRepositoryRoot = GetRelativePathCompat(outputDirectory, projectRoot)
            .Replace('\\', '/');
        var relativeConfigPath = string.IsNullOrWhiteSpace(moduleConfigPath)
            ? null
            : GetRelativePathCompat(projectRoot, moduleConfigPath!).Replace('\\', '/');
        var relativeScriptPath = string.IsNullOrWhiteSpace(moduleScriptPath)
            ? null
            : GetRelativePathCompat(projectRoot, moduleScriptPath!).Replace('\\', '/');
        var moduleRoot = modulePipeline is not null
            ? ResolvePath(Path.GetDirectoryName(moduleConfigPath!) ?? projectRoot, modulePipeline.Build.SourcePath)
            : Directory.GetParent(Path.GetDirectoryName(moduleScriptPath!) ?? projectRoot)?.FullName ?? projectRoot;
        var artifactPaths = modulePipeline is not null
            ? BuildModuleArtifactPaths(projectRoot, moduleRoot, modulePipeline)
            : new[]
            {
                Path.Combine(moduleRoot, "Artefacts", "Packed"),
                Path.Combine(moduleRoot, "Artefacts", "PackedWithModules"),
                Path.Combine(moduleRoot, "Artefacts", "Unpacked")
            }
            .Select(path => GetRelativePathCompat(projectRoot, path).Replace('\\', '/'))
            .ToArray();
        var manifestPath = modulePipeline is null
            ? null
            : Path.Combine(moduleRoot, modulePipeline.Build.Name + ".psd1");

        return new PowerForgeModuleReleaseOptions
        {
            RepositoryRoot = string.IsNullOrWhiteSpace(relativeRepositoryRoot) ? "." : relativeRepositoryRoot,
            ModuleName = NormalizeNullable(modulePipeline?.Build.Name),
            ConfigPath = relativeConfigPath,
            ScriptPath = relativeScriptPath,
            ManifestPath = manifestPath is not null && File.Exists(manifestPath)
                ? GetRelativePathCompat(projectRoot, manifestPath).Replace('\\', '/')
                : null,
            ModuleVersion = NormalizeNullable(modulePipeline?.Build.Version),
            ArtifactPaths = artifactPaths
        };
    }

    private static string[] BuildModuleArtifactPaths(
        string projectRoot,
        string moduleRoot,
        ModulePipelineSpec modulePipeline)
        => (modulePipeline.Segments ?? Array.Empty<IConfigurationSegment>())
            .OfType<ConfigurationArtefactSegment>()
            .Where(segment => segment.Configuration?.Enabled == true)
            .Select(segment =>
            {
                var configuredPath = segment.Configuration?.Path;
                return string.IsNullOrWhiteSpace(configuredPath)
                    ? Path.Combine(moduleRoot, "Artefacts", segment.ArtefactType.ToString())
                    : PathTokenProtection.IsPathRooted(configuredPath!)
                        ? configuredPath!
                        : PathTokenProtection.Combine(moduleRoot, configuredPath!);
            })
            .Select(path => GetRelativePathCompat(projectRoot, path).Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static string GetRelativePathCompat(string relativeTo, string path)
    {
        return PathTokenProtection.GetRelativePath(relativeTo, path);
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
        options.Converters.Add(new ConfigurationSegmentJsonConverter());
        return options;
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
