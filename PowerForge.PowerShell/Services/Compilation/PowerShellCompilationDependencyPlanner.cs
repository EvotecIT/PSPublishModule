using System.Reflection;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Builds a deterministic, non-executing inventory of PowerShell compilation dependencies and resources.</summary>
public sealed partial class PowerShellCompilationDependencyPlanner
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
        PowerShellCompilationMode? mode = null,
        PowerShellCompilationResourceMode resourceMode = PowerShellCompilationResourceMode.Declared,
        IEnumerable<string>? includeResource = null,
        IEnumerable<string>? excludeResource = null,
        string? outputDirectory = null)
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
            outputDirectory);
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
            spec.OutputDirectory);
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

    /// <summary>Summarizes selected and unselected local resource payload.</summary>
    public static PowerShellCompilationResourceSummary SummarizeResources(IEnumerable<PowerShellCompilationDependency> dependencies)
        => PowerShellCompilationResourceSummary.Create(dependencies);

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
        string? outputDirectory)
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

    private static void CollectConfiguredPayload(
        string moduleRoot,
        string sourcePath,
        string? manifestPath,
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        PowerShellCompilationResourceMode resourceMode,
        IEnumerable<string>? includeResource,
        IEnumerable<string>? excludeResource,
        string? outputDirectory,
        IEnumerable<string> compilationGraph,
        ICollection<PowerShellCompilationDependency> results,
        ISet<string> localPaths)
    {
        var includePatterns = NormalizePatterns(includeResource, nameof(includeResource));
        var excludePatterns = NormalizePatterns(excludeResource, nameof(excludeResource));
        var isModuleInput = manifestPath is not null || Path.GetExtension(sourcePath).Equals(".psm1", StringComparison.OrdinalIgnoreCase);
        var outputRoot = string.IsNullOrWhiteSpace(outputDirectory) ? null : Path.GetFullPath(outputDirectory!);
        var inventory = new List<ResourceCandidate>();

        if (isModuleInput)
        {
            foreach (var file in EnumerateContainedFiles(moduleRoot))
            {
                var fullPath = Path.GetFullPath(file);
                if (outputRoot is not null && IsSameOrContained(outputRoot, fullPath))
                    continue;
                var relative = FrameworkCompatibility.GetRelativePath(moduleRoot, fullPath).Replace('\\', '/');
                inventory.Add(new ResourceCandidate(fullPath, relative, GetConventionDiscovery(relative)));
            }
        }
        else
        {
            var explicitRoots = includePatterns
                .Where(static pattern => !HasWildcards(pattern))
                .Select(pattern => Path.GetFullPath(Path.Combine(moduleRoot, pattern.Replace('/', Path.DirectorySeparatorChar))))
                .Where(Directory.Exists)
                .ToArray();
            foreach (var explicitFile in includePatterns
                         .Where(static pattern => !HasWildcards(pattern))
                         .Select(pattern => Path.GetFullPath(Path.Combine(moduleRoot, pattern.Replace('/', Path.DirectorySeparatorChar))))
                         .Where(File.Exists))
            {
                PowerShellCompilationPathSafety.EnsureContained(moduleRoot, explicitFile, $"Included resource '{explicitFile}' escapes the script root.");
                var relative = FrameworkCompatibility.GetRelativePath(moduleRoot, explicitFile).Replace('\\', '/');
                inventory.Add(new ResourceCandidate(explicitFile, relative, GetConventionDiscovery(relative)));
            }
            foreach (var directory in explicitRoots)
            {
                PowerShellCompilationPathSafety.EnsureContained(moduleRoot, directory, $"Included resource directory '{directory}' escapes the script root.");
                PowerShellCompilationPathSafety.EnsureNoLinks(moduleRoot, directory, $"Included resource directory '{directory}' traverses a symbolic link or junction.");
                foreach (var file in EnumerateContainedFiles(directory))
                {
                    var fullPath = Path.GetFullPath(file);
                    if (outputRoot is not null && IsSameOrContained(outputRoot, fullPath))
                        continue;
                    var relative = FrameworkCompatibility.GetRelativePath(moduleRoot, fullPath).Replace('\\', '/');
                    inventory.Add(new ResourceCandidate(fullPath, relative, GetConventionDiscovery(relative)));
                }
            }
            foreach (var pattern in includePatterns.Where(HasWildcards))
            {
                foreach (var file in EnumerateContainedFiles(moduleRoot).Where(file => GlobMatches(pattern, FrameworkCompatibility.GetRelativePath(moduleRoot, file).Replace('\\', '/'))))
                {
                    var fullPath = Path.GetFullPath(file);
                    if (outputRoot is not null && IsSameOrContained(outputRoot, fullPath))
                        continue;
                    var relative = FrameworkCompatibility.GetRelativePath(moduleRoot, fullPath).Replace('\\', '/');
                    inventory.Add(new ResourceCandidate(fullPath, relative, GetConventionDiscovery(relative)));
                }
            }
        }

        var inferredPaths = resourceMode == PowerShellCompilationResourceMode.None
            ? new HashSet<string>(PowerShellCompilationPathSafety.PathComparer)
            : DiscoverLiteralResources(compilationGraph, moduleRoot).ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        foreach (var inferred in inferredPaths)
        {
            if (inventory.All(candidate => !PowerShellCompilationPathSafety.PathEquals(candidate.FullPath, inferred)))
            {
                var relative = FrameworkCompatibility.GetRelativePath(moduleRoot, inferred).Replace('\\', '/');
                inventory.Add(new ResourceCandidate(inferred, relative, GetConventionDiscovery(relative)));
            }
        }

        ValidateCaseCollisions(inventory);
        ValidatePatternsMatched(includePatterns, inventory, "IncludeResource");
        ValidatePatternsMatched(excludePatterns, inventory, "ExcludeResource");

        foreach (var candidate in inventory
                     .GroupBy(static candidate => candidate.FullPath, PowerShellCompilationPathSafety.PathComparer)
                     .Select(static group => group.First())
                     .OrderBy(static candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (localPaths.Contains(candidate.FullPath))
                continue;
            if (File.Exists(candidate.FullPath))
                PowerShellCompilationPathSafety.EnsureNoLinks(moduleRoot, candidate.FullPath, $"Resource payload '{candidate.RelativePath}' traverses a symbolic link or junction.");
            if (outputRoot is not null && IsSameOrContained(outputRoot, candidate.FullPath))
                throw new InvalidOperationException($"Resource payload '{candidate.RelativePath}' overlaps the durable output directory '{outputRoot}'.");

            var explicitlyIncluded = includePatterns.Any(pattern => PatternMatchesCandidate(pattern, candidate));
            var explicitlyExcluded = excludePatterns.Any(pattern => PatternMatchesCandidate(pattern, candidate));
            var inferred = inferredPaths.Contains(candidate.FullPath);
            if (explicitlyIncluded && explicitlyExcluded)
                throw new InvalidOperationException($"Resource '{candidate.RelativePath}' matches both IncludeResource and ExcludeResource. Remove the conflicting pattern.");

            var selection = explicitlyIncluded
                ? PowerShellCompilationDependencySelection.ExplicitInclude
                : explicitlyExcluded
                    ? PowerShellCompilationDependencySelection.Excluded
                    : inferred && resourceMode != PowerShellCompilationResourceMode.None
                        ? PowerShellCompilationDependencySelection.Inferred
                        : resourceMode == PowerShellCompilationResourceMode.CompleteModule && isModuleInput
                            ? PowerShellCompilationDependencySelection.PolicyInclude
                            : PowerShellCompilationDependencySelection.Unclassified;
            var included = selection is PowerShellCompilationDependencySelection.ExplicitInclude or
                PowerShellCompilationDependencySelection.Inferred or
                PowerShellCompilationDependencySelection.PolicyInclude;
            var discovery = selection == PowerShellCompilationDependencySelection.ExplicitInclude
                ? PowerShellCompilationDependencyDiscovery.ExplicitResourceInclude
                : selection == PowerShellCompilationDependencySelection.Inferred
                    ? PowerShellCompilationDependencyDiscovery.InferredLiteralResource
                    : candidate.Discovery;
            AddLocal(
                results,
                localPaths,
                moduleRoot,
                candidate.FullPath,
                discovery,
                included ? GetResourceDisposition(kind, mode) : PowerShellCompilationDependencyDisposition.NotIncluded,
                GetResourceNote(selection, candidate.Discovery),
                required: inferred,
                selection);
        }

        foreach (var dependency in results.Where(static dependency =>
                     dependency.SourcePath is not null &&
                     dependency.Selection == PowerShellCompilationDependencySelection.Source))
        {
            var candidate = new ResourceCandidate(dependency.SourcePath!, dependency.RelativePath, dependency.Discovery);
            if (excludePatterns.Any(pattern => PatternMatchesCandidate(pattern, candidate)))
                throw new InvalidOperationException($"Compilation input '{dependency.RelativePath}' cannot be excluded because exclusions apply only to optional payload.");
        }

        foreach (var dependency in results.Where(static dependency =>
                     dependency.SourcePath is not null &&
                     dependency.Discovery is (PowerShellCompilationDependencyDiscovery.RootModule or
                         PowerShellCompilationDependencyDiscovery.RequiredAssemblies or
                         PowerShellCompilationDependencyDiscovery.NestedModules or
                         PowerShellCompilationDependencyDiscovery.ScriptsToProcess or
                         PowerShellCompilationDependencyDiscovery.TypesToProcess or
                         PowerShellCompilationDependencyDiscovery.FormatsToProcess or
                         PowerShellCompilationDependencyDiscovery.FileList)))
        {
            var candidate = new ResourceCandidate(dependency.SourcePath!, dependency.RelativePath, dependency.Discovery);
            if (excludePatterns.Any(pattern => PatternMatchesCandidate(pattern, candidate)))
                throw new InvalidOperationException($"Required manifest resource '{dependency.RelativePath}' cannot be excluded.");
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
                    throw new InvalidOperationException($"Resource payload directory '{directory}' is a symbolic link or junction.");
                pending.Push(directory);
            }
        }
    }

    private static string[] NormalizePatterns(IEnumerable<string>? patterns, string parameterName)
    {
        var normalized = new List<string>();
        foreach (var value in patterns ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var raw = value.Trim().Trim('"');
            var pattern = raw.Replace('\\', '/');
            if (raw.StartsWith("/", StringComparison.Ordinal) ||
                raw.StartsWith("\\", StringComparison.Ordinal) ||
                Path.IsPathRooted(raw) || LooksLikeWindowsRootedPath(raw) ||
                pattern.Split('/').Any(static segment => segment == ".."))
                throw new ArgumentException($"Resource pattern '{pattern}' must be a contained path relative to the source root.", parameterName);
            if (pattern.Length == 0 || pattern == ".")
                throw new ArgumentException("Resource patterns must identify a file, directory, or contained glob.", parameterName);
            if (!normalized.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                normalized.Add(pattern);
        }
        return normalized.ToArray();
    }

    private static void ValidatePatternsMatched(
        IEnumerable<string> patterns,
        IReadOnlyCollection<ResourceCandidate> inventory,
        string optionName)
    {
        foreach (var pattern in patterns)
        {
            if (!inventory.Any(candidate => PatternMatchesCandidate(pattern, candidate)))
                throw new InvalidOperationException($"{optionName} pattern '{pattern}' did not match any contained file or directory.");
        }
    }

    private static bool PatternMatchesCandidate(string pattern, ResourceCandidate candidate)
    {
        if (GlobMatches(pattern, candidate.RelativePath)) return true;
        if (HasWildcards(pattern)) return false;
        var directoryPrefix = pattern.TrimEnd('/') + "/";
        return candidate.RelativePath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool GlobMatches(string pattern, string relativePath)
    {
        var expression = new System.Text.StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                index++;
                if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                {
                    index++;
                    expression.Append("(?:.*/)?");
                }
                else
                {
                    expression.Append(".*");
                }
                continue;
            }
            if (character == '*')
            {
                expression.Append("[^/]*");
                continue;
            }
            if (character == '?')
            {
                expression.Append("[^/]");
                continue;
            }
            expression.Append(Regex.Escape(character.ToString()));
        }
        expression.Append('$');
        return Regex.IsMatch(relativePath, expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasWildcards(string pattern)
        => pattern.IndexOfAny(new[] { '*', '?' }) >= 0;

    private static void ValidateCaseCollisions(IEnumerable<ResourceCandidate> inventory)
    {
        var collision = inventory
            .GroupBy(static candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Select(candidate => candidate.RelativePath).Distinct(StringComparer.Ordinal).Skip(1).Any());
        if (collision is not null)
            throw new InvalidOperationException($"Resource payload contains a case-colliding path: {string.Join(", ", collision.Select(static candidate => candidate.RelativePath).Distinct(StringComparer.Ordinal))}.");
    }

    private static PowerShellCompilationDependencyDiscovery GetConventionDiscovery(string relativePath)
    {
        var first = relativePath.Split('/')[0];
        var conventional = ConventionalDirectories.FirstOrDefault(candidate => candidate.Name.Equals(first, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(conventional.Name)
            ? PowerShellCompilationDependencyDiscovery.OptionalPayload
            : conventional.Discovery;
    }

    private static string GetResourceNote(
        PowerShellCompilationDependencySelection selection,
        PowerShellCompilationDependencyDiscovery hint)
        => selection switch
        {
            PowerShellCompilationDependencySelection.ExplicitInclude => "The optional resource matched an explicit IncludeResource pattern and is delivered using the selected artifact's resource policy.",
            PowerShellCompilationDependencySelection.Inferred => "The optional resource was inferred from a contained literal PSScriptRoot path and is delivered using the selected artifact's resource policy.",
            PowerShellCompilationDependencySelection.PolicyInclude => "CompleteModule resource mode selected this contained optional file for artifact delivery.",
            PowerShellCompilationDependencySelection.Excluded => "The optional resource matched ExcludeResource and is not included.",
            _ when hint is PowerShellCompilationDependencyDiscovery.ConventionalResourceDirectory or
                PowerShellCompilationDependencyDiscovery.ConventionalLibraryDirectory or
                PowerShellCompilationDependencyDiscovery.ConventionalRuntimeDirectory =>
                "The folder name is a classification hint only. Use IncludeResource, FileList, a safe literal reference, or CompleteModule mode to include this optional file.",
            _ => "The optional file is inventoried but unclassified and is not included. Declare it with FileList or IncludeResource, or select CompleteModule mode."
        };

    private static PowerShellCompilationDependencyDisposition GetResourceDisposition(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode)
        => kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Package
            ? PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted
            : PowerShellCompilationDependencyDisposition.CopiedAdjacent;

    private static bool IsSameOrContained(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return PowerShellCompilationPathSafety.PathEquals(fullRoot, fullPath) ||
               PowerShellCompilationPathSafety.PathStartsWith(fullPath, fullRoot + Path.DirectorySeparatorChar);
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
            return IsScriptRuntimeHook(discovery, sourcePath)
                ? PowerShellCompilationDependencyDisposition.NotIncluded
                : kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Package
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
                : kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Package
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
               extension.Equals(".ps1xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWindowsRootedPath(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
           path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
}
