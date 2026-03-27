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

        var moduleScriptPath = ResolveOptionalConfigPath(
            projectRoot,
            explicitPath: null,
            label: "Module build script",
            Path.Combine("Module", "Build", "Build-Module.ps1"),
            Path.Combine("Build", "Build-Module.ps1"));

        if (string.IsNullOrWhiteSpace(packageConfigPath) && string.IsNullOrWhiteSpace(dotNetPublishConfigPath))
            throw new InvalidOperationException(
                "Could not find package or DotNet publish configs to scaffold from. Provide -PackagesConfigPath and/or -DotNetPublishConfigPath, or create Build/project.build.json / Build/powerforge.dotnetpublish.json first.");

        var packages = string.IsNullOrWhiteSpace(packageConfigPath)
            ? null
            : LoadProjectBuildConfig(packageConfigPath!);

        var spec = new PowerForgeReleaseSpec
        {
            Schema = request.IncludeSchema
                ? "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.release.schema.json"
                : null,
            SchemaVersion = 1,
            Module = BuildModuleSection(projectRoot, outputPath, moduleScriptPath),
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
        var relative = Path.GetRelativePath(projectRoot, path);
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

    private static PowerForgeToolReleaseSpec? BuildToolsSection(
        PowerForgeReleaseConfigScaffoldRequest request,
        string outputPath,
        ProjectBuildConfiguration? packages,
        string? dotNetPublishConfigPath)
    {
        if (string.IsNullOrWhiteSpace(dotNetPublishConfigPath))
            return null;

        var outputDirectory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        var relativeDotNetPublishPath = Path.GetRelativePath(outputDirectory, dotNetPublishConfigPath!)
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

    private static PowerForgeModuleReleaseOptions? BuildModuleSection(string projectRoot, string outputPath, string? moduleScriptPath)
    {
        if (string.IsNullOrWhiteSpace(moduleScriptPath))
            return null;

        var outputDirectory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        var relativeRepositoryRoot = Path.GetRelativePath(outputDirectory, projectRoot)
            .Replace('\\', '/');
        var relativeScriptPath = Path.GetRelativePath(projectRoot, moduleScriptPath!)
            .Replace('\\', '/');

        return new PowerForgeModuleReleaseOptions
        {
            RepositoryRoot = string.IsNullOrWhiteSpace(relativeRepositoryRoot) ? "." : relativeRepositoryRoot,
            ScriptPath = relativeScriptPath,
            ArtifactPaths = new[]
            {
                "Module/Artefacts/Packed",
                "Module/Artefacts/PackedWithModules",
                "Module/Artefacts/Unpacked"
            }
        };
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
