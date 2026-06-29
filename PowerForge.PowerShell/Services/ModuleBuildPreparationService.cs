using System.Collections;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class ModuleBuildPreparationService
{
    private static readonly HashSet<string> PackageBuildOptionPathNames = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(PackageBuildConfiguration.RootPath),
        nameof(PackageBuildConfiguration.OutputPath),
        nameof(PackageBuildConfiguration.ReleaseZipOutputPath),
        nameof(PackageBuildConfiguration.StagingPath),
        nameof(PackageBuildConfiguration.PlanOutputPath),
        nameof(PackageBuildConfiguration.PublishApiKeyFilePath),
        nameof(PackageBuildConfiguration.NugetCredentialSecretFilePath),
        nameof(PackageBuildConfiguration.GitHubAccessTokenFilePath)
    };

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
                : CollectSettingsFromWorkspace(request.Settings, UsesModuleFolderLayout(workspaceRoot, projectRoot) ? projectRoot : workspaceRoot))
            : Array.Empty<IConfigurationSegment>();
        ResolveWorkspaceRelativeSegmentPaths(segments, workspaceRoot, projectRoot);

        var frameworks = useLegacy && !request.DotNetFrameworkWasBound
            ? Array.Empty<string>()
            : request.DotNetFramework;
        var binaryConflictSearchRoots = ResolveWorkspacePaths(workspaceRoot, request.DiagnosticsBinaryConflictSearchRoot);
        var installRoots = request.InstallRootsWasBound
            ? ResolveWorkspacePaths(workspaceRoot, request.InstallRoots)
            : null;

        var spec = new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName!,
                SourcePath = projectRoot,
                StagingPath = ResolveWorkspacePath(workspaceRoot, request.StagingPath),
                CsprojPath = ResolveWorkspacePath(workspaceRoot, request.CsprojPath),
                Version = "1.0.0",
                Configuration = request.DotNetConfiguration,
                Frameworks = frameworks,
                KeepStaging = request.KeepStaging,
                ExcludeDirectories = request.ExcludeDirectories ?? Array.Empty<string>(),
                ExcludeFiles = BuildStageExcludeFiles(request.ExcludeFiles, moduleName!),
                BinaryConflictSearchRoots = binaryConflictSearchRoots,
            },
            Install = new ModulePipelineInstallOptions
            {
                Enabled = !request.SkipInstall,
                Strategy = request.InstallStrategyWasBound ? request.InstallStrategy : null,
                KeepVersions = request.KeepVersionsWasBound ? request.KeepVersions : null,
                Roots = installRoots,
                LegacyFlatHandling = request.LegacyFlatHandlingWasBound ? request.LegacyFlatHandling : null,
                PreserveVersions = request.PreserveInstallVersionsWasBound ? request.PreserveInstallVersions : null,
            },
            Diagnostics = new ModulePipelineDiagnosticsOptions
            {
                BaselinePath = ResolveWorkspaceQualifiedPath(workspaceRoot, projectRoot, request.DiagnosticsBaselinePath),
                GenerateBaseline = request.GenerateDiagnosticsBaseline,
                UpdateBaseline = request.UpdateDiagnosticsBaseline,
                FailOnNewDiagnostics = request.FailOnNewDiagnostics,
                FailOnSeverity = request.FailOnDiagnosticsSeverity,
                BinaryConflictSearchRoots = binaryConflictSearchRoots
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
            JsonOutputPath = request.JsonOnly ? ResolveJsonOutputPath(request, projectRoot, workspaceRoot) : null,
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
            JsonOutputPath = request.JsonOnly ? ResolveJsonOutputPath(request, spec.Build.SourcePath, request.CurrentPath) : null,
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
        spec.Build.BinaryConflictSearchRoots = ResolveConfigPaths(projectRoot, spec.Build.BinaryConflictSearchRoots);
        if (spec.Install?.Roots is not null)
            spec.Install.Roots = ResolveConfigPaths(projectRoot, spec.Install.Roots);
        if (spec.Diagnostics is not null && !string.IsNullOrWhiteSpace(spec.Diagnostics.BaselinePath))
            spec.Diagnostics.BaselinePath = ResolveConfigPath(baseDir, spec.Diagnostics.BaselinePath!);
        if (spec.Diagnostics is not null)
            spec.Diagnostics.BinaryConflictSearchRoots = ResolveConfigPaths(projectRoot, spec.Diagnostics.BinaryConflictSearchRoots);

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
            if (cfg is null) continue;
            if (!string.IsNullOrWhiteSpace(cfg.ConfigPath))
                cfg.ConfigPath = ResolveConfigPath(projectRoot, cfg.ConfigPath);
            ResolvePackageBuildOptionPaths(cfg.Options, projectRoot);
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
            ResolvePackageBuildOptionPaths(cfg.Options, projectRoot);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationDocumentationSegment>() ?? Enumerable.Empty<ConfigurationDocumentationSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.Path = ResolveConfigPathNullable(projectRoot, cfg.Path) ?? string.Empty;
            cfg.PathReadme = ResolveConfigPathNullable(projectRoot, cfg.PathReadme) ?? string.Empty;
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationBuildDocumentationSegment>() ?? Enumerable.Empty<ConfigurationBuildDocumentationSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.AboutTopicsSourcePath = ResolveConfigPaths(projectRoot, cfg.AboutTopicsSourcePath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationTestSegment>() ?? Enumerable.Empty<ConfigurationTestSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.TestsPath)) continue;
            cfg.TestsPath = ResolveConfigPath(projectRoot, cfg.TestsPath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationValidationSegment>() ?? Enumerable.Empty<ConfigurationValidationSegment>())
        {
            var settings = segment.Settings;
            if (settings is null) continue;
            settings.Tests.TestPath = ResolveConfigPathNullable(projectRoot, settings.Tests.TestPath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationOptionsSegment>() ?? Enumerable.Empty<ConfigurationOptionsSegment>())
        {
            var signing = segment.Options?.Signing;
            if (signing is null) continue;
            signing.CertificatePFXPath = ResolveConfigPathNullable(projectRoot, signing.CertificatePFXPath);
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
            return (fullProjectPath, basePath, basePath);
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

        var preserveProjectRelativeSegmentPaths = UsesModuleFolderLayout(workspaceRoot, projectRoot);
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case ConfigurationBuildLibrariesSegment buildLibraries:
                    var libraries = buildLibraries.BuildLibraries;
                    libraries.NETProjectPath = ResolveSegmentPath(workspaceRoot, projectRoot, libraries.NETProjectPath, preserveProjectRelativeSegmentPaths);
                    libraries.DevelopmentBinariesPath = ResolveSegmentPath(workspaceRoot, projectRoot, libraries.DevelopmentBinariesPath, preserveProjectRelativeSegmentPaths);
                    libraries.NETDevelopmentBinariesPath = ResolveSegmentPath(workspaceRoot, projectRoot, libraries.NETDevelopmentBinariesPath, preserveProjectRelativeSegmentPaths);
                    break;
                case ConfigurationProjectBuildSegment projectBuild:
                    projectBuild.Configuration.ConfigPath = ResolveSegmentPath(workspaceRoot, projectRoot, projectBuild.Configuration.ConfigPath, preserveProjectRelativeSegmentPaths) ?? string.Empty;
                    ResolvePackageBuildOptionPaths(projectBuild.Configuration.Options, workspaceRoot, projectRoot, preserveProjectRelativeSegmentPaths);
                    break;
                case ConfigurationPackageBuildSegment packageBuild:
                    var package = packageBuild.Configuration;
                    package.RootPath = ResolveSegmentPath(workspaceRoot, projectRoot, package.RootPath, preserveProjectRelativeSegmentPaths);
                    package.OutputPath = ResolveSegmentPath(workspaceRoot, projectRoot, package.OutputPath, preserveProjectRelativeSegmentPaths);
                    package.ReleaseZipOutputPath = ResolveSegmentPath(workspaceRoot, projectRoot, package.ReleaseZipOutputPath, preserveProjectRelativeSegmentPaths);
                    package.StagingPath = ResolveSegmentPath(workspaceRoot, projectRoot, package.StagingPath, preserveProjectRelativeSegmentPaths);
                    package.PlanOutputPath = ResolveSegmentPath(workspaceRoot, projectRoot, package.PlanOutputPath, preserveProjectRelativeSegmentPaths);
                    package.PublishApiKeyFilePath = ResolveSegmentPath(workspaceRoot, projectRoot, package.PublishApiKeyFilePath, preserveProjectRelativeSegmentPaths);
                    package.NugetCredentialSecretFilePath = ResolveSegmentPath(workspaceRoot, projectRoot, package.NugetCredentialSecretFilePath, preserveProjectRelativeSegmentPaths);
                    package.GitHubAccessTokenFilePath = ResolveSegmentPath(workspaceRoot, projectRoot, package.GitHubAccessTokenFilePath, preserveProjectRelativeSegmentPaths);
                    ResolvePackageBuildOptionPaths(package.Options, workspaceRoot, projectRoot, preserveProjectRelativeSegmentPaths);
                    break;
                case ConfigurationDocumentationSegment documentation:
                    documentation.Configuration.Path = ResolveWorkspaceQualifiedPath(workspaceRoot, projectRoot, documentation.Configuration.Path) ?? string.Empty;
                    documentation.Configuration.PathReadme = ResolveWorkspaceQualifiedPath(workspaceRoot, projectRoot, documentation.Configuration.PathReadme) ?? string.Empty;
                    break;
                case ConfigurationBuildDocumentationSegment buildDocumentation:
                    break;
                case ConfigurationTestSegment test:
                    test.Configuration.TestsPath = ResolveWorkspaceQualifiedPath(workspaceRoot, projectRoot, test.Configuration.TestsPath) ?? string.Empty;
                    break;
                case ConfigurationValidationSegment validation:
                    validation.Settings.Tests.TestPath = ResolveWorkspaceQualifiedPath(workspaceRoot, projectRoot, validation.Settings.Tests.TestPath);
                    break;
                case ConfigurationOptionsSegment options:
                    var signing = options.Options?.Signing;
                    if (signing is not null)
                        signing.CertificatePFXPath = ResolveSegmentPath(workspaceRoot, projectRoot, signing.CertificatePFXPath, preserveProjectRelativeSegmentPaths);
                    break;
                case ConfigurationReleaseSegment release:
                    release.Configuration.StageRoot = ResolveSegmentPath(workspaceRoot, projectRoot, release.Configuration.StageRoot, preserveProjectRelativeSegmentPaths);
                    break;
                case ConfigurationPublishSegment publish:
                    publish.Configuration.ApiKeyFilePath = ResolveSegmentPath(workspaceRoot, projectRoot, publish.Configuration.ApiKeyFilePath, preserveProjectRelativeSegmentPaths);
                    break;
                case ConfigurationActionSegment action:
                    action.Configuration.FilePath = ResolveSegmentPath(workspaceRoot, projectRoot, action.Configuration.FilePath, preserveProjectRelativeSegmentPaths);
                    action.Configuration.WorkingDirectory = ResolveSegmentPath(workspaceRoot, projectRoot, action.Configuration.WorkingDirectory, preserveProjectRelativeSegmentPaths);
                    break;
                case ConfigurationAppleAppSegment appleApp:
                    appleApp.Configuration.ProjectPath = ResolveWorkspaceQualifiedPath(workspaceRoot, projectRoot, appleApp.Configuration.ProjectPath) ?? string.Empty;
                    break;
                case ConfigurationXcodeProjectVersionSegment xcodeProject:
                    xcodeProject.Configuration.Path = ResolveWorkspaceQualifiedPath(workspaceRoot, projectRoot, xcodeProject.Configuration.Path) ?? string.Empty;
                    break;
                case ConfigurationArtefactSegment artefact:
                    ResolveWorkspaceRelativeArtefactPaths(artefact.Configuration, workspaceRoot, projectRoot);
                    break;
            }
        }
    }

    private static string? ResolveSegmentPath(string workspaceRoot, string projectRoot, string? path, bool preserveProjectRelativePath)
        => preserveProjectRelativePath
            ? ResolveWorkspaceQualifiedPath(workspaceRoot, projectRoot, path)
            : ResolveWorkspacePath(workspaceRoot, path);

    private static bool UsesModuleFolderLayout(string workspaceRoot, string projectRoot)
    {
        if (SamePath(workspaceRoot, projectRoot))
            return false;

        var projectDirectory = new DirectoryInfo(Path.GetFullPath(projectRoot));
        return string.Equals(projectDirectory.Name, "Module", StringComparison.OrdinalIgnoreCase) &&
               projectDirectory.Parent is not null &&
               SamePath(workspaceRoot, projectDirectory.Parent.FullName);
    }

    private static void ResolveWorkspaceRelativeArtefactPaths(ArtefactConfiguration configuration, string workspaceRoot, string projectRoot)
    {
        configuration.Path = ResolveWorkspaceQualifiedPath(workspaceRoot, projectRoot, configuration.Path);
        var layoutRoot = Path.IsPathRooted(configuration.Path ?? string.Empty)
            ? workspaceRoot
            : projectRoot;
        configuration.RequiredModules.Path = ResolveArtefactLayoutPath(layoutRoot, configuration.Path, configuration.RequiredModules.Path);
        configuration.RequiredModules.ModulesPath = ResolveArtefactLayoutPath(layoutRoot, configuration.Path, configuration.RequiredModules.ModulesPath);
        ResolveCopyMappingSources(configuration.DirectoryOutput, workspaceRoot, projectRoot);
        ResolveCopyMappingSources(configuration.FilesOutput, workspaceRoot, projectRoot);
    }

    private static string? ResolveWorkspacePath(string workspaceRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return path;

        if (ContainsPathToken(path!))
            return Path.Combine(workspaceRoot, PathValueResolver.Clean(path!));

        return PathValueResolver.Resolve(workspaceRoot, path!);
    }

    private static string[] ResolveWorkspacePaths(string workspaceRoot, string[]? paths)
    {
        if (paths is null || paths.Length == 0)
            return Array.Empty<string>();

        return paths
            .Select(path => ResolveWorkspacePath(workspaceRoot, path) ?? string.Empty)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private static string? ResolveWorkspaceQualifiedPath(string workspaceRoot, string projectRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || SamePath(workspaceRoot, projectRoot))
            return path;

        var moduleDirectoryName = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;
        if (!StartsWithPathSegment(path!, moduleDirectoryName))
            return path;

        return ResolveWorkspacePath(workspaceRoot, path);
    }

    private static bool ContainsPathToken(string path)
        => path.Contains("<TagName>", StringComparison.OrdinalIgnoreCase) ||
           path.Contains("{TagName}", StringComparison.OrdinalIgnoreCase) ||
           path.Contains("<ModuleVersion>", StringComparison.OrdinalIgnoreCase) ||
           path.Contains("{ModuleVersion}", StringComparison.OrdinalIgnoreCase) ||
           path.Contains("<ModuleVersionWithPreRelease>", StringComparison.OrdinalIgnoreCase) ||
           path.Contains("{ModuleVersionWithPreRelease}", StringComparison.OrdinalIgnoreCase) ||
           path.Contains("<TagModuleVersionWithPreRelease>", StringComparison.OrdinalIgnoreCase) ||
           path.Contains("{TagModuleVersionWithPreRelease}", StringComparison.OrdinalIgnoreCase) ||
           path.Contains("<ModuleName>", StringComparison.OrdinalIgnoreCase) ||
           path.Contains("{ModuleName}", StringComparison.OrdinalIgnoreCase);

    private static string[] ResolveConfigPaths(string rootPath, string[]? paths)
    {
        if (paths is null || paths.Length == 0)
            return Array.Empty<string>();

        return paths
            .Select(path => ResolveConfigPathNullable(rootPath, path) ?? string.Empty)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private static IConfigurationSegment[] CollectSettingsFromWorkspace(ScriptBlock? settings, string workspaceRoot)
    {
        if (settings is null)
            return Array.Empty<IConfigurationSegment>();

        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return LegacySegmentAdapter.CollectFromSettings(settings);

        var previousDirectory = Directory.GetCurrentDirectory();
        var runspace = Runspace.DefaultRunspace;
        var runspaceLocationChanged = false;
        string? previousRunspaceLocation = null;

        try
        {
            Directory.SetCurrentDirectory(workspaceRoot);
            if (runspace is not null)
            {
                try
                {
                    previousRunspaceLocation = runspace.SessionStateProxy.Path.CurrentFileSystemLocation?.ProviderPath;
                    runspace.SessionStateProxy.Path.SetLocation(workspaceRoot);
                    runspaceLocationChanged = true;
                }
                catch
                {
                    runspaceLocationChanged = false;
                }
            }

            return LegacySegmentAdapter.CollectFromSettings(settings);
        }
        finally
        {
            if (runspaceLocationChanged && !string.IsNullOrWhiteSpace(previousRunspaceLocation))
            {
                try { runspace!.SessionStateProxy.Path.SetLocation(previousRunspaceLocation!); } catch { }
            }

            try { Directory.SetCurrentDirectory(previousDirectory); } catch { }
        }
    }

    private static void ResolvePackageBuildOptionPaths(Dictionary<string, object?>? options, string rootPath)
    {
        if (options is null || options.Count == 0)
            return;

        foreach (var optionName in PackageBuildOptionPathNames)
        {
            if (!options.TryGetValue(optionName, out var value))
                continue;

            var path = GetStringOptionValue(value);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            options[optionName] = ResolveWorkspacePath(rootPath, path);
        }
    }

    private static void ResolvePackageBuildOptionPaths(
        Dictionary<string, object?>? options,
        string workspaceRoot,
        string projectRoot,
        bool preserveProjectRelativePath)
    {
        if (options is null || options.Count == 0)
            return;

        foreach (var optionName in PackageBuildOptionPathNames)
        {
            if (!options.TryGetValue(optionName, out var value))
                continue;

            var path = GetStringOptionValue(value);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            options[optionName] = ResolveSegmentPath(workspaceRoot, projectRoot, path, preserveProjectRelativePath);
        }
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

    private static void ResolveCopyMappingSources(ArtefactCopyMapping[]? mappings, string rootPath, string? projectRoot = null)
    {
        if (mappings is null)
            return;

        var requiredFirstSegment = string.IsNullOrWhiteSpace(projectRoot)
            ? null
            : Path.GetFileName(projectRoot!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;

        foreach (var mapping in mappings)
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Source) || Path.IsPathRooted(mapping.Source))
                continue;

            if (!string.IsNullOrWhiteSpace(requiredFirstSegment) && !StartsWithPathSegment(mapping.Source, requiredFirstSegment!))
                continue;

            mapping.Source = PathValueResolver.Resolve(rootPath, mapping.Source);
        }
    }

    private static bool StartsWithPathSegment(string path, string segment)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(segment))
            return false;

        var firstSegment = GetFirstPathSegment(path);
        return string.Equals(firstSegment, segment, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFirstPathSegment(string path)
    {
        var cleaned = PathValueResolver.Clean(path);
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return cleaned
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(static segment => !string.Equals(segment, ".", StringComparison.Ordinal)) ?? string.Empty;
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

    private static string ResolveJsonOutputPath(ModuleBuildPreparationRequest request, string projectRoot, string jsonBasePath)
    {
        if (!string.IsNullOrWhiteSpace(request.JsonPath))
        {
            var jsonPath = request.JsonPath!;
            if (RequiresPowerShellPathResolution(jsonPath))
                return request.ResolvePath!(jsonPath);

            return ResolveConfigPath(jsonBasePath, jsonPath);
        }

        return Path.Combine(projectRoot, "powerforge.json");
    }

    private static bool RequiresPowerShellPathResolution(string path)
    {
        var cleaned = PathValueResolver.Clean(path);
        if (cleaned.StartsWith("~", StringComparison.Ordinal))
            return true;

        var colonIndex = cleaned.IndexOf(':');
        return colonIndex > 0 && cleaned.Substring(0, colonIndex).All(static c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');
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
        var workspaceRoot = ResolveJsonWorkspaceRoot(projectRoot);

        spec.Build.SourcePath = MakeRelativeForConfig(baseDir, spec.Build.SourcePath);
        spec.Build.StagingPath = MakeRelativeForConfigNullable(baseDir, spec.Build.StagingPath);
        spec.Build.CsprojPath = MakeRelativeForConfigNullable(baseDir, spec.Build.CsprojPath);
        spec.Build.DevelopmentBinariesPath = MakeRelativeForConfigNullable(baseDir, spec.Build.DevelopmentBinariesPath);
        spec.Build.BinaryConflictSearchRoots = MakePathsRelativeForProjectRoot(projectRoot, spec.Build.BinaryConflictSearchRoots, preserveExternalRooted: true, workspaceRoot);
        if (spec.Install?.Roots is not null)
            spec.Install.Roots = MakePathsRelativeForProjectRoot(projectRoot, spec.Install.Roots, preserveExternalRooted: true, workspaceRoot);
        if (spec.Diagnostics is not null && !string.IsNullOrWhiteSpace(spec.Diagnostics.BaselinePath))
            spec.Diagnostics.BaselinePath = MakeRelativeForConfig(baseDir, spec.Diagnostics.BaselinePath!);
        if (spec.Diagnostics is not null)
            spec.Diagnostics.BinaryConflictSearchRoots = MakePathsRelativeForProjectRoot(projectRoot, spec.Diagnostics.BinaryConflictSearchRoots, preserveExternalRooted: true, workspaceRoot);

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
            if (cfg is null) continue;
            if (!string.IsNullOrWhiteSpace(cfg.ConfigPath))
                cfg.ConfigPath = MakeRelativeForConfig(projectRoot, ResolveConfigPath(projectRoot, cfg.ConfigPath));
            MakePackageBuildOptionPathsRelative(cfg.Options, projectRoot, workspaceRoot);
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
            cfg.PublishApiKeyFilePath = MakeRelativeForProjectRoot(projectRoot, cfg.PublishApiKeyFilePath, preserveExternalRooted: true, workspaceRoot);
            cfg.NugetCredentialSecretFilePath = MakeRelativeForProjectRoot(projectRoot, cfg.NugetCredentialSecretFilePath, preserveExternalRooted: true, workspaceRoot);
            cfg.GitHubAccessTokenFilePath = MakeRelativeForProjectRoot(projectRoot, cfg.GitHubAccessTokenFilePath, preserveExternalRooted: true, workspaceRoot);
            MakePackageBuildOptionPathsRelative(cfg.Options, projectRoot, workspaceRoot);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationDocumentationSegment>() ?? Enumerable.Empty<ConfigurationDocumentationSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.Path = MakeRelativeForProjectRoot(projectRoot, cfg.Path, preserveExternalRooted: true, workspaceRoot) ?? string.Empty;
            cfg.PathReadme = MakeRelativeForProjectRoot(projectRoot, cfg.PathReadme, preserveExternalRooted: true, workspaceRoot) ?? string.Empty;
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationBuildDocumentationSegment>() ?? Enumerable.Empty<ConfigurationBuildDocumentationSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.AboutTopicsSourcePath = MakePathsRelativeForProjectRoot(projectRoot, cfg.AboutTopicsSourcePath, preserveExternalRooted: true, workspaceRoot);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationTestSegment>() ?? Enumerable.Empty<ConfigurationTestSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.TestsPath)) continue;
            cfg.TestsPath = MakeRelativeForConfig(projectRoot, ResolveConfigPath(projectRoot, cfg.TestsPath));
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationValidationSegment>() ?? Enumerable.Empty<ConfigurationValidationSegment>())
        {
            var settings = segment.Settings;
            if (settings is null) continue;
            settings.Tests.TestPath = MakeRelativeForProjectRoot(projectRoot, settings.Tests.TestPath);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationOptionsSegment>() ?? Enumerable.Empty<ConfigurationOptionsSegment>())
        {
            var signing = segment.Options?.Signing;
            if (signing is null) continue;
            signing.CertificatePFXPath = MakeRelativeForProjectRoot(projectRoot, signing.CertificatePFXPath, preserveExternalRooted: true, workspaceRoot);
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
            cfg.ApiKeyFilePath = MakeRelativeForProjectRoot(projectRoot, cfg.ApiKeyFilePath, preserveExternalRooted: true, workspaceRoot);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationActionSegment>() ?? Enumerable.Empty<ConfigurationActionSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.FilePath = MakeRelativeForProjectRoot(projectRoot, cfg.FilePath, preserveExternalRooted: true, workspaceRoot);
            cfg.WorkingDirectory = MakeRelativeForProjectRoot(projectRoot, cfg.WorkingDirectory, preserveExternalRooted: true, workspaceRoot);
        }

        foreach (var segment in spec.Segments?.OfType<ConfigurationArtefactSegment>() ?? Enumerable.Empty<ConfigurationArtefactSegment>())
        {
            var cfg = segment.Configuration;
            if (cfg is null) continue;
            cfg.Path = MakeRelativeForProjectRoot(projectRoot, cfg.Path);
            cfg.RequiredModules.Path = MakeArtefactLayoutPathForJson(projectRoot, cfg.Path, cfg.RequiredModules.Path);
            cfg.RequiredModules.ModulesPath = MakeArtefactLayoutPathForJson(projectRoot, cfg.Path, cfg.RequiredModules.ModulesPath);
            MakeCopyMappingSourcesRelative(cfg.DirectoryOutput, projectRoot, workspaceRoot);
            MakeCopyMappingSourcesRelative(cfg.FilesOutput, projectRoot, workspaceRoot);
        }
    }

    private static string ResolveJsonWorkspaceRoot(string projectRoot)
    {
        var projectDirectory = new DirectoryInfo(Path.GetFullPath(projectRoot));
        return string.Equals(projectDirectory.Name, "Module", StringComparison.OrdinalIgnoreCase) && projectDirectory.Parent is not null
            ? projectDirectory.Parent.FullName
            : projectRoot;
    }

    private static string? MakeArtefactLayoutPathForJson(string projectRoot, string? artefactPath, string? layoutPath)
    {
        if (string.IsNullOrWhiteSpace(layoutPath))
            return null;
        if (Path.IsPathRooted(layoutPath))
            return MakeRelativeForProjectRoot(projectRoot, layoutPath);
        if (string.IsNullOrWhiteSpace(artefactPath))
            return NormalizePathSeparators(layoutPath!);
        if (ContainsPathToken(layoutPath!) || ContainsPathToken(artefactPath!))
            return NormalizePathSeparators(layoutPath!);

        var artefactRoot = ResolveConfigPath(projectRoot, artefactPath);
        var candidate = ResolveConfigPath(projectRoot, layoutPath);
        return IsSameOrChildPath(artefactRoot, candidate)
            ? MakeRelativeForProjectRoot(projectRoot, candidate)
            : NormalizePathSeparators(layoutPath!);
    }

    private static void MakeCopyMappingSourcesRelative(ArtefactCopyMapping[]? mappings, string projectRoot, string workspaceRoot)
    {
        if (mappings is null)
            return;

        foreach (var mapping in mappings)
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Source))
                continue;

            mapping.Source = MakeRelativeForProjectRoot(projectRoot, mapping.Source, preserveExternalRooted: true, workspaceRoot) ?? string.Empty;
        }
    }

    private static string? MakeRelativeForProjectRoot(string projectRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return MakeRelativeForProjectRoot(projectRoot, path, preserveExternalRooted: false);
    }

    private static string? MakeRelativeForProjectRoot(string projectRoot, string? path, bool preserveExternalRooted)
        => MakeRelativeForProjectRoot(projectRoot, path, preserveExternalRooted, projectRoot);

    private static string? MakeRelativeForProjectRoot(string projectRoot, string? path, bool preserveExternalRooted, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (ContainsPathToken(path!))
            return MakeTokenizedPathRelativeForProjectRoot(projectRoot, path!);

        var resolved = ResolveConfigPath(projectRoot, path);
        if (preserveExternalRooted &&
            Path.IsPathRooted(PathValueResolver.Clean(path!)) &&
            !IsSameOrChildPath(projectRoot, resolved) &&
            !IsSameOrChildPath(workspaceRoot, resolved))
        {
            return NormalizePathSeparators(resolved);
        }

        return MakeRelativeForConfig(projectRoot, resolved);
    }

    private static void MakePackageBuildOptionPathsRelative(Dictionary<string, object?>? options, string projectRoot, string workspaceRoot)
    {
        if (options is null || options.Count == 0)
            return;

        foreach (var optionName in PackageBuildOptionPathNames)
        {
            if (!options.TryGetValue(optionName, out var value))
                continue;

            var path = GetStringOptionValue(value);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            options[optionName] = MakeRelativeForProjectRoot(projectRoot, path, preserveExternalRooted: IsPackageCredentialPathName(optionName), workspaceRoot);
        }
    }

    private static bool IsPackageCredentialPathName(string optionName)
        => string.Equals(optionName, nameof(PackageBuildConfiguration.PublishApiKeyFilePath), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(optionName, nameof(PackageBuildConfiguration.NugetCredentialSecretFilePath), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(optionName, nameof(PackageBuildConfiguration.GitHubAccessTokenFilePath), StringComparison.OrdinalIgnoreCase);

    private static string[] MakePathsRelativeForProjectRoot(string projectRoot, string[]? paths, bool preserveExternalRooted = false)
        => MakePathsRelativeForProjectRoot(projectRoot, paths, preserveExternalRooted, projectRoot);

    private static string[] MakePathsRelativeForProjectRoot(string projectRoot, string[]? paths, bool preserveExternalRooted, string workspaceRoot)
    {
        if (paths is null || paths.Length == 0)
            return Array.Empty<string>();

        return paths
            .Select(path => MakeRelativeForProjectRoot(projectRoot, path, preserveExternalRooted, workspaceRoot) ?? string.Empty)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private static string MakeTokenizedPathRelativeForProjectRoot(string projectRoot, string path)
    {
        var cleaned = PathValueResolver.Clean(path);
        if (!Path.IsPathRooted(cleaned))
            return NormalizePathSeparators(cleaned);

        var tokenIndex = IndexOfPathToken(cleaned);
        if (tokenIndex < 0)
            return NormalizePathSeparators(cleaned);

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var prefix = cleaned.Substring(0, tokenIndex).TrimEnd(separators);
        if (string.IsNullOrWhiteSpace(prefix))
            return NormalizePathSeparators(cleaned);

        var prefixFullPath = Path.GetFullPath(prefix);
        if (!IsSameOrChildPath(projectRoot, prefixFullPath))
            return NormalizePathSeparators(cleaned);

        var relativePrefix = MakeRelativeForConfig(projectRoot, prefixFullPath);
        var suffix = cleaned.Substring(tokenIndex).TrimStart(separators);
        return string.Equals(relativePrefix, ".", StringComparison.Ordinal)
            ? NormalizePathSeparators(suffix)
            : NormalizePathSeparators(Path.Combine(relativePrefix, suffix));
    }

    private static int IndexOfPathToken(string path)
    {
        var tokens = new[]
        {
            "<TagName>",
            "{TagName}",
            "<ModuleVersion>",
            "{ModuleVersion}",
            "<ModuleVersionWithPreRelease>",
            "{ModuleVersionWithPreRelease}",
            "<TagModuleVersionWithPreRelease>",
            "{TagModuleVersionWithPreRelease}",
            "<ModuleName>",
            "{ModuleName}"
        };

        var index = -1;
        foreach (var token in tokens)
        {
            var tokenIndex = path.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex >= 0 && (index < 0 || tokenIndex < index))
                index = tokenIndex;
        }

        return index;
    }

    private static string? GetStringOptionValue(object? value)
    {
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
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
