using System.Management.Automation;
using System.Management.Automation.Language;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Rewrites a sibling module manifest around generated binary and hybrid module artifacts.
/// </summary>
internal static class PowerShellCompiledModuleManifest
{
    internal static string[] GetRuntimeScriptHooks(string sourcePath)
    {
        var sourceManifest = Path.ChangeExtension(sourcePath, ".psd1");
        if (!File.Exists(sourceManifest))
            return Array.Empty<string>();

        var hooks = new List<string>();
        hooks.AddRange(ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(sourceManifest, "ScriptsToProcess") ?? Array.Empty<string>());
        hooks.AddRange(ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(sourceManifest, "NestedModules")
            .Where(static reference => !Path.GetExtension(reference).Equals(".dll", StringComparison.OrdinalIgnoreCase)));
        return hooks
            .Where(static reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(GetPathComparer())
            .ToArray();
    }

    internal static string[]? Create(
        string sourcePath,
        string moduleDirectory,
        string artifactName,
        string rootModuleFileName,
        PowerShellTypedCompilationResult typed)
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
        var selectedCompiled = Select(explicitCompiled, manifestFunctions);
        var selectedFallback = Select(explicitFallback, manifestFunctions);
        var explicitCmdlets = explicitExports?.Cmdlets ?? Array.Empty<string>();
        var selectedSourceCmdlets = manifestCmdlets?.Contains("*", StringComparer.OrdinalIgnoreCase) == true
            ? explicitCmdlets
            : Select(explicitCmdlets, manifestCmdlets);
        var selectedCmdlets = selectedSourceCmdlets
            .Concat(selectedCompiled)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mutator = new AstModuleManifestMutator();
        if (!mutator.TrySetTopLevelString(targetManifest, "RootModule", rootModuleFileName))
            throw new InvalidOperationException($"Module manifest '{sourceManifest}' does not contain a literal RootModule entry that can be updated.");
        mutator.TrySetManifestExports(
            targetManifest,
            selectedFallback,
            selectedCmdlets,
            manifestAliases);
        if (manifestVariables is not null)
            mutator.TrySetTopLevelStringArray(targetManifest, "VariablesToExport", manifestVariables);

        var copied = new List<string> { targetManifest };
        foreach (var reference in ReadReferencedFiles(sourceManifest).Distinct(GetPathComparer()))
        {
            var sourceFile = ResolveContainedPath(Path.GetDirectoryName(sourceManifest)!, reference);
            if (!File.Exists(sourceFile))
                continue;
            if (sourceFile.Equals(sourcePath, GetPathComparison()) ||
                sourceFile.Equals(sourceManifest, GetPathComparison()))
                continue;
            var targetFile = ResolveContainedPath(moduleDirectory, reference);
            if (File.Exists(targetFile))
                throw new InvalidOperationException($"Module manifest file reference '{reference}' collides with a generated compilation artifact.");
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile) ?? moduleDirectory);
            File.Copy(sourceFile, targetFile, overwrite: false);
            copied.Add(targetFile);
        }
        return copied.ToArray();
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

    private static IEnumerable<string> ReadReferencedFiles(string manifestPath)
    {
        foreach (var key in new[] { "FormatsToProcess", "TypesToProcess", "ScriptsToProcess", "RequiredAssemblies", "FileList" })
        foreach (var value in ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, key))
            yield return value;
        foreach (var value in ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, "NestedModules"))
            yield return value;
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException($"Module manifest file reference '{relativePath}' must remain relative to the module root.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(fullRoot, GetPathComparison()))
            throw new InvalidOperationException($"Module manifest file reference '{relativePath}' escapes the module root.");
        return fullPath;
    }

    private static StringComparison GetPathComparison()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer GetPathComparer()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
