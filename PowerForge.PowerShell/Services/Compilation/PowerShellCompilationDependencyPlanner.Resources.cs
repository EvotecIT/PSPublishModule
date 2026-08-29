using System.Text.RegularExpressions;

namespace PowerForge;

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
        if (resourceMode == PowerShellCompilationResourceMode.CompleteModule && isModuleInput && outputRoot is not null && IsSameOrContained(outputRoot, moduleRoot))
            throw new InvalidOperationException($"CompleteModule resource mode requires an output directory that is neither the module root '{moduleRoot}' nor one of its ancestors, so authored payload is not mistaken for generated output.");
        var inventory = new List<ResourceCandidate>();
        if (isModuleInput)
        {
            foreach (var file in EnumerateContainedFiles(moduleRoot))
            {
                var fullPath = Path.GetFullPath(file);
                if (outputRoot is not null && IsSameOrContained(outputRoot, fullPath)) continue;
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
                    if (outputRoot is not null && IsSameOrContained(outputRoot, fullPath)) continue;
                    var relative = FrameworkCompatibility.GetRelativePath(moduleRoot, fullPath).Replace('\\', '/');
                    inventory.Add(new ResourceCandidate(fullPath, relative, GetConventionDiscovery(relative)));
                }
            }
            foreach (var pattern in includePatterns.Where(HasWildcards))
            {
                foreach (var file in EnumerateContainedFiles(moduleRoot).Where(file => GlobMatches(pattern, FrameworkCompatibility.GetRelativePath(moduleRoot, file).Replace('\\', '/'))))
                {
                    var fullPath = Path.GetFullPath(file);
                    if (outputRoot is not null && IsSameOrContained(outputRoot, fullPath)) continue;
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
            if (localPaths.Contains(candidate.FullPath)) continue;
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
                PowerShellCompilationDependencySelection.Inferred or PowerShellCompilationDependencySelection.PolicyInclude;
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
                     dependency.SourcePath is not null && dependency.Selection == PowerShellCompilationDependencySelection.Source))
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
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly);
                directories = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }
            foreach (var file in files)
                yield return Path.GetFullPath(file);
            foreach (var directory in directories)
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
            if (raw.StartsWith("/", StringComparison.Ordinal) || raw.StartsWith("\\", StringComparison.Ordinal) ||
                Path.IsPathRooted(raw) || LooksLikeWindowsRootedPath(raw) ||
                pattern.Split('/').Any(static segment => segment == ".."))
                throw new ArgumentException($"Resource pattern '{pattern}' must be a contained path relative to the source root.", parameterName);
            if (pattern.Length == 0 || pattern == ".")
                throw new ArgumentException("Resource patterns must identify a file, directory, or contained glob.", parameterName);
            if (!normalized.Contains(pattern, StringComparer.OrdinalIgnoreCase)) normalized.Add(pattern);
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

    private static bool HasWildcards(string pattern) => pattern.IndexOfAny(new[] { '*', '?' }) >= 0;

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
}
