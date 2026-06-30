using System.IO;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void ExecuteExternalAssets(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state)
    {
        if (plan.ExternalAssets is not { Length: > 0 })
            return;

        var service = new ExternalAssetPreparationService(_logger);
        foreach (var externalAsset in plan.ExternalAssets)
        {
            var step = session.GetExternalAssetStep(externalAsset);
            session.Start(step);
            try
            {
                state.ExternalAssetResults.Add(service.Prepare(plan.ProjectRoot, externalAsset));
                session.Done(step);
            }
            catch (Exception ex)
            {
                session.Fail(step, ex);
                throw;
            }
        }
    }

    private static void SyncExternalAssetsToStaging(ModulePipelinePlan plan, ModulePipelineRunState state)
    {
        if (state.ExternalAssetResults.Count == 0)
            return;

        var staged = state.RequireStaged();
        foreach (var result in state.ExternalAssetResults)
        {
            SyncExternalAssetDirectory(plan.ProjectRoot, staged.StagingPath, result.OutputPath);
            if (!IsSameOrChildExternalAssetPath(result.OutputPath, result.ManifestPath))
                SyncExternalAssetFile(plan.ProjectRoot, staged.StagingPath, result.ManifestPath);
        }
    }

    private static void SyncExternalAssetDirectory(string projectRoot, string stagingRoot, string sourceDirectory)
    {
        if (!IsSameOrChildExternalAssetPath(projectRoot, sourceDirectory))
            throw new InvalidOperationException($"External asset output path must be under the module project root to be staged: {sourceDirectory}");

        var relativePath = FrameworkCompatibility.GetRelativePath(projectRoot, sourceDirectory);
        var destinationDirectory = Path.Combine(stagingRoot, relativePath);
        CopyDirectory(sourceDirectory, destinationDirectory);
    }

    private static void SyncExternalAssetFile(string projectRoot, string stagingRoot, string sourceFile)
    {
        if (!IsSameOrChildExternalAssetPath(projectRoot, sourceFile))
            throw new InvalidOperationException($"External asset manifest path must be under the module project root to be staged: {sourceFile}");

        var relativePath = FrameworkCompatibility.GetRelativePath(projectRoot, sourceFile);
        var destinationFile = Path.Combine(stagingRoot, relativePath);
        var destinationDirectory = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);
        File.Copy(sourceFile, destinationFile, overwrite: true);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = FrameworkCompatibility.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = FrameworkCompatibility.GetRelativePath(sourceDirectory, file);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            var destinationParent = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationParent))
                Directory.CreateDirectory(destinationParent);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static bool IsSameOrChildExternalAssetPath(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var candidateWithSeparator = candidate + Path.DirectorySeparatorChar;
        return candidateWithSeparator.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
