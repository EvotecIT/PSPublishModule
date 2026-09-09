using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private string[] ExcludeManifestScriptsToProcess(
        string manifestPath,
        string stagingPath,
        IReadOnlyList<string> scriptFiles)
    {
        // ScriptsToProcess run before RootModule import and retain caller-session semantics.
        // Keep those hooks as delivered files instead of moving them into the merged PSM1.
        var manifestScripts = ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "ScriptsToProcess");
        if (manifestScripts.Length == 0 || scriptFiles.Count == 0)
            return scriptFiles.ToArray();

        string[] resolvedScripts = scriptFiles
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToArray();
        string[] comparisonScripts = resolvedScripts
            .Select(static path => path.Normalize(NormalizationForm.FormC))
            .ToArray();
        string[] normalizedManifestScripts = manifestScripts.ToArray();
        bool manifestChanged = false;
        var runtimeHooks = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < manifestScripts.Length; index++)
        {
            string script = manifestScripts[index];
            var resolved = ResolveManifestScriptPath(stagingPath, script);
            if (resolved is null)
                continue;
            string comparisonPath = resolved.Normalize(NormalizationForm.FormC);

            int[] exactMatchIndexes = comparisonScripts
                .Select((path, matchIndex) => new { path, matchIndex })
                .Where(match => string.Equals(match.path, comparisonPath, StringComparison.Ordinal))
                .Select(static match => match.matchIndex)
                .ToArray();
            if (exactMatchIndexes.Length > 0)
            {
                foreach (int matchIndex in exactMatchIndexes)
                    runtimeHooks.Add(resolvedScripts[matchIndex]);
                if (exactMatchIndexes.Length == 1 &&
                    !string.Equals(resolvedScripts[exactMatchIndexes[0]], resolved, StringComparison.Ordinal))
                {
                    normalizedManifestScripts[index] = FrameworkCompatibility
                        .GetRelativePath(stagingPath, resolvedScripts[exactMatchIndexes[0]])
                        .Replace('\\', '/');
                    manifestChanged = true;
                }
                continue;
            }

            // Manifests are commonly authored on Windows and then packaged on Linux. Honor a
            // casing-only or canonically equivalent Unicode mismatch when it identifies exactly
            // one staged source, but do not collapse genuinely case-distinct files.
            int[] portableMatchIndexes = comparisonScripts
                .Select((path, matchIndex) => new { path, matchIndex })
                .Where(match => string.Equals(match.path, comparisonPath, StringComparison.OrdinalIgnoreCase))
                .Select(static match => match.matchIndex)
                .ToArray();
            if (portableMatchIndexes.Length == 1)
            {
                string portableMatch = resolvedScripts[portableMatchIndexes[0]];
                runtimeHooks.Add(portableMatch);
                normalizedManifestScripts[index] = FrameworkCompatibility
                    .GetRelativePath(stagingPath, portableMatch)
                    .Replace('\\', '/');
                manifestChanged = true;
            }
        }

        if (manifestChanged &&
            !_manifestMutator.TrySetTopLevelStringArray(
                manifestPath,
                "ScriptsToProcess",
                normalizedManifestScripts))
        {
            throw new InvalidOperationException(
                $"Failed to normalize ScriptsToProcess paths in manifest '{manifestPath}'.");
        }

        return resolvedScripts
            .Where(path => !runtimeHooks.Contains(path))
            .ToArray();
    }
}
