using System.Reflection;

namespace PowerForge;

/// <summary>Builds a deterministic, non-executing inventory of PowerShell compilation dependencies and resources.</summary>
public sealed class PowerShellCompilationDependencyPlanner
{
    private static readonly (string Name, PowerShellCompilationDependencyDiscovery Discovery)[] ConventionalDirectories =
    {
        ("Resources", PowerShellCompilationDependencyDiscovery.ConventionalResourceDirectory),
        ("Resource", PowerShellCompilationDependencyDiscovery.ConventionalResourceDirectory),
        ("Lib", PowerShellCompilationDependencyDiscovery.ConventionalLibraryDirectory),
        ("Libraries", PowerShellCompilationDependencyDiscovery.ConventionalLibraryDirectory),
        ("runtimes", PowerShellCompilationDependencyDiscovery.ConventionalRuntimeDirectory)
    };

    /// <summary>Plans dependencies for a source graph selected by the shared input resolver.</summary>
    public PowerShellCompilationDependency[] Analyze(
        PowerShellCompilationResolvedInput input,
        PowerShellCompilationMode? mode = null)
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
            input.CompilationSourceFiles);
    }

    internal static PowerShellCompilationDependency[] Analyze(
        PowerShellCompilationBuildSpec spec,
        IEnumerable<string> sourceFiles)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        var moduleRoot = Path.GetDirectoryName(Path.GetFullPath(spec.ModuleManifestPath ?? spec.SourcePath))
                         ?? Directory.GetCurrentDirectory();
        return AnalyzeCore(
            spec.SourcePath,
            spec.ModuleManifestPath,
            moduleRoot,
            spec.Kind,
            spec.Mode,
            spec.RuntimeSourcePaths is { Length: > 0 } ? spec.RuntimeSourcePaths : sourceFiles.ToArray(),
            sourceFiles);
    }

    /// <summary>Aggregates a detailed dependency inventory for census and dashboard output.</summary>
    public static PowerShellCompilationDependencySummary[] Summarize(IEnumerable<PowerShellCompilationDependency> dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        return dependencies
            .GroupBy(static dependency => new { dependency.Kind, dependency.Disposition })
            .Select(static group => new PowerShellCompilationDependencySummary(
                group.Key.Kind,
                group.Key.Disposition,
                group.Count(),
                group.Count(static dependency => dependency.Disposition == PowerShellCompilationDependencyDisposition.Missing),
                group.Sum(static dependency => dependency.SizeBytes)))
            .OrderBy(static summary => summary.Kind)
            .ThenBy(static summary => summary.Disposition)
            .ToArray();
    }

    private static PowerShellCompilationDependency[] AnalyzeCore(
        string sourcePath,
        string? manifestPath,
        string moduleRoot,
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        IEnumerable<string> sourceFiles,
        IEnumerable<string> compilationSourceFiles)
    {
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
                GetSourceNote(kind, mode));
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
                    : "Module manifests are not included in plain CLR libraries or script executables.");
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
                GetRuntimeSourceNote(kind, mode));
        }

        CollectConventionalPayload(root, kind, results, localPaths);
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
                    "RequiredModules are preserved in a generated module manifest and resolved by the importing PowerShell environment; they are not embedded."));
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
        AddManifestArray("FileList", PowerShellCompilationDependencyDiscovery.FileList, required: false);
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
                "This named dependency remains an external PowerShell/.NET resolution requirement and is not embedded."));
            return;
        }

        var normalized = PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(value);
        if (Path.IsPathRooted(normalized) || LooksLikeWindowsRootedPath(value))
            throw new InvalidOperationException($"Module dependency '{value}' must be relative to its manifest.");
        var sourcePath = Path.GetFullPath(Path.Combine(manifestDirectory, normalized));
        PowerShellCompilationPathSafety.EnsureContained(moduleRoot, sourcePath, $"Module dependency '{value}' escapes the module root.");
        if (File.Exists(sourcePath))
            PowerShellCompilationPathSafety.EnsureNoLinks(moduleRoot, sourcePath, $"Module dependency '{value}' traverses a symbolic link or junction.");

        var disposition = GetManifestDisposition(kind, mode, discovery, sourcePath);
        AddLocal(results, localPaths, moduleRoot, sourcePath, discovery, disposition,
            GetManifestNote(kind, mode, discovery, sourcePath), required);
        if (File.Exists(sourcePath) && Path.GetExtension(sourcePath).Equals(".psd1", StringComparison.OrdinalIgnoreCase))
            CollectManifestDependencies(sourcePath, moduleRoot, kind, mode, results, localPaths, visited, includeRootModule: true);
    }

    private static void CollectConventionalPayload(
        string moduleRoot,
        PowerShellCompilationArtifactKind kind,
        ICollection<PowerShellCompilationDependency> results,
        ISet<string> localPaths)
    {
        foreach (var conventional in ConventionalDirectories)
        {
            var directory = FindTopLevelDirectory(moduleRoot, conventional.Name);
            if (directory is null) continue;
            PowerShellCompilationPathSafety.EnsureNoLinks(moduleRoot, directory, $"Conventional module payload directory '{directory}' traverses a symbolic link or junction.");
            foreach (var file in EnumerateContainedFiles(directory))
            {
                PowerShellCompilationPathSafety.EnsureNoLinks(moduleRoot, file, $"Conventional module payload '{file}' traverses a symbolic link or junction.");
                AddLocal(
                    results,
                    localPaths,
                    moduleRoot,
                    file,
                    conventional.Discovery,
                    kind == PowerShellCompilationArtifactKind.BinaryModule
                        ? PowerShellCompilationDependencyDisposition.CopiedAdjacent
                        : PowerShellCompilationDependencyDisposition.NotIncluded,
                    kind == PowerShellCompilationArtifactKind.BinaryModule
                        ? "The conventional runtime payload is copied beside the generated module with its relative layout preserved."
                        : "Conventional module payloads are not automatically embedded into script executables or plain CLR libraries.");
            }
        }
    }

    private static IEnumerable<string> EnumerateContainedFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
                yield return Path.GetFullPath(file);
            foreach (var directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"Conventional module payload directory '{directory}' is a symbolic link or junction.");
                pending.Push(directory);
            }
        }
    }

    private static void AddLocal(
        ICollection<PowerShellCompilationDependency> results,
        ISet<string> localPaths,
        string moduleRoot,
        string sourcePath,
        PowerShellCompilationDependencyDiscovery discovery,
        PowerShellCompilationDependencyDisposition disposition,
        string note,
        bool required = true)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        if (!localPaths.Add(fullPath)) return;
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
            effectiveNote));
    }

    private static PowerShellCompilationDependencyDisposition GetSourceDisposition(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        bool isEntryPoint)
    {
        if (kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Package)
            return isEntryPoint ? PowerShellCompilationDependencyDisposition.Embedded : PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted;
        if (kind == PowerShellCompilationArtifactKind.BinaryModule && mode == PowerShellCompilationMode.Hybrid)
            return PowerShellCompilationDependencyDisposition.PreservedScript;
        return PowerShellCompilationDependencyDisposition.Compiled;
    }

    private static string GetSourceNote(PowerShellCompilationArtifactKind kind, PowerShellCompilationMode mode)
        => kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Package
            ? "The entry script is embedded; reachable literal dot-source dependencies are embedded and extracted into a contained temporary layout."
            : kind == PowerShellCompilationArtifactKind.BinaryModule && mode == PowerShellCompilationMode.Hybrid
                ? "Eligible functions compile into cmdlets while unsupported script regions remain in the generated Hybrid module."
                : kind == PowerShellCompilationArtifactKind.Library && mode == PowerShellCompilationMode.Hybrid
                    ? "Eligible functions compile into CLR methods; unsupported functions are omitted because a plain library has no PowerShell fallback host."
                    : "The complete accepted source graph is lowered into generated CLR code.";

    private static PowerShellCompilationDependencyDisposition GetRuntimeSourceDisposition(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode)
        => kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Package
            ? PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted
            : kind == PowerShellCompilationArtifactKind.BinaryModule && mode == PowerShellCompilationMode.Hybrid
                ? PowerShellCompilationDependencyDisposition.PreservedScript
                : PowerShellCompilationDependencyDisposition.NotIncluded;

    private static string GetRuntimeSourceNote(PowerShellCompilationArtifactKind kind, PowerShellCompilationMode mode)
        => kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Package
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
            return PowerShellCompilationDependencyDisposition.NotIncluded;
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
            return "Manifest runtime files are not included in plain CLR libraries or standalone script executables.";
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
        if (extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase)) return PowerShellCompilationDependencyKind.ModuleManifest;
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

    private static string? FindTopLevelDirectory(string root, string name)
        => Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(directory => Path.GetFileName(directory).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsContainedReference(string value)
    {
        var extension = Path.GetExtension(value);
        return value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.StartsWith(".", StringComparison.Ordinal) ||
               extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ps1xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWindowsRootedPath(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
           path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
}
