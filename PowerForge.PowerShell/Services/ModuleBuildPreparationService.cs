using System.Collections;
using System.IO;
using System.Management.Automation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class ModuleBuildPreparationService
{
    public ModuleBuildPreparedContext Prepare(ModuleBuildPreparationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.CurrentPath))
            throw new ArgumentException("Current path is required.", nameof(request));
        if (request.ResolvePath is null)
            throw new ArgumentException("ResolvePath is required.", nameof(request));

        if (string.Equals(request.ParameterSetName, "Config", StringComparison.Ordinal))
            return PrepareFromConfig(request);

        var moduleName = string.Equals(request.ParameterSetName, "Configuration", StringComparison.Ordinal)
            ? LegacySegmentAdapter.ResolveModuleNameFromLegacyConfiguration(request.Configuration)
            : request.ModuleName;
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new PSArgumentException("ModuleName is required.");

        var (projectRoot, basePathForScaffold, workspaceRoot) = ResolveProjectPaths(request, moduleName!);
        var useLegacy = request.Legacy ||
                        string.Equals(request.ParameterSetName, "Configuration", StringComparison.Ordinal) ||
                        request.Settings is not null;
        var segments = useLegacy
            ? (string.Equals(request.ParameterSetName, "Configuration", StringComparison.Ordinal)
                ? LegacySegmentAdapter.CollectFromLegacyConfiguration(request.Configuration)
                : LegacySegmentAdapter.CollectFromSettings(request.Settings))
            : Array.Empty<IConfigurationSegment>();
        ResolveWorkspaceRelativeSegmentPaths(segments, workspaceRoot, projectRoot);

        var frameworks = useLegacy && !request.DotNetFrameworkWasBound
            ? Array.Empty<string>()
            : request.DotNetFramework;

        var spec = new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName!,
                SourcePath = projectRoot,
                StagingPath = request.StagingPath,
                CsprojPath = request.CsprojPath,
                Version = "1.0.0",
                Configuration = request.DotNetConfiguration,
                Frameworks = frameworks,
                KeepStaging = request.KeepStaging,
                ExcludeDirectories = request.ExcludeDirectories ?? Array.Empty<string>(),
                ExcludeFiles = BuildStageExcludeFiles(request.ExcludeFiles, moduleName!),
                BinaryConflictSearchRoots = request.DiagnosticsBinaryConflictSearchRoot ?? Array.Empty<string>(),
            },
            Install = new ModulePipelineInstallOptions
            {
                Enabled = !request.SkipInstall,
                Strategy = request.InstallStrategyWasBound ? request.InstallStrategy : null,
                KeepVersions = request.KeepVersionsWasBound ? request.KeepVersions : null,
                Roots = request.InstallRootsWasBound ? (request.InstallRoots ?? Array.Empty<string>()) : null,
                LegacyFlatHandling = request.LegacyFlatHandlingWasBound ? request.LegacyFlatHandling : null,
                PreserveVersions = request.PreserveInstallVersionsWasBound ? request.PreserveInstallVersions : null,
            },
            Diagnostics = new ModulePipelineDiagnosticsOptions
            {
                BaselinePath = request.DiagnosticsBaselinePath,
                GenerateBaseline = request.GenerateDiagnosticsBaseline,
                UpdateBaseline = request.UpdateDiagnosticsBaseline,
                FailOnNewDiagnostics = request.FailOnNewDiagnostics,
                FailOnSeverity = request.FailOnDiagnosticsSeverity,
                BinaryConflictSearchRoots = request.DiagnosticsBinaryConflictSearchRoot ?? Array.Empty<string>()
            },
            Segments = segments
        };

        spec.Build.Version = ResolveBaseVersion(projectRoot, moduleName!, segments);

        return new ModuleBuildPreparedContext
        {
            ModuleName = moduleName!,
            ProjectRoot = projectRoot,
            BasePathForScaffold = basePathForScaffold,
            UseLegacy = useLegacy,
            PipelineSpec = spec,
            JsonOutputPath = request.JsonOnly ? ResolveJsonOutputPath(request, projectRoot) : null,
            ConfigLabel = useLegacy ? "dsl" : "cmdlet"
        };
    }

    private ModuleBuildPreparedContext PrepareFromConfig(ModuleBuildPreparationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConfigPath))
            throw new PSArgumentException("ConfigPath is required.");

        var configFullPath = request.ResolvePath!(request.ConfigPath!);
        if (!File.Exists(configFullPath))
            throw new FileNotFoundException($"Module build config file not found: {configFullPath}", configFullPath);

        var spec = ReadPipelineSpecJson(configFullPath);
        ResolvePipelineSpecPaths(spec, configFullPath);

        if (spec.Build is null)
            throw new InvalidOperationException("Module build config requires a Build section.");
        if (string.IsNullOrWhiteSpace(spec.Build.Name))
            throw new InvalidOperationException("Module build config requires Build.Name.");
        if (string.IsNullOrWhiteSpace(spec.Build.SourcePath))
            throw new InvalidOperationException("Module build config requires Build.SourcePath.");

        if (string.IsNullOrWhiteSpace(spec.Build.Version))
            spec.Build.Version = ResolveBaseVersion(spec.Build.SourcePath, spec.Build.Name);

        return new ModuleBuildPreparedContext
        {
            ModuleName = spec.Build.Name,
            ProjectRoot = spec.Build.SourcePath,
            BasePathForScaffold = null,
            UseLegacy = false,
            PipelineSpec = spec,
            JsonOutputPath = request.JsonOnly ? ResolveJsonOutputPath(request, spec.Build.SourcePath) : null,
            ConfigLabel = "json",
            ConfigFilePath = configFullPath
        };
    }

    public void WritePipelineSpecJson(ModulePipelineSpec spec, string jsonFullPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(jsonFullPath)) throw new ArgumentException("Json path is required.", nameof(jsonFullPath));

        PrepareSpecForJsonExport(spec, jsonFullPath);

        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        opts.Converters.Add(new ConfigurationSegmentJsonConverter());

        var outDir = Path.GetDirectoryName(jsonFullPath);
        if (!string.IsNullOrWhiteSpace(outDir))
            Directory.CreateDirectory(outDir);

        var json = JsonSerializer.Serialize(spec, opts) + Environment.NewLine;
        File.WriteAllText(jsonFullPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public ModulePipelineSpec ReadPipelineSpecJson(string jsonFullPath)
    {
        if (string.IsNullOrWhiteSpace(jsonFullPath)) throw new ArgumentException("Json path is required.", nameof(jsonFullPath));

        try
        {
            var json = File.ReadAllText(jsonFullPath);
            var spec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, CreateJsonOptions());
            return spec ?? throw new InvalidOperationException("Parsed config is null.");
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            throw new InvalidOperationException($"Failed to parse module build config '{jsonFullPath}'. {ex.Message}", ex);
        }
    }

    public void ResolvePipelineSpecPaths(ModulePipelineSpec spec, string configFullPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (spec.Build is null) return;

        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();

        spec.Build.SourcePath = ResolveConfigPath(baseDir, spec.Build.SourcePath);
        var projectRoot = string.IsNullOrWhiteSpace(spec.Build.SourcePath) ? baseDir : spec.Build.SourcePath;
        spec.Build.StagingPath = ResolveConfigPathNullable(baseDir, spec.Build.StagingPath);
        spec.Build.CsprojPath = ResolveConfigPathNullable(baseDir, spec.Build.CsprojPath);
        spec.Build.DevelopmentBinariesPath = ResolveConfigPathNullable(baseDir, spec.Build.DevelopmentBinariesPath);
        if (spec.Diagnostics is not null && !string.IsNullOrWhiteSpace(spec.Diagnostics.BaselinePath))
            spec.Diagnostics.BaselinePath = ResolveConfigPath(baseDir, spec.Diagnostics.BaselinePath!);

        foreach (var segment in spec.Segments?.OfType<ConfigurationAppleAppSegment>() ?? Enumerable.Empty<ConfigurationAppleAppSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.ProjectPath)) continue;
            cfg.ProjectPath = ResolveConfigPath(projectRoot, cfg.ProjectPath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationXcodeProjectVersionSegment>() ?? Enumerable.Empty<ConfigurationXcodeProjectVersionSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.Path)) continue;
            cfg.Path = ResolveConfigPath(projectRoot, cfg.Path);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationProjectBuildSegment>() ?? Enumerable.Empty<ConfigurationProjectBuildSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.ConfigPath)) continue;
            cfg.ConfigPath = ResolveConfigPath(projectRoot, cfg.ConfigPath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationBuildLibrariesSegment>() ?? Enumerable.Empty<ConfigurationBuildLibrariesSegment>())
        {
            var cfg = segment.BuildLibraries;
            cfg.NETProjectPath = ResolveConfigPathNullable(projectRoot, cfg.NETProjectPath);
            cfg.DevelopmentBinariesPath = ResolveConfigPathNullable(projectRoot, cfg.DevelopmentBinariesPath);
            cfg.NETDevelopmentBinariesPath = ResolveConfigPathNullable(projectRoot, cfg.NETDevelopmentBinariesPath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationPackageBuildSegment>() ?? Enumerable.Empty<ConfigurationPackageBuildSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.RootPath = ResolveConfigPathNullable(projectRoot, cfg.RootPath);
            cfg.OutputPath = ResolveConfigPathNullable(projectRoot, cfg.OutputPath);
            cfg.ReleaseZipOutputPath = ResolveConfigPathNullable(projectRoot, cfg.ReleaseZipOutputPath);
            cfg.StagingPath = ResolveConfigPathNullable(projectRoot, cfg.StagingPath);
            cfg.PlanOutputPath = ResolveConfigPathNullable(projectRoot, cfg.PlanOutputPath);
            cfg.PublishApiKeyFilePath = ResolveConfigPathNullable(projectRoot, cfg.PublishApiKeyFilePath);
            cfg.NugetCredentialSecretFilePath = ResolveConfigPathNullable(projectRoot, cfg.NugetCredentialSecretFilePath);
            cfg.GitHubAccessTokenFilePath = ResolveConfigPathNullable(projectRoot, cfg.GitHubAccessTokenFilePath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationReleaseSegment>() ?? Enumerable.Empty<ConfigurationReleaseSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.StageRoot)) continue;
            cfg.StageRoot = ResolveConfigPath(projectRoot, cfg.StageRoot);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationPublishSegment>() ?? Enumerable.Empty<ConfigurationPublishSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.ApiKeyFilePath = ResolveConfigPathNullable(projectRoot, cfg.ApiKeyFilePath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationActionSegment>() ?? Enumerable.Empty<ConfigurationActionSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.FilePath = ResolveConfigPathNullable(projectRoot, cfg.FilePath);
            cfg.WorkingDirectory = ResolveConfigPathNullable(projectRoot, cfg.WorkingDirectory);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationArtefactSegment>() ?? Enumerable.Empty<ConfigurationArtefactSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.Path = ResolveConfigPathNullable(projectRoot, cfg.Path);
            cfg.RequiredModules.Path = ResolveArtefactLayoutPath(projectRoot, cfg.Path, cfg.RequiredModules.Path);
            cfg.RequiredModules.ModulesPath = ResolveArtefactLayoutPath(projectRoot, cfg.Path, cfg.RequiredModules.ModulesPath);
            ResolveCopyMappingSources(cfg.DirectoryOutput, projectRoot);
            ResolveCopyMappingSources(cfg.FilesOutput, projectRoot);
        }
    }

    private static string ResolveBaseVersion(string projectRoot, string moduleName, IReadOnlyList<IConfigurationSegment>? segments)
    {
        var configuredVersion = ResolveConfiguredVersion(segments);
        if (!string.IsNullOrWhiteSpace(configuredVersion))
            return configuredVersion!;

        return ResolveBaseVersion(projectRoot, moduleName);
    }

    private static string? ResolveConfiguredVersion(IReadOnlyList<IConfigurationSegment>? segments)
    {
        if (segments is null || segments.Count == 0)
            return null;

        for (var index = segments.Count - 1; index >= 0; index--)
        {
            if (segments[index] is not ConfigurationManifestSegment manifest)
                continue;

            var moduleVersion = manifest.Configuration?.ModuleVersion;
            if (!string.IsNullOrWhiteSpace(moduleVersion))
            {
                var trimmed = (moduleVersion ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    return trimmed;
            }
        }

        return null;
    }

    private static string ResolveBaseVersion(string projectRoot, string moduleName)
    {
        var baseVersion = "1.0.0";
        var psd1 = Path.Combine(projectRoot, $"{moduleName}.psd1");
        if (File.Exists(psd1) &&
            ManifestEditor.TryGetTopLevelString(psd1, "ModuleVersion", out var version) &&
            !string.IsNullOrWhiteSpace(version))
        {
            baseVersion = version!;
        }

        return baseVersion;
    }

    private static (string ProjectRoot, string? BasePathForScaffold, string WorkspaceRoot) ResolveProjectPaths(ModuleBuildPreparationRequest request, string moduleName)
    {
        if (string.Equals(request.ParameterSetName, "Modern", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(request.InputPath))
        {
            var basePath = request.ResolvePath!(request.InputPath!);
            var fullProjectPath = Path.Combine(basePath, moduleName);
            return (fullProjectPath, basePath, fullProjectPath);
        }

        var rootToUse = !string.IsNullOrWhiteSpace(request.ScriptRoot)
            ? Path.GetFullPath(Path.Combine(request.ScriptRoot!, ".."))
            : request.CurrentPath;

        return (rootToUse, null, ResolveWorkspaceRoot(rootToUse, request.ScriptRoot));
    }

    private static string ResolveWorkspaceRoot(string projectRoot, string? scriptRoot)
    {
        if (string.IsNullOrWhiteSpace(scriptRoot))
            return projectRoot;

        var scriptDirectory = new DirectoryInfo(Path.GetFullPath(scriptRoot!));
        var moduleDirectory = new DirectoryInfo(Path.GetFullPath(projectRoot));
        if (!string.Equals(scriptDirectory.Name, "Build", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(moduleDirectory.Name, "Module", StringComparison.OrdinalIgnoreCase) ||
            moduleDirectory.Parent is null)
        {
            return projectRoot;
        }

        return moduleDirectory.Parent.FullName;
    }

    private static void ResolveWorkspaceRelativeSegmentPaths(
        IReadOnlyList<IConfigurationSegment> segments,
        string workspaceRoot,
        string projectRoot)
    {
        if (segments.Count == 0 || SamePath(workspaceRoot, projectRoot))
            return;

        foreach (var segment in segments)
        {
            switch (segment)
            {
                case ConfigurationBuildLibrariesSegment buildLibraries:
                    var libraries = buildLibraries.BuildLibraries;
                    libraries.NETProjectPath = ResolveWorkspacePath(workspaceRoot, libraries.NETProjectPath);
                    libraries.DevelopmentBinariesPath = ResolveWorkspacePath(workspaceRoot, libraries.DevelopmentBinariesPath);
                    libraries.NETDevelopmentBinariesPath = ResolveWorkspacePath(workspaceRoot, libraries.NETDevelopmentBinariesPath);
                    break;
                case ConfigurationProjectBuildSegment projectBuild:
                    projectBuild.Configuration.ConfigPath = ResolveWorkspacePath(workspaceRoot, projectBuild.Configuration.ConfigPath) ?? string.Empty;
                    break;
                case ConfigurationPackageBuildSegment packageBuild:
                    var package = packageBuild.Configuration;
                    package.RootPath = ResolveWorkspacePath(workspaceRoot, package.RootPath);
                    package.OutputPath = ResolveWorkspacePath(workspaceRoot, package.OutputPath);
                    package.ReleaseZipOutputPath = ResolveWorkspacePath(workspaceRoot, package.ReleaseZipOutputPath);
                    package.StagingPath = ResolveWorkspacePath(workspaceRoot, package.StagingPath);
                    package.PlanOutputPath = ResolveWorkspacePath(workspaceRoot, package.PlanOutputPath);
                    package.PublishApiKeyFilePath = ResolveWorkspacePath(workspaceRoot, package.PublishApiKeyFilePath);
                    package.NugetCredentialSecretFilePath = ResolveWorkspacePath(workspaceRoot, package.NugetCredentialSecretFilePath);
                    package.GitHubAccessTokenFilePath = ResolveWorkspacePath(workspaceRoot, package.GitHubAccessTokenFilePath);
                    break;
                case ConfigurationReleaseSegment release:
                    release.Configuration.StageRoot = ResolveWorkspacePath(workspaceRoot, release.Configuration.StageRoot);
                    break;
                case ConfigurationPublishSegment publish:
                    publish.Configuration.ApiKeyFilePath = ResolveWorkspacePath(workspaceRoot, publish.Configuration.ApiKeyFilePath);
                    break;
                case ConfigurationActionSegment action:
                    action.Configuration.FilePath = ResolveWorkspacePath(workspaceRoot, action.Configuration.FilePath);
                    action.Configuration.WorkingDirectory = ResolveWorkspacePath(workspaceRoot, action.Configuration.WorkingDirectory);
                    break;
                case ConfigurationAppleAppSegment appleApp:
                    appleApp.Configuration.ProjectPath = ResolveWorkspacePath(workspaceRoot, appleApp.Configuration.ProjectPath) ?? string.Empty;
                    break;
                case ConfigurationXcodeProjectVersionSegment xcodeProject:
                    xcodeProject.Configuration.Path = ResolveWorkspacePath(workspaceRoot, xcodeProject.Configuration.Path) ?? string.Empty;
                    break;
                case ConfigurationArtefactSegment artefact:
                    ResolveWorkspaceRelativeArtefactPaths(artefact.Configuration, workspaceRoot);
                    break;
            }
        }
    }

    private static void ResolveWorkspaceRelativeArtefactPaths(ArtefactConfiguration configuration, string workspaceRoot)
    {
        configuration.Path = ResolveWorkspacePath(workspaceRoot, configuration.Path);
        configuration.RequiredModules.Path = ResolveArtefactLayoutPath(workspaceRoot, configuration.Path, configuration.RequiredModules.Path);
        configuration.RequiredModules.ModulesPath = ResolveArtefactLayoutPath(workspaceRoot, configuration.Path, configuration.RequiredModules.ModulesPath);
        ResolveCopyMappingSources(configuration.DirectoryOutput, workspaceRoot);
        ResolveCopyMappingSources(configuration.FilesOutput, workspaceRoot);
    }

    private static string? ResolveWorkspacePath(string workspaceRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return path;

        return PathValueResolver.Resolve(workspaceRoot, path!);
    }

    private static string? ResolveArtefactLayoutPath(string rootPath, string? artefactPath, string? layoutPath)
    {
        if (string.IsNullOrWhiteSpace(layoutPath) || Path.IsPathRooted(layoutPath))
            return layoutPath;
        if (string.IsNullOrWhiteSpace(artefactPath))
            return layoutPath;

        var artefactRoot = PathValueResolver.Resolve(rootPath, artefactPath!);
        var candidate = PathValueResolver.Resolve(rootPath, layoutPath!);
        return IsSameOrChildPath(artefactRoot, candidate) ? candidate : layoutPath;
    }

    private static void ResolveCopyMappingSources(ArtefactCopyMapping[]? mappings, string rootPath)
    {
        if (mappings is null)
            return;

        foreach (var mapping in mappings)
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Source) || Path.IsPathRooted(mapping.Source))
                continue;

            mapping.Source = PathValueResolver.Resolve(rootPath, mapping.Source);
        }
    }

    private static bool SamePath(string left, string right)
    {
        var normalizedLeft = Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRight = Path.GetFullPath(right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] BuildStageExcludeFiles(string[]? excludeFiles, string moduleName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in (excludeFiles ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)))
            set.Add(entry.Trim());

        if (!string.IsNullOrWhiteSpace(moduleName))
            set.Add($"{moduleName}.Tests.ps1");

        return set.ToArray();
    }

    private static string ResolveJsonOutputPath(ModuleBuildPreparationRequest request, string projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(request.JsonPath))
            return request.ResolvePath!(request.JsonPath!);

        return Path.Combine(projectRoot, "powerforge.json");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        opts.Converters.Add(new ConfigurationSegmentJsonConverter());
        return opts;
    }

    private static string ResolveConfigPath(string baseDir, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return PathValueResolver.Resolve(baseDir, path!);
    }

    private static string? ResolveConfigPathNullable(string baseDir, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return ResolveConfigPath(baseDir, path);
    }

    private static void PrepareSpecForJsonExport(ModulePipelineSpec spec, string jsonFullPath)
    {
        if (spec.Build is null) throw new ArgumentException("Spec.Build is required.", nameof(spec));

        var baseDir = Path.GetDirectoryName(jsonFullPath);
        if (string.IsNullOrWhiteSpace(baseDir)) return;
        var projectRoot = ResolveConfigPath(baseDir, spec.Build.SourcePath);

        spec.Build.SourcePath = MakeRelativeForConfig(baseDir, spec.Build.SourcePath);
        spec.Build.StagingPath = MakeRelativeForConfigNullable(baseDir, spec.Build.StagingPath);
        spec.Build.CsprojPath = MakeRelativeForConfigNullable(baseDir, spec.Build.CsprojPath);
        spec.Build.DevelopmentBinariesPath = MakeRelativeForConfigNullable(baseDir, spec.Build.DevelopmentBinariesPath);
        if (spec.Diagnostics is not null && !string.IsNullOrWhiteSpace(spec.Diagnostics.BaselinePath))
            spec.Diagnostics.BaselinePath = MakeRelativeForConfig(baseDir, spec.Diagnostics.BaselinePath!);

        foreach (var segment in spec.Segments?.OfType<ConfigurationAppleAppSegment>() ?? Enumerable.Empty<ConfigurationAppleAppSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.ProjectPath)) continue;
            cfg.ProjectPath = MakeRelativeForConfig(projectRoot, ResolveConfigPath(projectRoot, cfg.ProjectPath));
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationXcodeProjectVersionSegment>() ?? Enumerable.Empty<ConfigurationXcodeProjectVersionSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.Path)) continue;
            cfg.Path = MakeRelativeForConfig(projectRoot, ResolveConfigPath(projectRoot, cfg.Path));
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationProjectBuildSegment>() ?? Enumerable.Empty<ConfigurationProjectBuildSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.ConfigPath)) continue;
            cfg.ConfigPath = MakeRelativeForConfig(projectRoot, ResolveConfigPath(projectRoot, cfg.ConfigPath));
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationBuildLibrariesSegment>() ?? Enumerable.Empty<ConfigurationBuildLibrariesSegment>())
        {
            var cfg = segment.BuildLibraries;
            cfg.NETProjectPath = MakeRelativeForProjectRoot(projectRoot, cfg.NETProjectPath);
            cfg.DevelopmentBinariesPath = MakeRelativeForProjectRoot(projectRoot, cfg.DevelopmentBinariesPath);
            cfg.NETDevelopmentBinariesPath = MakeRelativeForProjectRoot(projectRoot, cfg.NETDevelopmentBinariesPath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationPackageBuildSegment>() ?? Enumerable.Empty<ConfigurationPackageBuildSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.RootPath = MakeRelativeForProjectRoot(projectRoot, cfg.RootPath);
            cfg.OutputPath = MakeRelativeForProjectRoot(projectRoot, cfg.OutputPath);
            cfg.ReleaseZipOutputPath = MakeRelativeForProjectRoot(projectRoot, cfg.ReleaseZipOutputPath);
            cfg.StagingPath = MakeRelativeForProjectRoot(projectRoot, cfg.StagingPath);
            cfg.PlanOutputPath = MakeRelativeForProjectRoot(projectRoot, cfg.PlanOutputPath);
            cfg.PublishApiKeyFilePath = MakeRelativeForProjectRoot(projectRoot, cfg.PublishApiKeyFilePath);
            cfg.NugetCredentialSecretFilePath = MakeRelativeForProjectRoot(projectRoot, cfg.NugetCredentialSecretFilePath);
            cfg.GitHubAccessTokenFilePath = MakeRelativeForProjectRoot(projectRoot, cfg.GitHubAccessTokenFilePath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationReleaseSegment>() ?? Enumerable.Empty<ConfigurationReleaseSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.StageRoot)) continue;
            cfg.StageRoot = MakeRelativeForConfig(projectRoot, ResolveConfigPath(projectRoot, cfg.StageRoot));
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationPublishSegment>() ?? Enumerable.Empty<ConfigurationPublishSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.ApiKeyFilePath = MakeRelativeForProjectRoot(projectRoot, cfg.ApiKeyFilePath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationActionSegment>() ?? Enumerable.Empty<ConfigurationActionSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.FilePath = MakeRelativeForProjectRoot(projectRoot, cfg.FilePath);
            cfg.WorkingDirectory = MakeRelativeForProjectRoot(projectRoot, cfg.WorkingDirectory);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationArtefactSegment>() ?? Enumerable.Empty<ConfigurationArtefactSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.Path = MakeRelativeForProjectRoot(projectRoot, cfg.Path);
            cfg.RequiredModules.Path = MakeArtefactLayoutPathForJson(projectRoot, cfg.Path, cfg.RequiredModules.Path);
            cfg.RequiredModules.ModulesPath = MakeArtefactLayoutPathForJson(projectRoot, cfg.Path, cfg.RequiredModules.ModulesPath);
            MakeCopyMappingSourcesRelative(cfg.DirectoryOutput, projectRoot);
            MakeCopyMappingSourcesRelative(cfg.FilesOutput, projectRoot);
        }
    }

    private static string? MakeArtefactLayoutPathForJson(string projectRoot, string? artefactPath, string? layoutPath)
    {
        if (string.IsNullOrWhiteSpace(layoutPath))
            return null;
        if (Path.IsPathRooted(layoutPath))
            return MakeRelativeForProjectRoot(projectRoot, layoutPath);
        if (string.IsNullOrWhiteSpace(artefactPath))
            return NormalizePathSeparators(layoutPath!);

        var artefactRoot = ResolveConfigPath(projectRoot, artefactPath);
        var candidate = ResolveConfigPath(projectRoot, layoutPath);
        return IsSameOrChildPath(artefactRoot, candidate)
            ? MakeRelativeForProjectRoot(projectRoot, candidate)
            : NormalizePathSeparators(layoutPath!);
    }

    private static void MakeCopyMappingSourcesRelative(ArtefactCopyMapping[]? mappings, string projectRoot)
    {
        if (mappings is null)
            return;

        foreach (var mapping in mappings)
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Source))
                continue;

            mapping.Source = MakeRelativeForProjectRoot(projectRoot, mapping.Source) ?? string.Empty;
        }
    }

    private static string? MakeRelativeForProjectRoot(string projectRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return MakeRelativeForConfig(projectRoot, ResolveConfigPath(projectRoot, path));
    }

    private static string MakeRelativeForConfig(string baseDir, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        try
        {
            var full = Path.GetFullPath(path);
            var rel = GetRelativePath(baseDir, full);
            return rel.Replace('\\', '/');
        }
        catch
        {
            return path.Replace('\\', '/');
        }
    }

    private static string? MakeRelativeForConfigNullable(string baseDir, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return MakeRelativeForConfig(baseDir, path!);
    }

    private static string NormalizePathSeparators(string path)
        => path.Replace('\\', '/');

    private static bool IsSameOrChildPath(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var candidateWithSeparator = candidate + Path.DirectorySeparatorChar;
        return candidateWithSeparator.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(string baseDir, string fullPath)
    {
#if NET472
        var baseFull = EnsureTrailingSeparator(Path.GetFullPath(baseDir));
        var baseUri = new Uri(baseFull);
        var pathUri = new Uri(Path.GetFullPath(fullPath));

        if (!string.Equals(baseUri.Scheme, pathUri.Scheme, StringComparison.OrdinalIgnoreCase))
            return fullPath;

        var relativeUri = baseUri.MakeRelativeUri(pathUri);
        var relative = Uri.UnescapeDataString(relativeUri.ToString());
        return relative.Replace('/', Path.DirectorySeparatorChar);
#else
        return Path.GetRelativePath(baseDir, fullPath);
#endif

#if NET472
        static string EnsureTrailingSeparator(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            if (input.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                input.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return input;
            return input + Path.DirectorySeparatorChar;
        }
#endif
    }
}
