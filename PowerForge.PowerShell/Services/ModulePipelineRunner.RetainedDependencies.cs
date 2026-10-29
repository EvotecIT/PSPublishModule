using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation.Language;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    /// <summary>
    /// Keeps dependencies referenced by manifest-loaded files that remain separate from the merged
    /// root module. Their functions are not inlined into those runtime hooks or nested modules.
    /// </summary>
    private string[] ResolveApprovedModulesSatisfiedByFinalPayload(
        ModulePipelinePlan plan,
        ModuleBuildResult result,
        string mergedRootPath,
        string[] satisfiedByRoot)
    {
        if (satisfiedByRoot.Length == 0)
            return satisfiedByRoot;

        var candidates = new HashSet<string>(satisfiedByRoot, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(FrameworkCompatibility.PathComparer);
        var manifests = new Stack<string>();
        manifests.Push(result.ManifestPath);
        while (manifests.Count > 0 && candidates.Count > 0)
        {
            string manifest = manifests.Pop();
            if (!visited.Add(manifest))
                continue;
            string manifestText = File.ReadAllText(manifest);
            foreach (string property in ModuleManifestLoadedContent.PropertyNames)
            {
                if (!TryReadStaticManifestModuleReferences(manifestText, property, out _))
                    return Array.Empty<string>();
            }
            if (!FrameworkCompatibility.PathComparer.Equals(manifest, result.ManifestPath))
            {
                // A nested manifest can explicitly import a donor even when its own code is empty.
                if (!TryReadStaticManifestModuleReferences(manifestText, "RequiredModules", out string[] requirements))
                    return Array.Empty<string>();
                candidates.ExceptWith(requirements);
            }
            string[] references;
            try
            {
                // Preserve case variants until they are resolved to actual filesystem paths.
                references = ModuleManifestLoadedContent.ReadRelativePaths(manifest, StringComparer.Ordinal);
            }
            catch (InvalidDataException)
            {
                // Computed nested-module references cannot prove any donor unused.
                return Array.Empty<string>();
            }
            foreach (string reference in references)
            {
                string? path = ResolveManifestScriptPath(Path.GetDirectoryName(manifest)!, reference);
                if (path is null || !IsSameOrChildPath(result.StagingPath, path) || !File.Exists(path))
                    return Array.Empty<string>();
                if (FrameworkCompatibility.PathComparer.Equals(path, mergedRootPath) || visited.Contains(path))
                    continue;

                string extension = Path.GetExtension(path);
                if (extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase))
                {
                    manifests.Push(path);
                    continue;
                }
                string? code = null;
                if (extension.Equals(".ps1xml", StringComparison.OrdinalIgnoreCase))
                {
                    // Type and format assets can carry executable PowerShell, unlike property-only views.
                    try
                    {
                        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                        code = string.Join(Environment.NewLine, XDocument.Load(reader).Descendants()
                            .Where(element => element.Name.LocalName is "ScriptBlock" or "GetScriptBlock" or "SetScriptBlock" or "Script")
                            .Select(element => element.Value));
                    }
                    catch (XmlException)
                    {
                        return Array.Empty<string>();
                    }
                    if (string.IsNullOrWhiteSpace(code))
                        continue;
                }
                else if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) &&
                         !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                    continue;
                visited.Add(path);
                var analysis = AnalyzeMissingFunctions(code is null ? path : null, code, plan, Array.Empty<string>());
                if (analysis is null || analysis.Summary.Any(command => string.IsNullOrWhiteSpace(command.Source)))
                    return Array.Empty<string>();
                candidates.IntersectWith(analysis.FullyInlinedApprovedModules);
                // Even inlineable donor functions remain external when this file itself is retained.
                candidates.ExceptWith(analysis.Summary.Select(command => command.Source));
            }
        }
        return candidates.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryReadStaticManifestModuleReferences(string manifestText, string property, out string[] names)
    {
        names = Array.Empty<string>();
        if (!ModuleManifestTextParser.TryReadTopLevelAssignedExpressionByKey(manifestText, property, out string? expression))
            return true;
        var ast = Parser.ParseInput(expression!, out _, out ParseError[] errors);
        return errors.Length == 0 && ast.EndBlock?.Statements.Count == 1 &&
               ApprovedModuleRuntimeReferenceAnalyzer.TryReadStaticModuleNames(ast.EndBlock.Statements[0], out names);
    }
}
