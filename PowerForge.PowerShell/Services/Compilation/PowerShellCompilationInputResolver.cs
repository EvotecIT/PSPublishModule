namespace PowerForge;

/// <summary>
/// Describes a deterministic PowerShell compilation input selected from a script, module, manifest, or module directory.
/// </summary>
public sealed class PowerShellCompilationResolvedInput
{
    internal PowerShellCompilationResolvedInput(
        string requestedPath,
        string sourcePath,
        string? moduleManifestPath,
        string moduleRoot,
        string artifactName,
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        string[] sourceFiles,
        string[] compilationSourceFiles,
        string[]? recursiveSourceDirectories = null)
    {
        RequestedPath = requestedPath;
        SourcePath = sourcePath;
        ModuleManifestPath = moduleManifestPath;
        ModuleRoot = moduleRoot;
        ArtifactName = artifactName;
        Kind = kind;
        Mode = mode;
        PowerShellCompilationBuildSpec.EnsureModeSupported(kind, mode);
        SourceFiles = sourceFiles;
        CompilationSourceFiles = compilationSourceFiles;
        RecursiveSourceDirectories = recursiveSourceDirectories ?? Array.Empty<string>();
        var dependencyPlanner = new PowerShellCompilationDependencyPlanner();
        Dependencies = dependencyPlanner.Analyze(this);
        DependencyGraph = PowerShellCompilationDependencyGraphBuilder.Build(
            SourcePath,
            ModuleManifestPath,
            ModuleRoot,
            Kind,
            Mode,
            CompilationSourceFiles,
            Dependencies);
    }

    /// <summary>Original absolute input path.</summary>
    public string RequestedPath { get; }

    /// <summary>Resolved root PowerShell script or script-module path passed to the compiler.</summary>
    public string SourcePath { get; }

    /// <summary>Resolved source module manifest, when present.</summary>
    public string? ModuleManifestPath { get; }

    /// <summary>Root directory containing the resolved module or script.</summary>
    public string ModuleRoot { get; }

    /// <summary>Default artifact name inferred from the module or script.</summary>
    public string ArtifactName { get; }

    /// <summary>Resolved artifact kind, including inference when the caller omitted it.</summary>
    public PowerShellCompilationArtifactKind Kind { get; }

    /// <summary>Resolved compilation mode, including inference when the caller omitted it.</summary>
    public PowerShellCompilationMode Mode { get; }

    /// <summary>Contained runtime source files discovered from literal manifest hooks and dot-source expressions.</summary>
    public string[] SourceFiles { get; }

    /// <summary>Root module plus contained literal dot-sourced files that share its compilation scope.</summary>
    public string[] CompilationSourceFiles { get; }

    /// <summary>Deterministic source, module, assembly, and content dependency decisions for the inferred artifact shape.</summary>
    public PowerShellCompilationDependency[] Dependencies { get; }

    /// <summary>Locked dependency graph sharing stable identities across analysis and deployment views.</summary>
    public PowerShellCompilationDependencyGraph DependencyGraph { get; }

    internal string[] RecursiveSourceDirectories { get; }
}

/// <summary>
/// Resolves common PowerShell project layouts without requiring a build DSL configuration.
/// </summary>
public sealed class PowerShellCompilationInputResolver
{
    /// <summary>Resolves a loose set of PowerShell script files into one typed library or strict binary module.</summary>
    public PowerShellCompilationResolvedInput Resolve(
        IEnumerable<string> paths,
        PowerShellCompilationArtifactKind? kind = null,
        PowerShellCompilationMode? mode = null,
        string? entryPointPath = null,
        bool allowDynamicModuleRuntimeSources = false)
    {
        if (paths is null) throw new ArgumentNullException(nameof(paths));
        var requestedPaths = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim().Trim('"')))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        if (requestedPaths.Length == 0)
            throw new ArgumentException("At least one PowerShell script path is required.", nameof(paths));
        if (requestedPaths.Length == 1 && string.IsNullOrWhiteSpace(entryPointPath))
            return Resolve(requestedPaths[0], kind, mode, allowDynamicModuleRuntimeSources);
        if (kind.HasValue && !Enum.IsDefined(typeof(PowerShellCompilationArtifactKind), kind.Value))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (mode.HasValue && (!Enum.IsDefined(typeof(PowerShellCompilationMode), mode.Value) || mode.Value == PowerShellCompilationMode.Analyze))
            throw new ArgumentOutOfRangeException(nameof(mode), "Artifact builds accept Package, Hybrid, or Strict mode.");

        foreach (var requestedPath in requestedPaths)
        {
            if (!File.Exists(requestedPath))
                throw new FileNotFoundException("PowerShell compilation input was not found.", requestedPath);
            if (!Path.GetExtension(requestedPath).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A loose compilation file set may contain only .ps1 files. Point to a .psd1, .psm1, or module directory for module discovery.", nameof(paths));
        }

        var resolvedKind = kind ?? (string.IsNullOrWhiteSpace(entryPointPath)
            ? PowerShellCompilationArtifactKind.Library
            : PowerShellCompilationArtifactKind.Executable);
        if (!string.IsNullOrWhiteSpace(entryPointPath) && resolvedKind != PowerShellCompilationArtifactKind.Executable)
            throw new InvalidOperationException("An explicit entrypoint is valid only for an Executable artifact.");
        if (resolvedKind == PowerShellCompilationArtifactKind.Executable)
        {
            if (requestedPaths.Length > 1 && string.IsNullOrWhiteSpace(entryPointPath))
                throw new InvalidOperationException("A multi-file executable requires an explicit .ps1 entrypoint.");
            var entryPoint = string.IsNullOrWhiteSpace(entryPointPath)
                ? requestedPaths[0]
                : Path.GetFullPath(entryPointPath!.Trim().Trim('"'));
            if (!requestedPaths.Contains(entryPoint, PowerShellCompilationPathSafety.PathComparer))
                throw new InvalidOperationException("The executable entrypoint must also be present in the requested path set.");
            var sourceRoot = Path.GetDirectoryName(entryPoint) ?? Directory.GetCurrentDirectory();
            var closure = PowerShellHybridDependencyResolver.DiscoverDependencies(entryPoint);
            var unreachable = requestedPaths.Where(path => !closure.Contains(path, PowerShellCompilationPathSafety.PathComparer)).ToArray();
            if (unreachable.Length > 0)
                throw new InvalidOperationException($"Executable path set contains source file(s) unreachable from the entrypoint: {string.Join(", ", unreachable.Select(Path.GetFileName))}.");
            var executableMode = mode ?? PowerShellCompilationMode.Package;
            return new PowerShellCompilationResolvedInput(
                entryPoint,
                entryPoint,
                null,
                sourceRoot,
                Path.GetFileNameWithoutExtension(entryPoint),
                resolvedKind,
                executableMode,
                closure,
                closure);
        }
        var resolvedMode = mode ?? (resolvedKind == PowerShellCompilationArtifactKind.BinaryModule
            ? PowerShellCompilationMode.Strict
            : PowerShellCompilationBuildSpec.GetDefaultMode(resolvedKind));
        if (resolvedKind == PowerShellCompilationArtifactKind.BinaryModule && resolvedMode != PowerShellCompilationMode.Strict)
            throw new InvalidOperationException(
                "A loose BinaryModule file set requires Strict mode because it has no script-module entrypoint in which unsupported functions could be preserved. Point to a module root for Hybrid fallback.");

        var sourcePath = requestedPaths[0];
        var moduleRoot = FindCommonSourceRoot(requestedPaths);
        foreach (var requestedPath in requestedPaths)
        {
            PowerShellCompilationPathSafety.EnsureContained(
                moduleRoot,
                requestedPath,
                $"Loose compilation source '{requestedPath}' must be contained by the common source directory '{moduleRoot}'.");
            PowerShellCompilationPathSafety.EnsureNoLinks(
                moduleRoot,
                requestedPath,
                $"Loose compilation source '{requestedPath}' traverses a symbolic link or junction.");
        }

        return new PowerShellCompilationResolvedInput(
            sourcePath,
            sourcePath,
            null,
            moduleRoot,
            Path.GetFileNameWithoutExtension(sourcePath),
            resolvedKind,
            resolvedMode,
            requestedPaths,
            requestedPaths);
    }

    private static string FindCommonSourceRoot(IReadOnlyCollection<string> sourcePaths)
    {
        var first = sourcePaths.First();
        var candidate = Path.GetDirectoryName(first) ?? Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(candidate))
        {
            var prefix = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (sourcePaths.All(path => PowerShellCompilationPathSafety.PathStartsWith(Path.GetFullPath(path), prefix)))
                return candidate!;
            candidate = Directory.GetParent(candidate)?.FullName;
        }

        throw new InvalidOperationException("Loose compilation sources must share a common filesystem root.");
    }

    /// <summary>Resolves a script, module manifest, script module, or module directory into one build input.</summary>
    public PowerShellCompilationResolvedInput Resolve(
        string path,
        PowerShellCompilationArtifactKind? kind = null,
        PowerShellCompilationMode? mode = null,
        bool allowDynamicModuleRuntimeSources = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A PowerShell script, module, manifest, or directory path is required.", nameof(path));
        if (kind.HasValue && !Enum.IsDefined(typeof(PowerShellCompilationArtifactKind), kind.Value))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (mode.HasValue && (!Enum.IsDefined(typeof(PowerShellCompilationMode), mode.Value) || mode.Value == PowerShellCompilationMode.Analyze))
            throw new ArgumentOutOfRangeException(nameof(mode), "Artifact builds accept Package, Hybrid, or Strict mode.");

        var requestedPath = Path.GetFullPath(path.Trim().Trim('"'));
        string sourcePath;
        string? manifestPath;
        string artifactName;
        if (Directory.Exists(requestedPath))
        {
            (sourcePath, manifestPath, artifactName) = ResolveDirectory(requestedPath);
        }
        else if (File.Exists(requestedPath))
        {
            (sourcePath, manifestPath, artifactName) = ResolveFile(requestedPath);
        }
        else
        {
            throw new FileNotFoundException("PowerShell compilation input was not found.", requestedPath);
        }

        var moduleRoot = Path.GetDirectoryName(manifestPath ?? sourcePath) ?? Directory.GetCurrentDirectory();
        PowerShellCompilationPathSafety.EnsureNoLinks(
            moduleRoot,
            sourcePath,
            $"PowerShell compilation source '{sourcePath}' traverses a symbolic link or junction.");
        if (manifestPath is not null)
        {
            PowerShellCompilationPathSafety.EnsureNoLinks(
                moduleRoot,
                manifestPath,
                $"PowerShell module manifest '{manifestPath}' traverses a symbolic link or junction.");
        }

        var resolvedKind = kind ?? (Path.GetExtension(sourcePath).Equals(".ps1", StringComparison.OrdinalIgnoreCase)
            ? PowerShellCompilationArtifactKind.Executable
            : PowerShellCompilationArtifactKind.BinaryModule);
        if (resolvedKind == PowerShellCompilationArtifactKind.Executable &&
            Path.GetExtension(sourcePath).Equals(".psm1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Executable compilation accepts a standalone .ps1 entrypoint. Use BinaryModule for module inputs until module-to-executable entrypoint semantics are defined.");
        }
        var resolvedMode = mode ?? PowerShellCompilationBuildSpec.GetDefaultMode(resolvedKind);
        string[] compilationSourceFiles;
        string[] sourceFiles;
        var recursiveSourceDirectories = Array.Empty<string>();
        if (resolvedKind == PowerShellCompilationArtifactKind.Executable)
        {
            compilationSourceFiles = PowerShellHybridDependencyResolver.DiscoverDependencies(sourcePath);
            sourceFiles = compilationSourceFiles;
        }
        else
        {
            var conventionalDiscovery = PowerShellConventionalModuleSourceDiscovery.Analyze(sourcePath);
            recursiveSourceDirectories = conventionalDiscovery.RecursiveSourceDirectories;
            var conventionalSources = conventionalDiscovery.SourcePaths;
            var runtimeHooks = manifestPath is null
                ? Array.Empty<string>()
                : PowerShellCompiledModuleManifest.GetContainedRuntimeScriptFiles(sourcePath, manifestPath)
                    .Select(reference => ResolveContainedModulePath(moduleRoot, reference, "runtime script hook"))
                    .ToArray();
            var runtimeHookSet = runtimeHooks.ToHashSet(PowerShellCompilationPathSafety.PathComparer);
            compilationSourceFiles = PowerShellHybridDependencyResolver.DiscoverModuleScopeDependencies(sourcePath)
                .Concat(conventionalSources)
                .Where(file => IsPowerShellSource(file))
                .Where(file => !runtimeHookSet.Contains(file))
                .OrderBy(file => FrameworkCompatibility.GetRelativePath(moduleRoot, file), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            sourceFiles = (allowDynamicModuleRuntimeSources && resolvedMode != PowerShellCompilationMode.Strict
                    ? new[] { sourcePath }
                        .Concat(runtimeHooks)
                        .Concat(conventionalSources)
                        .Distinct(PowerShellCompilationPathSafety.PathComparer)
                        .SelectMany(PowerShellHybridDependencyResolver.DiscoverModuleScopeDependencies)
                        .Distinct(PowerShellCompilationPathSafety.PathComparer)
                    : PowerShellHybridDependencyResolver.DiscoverDependencies(
                        sourcePath,
                        runtimeHooks.Concat(conventionalSources),
                        conventionalLoaders: conventionalDiscovery.Loaders))
                .Where(file => IsPowerShellSource(file))
                .OrderBy(file => FrameworkCompatibility.GetRelativePath(moduleRoot, file), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return new PowerShellCompilationResolvedInput(
            requestedPath,
            sourcePath,
            manifestPath,
            moduleRoot,
            artifactName,
            resolvedKind,
            resolvedMode,
            sourceFiles,
            compilationSourceFiles,
            recursiveSourceDirectories);
    }

    private static (string SourcePath, string? ManifestPath, string ArtifactName) ResolveFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            return (path, null, Path.GetFileNameWithoutExtension(path));
        if (extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
        {
            var siblingManifest = FindSibling(path, ".psd1");
            if (siblingManifest is not null)
            {
                var manifestRoot = ResolveManifestRoot(siblingManifest);
                if (!PowerShellCompilationPathSafety.PathEquals(manifestRoot, path))
                    throw new InvalidOperationException($"Sibling module manifest '{siblingManifest}' does not point back to selected module source '{path}'.");
            }
            return (path, siblingManifest, siblingManifest is null ? Path.GetFileNameWithoutExtension(path) : Path.GetFileNameWithoutExtension(siblingManifest));
        }
        if (extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase))
        {
            var sourcePath = ResolveManifestRoot(path);
            return (sourcePath, path, Path.GetFileNameWithoutExtension(path));
        }
        throw new ArgumentException("PowerShell compilation input must be a .ps1, .psm1, .psd1, or directory path.", nameof(path));
    }

    private static (string SourcePath, string? ManifestPath, string ArtifactName) ResolveDirectory(string directory)
    {
        var name = new DirectoryInfo(directory).Name;
        var manifests = EnumerateTopLevel(directory, ".psd1");
        var manifest = SelectNamedOrSingle(manifests, name, "module manifests", directory);
        if (manifest is not null)
            return (ResolveManifestRoot(manifest), manifest, Path.GetFileNameWithoutExtension(manifest));

        var modules = EnumerateTopLevel(directory, ".psm1");
        var module = SelectNamedOrSingle(modules, name, "script modules", directory)
            ?? throw new InvalidOperationException($"Directory '{directory}' does not contain a top-level .psd1 or .psm1 module entrypoint.");
        return (module, null, Path.GetFileNameWithoutExtension(module));
    }

    private static string ResolveManifestRoot(string manifestPath)
    {
        var moduleRoot = Path.GetDirectoryName(manifestPath) ?? Directory.GetCurrentDirectory();
        var rootModule = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "RootModule");
        if (string.IsNullOrWhiteSpace(rootModule))
        {
            var matchingModule = FindSibling(manifestPath, ".psm1");
            if (matchingModule is not null)
                return matchingModule;
            var modules = EnumerateTopLevel(moduleRoot, ".psm1");
            return SelectNamedOrSingle(modules, Path.GetFileNameWithoutExtension(manifestPath), "script modules", moduleRoot)
                ?? throw new InvalidOperationException($"Module manifest '{manifestPath}' has no literal RootModule and no unambiguous top-level .psm1 entrypoint.");
        }

        var extension = Path.GetExtension(rootModule);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Module manifest '{manifestPath}' already points to binary RootModule '{rootModule}'. Provide authored .ps1 or .psm1 source instead.");
        if (!extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Module manifest '{manifestPath}' RootModule must be a literal .psm1 path for PowerShell compilation.");
        var sourcePath = ResolveContainedModulePath(moduleRoot, rootModule!, "RootModule");
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Module manifest RootModule '{rootModule}' was not found.", sourcePath);
        if (!PowerShellCompilationPathSafety.PathEquals(Path.GetDirectoryName(sourcePath), moduleRoot))
            throw new InvalidOperationException($"Module manifest '{manifestPath}' uses nested RootModule '{rootModule}'. Direct compilation currently requires the .psm1 root beside its .psd1 manifest.");
        return sourcePath;
    }

    private static string? SelectNamedOrSingle(string[] candidates, string preferredName, string kind, string directory)
    {
        var preferred = candidates.Where(candidate => Path.GetFileNameWithoutExtension(candidate).Equals(preferredName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (preferred.Length == 1)
            return preferred[0];
        if (candidates.Length == 0)
            return null;
        if (candidates.Length == 1)
            return candidates[0];
        throw new InvalidOperationException(
            $"Directory '{directory}' contains multiple top-level {kind}; specify one explicitly: {string.Join(", ", candidates.Select(Path.GetFileName))}.");
    }

    private static string[] EnumerateTopLevel(string directory, string extension)
        => Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(file => Path.GetExtension(file).Equals(extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? FindSibling(string path, string extension)
    {
        var candidateName = Path.GetFileNameWithoutExtension(path);
        var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        return EnumerateTopLevel(directory, extension)
            .FirstOrDefault(candidate => Path.GetFileNameWithoutExtension(candidate).Equals(candidateName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveContainedModulePath(string root, string relativePath, string role)
    {
        var normalized = PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(relativePath);
        if (Path.IsPathRooted(normalized) || LooksLikeWindowsRootedPath(relativePath))
            throw new InvalidOperationException($"Module {role} '{relativePath}' must be relative to the module root.");
        var path = Path.GetFullPath(Path.Combine(root, normalized));
        PowerShellCompilationPathSafety.EnsureContained(root, path, $"Module {role} '{relativePath}' escapes the module root.");
        return path;
    }

    private static bool IsPowerShellSource(string path)
        => Path.GetExtension(path) is var extension &&
           (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) || extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeWindowsRootedPath(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal) ||
           path.StartsWith("//", StringComparison.Ordinal) ||
           path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';

}
