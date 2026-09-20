using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleaseBuildExecutionService
{
    private static ProjectBuildConfigCheckpoint? CaptureProjectBuildConfigCheckpoint(
        RepositoryCatalogEntry repository)
    {
        if (string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath))
            return null;

        var configPath = RepositoryPlanPreviewService.ResolveProjectConfigPath(
            repository.ProjectBuildScriptPath,
            repository.RootPath);
        return string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)
            ? null
            : new ProjectBuildConfigCheckpoint(
                configPath,
                UnifiedReleaseConfigFingerprint.ComputeProjectBuildConfig(configPath));
    }

    private static void ValidateProjectBuildConfigCheckpoint(ProjectBuildConfigCheckpoint? checkpoint)
    {
        if (checkpoint is null)
            return;

        UnifiedReleaseConfigFingerprint.ValidateProjectBuildConfig(
            checkpoint.ConfigPath,
            checkpoint.Fingerprint);
    }

    private sealed record ProjectBuildConfigCheckpoint(string ConfigPath, string Fingerprint);
}
