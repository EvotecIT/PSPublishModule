using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        string[] normalizedManifestScripts = manifestScripts.ToArray();
        bool manifestChanged = false;
        var runtimeHooks = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < manifestScripts.Length; index++)
        {
            string script = manifestScripts[index];
            var resolved = ResolveManifestScriptPath(stagingPath, script);
            if (resolved is null)
                continue;

            string[] exactMatches = resolvedScripts
                .Where(path => string.Equals(path, resolved, StringComparison.Ordinal))
                .ToArray();
            if (exactMatches.Length > 0)
            {
                foreach (string match in exactMatches)
                    runtimeHooks.Add(match);
                continue;
            }

            // Manifests are commonly authored on Windows and then packaged on Linux. Honor a
            // casing-only mismatch when it identifies exactly one staged source, but do not
            // collapse genuinely case-distinct files on a case-sensitive filesystem.
            string[] portableMatches = resolvedScripts
                .Where(path => string.Equals(path, resolved, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (portableMatches.Length == 1)
            {
                runtimeHooks.Add(portableMatches[0]);
                normalizedManifestScripts[index] = FrameworkCompatibility
                    .GetRelativePath(stagingPath, portableMatches[0])
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
