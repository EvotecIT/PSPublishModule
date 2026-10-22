using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        if (!plan.MergeMissing || satisfiedByRoot.Length == 0)
            return satisfiedByRoot;

        var candidates = new HashSet<string>(satisfiedByRoot, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifests = new Stack<string>();
        manifests.Push(result.ManifestPath);
        while (manifests.Count > 0 && candidates.Count > 0)
        {
            string manifest = manifests.Pop();
            if (!visited.Add(manifest))
                continue;
            if (!string.Equals(manifest, result.ManifestPath, StringComparison.OrdinalIgnoreCase))
            {
                // A nested manifest can explicitly import a donor even when its own code is empty.
                candidates.ExceptWith(ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifest, "RequiredModules"));
            }
            foreach (string reference in ModuleManifestLoadedContent.ReadRelativePaths(manifest))
            {
                string? path = ResolveManifestScriptPath(Path.GetDirectoryName(manifest)!, reference);
                if (path is null || !IsSameOrChildPath(result.StagingPath, path) || !File.Exists(path))
                    return Array.Empty<string>();
                if (string.Equals(path, mergedRootPath, StringComparison.OrdinalIgnoreCase) || visited.Contains(path))
                    continue;

                string extension = Path.GetExtension(path);
                if (extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase))
                {
                    manifests.Push(path);
                    continue;
                }
                // Binary loaders and XML type/format scripts have no complete PowerShell AST here.
                if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<string>();
                visited.Add(path);
                var analysis = AnalyzeMissingFunctions(path, null, plan, Array.Empty<string>());
                if (analysis is null || analysis.Summary.Any(command => string.IsNullOrWhiteSpace(command.Source)))
                    return Array.Empty<string>();
                candidates.IntersectWith(analysis.FullyInlinedApprovedModules);
                // Even inlineable donor functions remain external when this file itself is retained.
                candidates.ExceptWith(analysis.Summary.Select(command => command.Source));
            }
        }
        return candidates.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
