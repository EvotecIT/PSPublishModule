using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Signing;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService
{
    private (ProjectBuildPublishHostConfiguration? Configuration, ReleasePublishReceipt? Failure)
        TryLoadCheckpointedProjectPublishConfiguration(
            PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
            ReleaseSigningExecutionResult signingResult,
            string configPath)
    {
        var buildResult = _checkpointSerializer.TryDeserialize<ReleaseBuildExecutionResult>(
            signingResult.SourceCheckpointStateJson);
        if (buildResult is null)
        {
            return (null, ProjectConfigFailure(
                repository,
                configPath,
                "Project build checkpoint is missing. Rebuild before publishing."));
        }

        if (!MatchesProjectBuildCheckpoint(configPath, buildResult.ProjectBuildConfigSha256))
        {
            return (null, ProjectConfigFailure(
                repository,
                configPath,
                "Project publication configuration changed after the build checkpoint. Rebuild and inspect the updated contract before publishing."));
        }

        ProjectBuildPublishHostConfiguration config;
        try
        {
            config = _projectBuildPublishHostService.LoadConfiguration(configPath);
        }
        catch (Exception)
        {
            return (null, ProjectConfigFailure(
                repository,
                configPath,
                "Project publication configuration could not be loaded. Correct it and rebuild before publishing."));
        }

        // The loaded object is immutable for this operation; a second check closes changes during parsing.
        return !MatchesProjectBuildCheckpoint(configPath, buildResult.ProjectBuildConfigSha256)
            ? (null, ProjectConfigFailure(
                repository,
                configPath,
                "Project publication configuration changed after the build checkpoint. Rebuild and inspect the updated contract before publishing."))
            : (config, null);
    }

    private static bool MatchesProjectBuildCheckpoint(string configPath, string? expectedSha256)
    {
        try
        {
            UnifiedReleaseConfigFingerprint.ValidateProjectBuildConfig(configPath, expectedSha256);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ReleasePublishReceipt ProjectConfigFailure(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        string configPath,
        string summary)
        => FailedReceipt(
            repository.RootPath,
            repository.Name,
            ReleaseBuildAdapterKind.ProjectBuild.ToString(),
            "Project publish",
            configPath,
            summary);
}
