using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void ValidateDeliveredArtefactIntegrity(ModulePipelinePlan plan, ModulePipelineRunState state)
    {
        ValidateFinalizedPackedArtefactIntegrity(state, plan.SignModule);
        foreach (ArtefactBuildResult artefact in state.ArtefactResults)
        {
            if (artefact.Type is not (ArtefactType.Unpacked or ArtefactType.Script))
                continue;

            foreach (ArtefactModuleEntry module in artefact.Modules.Where(static module => module.IsMainModule))
                ValidateDeliveredBinaryDependencies(
                    plan,
                    module.Path,
                    artefact.Type == ArtefactType.Script ? state.RequireBuildResult().ManifestPath : null);
        }
    }

    private static void CaptureFinalizedPackedArtefactIntegrity(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        ArtefactBuildResult artefact)
    {
        foreach (string path in EnumerateFinalizedPackedArtefactPaths(artefact))
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("A finalized artefact or its evidence was not found.", path);
            state.FinalizedPackedArtefactHashes[path] = ComputeFileSha256(path);
            int? unixMode = ReadFinalizedArtefactUnixMode(path);
            if (unixMode.HasValue)
                state.FinalizedPackedArtefactUnixModes[path] = unixMode.Value;
        }

        foreach (string root in EnumerateFinalizedLooseArtefactRoots(artefact))
        {
            state.FinalizedLooseArtefactFileInventories[root] = Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .OrderBy(static path => path, PowerShellCompilationPathSafety.PathComparer)
                .ToArray();
            state.FinalizedLooseArtefactDirectoryInventories[root] = Directory
                .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .OrderBy(static path => path, PowerShellCompilationPathSafety.PathComparer)
                .ToArray();
            foreach (string directory in new[] { root }.Concat(state.FinalizedLooseArtefactDirectoryInventories[root]))
            {
                int? unixMode = ReadFinalizedArtefactUnixMode(directory);
                if (unixMode.HasValue)
                    state.FinalizedLooseArtefactDirectoryUnixModes[directory] = unixMode.Value;
            }
        }
    }

    private static void RefreshFinalizedReleasePayloadIntegrity(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        CaptureFinalizedModulePayloadIntegrity(state);
        RefreshFinalizedArtefactIntegrity(plan, state);
    }

    private static void RefreshFinalizedArtefactIntegrity(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        state.FinalizedPackedArtefactHashes.Clear();
        state.FinalizedPackedArtefactUnixModes.Clear();
        state.FinalizedLooseArtefactFileInventories.Clear();
        state.FinalizedLooseArtefactDirectoryInventories.Clear();
        state.FinalizedLooseArtefactDirectoryUnixModes.Clear();
        foreach (ArtefactBuildResult artefact in state.ArtefactResults)
            CaptureFinalizedPackedArtefactIntegrity(plan, state, artefact);
    }

    private static void ValidateFinalizedPackedArtefactIntegrity(ModulePipelineRunState state, bool signed)
    {
        foreach (KeyValuePair<string, string> expected in state.FinalizedPackedArtefactHashes)
        {
            if (!File.Exists(expected.Key) ||
                !string.Equals(ComputeFileSha256(expected.Key), expected.Value, StringComparison.OrdinalIgnoreCase) ||
                (state.FinalizedPackedArtefactUnixModes.TryGetValue(expected.Key, out int expectedMode) &&
                 ReadFinalizedArtefactUnixMode(expected.Key) != expectedMode))
            {
                ThrowFinalizedArtefactChanged(expected.Key, signed);
            }
        }
        foreach (KeyValuePair<string, string[]> inventory in state.FinalizedLooseArtefactFileInventories)
        {
            if (!Directory.Exists(inventory.Key))
                ThrowFinalizedArtefactChanged(inventory.Key, signed);

            var expectedPaths = new HashSet<string>(inventory.Value, PowerShellCompilationPathSafety.PathComparer);
            var actualPaths = new HashSet<string>(
                Directory.EnumerateFiles(inventory.Key, "*", SearchOption.AllDirectories).Select(Path.GetFullPath),
                PowerShellCompilationPathSafety.PathComparer);
            string? changedPath = actualPaths.FirstOrDefault(path => !expectedPaths.Contains(path)) ??
                                  expectedPaths.FirstOrDefault(path => !actualPaths.Contains(path));
            if (changedPath is not null)
                ThrowFinalizedArtefactChanged(changedPath, signed);
        }
        foreach (KeyValuePair<string, string[]> inventory in state.FinalizedLooseArtefactDirectoryInventories)
        {
            if (!Directory.Exists(inventory.Key))
                ThrowFinalizedArtefactChanged(inventory.Key, signed);

            var expectedPaths = new HashSet<string>(inventory.Value, PowerShellCompilationPathSafety.PathComparer);
            var actualPaths = new HashSet<string>(
                Directory.EnumerateDirectories(inventory.Key, "*", SearchOption.AllDirectories).Select(Path.GetFullPath),
                PowerShellCompilationPathSafety.PathComparer);
            string? changedPath = actualPaths.FirstOrDefault(path => !expectedPaths.Contains(path)) ??
                                  expectedPaths.FirstOrDefault(path => !actualPaths.Contains(path));
            if (changedPath is not null)
                ThrowFinalizedArtefactChanged(changedPath, signed);
        }
        foreach (KeyValuePair<string, int> expected in state.FinalizedLooseArtefactDirectoryUnixModes)
        {
            if (!Directory.Exists(expected.Key) || ReadFinalizedArtefactUnixMode(expected.Key) != expected.Value)
                ThrowFinalizedArtefactChanged(expected.Key, signed);
        }
    }

    private static void ValidateFinalizedOwnedArtefactIntegrity(ModulePipelineRunState state, bool signed)
    {
        foreach (ArtefactBuildResult artefact in state.ArtefactResults)
        {
            // The loose output root may contain later trusted siblings. Module, copy,
            // archive, and evidence paths belong to this artefact and must not change.
            var ownedPaths = EnumerateOwnedArtefactPaths(artefact)
                .Select(Path.GetFullPath)
                .Distinct(PowerShellCompilationPathSafety.PathComparer);

            foreach (string path in ownedPaths)
            {
                if (Directory.Exists(path))
                {
                    var expectedFiles = new HashSet<string>(state.FinalizedLooseArtefactFileInventories.Values
                        .SelectMany(static inventory => inventory)
                        .Where(file => IsSameOrChildPath(path, file)), PowerShellCompilationPathSafety.PathComparer);
                    var actualFiles = new HashSet<string>(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                        .Select(Path.GetFullPath), PowerShellCompilationPathSafety.PathComparer);
                    if (!expectedFiles.SetEquals(actualFiles))
                        ThrowFinalizedArtefactChanged(path, signed);

                    var expectedDirectories = new HashSet<string>(state.FinalizedLooseArtefactDirectoryInventories.Values
                        .SelectMany(static inventory => inventory)
                        .Where(directory => IsSameOrChildPath(path, directory) &&
                                            !PowerShellCompilationPathSafety.PathComparer.Equals(path, directory)),
                        PowerShellCompilationPathSafety.PathComparer);
                    var actualDirectories = new HashSet<string>(Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                        .Select(Path.GetFullPath), PowerShellCompilationPathSafety.PathComparer);
                    if (!expectedDirectories.SetEquals(actualDirectories))
                        ThrowFinalizedArtefactChanged(path, signed);

                    foreach (string file in expectedFiles)
                        ValidateOwnedArtefactFile(state, file, signed);
                    foreach (string directory in new[] { path }.Concat(expectedDirectories))
                    {
                        if (state.FinalizedLooseArtefactDirectoryUnixModes.TryGetValue(directory, out int expectedMode) &&
                            ReadFinalizedArtefactUnixMode(directory) != expectedMode)
                            ThrowFinalizedArtefactChanged(directory, signed);
                    }
                }
                else
                {
                    ValidateOwnedArtefactFile(state, path, signed);
                }
            }
        }
    }

    private static void ValidateInstallArtefactPathConflicts(ModulePipelinePlan plan, ModulePipelineRunState state)
    {
        var ownedPaths = state.ArtefactResults.SelectMany(EnumerateOwnedArtefactPaths)
            .Select(Path.GetFullPath)
            .ToArray();

        foreach (string root in plan.InstallRoots)
        {
            string installedModuleParent = Path.GetFullPath(Path.Combine(root, plan.ModuleName));
            if (ownedPaths.Any(path => IsSameOrChildPath(path, installedModuleParent) ||
                                       IsSameOrChildPath(installedModuleParent, path)))
                throw new InvalidOperationException(
                    $"Install root '{root}' overlaps a finalized artefact output. Choose a sibling install root.");
        }
    }

    private static IEnumerable<string> EnumerateOwnedArtefactPaths(ArtefactBuildResult artefact)
        => (artefact.Type is ArtefactType.Script or ArtefactType.Unpacked
                ? artefact.Modules.Select(static module => module.Path)
                    .Concat(artefact.CopiedItems.Select(static item => item.Destination))
                : new[] { artefact.OutputPath })
            .Concat(artefact.EvidencePaths);

    private static void ValidateOwnedArtefactFile(ModulePipelineRunState state, string path, bool signed)
    {
        if (!state.FinalizedPackedArtefactHashes.TryGetValue(path, out var expectedHash) ||
            !File.Exists(path) ||
            !string.Equals(ComputeFileSha256(path), expectedHash, StringComparison.OrdinalIgnoreCase) ||
            (state.FinalizedPackedArtefactUnixModes.TryGetValue(path, out int expectedMode) &&
             ReadFinalizedArtefactUnixMode(path) != expectedMode))
            ThrowFinalizedArtefactChanged(path, signed);
    }

    private static void ThrowFinalizedArtefactChanged(string path, bool signed)
        => throw new InvalidOperationException(
            $"The finalized artefact or its evidence changed after {(signed ? "signing" : "package validation")}: '{path}'. " +
            "Artifact actions must not mutate release outputs after finalization.");

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
            .Concat(artefact.Type is ArtefactType.Script or ArtefactType.Unpacked
                ? artefact.Modules
                    .Select(static module => module.Path)
                : Array.Empty<string>())
            .Concat(artefact.Type is ArtefactType.Script or ArtefactType.Unpacked
                ? artefact.CopiedItems.Select(static item => item.Destination)
                : Array.Empty<string>())
            .SelectMany(static path => Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                : new[] { path })
            .Concat(artefact.EvidencePaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer);

    private static IEnumerable<string> EnumerateFinalizedLooseArtefactRoots(ArtefactBuildResult artefact)
        => artefact.Type is ArtefactType.Script or ArtefactType.Unpacked
            ? new[] { artefact.OutputPath }
                .Concat(artefact.Modules
                    .Select(static module => module.Path))
                .Concat(artefact.CopiedItems.Where(static item => item.IsDirectory)
                    .Select(static item => item.Destination))
                .Where(Directory.Exists)
                .Select(Path.GetFullPath)
                .Distinct(PowerShellCompilationPathSafety.PathComparer)
            : Array.Empty<string>();
}
