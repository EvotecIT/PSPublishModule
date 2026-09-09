using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static void CaptureFinalizedPackedArtefactIntegrity(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        ArtefactBuildResult artefact)
    {
        if (!plan.SignModule ||
            (artefact.Type != ArtefactType.Packed &&
             artefact.Type != ArtefactType.Script &&
             artefact.Type != ArtefactType.ScriptPacked))
            return;

        foreach (string path in EnumerateFinalizedPackedArtefactPaths(artefact))
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("A finalized signed artefact or its evidence was not found.", path);
            state.FinalizedPackedArtefactHashes[path] = ComputeFileSha256(path);
            int? unixMode = ReadFinalizedArtefactUnixMode(path);
            if (unixMode.HasValue)
                state.FinalizedPackedArtefactUnixModes[path] = unixMode.Value;
        }

        foreach (string root in EnumerateFinalizedScriptLayoutRoots(artefact))
        {
            state.FinalizedPackedArtefactDirectoryInventories[root] = Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .OrderBy(static path => path, PowerShellCompilationPathSafety.PathComparer)
                .ToArray();
        }
    }

    private static void ValidateFinalizedPackedArtefactIntegrity(ModulePipelineRunState state)
    {
        foreach (KeyValuePair<string, string> expected in state.FinalizedPackedArtefactHashes)
        {
            if (!File.Exists(expected.Key) ||
                !string.Equals(ComputeFileSha256(expected.Key), expected.Value, StringComparison.OrdinalIgnoreCase) ||
                (state.FinalizedPackedArtefactUnixModes.TryGetValue(expected.Key, out int expectedMode) &&
                 ReadFinalizedArtefactUnixMode(expected.Key) != expectedMode))
            {
                throw new InvalidOperationException(
                    $"The finalized signed artefact or its evidence changed after signing: '{expected.Key}'. " +
                    "Artifact actions must not mutate signed release outputs after finalization.");
            }
        }
        foreach (KeyValuePair<string, string[]> inventory in state.FinalizedPackedArtefactDirectoryInventories)
        {
            var expectedPaths = new HashSet<string>(inventory.Value, PowerShellCompilationPathSafety.PathComparer);
            string? unexpectedPath = Directory.Exists(inventory.Key)
                ? Directory.EnumerateFiles(inventory.Key, "*", SearchOption.AllDirectories)
                    .Select(Path.GetFullPath)
                    .FirstOrDefault(path => !expectedPaths.Contains(path))
                : null;
            if (unexpectedPath is not null)
            {
                throw new InvalidOperationException(
                    $"The finalized signed artefact or its evidence changed after signing: '{unexpectedPath}'. " +
                    "Artifact actions must not mutate signed release outputs after finalization.");
            }
        }
    }

    private static int? ReadFinalizedArtefactUnixMode(string path)
    {
#if NET472
        return null;
#else
        return OperatingSystem.IsWindows()
            ? null
            : (int)File.GetUnixFileMode(path);
#endif
    }

    private static IEnumerable<string> EnumerateFinalizedPackedArtefactPaths(ArtefactBuildResult artefact)
        => new[] { artefact.OutputPath }
            .Concat(artefact.Type == ArtefactType.Script
                ? artefact.Modules
                    .Where(static module => module.IsMainModule)
                    .Select(static module => module.Path)
                : Array.Empty<string>())
            .SelectMany(static path => Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                : new[] { path })
            .Concat(artefact.EvidencePaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer);

    private static IEnumerable<string> EnumerateFinalizedScriptLayoutRoots(ArtefactBuildResult artefact)
        => artefact.Type == ArtefactType.Script
            ? new[] { artefact.OutputPath }
                .Concat(artefact.Modules
                    .Where(static module => module.IsMainModule)
                    .Select(static module => module.Path))
                .Where(Directory.Exists)
                .Select(Path.GetFullPath)
                .Distinct(PowerShellCompilationPathSafety.PathComparer)
            : Array.Empty<string>();
}
