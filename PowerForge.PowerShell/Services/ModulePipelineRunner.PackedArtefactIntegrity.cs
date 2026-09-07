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
        }
    }

    private static void ValidateFinalizedPackedArtefactIntegrity(ModulePipelineRunState state)
    {
        foreach (KeyValuePair<string, string> expected in state.FinalizedPackedArtefactHashes)
        {
            if (!File.Exists(expected.Key) ||
                !string.Equals(ComputeFileSha256(expected.Key), expected.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The finalized signed artefact or its evidence changed after signing: '{expected.Key}'. " +
                    "Artifact actions must not mutate signed release outputs after finalization.");
            }
        }
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
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
