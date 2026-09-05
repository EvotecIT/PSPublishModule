using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static Dictionary<string, string[]> NormalizeCommandDependencies(
        IReadOnlyDictionary<string, List<string>> commandDependencies)
        => commandDependencies.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value
                .Where(static command => !string.IsNullOrWhiteSpace(command))
                .Select(static command => command.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

    private static PlaceHolderReplacement[] NormalizePlaceHolders(IEnumerable<PlaceHolderReplacement> placeHolders)
        => placeHolders
            .Where(static replacement => replacement is not null &&
                (!string.IsNullOrWhiteSpace(replacement.Find) || !string.IsNullOrWhiteSpace(replacement.Replace)))
            .ToArray();

    private ModulePlanExecutionSurface FinalizePlanExecutionSurface(ModulePlanExecutionSurface surface)
    {
        if (surface.Delivery?.Sign == true)
        {
            surface.Signing = ApplyDeliverySigningPreference(surface.Signing, surface.Delivery);
            if (!surface.SignModule)
            {
                surface.SignModule = true;
                _logger.Info("Delivery signing requested; enabling signing so bundled internals are also signed.");
            }
        }

        if (surface.ModuleSkipForce || surface.ModuleSkipFailOnMissingCommands ||
            surface.IgnoredModules.Length > 0 || surface.IgnoredFunctions.Count > 0)
        {
            surface.ModuleSkip = new ModuleSkipConfiguration
            {
                Force = surface.ModuleSkipForce,
                FailOnMissingCommands = surface.ModuleSkipFailOnMissingCommands,
                IgnoreModuleName = surface.IgnoredModules,
                IgnoreFunctionName = surface.IgnoredFunctions
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        if (surface.Formatting?.Options is { UpdateProjectRoot: false } &&
            ModulePipelinePlanningHelpers.HasStandardFormattingConfiguration(surface.Formatting))
        {
            surface.Formatting.Options.UpdateProjectRoot = true;
            _logger.Info("UpdateProjectRoot not explicitly set; enabling because Default* formatting targets are configured (legacy compatibility).");
        }

        if (surface.RefreshManifestOnly)
        {
            if (surface.SignModule) _logger.Info("RefreshPSD1Only enabled: disabling signing for this run.");
            surface.SignModule = false;
            surface.InstallEnabled = false;
            surface.InstallMissingModules = false;
            surface.InstallMissingModulesForce = false;
            surface.InstallMissingModulesPrerelease = false;
            surface.Documentation = null;
            surface.DocumentationBuild = null;
            surface.CompatibilitySettings = null;
            surface.FileConsistencySettings = null;
            surface.ValidationSettings = null;
            surface.ImportModules = null;
            surface.TestsAfterMerge.Clear();
            surface.EnabledExternalAssets = Array.Empty<ConfigurationExternalAssetSegment>();
            surface.EnabledArtefacts = Array.Empty<ConfigurationArtefactSegment>();
            surface.EnabledPublishes = Array.Empty<ConfigurationPublishSegment>();
            surface.ProjectBuilds.Clear();
            surface.PackageBuilds.Clear();
            surface.Release = null;
        }

        if (surface.GateMode == ConfigurationGateMode.Documentation)
        {
            if (surface.SignModule) _logger.Info("Gate mode Documentation enabled: disabling signing for this run.");
            surface.SignModule = false;
            surface.InstallEnabled = false;
            surface.Formatting = null;
            surface.CompatibilitySettings = null;
            surface.FileConsistencySettings = null;
            surface.ValidationSettings = null;
            surface.ImportModules = null;
            surface.TestsAfterMerge.Clear();
            surface.EnabledArtefacts = Array.Empty<ConfigurationArtefactSegment>();
            surface.EnabledPublishes = Array.Empty<ConfigurationPublishSegment>();
            surface.Delivery = null;
            surface.ProjectBuilds.RemoveAll(static build => build?.Configuration?.BuildBeforeModule != true);
            surface.PackageBuilds.RemoveAll(static build => build?.Configuration?.BuildBeforeModule != true);
            surface.AppleApps.Clear();
            surface.XcodeProjectVersions.Clear();
            surface.Release = null;
            surface.Actions.RemoveAll(static action => action?.Configuration is null ||
                !action.Configuration.Enabled ||
                !IsDocumentationGateActionStage(action.Configuration.At));
        }
        return surface;
    }

    private sealed class ModulePlanExecutionSurface
    {
        internal DeliveryOptionsConfiguration? Delivery { get; set; }
        internal SigningOptionsConfiguration? Signing { get; set; }
        internal bool SignModule { get; set; }
        internal bool ModuleSkipForce { get; set; }
        internal bool ModuleSkipFailOnMissingCommands { get; set; }
        internal string[] IgnoredModules { get; set; } = Array.Empty<string>();
        internal List<string> IgnoredFunctions { get; set; } = new();
        internal ModuleSkipConfiguration? ModuleSkip { get; set; }
        internal ConfigurationFormattingSegment? Formatting { get; set; }
        internal bool RefreshManifestOnly { get; set; }
        internal ConfigurationGateMode? GateMode { get; set; }
        internal bool InstallEnabled { get; set; }
        internal bool InstallMissingModules { get; set; }
        internal bool InstallMissingModulesForce { get; set; }
        internal bool InstallMissingModulesPrerelease { get; set; }
        internal DocumentationConfiguration? Documentation { get; set; }
        internal BuildDocumentationConfiguration? DocumentationBuild { get; set; }
        internal CompatibilitySettings? CompatibilitySettings { get; set; }
        internal FileConsistencySettings? FileConsistencySettings { get; set; }
        internal ModuleValidationSettings? ValidationSettings { get; set; }
        internal ImportModulesConfiguration? ImportModules { get; set; }
        internal ConfigurationExternalAssetSegment[] EnabledExternalAssets { get; set; } = Array.Empty<ConfigurationExternalAssetSegment>();
        internal ConfigurationArtefactSegment[] EnabledArtefacts { get; set; } = Array.Empty<ConfigurationArtefactSegment>();
        internal ConfigurationPublishSegment[] EnabledPublishes { get; set; } = Array.Empty<ConfigurationPublishSegment>();
        internal ConfigurationReleaseSegment? Release { get; set; }
        internal List<TestConfiguration> TestsAfterMerge { get; set; } = new();
        internal List<ConfigurationProjectBuildSegment> ProjectBuilds { get; set; } = new();
        internal List<ConfigurationPackageBuildSegment> PackageBuilds { get; set; } = new();
        internal List<ConfigurationAppleAppSegment> AppleApps { get; set; } = new();
        internal List<ConfigurationXcodeProjectVersionSegment> XcodeProjectVersions { get; set; } = new();
        internal List<ConfigurationActionSegment> Actions { get; set; } = new();
    }

    private static void ApplyReleaseSourceProtection(
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        bool localVersioning,
        ReleaseProtectionConfiguration? releaseProtection,
        ConfigurationGateMode? gateMode)
    {
        plan.UseLocalVersioning = localVersioning;
        plan.GenerateReleaseProvenance = releaseProtection?.GenerateProvenance == true &&
                                         gateMode is not ConfigurationGateMode.Manifest and
                                             not ConfigurationGateMode.Documentation and
                                             not ConfigurationGateMode.Build;
        plan.RequireReleaseSourceUnchanged = plan.GenerateReleaseProvenance ||
                                             releaseProtection?.RequireSourceUnchanged == true;
        plan.RequireCleanReleaseSource = plan.RequireReleaseSourceUnchanged ||
                                         releaseProtection?.RequireCleanSource == true;
        var hasGitHubRelease = spec.UnifiedGitHubRelease ||
                               plan.Publishes.Any(static publish =>
                                   publish?.Configuration?.Destination == PublishDestination.GitHub);
        if (plan.GenerateReleaseProvenance &&
            (!plan.SignModule || plan.Artefacts.Length == 0 || !hasGitHubRelease))
        {
            throw new InvalidOperationException(
                "GenerateProvenance requires module signing, at least one artefact, and a GitHub release destination.");
        }
        if (!plan.RequireCleanReleaseSource) return;

        var generatedProvenancePaths = GetGeneratedReleaseProvenancePaths(plan.ProjectRoot);
        var lifecycleActionInputs = CollectReleaseActionInputPaths(plan.ProjectRoot, plan.Actions);
        var artefactMappingInputs = CollectReleaseArtefactInputPaths(
            plan.ProjectRoot,
            plan.ModuleName,
            plan.ResolvedVersion,
            plan.PreRelease,
            plan.Artefacts);
        var artefactMappingRoots = CollectReleaseArtefactInputRootPaths(
            plan.ProjectRoot,
            plan.ModuleName,
            plan.ResolvedVersion,
            plan.PreRelease,
            plan.Artefacts);
        plan.SourceRootPaths = new[] { plan.BuildSpec.SourcePath }
            .Concat(artefactMappingRoots)
            .Distinct(Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();
        plan.SourceInputPaths = CollectReleaseSourceInputPaths(
            plan.BuildSpec,
            (spec.SourceInputPaths ?? Array.Empty<string>())
                .Concat(lifecycleActionInputs)
                .Concat(artefactMappingInputs),
            generatedProvenancePaths);
        var provenance = DotNetPublishPipelineRunner.ReadSourceProvenance(
            plan.ProjectRoot,
            generatedPaths: generatedProvenancePaths,
            explicitInputPaths: plan.SourceInputPaths,
            buildProjectPaths: string.IsNullOrWhiteSpace(plan.BuildSpec.CsprojPath)
                ? Array.Empty<string>()
                : new[] { plan.BuildSpec.CsprojPath! },
            buildConfiguration: plan.BuildSpec.Configuration,
            sourceRootPaths: plan.SourceRootPaths);
        if (string.IsNullOrWhiteSpace(provenance.Revision) || provenance.Dirty is not false)
        {
            throw new InvalidOperationException(
                "Release source protection requires a resolved Git revision with clean release inputs before packaging." +
                FormatDirtySourcePaths(provenance));
        }

        plan.SourceRevision = DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(
            provenance.Revision,
            "module source revision");
        plan.SourceDirty = false;
        if (plan.GenerateReleaseProvenance)
            plan.SourceRepositoryUrl = ResolveGitHubModuleRepositoryUrl(plan);
    }

    private static string[] GetGeneratedReleaseProvenancePaths(string projectRoot) =>
        new[]
        {
            Path.Combine(projectRoot, PublishedRegistryProvenanceValidator.ModuleProvenanceFileName),
            Path.Combine(projectRoot, PowerForgeModuleSourceAttestationWriter.FileName)
        };

    private static string FormatDirtySourcePaths(DotNetPublishPipelineRunner.SourceProvenance provenance)
    {
        string paths = provenance.DirtyPaths.Length == 0
            ? string.Empty
            : " Blocking source input(s): " + string.Join(", ", provenance.DirtyPaths) + ".";
        string reasons = provenance.DirtyReasons.Length == 0
            ? string.Empty
            : " Blocking condition(s): " + string.Join("; ", provenance.DirtyReasons) + ".";
        return paths + reasons;
    }

    private static string[] CollectReleaseSourceInputPaths(
        ModuleBuildSpec build,
        IEnumerable<string>? configuredInputs,
        IEnumerable<string>? generatedPaths)
    {
        var comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var generated = new HashSet<string>(
            (generatedPaths ?? Array.Empty<string>()).Select(Path.GetFullPath),
            comparer);
        var inputs = new HashSet<string>(
            (configuredInputs ?? Array.Empty<string>())
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath),
            comparer);
        if (!string.IsNullOrWhiteSpace(build.CsprojPath))
            inputs.Add(Path.GetFullPath(build.CsprojPath!));

        var excludedDirectories = new HashSet<string>(
            (build.ExcludeDirectories ?? Array.Empty<string>())
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var excludedFiles = new HashSet<string>(
            (build.ExcludeFiles ?? Array.Empty<string>())
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(build.SourcePath));
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Signed GitHub module release source directory '{directory}' cannot be a reparse point.");
            }
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!excludedFiles.Contains(Path.GetFileName(file)))
                {
                    string fullPath = Path.GetFullPath(file);
                    if (!generated.Contains(fullPath))
                        inputs.Add(fullPath);
                }
            }
            foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!excludedDirectories.Contains(Path.GetFileName(child)))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Signed GitHub module release source directory '{child}' cannot be a reparse point.");
                    }
                    pending.Push(child);
                }
            }
        }
        return inputs.OrderBy(static path => path, comparer).ToArray();
    }

    private static string[] CollectReleaseActionInputPaths(
        string projectRoot,
        IEnumerable<ConfigurationActionSegment>? actions)
    {
        var comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return (actions ?? Array.Empty<ConfigurationActionSegment>())
            .Where(static action =>
                action?.Configuration is { Enabled: true } configuration &&
                !string.IsNullOrWhiteSpace(configuration.FilePath))
            .Select(action => ResolvePath(projectRoot, action.Configuration.FilePath!))
            .Distinct(comparer)
            .OrderBy(static path => path, comparer)
            .ToArray();
    }

    private static string[] CollectReleaseArtefactInputPaths(
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IEnumerable<ConfigurationArtefactSegment>? artefacts)
    {
        var comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var inputs = new HashSet<string>(comparer);
        foreach (ConfigurationArtefactSegment artefact in artefacts ?? Array.Empty<ConfigurationArtefactSegment>())
        {
            ArtefactConfiguration? configuration = artefact?.Configuration;
            if (configuration?.Enabled != true)
                continue;

            foreach (ArtefactCopyMapping mapping in configuration.FilesOutput ?? Array.Empty<ArtefactCopyMapping>())
            {
                if (mapping is null)
                    continue;
                inputs.Add(ResolveArtefactInputPath(mapping.Source, projectRoot, moduleName, moduleVersion, preRelease));
            }

            foreach (ArtefactCopyMapping mapping in configuration.DirectoryOutput ?? Array.Empty<ArtefactCopyMapping>())
            {
                if (mapping is null)
                    continue;
                string source = ResolveArtefactInputPath(mapping.Source, projectRoot, moduleName, moduleVersion, preRelease);
                if (!Directory.Exists(source))
                    throw new DirectoryNotFoundException($"Directory not found: {source}");

                var pending = new Stack<string>();
                pending.Push(source);
                while (pending.Count > 0)
                {
                    string directory = pending.Pop();
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Signed GitHub module release artefact source directory '{directory}' cannot be a reparse point.");
                    }
                    foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                        inputs.Add(Path.GetFullPath(file));
                    foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                        pending.Push(child);
                }
            }
        }

        return inputs.OrderBy(static path => path, comparer).ToArray();
    }

    private static string[] CollectReleaseArtefactInputRootPaths(
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IEnumerable<ConfigurationArtefactSegment>? artefacts)
    {
        var comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return (artefacts ?? Array.Empty<ConfigurationArtefactSegment>())
            .Where(static artefact => artefact?.Configuration?.Enabled == true)
            .SelectMany(static artefact => artefact.Configuration.DirectoryOutput ?? Array.Empty<ArtefactCopyMapping>())
            .Where(static mapping => mapping is not null)
            .Select(mapping => ResolveArtefactInputPath(mapping.Source, projectRoot, moduleName, moduleVersion, preRelease))
            .Distinct(comparer)
            .OrderBy(static path => path, comparer)
            .ToArray();
    }

    private static string ResolveArtefactInputPath(
        string value,
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        string raw = ModulePathTokenFormatter.ReplacePathTokens(
                value ?? string.Empty,
                moduleName,
                moduleVersion,
                preRelease)
            .Trim()
            .Trim('"');
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Copy mapping source path is empty.", nameof(value));
        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(projectRoot, raw));
    }

    private static void ValidateReleaseSourceUnchanged(
        ModulePipelinePlan plan,
        IEnumerable<string>? generatedOutputPaths,
        IEnumerable<string>? trackedGeneratedOutputPaths)
    {
        string[] generatedPaths = GetGeneratedReleaseProvenancePaths(plan.ProjectRoot)
            .Concat(generatedOutputPaths ?? Array.Empty<string>())
            .Distinct(Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();
        string[] currentInputs = CollectReleaseSourceInputPaths(
            plan.BuildSpec,
            plan.SourceInputPaths.Concat(CollectReleaseArtefactInputPaths(
                plan.ProjectRoot,
                plan.ModuleName,
                plan.ResolvedVersion,
                plan.PreRelease,
                plan.Artefacts)),
            generatedPaths);
        DotNetPublishPipelineRunner.SourceProvenance current =
            DotNetPublishPipelineRunner.ReadSourceProvenance(
                plan.ProjectRoot,
                generatedPaths: generatedPaths,
                explicitInputPaths: currentInputs,
                trackedGeneratedPaths: trackedGeneratedOutputPaths,
                buildProjectPaths: string.IsNullOrWhiteSpace(plan.BuildSpec.CsprojPath)
                    ? Array.Empty<string>()
                    : new[] { plan.BuildSpec.CsprojPath! },
                buildConfiguration: plan.BuildSpec.Configuration,
                sourceRootPaths: plan.SourceRootPaths);
        if (string.IsNullOrWhiteSpace(current.Revision) ||
            !string.Equals(current.Revision, plan.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
            current.Dirty is not false)
        {
            throw new InvalidOperationException(
                "Module release source changed after planning; packaging or publication is blocked before remote mutation.");
        }
    }

    private static void ValidatePackageReleaseSourceUnchanged(
        ModulePipelinePlan plan,
        DotNetRepositoryReleaseSpec spec)
    {
        string[] projectPaths = DotNetRepositoryReleaseService.ResolveSelectedProjectPaths(spec);
        string[] generatedPaths = GetGeneratedReleaseProvenancePaths(plan.ProjectRoot)
            .Concat(new[] { spec.OutputPath, spec.ReleaseZipOutputPath })
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path!))
            .Distinct(Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();
        string[] currentArtefactInputs = CollectReleaseArtefactInputPaths(
            plan.ProjectRoot,
            plan.ModuleName,
            plan.ResolvedVersion,
            plan.PreRelease,
            plan.Artefacts);
        DotNetPublishPipelineRunner.SourceProvenance current =
            DotNetPublishPipelineRunner.ReadSourceProvenance(
                plan.ProjectRoot,
                generatedPaths: generatedPaths,
                explicitInputPaths: (plan.SourceInputPaths ?? Array.Empty<string>())
                    .Concat(currentArtefactInputs)
                    .Concat(projectPaths),
                buildProjectPaths: projectPaths,
                buildConfiguration: spec.Configuration,
                sourceRootPaths: plan.SourceRootPaths);
        if (string.IsNullOrWhiteSpace(current.Revision) ||
            !string.Equals(current.Revision, plan.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
            current.Dirty is not false)
        {
            throw new InvalidOperationException(
                "Signed GitHub module release package source changed after planning; publication is blocked before remote mutation.");
        }
    }

    private static string ResolveGitHubModuleRepositoryUrl(ModulePipelinePlan plan)
    {
        PublishConfiguration? publish = plan.Publishes
            .Select(static segment => segment.Configuration)
            .FirstOrDefault(static configuration => configuration.Destination == PublishDestination.GitHub);
        if (publish is null)
        {
            GitCommandResult remote = new GitClient(defaultTimeout: TimeSpan.FromSeconds(15))
                .GetRemoteUrlAsync(plan.ProjectRoot)
                .GetAwaiter()
                .GetResult();
            if (!remote.Succeeded || string.IsNullOrWhiteSpace(remote.StdOut))
            {
                throw new InvalidOperationException(
                    "Unified GitHub module release provenance requires a resolved source repository URL.");
            }

            return remote.StdOut.Trim();
        }
        if (string.IsNullOrWhiteSpace(publish.UserName))
            throw new InvalidOperationException("UserName is required for GitHub publishing.");

        string repository = string.IsNullOrWhiteSpace(publish.RepositoryName)
            ? plan.ModuleName
            : publish.RepositoryName!.Trim();
        return $"https://github.com/{publish.UserName!.Trim()}/{repository}";
    }

    private void ApplyGateModeToPlanInputs(
        ConfigurationGateMode? gateMode,
        ref bool refreshPsd1Only)
    {
        if (gateMode is null)
            return;

        switch (gateMode.Value)
        {
            case ConfigurationGateMode.Manifest:
                if (!refreshPsd1Only)
                    _logger.Info("Gate mode Manifest enabled: forcing RefreshPSD1Only for this run.");
                refreshPsd1Only = true;
                break;
            case ConfigurationGateMode.Documentation:
            case ConfigurationGateMode.Build:
            case ConfigurationGateMode.Publish:
                if (refreshPsd1Only)
                    _logger.Info($"Gate mode {gateMode.Value} enabled: disabling RefreshPSD1Only for this run.");
                refreshPsd1Only = false;
                break;
        }
    }

    private static bool IsVersionPattern(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value!.IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0;

    private static ModuleDevelopmentBinaryMode ResolveDevelopmentBinariesMode(
        bool? enabledFromSegments,
        ModuleDevelopmentBinaryMode? modeFromSegments,
        ModuleDevelopmentBinaryMode modeFromSpec)
    {
        if (enabledFromSegments.HasValue)
        {
            if (!enabledFromSegments.Value)
                return ModuleDevelopmentBinaryMode.Off;

            return modeFromSegments ?? ModuleDevelopmentBinaryMode.Environment;
        }

        return modeFromSegments ?? modeFromSpec;
    }

    private static ConfigurationPublishSegment[] ResolveGateFilteredPublishes(
        ConfigurationGateMode? gateMode,
        IEnumerable<ConfigurationPublishSegment> publishes)
        => gateMode switch
        {
            ConfigurationGateMode.Manifest or ConfigurationGateMode.Documentation or ConfigurationGateMode.Build => Array.Empty<ConfigurationPublishSegment>(),
            ConfigurationGateMode.Publish => publishes
                .Where(static publish => publish?.Configuration is not null)
                .Select(static publish => NormalizePublishGateSegment(publish))
                .ToArray(),
            _ => publishes
                .Where(static publish => publish?.Configuration?.Enabled == true)
                .ToArray()
        };

    private static ConfigurationPublishSegment[] ResolveDependencyVersionSourcePublishes(
        ConfigurationGateMode? gateMode,
        IEnumerable<ConfigurationPublishSegment> publishes)
        => gateMode switch
        {
            ConfigurationGateMode.Manifest => publishes
                .Where(static publish => publish?.Configuration?.Enabled == true)
                .ToArray(),
            ConfigurationGateMode.Documentation or ConfigurationGateMode.Build or ConfigurationGateMode.Publish => publishes
                .Where(static publish => publish?.Configuration is not null)
                .ToArray(),
            _ => publishes
                .Where(static publish => publish?.Configuration?.Enabled == true)
                .ToArray()
        };

    private static ConfigurationPublishSegment NormalizePublishGateSegment(ConfigurationPublishSegment publish)
    {
        publish.Configuration.Enabled = true;
        return publish;
    }

    private static bool IsGateEnabledProjectBuild(
        ConfigurationGateMode? gateMode,
        ConfigurationProjectBuildSegment? segment)
        => segment?.Configuration is not null &&
           gateMode is not ConfigurationGateMode.Manifest &&
           (gateMode != ConfigurationGateMode.Documentation ||
            (segment.Configuration.Enabled && segment.Configuration.BuildBeforeModule)) &&
           (gateMode.HasValue || segment.Configuration.Enabled);

    private static bool IsGateEnabledPackageBuild(
        ConfigurationGateMode? gateMode,
        ConfigurationPackageBuildSegment? segment)
        => segment?.Configuration is not null &&
           gateMode is not ConfigurationGateMode.Manifest &&
           (gateMode != ConfigurationGateMode.Documentation ||
            (segment.Configuration.Enabled && segment.Configuration.BuildBeforeModule)) &&
           (gateMode.HasValue || segment.Configuration.Enabled);

    private static bool IsDocumentationGateActionStage(ModulePipelineActionStage stage)
        => stage is ModulePipelineActionStage.BeforeDependencies
            or ModulePipelineActionStage.AfterDependencies
            or ModulePipelineActionStage.BeforeVersioning
            or ModulePipelineActionStage.AfterVersioning
            or ModulePipelineActionStage.BeforeStaging
            or ModulePipelineActionStage.AfterStaging
            or ModulePipelineActionStage.BeforeBuild
            or ModulePipelineActionStage.AfterBuild
            or ModulePipelineActionStage.BeforeManifest
            or ModulePipelineActionStage.AfterManifest
            or ModulePipelineActionStage.BeforeDocumentation
            or ModulePipelineActionStage.AfterDocumentation;

    private DependencyVersionSourceRepository? ResolvePublishDependencyVersionSource(ConfigurationPublishSegment[] enabledPublishes)
    {
        var candidates = (enabledPublishes ?? Array.Empty<ConfigurationPublishSegment>())
            .Select(static publish => publish.Configuration)
            .Where(static publish => publish.UseAsDependencyVersionSource)
            .ToArray();

        if (candidates.Length == 0)
            return null;

        if (candidates.Length > 1)
            throw new InvalidOperationException("Only one effective New-ConfigurationPublish segment can use -UseAsDependencyVersionSource.");

        var publish = candidates[0];
        if (publish.Destination != PublishDestination.PowerShellGallery)
            throw new InvalidOperationException("-UseAsDependencyVersionSource can only be used with PowerShell repository publish destinations.");

        var repository = publish.Repository?.Name ?? publish.RepositoryName;
        if (string.IsNullOrWhiteSpace(repository))
            repository = "PSGallery";

        _logger.Info($"Dependency version source: resolving Auto/Latest module dependencies from repository '{repository}'.");
        return new DependencyVersionSourceRepository(
            repository,
            publish.Repository?.Credential,
            preferOnlineMetadata: true,
            allowOnlineLookup: true);
    }

    private bool TryAddExternalModuleDependency(
        string moduleName,
        HashSet<string> externalIndex,
        List<string> externalModules)
    {
        if (ModulePipelinePlanningHelpers.ShouldSkipManifestDependencyModule(moduleName))
        {
            _logger.Info($"Skipping built-in PowerShell module '{moduleName}' from manifest dependency output.");
            return false;
        }

        if (externalIndex.Add(moduleName))
            externalModules.Add(moduleName);

        return true;
    }
}
