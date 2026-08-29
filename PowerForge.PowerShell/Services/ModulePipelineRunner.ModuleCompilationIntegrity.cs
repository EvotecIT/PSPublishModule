using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static void CaptureFinalizedModulePayloadIntegrity(ModulePipelineRunState state)
    {
        state.FinalizedModulePayloadHashes.Clear();
        if (state.PowerShellCompilationResult is null) return;

        foreach (var path in state.RequireBuildResult().FinalizedPayloadFiles)
        {
            var fullPath = Path.GetFullPath(path);
            EnsureFinalizedModulePayloadFile(state.RequireBuildResult().StagingPath, fullPath);
            state.FinalizedModulePayloadHashes.Add(fullPath, ComputeModulePayloadSha256(fullPath));
        }
    }

    private static void ValidateFinalizedModulePayloadIntegrity(ModulePipelineRunState state)
    {
        if (state.PowerShellCompilationResult is null) return;
        if (state.FinalizedModulePayloadHashes.Count == 0)
            throw new InvalidOperationException("Compiled module payload integrity was not captured before delivery.");

        var currentPaths = state.RequireBuildResult().FinalizedPayloadFiles
            .Select(Path.GetFullPath)
            .OrderBy(static path => path, PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        var expectedPaths = state.FinalizedModulePayloadHashes.Keys
            .OrderBy(static path => path, PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        if (!currentPaths.SequenceEqual(expectedPaths, PowerShellCompilationPathSafety.PathComparer))
            throw new InvalidOperationException("Compiled module finalized payload paths changed after checkpoint finalization.");

        foreach (var path in expectedPaths)
        {
            EnsureFinalizedModulePayloadFile(state.RequireBuildResult().StagingPath, path);
            if (!string.Equals(
                    state.FinalizedModulePayloadHashes[path],
                    ComputeModulePayloadSha256(path),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Compiled module finalized payload changed after checkpoint finalization: '{path}'.");
            }
        }
    }

    private static void EnsureFinalizedModulePayloadFile(string stagingPath, string path)
    {
        var root = Path.GetFullPath(stagingPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        var comparison = FrameworkCompatibility.GetPathStringComparison(root);
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, comparison) || !File.Exists(fullPath))
            throw new InvalidOperationException($"Compiled module finalized payload file is missing or outside staging: '{fullPath}'.");
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Compiled module finalized payload does not permit symbolic links: '{fullPath}'.");
        var parent = new FileInfo(fullPath).Directory;
        while (parent is not null)
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Compiled module finalized payload does not permit junctions: '{fullPath}'.");
            if (string.Equals(Path.GetFullPath(parent.FullName), root, comparison)) break;
            parent = parent.Parent;
        }
    }

    private static string ComputeModulePayloadSha256(string path)
    {
        using var input = File.OpenRead(path);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", string.Empty);
    }
}
