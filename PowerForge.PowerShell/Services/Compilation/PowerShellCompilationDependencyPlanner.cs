using System.Reflection;

namespace PowerForge;

/// <summary>Builds a deterministic, non-executing inventory of PowerShell compilation dependencies and resources.</summary>
public sealed partial class PowerShellCompilationDependencyPlanner
{
    /// <summary>Builds the exact dependency lock that an artifact build specification will consume.</summary>
    public PowerShellCompilationDependencyGraph AnalyzeGraph(PowerShellCompilationBuildSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        PowerShellCompilationArtifactBuilder.ApplyExplicitTargetContract(spec);
        var runtimeIdentifier = PowerShellCompilationArtifactBuilder.ResolveRuntimeIdentifier(spec);
        if (!string.IsNullOrWhiteSpace(runtimeIdentifier)) spec.RuntimeIdentifier = runtimeIdentifier;
        var sourcePath = Path.GetFullPath(spec.SourcePath);
        var sourceRoot = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var sourceFiles = new[] { sourcePath }
            .Concat(spec.CompilationSourcePaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim().Trim('"')))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        foreach (var path in sourceFiles)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("PowerShell compilation source file was not found.", path);
            if (!PowerShellCompilationPathSafety.PathEquals(path, sourcePath))
                PowerShellCompilationPathSafety.EnsureContained(sourceRoot, path, $"Additional compilation source '{path}' escapes the root module directory.");
        }
        var dependencies = Analyze(spec, sourceFiles);
        return AnalyzeGraph(spec, sourceFiles, dependencies);
    }

    /// <summary>Plans dependencies for a source graph selected by the shared input resolver.</summary>
    public PowerShellCompilationDependency[] Analyze(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationMode? mode = null,
        PowerShellCompilationResourceMode resourceMode = PowerShellCompilationResourceMode.Declared,
        IEnumerable<string>? includeResource = null,
        IEnumerable<string>? excludeResource = null,
        string? outputDirectory = null)
        => Analyze(input, mode, resourceMode, includeResource, excludeResource, outputDirectory, null);

    internal PowerShellCompilationDependency[] Analyze(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationMode? mode,
        PowerShellCompilationResourceMode resourceMode,
        IEnumerable<string>? includeResource,
        IEnumerable<string>? excludeResource,
        string? outputDirectory,
        IEnumerable<string>? generatedOutputDirectories)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var effectiveMode = mode ?? input.Mode;
        return AnalyzeCore(
            input.SourcePath,
            input.ModuleManifestPath,
            input.ModuleRoot,
            input.Kind,
            effectiveMode,
            input.SourceFiles,
            input.CompilationSourceFiles,
            resourceMode,
            includeResource,
            excludeResource,
            outputDirectory,
            generatedOutputDirectories);
    }

    /// <summary>Builds the locked graph used by semantic analysis, artifact planning, and deployment evidence.</summary>
    public PowerShellCompilationDependencyGraph AnalyzeGraph(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationMode? mode = null,
        PowerShellCompilationResourceMode resourceMode = PowerShellCompilationResourceMode.Declared,
        IEnumerable<string>? includeResource = null,
        IEnumerable<string>? excludeResource = null,
        string? outputDirectory = null,
        string? targetFramework = null,
        string? runtimeIdentifier = null)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var effectiveMode = mode ?? input.Mode;
        var dependencies = Analyze(input, effectiveMode, resourceMode, includeResource, excludeResource, outputDirectory);
        return PowerShellCompilationDependencyGraphBuilder.Build(
            input.SourcePath,
            input.ModuleManifestPath,
            input.ModuleRoot,
            input.Kind,
            effectiveMode,
            input.CompilationSourceFiles,
            dependencies,
            targetFramework,
            runtimeIdentifier);
    }

    internal static PowerShellCompilationDependency[] Analyze(
        PowerShellCompilationBuildSpec spec,
        IEnumerable<string> sourceFiles)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        var manifestPath = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule
            ? PowerShellCompiledModuleManifest.ResolveSourceManifest(spec.SourcePath, spec.ModuleManifestPath) : spec.ModuleManifestPath;
        if (!File.Exists(manifestPath)) manifestPath = null;
        var moduleRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath ?? spec.SourcePath)) ?? Directory.GetCurrentDirectory();
        return AnalyzeCore(
            spec.SourcePath,
            manifestPath,
            moduleRoot,
            spec.Kind,
            spec.Mode,
            spec.RuntimeSourcePaths is { Length: > 0 } ? spec.RuntimeSourcePaths : sourceFiles.ToArray(),
            sourceFiles,
            spec.ResourceMode,
            spec.IncludeResource,
            spec.ExcludeResource,
            spec.OutputDirectory,
            spec.GeneratedOutputDirectories);
    }

    internal static PowerShellCompilationDependencyGraph AnalyzeGraph(
        PowerShellCompilationBuildSpec spec,
        IEnumerable<string> sourceFiles,
        IReadOnlyCollection<PowerShellCompilationDependency> dependencies)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        var manifestPath = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule
            ? PowerShellCompiledModuleManifest.ResolveSourceManifest(spec.SourcePath, spec.ModuleManifestPath)
            : spec.ModuleManifestPath;
        if (!File.Exists(manifestPath)) manifestPath = null;
        var moduleRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath ?? spec.SourcePath)) ?? Directory.GetCurrentDirectory();
        return PowerShellCompilationDependencyGraphBuilder.Build(
            spec.SourcePath,
            manifestPath,
            moduleRoot,
            spec.Kind,
            spec.Mode,
            sourceFiles,
            dependencies,
            spec.TargetFramework,
            spec.RuntimeIdentifier,
            includeRuntimePack: spec.Kind == PowerShellCompilationArtifactKind.Executable &&
                                (spec.SelfContained || spec.Optimization != PowerShellCompilationExecutableOptimization.None),
            nuGetPackageRoot: spec.NuGetPackageRoot);
    }
    private static PowerShellCompilationDependency[] AnalyzeCore(
        string sourcePath,
        string? manifestPath,
        string moduleRoot,
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        IEnumerable<string> sourceFiles,
        IEnumerable<string> compilationSourceFiles,
        PowerShellCompilationResourceMode resourceMode,
        IEnumerable<string>? includeResource,
        IEnumerable<string>? excludeResource,
        string? outputDirectory,
        IEnumerable<string>? generatedOutputDirectories)
    {
        if (!Enum.IsDefined(typeof(PowerShellCompilationResourceMode), resourceMode))
            throw new ArgumentOutOfRangeException(nameof(resourceMode));
        var root = Path.GetFullPath(moduleRoot);
        var source = Path.GetFullPath(sourcePath);
        var results = new List<PowerShellCompilationDependency>();
        var localPaths = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        var compilationGraph = compilationSourceFiles
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .OrderBy(path => FrameworkCompatibility.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var file in compilationGraph)
        {
            AddLocal(
                results,
                localPaths,
                root,
                file,
                PowerShellCompilationDependencyDiscovery.SourceGraph,
                GetSourceDisposition(kind, mode, PowerShellCompilationPathSafety.PathEquals(file, source)),
                GetSourceNote(kind, mode),
                selection: PowerShellCompilationDependencySelection.Source);
        }

        if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
        {
            var manifest = Path.GetFullPath(manifestPath!);
            AddLocal(
                results,
                localPaths,
                root,
                manifest,
                PowerShellCompilationDependencyDiscovery.ModuleManifest,
                kind == PowerShellCompilationArtifactKind.BinaryModule
                    ? PowerShellCompilationDependencyDisposition.CopiedAdjacent
                    : PowerShellCompilationDependencyDisposition.NotIncluded,
                kind == PowerShellCompilationArtifactKind.BinaryModule
                    ? "The source manifest is rewritten around the generated module while compatible metadata is preserved."
                    : "Module manifests are not included in plain CLR libraries or script executables.",
                selection: PowerShellCompilationDependencySelection.Source);
            CollectManifestDependencies(manifest, root, kind, mode, results, localPaths, new HashSet<string>(PowerShellCompilationPathSafety.PathComparer), includeRootModule: false);
        }

        foreach (var file in sourceFiles
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(PowerShellCompilationPathSafety.PathComparer)
                     .OrderBy(path => FrameworkCompatibility.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase))
        {
            AddLocal(
                results,
                localPaths,
                root,
                file,
                PowerShellCompilationDependencyDiscovery.SourceGraph,
                GetRuntimeSourceDisposition(kind, mode),
                GetRuntimeSourceNote(kind, mode),
                selection: PowerShellCompilationDependencySelection.Source);
        }

        CollectConfiguredPayload(
            root,
            source,
            manifestPath,
            kind,
            mode,
            resourceMode,
            includeResource,
            excludeResource,
            outputDirectory,
            generatedOutputDirectories,
            compilationGraph,
            results,
            localPaths);
        return results
            .OrderBy(static dependency => dependency.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static dependency => dependency.Discovery)
            .ToArray();
    }

    private static void CollectManifestDependencies(
        string manifestPath,
        string moduleRoot,
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        ICollection<PowerShellCompilationDependency> results,
        ISet<string> localPaths,
        ISet<string> visited,
        bool includeRootModule)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        if (!visited.Add(manifestPath)) return;
        var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? moduleRoot;

        if (ManifestEditor.TryGetRequiredModules(manifestPath, out RequiredModuleReference[]? requiredModules) && requiredModules is not null)
        {
            foreach (var module in requiredModules)
            {
                results.Add(new PowerShellCompilationDependency(
                    module.ModuleName,
                    null,
                    module.ModuleName,
                    PowerShellCompilationDependencyKind.RequiredModule,
                    PowerShellCompilationDependencyDiscovery.RequiredModules,
                    PowerShellCompilationDependencyDisposition.ExternalRequirement,
                    exists: false,
                    sizeBytes: 0,
                    "RequiredModules are preserved in a generated module manifest and resolved by the importing PowerShell environment; they are not embedded.",
                    PowerShellCompilationDependencySelection.External));
            }
        }

        if (includeRootModule)
        {
            var rootModule = ModuleManifestValueReader.ReadTopLevelLiteralStringOrThrow(manifestPath, "RootModule");
            if (!string.IsNullOrWhiteSpace(rootModule))
                AddManifestReference(rootModule!, PowerShellCompilationDependencyDiscovery.RootModule, required: true, manifestDirectory, moduleRoot, kind, mode, results, localPaths, visited);
        }

        AddManifestArray("FormatsToProcess", PowerShellCompilationDependencyDiscovery.FormatsToProcess, required: true);
        AddManifestArray("TypesToProcess", PowerShellCompilationDependencyDiscovery.TypesToProcess, required: true);
        AddManifestArray("ScriptsToProcess", PowerShellCompilationDependencyDiscovery.ScriptsToProcess, required: true);
        AddManifestArray("RequiredAssemblies", PowerShellCompilationDependencyDiscovery.RequiredAssemblies, required: false);
        AddManifestArray("FileList", PowerShellCompilationDependencyDiscovery.FileList, required: true);
        foreach (var nested in ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, "NestedModules"))
            AddManifestReference(nested, PowerShellCompilationDependencyDiscovery.NestedModules, IsContainedReference(nested), manifestDirectory, moduleRoot, kind, mode, results, localPaths, visited);

        void AddManifestArray(
            string key,
            PowerShellCompilationDependencyDiscovery discovery,
            bool required)
        {
            foreach (var value in ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(manifestPath, key) ?? Array.Empty<string>())
                AddManifestReference(
                    value,
                    discovery,
                    required || discovery != PowerShellCompilationDependencyDiscovery.FileList && IsContainedReference(value),
                    manifestDirectory,
                    moduleRoot,
                    kind,
                    mode,
                    results,
                    localPaths,
                    visited);
        }
    }

    private static void AddManifestReference(
        string value,
        PowerShellCompilationDependencyDiscovery discovery,
        bool required,
        string manifestDirectory,
        string moduleRoot,
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        ICollection<PowerShellCompilationDependency> results,
        ISet<string> localPaths,
        ISet<string> visited)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var isContainedReference = discovery == PowerShellCompilationDependencyDiscovery.FileList || IsContainedReference(value);
        if (!isContainedReference)
        {
            results.Add(new PowerShellCompilationDependency(
                value,
                null,
                value,
                discovery == PowerShellCompilationDependencyDiscovery.RequiredAssemblies
                    ? PowerShellCompilationDependencyKind.ManagedAssembly
                    : PowerShellCompilationDependencyKind.RequiredModule,
                discovery,
                PowerShellCompilationDependencyDisposition.ExternalRequirement,
                exists: false,
                sizeBytes: 0,
                "This named dependency remains an external PowerShell/.NET resolution requirement and is not embedded.",
                PowerShellCompilationDependencySelection.External));
            return;
        }

        var normalized = PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(value);
        if (Path.IsPathRooted(normalized) || LooksLikeWindowsRootedPath(value))
            throw new InvalidOperationException($"Module manifest file reference '{value}' must remain relative to the module root.");
        var sourcePath = Path.GetFullPath(Path.Combine(manifestDirectory, normalized));
        PowerShellCompilationPathSafety.EnsureContained(moduleRoot, sourcePath, $"Module dependency '{value}' escapes the module root.");
        if (Directory.Exists(sourcePath))
        {
            PowerShellCompilationPathSafety.EnsureNoLinks(
                moduleRoot,
                sourcePath,
                $"Module dependency directory '{value}' traverses a symbolic link or junction.");
            foreach (var containedFile in EnumerateContainedFiles(sourcePath, ignoreInaccessible: false))
            {
                var containedDisposition = GetManifestDisposition(kind, mode, discovery, containedFile);
                AddLocal(
                    results,
                    localPaths,
                    moduleRoot,
                    containedFile,
                    discovery,
                    containedDisposition,
                    GetManifestNote(kind, mode, discovery, containedFile),
                    required,
                    PowerShellCompilationDependencySelection.Required);
            }
            return;
        }
        if (File.Exists(sourcePath))
            PowerShellCompilationPathSafety.EnsureNoLinks(moduleRoot, sourcePath, $"Module dependency '{value}' traverses a symbolic link or junction.");
        if (discovery == PowerShellCompilationDependencyDiscovery.FileList && localPaths.Contains(sourcePath))
            return;

        var disposition = GetManifestDisposition(kind, mode, discovery, sourcePath);
        AddLocal(results, localPaths, moduleRoot, sourcePath, discovery, disposition,
            GetManifestNote(kind, mode, discovery, sourcePath), required,
            PowerShellCompilationDependencySelection.Required);
        if (File.Exists(sourcePath) && Path.GetExtension(sourcePath).Equals(".psd1", StringComparison.OrdinalIgnoreCase))
            CollectManifestDependencies(sourcePath, moduleRoot, kind, mode, results, localPaths, visited, includeRootModule: true);
    }

    private static void AddLocal(
        ICollection<PowerShellCompilationDependency> results,
        ISet<string> localPaths,
        string moduleRoot,
        string sourcePath,
        PowerShellCompilationDependencyDiscovery discovery,
        PowerShellCompilationDependencyDisposition disposition,
        string note,
        bool required = true,
        PowerShellCompilationDependencySelection selection = PowerShellCompilationDependencySelection.Source)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var isNewPath = localPaths.Add(fullPath);
        if (!isNewPath && (selection != PowerShellCompilationDependencySelection.Required ||
                           results.Any(dependency =>
                               dependency.Selection == PowerShellCompilationDependencySelection.Required &&
                               dependency.SourcePath is not null &&
                               PowerShellCompilationPathSafety.PathEquals(dependency.SourcePath, fullPath))))
            return;
        var exists = File.Exists(fullPath);
        var relative = FrameworkCompatibility.GetRelativePath(moduleRoot, fullPath).Replace('\\', '/');
        var effectiveDisposition = !exists
            ? required
                ? PowerShellCompilationDependencyDisposition.Missing
                : PowerShellCompilationDependencyDisposition.NotIncluded
            : disposition;
        var effectiveNote = !exists
            ? required
                ? "The required contained dependency was not found; artifact generation must fail closed."
                : "The optional manifest content was not present and cannot be copied into the generated artifact."
            : note;
        results.Add(new PowerShellCompilationDependency(
            Path.GetFileName(fullPath),
            fullPath,
            relative,
            Classify(fullPath),
            discovery,
            effectiveDisposition,
            exists,
            exists ? new FileInfo(fullPath).Length : 0,
            effectiveNote,
            selection));
    }

    private static PowerShellCompilationDependencyDisposition GetSourceDisposition(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        bool isEntryPoint)
    {
        if (kind == PowerShellCompilationArtifactKind.Executable && mode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Hybrid)
            return isEntryPoint ? PowerShellCompilationDependencyDisposition.Embedded : PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted;
        if (kind == PowerShellCompilationArtifactKind.BinaryModule && mode == PowerShellCompilationMode.Hybrid)
            return PowerShellCompilationDependencyDisposition.PreservedScript;
        return PowerShellCompilationDependencyDisposition.Compiled;
    }

    private static string GetSourceNote(PowerShellCompilationArtifactKind kind, PowerShellCompilationMode mode)
        => kind == PowerShellCompilationArtifactKind.Executable && mode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Hybrid
            ? mode == PowerShellCompilationMode.Hybrid
                ? "Eligible functions compile into registered cmdlets while the entry script and unsupported regions are embedded for the PowerShell fallback host."
                : "The entry script is embedded; reachable literal dot-source dependencies are embedded and extracted into a contained temporary layout."
            : kind == PowerShellCompilationArtifactKind.BinaryModule && mode == PowerShellCompilationMode.Hybrid
                ? "Eligible functions compile into cmdlets while unsupported script regions remain in the generated Hybrid module."
                : kind == PowerShellCompilationArtifactKind.Library && mode == PowerShellCompilationMode.Hybrid
                    ? "Eligible functions compile into CLR methods; unsupported functions are omitted because a plain library has no PowerShell fallback host."
                    : "The complete accepted source graph is lowered into generated CLR code.";

    private static PowerShellCompilationDependencyDisposition GetRuntimeSourceDisposition(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode)
        => kind == PowerShellCompilationArtifactKind.Executable && mode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Hybrid
            ? PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted
            : kind == PowerShellCompilationArtifactKind.BinaryModule && mode == PowerShellCompilationMode.Hybrid
                ? PowerShellCompilationDependencyDisposition.PreservedScript
                : PowerShellCompilationDependencyDisposition.NotIncluded;

    private static string GetRuntimeSourceNote(PowerShellCompilationArtifactKind kind, PowerShellCompilationMode mode)
        => kind == PowerShellCompilationArtifactKind.Executable && mode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Hybrid
            ? "The reachable script dependency is embedded and extracted into the contained entrypoint layout."
            : kind == PowerShellCompilationArtifactKind.BinaryModule && mode == PowerShellCompilationMode.Hybrid
                ? "The source has runtime loading or scope semantics and remains on the generated Hybrid script path."
                : "This source is outside the typed compilation scope and the selected artifact shape has no fallback host for it.";

    private static PowerShellCompilationDependencyDisposition GetManifestDisposition(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        PowerShellCompilationDependencyDiscovery discovery,
        string sourcePath)
    {
        if (kind != PowerShellCompilationArtifactKind.BinaryModule)
            return IsScriptRuntimeHook(discovery, sourcePath)
                ? PowerShellCompilationDependencyDisposition.NotIncluded
                : kind == PowerShellCompilationArtifactKind.Executable && mode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Hybrid
                    ? PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted
                    : PowerShellCompilationDependencyDisposition.CopiedAdjacent;
        if (mode == PowerShellCompilationMode.Strict && IsScriptRuntimeHook(discovery, sourcePath))
            return PowerShellCompilationDependencyDisposition.NotIncluded;
        return PowerShellCompilationDependencyDisposition.CopiedAdjacent;
    }

    private static string GetManifestNote(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        PowerShellCompilationDependencyDiscovery discovery,
        string sourcePath)
    {
        if (kind != PowerShellCompilationArtifactKind.BinaryModule)
            return IsScriptRuntimeHook(discovery, sourcePath)
                ? "Manifest script runtime hooks are not copied into CLR libraries or standalone script executables."
                : kind == PowerShellCompilationArtifactKind.Executable && mode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Hybrid
                    ? "The manifest-required contained file is embedded and extracted into the packaged source layout."
                    : "The manifest-required contained file is copied beside the generated artifact with its relative path preserved.";
        if (mode == PowerShellCompilationMode.Strict && IsScriptRuntimeHook(discovery, sourcePath))
            return "Strict binary modules reject script runtime hooks; use Hybrid or remove the hook.";
        return "The contained manifest dependency is copied beside the generated module and retains its relative path.";
    }

    private static bool IsScriptRuntimeHook(PowerShellCompilationDependencyDiscovery discovery, string sourcePath)
    {
        if (discovery == PowerShellCompilationDependencyDiscovery.ScriptsToProcess) return true;
        if (discovery != PowerShellCompilationDependencyDiscovery.NestedModules) return false;
        var extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase) || !File.Exists(sourcePath))
            return false;
        var rootModule = ModuleManifestValueReader.ReadTopLevelLiteralStringOrThrow(sourcePath, "RootModule");
        return string.IsNullOrWhiteSpace(rootModule) ||
               !Path.GetExtension(rootModule).Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static PowerShellCompilationDependencyKind Classify(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase))
            return IsModuleManifest(path)
                ? PowerShellCompilationDependencyKind.ModuleManifest
                : PowerShellCompilationDependencyKind.Content;
        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) || extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase)) return PowerShellCompilationDependencyKind.PowerShellSource;
        if (extension.Equals(".css", StringComparison.OrdinalIgnoreCase)) return PowerShellCompilationDependencyKind.StyleSheet;
        if (extension.Equals(".js", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase)) return PowerShellCompilationDependencyKind.JavaScript;
        if (extension.Equals(".ps1xml", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(path).IndexOf("type", StringComparison.OrdinalIgnoreCase) >= 0
                ? PowerShellCompilationDependencyKind.TypeData
                : PowerShellCompilationDependencyKind.FormatData;
        if (extension.Equals(".so", StringComparison.OrdinalIgnoreCase) || extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase)) return PowerShellCompilationDependencyKind.NativeLibrary;
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)) return IsManagedAssembly(path)
            ? PowerShellCompilationDependencyKind.ManagedAssembly
            : PowerShellCompilationDependencyKind.NativeLibrary;
        return PowerShellCompilationDependencyKind.Content;
    }

    private static bool IsManagedAssembly(string path)
    {
        if (!File.Exists(path)) return true;
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (FileLoadException)
        {
            return true;
        }
    }

    private static bool IsModuleManifest(string path)
        => File.Exists(path) &&
           (!string.IsNullOrWhiteSpace(ModuleManifestValueReader.ReadTopLevelString(path, "ModuleVersion")) ||
            ModuleManifestValueReader.TryGetTopLevelString(path, "RootModule", out _) ||
            ModuleManifestValueReader.TryGetTopLevelString(path, "ModuleToProcess", out _));

    private sealed class ResourceCandidate
    {
        internal ResourceCandidate(string fullPath, string relativePath, PowerShellCompilationDependencyDiscovery discovery)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            Discovery = discovery;
        }

        internal string FullPath { get; }
        internal string RelativePath { get; }
        internal PowerShellCompilationDependencyDiscovery Discovery { get; }
    }

    private static bool IsContainedReference(string value)
    {
        var extension = Path.GetExtension(value);
        return value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.StartsWith(".", StringComparison.Ordinal) ||
               extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".cdxml", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ps1xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWindowsRootedPath(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
           path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
}
