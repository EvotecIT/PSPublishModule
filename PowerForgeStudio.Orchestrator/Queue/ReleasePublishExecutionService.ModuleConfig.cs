using PowerForge;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService
{
    private void ValidateModulePublishCheckpoint(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult)
    {
        var unifiedConfigPath = repository.UnifiedReleaseConfigPath;
        var directJsonConfigPath =
            string.Equals(
                Path.GetExtension(repository.ModuleBuildScriptPath),
                ".json",
                StringComparison.OrdinalIgnoreCase)
                ? repository.ModuleBuildScriptPath
                : null;
        if (string.IsNullOrWhiteSpace(unifiedConfigPath) &&
            string.IsNullOrWhiteSpace(directJsonConfigPath))
        {
            return;
        }

        var buildResult = _checkpointSerializer.TryDeserialize<ReleaseBuildExecutionResult>(
            signingResult.SourceCheckpointStateJson);
        if (buildResult is null)
            throw new InvalidOperationException("The signed module build checkpoint is missing. Rebuild before publishing.");

        if (!string.IsNullOrWhiteSpace(unifiedConfigPath))
        {
            UnifiedReleaseConfigFingerprint.Validate(
                unifiedConfigPath!,
                buildResult.UnifiedReleaseConfigSha256);
            return;
        }

        UnifiedReleaseConfigFingerprint.ValidateModuleConfig(
            directJsonConfigPath!,
            buildResult.ModuleBuildConfigSha256);
    }

    private async Task<ModulePublishConfigurationSet> ExportModulePublishConfigsAsync(
        string repositoryRoot,
        string buildInputPath,
        CancellationToken cancellationToken)
    {
        if (string.Equals(Path.GetExtension(buildInputPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            var context = new ModulePipelineConfigurationService().Load(buildInputPath);
            return new ModulePublishConfigurationSet(
                new ModulePublishConfigurationReader().Read(context.Spec),
                context);
        }

        var repositoryName = Path.GetFileName(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var exportPath = PowerForgeStudioHostPaths.GetRuntimeFilePath(repositoryName, "module-publish", "powerforge.publish.json");
        var execution = await _moduleBuildHostService.ExportPipelineJsonAsync(new ModuleBuildHostExportRequest {
            RepositoryRoot = repositoryRoot,
            ScriptPath = buildInputPath,
            ModulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath(),
            OutputPath = exportPath
        }, cancellationToken);
        if (execution.ExitCode != 0 || !File.Exists(exportPath))
        {
            throw new InvalidOperationException(
                $"Module publish configuration export failed for '{buildInputPath}' (exit {execution.ExitCode}).");
        }

        var configurations = new ModulePublishConfigurationReader().Read(exportPath);
        var exportedPipelineContext = new ModulePipelineConfigurationService().TryLoad(exportPath, out var exportedContext)
            ? exportedContext
            : null;
        return new ModulePublishConfigurationSet(configurations, exportedPipelineContext);
    }

    private sealed record ModulePublishConfigurationSet(
        IReadOnlyList<PublishConfiguration> Configurations,
        ModulePipelineConfigurationContext? Context);
}
