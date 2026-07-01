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
            RemoveExternalAssetOutputFromStaging(plan.ProjectRoot, staged.StagingPath, result.OutputPath);

            foreach (var file in result.Files)
                SyncExternalAssetFile(plan.ProjectRoot, staged.StagingPath, file.FilePath);

            SyncExternalAssetFile(plan.ProjectRoot, staged.StagingPath, result.ManifestPath);
        }
    }

    private static void ValidateExternalAssets(ModulePipelinePlan plan)
    {
        if (plan.ExternalAssets is not { Length: > 0 })
            return;

        ExternalAssetPreparationService.ValidateOutputPathConflicts(plan.ProjectRoot, plan.ExternalAssets);
    }

    private static void RemoveExternalAssetOutputFromStaging(string projectRoot, string stagingRoot, string sourceOutputDirectory)
    {
        if (!IsSameOrChildExternalAssetPath(projectRoot, sourceOutputDirectory))
            throw new InvalidOperationException($"External asset output path must be under the module project root to be staged: {sourceOutputDirectory}");

        var relativePath = FrameworkCompatibility.GetRelativePath(projectRoot, sourceOutputDirectory);
        var stagingOutputDirectory = Path.Combine(stagingRoot, relativePath);
        if (!IsSameOrChildExternalAssetPath(stagingRoot, stagingOutputDirectory) ||
            string.Equals(
                Path.GetFullPath(stagingRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(stagingOutputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                FrameworkCompatibility.GetPathStringComparison(stagingRoot)))
        {
            throw new InvalidOperationException($"External asset output path cannot resolve to the staging root: {sourceOutputDirectory}");
        }

        if (Directory.Exists(stagingOutputDirectory))
            Directory.Delete(stagingOutputDirectory, recursive: true);
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

    private static bool IsSameOrChildExternalAssetPath(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = FrameworkCompatibility.GetPathStringComparison(root);
        if (string.Equals(root, candidate, comparison))
            return true;

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var candidateWithSeparator = candidate + Path.DirectorySeparatorChar;
        return candidateWithSeparator.StartsWith(rootWithSeparator, comparison);
    }
}
