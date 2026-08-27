using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>Describes functions and aliases contributed by retained nested modules.</summary>
internal sealed class NestedModuleExportAnalysis
{
    internal static NestedModuleExportAnalysis CompleteEmpty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        isFunctionComplete: true,
        isAliasComplete: true);

    internal NestedModuleExportAnalysis(
        IEnumerable<string> functions,
        IEnumerable<string> aliases,
        bool isFunctionComplete,
        bool isAliasComplete)
    {
        Functions = (functions ?? Array.Empty<string>()).ToArray();
        Aliases = (aliases ?? Array.Empty<string>()).ToArray();
        IsFunctionComplete = isFunctionComplete;
        IsAliasComplete = isAliasComplete;
    }

    internal string[] Functions { get; }
    internal string[] Aliases { get; }
    internal bool IsFunctionComplete { get; }
    internal bool IsAliasComplete { get; }
}

/// <summary>Inspects local nested-module references so root replacement preserves shared exports.</summary>
internal sealed class NestedModuleExportDetector
{
    private readonly IScriptFunctionExportDetector _scriptFunctionExportDetector;

    internal NestedModuleExportDetector(IScriptFunctionExportDetector scriptFunctionExportDetector)
    {
        _scriptFunctionExportDetector = scriptFunctionExportDetector ??
                                        throw new ArgumentNullException(nameof(scriptFunctionExportDetector));
    }

    internal NestedModuleExportAnalysis Analyze(
        string projectRoot,
        IEnumerable<string> nestedModuleReferences)
    {
        var functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isFunctionComplete = true;
        var isAliasComplete = true;
        foreach (var reference in nestedModuleReferences ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                isFunctionComplete = false;
                isAliasComplete = false;
                continue;
            }

            try
            {
                var path = Path.IsPathRooted(reference)
                    ? Path.GetFullPath(reference)
                    : Path.GetFullPath(Path.Combine(projectRoot, reference));
                var extension = Path.GetExtension(path);
                if (string.Equals(extension, ".psm1", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(path))
                    {
                        isFunctionComplete = false;
                        isAliasComplete = false;
                        continue;
                    }

                    foreach (var function in _scriptFunctionExportDetector.DetectScriptFunctions(new[] { path }))
                        functions.Add(function);

                    if (_scriptFunctionExportDetector is IScriptAliasExportAnalysisDetector analysisDetector)
                    {
                        var analysis = analysisDetector.AnalyzeScriptAliases(new[] { path });
                        foreach (var alias in analysis.Aliases)
                            aliases.Add(alias);
                        if (!analysis.IsComplete)
                        {
                            isFunctionComplete = false;
                            isAliasComplete = false;
                        }
                    }
                    else
                    {
                        isFunctionComplete = false;
                        isAliasComplete = false;
                    }

                    if (_scriptFunctionExportDetector is IScriptAliasExternalSourceDetector externalSourceDetector &&
                        externalSourceDetector.HasModuleScopeDotSources(new[] { path }))
                    {
                        isFunctionComplete = false;
                        isAliasComplete = false;
                    }
                    continue;
                }

                if (string.Equals(extension, ".psd1", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(path))
                    {
                        isFunctionComplete = false;
                        isAliasComplete = false;
                        continue;
                    }

                    AddManifestExports(path, "FunctionsToExport", functions, ref isFunctionComplete);
                    AddManifestExports(path, "AliasesToExport", aliases, ref isAliasComplete);
                    continue;
                }

                if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    try
                    {
                        foreach (var alias in BinaryExportDetector.DetectBinaryAliases(new[] { path }))
                            aliases.Add(alias);
                    }
                    catch
                    {
                        isAliasComplete = false;
                    }
                    continue;
                }

                if (string.Equals(extension, ".cdxml", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    continue;

                isFunctionComplete = false;
                isAliasComplete = false;
            }
            catch
            {
                isFunctionComplete = false;
                isAliasComplete = false;
            }
        }

        return new NestedModuleExportAnalysis(
            functions,
            aliases,
            isFunctionComplete,
            isAliasComplete);
    }

    private static void AddManifestExports(
        string manifestPath,
        string key,
        ISet<string> exports,
        ref bool isComplete)
    {
        var declaredExports = ModuleManifestValueReader.ReadTopLevelLiteralStringOrArray(manifestPath, key);
        if (declaredExports is null || declaredExports.Any(ContainsWildcardCharacters))
        {
            isComplete = false;
            return;
        }

        foreach (var export in declaredExports)
            exports.Add(export);
    }

    private static bool ContainsWildcardCharacters(string value)
        => !string.IsNullOrEmpty(value) && value.IndexOfAny(new[] { '*', '?', '[' }) >= 0;
}
