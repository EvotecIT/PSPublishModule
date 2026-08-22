using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>Describes functions that retained nested modules can export and whether discovery was exhaustive.</summary>
internal sealed class NestedModuleFunctionExportAnalysis
{
    internal NestedModuleFunctionExportAnalysis(IEnumerable<string> functions, bool isComplete)
    {
        Functions = (functions ?? Array.Empty<string>()).ToArray();
        IsComplete = isComplete;
    }

    internal string[] Functions { get; }
    internal bool IsComplete { get; }
}

/// <summary>Inspects local nested-module references so root-script replacement preserves shared function exports.</summary>
internal sealed class NestedModuleFunctionExportDetector
{
    private readonly IScriptFunctionExportDetector _scriptFunctionExportDetector;

    internal NestedModuleFunctionExportDetector(IScriptFunctionExportDetector scriptFunctionExportDetector)
    {
        _scriptFunctionExportDetector = scriptFunctionExportDetector ??
                                        throw new ArgumentNullException(nameof(scriptFunctionExportDetector));
    }

    internal NestedModuleFunctionExportAnalysis Analyze(
        string projectRoot,
        IEnumerable<string> nestedModuleReferences)
    {
        var functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isComplete = true;
        foreach (var reference in nestedModuleReferences ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                isComplete = false;
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
                        isComplete = false;
                        continue;
                    }

                    foreach (var function in _scriptFunctionExportDetector.DetectScriptFunctions(new[] { path }))
                        functions.Add(function);

                    if (_scriptFunctionExportDetector is not IScriptAliasExportAnalysisDetector analysisDetector ||
                        !analysisDetector.AnalyzeScriptAliases(new[] { path }).IsComplete ||
                        (_scriptFunctionExportDetector is IScriptAliasExternalSourceDetector externalSourceDetector &&
                         externalSourceDetector.HasModuleScopeDotSources(new[] { path })))
                    {
                        isComplete = false;
                    }
                    continue;
                }

                if (string.Equals(extension, ".psd1", StringComparison.OrdinalIgnoreCase))
                {
                    var manifestFunctions = ModuleManifestValueReader.ReadTopLevelLiteralStringOrArray(
                        path,
                        "FunctionsToExport");
                    if (!File.Exists(path) ||
                        manifestFunctions is null ||
                        manifestFunctions.Any(ContainsWildcardCharacters))
                    {
                        isComplete = false;
                        continue;
                    }

                    foreach (var function in manifestFunctions)
                        functions.Add(function);
                    continue;
                }

                if ((string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(extension, ".cdxml", StringComparison.OrdinalIgnoreCase)) &&
                    File.Exists(path))
                {
                    continue;
                }

                isComplete = false;
            }
            catch
            {
                isComplete = false;
            }
        }

        return new NestedModuleFunctionExportAnalysis(functions, isComplete);
    }

    private static bool ContainsWildcardCharacters(string value)
        => !string.IsNullOrEmpty(value) && value.IndexOfAny(new[] { '*', '?', '[' }) >= 0;
}
