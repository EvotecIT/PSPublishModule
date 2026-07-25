using PowerForge;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService
{
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
