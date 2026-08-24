using System.Management.Automation;
using System.Management.Automation.Language;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Rewrites a sibling module manifest around generated binary and hybrid module artifacts.
/// </summary>
internal static class PowerShellCompiledModuleManifest
{
    internal static string[] GetProtectedSourceFiles(string sourcePath, bool includeHybridDependencies)
    {
        var protectedFiles = new List<string> { Path.GetFullPath(sourcePath) };
        var sourceManifest = Path.ChangeExtension(sourcePath, ".psd1");
        if (File.Exists(sourceManifest))
        {
            protectedFiles.Add(Path.GetFullPath(sourceManifest));
            protectedFiles.AddRange(ReadReferencedFileClosure(sourceManifest)
                .Select(static reference => reference.SourcePath)
                .Where(File.Exists));
        }
        if (includeHybridDependencies)
        {
            var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory();
            var runtimeHooks = GetContainedRuntimeScriptFiles(sourcePath)
                .Select(reference => Path.GetFullPath(Path.Combine(sourceRoot, reference)));
            protectedFiles.AddRange(PowerShellHybridDependencyResolver.DiscoverDependencies(sourcePath, runtimeHooks));
        }
        return protectedFiles.Distinct(GetPathComparer()).ToArray();
    }

    internal static string[] GetRuntimeScriptHooks(string sourcePath)
    {
        var sourceManifest = Path.ChangeExtension(sourcePath, ".psd1");
        if (!File.Exists(sourceManifest))
            return Array.Empty<string>();
        return CollectRuntimeScriptFiles(sourceManifest);
    }

    internal static string[] GetContainedRuntimeScriptFiles(string sourcePath)
    {
        var sourceManifest = Path.ChangeExtension(sourcePath, ".psd1");
        if (!File.Exists(sourceManifest))
            return Array.Empty<string>();

        return CollectRuntimeScriptFiles(sourceManifest);
    }

    internal static string[]? Create(
        string sourcePath,
        string moduleDirectory,
        string artifactName,
        string rootModuleFileName,
        PowerShellTypedCompilationResult typed,
        string targetFramework)
    {
        var sourceManifest = Path.ChangeExtension(sourcePath, ".psd1");
        if (!File.Exists(sourceManifest))
            return null;

        var targetManifest = Path.Combine(moduleDirectory, artifactName + ".psd1");
        File.Copy(sourceManifest, targetManifest, overwrite: true);
        var allFunctions = ReadFunctionNames(sourcePath);
        var compiled = typed.Methods.Select(static method => method.SourceName).ToArray();
        var compiledSet = compiled.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fallback = allFunctions.Where(name => !compiledSet.Contains(name)).ToArray();
        var explicitExports = PowerShellModuleExportContract.TryRead(sourcePath);
        var explicitCompiled = explicitExports?.SelectFunctions(compiled) ?? compiled;
        var explicitFallback = explicitExports?.SelectFunctions(fallback) ?? fallback;

        var manifestFunctions = ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(sourceManifest, "FunctionsToExport");
        var manifestCmdlets = ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(sourceManifest, "CmdletsToExport");
        var manifestAliases = ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(sourceManifest, "AliasesToExport");
        var manifestVariables = ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(sourceManifest, "VariablesToExport");
        var manifestFileList = ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(sourceManifest, "FileList");
        var selectedCompiled = Select(explicitCompiled, manifestFunctions);
        var explicitCmdlets = explicitExports?.Cmdlets ?? Array.Empty<string>();
        var hasNestedModules = ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(sourceManifest, "NestedModules").Any();
        var preserveWildcardFunctions = HasNestedModuleWildcardFunctionExports(sourcePath);
        var selectedFallback = preserveWildcardFunctions
            ? new[] { "*" }
            : Select(explicitFallback, manifestFunctions);
        var preserveWildcardCmdlets = hasNestedModules && manifestCmdlets?.Contains("*", StringComparer.OrdinalIgnoreCase) == true;
        var selectedSourceCmdlets = manifestCmdlets?.Contains("*", StringComparer.OrdinalIgnoreCase) == true
            ? explicitCmdlets
            : Select(explicitCmdlets, manifestCmdlets);
        var selectedCmdlets = preserveWildcardCmdlets
            ? new[] { "*" }
            : selectedSourceCmdlets
                .Concat(selectedCompiled)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var mutator = new AstModuleManifestMutator();
        if (!mutator.TrySetTopLevelString(targetManifest, "RootModule", rootModuleFileName) &&
            !string.Equals(
                ModuleManifestValueReader.ReadTopLevelString(targetManifest, "RootModule"),
                rootModuleFileName,
                GetPathComparison()))
            throw new InvalidOperationException($"Module manifest '{sourceManifest}' does not contain a literal RootModule entry that can be updated.");
        ApplyTargetCompatibility(sourceManifest, targetManifest, targetFramework, mutator);
        mutator.TrySetManifestExports(
            targetManifest,
            selectedFallback,
            selectedCmdlets,
            manifestAliases);
        if (manifestVariables is not null)
            mutator.TrySetTopLevelStringArray(targetManifest, "VariablesToExport", manifestVariables);
        if (manifestFileList is not null)
        {
            var rewrittenFileList = RewriteFileList(
                sourceManifest,
                sourcePath,
                artifactName,
                rootModuleFileName,
                manifestFileList);
            mutator.TrySetTopLevelStringArray(targetManifest, "FileList", rewrittenFileList);
        }

        var copied = new List<string> { targetManifest };
        foreach (var reference in ReadReferencedFileClosure(sourceManifest))
        {
            var sourceFile = reference.SourcePath;
            if (!File.Exists(sourceFile))
            {
                if (reference.Required)
                    throw new FileNotFoundException($"Required module manifest file reference '{reference.RelativePath}' was not found.", sourceFile);
                continue;
            }
            PowerShellCompilationPathSafety.EnsureNoLinks(
                Path.GetDirectoryName(sourceManifest)!,
                sourceFile,
                $"Module manifest file reference '{reference.RelativePath}' traverses a symbolic link or junction, which is not allowed for artifact staging.");
            if (sourceFile.Equals(sourcePath, GetPathComparison()) ||
                sourceFile.Equals(sourceManifest, GetPathComparison()))
                continue;
            var targetFile = ResolveContainedPath(moduleDirectory, reference.RelativePath);
            if (File.Exists(targetFile))
                throw new InvalidOperationException($"Module manifest file reference '{reference.RelativePath}' collides with a generated compilation artifact.");
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile) ?? moduleDirectory);
            File.Copy(sourceFile, targetFile, overwrite: false);
            copied.Add(targetFile);
        }
        return copied.ToArray();
    }

    private static string[] RewriteFileList(
        string sourceManifest,
        string sourcePath,
        string artifactName,
        string rootModuleFileName,
        IEnumerable<string> sourceEntries)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceManifest) ?? Directory.GetCurrentDirectory();
        var rewritten = new List<string>();
        foreach (var entry in sourceEntries)
        {
            var resolved = ResolveContainedPath(sourceDirectory, entry);
            if (resolved.Equals(sourcePath, GetPathComparison()))
                continue;
            if (resolved.Equals(sourceManifest, GetPathComparison()))
            {
                rewritten.Add(artifactName + ".psd1");
                continue;
            }
            rewritten.Add(entry);
        }
        rewritten.Add(rootModuleFileName);
        rewritten.Add(artifactName + ".dll");
        return rewritten.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static bool HasNestedModuleWildcardFunctionExports(string sourcePath)
    {
        var sourceManifest = Path.ChangeExtension(sourcePath, ".psd1");
        return File.Exists(sourceManifest) &&
               ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(sourceManifest, "NestedModules").Any() &&
               ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(sourceManifest, "FunctionsToExport")
                   ?.Contains("*", StringComparer.OrdinalIgnoreCase) == true;
    }

    private static void ApplyTargetCompatibility(
        string sourceManifest,
        string targetManifest,
        string targetFramework,
        AstModuleManifestMutator mutator)
    {
        var (powerShellVersion, edition) = targetFramework.ToLowerInvariant() switch
        {
            "net472" => ("5.1", "Desktop"),
            "net8.0" => ("7.4", "Core"),
            "net10.0" => ("7.6", "Core"),
            _ => throw new ArgumentException($"Unsupported compiled module target framework '{targetFramework}'.", nameof(targetFramework))
        };

        var targetVersion = Version.Parse(powerShellVersion);
        var effectiveVersion = targetVersion;
        var sourceVersionText = ModuleManifestValueReader.ReadTopLevelString(sourceManifest, "PowerShellVersion");
        if (!string.IsNullOrWhiteSpace(sourceVersionText))
        {
            if (!Version.TryParse(sourceVersionText, out var sourceVersion))
                throw new InvalidDataException($"Module manifest PowerShellVersion '{sourceVersionText}' is not a valid version.");
            if (edition.Equals("Desktop", StringComparison.OrdinalIgnoreCase) && sourceVersion > targetVersion)
            {
                throw new InvalidOperationException(
                    $"Module manifest requires PowerShell {sourceVersion}, which is newer than the {powerShellVersion} runtime supported by target framework '{targetFramework}'.");
            }
            if (sourceVersion > effectiveVersion)
                effectiveVersion = sourceVersion;
        }

        var sourceEditions = ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(sourceManifest, "CompatiblePSEditions");
        if (sourceEditions is { Length: > 0 } && !sourceEditions.Contains(edition, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Module manifest CompatiblePSEditions does not include '{edition}', which is required by target framework '{targetFramework}'.");
        }

        mutator.TrySetTopLevelString(targetManifest, "PowerShellVersion", effectiveVersion.ToString());
        mutator.TrySetTopLevelStringArray(targetManifest, "CompatiblePSEditions", new[] { edition });
    }

    private static string[] ReadFunctionNames(string sourcePath)
    {
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Module functions could not be parsed while preserving manifest exports.");
        return ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .Select(static function => function.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] Select(IEnumerable<string> names, string[]? patterns)
    {
        if (patterns is null)
            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matchers = patterns.Select(pattern => new WildcardPattern(pattern, WildcardOptions.IgnoreCase)).ToArray();
        return names.Where(name => matchers.Any(matcher => matcher.IsMatch(name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ResolvedManifestFileReference> ReadReferencedFileClosure(string manifestPath)
    {
        var rootManifest = Path.GetFullPath(manifestPath);
        var rootDirectory = Path.GetDirectoryName(rootManifest) ?? Directory.GetCurrentDirectory();
        var pending = new Stack<(string ManifestPath, bool IsRoot)>();
        var visitedManifests = new HashSet<string>(GetPathComparer());
        var references = new Dictionary<string, ResolvedManifestFileReference>(GetPathComparer());
        pending.Push((rootManifest, true));

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visitedManifests.Add(current.ManifestPath))
                continue;

            var currentDirectory = Path.GetDirectoryName(current.ManifestPath) ?? rootDirectory;
            foreach (var reference in ReadReferencedFiles(current.ManifestPath, includeRootModule: !current.IsRoot))
            {
                var sourceFile = ResolveContainedPath(rootDirectory, currentDirectory, reference.Path);
                var relativePath = NormalizeManifestRelativePath(FrameworkCompatibility.GetRelativePath(rootDirectory, sourceFile));
                if (references.TryGetValue(sourceFile, out var existing))
                {
                    if (reference.Required && !existing.Required)
                        references[sourceFile] = new ResolvedManifestFileReference(sourceFile, relativePath, required: true);
                }
                else
                {
                    references.Add(sourceFile, new ResolvedManifestFileReference(sourceFile, relativePath, reference.Required));
                }

                if (File.Exists(sourceFile) && Path.GetExtension(sourceFile).Equals(".psd1", StringComparison.OrdinalIgnoreCase))
                    pending.Push((sourceFile, false));
            }
        }

        return references.Values
            .OrderBy(static reference => reference.RelativePath, GetPathComparer())
            .ToArray();
    }

    private static string[] CollectRuntimeScriptFiles(string manifestPath)
    {
        var rootManifest = Path.GetFullPath(manifestPath);
        var rootDirectory = Path.GetDirectoryName(rootManifest) ?? Directory.GetCurrentDirectory();
        var scripts = new HashSet<string>(GetPathComparer());
        var visited = new HashSet<string>(GetPathComparer());
        CollectRuntimeScriptFiles(rootManifest, rootDirectory, includeRootModule: false, scripts, visited);
        return scripts.OrderBy(static path => path, GetPathComparer()).ToArray();
    }

    private static void CollectRuntimeScriptFiles(
        string manifestPath,
        string rootDirectory,
        bool includeRootModule,
        ISet<string> scripts,
        ISet<string> visited)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        if (!visited.Add(manifestPath))
            return;

        var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? rootDirectory;
        if (includeRootModule)
        {
            var rootModule = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "RootModule");
            if (string.IsNullOrWhiteSpace(rootModule))
            {
                scripts.Add(NormalizeManifestRelativePath(FrameworkCompatibility.GetRelativePath(rootDirectory, manifestPath)));
            }
            else
            {
                AddRuntimeModuleReference(rootModule!, manifestDirectory, rootDirectory, scripts, visited);
            }
        }

        foreach (var script in ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(manifestPath, "ScriptsToProcess") ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(script))
                continue;
            var sourceFile = ResolveContainedPath(rootDirectory, manifestDirectory, script);
            scripts.Add(NormalizeManifestRelativePath(FrameworkCompatibility.GetRelativePath(rootDirectory, sourceFile)));
        }
        foreach (var nestedModule in ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, "NestedModules"))
            AddRuntimeModuleReference(nestedModule, manifestDirectory, rootDirectory, scripts, visited);
    }

    private static void AddRuntimeModuleReference(
        string reference,
        string manifestDirectory,
        string rootDirectory,
        ISet<string> scripts,
        ISet<string> visited)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return;
        var extension = Path.GetExtension(reference);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            return;

        var sourceFile = ResolveContainedPath(rootDirectory, manifestDirectory, reference);
        var relativePath = NormalizeManifestRelativePath(FrameworkCompatibility.GetRelativePath(rootDirectory, sourceFile));
        if (extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase) && File.Exists(sourceFile))
        {
            CollectRuntimeScriptFiles(sourceFile, rootDirectory, includeRootModule: true, scripts, visited);
            return;
        }
        scripts.Add(relativePath);
    }

    private static IEnumerable<ManifestFileReference> ReadReferencedFiles(string manifestPath, bool includeRootModule = false)
    {
        if (includeRootModule)
        {
            var rootModule = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "RootModule");
            if (!string.IsNullOrWhiteSpace(rootModule))
                yield return new ManifestFileReference(rootModule!, required: true);
        }
        foreach (var key in new[] { "FormatsToProcess", "TypesToProcess", "ScriptsToProcess" })
        foreach (var value in ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, key))
            yield return new ManifestFileReference(value, required: true);
        foreach (var value in ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "RequiredAssemblies"))
            yield return new ManifestFileReference(value, IsContainedModulePath(value));
        foreach (var value in ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "FileList"))
            yield return new ManifestFileReference(value, required: false);
        foreach (var value in ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, "NestedModules"))
            yield return new ManifestFileReference(value, IsContainedModulePath(value));
    }

    private static bool IsContainedModulePath(string value)
    {
        var extension = Path.GetExtension(value);
        return value.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
               value.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
               value.IndexOf('\\') >= 0 ||
               value.StartsWith(".", StringComparison.Ordinal) ||
               extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var normalizedPath = NormalizeManifestRelativePath(relativePath);
        if (Path.IsPathRooted(normalizedPath) || LooksLikeWindowsRootedPath(relativePath))
            throw new InvalidOperationException($"Module manifest file reference '{relativePath}' must remain relative to the module root.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, normalizedPath));
        if (!fullPath.StartsWith(fullRoot, GetPathComparison()))
            throw new InvalidOperationException($"Module manifest file reference '{relativePath}' escapes the module root.");
        return fullPath;
    }

    private static string ResolveContainedPath(string root, string baseDirectory, string relativePath)
    {
        var normalizedPath = NormalizeManifestRelativePath(relativePath);
        if (Path.IsPathRooted(normalizedPath) || LooksLikeWindowsRootedPath(relativePath))
            throw new InvalidOperationException($"Module manifest file reference '{relativePath}' must remain relative to the module root.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, normalizedPath));
        if (!fullPath.StartsWith(fullRoot, GetPathComparison()))
            throw new InvalidOperationException($"Module manifest file reference '{relativePath}' escapes the module root.");
        return fullPath;
    }

    internal static string NormalizeManifestRelativePath(string path)
        => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static bool LooksLikeWindowsRootedPath(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal) ||
           path.StartsWith("//", StringComparison.Ordinal) ||
           path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';

    private static StringComparison GetPathComparison()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer GetPathComparer()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed class ManifestFileReference
    {
        internal ManifestFileReference(string path, bool required)
        {
            Path = path;
            Required = required;
        }

        internal string Path { get; }
        internal bool Required { get; }
    }

    private sealed class ResolvedManifestFileReference
    {
        internal ResolvedManifestFileReference(string sourcePath, string relativePath, bool required)
        {
            SourcePath = sourcePath;
            RelativePath = relativePath;
            Required = required;
        }

        internal string SourcePath { get; }
        internal string RelativePath { get; }
        internal bool Required { get; }
    }
}
